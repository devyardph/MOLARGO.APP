using DYS.Molargo.Api.Infrastructure;

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
