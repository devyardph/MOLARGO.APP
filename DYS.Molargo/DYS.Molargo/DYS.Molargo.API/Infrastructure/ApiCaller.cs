using System.Security.Claims;
using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Api.Infrastructure;

/// <summary>
/// The names the token uses for what it carries.
/// </summary>
/// <remarks>
/// Spelled out rather than reusing the framework's URI-shaped defaults. These end up in
/// every token this API issues, and a claim renamed by a framework upgrade would be a
/// silent authorisation failure — the claim simply absent, the caller simply unprivileged.
/// </remarks>
public static class MolargoClaims
{
    public const string TenantId = "molargo:tenant";

    public const string ProviderId = "molargo:provider";

    public const string LocationId = "molargo:location";

    public const string Role = "molargo:role";

    public const string Permissions = "molargo:permissions";

    public const string IsOwner = "molargo:owner";
}

/// <summary>
/// Who is making this request, read off their token.
/// </summary>
/// <remarks>
/// <para>
/// The API's counterpart to the heads' <c>ISessionService</c>, and deliberately not that
/// interface. A session is a thing that is signed in and out, holds a selected location
/// and raises change events — all of which describe a process serving one person. A
/// request has none of that: it arrives, it is already authenticated or it is not, and it
/// ends.
/// </para>
/// <para>
/// Every property is read from claims, never from the database. A token is the practice's
/// own statement about the caller, checked by signature — going back to the provider row
/// on every request would be a query per call for an answer that cannot have changed since
/// the token was issued, and the token's own lifetime is what bounds how stale it gets.
/// </para>
/// </remarks>
public interface IApiCaller
{
    bool IsAuthenticated { get; }

    Guid TenantId { get; }

    Guid ProviderId { get; }

    /// <summary>
    /// The caller's name, as the token carries it.
    /// </summary>
    /// <remarks>
    /// From the token rather than from the staff record. It is what the session shows as
    /// "signed in as", and a token names who was authenticated — re-reading the row would
    /// answer a slightly different question and would hide a stale token instead of showing
    /// it. Empty when the token carries no name, never null: it is rendered.
    /// </remarks>
    string DisplayName { get; }

    /// <summary>The site this caller works at, where the token names one.</summary>
    Guid LocationId { get; }

    ProviderRole Role { get; }

    PracticePermissions Permissions { get; }

    bool IsOwner { get; }

    /// <summary>Whether this caller may act in <paramref name="wanted"/>.</summary>
    /// <remarks>
    /// The same <see cref="PracticeAccess"/> rule the app enforces, not a second reading of
    /// it. One answer to "may they" across the device and the server is the only way the
    /// two cannot drift apart — and a permission tightened in one place and left open in
    /// the other is the drift that matters.
    /// </remarks>
    bool Can(PracticePermissions wanted);
}

/// <inheritdoc cref="IApiCaller"/>
public sealed class ApiCaller : IApiCaller
{
    private readonly ClaimsPrincipal _principal;

    public ApiCaller(IHttpContextAccessor accessor) =>
        _principal = accessor.HttpContext?.User ?? new ClaimsPrincipal();

    public bool IsAuthenticated => _principal.Identity?.IsAuthenticated == true;

    public Guid TenantId => Guid(MolargoClaims.TenantId);

    public Guid ProviderId => Guid(MolargoClaims.ProviderId);

    public string DisplayName => _principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty;

    public Guid LocationId => Guid(MolargoClaims.LocationId);

    public ProviderRole Role =>
        Enum.TryParse<ProviderRole>(_principal.FindFirstValue(MolargoClaims.Role), out var role)
            ? role
            : ProviderRole.Administration;

    public PracticePermissions Permissions =>
        Enum.TryParse<PracticePermissions>(
            _principal.FindFirstValue(MolargoClaims.Permissions), out var held)
            ? held
            : PracticePermissions.None;

    public bool IsOwner =>
        bool.TryParse(_principal.FindFirstValue(MolargoClaims.IsOwner), out var owner) && owner;

    public bool Can(PracticePermissions wanted) =>
        IsAuthenticated && PracticeAccess.Allows(IsOwner, Permissions, wanted);

    /// <remarks>
    /// Empty for a missing or unparseable claim, never an exception. An id that cannot be
    /// read is treated as no id, which every query filter then matches no rows for — the
    /// same fail-closed direction the tenant context takes.
    /// </remarks>
    private Guid Guid(string claim) =>
        System.Guid.TryParse(_principal.FindFirstValue(claim), out var value)
            ? value
            : System.Guid.Empty;
}
