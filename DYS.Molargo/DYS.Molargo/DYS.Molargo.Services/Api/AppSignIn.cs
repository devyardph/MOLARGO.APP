using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Services.Features.Auth;

namespace DYS.Molargo.Services.Api;

/// <summary>
/// Signing the app in: one call that checks the credential and issues the token.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <c>POST /auth/token</c>, which stays as the integration surface. That one
/// has its own password check, its own lockout counter and no notion of the two-step code —
/// a second rulebook, which was tolerable while only a script used it and is not tolerable
/// now that the app signs in this way.
/// </para>
/// <para>
/// This one runs the app's own <see cref="IAuthService"/> on the server and mints a token
/// from the result. One password check, one lockout counter, one place that knows what
/// two-step means.
/// </para>
/// </remarks>
/// <param name="Code">
/// The emailed code, on the second call of a two-step sign-in. Null on the first. One
/// request shape for both steps rather than two routes, because the client's decision
/// between them is already made by the previous answer.
/// </param>
public sealed record AppSignInRequest(
    string ClinicCode, string Username, string Password, string? Code = null);

/// <summary>
/// What the server says about a sign-in attempt.
/// </summary>
/// <remarks>
/// <para>
/// Carries the app's own <see cref="SignInResult"/> whole, rather than restating its fields.
/// The sign-in screen already reads that record — the refusal text, the lockout time, the
/// masked address a code went to — and a flattened copy would be a second thing to keep in
/// step with it.
/// </para>
/// <para>
/// The role, ownership and permissions ride alongside rather than inside it because
/// <see cref="SignInResult"/> does not carry them: locally the session reads them from the
/// staff record it already has in hand, and over the wire there is no such record.
/// </para>
/// </remarks>
/// <param name="Token">
/// Null unless the sign-in actually succeeded — including when a code has been emailed,
/// which is not a session. A token issued at that point would be a password-only login
/// wearing a second factor's name.
/// </param>
public sealed record AppSignInResponse(
    SignInResult Result,
    string? Token,
    DateTime? ExpiresUtc,
    ProviderRole Role,
    bool IsOwner,
    PracticePermissions Permissions,
    Guid LocationId,

    /// <summary>
    /// The clinic's currency, so figures render correctly from the first screen.
    /// </summary>
    /// <remarks>
    /// Sent with the sign-in rather than fetched after it. It is needed before anything with
    /// a dollar sign in it draws, and a separate call for one string would mean the first
    /// render of every money column was in the wrong currency and then corrected.
    /// </remarks>
    string? CurrencyCode = null)
{
    /// <summary>The route both sides use.</summary>
    public const string Route = "/auth/app";
}
