using System.Text;

namespace DYS.Molargo.Domain;

/// <summary>One request, ready to post to a provider.</summary>
public sealed record SmsRequest(
    string Url,
    string ContentType,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    string Body);

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

    public const string DefaultHeaders = "Authorization: Bearer {{ApiKey}}";

    /// <summary>
    /// Builds the request a send would make.
    /// </summary>
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
        string apiKey,
        bool maskKey = false)
    {
        var key = maskKey ? MaskedKey : apiKey;

        var body = Substitute(template ?? string.Empty, contentType, to, message, senderId, key);

        var headers = new List<KeyValuePair<string, string>>();

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
