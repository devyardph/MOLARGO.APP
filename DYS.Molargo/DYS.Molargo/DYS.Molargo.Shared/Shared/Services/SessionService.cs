using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Shared.Repositories;

namespace DYS.Molargo.Shared.Services;

/// <summary>
/// Who is signed in and which practice location they are working at.
/// </summary>
/// <remarks>
/// The location matters more than it looks: a multi-site practice shares one patient
/// record but keeps separate diaries, chairs, stock and takings, so almost every query is
/// scoped by it. Holding it here means a view model asks the session rather than threading
/// a location id through every call.
/// </remarks>
public interface ISessionService
{
    /// <summary>The signed-in staff member, or null before sign-in.</summary>
    string? UserDisplayName { get; }

    /// <summary>
    /// The signed-in clinician, for stamping what they record. Null before sign-in, and
    /// null for a non-clinical login — a receptionist cannot be the author of a clinical
    /// finding, so callers store null rather than substituting somebody.
    /// </summary>
    Guid? ProviderId { get; }

    /// <summary>
    /// The signed-in person's role. Null before sign-in.
    /// </summary>
    /// <remarks>
    /// Read from their staff record every time a session starts, including when one is
    /// restored from the host's cookie — never carried in the cookie itself. A role in a
    /// cookie is a claim the browser holds, and this one decides who may administer every
    /// clinic on the platform.
    /// </remarks>
    ProviderRole? UserRole { get; }

    /// <summary>Whether this is the vendor's operator rather than a clinic's staff.</summary>
    bool IsSuperAdmin { get; }

    /// <summary>
    /// Whether the signed-in person owns this practice.
    /// </summary>
    /// <remarks>
    /// Read from their staff record at sign-in, like <see cref="UserRole"/>, and never
    /// carried in the cookie — a cookie's contents are a claim the browser holds.
    /// </remarks>
    bool IsOwner { get; }

    /// <summary>The administrative areas the signed-in person may run.</summary>
    PracticePermissions Permissions { get; }

    /// <summary>
    /// Whether the signed-in person may act in <paramref name="wanted"/>.
    /// </summary>
    /// <remarks>
    /// For deciding what to render — a nav item, a New item button, a fee box. NOT the
    /// boundary: this reads a copy taken when the session started, so it keeps answering
    /// yes for the rest of a circuit after a permission is withdrawn. What actually refuses
    /// a write is <see cref="IPracticeGuard"/>, which re-reads the staff record every call.
    /// </remarks>
    bool Can(PracticePermissions wanted);

    /// <summary>Whether the Admin section is worth offering to this person at all.</summary>
    bool CanReachAdmin { get; }

    /// <summary>Two-letter monogram for the app bar avatar. Empty when nobody is signed in.</summary>
    string UserInitials { get; }

    /// <summary>The practice location currently selected.</summary>
    Guid LocationId { get; }

    string? LocationName { get; }

    /// <summary>
    /// The selected site's trading days and hours.
    /// </summary>
    /// <remarks>
    /// On the session because it is read on nearly every diary and booking screen and it
    /// changes only when the site does. The alternative — a lookup per call — would put a
    /// database read behind drawing one column of the diary grid.
    /// </remarks>
    PracticeHours Hours { get; }

    /// <summary>
    /// The sites this user may work at, in display order — normally just their own.
    /// </summary>
    /// <remarks>
    /// Scoped to the signed-in staff member's assigned site, so the app bar offers the one
    /// clinic they actually work at rather than every site the practice runs. Everything
    /// the shell filters by location reads this, which is the point: a hygienist at one
    /// surgery has no business browsing another's diary, chairs or takings by default.
    /// </remarks>
    IReadOnlyList<SessionLocation> Locations { get; }

    /// <summary>Every active site in the clinic, whoever works there.</summary>
    /// <remarks>
    /// For administration, not for working in — assigning a staff member to a site is
    /// exactly the case where the list cannot be limited to sites they are already at.
    /// Kept separate from <see cref="Locations"/> rather than widening it, because that
    /// list is what scopes the diary and the day's takings.
    /// </remarks>
    IReadOnlyList<SessionLocation> AllLocations { get; }

    bool IsSignedIn { get; }

