using DYS.Molargo.Shared.Documents;
using DYS.Molargo.Shared.Features.Patient.Services;

namespace DYS.Molargo.Services;

/// <summary>
/// Opens a stored document with whatever app the device uses for that file type.
/// </summary>
/// <remarks>
/// <para>
/// Handing the file to the platform rather than rendering it in the WebView. A dental
/// practice's documents are PDFs, JPEGs and DICOM slices, and the OS viewers for those are
/// better than anything this app would build — particularly for a radiograph, where
/// pinch-zoom and windowing matter clinically.
/// </para>
/// <para>
/// The file is copied to the cache directory first. <c>Launcher</c> hands the path to
/// another app, and on both iOS and Android that app cannot read this one's private data
/// directory — so the copy is what makes the file reachable. The cache is the right place
/// for it: the OS may reclaim it, and the original is still in the document store.
/// </para>
/// </remarks>
public sealed class MauiDocumentViewer : IDocumentViewer
{
    private readonly IPatientService _patients;
    private readonly IDocumentStore _store;

    public MauiDocumentViewer(IPatientService patients, IDocumentStore store)
    {
        _patients = patients;
        _store = store;
    }

    public bool CanOpen => true;

    public async Task OpenAsync(Guid documentId, CancellationToken ct = default)
    {
        var document = await _patients.GetDocumentAsync(documentId, ct).ConfigureAwait(false);
        if (document is null) return;

        await using var source = await _store
            .OpenAsync(document.RelativePath, ct)
            .ConfigureAwait(false);

        if (source is null) return;

        // Named from the stored key, so the extension survives — it is what decides which
        // app the OS offers, and a file without one opens in nothing.
        var fileName = Path.GetFileName(document.RelativePath);
        var cached = Path.Combine(FileSystem.CacheDirectory, fileName);

        await using (var destination = File.Create(cached))
        {
            await source.CopyToAsync(destination, ct).ConfigureAwait(false);
        }

        await Launcher.Default
            .OpenAsync(new OpenFileRequest(document.Name, new ReadOnlyFile(cached)))
            .ConfigureAwait(false);
    }
}
