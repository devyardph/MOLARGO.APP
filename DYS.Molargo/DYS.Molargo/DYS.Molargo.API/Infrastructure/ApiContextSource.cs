using DYS.Molargo.Data;
using Microsoft.EntityFrameworkCore;

namespace DYS.Molargo.Api.Infrastructure;

/// <summary>
/// Hands the repository a context stamped with this request's clinic.
/// </summary>
/// <remarks>
/// <para>
/// The server's answer to what <c>MolargoDatabase</c> does on a device — minus everything
/// that only makes sense when the database is a file you own. No first-use creation, no
/// schema-version check, no sample seed: a shared Postgres database is migrated by a
/// deployment, not by whichever request happened to arrive first, and two of those racing
/// each other on startup is exactly the failure the device's semaphore exists to prevent.
/// </para>
/// <para>
/// Scoped, because <see cref="ITenantContext"/> is. A singleton here would capture the
/// first request's clinic and stamp it on every context afterwards.
/// </para>
/// </remarks>
public sealed class ApiContextSource : IMolargoContextSource
{
    private readonly IDbContextFactory<MolargoDbContext> _factory;
    private readonly ITenantContext _tenant;

    public ApiContextSource(
        IDbContextFactory<MolargoDbContext> factory, ITenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task<MolargoDbContext> CreateContextAsync(CancellationToken ct = default)
    {
        var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // The context reads the tenant through this on every query filter. Set on the way
        // out rather than baked into the options, because the factory builds contexts from
        // options alone and those are singleton — see MolargoDbContext.TenantSource.
        db.TenantSource = _tenant;

        return db;
    }
}
