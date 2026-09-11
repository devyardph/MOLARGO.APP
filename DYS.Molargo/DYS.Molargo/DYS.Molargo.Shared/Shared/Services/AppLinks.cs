namespace DYS.Molargo.Shared.Services;

/// <summary>
/// Absolute links into this app, for putting in an email.
/// </summary>
/// <remarks>
/// <para>
/// A head seam, like <c>IDatabasePathProvider</c>, because only a head knows the answer. The
/// web head is served from an address and can read its own; a device install is not served
/// from anywhere, so a link back into it needs an address configured by hand or a deep link
/// that does not exist yet.
/// </para>
/// <para>
/// In-app navigation does not come through here — <c>IAppNavigator</c> handles that with
/// relative paths. This exists only for the case where a URL has to survive leaving the
/// app, which today means one thing: the password-reset email.
/// </para>
/// </remarks>
public interface IAppLinks
{
    /// <summary>
    /// The reset page, carrying the token.
    /// </summary>
    /// <remarks>
    /// The token travels in the query string, which is a deliberate and slightly
    /// uncomfortable choice: a URL is logged by proxies and lands in browser history. It is
    /// also the only place a link can carry anything, and the mitigations are on the token
    /// instead — single use, thirty minutes, and cancelled outright once spent.
    /// </remarks>
    string ResetPassword(string token);

    /// <summary>
    /// Whether a link emailed from this head can actually be opened and honoured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// False on a device install, and not because of missing deep-link plumbing — a custom
    /// scheme would open the app perfectly well. It is false because this app has no
    /// server: every head keeps its own SQLite file, so a reset token written on a tablet
    /// exists only in that tablet's database. A link opened anywhere else — a browser, a
    /// phone, the practice's web install — reaches a database where the token was never
    /// written, and is indistinguishable from an invented one.
    /// </para>
    /// <para>
    /// Which leaves only the same-device case: request it in the app, read the mail on the
    /// same device, tap the link. That is asking a tablet to email itself, and anybody able
    /// to do it is already looking at the sign-in screen of the install that holds the
    /// data. So the feature is withheld on those heads rather than offered and broken.
    /// </para>
    /// <para>
    /// This becomes true for every head the day staff and tokens live behind an API. Deep
    /// linking is then worth adding — to open the app rather than a browser — but it is a
    /// convenience at that point, not the thing that makes the reset work.
    /// </para>
    /// </remarks>
    bool SupportsEmailedLinks { get; }
}

/// <summary>
/// The fallback used where a head has not registered its own.
/// </summary>
/// <remarks>
/// Emits a relative path, which is wrong in an email and says so when read. Registered by
/// nothing on purpose: a head that forgets to supply a real one should produce a link that
/// obviously needs fixing rather than one that silently points at localhost.
/// </remarks>
public sealed class RelativeAppLinks : IAppLinks
{
    public string ResetPassword(string token) =>
        $"/reset-password?token={Uri.EscapeDataString(token)}";

    /// <summary>
    /// No. See <see cref="IAppLinks.SupportsEmailedLinks"/> — this head has no address, and
    /// more to the point no shared store for a token to be found in.
    /// </summary>
    public bool SupportsEmailedLinks => false;
}
