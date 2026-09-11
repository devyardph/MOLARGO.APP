namespace DYS.Molargo.Shared.Documents;

/// <summary>
/// Shows a stored file to the user.
/// </summary>
/// <remarks>
/// Interface only, because this is a real platform difference rather than a detail. A
/// browser opens a file by fetching a URL the server streams; a device opens it by handing
/// the path to the OS viewer. Neither approach works on the other head, so each supplies
/// its own — the same reasoning as <c>IFormFactor</c>.
/// </remarks>
public interface IDocumentViewer
{
    /// <summary>
    /// True where this head can open files at all. The documents tab hides its open
    /// action rather than offering one that does nothing.
    /// </summary>
    bool CanOpen { get; }

    /// <summary>
    /// Opens the document. <paramref name="documentId"/> rather than the key, so the head
    /// resolves through the store and an arbitrary key from the page cannot reach a file.
    /// </summary>
    Task OpenAsync(Guid documentId, CancellationToken ct = default);
}
