using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// The SMS provider the vendor uses for one country.
/// </summary>
/// <remarks>
/// <para>
/// Per country and held by the vendor, unlike <see cref="NotificationSettings"/> which is
/// per clinic. Mail is the practice's own account — messages go out under their address,
/// and they are the ones who pay for it. SMS is not: a sender id has to be registered with
/// a carrier country by country, the rules differ per country, and no single clinic is
/// going to do that. So the vendor holds one gateway per country and every practice in it
/// sends through that.
/// </para>
/// <para>
/// Which makes the country the identity of the row — one gateway per country, enforced by
/// the database. Two rows for AU would be two answers to "who sends this clinic's texts",
/// and whichever the query read first would become the answer.
/// </para>
/// </remarks>
public sealed class SmsGateway : EntityBase
{
    /// <summary>
    /// The two-letter country this gateway serves — "AU", "PH", "NZ".
    /// </summary>
    /// <remarks>
    /// Matched against the clinic's <see cref="Tenant.CountryCode"/>, which is the billing
    /// country rather than where a surgery stands. Deliberate: a group registered in one
    /// country sends under one sender id, which is the registration the carrier actually
    /// checks.
    /// </remarks>
    public string CountryCode { get; set; } = string.Empty;

    /// <summary>Who the provider is, for the vendor's own records — "Twilio", "MessageBird".</summary>
    /// <remarks>
    /// A label, not a switch. Nothing branches on it: the request is shaped by
    /// <see cref="ApiUrl"/>, <see cref="PayloadTemplate"/> and <see cref="Headers"/>, so
    /// adding a provider is a row rather than a code change. It exists so somebody reading
    /// the screen knows whose bill to check when messages stop.
    /// </remarks>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>Where the send is posted.</summary>
    /// <remarks>
    /// Stored per country rather than derived from the provider name, because the same
    /// provider gives different endpoints per region, and a country can be moved to another
    /// provider without anything being deployed.
    /// </remarks>
    public string ApiUrl { get; set; } = string.Empty;

    /// <summary>
    /// Which authentication shape the provider wants.
    /// </summary>
    /// <remarks>
    /// The one part of the request that is not free text, and deliberately so — see
    /// <see cref="SmsAuthType"/>. It decides which of the fields below are read:
    /// <see cref="AuthHeaderName"/> for an API key, <see cref="AuthUsername"/> for Basic.
    /// The credential itself is <see cref="ApiKey"/> either way.
    /// </remarks>
    public SmsAuthType AuthType { get; set; } = SmsAuthType.ApiKey;

    /// <summary>
    /// The header an API key is sent in — <c>X-API-Key</c>, <c>Authorization</c>.
    /// </summary>
    /// <remarks>
    /// Read only under <see cref="SmsAuthType.ApiKey"/>. Basic is <c>Authorization</c> by
    /// definition, and a typed name there would let a correctly built credential be sent in
    /// a header the provider never looks at.
    /// </remarks>
    public string AuthHeaderName { get; set; } = SmsAuth.AuthorizationHeader;

    /// <summary>
    /// The username half of a Basic credential.
    /// </summary>
    /// <remarks>
    /// Stored beside the password rather than encoded together with it, so the pair can be
    /// read back, checked and changed one at a time. Encoding happens at send — see
    /// <see cref="SmsAuth.Header"/>. Not a secret on its own: an account id, a sender id or
    /// an email, and it is shown on the screen like any other field.
    /// </remarks>
    public string AuthUsername { get; set; } = string.Empty;

    /// <summary>
    /// The credential the provider authenticates with — the key's value, or the password.
    /// </summary>
    /// <remarks>
    /// One secret per gateway whatever the scheme calls it, so the care around it lives in
    /// one place: write-only through the vendor's service, masked in the preview, cleared on
    /// delete. A column per scheme would mean repeating all of that and forgetting some of
    /// it on the scheme added next.
    ///
    /// In the database in plain text, like the mail account's app password beside it, and
    /// for the same bad reason: this app has no secret store and no server to hold one. It
    /// is the vendor's credential rather than a clinic's, which makes it worse, not better
    /// — one key opens sending for every practice in the country. Masked on screen and
    /// never rendered back, which stops it being read over a shoulder and nothing more.
    ///
    /// The fix, when there is somewhere to put it: a secret reference here and the value in
    /// the host's own store.
    /// </remarks>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// What the message appears to come from — a registered alphanumeric id or a number.
    /// </summary>
    /// <remarks>
    /// Per country because the rules are: some countries require a pre-registered id, some
    /// require a real number that can receive replies, and some rewrite whatever is sent.
    /// </remarks>
    public string? SenderId { get; set; }

    /// <summary>
    /// What the vendor charges a practice for one message sent through this gateway.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per country, because the cost of a message is: a text to a Philippine mobile and one
    /// to a UK mobile are bought at different rates from different carriers, and a single
    /// global price would be the vendor absorbing the difference in one direction and
    /// overcharging in the other.
    /// </para>
    /// <para>
    /// Zero is a real price, not "unset" — a country where messages are included in the
    /// plan. Which is why this is a decimal with no null: absent and free would otherwise
    /// be the same value, and one of them means "nobody has set this yet".
    /// </para>
    /// </remarks>
    public decimal PricePerMessage { get; set; }

