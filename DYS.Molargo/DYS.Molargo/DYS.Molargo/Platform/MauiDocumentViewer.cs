using DYS.Molargo.Shared.Api;
using DYS.Molargo.Shared.Documents;
using DYS.Molargo.Services.Features.Patient;

namespace DYS.Molargo.Platform;

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
    private readonly IDocumentDownloader _documents;

    public MauiDocumentViewer(IDocumentDownloader documents) => _documents = documents;

    public bool CanOpen => true;

    public async Task OpenAsync(Guid documentId, CancellationToken ct = default)
    {
        // One call, where there used to be two. The lookup and the open both happen on the
        // server now — this device has no document store to open anything from.
        await using var file = await _documents
            .OpenAsync(documentId, ct)
            .ConfigureAwait(false);

        if (file is null) return;

        // The server's file name, so the extension survives — it is what decides which app
        // the OS offers, and a file without one opens in nothing.
        var cached = Path.Combine(FileSystem.CacheDirectory, file.FileName);

        await using (var destination = File.Create(cached))
        {
            await file.Content.CopyToAsync(destination, ct).ConfigureAwait(false);
        }

        await Launcher.Default
            .OpenAsync(new OpenFileRequest(file.FileName, new ReadOnlyFile(cached)))
            .ConfigureAwait(false);
    }
}
