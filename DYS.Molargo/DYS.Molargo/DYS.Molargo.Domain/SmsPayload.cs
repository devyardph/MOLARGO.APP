using System.Text;
using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain;

/// <summary>One request, ready to post to a provider.</summary>
public sealed record SmsRequest(
    string Url,
    string ContentType,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    string Body);

/// <summary>
/// How one gateway authenticates, and the values that shape needs.
/// </summary>
/// <remarks>
/// <para>
/// One object rather than four more parameters on <see cref="SmsPayload.Render"/>, which
/// already carries everything about the request that varies per message. These vary per
/// gateway, they are only meaningful together — a username means nothing without the scheme
/// that reads it — and half of them are unused in either shape.
/// </para>
/// <para>
/// <see cref="Secret"/> is the gateway's one credential whatever the scheme calls it: the
/// key's value under <see cref="SmsAuthType.ApiKey"/>, the password under
/// <see cref="SmsAuthType.BasicAuth"/>. One secret per gateway means one field that is
/// write-only, one that is masked in the preview and one that is cleared on delete, rather
/// than that care being repeated per scheme and forgotten on the scheme added next.
/// </para>
/// </remarks>
/// <param name="HeaderName">
/// The header the credential is sent in. Used by <see cref="SmsAuthType.ApiKey"/> only —
/// Basic is <c>Authorization</c> by definition, and letting it be typed would invite a
/// gateway that sends a correctly built Basic credential in a header the provider does not
/// read.
/// </param>
/// <param name="Base64EncodeKey">
/// Encodes the API key's value on its way into the header, for providers that expect it that
/// way. Ignored under <see cref="SmsAuthType.BasicAuth"/>, which always encodes — that is
/// what Basic is.
/// </param>
public sealed record SmsAuth(
    SmsAuthType Type,
    string HeaderName,
    string Username,
    string Secret,
    bool Base64EncodeKey)
{
    /// <summary>The header <c>Authorization</c>, which Basic is defined to use.</summary>
    public const string AuthorizationHeader = "Authorization";

    /// <summary>A gateway that sends no credential of its own.</summary>
    /// <remarks>
    /// The shape the preview renders before anything has been filled in, and what the send
    /// path falls back to for a row that predates this being a choice. Not a supported way
    /// to configure a gateway — the screen requires one — but a request with no auth header
    /// is a clean 401 from the provider, where a half-built one is a malformed header.
    /// </remarks>
    public static SmsAuth None { get; } =
        new(SmsAuthType.ApiKey, string.Empty, string.Empty, string.Empty, false);

    /// <summary>
    /// The one header this scheme contributes, or null where there is nothing to send.
    /// </summary>
    /// <param name="mask">True for the preview, which must never print the credential.</param>
    public KeyValuePair<string, string>? Header(bool mask)
    {
        var secret = mask ? SmsPayload.MaskedKey : Secret;

        if (Type == SmsAuthType.BasicAuth)
        {
            // The username, not the pair. An empty password is a credential providers
            // genuinely document — the key as the username and nothing after the colon —
            // so "key:" is sent as it stands. Both halves empty is not a credential at all,
            // and encoding it would put a plausible-looking "Basic Og==" on the wire.
            if (Username.Length == 0 && Secret.Length == 0) return null;

            // Masked whole rather than encoded. Base64 of a username beside eight dots is a
            // string somebody would try to decode and compare, and it would decode to the
            // dots — which reads as the password having been stored wrong.
            var value = mask
                ? SmsPayload.MaskedKey
                : SmsPayload.ToBase64($"{Username}:{Secret}");

            return new KeyValuePair<string, string>(AuthorizationHeader, "Basic " + value);
        }

        var name = HeaderName.Trim();

        if (name.Length == 0 || secret.Length == 0) return null;

        // Never encoded when masked — the dots would become a plausible-looking credential,
        // and the preview's job is to be obviously not one.
        return new KeyValuePair<string, string>(
            name, !mask && Base64EncodeKey ? SmsPayload.ToBase64(secret) : secret);
    }
}

/// <summary>
/// Turns a provider's payload template into a request.
/// </summary>
/// <remarks>
/// <para>
/// A template rather than an adapter class per carrier. The gateway is already a row per
/// country — see <c>SmsGateway</c> — so adding a carrier has to be a row too, or the
/// per-country design is decoration around a switch statement that still needs a deploy.
/// </para>
/// <para>
/// Four tokens, and no more: <c>{{To}}</c>, <c>{{Message}}</c>, <c>{{SenderId}}</c> and
/// <c>{{ApiKey}}</c>. Everything else a provider wants — an account id, a route, a callback
/// URL — is a constant the operator types straight into the template, which is why there is
/// no list of those here to keep up to date.
/// </para>
/// </remarks>
public static class SmsPayload
{
    /// <summary>The token for the credential, which the preview masks.</summary>
    public const string ApiKeyToken = "{{ApiKey}}";

    public const string Json = "application/json";

    public const string Form = "application/x-www-form-urlencoded";

    /// <summary>What the preview prints where the key would go.</summary>
    public const string MaskedKey = "••••••••";

    /// <summary>A starting template, for a gateway that has none yet.</summary>
    /// <remarks>
    /// The commonest shape, not a claim about any particular provider. It exists so the box
    /// is not empty on a new gateway: an empty template is a send that posts nothing, and
    /// editing an example is faster than writing one from memory.
    /// </remarks>
    public const string DefaultTemplate =
        "{\n  \"to\": \"{{To}}\",\n  \"from\": \"{{SenderId}}\",\n  \"message\": \"{{Message}}\"\n}";

