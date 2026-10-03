using DYS.Molargo.Domain.Data;

namespace DYS.Molargo.Api.Infrastructure;

/// <summary>
/// Puts the caller's clinic into the scoped tenant context, once per request.
/// </summary>
/// <remarks>
/// <para>
/// After authentication and before anything reads data. The context starts at
/// <see cref="Guid.Empty"/>, every query filter compares against it, and an empty value
/// matches no rows — so the failure mode of this middleware not running is an endpoint
/// that returns nothing, not one that returns everybody's records.
/// </para>
/// <para>
/// It resolves from the token and from nowhere else. A tenant taken from a header or a
/// query parameter would let any authenticated caller read any clinic by changing one
/// value, which is the whole of multi-tenancy given away for the convenience of testing.
/// </para>
/// </remarks>
public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IApiCaller caller, ITenantContext tenant)
    {
        if (caller.IsAuthenticated && caller.TenantId != Guid.Empty)
        {
            tenant.Use(caller.TenantId);
        }

        await _next(context).ConfigureAwait(false);
    }
}
