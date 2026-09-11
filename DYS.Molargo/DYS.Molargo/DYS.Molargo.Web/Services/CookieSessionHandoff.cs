using System.Collections.Concurrent;
using Microsoft.AspNetCore.WebUtilities;
using System.Security.Claims;
using DYS.Molargo.Shared.Services;
using Microsoft.AspNetCore.Components;

namespace DYS.Molargo.Web.Services;

/// <summary>
/// One verified identity, waiting to be exchanged for a cookie.
/// </summary>
/// <remarks>
/// Held server-side and named by an opaque token, so nothing about the user travels in the
/// URL. The token is single-use and expires in seconds — long enough for one redirect and
/// no longer, because it does land in the browser's history.
/// </remarks>
public sealed class SessionHandoffStore
{
    /// <summary>
    /// How long a handoff token is good for.
    /// </summary>
    /// <remarks>
    /// Thirty seconds: it is consumed by an immediate redirect, and anything longer is a
    /// window in which a token copied out of history still works.
    /// </remarks>
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, (SignedInUser User, DateTimeOffset Expires)> _pending
        = new(StringComparer.Ordinal);

    public string Issue(SignedInUser user)
    {
        Prune();

        // 32 bytes from a cryptographic source, URL-safe. Not a GUID: a GUID is not
        // guaranteed unpredictable, and this token is momentarily as good as a password.
        var token = WebEncoders.Base64UrlEncode(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        _pending[token] = (user, DateTimeOffset.UtcNow.Add(Lifetime));
        return token;
    }

    /// <summary>
    /// Takes the identity for a token, and destroys the token.
    /// </summary>
    /// <remarks>
    /// Removal happens whether or not the token had expired, so a replayed token is gone
    /// even on the attempt that failed.
    /// </remarks>
    public SignedInUser? Redeem(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        if (!_pending.TryRemove(token, out var entry)) return null;

        return entry.Expires > DateTimeOffset.UtcNow ? entry.User : null;
    }

    private void Prune()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var (token, entry) in _pending)
        {
            if (entry.Expires <= now) _pending.TryRemove(token, out _);
        }
    }
}

/// <summary>
/// The web head's session handoff: a cookie, set through a real HTTP round trip.
/// </summary>
/// <remarks>
/// <para>
/// A cookie cannot be written from inside a Blazor circuit — the response that would have
/// carried it was sent when the page loaded. So a verified sign-in is parked in
/// <see cref="SessionHandoffStore"/>, the circuit navigates to an endpoint with the token,
/// and that request writes the cookie before redirecting on.
/// </para>
/// <para>
/// The alternative — keeping a token in <c>localStorage</c> — would need no redirect and
/// is what a lot of Blazor apps do. It is worse: script on the page can read it, and a
/// session that survives a reload is exactly the thing worth not handing to an injected
/// script. The cookie is <c>HttpOnly</c>.
/// </para>
/// </remarks>
public sealed class CookieSessionHandoff : ISessionHandoff
{
    private readonly SessionHandoffStore _store;
    private readonly NavigationManager _navigation;
    private readonly IHttpContextAccessor _http;

    public CookieSessionHandoff(
        SessionHandoffStore store,
        NavigationManager navigation,
        IHttpContextAccessor http)
    {
        _store = store;
        _navigation = navigation;
        _http = http;
    }

    public bool RedirectsAfterSignIn => true;

    public Task CompleteSignInAsync(SignedInUser user, CancellationToken ct = default)
    {
        var token = _store.Issue(user);

        // forceLoad, deliberately. The point is to leave the circuit and make a real
        // request, because only a real response can carry a Set-Cookie header.
        _navigation.NavigateTo(
            $"auth/resume?token={Uri.EscapeDataString(token)}", forceLoad: true);

        return Task.CompletedTask;
    }

    public Task CompleteSignOutAsync(CancellationToken ct = default)
    {
        _navigation.NavigateTo("auth/sign-out", forceLoad: true);
        return Task.CompletedTask;
    }

    public Task<SignedInUser?> RestoreAsync(CancellationToken ct = default)
    {
        // Read from the authenticated principal the cookie middleware has already
        // established for this request. Available during the circuit's first, prerendered
        // render — which is when the shell asks.
        var user = _http.HttpContext?.User;

        if (user?.Identity?.IsAuthenticated is not true)
        {
            return Task.FromResult<SignedInUser?>(null);
        }

        var tenantId = Read(user, AuthClaims.TenantId);
        var providerId = Read(user, AuthClaims.ProviderId);

        if (!Guid.TryParse(tenantId, out var tenant)
            || !Guid.TryParse(providerId, out var provider))
        {
            // A cookie from an older build, or a tampered one. Treated as nobody rather
            // than as a partially-known user.
            return Task.FromResult<SignedInUser?>(null);
        }

        return Task.FromResult<SignedInUser?>(new SignedInUser(
            tenant,
            Read(user, AuthClaims.TenantName) ?? string.Empty,
            provider,
            user.Identity.Name ?? string.Empty));
    }

    private static string? Read(ClaimsPrincipal user, string claim) =>
        user.FindFirst(claim)?.Value;
}

/// <summary>The claim names the cookie carries.</summary>
/// <remarks>
/// Constants rather than literals at three call sites, because a typo in one of them reads
/// as "not signed in" rather than as an error.
/// </remarks>
public static class AuthClaims
{
    public const string TenantId = "molargo:tenant_id";
    public const string TenantName = "molargo:tenant_name";
    public const string ProviderId = "molargo:provider_id";
}