    /// <summary>
    /// The header lines a new gateway starts with.
    /// </summary>
    /// <remarks>
    /// Empty, since the credential is no longer typed here — the auth type builds that
    /// header. This box is for the rest: an Accept, a route code, whatever else a provider
    /// documents. Prefilling it with an Authorization line again would produce a second one
    /// beside the one the auth type already sends.
    /// </remarks>
    public const string DefaultHeaders = "";

    /// <summary>
    /// Builds the request a send would make.
    /// </summary>
    /// <param name="auth">
    /// How the gateway authenticates. Contributes one header, added ahead of the typed ones
    /// so it reads first in the preview — it is the line somebody is usually checking.
    /// </param>
    /// <param name="maskKey">
    /// True for the preview on screen. The key is the one value that must never be rendered
    /// into a page — the whole gateway service is built so it does not leave the database.
    /// </param>
    public static SmsRequest Render(
        string url,
        string contentType,
        string? headerLines,
        string template,
        string to,
        string message,
        string? senderId,
        SmsAuth auth,
        bool maskKey = false)
    {
        var key = maskKey ? MaskedKey : auth.Secret;

        var body = Substitute(template ?? string.Empty, contentType, to, message, senderId, key);

        var headers = new List<KeyValuePair<string, string>>();

        // First, and built rather than typed. Everything below is the operator's text; this
        // one is the scheme's, and a provider that refuses the request refuses it over this
        // line far more often than over any of the others.
        if (auth.Header(maskKey) is { } credential) headers.Add(credential);

        foreach (var line in ReadLines(headerLines))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);

            // A line with no colon is not a header. Skipped rather than guessed at: half a
            // header sent to a provider is a request refused for a reason nobody can see
            // from this end.
            if (colon <= 0) continue;

            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            // Headers are never escaped for the body's content type. They are not part of
            // the body, and JSON-escaping a bearer token would corrupt it.
            headers.Add(new KeyValuePair<string, string>(
                name, Replace(value, to, message, senderId, key, Raw)));
        }

        return new SmsRequest(url, contentType, headers, body);
    }

    /// <summary>The header lines of a gateway, trimmed and without the blanks.</summary>
    public static IEnumerable<string> ReadLines(string? headerLines) =>
        (headerLines ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Substitute(
        string template, string contentType, string to, string message,
        string? senderId, string key)
    {
        Func<string, string> escape = contentType switch
        {
            Form => Uri.EscapeDataString,

            // JSON by default, including for anything unrecognised. A template that is
            // neither JSON nor a form is almost certainly XML or JSON-like, and escaping
            // quotes and control characters is the safer wrong answer.
            _ => JsonEscape,
        };

        return Replace(template, to, message, senderId, key, escape);
    }

    private static string Replace(
        string text, string to, string message, string? senderId, string key,
        Func<string, string> escape) =>
        text
            .Replace("{{To}}", escape(to), StringComparison.Ordinal)
            .Replace("{{Message}}", escape(message), StringComparison.Ordinal)
            .Replace("{{SenderId}}", escape(senderId ?? string.Empty), StringComparison.Ordinal)
            .Replace(ApiKeyToken, escape(key), StringComparison.Ordinal);

    private static string Raw(string value) => value;

    /// <summary>
    /// Base64 of the credential, as a provider expecting HTTP Basic wants it.
    /// </summary>
    /// <remarks>
    /// UTF-8 rather than the platform's encoding, so a key carrying a non-ASCII character
    /// encodes to the same bytes here as it does anywhere else. Empty in, empty out — a
    /// gateway with no key yet must not put <c>Basic</c> followed by the Base64 of nothing
    /// on the wire, which reads to a provider as a malformed credential rather than a
    /// missing one.
    /// </remarks>
    internal static string ToBase64(string value) =>
        value.Length == 0
            ? string.Empty
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    /// <summary>
    /// Escapes a value for use inside a JSON string.
    /// </summary>
    /// <remarks>
    /// The whole reason substitution cannot be a plain string replace. A reminder carrying
    /// an apostrophe is harmless; one carrying a quote, a backslash or a line break — a
    /// practice called O'Brien &amp; Sons is fine, a two-line message is not — turns the
    /// template into malformed JSON. The provider then refuses the request, and the failure
    /// reads as "the gateway is broken" rather than "that message had a quote in it".
    ///
    /// The quotes around the token stay in the template, so this escapes the contents and
    /// never the delimiters.
    /// </remarks>
    private static string JsonEscape(string value)
    {
        var builder = new StringBuilder(value.Length + 8);

        foreach (var character in value)
        {
            switch (character)
            {
                case '"': builder.Append('\\').Append('"'); break;
                case '\\': builder.Append('\\').Append('\\'); break;
                case '\b': builder.Append('\\').Append('b'); break;
                case '\f': builder.Append('\\').Append('f'); break;
                case '\n': builder.Append('\\').Append('n'); break;
                case '\r': builder.Append('\\').Append('r'); break;
                case '\t': builder.Append('\\').Append('t'); break;

                default:
                    // Control characters have to go as \u00XX; anything printable, including
                    // every non-ASCII letter, is valid in a JSON string as itself. Escaping
                    // those too would send é where the carrier expected é, and a
                    // carrier counts characters to decide how many messages it is charging
                    // for.
                    if (char.IsControl(character))
                    {
                        builder.Append('\\').Append('u').Append(((int)character).ToString("x4"));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        return builder.ToString();
    }
}
