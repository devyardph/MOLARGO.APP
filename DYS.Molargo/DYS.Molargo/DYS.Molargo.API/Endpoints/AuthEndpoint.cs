using DYS.Molargo.Api.Infrastructure;
using DYS.Molargo.Services.Api;
using DYS.Molargo.Services.Features.Auth;

namespace DYS.Molargo.Api.Endpoints;

/// <summary>Signing in, and finding out who the token says you are.</summary>
/// <inheritdoc cref="PatientsEndpoint" path="/remarks"/>
public sealed class AuthEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/token", SignInAsync)

            // The one anonymous route in the API, and it has to be: there is no token to
            // present until this answers. Everything else is covered by the fallback policy
            // in Program.cs, which requires authentication unless a route opts out here.
            .AllowAnonymous()
            .WithSummary("Exchanges a clinic code, username and password for a token.");

        group.MapPost("/app", AppSignInAsync)
            .AllowAnonymous()
            .WithSummary("Signs the app in: runs the app's own sign-in, then issues a token.");

        group.MapGet("/me", WhoAmI)
            .WithSummary("What the caller's own token says about them.");
    }

    /// <summary>POST /auth/token</summary>
    private static async Task<IResult> SignInAsync(
        SignInRequest request, TokenService tokens, CancellationToken ct)
    {
        var (response, refusal) = await tokens.SignInAsync(request, ct);

        // 401 rather than 400. The request was well formed; what failed was the credential,
        // and a client telling those apart from the status code is the point of it.
        return refusal is null
            ? Results.Ok(response)
            : Results.Problem(refusal, statusCode: StatusCodes.Status401Unauthorized);
    }

    /// <summary>POST /auth/app</summary>
    /// <remarks>
    /// <para>
    /// The app's door, beside <c>POST /auth/token</c> rather than replacing it. That one
    /// stays as the integration surface for anything outside this codebase; this one runs
    /// the shared <see cref="IAuthService"/> — the same class the device ran locally — so
    /// the lockout counter, the generic refusal and the two-step emailed code are the app's
    /// own and exist in one place.
    /// </para>
    /// <para>
    /// The token is minted only once the service says a session exists. A password that was
    /// correct but awaits its emailed code returns a result and no token, because issuing
    /// one there would make the second factor decorative.
    /// </para>
    /// </remarks>
    private static async Task<IResult> AppSignInAsync(
        AppSignInRequest body, IAuthService auth, TokenService tokens, CancellationToken ct)
    {
        var result = string.IsNullOrEmpty(body.Code)
            ? await auth.SignInAsync(body.ClinicCode, body.Username, body.Password, ct)
            : await auth.CompleteWithCodeAsync(body.ClinicCode, body.Username, body.Code, ct);

        // A refusal is a 200 carrying a refusing result, not a status code. The service
        // wrote a sentence for the person — why it was refused, when a lockout lifts, where
        // a code went — and a bare 401 would throw that away and leave the screen to make
        // something up.
        if (!result.Succeeded)
        {
            return Results.Ok(new AppSignInResponse(
                result, null, null, default, false, default, Guid.Empty));
        }

        var issued = await tokens
            .IssueForAsync(result.TenantId, result.ProviderId, ct)
            .ConfigureAwait(false);

        if (issued is not { } token)
        {
            // The service authenticated somebody the token minter cannot find. That is a
            // server fault, not a bad credential, and saying so is what stops somebody
            // hunting for a typo in a password that was right.
            return Results.Problem(
                "Signed in, but a token could not be issued for that account.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        return Results.Ok(new AppSignInResponse(
            result,
            token.Token,
            token.ExpiresUtc,
            token.Role,
            token.IsOwner,
            token.Permissions,
            token.LocationId,
            token.CurrencyCode));
    }

    /// <summary>
    /// GET /auth/me
    /// </summary>
    /// <remarks>
    /// Read from the token, never from the database. It answers "what am I authorised as",
    /// which is a question about the credential presented — going back to the provider row
    /// would answer a different question and hide a stale token rather than reveal it.
    /// </remarks>
    private static IResult WhoAmI(IApiCaller caller) => Results.Ok(new
    {
        caller.TenantId,
        caller.ProviderId,
        caller.LocationId,
        Role = caller.Role.ToString(),
        Permissions = caller.Permissions.ToString(),
        caller.IsOwner,
    });
}
