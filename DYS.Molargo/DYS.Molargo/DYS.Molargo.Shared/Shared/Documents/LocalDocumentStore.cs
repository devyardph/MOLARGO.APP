using System.Text.RegularExpressions;

namespace DYS.Molargo.Shared.Documents;

/// <summary>
/// Stores patient files on the local filesystem, under the root the head supplies.
/// </summary>
/// <remarks>
/// The placeholder for the Supabase bucket. Files sit beside the SQLite database, which
/// is the same shape the rest of the app already has while it is offline-only: one local
/// place holding everything, replaced wholesale when there is a server.
/// </remarks>
public sealed partial class LocalDocumentStore : IDocumentStore
{
    /// <summary>
    /// Longest name kept from the original file. Windows caps a full path at 260
    /// characters by default, and a patient subdirectory plus a GUID plus a long
    /// scanner-generated filename reaches it, at which point the write fails with a path
    /// error that says nothing about the cause.
    /// </summary>
    private const int MaxNameLength = 60;

    private readonly IDocumentPathProvider _paths;

    public LocalDocumentStore(IDocumentPathProvider paths) => _paths = paths;

    public async Task<StoredFile> SaveAsync(
        Guid patientId, string fileName, Stream content, CancellationToken ct = default)
    {
        // The key carries a GUID, not just the file name. Two patients uploading
        // "scan.jpg" must not collide, and neither must the same patient uploading it
        // twice — which is what happened when the key was the sanitised name alone.
        var key = $"patients/{patientId:n}/{Guid.NewGuid():n}-{Sanitise(fileName)}";

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

    /// <summary>
    /// Reduces an uploaded file name to something safe to put in a path.
    /// </summary>
    /// <remarks>
    /// Everything but letters, digits, dot, dash and underscore is replaced. A browser
    /// sends whatever the user's filesystem allowed — path separators, colons, control
    /// characters, right-to-left overrides — and none of it belongs in a key.
    /// </remarks>
    private static string Sanitise(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name)) return "file";

        var cleaned = UnsafeCharacters().Replace(name, "-").Trim('-', '.');
        if (cleaned.Length == 0) return "file";

        if (cleaned.Length <= MaxNameLength) return cleaned;

        // Truncate the stem, keep the extension: the extension is what decides how the
        // file opens, and losing it makes a perfectly good radiograph unopenable.
        var extension = Path.GetExtension(cleaned);
        var stem = Path.GetFileNameWithoutExtension(cleaned);
        var room = Math.Max(1, MaxNameLength - extension.Length);

        return stem[..Math.Min(stem.Length, room)] + extension;
    }

    [GeneratedRegex(@"[^A-Za-z0-9._-]+")]
    private static partial Regex UnsafeCharacters();
}