    /// <summary>Raised when the user or the selected location changes, so the shell re-renders.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Loads the locations and settles on one. Idempotent, and safe to call from every
    /// screen's initialise — the first caller does the work and the rest see the result.
    /// </summary>
    Task EnsureLoadedAsync(CancellationToken ct = default);

    /// <summary>
    /// Re-reads the sites after they have been edited.
    /// </summary>
    /// <remarks>
    /// <see cref="EnsureLoadedAsync"/> is idempotent by design, so it will not pick up a
    /// site that was just added or renamed. Without this, adding a surgery in Admin leaves
    /// the app bar and every site picker showing the old list until the next sign-in — the
    /// change appears to have been lost.
    /// </remarks>
    Task ReloadLocationsAsync(CancellationToken ct = default);

    void SelectLocation(Guid locationId);

    /// <summary>
    /// Starts the session for a verified staff member.
    /// </summary>
    /// <remarks>
    /// Takes the provider's id as well as their name, and only <c>AuthService</c> calls it
    /// after checking a password. It used to take a display name alone, which made the
    /// session something any caller could assert rather than something a credential
    /// established.
    /// </remarks>
    Task SignInAsync(
        Guid providerId,
        string displayName,
        ProviderRole role,
        bool isOwner,
        PracticePermissions permissions,
        CancellationToken ct = default);

    Task SignOutAsync(CancellationToken ct = default);
}

/// <summary>A location as the app bar needs it: enough to render and to switch to.</summary>
/// <param name="Hours">
/// The site's trading pattern, carried so switching sites switches the diary's day with
/// it rather than needing a second read.
/// </param>
public sealed record SessionLocation(Guid Id, string Name, PracticeHours Hours);

/// <summary>
/// In-memory session.
/// </summary>
/// <remarks>
/// <para>
/// Scoped, so one per circuit on the web head and one for the process lifetime on a
/// device — the right shape for both, since a web session ends with the circuit and a
/// device stays signed in.
/// </para>
/// <para>
/// Holds no credential and persists nothing of its own. A password is checked by
/// <c>AuthService</c>, which then calls <see cref="SignInAsync"/>; surviving a page refresh
/// is the web head's cookie, not this. Which means the session is authoritative about who
/// is signed in for the life of the circuit, and about nothing beyond it.
/// </para>
/// </remarks>
public sealed class SessionService : ISessionService
{
    private readonly IRepository<PracticeLocation> _locations;
    private readonly IRepository<Provider> _providers;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _loaded;

    public SessionService(IRepository<PracticeLocation> locations, IRepository<Provider> providers)
    {
        _locations = locations;
        _providers = providers;
    }

    public string? UserDisplayName { get; private set; }

    public Guid? ProviderId { get; private set; }

    public ProviderRole? UserRole { get; private set; }

    public bool IsOwner { get; private set; }

    public PracticePermissions Permissions { get; private set; } = PracticePermissions.None;

    public bool Can(PracticePermissions wanted) =>
        PracticeAccess.Allows(IsOwner, Permissions, wanted);

    public bool CanReachAdmin => PracticeAccess.CanReachAdmin(IsOwner, Permissions);

    public bool IsSuperAdmin =>
        UserRole is { } role && ProviderRoles.IsPlatform(role);

    public Guid LocationId { get; private set; }

    public string? LocationName =>
        Selected?.Name;

    public PracticeHours Hours => Selected?.Hours ?? PracticeHours.Default;

    /// <summary>
    /// The chosen site, looked up in the full list rather than the user's own.
    /// </summary>
    /// <remarks>
    /// AllLocations, not Locations: a super admin browsing a clinic they do not work at
    /// still has to see that clinic's hours, and scoping the lookup would have handed them
    /// the defaults instead.
    /// </remarks>
    private SessionLocation? Selected =>
        AllLocations.FirstOrDefault(location => location.Id == LocationId);

    public IReadOnlyList<SessionLocation> Locations { get; private set; } = [];

    public IReadOnlyList<SessionLocation> AllLocations { get; private set; } = [];

    public bool IsSignedIn => UserDisplayName is not null;

