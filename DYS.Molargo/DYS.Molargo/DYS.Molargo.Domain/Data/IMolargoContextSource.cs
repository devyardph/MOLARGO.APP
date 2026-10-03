namespace DYS.Molargo.Domain.Data;

/// <summary>
/// Hands out a context that is already confined to the caller's clinic.
/// </summary>
/// <remarks>
/// <para>
/// The seam between the repository and whoever owns the connection. There used to be two
/// owners: a device's, which also did first-use creation, the schema-version check and the
/// sample seed, and the server's, which does none of those — a shared database is not
/// created, versioned or seeded by whichever request happened to arrive first.
/// </para>
/// <para>
/// Only the server's remains. The seam is kept because it is what let the device's go
/// without the repository noticing, and one method is still all a repository has any
/// business asking for.
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
