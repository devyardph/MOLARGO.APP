using DYS.Molargo.Domain.Data;
using DYS.Molargo.Services.Session;
using Microsoft.Extensions.Caching.Hybrid;

namespace DYS.Molargo.Api.Infrastructure;

/// <summary>
/// The session directory, answered from memory where it can be.
/// </summary>
/// <remarks>
/// <para>
/// Every RPC call primes the session from the token, and the session loads the clinic's
/// sites before it can say where somebody is working. The session is scoped, so that load
/// happened once per request — two queries on top of whatever the call itself did, on every
/// call the app makes. Nothing else in the request was as cheap to remove.
/// </para>
/// <para>
/// Safe to cache because of what it is: the list of a clinic's active sites, and which site
/// a staff member is assigned to. Both change when somebody edits them on an admin screen,
/// which is rare, and neither decides what anybody is allowed to do — the guard re-reads the
/// staff record for that, every time, and is deliberately not cached.
/// </para>
/// <para>
/// Keyed by tenant, never globally. A cache keyed by anything less would hand one clinic
/// another clinic's sites, which is the one mistake a multi-tenant cache must not make.
/// </para>
/// <para>
/// Server-side only. A device holds one clinic in one process and has nothing to gain.
/// </para>
/// <para>
/// Through HybridCache, so this is an in-process cache when Redis is off and a shared one
/// when it is on — and this class does not change either way. See CacheRegistration.
/// </para>
/// </remarks>
internal sealed class CachedSessionDirectory : ISessionDirectory
{
    private readonly ISessionDirectory _inner;
    private readonly HybridCache _cache;
    private readonly ITenantContext _tenant;

    public CachedSessionDirectory(
        ISessionDirectory inner, HybridCache cache, ITenantContext tenant)
    {
        _inner = inner;
        _cache = cache;
        _tenant = tenant;
    }

    public Task<IReadOnlyList<SessionLocation>> GetActiveLocationsAsync(
        CancellationToken ct = default)
    {
        // No tenant, no cache. An empty tenant is a request that has not resolved a clinic,
        // and caching under it would be caching the one key every unresolved request shares.
        if (_tenant.TenantId == Guid.Empty) return _inner.GetActiveLocationsAsync(ct);

        // Lifetime comes from the Cache section rather than a constant here, so it is tuned
        // where the rest of the cache is configured. The factory runs once per miss even if
        // twenty requests miss together — the rest wait for it rather than each running the
        // query that the cache exists to avoid.
        return _cache.GetOrCreateAsync(
            $"sites:{_tenant.TenantId}",
            _inner,
            static (directory, token) => new ValueTask<IReadOnlyList<SessionLocation>>(
                directory.GetActiveLocationsAsync(token)),
            cancellationToken: ct).AsTask();
    }

    public Task<Guid?> GetPrimaryLocationAsync(Guid providerId, CancellationToken ct = default)
    {
        if (_tenant.TenantId == Guid.Empty) return _inner.GetPrimaryLocationAsync(providerId, ct);

        // The provider is in the key as well as the tenant. Without it every staff member in
        // a clinic would share one answer, and the second person to sign in would be put at
        // the first one's surgery.
        return _cache.GetOrCreateAsync(
            $"site:{_tenant.TenantId}:{providerId}",
            (Inner: _inner, Provider: providerId),
            static (state, token) => new ValueTask<Guid?>(
                state.Inner.GetPrimaryLocationAsync(state.Provider, token)),
            cancellationToken: ct).AsTask();
    }
}
