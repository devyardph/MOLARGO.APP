using System.Text.RegularExpressions;

namespace DYS.Molargo.Services.Documents;

/// <summary>
/// How a stored file's key is built, for every <see cref="IDocumentStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// Shared rather than private to one store, because the key is what the two have to agree
/// on. A practice moving from the local disk to a bucket copies the directory across and
/// the rows in the database are untouched — which only works if both stores would have
/// written the same key for the same upload. Two private copies of this rule is two copies
/// that can drift, and the drift would not show up until a file could not be found.
/// </para>
/// <para>
/// It builds a key; it never resolves one. Turning a key back into a path, and refusing one
/// that escapes its root, is each store's own business — see <c>LocalDocumentStore</c>,
/// where it is a security check and not a formatting one.
/// </para>
/// </remarks>
public static partial class DocumentKey
{
    /// <summary>
    /// Longest name kept from the original file.
    /// </summary>
    /// <remarks>
    /// Windows caps a full path at 260 characters by default, and a patient subdirectory
    /// plus a GUID plus a long scanner-generated filename reaches it, at which point the
    /// write fails with a path error that says nothing about the cause. Object storage has
    /// no such limit, but the keys stay identical across stores and that is worth more than
    /// a longer name in a bucket.
    /// </remarks>
    public const int MaxNameLength = 60;

    /// <summary>
    /// The key an upload is stored under.
    /// </summary>
    /// <remarks>
    /// The GUID is not decoration. Two patients uploading "scan.jpg" must not collide, and
    /// neither must the same patient uploading it twice — which is exactly what happened
    /// when the key was the sanitised name alone.
    /// </remarks>
    public static string For(Guid patientId, string fileName) =>
        $"patients/{patientId:n}/{Guid.NewGuid():n}-{Sanitise(fileName)}";

    /// <summary>
    /// Reduces an uploaded file name to something safe to put in a key.
    /// </summary>
    /// <remarks>
    /// Everything but letters, digits, dot, dash and underscore is replaced. A browser
    /// sends whatever the user's filesystem allowed — path separators, colons, control
    /// characters, right-to-left overrides — and none of it belongs in a key.
    /// </remarks>
    public static string Sanitise(string fileName)
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
