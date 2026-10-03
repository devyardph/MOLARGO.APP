using DYS.Molargo.Services.Documents;
using DYS.Molargo.Services.Features.Patient;

namespace DYS.Molargo.Api.Endpoints;

/// <summary>
/// Downloading a stored patient document.
/// </summary>
/// <remarks>
/// <para>
/// Its own route rather than a method on the RPC surface, because the thing coming back is
/// a <see cref="Stream"/> and JSON has nowhere to put one. Uploading goes the other way
/// through the RPC layer's multipart path — see <c>RpcChannel.Package</c> — but that trick
/// does not reverse: a response has one body, and the body is already the JSON return value.
/// </para>
/// <para>
/// Keyed by document id, not by storage path. The path is the store's own business, and a
/// route that accepted one would be a route that accepts <c>../</c> from anybody with a
/// token. The id is looked up through the patient service, so the tenant filter applies and
/// a clinic cannot pull another clinic's file even knowing its id.
/// </para>
/// </remarks>
public sealed class DocumentsEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/documents").WithTags("Documents");

        group.MapGet("/{id:guid}", OpenAsync)
            .WithSummary("Streams one stored patient document.");
    }

    /// <summary>GET /documents/{id}</summary>
    private static async Task<IResult> OpenAsync(
        Guid id,
        IPatientService patients,
        IDocumentStore store,
        CancellationToken ct)
    {
        var document = await patients.GetDocumentAsync(id, ct);

        // 404 for another clinic's document as much as for one that does not exist: the
        // service simply does not see it. Answering "forbidden" would confirm the file
        // exists somewhere, which is the one thing a tenant boundary withholds.
        if (document is null) return Results.NotFound();

        var content = await store.OpenAsync(document.RelativePath, ct);

        // A row whose file is missing. Worth distinguishing in the log one day; to the
        // caller it is the same nothing.
        if (content is null) return Results.NotFound();

        // Inline with a download name, and range processing on: a browser shows a PDF or an
        // image in a tab and saves what it cannot render, and a 25MB radiograph should be
        // resumable rather than restarted.
        return Results.File(
            content,
            document.ContentType ?? "application/octet-stream",
            Path.GetFileName(document.RelativePath),
            enableRangeProcessing: true);
    }
}
