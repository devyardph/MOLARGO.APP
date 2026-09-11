using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Data;
using DYS.Molargo.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace DYS.Molargo.Shared.Features.Platform.Services;

/// <summary>
/// One subscribing clinic as the vendor's tenant list shows it.
/// </summary>
/// <param name="Staff">Active staff records, which is what a seat count means.</param>
/// <param name="Seats">
/// What the plan allows, or null where it is unmetered. Recorded, never enforced —
/// nothing in the app refuses a sign-in for being over it.
/// </param>
/// <param name="LastActivityUtc">
/// The most recent audit entry, which is the closest thing to "are they using it". Null
/// for a clinic that has never done anything.
/// </param>
public sealed record TenantRow(
    Guid TenantId,
    string Name,
    string Slug,
    string CountryCode,
    Guid? PlanId,
    string? PlanName,
    string? ContactEmail,
    int Staff,
    int Clinicians,
    int Sites,
    int Patients,
    int Appointments,
    DateOnly? SubscribedOn,
    DateTime? LastActivityUtc,
    bool IsActive,

    /// <summary>What the clinic pays, priced from the plan it is on. Null with no plan.</summary>
    PlanQuote? Quote,

    /// <summary>Days of free trial left, or null once it has ended or never ran.</summary>
    int? TrialDaysLeft,

    /// <summary>The last day of the trial, whether or not it has passed.</summary>
    DateOnly? TrialEndsOn)
{
    public bool IsInTrial => TrialDaysLeft is not null;

    /// <summary>
    /// Had a trial, and it has run out.
    /// </summary>
    /// <remarks>
    /// Worth its own answer: nothing switches off when a trial ends, so this is the set of
    /// clinics now using the app on a plan nobody has started billing.
    /// </remarks>
    public bool TrialLapsed => TrialEndsOn is not null && TrialDaysLeft is null;

    /// <summary>Seats included by the plan, or null where there is no plan.</summary>
    public int? Seats => Quote?.IncludedSeats;

    /// <summary>
    /// More clinicians than the plan's allowance.
    /// </summary>
    /// <remarks>
    /// No longer a limit being breached — the extras are priced, so this is a clinic that
    /// has grown into a bigger bill rather than one doing something wrong. Surfaced because
    /// it is a conversation the vendor should have, not because anything is blocked.
    /// </remarks>
    public bool IsOverSeats => Quote is { ExtraSeats: > 0 };

    /// <summary>A clinic with no plan chosen, which cannot be invoiced.</summary>
    public bool HasNoPlan => PlanId is null;

    /// <summary>
    /// A clinic that signed up and never put anything in.
    /// </summary>
    /// <remarks>
    /// Worth surfacing on its own: an empty tenant is either an abandoned trial or a
    /// migration that failed, and both are things the vendor should chase rather than
    /// count as a customer.
    /// </remarks>
    public bool IsEmpty => Patients == 0 && Appointments == 0;
}

/// <summary>
/// The vendor's view of the clinics subscribing to the app.
/// </summary>
/// <remarks>
/// <para>
/// The only service in the app that reads across the tenant boundary, and the only one
/// that may. Everything else works through the global query filter and cannot see another
/// clinic's rows even by accident; here the filter is bypassed deliberately, per query,
/// with <c>IgnoreQueryFilters</c>.
/// </para>
/// <para>
/// What it bypasses the filter for is deliberately narrow: <b>counts and dates, never
/// content</b>. The vendor can see that a clinic holds 4,120 patients and last did
/// something on Tuesday. It cannot read a patient, a note, a script or an invoice through
/// this service, because none of those are on <see cref="TenantRow"/> — and a clinic's
/// records are not the vendor's to read.
/// </para>
/// <para>
/// Every method checks the caller first, against the staff record rather than the
/// session's own claim. That check is the boundary — not the hidden nav link and not the
/// layout's redirect, both of which are only tidiness. A method reached without the role
/// returns a refusal or an empty list, so a screen opened by mistake shows nothing rather
/// than an error page.
/// </para>
/// </remarks>
public interface IPlatformService
{
    /// <summary>Every subscribing clinic. The vendor's own tenant is not one.</summary>
    Task<IReadOnlyList<TenantRow>> GetTenantsAsync(CancellationToken ct = default);

    Task<Tenant?> GetTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Creates or updates a clinic's subscription record. Returns a refusal, or null.</summary>
    Task<string?> SaveTenantAsync(Tenant tenant, CancellationToken ct = default);

