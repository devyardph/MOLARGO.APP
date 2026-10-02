namespace DYS.Molargo.Data;

/// <summary>
/// Hands out a context that is already confined to the caller's clinic.
/// </summary>
/// <remarks>
/// <para>
/// The seam between the repository and whoever owns the connection. On a device that owner
/// is <c>MolargoDatabase</c>, which also does first-use creation, the schema-version check
/// and the sample seed. The API's owner does none of those things — a shared server
/// database is not created, versioned or seeded by whichever request happened to arrive
/// first — and it should not have to inherit a type that says it is.
/// </para>
/// <para>
/// One method, deliberately. Everything else <c>MolargoDatabase</c> exposes is about
/// owning a file, and a repository has no business asking for any of it.
/// </para>
/// </remarks>
public interface IMolargoContextSource
{
    /// <summary>
    /// A ready-to-use context, already filtered to the current clinic. Dispose it when the
    /// unit of work is done.
    /// </summary>
    Task<MolargoDbContext> CreateContextAsync(CancellationToken ct = default);
}
