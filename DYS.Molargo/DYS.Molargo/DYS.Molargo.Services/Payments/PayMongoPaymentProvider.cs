using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DYS.Molargo.Services.Payments;

/// <summary>
/// PayMongo, which is how the vendor collects in the Philippines.
/// </summary>
/// <remarks>
/// <para>
/// Chosen because Stripe does not onboard Philippine businesses, and because card
/// ownership in the Philippines is low enough that a card-only checkout would fail most
/// practices. PayMongo carries GCash, Maya, QR Ph and online banking beside cards, and
/// settles in PHP to a local account.
/// </para>
/// <para>
/// A hosted Checkout Session, not a card form. No card number, CVC or expiry ever reaches
/// these servers, which keeps the whole app outside PCI scope — the same reason the
/// practice-facing side of the app has never taken a card either.
/// </para>
/// </remarks>
public sealed class PayMongoPaymentProvider : IPaymentProvider
{
    public const string ProviderName = "PayMongo";

    /// <summary>
    /// One client for the process.
    /// </summary>
    /// <remarks>
    /// Static, for the reason <c>SmsSender</c> records: a client per request exhausts the
    /// socket pool under load. The timeout rides on a linked token instead, so the client
    /// itself stays shareable.
    /// </remarks>
    private static readonly HttpClient Client = new();

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The methods offered at checkout.
    /// </summary>
    /// <remarks>
    /// GCash first, and that order is not cosmetic: it is how most Filipinos pay, and a
    /// checkout that leads with a card reads as "not for me" to somebody who does not have
    /// one. Cards are included because this bills businesses, and a dental practice is more
    /// likely to hold one than a consumer.
    ///
    /// A method the merchant's account has not been approved for is refused by PayMongo
    /// when the session is created, which surfaces as a failed charge the vendor can read —
    /// better than a checkout page offering something that then does not work.
    /// </remarks>
    private static readonly string[] Methods = ["gcash", "paymaya", "card", "grab_pay"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => ProviderName;

    public async Task<CheckoutLink> CreateCheckoutAsync(
        PaymentCredentials credentials,
        decimal amount,
        string description,
        string reference,
        string? payerEmail,
        CancellationToken ct = default)
    {
        // Centavos, not pesos. PayMongo takes the smallest unit as an integer, and sending
        // 1500.00 where it expects 150000 charges a practice one per cent of its bill —
        // which reconciles as "they paid, nearly" and is found at the end of a month.
        var minor = (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

        if (minor < 100)
        {
            // PayMongo's own floor is ₱1.00. Refused here rather than sent, so the message
            // names the amount instead of quoting an API error about a field.
            return CheckoutLink.Failed(
                $"PayMongo will not take {amount:0.00} — the minimum is 1.00.");
        }

        var body = new
        {
            data = new
            {
                attributes = new
                {
                    line_items = new[]
                    {
                        new
                        {
                            currency = credentials.CurrencyCode,
                            amount = minor,
                            name = description,
                            quantity = 1,
                        },
                    },
                    payment_method_types = Methods,
                    description,

                    // Echoed back on the webhook, which is how a payment is matched to the
                    // charge that raised it. Without it the only link is PayMongo's own id,
                    // and that is lost if the request times out before its answer is saved.
                    reference_number = reference,
                    send_email_receipt = !string.IsNullOrWhiteSpace(payerEmail),
                    show_description = true,
                    show_line_items = true,
                },
            },
        };

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);

            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"{credentials.ApiUrl.TrimEnd('/')}/checkout_sessions")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json"),
            };