    /// <summary>
    /// Suspends a clinic's subscription, or restores it.
    /// </summary>
    /// <remarks>
    /// Suspending is what the vendor has instead of deleting. It refuses every sign-in for
    /// that clinic — <c>AuthService</c> already checks <c>IsActive</c> — while leaving
    /// every row intact, which is the only defensible behaviour for a practice's clinical
    /// records at the end of a billing dispute.
    /// </remarks>
    Task<string?> SetTenantActiveAsync(
        Guid tenantId, bool isActive, CancellationToken ct = default);

    /// <summary>The most recent administrative actions at one clinic.</summary>
    /// <remarks>
    /// Read across the boundary so the vendor can answer "what happened to us on Friday"
    /// for a practice that asks. Audit details are staff-written prose about
    /// configuration, not clinical content.
    /// </remarks>
    Task<IReadOnlyList<string>> GetRecentActivityAsync(
        Guid tenantId, CancellationToken ct = default);
}

/// <inheritdoc cref="IPlatformService"/>
public sealed class PlatformService : IPlatformService
{
    /// <summary>Activity lines shown for one clinic.</summary>
    private const int ActivityLines = 12;

    /// <summary>The refusal, shared with the operator service so the two cannot drift.</summary>
    private const string NotPermitted = PlatformGuard.NotPermitted;

    private readonly MolargoDatabase _database;
    private readonly ISessionService _session;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public PlatformService(
        MolargoDatabase database,
        ISessionService session,
        ITenantContext tenant,
        IClock clock)
    {
        _database = database;
        _session = session;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<IReadOnlyList<TenantRow>> GetTenantsAsync(
        CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false)) return [];

        // The tenant table is the one the filter already skips, so this needs no bypass.
        var tenants = await db.Tenants
            .AsNoTracking()
            .Where(tenant => !tenant.IsDeleted && !tenant.IsPlatform)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Counts, grouped in one query each rather than a query per clinic. Filters
        // ignored on purpose — this is the whole point of the screen — and grouped to a
        // number, so no row of anybody's data is materialised.
        var staff = await db.Providers
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(provider => !provider.IsDeleted && provider.IsActive)
            .GroupBy(provider => provider.TenantId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, ct)
            .ConfigureAwait(false);

        // Clinicians counted separately, because they are the billable seats. The roles
        // are listed here rather than filtered through ProviderRoles.IsClinical so the
        // count happens in SQL — the predicate cannot be translated, and pulling every
        // provider row across every tenant into memory to test it is precisely the
        // cross-tenant read this service is careful not to do.
        var clinicians = await db.Providers
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(provider => !provider.IsDeleted
                && provider.IsActive
                && (provider.Role == ProviderRole.Dentist
                    || provider.Role == ProviderRole.Hygienist
                    || provider.Role == ProviderRole.OralHealthTherapist
                    || provider.Role == ProviderRole.DentalTherapist
                    || provider.Role == ProviderRole.Prosthetist
                    || provider.Role == ProviderRole.Specialist))
            .GroupBy(provider => provider.TenantId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, ct)
            .ConfigureAwait(false);

        var sites = await db.PracticeLocations
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(location => !location.IsDeleted && location.IsActive)
            .GroupBy(location => location.TenantId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, ct)
            .ConfigureAwait(false);

        // The plans every listed clinic is on. Read once and matched in memory rather
        // than joined per row: there are a handful of plans and they are the vendor's own.
        var plans = await db.Plans
            .AsNoTracking()
            .ToDictionaryAsync(plan => plan.Id, plan => plan, ct)
            .ConfigureAwait(false);

        var patients = await db.Patients
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(patient => !patient.IsDeleted)
            .GroupBy(patient => patient.TenantId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, ct)
            .ConfigureAwait(false);

        var appointments = await db.Appointments
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(appointment => !appointment.IsDeleted)
            .GroupBy(appointment => appointment.TenantId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, ct)
            .ConfigureAwait(false);

        var lastSeen = await db.AuditEntries
            .AsNoTracking()
            .IgnoreQueryFilters()
            .GroupBy(entry => entry.TenantId)
            .Select(group => new { group.Key, Last = group.Max(entry => entry.OccurredUtc) })
            .ToDictionaryAsync(row => row.Key, row => row.Last, ct)
            .ConfigureAwait(false);

