namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// The payment provider the vendor takes subscription money through, for one country.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <see cref="SmsGateway"/> and for the same reason: a payment provider
/// is a per-country arrangement. Who will onboard the vendor as a merchant, which methods
/// a customer expects to see, which currency settles and which regulator is involved all
/// change at the border. One provider everywhere is not an option that exists — Stripe
/// does not onboard Philippine businesses at all, and a provider that does will not be the
/// right one for Australia.
/// </para>
/// <para>
/// So the country is the identity of the row, enforced by the database. Two rows for PH
/// would be two answers to "where does this practice pay", and whichever the query read
/// first would become the answer.
/// </para>
/// <para>
/// One deliberate difference from the SMS gateway, and it is the interesting one. There,
/// the request itself is data — <c>PayloadTemplate</c> and <c>Headers</c> are editable,
/// because sending a text is one POST of a flat body and a new provider is a configuration
/// change. A payment provider is not that: creating a checkout session, verifying a signed
/// webhook and reconciling a refund are three protocols that differ in structure, not just
/// in field names. Templating them would produce a configuration language nobody could get
/// right under pressure.
/// </para>
/// <para>
/// So this row carries the <em>routing and the credentials</em>, and
/// <see cref="ProviderName"/> selects an implementation in code. Adding PayMongo to a new
/// country is a row; adding a provider nobody has written yet is a class.
/// </para>
/// </remarks>
public sealed class PaymentGateway : EntityBase
{
    /// <summary>
    /// The two-letter country this gateway serves — "PH", "AU", "NZ".
    /// </summary>
    /// <remarks>
    /// Matched against the clinic's <see cref="Tenant.CountryCode"/>, which is its billing
    /// country rather than where a surgery stands. That is the right key here for a reason
    /// stronger than it is for SMS: the billing country is the one the invoice is raised
    /// in, the one whose tax applies, and the one the money settles in.
    /// </remarks>
    public string CountryCode { get; set; } = string.Empty;

    /// <summary>
    /// Which implementation handles this country — "PayMongo" today.
    /// </summary>
    /// <remarks>
    /// Matched case-insensitively against the registered <c>IPaymentProvider</c>s. A name
    /// with nothing behind it is refused when the row is saved rather than at the moment
    /// somebody tries to pay, because the second is a practice staring at a failed
    /// checkout and the first is a vendor typing into an admin screen.
    /// </remarks>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// The provider's API base — <c>https://api.paymongo.com/v1</c>.
    /// </summary>
    /// <remarks>
    /// Configuration rather than a constant in the provider, so a sandbox and a production
    /// endpoint are a row apart, and so a provider that moves a version prefix does not
    /// need a release. https only, enforced on save: this request carries a key that can
    /// move money.
    /// </remarks>
    public string ApiUrl { get; set; } = string.Empty;

    /// <summary>
    /// The secret key, write-only from the vendor's screen.
    /// </summary>
    /// <remarks>
    /// In the platform database in the clear, for the same bad reason as the SMS key and
    /// the mail password: there is no secret store wired up. This one is worse than both.
    /// It does not send a message — it takes money, and it is the vendor's own merchant
    /// credential for every practice in the country.
    ///
    /// Masked on screen and never rendered back, which stops it being read over a shoulder
    /// and nothing more. The fix, when there is somewhere to put it: a reference here and
    /// the value in the host's own store.
    /// </remarks>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// The webhook signing secret, which is <em>not</em> the API key.
    /// </summary>
    /// <remarks>
    /// Its own field because providers issue it separately — PayMongo mints one per webhook
    /// endpoint, independent of the secret key. Conflating the two is a common mistake and
    /// an expensive one: every webhook would fail verification, which looks exactly like
    /// nobody paying.
    ///
    /// Empty means the endpoint refuses every webhook rather than trusting it. An unsigned
    /// webhook that is believed is an open instruction to mark any charge paid.
    /// </remarks>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>The currency this gateway settles in. "PHP" for the Philippines.</summary>
    /// <remarks>
    /// Checked against the plan's currency when a charge is raised. A PHP gateway and an
    /// AUD plan is a figure converted by somebody's bank at a rate nobody chose, and the
    /// practice is charged an amount the invoice does not state.
    /// </remarks>
    public string CurrencyCode { get; set; } = "PHP";

    /// <summary>
    /// What the provider keeps, as a percentage of the amount.
    /// </summary>
    /// <remarks>
    /// Recorded so the vendor's own billing screen can show what a charge actually
    /// returned. Not used to compute anything charged to a practice — the practice pays
    /// the invoice, and the provider's cut is the vendor's cost of collecting it.
    /// </remarks>
    public decimal PercentageFee { get; set; }

    /// <summary>What the provider keeps per transaction, on top of the percentage.</summary>
    public decimal FixedFee { get; set; }

    /// <summary>
    /// Whether this row's keys are live ones.
    /// </summary>
    /// <remarks>
    /// Stored rather than inferred from the key's prefix. Every provider spells its test
    /// keys differently and a prefix check is a guess that eventually gets it wrong in the
    /// direction that matters — treating a live key as a test one and taking real money
    /// from a practice during a trial run.
    ///
    /// It drives what the screen says, not what the request does. The key itself decides
    /// which environment the provider uses.
    /// </remarks>
    public bool IsLive { get; set; }

    /// <summary>
    /// Whether this gateway is in use.
    /// </summary>
    /// <remarks>
    /// Switched off rather than deleted when a provider is being changed over: the charges
    /// already raised through it hold its reference, and a deleted row makes those
    /// unreadable.
    /// </remarks>
    public bool IsActive { get; set; } = true;

    /// <summary>True when there is enough here to attempt a payment.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiUrl)
        && !string.IsNullOrWhiteSpace(SecretKey)
        && !string.IsNullOrWhiteSpace(ProviderName);
}
