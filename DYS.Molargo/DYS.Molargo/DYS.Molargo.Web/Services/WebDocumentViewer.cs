using DYS.Molargo.Shared.Documents;
using Microsoft.AspNetCore.Components;

namespace DYS.Molargo.Web.Services;

/// <summary>
/// Opens a stored document in the browser by navigating to the endpoint that streams it.
/// </summary>
/// <remarks>
/// <para>
/// A navigation, not a JS-interop download. Handing a 25MB radiograph to the browser as a
/// base64 data URI over the SignalR circuit means encoding it, pushing it through the
/// connection, and holding all of it in memory on both ends. A plain GET streams.
/// </para>
/// <para>
/// <c>forceLoad</c> is required: the router would otherwise try to resolve
/// <c>/documents/{id}</c> as a Blazor page and find nothing.
/// </para>
/// </remarks>
public sealed class WebDocumentViewer : IDocumentViewer
{
    private readonly NavigationManager _navigation;

    public WebDocumentViewer(NavigationManager navigation) => _navigation = navigation;

    public bool CanOpen => true;

    public Task OpenAsync(Guid documentId, CancellationToken ct = default)
    {
        _navigation.NavigateTo($"documents/{documentId}", forceLoad: true);
        return Task.CompletedTask;
    }
}
