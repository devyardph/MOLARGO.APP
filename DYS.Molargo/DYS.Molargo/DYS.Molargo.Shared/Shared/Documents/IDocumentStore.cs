namespace DYS.Molargo.Shared.Documents;

/// <summary>A file the store has written.</summary>
/// <param name="Key">The relative key to record against the document row.</param>
/// <param name="SizeBytes">
/// Bytes actually written. Returned rather than read from the source stream, because an
/// upload stream is not seekable — asking it for Length gave zero, and every uploaded
/// document displayed as "0 B".
/// </param>
public sealed record StoredFile(string Key, long SizeBytes);

/// <summary>
/// Where a patient's files actually live.
/// </summary>
/// <remarks>
/// <para>
/// The seam the Supabase bucket goes behind. Everything above this interface deals in a
/// relative key and a stream; nothing knows whether the bytes are on a local disk or in
/// object storage. <see cref="LocalDocumentStore"/> is the local implementation, and a
/// <c>SupabaseDocumentStore</c> replaces it in DI when online sync is built — no caller
/// changes.
/// </para>
/// <para>
/// The key is a relative path, never absolute. An absolute path recorded on one device is
/// meaningless on another, and on iOS it changes between installs of the same app — so
/// <c>PatientDocument.RelativePath</c> holds a key this store resolves, and the resolution
/// is the store's business alone.
/// </para>
/// </remarks>
public interface IDocumentStore
{
    /// <summary>
    /// Writes a file and returns its key and size.
    /// </summary>
    /// <param name="patientId">Scopes the key, so one patient's files sit together.</param>
    /// <param name="fileName">
    /// The name the file arrived with. Used for the extension and sanitised into the key;
    /// it is never trusted as a path.
    /// </param>
    Task<StoredFile> SaveAsync(
        Guid patientId, string fileName, Stream content, CancellationToken ct = default);

    /// <summary>Opens a stored file for reading, or null when the key resolves to nothing.</summary>
    Task<Stream?> OpenAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Removes a stored file. Idempotent: a key with nothing behind it is not an error,
    /// because a row whose file is already gone still has to be deletable.
    /// </summary>
    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// True when the key has bytes behind it. The documents tab shows a missing file
    /// rather than pretending the row is fine — a record referring to a file nobody can
    /// open is worth knowing about.
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}

/// <summary>
/// The root directory the local document store writes under. Interface only: only a head
/// knows where its platform allows writing.
/// </summary>
public interface IDocumentPathProvider
{
    /// <summary>
    /// Absolute path to the directory holding patient files. The implementation must
    /// create it if missing.
    /// </summary>
    string GetDocumentRoot();
}