    /// <summary>
    /// The currency <see cref="PricePerMessage"/> is in.
    /// </summary>
    /// <remarks>
    /// Stored beside the price rather than looked up from the country's plans. It has to
    /// match what the practice is billed in, and a country can carry plans in more than one
    /// currency — deriving it would mean picking one of them and being silently wrong for
    /// the other. The screen offers the plan currency as the default, which is the useful
    /// half of deriving it without the guessing.
    /// </remarks>
    public string CurrencyCode { get; set; } = "AUD";

    /// <summary>
    /// The request body to post, with tokens for the parts that change per message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The field that makes <see cref="ProviderName"/> honest about being a label. Carriers
    /// agree on what a text message is and on nothing else about how to ask for one: one
    /// wants <c>{"to":…,"message":…}</c>, the next wants <c>destination</c> and <c>text</c>,
    /// the one after that wants a form post. Written as code, each of those is a class and a
    /// deploy — which would make a table of gateways per country pointless, since a country
    /// could only ever be moved to a provider somebody had already written.
    /// </para>
    /// <para>
    /// Four tokens, substituted by <see cref="SmsPayload"/>: <c>{{To}}</c>,
    /// <c>{{Message}}</c>, <c>{{SenderId}}</c> and <c>{{ApiKey}}</c>. Anything else the
    /// provider wants — an account id, a route code, a callback URL — is typed in here as a
    /// constant, which is why the token list is short and does not grow.
    /// </para>
    /// </remarks>
    public string PayloadTemplate { get; set; } = string.Empty;

    /// <summary>
    /// The request headers, one <c>Name: Value</c> per line.
    /// </summary>
    /// <remarks>
    /// Data rather than code for the same reason as the body, and for one more: how a
    /// provider wants to be authenticated is itself provider-specific. A bearer token, an
    /// <c>X-API-Key</c>, a key repeated in the body — all three exist, and with the key as
    /// <c>{{ApiKey}}</c> in a header line, all three are a row somebody types rather than a
    /// branch somebody writes.
    /// </remarks>
    public string Headers { get; set; } = string.Empty;

    /// <summary>
    /// True where an API key's value is Base64-encoded on its way into its header.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For the providers that want a key pre-encoded without it being a Basic credential.
    /// Without this the operator encodes it by hand, which means the stored value is no
    /// longer the key the provider's dashboard shows, and rotating it becomes a step
    /// somebody has to remember rather than a paste.
    /// </para>
    /// <para>
    /// Read only under <see cref="SmsAuthType.ApiKey"/>. Basic always encodes — that is what
    /// Basic is — so under it this would either be redundant or a second encoding of an
    /// already encoded pair.
    /// </para>
    /// </remarks>
    public bool Base64EncodeApiKey { get; set; }

    /// <summary>
    /// What the body is — JSON, or a URL-encoded form.
    /// </summary>
    /// <remarks>
    /// Not cosmetic, and not inferable from the template: it decides how a value is escaped
    /// on the way in. A message with a quote in it has to be escaped for JSON and
    /// percent-encoded for a form, and getting that backwards produces a request the
    /// provider rejects for reasons that read like the gateway being broken. See the
    /// escaping note on <see cref="SmsPayload"/>.
    /// </remarks>
    public string ContentType { get; set; } = SmsPayload.Json;

    /// <summary>
    /// False where the country is configured but switched off.
    /// </summary>
    /// <remarks>
    /// Kept rather than deleted, so a gateway can be taken out of service without losing
    /// the settings — a provider outage is a switch, not a re-entry of the credentials.
    /// </remarks>
    public bool IsActive { get; set; } = true;

    /// <summary>When a test send was last attempted, and what came back.</summary>
    public DateTime? LastTestUtc { get; set; }

    public string? LastTestResult { get; set; }

    public bool LastTestSucceeded { get; set; }

    /// <summary>How this gateway authenticates, as the sender and the preview need it.</summary>
    public SmsAuth Auth => new(
        AuthType, AuthHeaderName, AuthUsername, ApiKey, Base64EncodeApiKey);

    /// <summary>True where the parts the chosen auth type needs are all present.</summary>
    /// <remarks>
    /// <para>
    /// Per type, because the fields differ — and so does which of them may be blank. An API
    /// key needs both a header to go in and a value to put there; neither half is any use
    /// alone.
    /// </para>
    /// <para>
    /// Basic needs only the username. An empty password is a real credential rather than a
    /// half-filled one: providers that authenticate on the key alone commonly take it as the
    /// username and want nothing after the colon, so <c>key:</c> is exactly what they
    /// document. A missing <em>username</em> is the broken case — it encodes to
    /// <c>:password</c>, which the provider reads as an empty account and answers 401 as
    /// though the password were wrong.
    /// </para>
    /// </remarks>
    public bool IsAuthConfigured => AuthType switch
    {
        SmsAuthType.BasicAuth => !string.IsNullOrWhiteSpace(AuthUsername),
        _ => !string.IsNullOrWhiteSpace(AuthHeaderName) && !string.IsNullOrWhiteSpace(ApiKey),
    };

    /// <summary>True where there is enough here to attempt a send.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CountryCode)
        && !string.IsNullOrWhiteSpace(ApiUrl)
        && IsAuthConfigured

        // The template counts. A gateway with a key and no body would post an empty
        // request, which a provider answers with a 400 — a configured-looking row that
        // cannot send is worse than one that admits it is not finished.
        && !string.IsNullOrWhiteSpace(PayloadTemplate);
}
