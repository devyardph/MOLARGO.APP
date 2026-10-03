using DYS.Molargo.Services.Features.Auth;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DYS.Molargo.Domain.Data;
using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Api;
using DYS.Molargo.Shared.Components;
using DYS.Molargo.Shared.Services;

namespace DYS.Molargo.Shared.Features.Auth.Services;

/// <summary>
/// Signing in when the records are on a server.
/// </summary>
/// <remarks>
/// <para>
/// The one hand-written client in API mode. Every other feature service is a generated
/// proxy, because every other feature service does its whole job on one machine — this one
/// does not. Checking the credential belongs on the server, where the staff record and the
/// lockout counter are; starting the session belongs here, because the session is what this
/// device shows in its app bar and guards its layout with.
/// </para>
/// <para>
/// So it is not a proxy and could not be one: a proxied <c>SignInAsync</c> would check the
/// password correctly and then start a session on the server, leaving the device signed out
/// and certain it had just signed in.
/// </para>
/// </remarks>
public sealed class ApiAuthService : IAuthService
{
    private readonly HttpClient _http;
    private readonly IApiTokenStore _tokens;
    private readonly ISessionService _session;
    private readonly ITenantContext _tenant;
    private readonly ISessionHandoff _handoff;

    public ApiAuthService(
        HttpClient http,
        IApiTokenStore tokens,
        ISessionService session,
        ITenantContext tenant,
        ISessionHandoff handoff)
    {
        _http = http;
        _tokens = tokens;
        _session = session;
        _tenant = tenant;
        _handoff = handoff;
    }

    public Task<SignInResult> SignInAsync(
        string tenantCode, string username, string password, CancellationToken ct = default) =>
        ExchangeAsync(new AppSignInRequest(tenantCode, username, password), ct);

    public Task<SignInResult> CompleteWithCodeAsync(
        string tenantCode, string username, string code, CancellationToken ct = default) =>

        // The password is not re-sent, and the server does not want it: the first step
        // already established it, and holding it on the device between the two steps would
        // be keeping a password in memory for as long as somebody takes to read an email.
        ExchangeAsync(new AppSignInRequest(tenantCode, username, string.Empty, code), ct);

