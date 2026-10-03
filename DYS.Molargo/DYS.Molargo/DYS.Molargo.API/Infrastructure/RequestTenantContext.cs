using DYS.Molargo.Domain.Data;

namespace DYS.Molargo.Api.Infrastructure;

/// <summary>
/// The clinic this one request belongs to.
/// </summary>
/// <remarks>
/// <para>
/// Registered scoped, which is the whole point. On a device the tenant is a singleton
/// because the process serves one signed-in person; here two requests for two clinics are
/// in flight at once, and a singleton would mean whichever signed in last decided what the
/// other could read.
/// </para>
/// <para>
/// It starts unresolved — <see cref="Guid.Empty"/> — and every query filters on equality
/// with it, so a request that somehow reached a repository before authentication ran
/// matches no rows at all. That is the safe direction to fail: treating "not yet known" as
/// "no filter" turns one bug in this file into a cross-clinic data leak.
/// </para>
/// </remarks>
public sealed class RequestTenantContext : TenantContext
{
}
