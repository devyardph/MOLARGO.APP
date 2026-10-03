using DYS.Molargo.Domain.Data;
using DYS.Molargo.Services.Documents;

namespace DYS.Molargo.Api.Infrastructure;

/// <summary>
/// The answers a head would normally give, given by the server instead.
/// </summary>
/// <remarks>
/// The feature services ask two questions only a host can answer: which installation this
/// is, and where patient files are kept. On a device those were a platform's business; here
/// they are the server's, and these are the server's answers — registered rather than
/// stubbed out, because a service that resolved nothing would fail at the call rather than
/// at startup.
///
/// There used to be a third, "where is the local database file", answered with a path that
/// deliberately did not exist. It went when the SQLite store did: nothing asks any more.
/// </remarks>
/// <summary>
/// Which installation acted, for the audit trail, when the installation is a server.
/// </summary>
/// <remarks>
/// <para>
/// A device mints an id on first use and stores it, so an entry can be traced to a specific
/// tablet. A server has no equivalent and should not invent one per process: an id that
/// changed on every restart would make the audit column noise.
/// </para>
/// <para>
/// So it answers with the machine name, prefixed. The prefix is what matters — an auditor
/// reading a mixed trail can tell at a glance which entries were written by somebody holding
/// a tablet and which came through the API, and those are different facts about who did
/// something.
/// </para>
/// </remarks>
internal sealed class ServerDeviceIdentity : IDeviceIdentity
{
    private readonly string _id = $"api:{Environment.MachineName}";

    public Task<string> GetDeviceIdAsync(CancellationToken ct = default) => Task.FromResult(_id);
}

/// <summary>
/// Where patient files live on the server.
/// </summary>
/// <remarks>
/// Beside the application rather than in a temporary directory, because these are patient
/// records: a scan uploaded through the API has to still be there after a restart. The
/// directory is created on first use for the same reason the local store does it — the
/// failure otherwise is an unhelpful "could not find a part of the path" at the moment
/// somebody attaches a file.
/// </remarks>
internal sealed class ServerDocumentPathProvider : IDocumentPathProvider
{
    private readonly string _root;

    public ServerDocumentPathProvider(IConfiguration configuration, IHostEnvironment environment)
    {
        // Configurable, because on a real deployment this wants to be a mounted volume that
        // is backed up — and a path compiled into the binary is a path nobody can move.
        _root = configuration["Documents:Root"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(environment.ContentRootPath, "documents");
    }

    public string GetDocumentRoot()
    {
        Directory.CreateDirectory(_root);

        return _root;
    }
}
