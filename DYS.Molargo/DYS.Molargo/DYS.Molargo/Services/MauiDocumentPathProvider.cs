using DYS.Molargo.Shared.Documents;

namespace DYS.Molargo.Services;

/// <summary>
/// Where a device keeps patient files.
/// </summary>
/// <remarks>
/// Under the same private, backed-up app directory as the database, in a <c>documents</c>
/// subdirectory. Not the cache directory: the OS is free to delete that under storage
/// pressure, and while the app is offline-only these files are the only copy.
/// </remarks>
public sealed class MauiDocumentPathProvider : IDocumentPathProvider
{
    public string GetDocumentRoot()
    {
        var root = Path.Combine(FileSystem.AppDataDirectory, "documents");
        Directory.CreateDirectory(root);
        return root;
    }
}
