using DYS.Molargo.Shared.Services;
using Microsoft.AspNetCore.Components;

namespace DYS.Molargo.Web.Services;

/// <summary>
/// Links built from the address this app is actually being served from.
/// </summary>
/// <remarks>
/// <para>
/// Read from <see cref="NavigationManager.BaseUri"/> rather than from configuration, so a
/// practice running this behind its own hostname gets links to that hostname with nothing
/// to configure — and a developer on localhost gets localhost, which is what makes the
/// reset email testable at all.
/// </para>
/// <para>
/// Scoped, because <see cref="NavigationManager"/> is. Anything resolving this has to be
/// scoped too; a singleton holding it would capture the first circuit's base address and
/// hand it to every practice after.
/// </para>
/// </remarks>
public sealed class WebAppLinks : IAppLinks
{
    private readonly NavigationManager _navigation;

    public WebAppLinks(NavigationManager navigation) => _navigation = navigation;

    public string ResetPassword(string token) =>
        _navigation.ToAbsoluteUri(
            $"reset-password?token={Uri.EscapeDataString(token)}").ToString();

    /// <summary>
    /// Yes. This head is served from an address, and the browser that opens the link talks
    /// to the same database that issued the token.
    /// </summary>
    public bool SupportsEmailedLinks => true;
}
