namespace DYS.Molargo.Services.Documents;

/// <summary>
/// Stores patient files on the local filesystem, under the root the head supplies.
/// </summary>
/// <remarks>
/// The placeholder for an object store. Files sit on the API server's own filesystem,
/// which works for exactly one API server: a file uploaded to one instance is not on
/// another, so this is what has to be replaced before the API can be scaled out. It also
/// means a backup of the database alone does not carry the documents.
/// </remarks>
public sealed class LocalDocumentStore : IDocumentStore
{
    private readonly IDocumentPathProvider _paths;

    public LocalDocumentStore(IDocumentPathProvider paths) => _paths = paths;

    public async Task<StoredFile> SaveAsync(
        Guid patientId, string fileName, Stream content, CancellationToken ct = default)
    {
        // Built by DocumentKey rather than here, so this store and the S3 one write the
        // same key for the same upload — which is what lets a practice be moved from one
        // to the other by copying files, with nothing in the database to rewrite.
        var key = DocumentKey.For(patientId, fileName);

        var destination = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        // FileMode.CreateNew, not Create: the GUID makes a collision essentially
        // impossible, and if one somehow happens this fails loudly rather than silently
        // overwriting another patient's file.
        await using var file = new FileStream(
            destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);

        await content.CopyToAsync(file, ct).ConfigureAwait(false);

        // Taken from the destination, which is the only place the true count exists: the
        // source is an unseekable upload stream and its Length throws or reads zero.
        return new StoredFile(key, file.Length);
    }

    public Task<Stream?> OpenAsync(string key, CancellationToken ct = default)
    {
        var path = Resolve(key);

        if (!File.Exists(path)) return Task.FromResult<Stream?>(null);

        return Task.FromResult<Stream?>(
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = Resolve(key);

        // Deliberately not throwing on a missing file. A row whose file has already gone
        // still has to be deletable, or the record is stuck holding a reference to
        // nothing.
        if (File.Exists(path)) File.Delete(path);

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(File.Exists(Resolve(key)));

    /// <summary>
    /// Turns a stored key into an absolute path, and refuses to escape the root.
    /// </summary>
    /// <remarks>
    /// The containment check is the important part. A key is data — it comes out of the
    /// database, and a row written by anything other than this store could hold
    /// "../../appsettings.json". Resolving the full path and comparing it against the
    /// root is what stops a document row being used to read or delete arbitrary files.
    /// </remarks>
    private string Resolve(string key)
    {
        var root = Path.GetFullPath(_paths.GetDocumentRoot());
        var full = Path.GetFullPath(Path.Combine(root, key));

        // The trailing separator matters: without it, a sibling directory whose name
        // starts with the root's name ("...\molargo-docs-old") passes the prefix test.
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Document key '{key}' resolves outside the document root.");
        }

        return full;
    }

}
