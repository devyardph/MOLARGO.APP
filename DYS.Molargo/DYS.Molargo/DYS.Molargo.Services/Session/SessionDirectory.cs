using DYS.Molargo.Domain.Data;
using DYS.Molargo.Domain.Entities;

namespace DYS.Molargo.Services.Session;

/// <summary>
/// The two reads a session needs before it can say where somebody is working.
/// </summary>
/// <remarks>
/// <para>
/// Pulled out of <see cref="SessionService"/> so the session can stay local in both modes.
/// The session itself must be local — it is read synchronously as properties and raises an
/// event when it changes, neither of which survives a round trip — but it has to get the
/// sites from somewhere, and in API mode there is no local database to get them from.
/// </para>
/// <para>
/// So the session keeps the state and this fetches the facts. Locally it reads the
/// repositories as before; in API mode it is a proxy to the server, which runs this same
/// class against PostgreSQL.
/// </para>
/// <para>
/// Two methods and no more. It is tempting to let the session ask for the whole staff
/// record while it is here, and that is how a seam becomes a second data layer.
/// </para>
/// </remarks>
public interface ISessionDirectory
{
    /// <summary>Every active site in the clinic, in the order the practice set.</summary>
    /// <remarks>
    /// Ordered here, not by the caller. DisplayOrder is something the practice chose on
    /// the Sites screen, so it is a fact about the data rather than a preference of
    /// whichever list is rendering — and it is the only place that column is readable,
    /// since <see cref="SessionLocation"/> does not carry it.
    /// </remarks>
    Task<IReadOnlyList<SessionLocation>> GetActiveLocationsAsync(CancellationToken ct = default);

    /// <summary>
    /// The site a staff member is assigned to, or null where they have none.
    /// </summary>
    /// <remarks>
    /// Returns the id rather than the provider. The session needs one field, and handing
    /// back a staff record would put a password hash on the wire to answer a question about
    /// a location.
    /// </remarks>
    Task<Guid?> GetPrimaryLocationAsync(Guid providerId, CancellationToken ct = default);
}

/// <inheritdoc cref="ISessionDirectory"/>
public sealed class SessionDirectory : ISessionDirectory
{
    private readonly IRepository<PracticeLocation> _locations;
    private readonly IRepository<Provider> _providers;

    public SessionDirectory(
        IRepository<PracticeLocation> locations, IRepository<Provider> providers)
    {
        _locations = locations;
        _providers = providers;
    }

    public async Task<IReadOnlyList<SessionLocation>> GetActiveLocationsAsync(
        CancellationToken ct = default)
    {
        var rows = await _locations
            .ListAsync(location => location.IsActive, ct)
            .ConfigureAwait(false);

        return rows
            .OrderBy(location => location.DisplayOrder)
            .ThenBy(location => location.Name)
            .Select(location => new SessionLocation(
                location.Id,
                location.ShortName ?? location.Name,
                location.Hours))
            .ToList();
    }

    public async Task<Guid?> GetPrimaryLocationAsync(
        Guid providerId, CancellationToken ct = default)
    {
        var provider = await _providers.GetByIdAsync(providerId, ct).ConfigureAwait(false);

        return provider?.PrimaryLocationId;
    }
}