    /// <summary>
    /// The clinic code this installation already belongs to.
    /// </summary>
    /// <remarks>
    /// Always null in API mode, which makes the sign-in screen ask for it. Locally this
    /// reads the clinic out of the device's own database; here there is no such thing — the
    /// device belongs to no clinic until somebody signs in, and one that remembered the last
    /// code would be remembering it on a device that may be shared by a whole front desk.
    /// </remarks>
    public Task<string?> GetInstalledTenantCodeAsync(CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    /// <summary>
    /// Restores a session the host already holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to return false always, on the reasoning that a bearer token surviving the
    /// process meant a bearer token written down somewhere. That reasoning holds on a device
    /// — which never reloads, so nothing is lost — and was simply wrong on the web head,
    /// where setting the sign-in cookie requires a full page load and therefore a new circuit
    /// with an empty session. The restore is the only thing that can repopulate it, so
    /// refusing to restore meant signing in, reloading, finding nobody signed in, and being
    /// sent back to sign in again, forever.
    /// </para>
    /// <para>
    /// Role and permissions come from the token itself, through <c>/auth/me</c>, rather than
    /// from the host's cookie. A cookie is a claim the browser holds; what a person may do
    /// is decided by the credential the server issued.
    /// </para>
    /// </remarks>
    public async Task<bool> RestoreAsync(CancellationToken ct = default)
    {
        if (_session.IsSignedIn) return true;

        var stored = await _handoff.RestoreAsync(ct).ConfigureAwait(false);

        // No host session, or one from before tokens were carried. Either way there is
        // nothing to call the API with, and a session without one is worse than none.
        if (stored?.ApiToken is not { Length: > 0 } token) return false;

        _tokens.Set(token);

        WhoAmI? me;

        try
        {
            // The header goes on this request by hand. Setting the token in the store above
            // does not put it on anything: RpcChannel attaches it per request on purpose,
            // because a default header on the shared client would still be the first
            // person's after somebody else signed in. GetFromJsonAsync knows none of that
            // and sent this anonymously — /auth/me answered 401, the restore failed, and the
            // web head bounced off its own sign-in screen with nothing reported anywhere.
            using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // Most likely an expired token out of the cookie. Nothing to restore, and
                // the sign-in screen is where that is recovered from.
                _tokens.Clear();

                return false;
            }

            me = await response.Content
                .ReadFromJsonAsync<WhoAmI>(MolargoRpc.Json, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Unreachable, or the token has expired and the server refused it. Both mean
            // no session; the sign-in screen is where either is recovered from.
            _tokens.Clear();

            return false;
        }

        if (me is null)
        {
            _tokens.Clear();

            return false;
        }

        _tenant.Use(stored.TenantId, stored.TenantName);

        await _session
            .SignInAsync(
                stored.ProviderId,
                stored.DisplayName,
                me.ParsedRole,
                me.IsOwner,
                me.ParsedPermissions,
                ct)
            .ConfigureAwait(false);

        if (me.LocationId != Guid.Empty) _session.SelectLocation(me.LocationId);

        return true;
    }

    /// <summary>
    /// What <c>/auth/me</c> says about the token being presented.
    /// </summary>
    /// <remarks>
    /// Role and Permissions are strings, because that is what the route sends — it writes
    /// them with ToString() so the REST surface is readable by a person. Declaring them as
    /// the enums threw a JsonException on every restore, which is not an
    /// HttpRequestException and so fell straight past the catch around the call: the web
    /// head bounced off its own sign-in screen and nothing anywhere said why.
    /// </remarks>
    private sealed record WhoAmI(
        Guid TenantId,
        Guid ProviderId,
        Guid LocationId,
        string Role,
        string Permissions,
        bool IsOwner)
    {
        /// <summary>Unknown reads as the least privilege, never as the most.</summary>
        public ProviderRole ParsedRole =>
            Enum.TryParse<ProviderRole>(Role, out var role) ? role : ProviderRole.Administration;

        public PracticePermissions ParsedPermissions =>
            Enum.TryParse<PracticePermissions>(Permissions, out var held)
                ? held
                : PracticePermissions.None;
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        // The token goes first. If clearing the session threw, a token left behind would be
        // a signed-out screen that could still make authenticated calls.
        _tokens.Clear();

        await _session.SignOutAsync(ct).ConfigureAwait(false);
        await _handoff.CompleteSignOutAsync(ct).ConfigureAwait(false);
    }

    private async Task<SignInResult> ExchangeAsync(
        AppSignInRequest request, CancellationToken ct)
    {
        AppSignInResponse? answer;

        try
        {
            var response = await _http
                .PostAsJsonAsync(AppSignInResponse.Route, request, MolargoRpc.Json, ct)
                .ConfigureAwait(false);

            // A refusal is a 200 carrying a refusing SignInResult, not a status code. The
            // server is reporting the outcome of a check it ran, and the sentence it wrote
            // for the person is in the body — collapsing that into a 401 would throw the
            // sentence away and leave the screen to invent its own.
            if (!response.IsSuccessStatusCode)
            {
                return SignInResult.Refused(
                    $"The practice server refused the sign-in ({(int)response.StatusCode}).");
            }

            answer = await response.Content
                .ReadFromJsonAsync<AppSignInResponse>(MolargoRpc.Json, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return SignInResult.Refused(
                ApiCallException.Unreachable(_http.BaseAddress, ex));
        }

        // A 200 that deserialised to nothing. Not a transport failure, but the same dead
        // end for the person in front of it.
        if (answer is null)
        {
            return SignInResult.Refused(ApiCallException.Unreachable(_http.BaseAddress));
        }

        // Either a refusal or the first half of a two-step sign-in. Both are the screen's
        // business and neither starts a session.
        if (!answer.Result.Succeeded || answer.Token is not { Length: > 0 } token)
        {
            return answer.Result;
        }

        _tokens.Set(token);

        // Before the session, because the session's own load goes over the wire and needs
        // the token to be there. Setting the clinic here also pushes the currency into
        // MolargoFormat, so the first money column drawn is already right.
        _tenant.Use(answer.Result.TenantId, answer.Result.TenantName, answer.CurrencyCode);

        await _session.SignInAsync(
            answer.Result.ProviderId,
            answer.Result.DisplayName ?? string.Empty,
            answer.Role,
            answer.IsOwner,
            answer.Permissions,
            ct).ConfigureAwait(false);

        if (answer.LocationId != Guid.Empty) _session.SelectLocation(answer.LocationId);

        // Then out to the host, exactly as the local AuthService does. Leaving this out was
        // a silent bug on the web head and only there: a Blazor circuit cannot set a cookie,
        // so CookieSessionHandoff answers by navigating through an endpoint that can — and
        // with nobody calling it, sign-in succeeded, the session filled, and the very next
        // request arrived with no cookie and bounced back to the sign-in screen with nothing
        // reported. On a device this does nothing, because the in-memory session above is
        // already enough, which is why the device looked fine.
        await _handoff
            .CompleteSignInAsync(
                new SignedInUser(
                    answer.Result.TenantId,
                    answer.Result.TenantName ?? string.Empty,
                    answer.Result.ProviderId,
                    answer.Result.DisplayName ?? string.Empty,

                    // The token, which is the entire reason the handoff carries one. Omitting
                    // it left the web head signing in, reloading to set its cookie, restoring
                    // a session with no way to call anything, and bouncing back to sign in —
                    // forever, and silently.
                    token),
                ct)
            .ConfigureAwait(false);

        return answer.Result;
    }
}
