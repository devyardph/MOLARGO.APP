using System.Net.Http.Headers;
using System.Text;
using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Shared.Services;

/// <summary>What came of trying to send one text.</summary>
/// <param name="Detail">
/// The provider's own words on failure. Kept verbatim, like the SMTP sender's: a carrier
/// names the cause exactly — an unregistered sender id, a number not in its coverage, a
/// spent balance — and paraphrasing loses the only part worth reading.
/// </param>
/// <param name="Number">
/// The number actually dialled, in international form. Not the same string the record
/// holds, and worth reporting: "sent to +61400123456" is how somebody checks that
/// <c>0400 123 456</c> was read as the number they meant.
/// </param>
public sealed record SmsResult(bool Succeeded, string Detail, string? Number = null)
{
    public static SmsResult Ok(string detail, string number) => new(true, detail, number);

    public static SmsResult Failed(string detail, string? number = null) =>
        new(false, detail, number);
}

/// <summary>
/// Sends one text message through the vendor's gateway for the clinic's country.
/// </summary>
/// <remarks>
/// <para>
/// The one place a text is transmitted, so every caller — a booking confirmation, a recall,
/// a balance reminder — gets the same number handling, the same timeout and the same
/// account. A caller supplies a number and a sentence and nothing else: which provider
/// carries it, what the request looks like and what it costs are all the gateway's, and
/// none of that should be repeated at a call site.
/// </para>
/// <para>
/// It does not write to the communication log. That belongs to the caller, which knows
/// what the message was about and which patient it concerned — and the log has to be
/// written for a failure too, which a sender that logged its own successes would make
/// somebody remember twice.
/// </para>
/// </remarks>
public interface ISmsSender
{
    /// <summary>
    /// Sends to one number. Never throws.
    /// </summary>
    /// <param name="toNumber">
    /// As it is stored on the record. Converted to international form here — see
    /// <see cref="PhoneNumber"/>.
    /// </param>
    /// <param name="purpose">
    /// What the message was for, in the words the audit trail should use — "Appointment
    /// reminder". Optional, and worth passing wherever it is known.
    /// </param>
    /// <param name="patientId">
    /// Set where the text was about a patient, so it lands on their access history beside
    /// everything else that touched the record.
    /// </param>
    Task<SmsResult> SendAsync(
        string? toNumber,
        string message,
        CancellationToken ct = default,
        string? purpose = null,
        Guid? patientId = null);

    /// <summary>
    /// Sends through a gateway handed in, rather than the one this clinic resolves to.
    /// </summary>
    /// <remarks>
    /// For the vendor testing a country's gateway from the platform screen, where the point
    /// is to exercise a specific row — usually one for a country the person testing is not
    /// in. Everything after the gateway is chosen is identical, which is why this is an
    /// entry point here rather than a second sender: the number handling, the timeout and
    /// the reading of the provider's answer are the parts worth having one copy of.
    /// </remarks>
    Task<SmsResult> SendViaAsync(
        SmsCredentials credentials,
        string? toNumber,
        string message,
        CancellationToken ct = default,
        string? purpose = null,
        Guid? patientId = null);
}

/// <inheritdoc cref="ISmsSender"/>
public sealed class SmsSender : ISmsSender
{
    /// <summary>
    /// How long to wait on the provider before giving up.
    /// </summary>
    /// <remarks>
    /// Twenty seconds, matching the mail sender. Long enough for a carrier's API on a
    /// clinic's connection, short enough that an endpoint that accepts the connection and
    /// never answers reports back instead of holding the screen on a saved booking.
    /// </remarks>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How much of the provider's answer is kept.
    /// </summary>
    /// <remarks>
    /// Enough for the message inside a JSON error and no more. Some providers answer a
    /// failure with an HTML page, and the whole of one in the failure column makes the
    /// comms log unreadable for every other row.
    /// </remarks>
    private const int MaxDetail = 400;

    /// <summary>
    /// One client for the process.
    /// </summary>
    /// <remarks>
    /// Static, because a client per send exhausts the socket pool under any load — the
    /// classic HttpClient mistake. The timeout is applied per request through a linked
    /// token rather than on the client, so the client itself stays shareable.
    /// </remarks>
    private static readonly HttpClient Client = new();

    private readonly ISmsGatewayResolver _gateways;
    private readonly IAuditLog _audit;

    public SmsSender(ISmsGatewayResolver gateways, IAuditLog audit)
    {
        _gateways = gateways;
        _audit = audit;
    }

    public async Task<SmsResult> SendAsync(
        string? toNumber,
        string message,
        CancellationToken ct = default,
        string? purpose = null,
        Guid? patientId = null)
    {
        var credentials = await _gateways.ForCurrentTenantAsync(ct).ConfigureAwait(false);

        if (credentials is null)
        {
            // Audited here rather than left to the send, which never happens. A country
            // with no gateway is the commonest reason a reminder does not arrive, and it is
            // invisible from everywhere except this line.
            var missing = SmsResult.Failed(
                "No SMS provider is set up for this practice's country, or the one set up "
                    + "is switched off.");

            await RecordAsync(missing, toNumber, purpose, patientId).ConfigureAwait(false);

            return missing;
        }

        return await SendViaAsync(credentials, toNumber, message, ct, purpose, patientId)
            .ConfigureAwait(false);
    }

