using DYS.Molargo.Shared.Data;

namespace DYS.Molargo.Web.Services;

/// <summary>
/// Where the SQLite file lives for the web head.
/// </summary>
/// <remarks>
/// <para>
/// The app is offline-only for now, so the web head runs against the same local SQLite
/// schema as the device heads rather than against an API that does not exist yet. That
/// makes this file server-side and shared by every browser session connected to this
/// process — which is fine for one practice on one machine, and is exactly what the
/// eventual API will replace.
/// </para>
/// <para>
/// The path is configurable through <c>Molargo:DatabasePath</c> so a deployment can put it
/// on a backed-up volume. It defaults under <see cref="Environment.SpecialFolder.LocalApplicationData"/>
/// rather than the content root, because the content root is replaced wholesale on every
/// deploy and the database would go with it.
/// </para>
/// </remarks>
public sealed class WebDatabasePathProvider : IDatabasePathProvider
{
    private const string FileName = "molargo.db3";

    private readonly string _path;

    public WebDatabasePathProvider(IConfiguration configuration)
    {
        var configured = configuration["Molargo:DatabasePath"];

        _path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Molargo",
                FileName)
            : configured;
    }

    public string GetDatabasePath()
    {
        // SQLite will not create a missing directory, and reports it as a bare "unable to
        // open database file" that names nothing.
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        return _path;
    }
}