            // HTTP Basic with the secret key as the username and no password — the colon
            // with nothing after it is what says so, and leaving it off is rejected.
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.SecretKey}:")));

            using var response = await Client
                .SendAsync(request, timeout.Token)
                .ConfigureAwait(false);

            var text = await response.Content
                .ReadAsStringAsync(timeout.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return CheckoutLink.Failed(
                    $"PayMongo refused the request ({(int)response.StatusCode}): {Detail(text)}");
            }

            using var document = JsonDocument.Parse(text);
            var data = document.RootElement.GetProperty("data");

            var id = data.GetProperty("id").GetString();
            var url = data.GetProperty("attributes").GetProperty("checkout_url").GetString();

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url))
            {
                // Answered 200 with something unrecognised. Treated as a failure rather
                // than stored, because a charge holding half an answer is one nobody can
                // settle and nobody can retry.
                return CheckoutLink.Failed(
                    "PayMongo accepted the request but did not return a checkout url.");
            }

            return new CheckoutLink(url, id, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || ct.IsCancellationRequested)
        {
            return CheckoutLink.Failed(Trim(ex.Message));
        }
        catch (OperationCanceledException)
        {
            // The linked token fired, not the caller's. That is this provider timing out.
            return CheckoutLink.Failed(
                $"PayMongo did not answer within {Timeout.TotalSeconds:0} seconds.");
        }
    }

    public WebhookVerdict Interpret(
        PaymentCredentials credentials, string body, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(credentials.WebhookSecret))
        {
            // Refused rather than trusted. An unverified webhook that is believed is an
            // open instruction to mark any charge paid, so a gateway with no signing
            // secret accepts nothing at all.
            return WebhookVerdict.Rejected(
                "No webhook signing secret is set for this country, so nothing can be "
                + "verified. Set it on the payment gateway before pointing PayMongo here.");
        }

        if (!Verify(credentials, body, signatureHeader, out var why))
        {
            return WebhookVerdict.Rejected(why);
        }

        try
        {
            using var document = JsonDocument.Parse(body);

            var attributes = document.RootElement
                .GetProperty("data")
                .GetProperty("attributes");

            var type = attributes.GetProperty("type").GetString() ?? string.Empty;

            // The event's own payload, which is the checkout session or the payment.
            var inner = attributes.TryGetProperty("data", out var held)
                ? held
                : default;

            var reference = inner.ValueKind == JsonValueKind.Object
                && inner.TryGetProperty("id", out var id)
                    ? id.GetString()
                    : null;

            // Only the two that change a charge. Everything else is acknowledged so
            // PayMongo stops redelivering it — a webhook left unanswered is retried for
            // hours and fills the log with events this app was never going to act on.
            return type switch
            {
                "checkout_session.payment.paid" or "payment.paid" =>
                    new WebhookVerdict(true, reference, PaymentOutcome.Paid, null),

                "payment.failed" =>
                    new WebhookVerdict(true, reference, PaymentOutcome.Failed, Reason(inner)),

                _ => WebhookVerdict.Uninteresting(),
            };
        }
        catch (JsonException ex)
        {
            return WebhookVerdict.Rejected($"The webhook body could not be read: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks the signature PayMongo sent against one computed here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The header is <c>t=&lt;unix seconds&gt;,te=&lt;test&gt;,li=&lt;live&gt;</c>, and the
    /// signature is HMAC-SHA256 over <c>{t}.{body}</c> keyed with the endpoint's signing
    /// secret. Test and live are separate fields in the same header, which is why the row
    /// records which it is rather than guessing from the key.
    /// </para>
    /// <para>
    /// The timestamp is checked as well as the signature. Without it a valid webhook
    /// captured once can be replayed forever, and replaying a <c>paid</c> is a charge
    /// settled a second time.
    /// </para>
    /// <para>
    /// Compared in fixed time. A plain string comparison returns sooner for a wrong first
    /// character, which over enough attempts tells somebody the signature a byte at a time.
    /// </para>
    /// </remarks>
    private static bool Verify(
        PaymentCredentials credentials, string body, string? header, out string why)
    {
        why = string.Empty;

        if (string.IsNullOrWhiteSpace(header))
        {
            why = "The webhook carried no signature header.";
            return false;
        }

        string? stamp = null;
        string? signature = null;
        var wanted = credentials.IsLive ? "li" : "te";

        foreach (var part in header.Split(',', StringSplitOptions.TrimEntries))
        {
            var split = part.IndexOf('=');
            if (split <= 0) continue;

            var key = part[..split];
            var value = part[(split + 1)..];

            if (key == "t") stamp = value;
            else if (key == wanted) signature = value;
        }

        if (stamp is null || signature is null)
        {
            why = $"The signature header has no 't' or no '{wanted}' part. A live gateway "
                + "reads 'li' and a test one reads 'te' — check which this row is set to.";
            return false;
        }

        if (!long.TryParse(stamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            why = "The signature timestamp is not a number.";
            return false;
        }

        var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(seconds);

        if (age > TimeSpan.FromMinutes(5) || age < TimeSpan.FromMinutes(-5))
        {
            // Both directions. Ahead of us by more than the tolerance is a clock that
            // disagrees, and accepting it would widen the replay window at the far end.
            why = $"The webhook is {age.TotalMinutes:0} minutes out of date. Either it is a "
                + "replay, or this server's clock is wrong.";
            return false;
        }

        var computed = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(credentials.WebhookSecret),
            Encoding.UTF8.GetBytes($"{stamp}.{body}")));

        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed), Encoding.UTF8.GetBytes(signature)))
        {
            why = "The webhook signature does not match. Either the signing secret here is "
                + "not the one PayMongo issued for this endpoint, or the body was altered "
                + "in transit.";
            return false;
        }

        return true;
    }

    /// <summary>PayMongo's own words about a failure, where it gave any.</summary>
    private static string? Reason(JsonElement inner)
    {
        if (inner.ValueKind != JsonValueKind.Object) return null;

        if (!inner.TryGetProperty("attributes", out var attributes)) return null;

        return attributes.TryGetProperty("last_payment_error", out var error)
            ? Trim(error.ToString())
            : null;
    }

    /// <summary>
    /// The useful part of an error body.
    /// </summary>
    /// <remarks>
    /// PayMongo answers a refusal with a JSON errors array. The whole body on a charge's
    /// failure reason makes the billing screen unreadable, so this takes the first detail
    /// and falls back to the raw text when the shape is not what was expected.
    /// </remarks>
    private static string Detail(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array
                && errors.GetArrayLength() > 0
                && errors[0].TryGetProperty("detail", out var detail))
            {
                return Trim(detail.GetString() ?? body);
            }
        }
        catch (JsonException)
        {
            // Not JSON. The raw text is still the most useful thing available.
        }

        return Trim(body);
    }

    private static string Trim(string value) =>
        value.Length <= 400 ? value : value[..400];
}
