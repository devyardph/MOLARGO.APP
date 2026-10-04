namespace DYS.Molargo.Services.Payments;

/// <summary>
/// Everything one provider needs to take a payment in one country.
/// </summary>
/// <remarks>
/// Carries the secret, unlike the row the vendor's screen reads. This type never reaches a
/// page — it exists so a provider can be handed everything it needs in one object, built
/// inside the resolver rather than returned to a view model.
/// </remarks>
public sealed record PaymentCredentials(
    string CountryCode,
    string ProviderName,
    string ApiUrl,
    string SecretKey,
    string WebhookSecret,
    string CurrencyCode,
    bool IsLive);

/// <summary>What a provider answered when asked for somewhere to pay.</summary>
/// <param name="CheckoutUrl">Where to send the payer. Null when the request failed.</param>
/// <param name="Reference">
/// The provider's own id for this attempt, recorded on the charge. It is what a webhook
/// arriving later is matched against, so a charge without one cannot be settled
/// automatically.
/// </param>
/// <param name="Detail">Why it failed, in words a person can act on. Null on success.</param>
public sealed record CheckoutLink(string? CheckoutUrl, string? Reference, string? Detail)
{
    public bool Succeeded => CheckoutUrl is { Length: > 0 } && Reference is { Length: > 0 };

    public static CheckoutLink Failed(string detail) => new(null, null, detail);
}

/// <summary>What a provider made of a webhook that arrived.</summary>
/// <param name="Verified">
/// Whether the signature checked out. False means the body is not to be believed at all —
/// not merely that it was uninteresting.
/// </param>
/// <param name="Reference">The provider's id for the attempt this concerns.</param>
/// <param name="Outcome">What happened to it.</param>
/// <param name="Detail">The provider's own words, kept for the failure reason.</param>
public sealed record WebhookVerdict(
    bool Verified,
    string? Reference,
    PaymentOutcome Outcome,
    string? Detail)
{
    public static WebhookVerdict Rejected(string detail) =>
        new(false, null, PaymentOutcome.Ignored, detail);

    /// <summary>Verified, and about nothing this app acts on.</summary>
    public static WebhookVerdict Uninteresting() =>
        new(true, null, PaymentOutcome.Ignored, null);
}

/// <summary>What a webhook says became of a payment.</summary>
public enum PaymentOutcome
{
    /// <summary>Not an event this app acts on. Acknowledged so it is not redelivered.</summary>
    Ignored = 0,

    /// <summary>The money arrived.</summary>
    Paid = 1,

    /// <summary>The attempt failed. The charge stays due.</summary>
    Failed = 2,
}

/// <summary>
/// One payment provider's protocol.
/// </summary>
/// <remarks>
/// <para>
/// Narrow on purpose. Everything the app needs of a provider is "give me somewhere to send
/// this practice to pay" and "tell me what this webhook means" — so those are the two
/// methods, and a second provider is a class implementing them rather than a change
/// anywhere above.
/// </para>
/// <para>
/// Credentials are passed in rather than injected, because which ones apply depends on the
/// country of the practice being billed. One provider instance serves every country it is
/// configured for; the row decides the keys.
/// </para>
/// <para>
/// Neither method throws for an ordinary failure. A provider that is down, a key that has
/// been revoked and a webhook that does not verify are all things to record and show
/// somebody, not exceptions in the middle of a billing run that would abandon the
/// practices after this one.
/// </para>
/// </remarks>
public interface IPaymentProvider
{
    /// <summary>The <c>ProviderName</c> this handles, matched case-insensitively.</summary>
    string Name { get; }

    /// <summary>
    /// Asks the provider for somewhere to send the payer.
    /// </summary>
    /// <param name="credentials">The country's row, keys included.</param>
    /// <param name="amount">The amount in <paramref name="credentials"/>'s currency.</param>
    /// <param name="description">What the payer sees they are paying for.</param>
    /// <param name="reference">
    /// The app's own id for the charge, sent so the provider echoes it back on the webhook.
    /// Matching on it rather than on the provider's id alone is what makes a webhook
    /// traceable to a charge when a request timed out before its answer was recorded.
    /// </param>
    /// <param name="payerEmail">Where the provider sends its own receipt, when it has one.</param>
    Task<CheckoutLink> CreateCheckoutAsync(
        PaymentCredentials credentials,
        decimal amount,
        string description,
        string reference,
        string? payerEmail,
        CancellationToken ct = default);

    /// <summary>
    /// Verifies a webhook and says what it means.
    /// </summary>
    /// <param name="credentials">The country's row, for the signing secret.</param>
    /// <param name="body">The raw request body, exactly as received.</param>
    /// <param name="signatureHeader">The provider's signature header, verbatim.</param>
    /// <remarks>
    /// The body must be the bytes as they arrived, not a re-serialised object. A signature
    /// covers the exact text, so anything that re-orders a key or changes whitespace makes
    /// every webhook fail verification — which reads as nobody paying.
    /// </remarks>
    WebhookVerdict Interpret(
        PaymentCredentials credentials, string body, string? signatureHeader);
}