    /// <remarks>
    /// The audit write wraps the send, for the reason on the mail sender's: every exit is a
    /// fact worth recording, and one wrapper cannot miss the branch somebody adds next.
    /// </remarks>
    public async Task<SmsResult> SendViaAsync(
        SmsCredentials credentials,
        string? toNumber,
        string message,
        CancellationToken ct = default,
        string? purpose = null,
        Guid? patientId = null)
    {
        var result = await TrySendAsync(credentials, toNumber, message, ct)
            .ConfigureAwait(false);

        await RecordAsync(result, toNumber, purpose, patientId).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Records what happened, without ever being the reason a send reports failure.
    /// </summary>
    /// <remarks>
    /// Swallowed, like the mail sender's. The text has already gone by the time this runs,
    /// and an audit row that cannot be written must not turn a delivered reminder into an
    /// error somebody reads as "not sent".
    /// </remarks>
    private async Task RecordAsync(
        SmsResult result, string? toNumber, string? purpose, Guid? patientId)
    {
        var what = string.IsNullOrWhiteSpace(purpose) ? "Text" : $"{purpose} text";

        // The number actually dialled where there is one, because that is the value worth
        // checking later — "0400 123 456" read as +61400123456 is the thing somebody wants
        // to confirm, and on a failure it may never have been derived at all.
        var to = result.Number ?? (string.IsNullOrWhiteSpace(toNumber)
            ? "no number"
            : toNumber.Trim());

        var detail = result.Succeeded
            ? $"{what} sent to {to} — {result.Detail}"
            : $"{what} to {to} failed — {result.Detail}";

        try
        {
            await _audit.RecordAsync(
                result.Succeeded
                    ? AuditAction.NotificationSent
                    : AuditAction.NotificationFailed,
                nameof(SmsGateway),
                entityId: null,
                detail,
                patientId)
                .ConfigureAwait(false);
        }
        catch
        {
            // Nothing to escalate to. The caller is told what the send did.
        }
    }

    private static async Task<SmsResult> TrySendAsync(
        SmsCredentials credentials,
        string? toNumber,
        string message,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return SmsResult.Failed("There is no message to send.");
        }

        var number = PhoneNumber.ToInternational(toNumber, credentials.CountryCode);

        if (!number.Ok) return SmsResult.Failed(number.Problem!);

        var request = credentials.Build(number.Number!, message);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);

            using var post = new HttpRequestMessage(HttpMethod.Post, request.Url)
            {
                Content = new StringContent(request.Body, Encoding.UTF8),
            };

            // Set on the content rather than passed to StringContent, which would append
            // "; charset=utf-8" to whatever the gateway's content type is. Some providers
            // match the header exactly and refuse the request over the suffix.
            post.Content.Headers.ContentType = new MediaTypeHeaderValue(request.ContentType)
            {
                CharSet = "utf-8",
            };

            foreach (var (name, value) in request.Headers)
            {
                // Content headers and request headers are different collections, and adding
                // one to the other throws. Tried on the request first because that is where
                // authentication goes, which is most of what a gateway carries.
                if (!post.Headers.TryAddWithoutValidation(name, value))
                {
                    post.Content.Headers.TryAddWithoutValidation(name, value);
                }
            }

            using var response = await Client
                .SendAsync(post, HttpCompletionOption.ResponseContentRead, timeout.Token)
                .ConfigureAwait(false);

            var answer = await response.Content
                .ReadAsStringAsync(timeout.Token)
                .ConfigureAwait(false);

            answer = Trim(answer);

            if (response.IsSuccessStatusCode)
            {
                return SmsResult.Ok(
                    answer.Length > 0 ? answer : $"Accepted ({(int)response.StatusCode}).",
                    number.Number!);
            }

            // The status code as well as the body, because a provider that answers 401 with
            // an empty body still tells you the key is wrong.
            return SmsResult.Failed(
                $"{(int)response.StatusCode} {response.ReasonPhrase}"
                    + (answer.Length > 0 ? " — " + answer : string.Empty),
                number.Number);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Our own timeout, not the caller's cancellation. Told apart because they
            // arrive as the same exception type, and reporting a cancelled screen as a
            // provider timeout would send somebody looking at the carrier.
            return SmsResult.Failed(
                $"The provider did not answer within {Timeout.TotalSeconds:0} seconds.",
                number.Number);
        }
        catch (Exception error)
        {
            // Caught, like the mail sender's. Whatever this is called from has usually
            // already written something — a booking, an invoice — and a carrier being
            // unreachable must not turn that into an error somebody reads as "not saved".
            return SmsResult.Failed(Trim(error.Message), number.Number);
        }
    }

    private static string Trim(string? text)
    {
        var flat = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();

        while (flat.Contains("  ", StringComparison.Ordinal))
        {
            flat = flat.Replace("  ", " ", StringComparison.Ordinal);
        }

        return flat.Length <= MaxDetail ? flat : flat[..MaxDetail] + "…";
    }
}