    public string UserInitials
    {
        get
        {
            if (UserDisplayName is null) return string.Empty;

            // Titles are not initials: "Dr Vance" should read "RV" from the person's
            // name, not "DV" from their honorific.
            var parts = UserDisplayName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(part => part is not ("Dr" or "Dr." or "Mr" or "Mrs" or "Ms" or "Prof"))
                .ToList();

            return parts.Count switch
            {
                0 => string.Empty,
                1 => parts[0][..1].ToUpperInvariant(),
                _ => $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant(),
            };
        }
    }

    public event EventHandler? Changed;

    public async Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (_loaded) return;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_loaded) return;

            var rows = await _locations
                .ListAsync(location => location.IsActive, ct)
                .ConfigureAwait(false);

            AllLocations = rows
                .OrderBy(location => location.DisplayOrder)
                .ThenBy(location => location.Name)
                .Select(location => new SessionLocation(
                    location.Id,
                    location.ShortName ?? location.Name,
                    location.Hours))
                .ToList();

            Locations = await ScopeToUserAsync(ct).ConfigureAwait(false);

            // The selected site has to be one this user may work at. Checked rather than
            // only defaulted, because a site can be selected before sign-in and the next
            // person to sign in at this device may not work there.
            if (Locations.Count > 0 && Locations.All(location => location.Id != LocationId))
            {
                LocationId = Locations[0].Id;
            }

            // No stand-in user any more.
            //
            // This used to sign the app in as the practice's first clinician so the shell
            // had a name in it. That is now what the sign-in screen is for, and filling it
            // in here would mean the app was usable without a password — the guard on the
            // layout would pass because somebody was always "signed in".
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task ReloadLocationsAsync(CancellationToken ct = default)
    {
        _loaded = false;

        return EnsureLoadedAsync(ct);
    }

    /// <summary>
    /// The sites the signed-in staff member may work at.
    /// </summary>
    /// <remarks>
    /// Their assigned site alone where they have one. Where they do not, every site — not
    /// as a convenience but because the alternative is worse: nearly every screen filters
    /// by location, so a session with no site shows an empty diary, empty stock and no
    /// takings, and reads as a broken app rather than an unassigned staff member. The Users
    /// pane is where that gets fixed, and it shows who has no site.
    /// </remarks>
    private async Task<IReadOnlyList<SessionLocation>> ScopeToUserAsync(CancellationToken ct)
    {
        if (ProviderId is not { } providerId) return AllLocations;

        var provider = await _providers.GetByIdAsync(providerId, ct).ConfigureAwait(false);

        if (provider?.PrimaryLocationId is not { } siteId) return AllLocations;

        var assigned = AllLocations.Where(location => location.Id == siteId).ToList();

        // Falls back where the assigned site has been closed or removed. Showing nothing
        // would strand the user with no location at all.
        return assigned.Count > 0 ? assigned : AllLocations;
    }

    public void SelectLocation(Guid locationId)
    {
        if (locationId == LocationId) return;

        // Refused rather than trusted. The app bar only offers sites this user may work
        // at, so anything else arrived from stale markup or a hand-made call.
        if (Locations.All(location => location.Id != locationId)) return;

        LocationId = locationId;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SignInAsync(
        Guid providerId,
        string displayName,
        ProviderRole role,
        bool isOwner,
        PracticePermissions permissions,
        CancellationToken ct = default)
    {
        UserDisplayName = displayName;
        ProviderId = providerId;
        UserRole = role;
        IsOwner = isOwner;
        Permissions = permissions;

        // Locations are loaded after the identity, not before. They are tenant-scoped, and
        // until sign-in has set the tenant the list reads empty — so a session that loaded
        // them first would show a location picker with nothing in it.
        _loaded = false;
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
    }

    public Task SignOutAsync(CancellationToken ct = default)
    {
        UserDisplayName = null;
        ProviderId = null;
        UserRole = null;

        // Cleared with the identity, not left behind. A device shared by the front desk
        // signs one person out and the next one in, and a stale "owner" here would have
        // decided what the next person's shell offered them.
        IsOwner = false;
        Permissions = PracticePermissions.None;

        // The location list goes too. It belongs to the clinic that was signed in, and
        // leaving it behind would show the next person at this device the previous
        // clinic's sites before they had authenticated.
        Locations = [];
        AllLocations = [];
        LocationId = Guid.Empty;
        _loaded = false;

        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}
