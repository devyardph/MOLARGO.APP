using DYS.Molargo.Shared.Documents;

namespace DYS.Molargo.Web.Services;

/// <summary>
/// Where the web head keeps patient files while storage is local.
/// </summary>
/// <remarks>
/// <para>
/// Configurable through <c>Molargo:DocumentPath</c>, mirroring the database path. It
/// defaults under <see cref="Environment.SpecialFolder.LocalApplicationData"/>, beside
/// the SQLite file, rather than under the content root — the content root is replaced
/// wholesale on every deploy, and the patients' radiographs would go with it.
/// </para>
/// <para>
/// Point the setting into the repository if you want the files visible in the project
/// while developing; nothing else has to change. This whole provider disappears when the
/// Supabase bucket arrives, because a bucket-backed store has no local path at all.
/// </para>
/// </remarks>
public sealed class WebDocumentPathProvider : IDocumentPathProvider
{
    private readonly string _root;

    public WebDocumentPathProvider(IConfiguration configuration)
    {
        var configured = configuration["Molargo:DocumentPath"];

        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Molargo",
                "documents")
            : configured;
    }

    public string GetDocumentRoot()
    {
        Directory.CreateDirectory(_root);
        return _root;
    }
}
