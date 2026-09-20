namespace DYS.Molargo.Domain.Enums;

/// <summary>How a gateway proves who it is to the provider.</summary>
/// <remarks>
/// <para>
/// A choice rather than free text, unlike the headers beside it. Authentication is the one
/// part of a provider's request that is nearly always one of a handful of shapes, and it is
/// the part that must be right before anything else can be debugged — a wrong body returns a
/// message naming the field, a wrong credential returns 401 and nothing else. Typed as a
/// header line it was a string somebody had to get exactly right with no help; as a choice
/// the screen can ask for the two or three things that shape actually needs.
/// </para>
/// <para>
/// The list is short on purpose and will grow. Whatever is added, the secret stays in
/// <c>SmsGateway.ApiKey</c> — one credential per gateway, write-only, whatever it is called
/// by the scheme using it.
/// </para>
/// </remarks>
public enum SmsAuthType
{
    /// <summary>
    /// A name and a value, sent as one header — <c>X-API-Key: abc123</c>.
    /// </summary>
    /// <remarks>
    /// Covers bearer tokens too, since the value is sent exactly as it is typed and
    /// <c>Bearer abc123</c> is a value like any other. Kept as one option rather than split
    /// into "api key" and "bearer", which differ only by seven characters the operator can
    /// see and type.
    /// </remarks>
    ApiKey = 0,

    /// <summary>
    /// A username and a password, sent as <c>Authorization: Basic</c> with the pair
    /// Base64-encoded.
    /// </summary>
    /// <remarks>
    /// Its own option because the encoding is the whole difficulty. Done by hand it means
    /// pasting a Base64 blob that is no longer either of the two values the provider's
    /// dashboard shows, so changing a password becomes a step somebody has to remember
    /// rather than a retype — and a blob nobody can read is a blob nobody can check.
    /// </remarks>
    BasicAuth = 1,
}
