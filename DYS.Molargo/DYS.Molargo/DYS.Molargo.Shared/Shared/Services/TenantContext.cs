namespace DYS.Molargo.Shared.Services;

/// <summary>
/// Which clinic the current work belongs to.
/// </summary>
/// <remarks>
/// <para>
/// Its own service rather than a field on the session, because the two answer different
/// questions and change at different times. The session's location and provider change as
/// staff move around the app; the tenant is fixed for the whole process and everything
/// written depends on it being right.
/// </para>
/// <para>
/// One install serves one clinic while the app is offline-only, so the tenant is minted
/// and stored locally on first run. When a server exists this is where a signed-in user's
/// clinic will be resolved instead — nothing else has to change, which is the point of the
/// seam.
/// </para>
/// </remarks>
public interface ITenantContext
{
    /// <summary>
    /// The current clinic, or <see cref="Guid.Empty"/> before it has been resolved.
    /// </summary>
    /// <remarks>
    /// Empty is a deliberate, safe default. Every query filters on equality with this
    /// value, so an unresolved tenant matches no rows — the alternative, treating "not
    /// yet known" as "no filter", turns one resolution bug into a cross-clinic data leak.
    /// </remarks>
    Guid TenantId { get; }

    /// <summary>The clinic's trading name, for a header or a footer.</summary>
    string? TenantName { get; }

    bool IsResolved { get; }

    /// <summary>
    /// Sets the current clinic. Called once, by the store, as it initialises.
    /// </summary>
    void Use(Guid tenantId, string? tenantName = null);
}

/// <inheritdoc cref="ITenantContext"/>
public sealed class TenantContext : ITenantContext
{
    public Guid TenantId { get; private set; }

    public string? TenantName { get; private set; }

    public bool IsResolved => TenantId != Guid.Empty;

    public void Use(Guid tenantId, string? tenantName = null)
    {
        TenantId = tenantId;

        // The name is context for a person, so a later call carrying only the id must not
        // blank a name already resolved.
        if (!string.IsNullOrWhiteSpace(tenantName)) TenantName = tenantName;
    }
}