        return tenants
            // Suspended first: they are the ones somebody has to decide about.
            .OrderBy(tenant => tenant.IsActive ? 1 : 0)
            .ThenBy(tenant => tenant.Name)
            .Select(tenant =>
            {
                var plan = tenant.PlanId is { } planId
                    ? plans.GetValueOrDefault(planId)
                    : null;

                var siteCount = sites.GetValueOrDefault(tenant.Id);
                var clinicianCount = clinicians.GetValueOrDefault(tenant.Id);

                return new TenantRow(
                    tenant.Id,
                    tenant.Name,
                    tenant.Slug,
                    tenant.CountryCode,
                    tenant.PlanId,
                    plan?.Name,
                    tenant.ContactEmail,
                    staff.GetValueOrDefault(tenant.Id),
                    clinicianCount,
                    siteCount,
                    patients.GetValueOrDefault(tenant.Id),
                    appointments.GetValueOrDefault(tenant.Id),
                    tenant.SubscribedOn,
                    lastSeen.TryGetValue(tenant.Id, out var seen) ? seen : null,
                    tenant.IsActive,

                    // Priced from the plan every time it is read, never stored on the
                    // tenant. A cached monthly figure would go stale the moment a clinic
                    // hired a hygienist, and the stale number is the one that gets
                    // invoiced.
                    plan is null ? null : Pricing.Quote(plan, siteCount, clinicianCount),
                    tenant.TrialDaysLeft(_clock.Today),
                    tenant.TrialEndsOn);
            })
            .ToList();
    }

    public async Task<Tenant?> GetTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false)) return null;

        return await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(tenant => tenant.Id == tenantId && !tenant.IsPlatform, ct)
            .ConfigureAwait(false);
    }

    public async Task<string?> SaveTenantAsync(
        Tenant tenant, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false)) return NotPermitted;

        if (string.IsNullOrWhiteSpace(tenant.Name)) return "A clinic needs a name.";

        var slug = (tenant.Slug ?? string.Empty).Trim().ToLowerInvariant();

        if (!IsUsableSlug(slug))
        {
            return "The clinic code has to be 3 to 40 characters of lowercase letters, "
                + "numbers and hyphens — \"apex-dental\".";
        }

        var country = (tenant.CountryCode ?? string.Empty).Trim().ToUpperInvariant();

        if (country.Length != 2 || !country.All(char.IsAsciiLetterUpper))
        {
            return "The billing country has to be a two-letter code — AU, NZ, GB.";
        }

        // Checked against the country, not just for existence. A clinic put on another
        // country's plan would be invoiced in the wrong currency at the wrong price, and
        // nothing downstream would notice.
        if (tenant.PlanId is { } chosenPlan)
        {
            var plan = await db.Plans
                .AsNoTracking()
                .FirstOrDefaultAsync(row => row.Id == chosenPlan, ct)
                .ConfigureAwait(false);

            if (plan is null) return "That plan no longer exists.";

            if (!string.Equals(plan.CountryCode, country, StringComparison.Ordinal))
            {
                return $"\"{plan.Name}\" is a {plan.CountryCode} plan and this clinic bills "
                    + $"in {country}. Choose a {country} plan, or change the billing "
                    + "country.";
            }
        }

        var existing = await db.Tenants
            .FirstOrDefaultAsync(row => row.Id == tenant.Id, ct)
            .ConfigureAwait(false);

        // The code is what a clinic types to sign in, so a duplicate would send one
        // practice's staff at another practice's records.
        var clash = await db.Tenants
            .AsNoTracking()
            .AnyAsync(row => row.Id != tenant.Id && row.Slug == slug && !row.IsDeleted, ct)
            .ConfigureAwait(false);

        if (clash) return $"The clinic code \"{slug}\" is already taken.";

        if (existing is not null && existing.IsPlatform)
        {
            return "That is the vendor's own tenant, not a subscribing clinic.";
        }

        var isNew = existing is null;
        var now = _clock.UtcNow;

        // Changing an existing clinic's code is refused rather than allowed. It is what
        // their staff type to sign in and what a future sync endpoint will address them
        // by; renaming it silently locks a practice out of its own data on Monday morning.
        if (existing is not null
            && !string.Equals(existing.Slug, slug, StringComparison.Ordinal))
        {
            return $"The clinic code cannot be changed once it is in use — "
                + $"{existing.Name} signs in with \"{existing.Slug}\".";
        }

        // The id is written back onto the caller's object for a new clinic, so whoever
        // asked for it can select what they just created. Minting a private one here left
        // the screen's "added" message pointing at a card it could not then show.
        if (tenant.Id == Guid.Empty) tenant.Id = Guid.NewGuid();

        var row = existing ?? new Tenant
        {
            Id = tenant.Id,
            Slug = slug,
            CreatedUtc = now,
            SubscribedOn = tenant.SubscribedOn ?? _clock.Today,
        };

        // Its own id, like every other tenant row. The tenant table is unfiltered, so this
        // is never read as a filter — but a zero here would be the one surprising value in
        // the column.
        row.TenantId = row.Id;
        row.Name = tenant.Name.Trim();
        row.Abn = Trim(tenant.Abn);
        row.ContactEmail = Trim(tenant.ContactEmail);
        row.ContactPhone = Trim(tenant.ContactPhone);
        row.CountryCode = country;
        row.PlanId = tenant.PlanId;
        row.UpdatedUtc = now;

        if (isNew)
        {
            row.IsActive = true;
            db.Tenants.Add(row);
        }
        else
        {
            row.SubscribedOn = tenant.SubscribedOn ?? row.SubscribedOn;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await AuditAsync(
            db,
            row.Id,
            isNew ? AuditAction.Created : AuditAction.Updated,
            isNew
                ? $"Subscription opened for {row.Name} (code \"{row.Slug}\")"
                : $"Subscription details updated for {row.Name}",
            ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string?> SetTenantActiveAsync(
        Guid tenantId, bool isActive, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false)) return NotPermitted;

        var tenant = await db.Tenants
            .FirstOrDefaultAsync(row => row.Id == tenantId, ct)
            .ConfigureAwait(false);

        if (tenant is null) return "That clinic no longer exists.";

        // The vendor's own tenant is the account doing the suspending. Suspending it would
        // lock every super admin out of the screen that could undo it.
        if (tenant.IsPlatform)
        {
            return "The vendor's own tenant cannot be suspended.";
        }

        if (tenant.IsActive == isActive)
        {
            return isActive
                ? $"{tenant.Name} is already active."
                : $"{tenant.Name} is already suspended.";
        }

        tenant.IsActive = isActive;
        tenant.UpdatedUtc = _clock.UtcNow;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await AuditAsync(
            db,
            tenant.Id,
            isActive ? AuditAction.Updated : AuditAction.Deleted,
            isActive
                ? $"Subscription restored for {tenant.Name} — staff can sign in again"
                : $"Subscription suspended for {tenant.Name} — every sign-in is refused, "
                    + "and no record has been deleted",
            ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<IReadOnlyList<string>> GetRecentActivityAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false)) return [];

        var entries = await db.AuditEntries
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(entry => entry.TenantId == tenantId)
            .OrderByDescending(entry => entry.OccurredUtc)
            .Take(ActivityLines)
            .Select(entry => new
            {
                entry.OccurredUtc,
                entry.ProviderName,
                entry.Detail,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return entries
            .Select(entry =>
                $"{entry.OccurredUtc.ToLocalTime():d MMM h:mm tt} · "
                + $"{entry.ProviderName ?? "unknown"} — {entry.Detail}")
            .ToList();
    }

    // ---- the boundary ----------------------------------------------------

    /// <summary>
    /// Whether the signed-in account really holds the platform role.
    /// </summary>
    /// <remarks>
    /// Read from the staff record on every call, not from <c>ISessionService</c>. The
    /// session's copy is set at sign-in and would keep saying "super admin" for the rest
    /// of a circuit after the role was taken away — which for this particular role means
    /// keeping the run of every clinic on the platform until the person closes their tab.
    ///
    /// The read needs no filter bypass: a super admin's own record lives in the platform
    /// tenant, which is the tenant their session is in.
    /// </remarks>
    private Task<bool> IsSuperAdminAsync(MolargoDbContext db, CancellationToken ct) =>
        PlatformGuard.IsSuperAdminAsync(db, _session.ProviderId, ct);

    // ---- helpers ---------------------------------------------------------

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// A clinic code that can safely be typed, and later put in a URL.
    /// </summary>
    /// <remarks>
    /// Lowercase, digits and hyphens only. Deliberately narrower than "not empty": this
    /// value is destined for a subdomain or a sync path, and a code with a space or a
    /// slash in it becomes somebody else's escaping bug later.
    /// </remarks>
    private static bool IsUsableSlug(string slug) =>
        slug.Length is >= 3 and <= 40
        && slug.All(character =>
            character is >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '-')
        && !slug.StartsWith('-')
        && !slug.EndsWith('-');

    /// <summary>
    /// Writes the entry into the clinic it happened to, not the vendor's tenant.
    /// </summary>
    /// <remarks>
    /// A suspension is the most consequential thing that can happen to a practice's
    /// access, and their own audit log is where they will look for it. The cost is that a
    /// super admin does not see their own actions in their own log — they read them
    /// through <see cref="GetRecentActivityAsync"/>, per clinic, which is how the question
    /// is actually asked.
    /// </remarks>
    private async Task AuditAsync(
        MolargoDbContext db,
        Guid tenantId,
        AuditAction action,
        string detail,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;

        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Action = action,
            EntityName = nameof(Tenant),
            EntityId = tenantId,
            ProviderId = _session.ProviderId,
            ProviderName = $"{_session.UserDisplayName ?? "unknown"} "
                + $"({_tenant.TenantName ?? "vendor"})",
            OccurredUtc = now,
            CreatedUtc = now,
            UpdatedUtc = now,
            DeviceId = await _database.GetDeviceIdAsync(ct).ConfigureAwait(false),
            Detail = detail,
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
