using System.Net;
using System.Net.Http.Headers;

namespace DYS.Molargo.Shared.Api;

/// <summary>One stored document, as it comes off the server.</summary>
/// <param name="Content">Dispose it when done — it is the live response body, not a copy.</param>
public sealed record DownloadedDocument(Stream Content, string FileName, string? ContentType)
    : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

/// <summary>
/// Fetches a stored patient document from the server.
/// </summary>
/// <remarks>
/// <para>
/// This exists because <c>IDocumentStore</c> no longer does, on a head. That interface was
/// "where the files are kept", and on a device the answer used to be a folder on its disk.
/// It is the server now, and a head keeping its own copy would be patient files at rest on
/// a tablet — exactly what moving the records off it was for.
/// </para>
/// <para>
/// Narrower than the store it replaces: open, and nothing else. Saving a document goes the
/// other way, through <c>IPatientService.AddDocumentAsync</c> and the RPC layer's multipart
/// path, where the service applies the rules about who may attach what to whose record. A
/// head that could write to the store directly would be a head that could step around them.
/// </para>
/// </remarks>
public interface IDocumentDownloader
{
    /// <summary>
    /// The document's bytes, or null when there is no such document for this clinic.
    /// </summary>
    Task<DownloadedDocument?> OpenAsync(Guid documentId, CancellationToken ct = default);
}

/// <inheritdoc cref="IDocumentDownloader"/>
public sealed class ApiDocumentDownloader : IDocumentDownloader
{
    private readonly HttpClient _http;
    private readonly IApiTokenStore _tokens;

    public ApiDocumentDownloader(HttpClient http, IApiTokenStore tokens)
    {
        _http = http;
        _tokens = tokens;
    }

    public async Task<DownloadedDocument?> OpenAsync(
        Guid documentId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/documents/{documentId}");

        if (_tokens.Token is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage response;

        try
        {
            // ResponseHeadersRead, so the body is not buffered into memory before this
            // returns. A radiograph is tens of megabytes and the caller is about to copy it
            // straight to a file or a browser — holding the whole thing first is the
            // difference between a download and an out-of-memory on a tablet.
            response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ApiCallException(
                ApiCallException.Unreachable(_http.BaseAddress, ex), inner: ex);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();

            return null;
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();

            throw new ApiCallException(ApiCallException.Expired, unauthorised: true);
        }

        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();

            throw new ApiCallException(
                $"The document could not be fetched ({(int)response.StatusCode}).");
        }

        // The name the server set, which carries the extension — and the extension is what
        // decides which app the device offers to open it with. A file without one opens in
        // nothing.
        var fileName =
            response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? documentId.ToString();

        var content = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        return new DownloadedDocument(
            content, fileName, response.Content.Headers.ContentType?.MediaType);
    }
}
