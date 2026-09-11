namespace DYS.Molargo.Shared.Services;

/// <summary>
/// The signed-in identity, as a host persists and restores it.
/// </summary>
public sealed record SignedInUser(Guid TenantId, string TenantName, Guid ProviderId, string DisplayName);

/// <summary>
/// Turns a verified sign-in into something that survives a page reload.
/// </summary>
/// <remarks>
/// <para>
/// A seam because only the host can do it. On the web head a session lives in a cookie,
/// and a cookie can only be set while an HTTP response is being written — which a Blazor
/// circuit is not. So the web implementation hands the verified identity back through a
/// short-lived, single-use token and a real HTTP round trip, and it is that round trip
/// that sets the cookie.
/// </para>
/// <para>
/// In a <c>BlazorWebView</c> there is no HTTP round trip to hang a cookie on, and no page
/// reload either — the view lives as long as the process — so the device implementation
/// does nothing and the in-memory session is already correct.
/// </para>
/// </remarks>
public interface ISessionHandoff
{
    /// <summary>
    /// True where completing the handoff navigates away, so the caller must not also
    /// navigate.
    /// </summary>
    bool RedirectsAfterSignIn { get; }

    /// <summary>
    /// Persists a verified identity for this browser or device.
    /// </summary>
    /// <remarks>
    /// Called only after a password has been checked. Nothing here verifies anything — it
    /// is the last step of a sign-in, not a way to become someone.
    /// </remarks>
    Task CompleteSignInAsync(SignedInUser user, CancellationToken ct = default);

    /// <summary>Discards whatever <see cref="CompleteSignInAsync"/> persisted.</summary>
    Task CompleteSignOutAsync(CancellationToken ct = default);

    /// <summary>
    /// The identity this browser or device already holds, if any.
    /// </summary>
    /// <remarks>
    /// Read on every new circuit, which is what makes a refresh keep the user signed in.
    /// Null means nobody, and the shell then sends them to the sign-in screen.
    /// </remarks>
    Task<SignedInUser?> RestoreAsync(CancellationToken ct = default);
}

/// <summary>
/// The device implementation: nothing to persist, nothing to restore.
/// </summary>
/// <remarks>
/// The default, so a head that has no notion of a reload — a MAUI
/// <c>BlazorWebView</c> — needs no code of its own. The session it already holds in memory
/// lasts as long as the app process, which on a tablet is the whole shift.
/// </remarks>
public sealed class InMemorySessionHandoff : ISessionHandoff
{
    public bool RedirectsAfterSignIn => false;

    public Task CompleteSignInAsync(SignedInUser user, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task CompleteSignOutAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<SignedInUser?> RestoreAsync(CancellationToken ct = default) =>
        Task.FromResult<SignedInUser?>(null);
}
