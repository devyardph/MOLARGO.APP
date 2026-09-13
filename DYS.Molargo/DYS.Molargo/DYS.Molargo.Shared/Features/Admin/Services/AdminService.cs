using System.Globalization;
using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Components;
using DYS.Molargo.Shared.Data;
using DYS.Molargo.Shared.Entities;
using DYS.Molargo.Shared.Repositories;
using DYS.Molargo.Shared.Services;
using Microsoft.EntityFrameworkCore;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Features.Admin.Services;

/// <summary>
/// What a password reset did.
/// </summary>
/// <param name="Refusal">Non-null where nothing happened at all.</param>
/// <param name="Emailed">True where the notice reached the account's own address.</param>
/// <param name="MailProblem">
/// Why it did not, where it did not. Reported rather than swallowed: the administrator has
/// to know whether to go and tell the person themselves, and "no mail account is set up"
/// and "the server rejected it" call for different next steps.
/// </param>
public sealed record PasswordResetResult(
    string? Refusal, bool Emailed, string? MailProblem)
{
    public static PasswordResetResult Refused(string why) => new(why, false, null);
}

/// <summary>One staff member as the users list shows them.</summary>
public sealed record StaffRow(
    Guid ProviderId,
    string Name,
    string? Email,
    ProviderRole Role,
    string? PrimarySite,
    bool IsActive,
    bool IsClinical,
    string? ProviderNumber,
    bool IsOwner,
    PracticePermissions Permissions)
{
    /// <summary>How many administrative areas this person runs, owners included.</summary>
    /// <remarks>
    /// Owners report every area rather than their stored flags, because that is what
    /// they can actually do. The list is a summary, and a summary that said "none" for
    /// the person who runs the practice would be wrong in the way that matters.
    /// </remarks>
    public int AdminAreaCount => IsOwner
        ? PracticeAccess.Grantable.Length
        : PracticeAccess.Grantable.Count(area => Permissions.HasFlag(area));
}

/// <summary>One location, with what is actually set up at it.</summary>
/// <summary>
/// One consent template in the Admin list.
/// </summary>
/// <param name="InUse">
/// How many signed consents were raised from this category's wording. Shown because it is
/// the number that makes an edit feel different: changing wording nobody has signed is
/// housekeeping, changing wording behind four hundred signatures is not — even though
/// neither touches a consent already given.
/// </param>
public sealed record ConsentTemplateRow(
    Guid TemplateId,
    string Name,
    string? Category,
    bool IsActive,
    int InUse);

public sealed record SiteRow(
    Guid LocationId,
    string Name,
    string? ShortName,
    string? Address,
    string? Phone,
    string? Abn,
    string? TimeZoneId,
    int Chairs,
    int ChairsOutOfService,
    int Providers,
    bool IsPrimary,
    bool IsActive,
    PracticeHours Hours)
{
    /// <summary>"Mon, Tue, Wed, Thu, Fri, Sat · 08:00-18:00" — this site's own.</summary>
    public string TradingHours => Hours.Summary;

    /// <summary>
    /// A site with no working chair, which takes no bookings.
    /// </summary>
    /// <remarks>
    /// Surfaced rather than refused at save time: a new site legitimately exists for a
    /// minute before its first chair is added. It is the diary that would silently show an
    /// empty day, so the card says it instead.
    /// </remarks>
    public bool HasNoChairs => Chairs == 0;
}

/// <summary>One chair at a site, and whether it can be taken out of service.</summary>
/// <param name="UpcomingAppointments">
/// Bookings still ahead in this chair, counted from the appointments themselves. Never a
/// stored total: the number exists to refuse a change that would orphan real bookings, so
/// a stale one would let exactly the thing through that it is there to stop.
/// </param>
public sealed record ChairRow(
    Guid OperatoryId,
    Guid LocationId,
    string Name,
    int DisplayOrder,
    bool IsSurgical,
    bool IsActive,
    int UpcomingAppointments);

/// <summary>A clinician's registration, and whether it still stands.</summary>
public sealed record RegistrationRow(
    Guid ProviderId,
    string Name,
    ProviderRole Role,
    string? AhpraNumber,
    DateOnly? ExpiresOn,
    int? DaysRemaining)
{
    public bool IsMissing => string.IsNullOrWhiteSpace(AhpraNumber);

    /// <summary>No expiry recorded. Not the same as current — nobody has checked.</summary>
    public bool ExpiryUnknown => !IsMissing && ExpiresOn is null;

    public bool IsExpired => DaysRemaining is < 0;

    /// <summary>Inside the window AHPRA itself starts reminding at.</summary>
    public bool IsExpiringSoon => DaysRemaining is >= 0 and <= AdminService.RegistrationWarningDays;

    /// <summary>
    /// Whether this clinician can lawfully sign. Missing or expired blocks both.
    /// </summary>
    /// <remarks>
    /// The same question the medical-certificate guard already asks, which is why it
    /// refuses for a provider with no AHPRA number. Expiry extends it: a number that has
    /// run out is no better than none.
    /// </remarks>
    public bool CanSign => !IsMissing && !IsExpired;
}

/// <summary>One line of the audit trail.</summary>
public sealed record AuditRow(
    Guid EntryId,
    AuditAction Action,
    string EntityName,
    Guid? EntityId,
    string? ProviderName,
    string? PatientName,
    DateTime OccurredUtc,
    string? DeviceId,
    string? Detail);

/// <summary>Where the data actually lives, and how much of it there is.</summary>
public sealed record DatabaseInfo(
    string Path,

    /// <summary>
    /// The whole database on disk: the main file plus its write-ahead log.
    /// </summary>
    /// <remarks>
    /// Both, because SQLite in WAL mode leaves recent writes in the sidecar file until a
    /// checkpoint. Reporting the main file alone showed 4 KB for a database holding
    /// twenty-five patients with 2 MB in the log beside it — a figure that reads as an
    /// empty database, next to a panel counting the records in it.
    /// </remarks>
    long SizeBytes,
    string DeviceId,
    int SchemaVersion,
    DateTime? FileModifiedUtc,
    int Patients,
    int Appointments,
    int Invoices,
    int AuditEntries,
    string? LastBackupPath,
    DateTime? LastBackupUtc)
{
    public decimal SizeMb => Math.Round(SizeBytes / 1024m / 1024m, 2);

    /// <summary>The folder the file sits in, which is what a person needs to find it.</summary>
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? Path;
}

/// <summary>What a backup attempt produced.</summary>
public sealed record BackupResult(string? Path, long SizeBytes, string? Refusal)
{
    public bool Succeeded => Refusal is null && Path is not null;

    public decimal SizeMb => Math.Round(SizeBytes / 1024m / 1024m, 2);
}

/// <summary>
/// Staff, sites, registrations, the audit trail and the local database.
/// </summary>
/// <remarks>
/// <para>
/// Every write here records an audit entry, which is the design's own promise about this
/// screen — "all actions below are themselves audit-logged". It is true of these actions
/// and of nothing else in the app yet, and the audit pane says so rather than letting an
/// empty log read as a quiet practice.
/// </para>
/// <para>
/// There is no authentication anywhere in Molargo: no passwords, no MFA, no SSO, no
/// sessions and no devices. The security and roles panes therefore report an absence
/// rather than a configuration.
/// </para>
/// </remarks>
public interface IAdminService
{
    // ---- staff -----------------------------------------------------------

    Task<IReadOnlyList<StaffRow>> GetStaffAsync(CancellationToken ct = default);

    Task<Provider?> GetStaffMemberAsync(Guid providerId, CancellationToken ct = default);

    /// <summary>Creates or updates a staff member. Returns a refusal, or null.</summary>
    Task<string?> SaveStaffAsync(Provider provider, CancellationToken ct = default);

    /// <summary>
    /// Deactivates or reactivates a staff member.
    /// </summary>
    /// <remarks>
    /// Never deletes. Clinical notes, prescriptions, invoices and audit entries stay
    /// attributed to the person who made them — a deleted author turns a signed note into
    /// an anonymous one, which is worse than a disabled login.
    /// </remarks>
    /// <summary>
    /// Sets a staff member's password and clears any lockout.
    /// </summary>
    /// <remarks>
    /// The practice's own recovery route for a forgotten password. Needs
    /// <c>ManageStaff</c>, because whoever can set a password can sign in as that person.
    /// </remarks>
    Task<PasswordResetResult> SetStaffPasswordAsync(
        Guid providerId, string password, CancellationToken ct = default);

    Task<string?> SetStaffActiveAsync(
        Guid providerId, bool isActive, CancellationToken ct = default);

    // ---- consent templates -----------------------------------------------

    /// <summary>Every template, inactive ones included, so one can be brought back.</summary>
    Task<IReadOnlyList<ConsentTemplateRow>> GetConsentTemplatesAsync(
        CancellationToken ct = default);

    Task<ConsentTemplate?> GetConsentTemplateAsync(
        Guid templateId, CancellationToken ct = default);

    /// <summary>Creates or updates a template. Returns a refusal, or null.</summary>
    Task<string?> SaveConsentTemplateAsync(
        ConsentTemplate template, CancellationToken ct = default);

    /// <summary>
    /// Retires a template, or brings it back.
    /// </summary>
    /// <remarks>
    /// Never deletes. A consent signed last year was signed against wording, and a record
    /// of what somebody agreed to that no longer has the words in it is not a record of
    /// anything. Retiring stops it being offered and leaves the history intact.
    /// </remarks>
    Task<string?> SetConsentTemplateActiveAsync(
        Guid templateId, bool isActive, CancellationToken ct = default);

    /// <summary>The schedule categories a template can be attached to.</summary>
    Task<IReadOnlyList<string>> GetProcedureCategoriesAsync(CancellationToken ct = default);

    // ---- sites -----------------------------------------------------------

    /// <summary>Every site, closed ones included, so a closure can be undone.</summary>
    Task<IReadOnlyList<SiteRow>> GetSitesAsync(CancellationToken ct = default);

    Task<PracticeLocation?> GetSiteAsync(Guid locationId, CancellationToken ct = default);

    /// <summary>Creates or updates a site. Returns a refusal, or null.</summary>
    Task<string?> SaveSiteAsync(PracticeLocation site, CancellationToken ct = default);

    /// <summary>
    /// Closes or reopens a site.
    /// </summary>
    /// <remarks>
    /// Closes rather than deletes, like a staff member: appointments, invoices and
    /// charting all record the site they happened at, and removing the row turns that
    /// history into a dangling id.
    /// </remarks>
    Task<string?> SetSiteActiveAsync(
        Guid locationId, bool isActive, CancellationToken ct = default);

    // ---- chairs ----------------------------------------------------------

    /// <summary>The chairs at one site, in diary order.</summary>
    Task<IReadOnlyList<ChairRow>> GetChairsAsync(
        Guid locationId, CancellationToken ct = default);

    Task<Operatory?> GetChairAsync(Guid operatoryId, CancellationToken ct = default);

    Task<string?> SaveChairAsync(Operatory chair, CancellationToken ct = default);

    /// <summary>Takes a chair out of service, or puts it back.</summary>
    Task<string?> SetChairActiveAsync(
        Guid operatoryId, bool isActive, CancellationToken ct = default);

    /// <summary>
    /// Moves a chair one place left or right in the diary.
    /// </summary>
    /// <param name="delta">-1 to move it left, +1 right.</param>
    /// <remarks>
    /// A swap with the neighbour rather than a typed-in number. Staff learn the diary by
    /// column position, and an editable order field lets two chairs hold the same one —
    /// after which the columns reorder themselves on whatever the sort falls back to.
    /// </remarks>
    Task<string?> MoveChairAsync(
        Guid operatoryId, int delta, CancellationToken ct = default);

    // ---- registrations ---------------------------------------------------

    Task<IReadOnlyList<RegistrationRow>> GetRegistrationsAsync(CancellationToken ct = default);

    Task<string?> SaveRegistrationAsync(
        Guid providerId,
        string? ahpraNumber,
        DateOnly? expiresOn,
        CancellationToken ct = default);

    // ---- audit and data --------------------------------------------------

    /// <summary>The trail, newest first. Null action means everything.</summary>
    /// <summary>
    /// One page of the audit log, newest first.
    /// </summary>
    /// <remarks>
    /// Paged rather than capped. It used to read every entry and take the newest two
    /// hundred, which put the older ones out of reach of the screen entirely — and an audit
    /// trail that cannot reach its own history is the one thing an audit trail must not be.
    /// A cap is also indistinguishable from "that is all there is" to whoever is reading.
    /// </remarks>
    Task<PagedResult<AuditRow>> GetAuditAsync(
        AuditAction? action = null,
        int page = 0,
        CancellationToken ct = default);

    Task<DatabaseInfo> GetDatabaseInfoAsync(CancellationToken ct = default);

    /// <summary>
    /// Writes a consistent snapshot of the database beside the live file.
    /// </summary>
    /// <remarks>
    /// Local only. There is no offsite copy and no schedule — this is a file on the same
    /// machine, which protects against a corrupt database and not against losing the
    /// machine, and the pane has to say which.
    /// </remarks>
    Task<BackupResult> BackupAsync(CancellationToken ct = default);
}

/// <inheritdoc cref="IAdminService"/>
public sealed class AdminService : IAdminService
{
    /// <summary>
    /// The shortest password an administrator may set for a staff member.
    /// </summary>
    /// <remarks>
    /// Ten, against the twelve a vendor operator needs: a clinic account reaches one
    /// practice's records, a platform one reaches every practice's subscription. Length
    /// rather than a character-class rule — the hash is PBKDF2 at 210,000 iterations, and
    /// against that a longer passphrase is worth more than a mandated punctuation mark.
    /// </remarks>
    private const int MinimumStaffPasswordLength = 10;

    /// <summary>
    /// How far ahead a lapsing registration is flagged.
    /// </summary>
    /// <remarks>
    /// Ninety days, matching the first of AHPRA's own reminders. Earlier than that and the
    /// warning sits on screen for a quarter and stops being read.
    /// </remarks>
    public const int RegistrationWarningDays = 90;

    /// <summary>Entries the audit pane shows before it stops being readable.</summary>
    /// <summary>
    /// Rows per page of the audit log.
    /// </summary>
    /// <remarks>
    /// Fifty, down from the two hundred this was as a cap. A cap wants to be large enough
    /// to hold everything interesting; a page wants to be small enough to read, because
    /// there is now a way to reach the next one.
    /// </remarks>
    private const int AuditPageSize = 50;


    private readonly IRepository<Provider> _providers;
    private readonly IRepository<PracticeLocation> _locations;
    private readonly IRepository<Operatory> _operatories;
    private readonly IRepository<AuditEntry> _audit;
    private readonly IRepository<PatientEntity> _patients;
    private readonly IRepository<Appointment> _appointments;
    private readonly IRepository<Invoice> _invoices;
    private readonly MolargoDatabase _database;
    private readonly IDatabasePathProvider _path;
    private readonly ISessionService _session;
    private readonly IPracticeGuard _guard;
    private readonly IPasswordHasher _hasher;
    private readonly IRepository<Tenant> _tenants;
    private readonly IRepository<MessageTemplate> _templates;
    private readonly IRepository<ConsentTemplate> _consentTemplates;
    private readonly IRepository<ConsentForm> _consents;
    private readonly IRepository<ProcedureCode> _codes;
    private readonly INotificationSettingsService _notifications;
    private readonly IEmailSender _email;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public AdminService(
        IRepository<Provider> providers,
        IRepository<PracticeLocation> locations,
        IRepository<Operatory> operatories,
        IRepository<AuditEntry> audit,
        IRepository<PatientEntity> patients,
        IRepository<Appointment> appointments,
        IRepository<Invoice> invoices,
        MolargoDatabase database,
        IDatabasePathProvider path,
        ISessionService session,
        IPracticeGuard guard,
        IPasswordHasher hasher,
        IRepository<Tenant> tenants,
        IRepository<MessageTemplate> templates,
        IRepository<ConsentTemplate> consentTemplates,
        IRepository<ConsentForm> consents,
        IRepository<ProcedureCode> codes,
        INotificationSettingsService notifications,
        IEmailSender email,
        ITenantContext tenant,
        IClock clock)
    {
        _providers = providers;
        _locations = locations;
        _operatories = operatories;
        _audit = audit;
        _patients = patients;
        _appointments = appointments;
        _invoices = invoices;
        _database = database;
        _path = path;
        _session = session;
        _guard = guard;
        _hasher = hasher;
        _consentTemplates = consentTemplates;
        _consents = consents;
        _codes = codes;
        _tenants = tenants;
        _templates = templates;
        _notifications = notifications;
        _email = email;
        _tenant = tenant;
        _clock = clock;
    }

    // ---- staff -----------------------------------------------------------

    public async Task<IReadOnlyList<StaffRow>> GetStaffAsync(CancellationToken ct = default)
    {
        var providers = await _providers.ListAsync(ct: ct).ConfigureAwait(false);
        var locations = await _locations.ListAsync(ct: ct).ConfigureAwait(false);

        var names = new Dictionary<Guid, string>();

        foreach (var location in locations)
        {
            names[location.Id] = location.ShortName ?? location.Name;
        }

        return providers
            // Active first, then by surname. A disabled login sorted among the current
            // staff is how somebody gets asked to cover a shift they cannot sign for.
            .OrderBy(provider => provider.IsActive ? 0 : 1)
            .ThenBy(provider => provider.LastName)
            .ThenBy(provider => provider.FirstName)
            .Select(provider => new StaffRow(
                provider.Id,
                provider.FullName,
                provider.Email,
                provider.Role,
                // A provider need not have a site: the field is nullable, and "not set" is
                // a different answer from "a site we have no name for".
                provider.PrimaryLocationId is { } siteId
                    ? names.GetValueOrDefault(siteId)
                    : null,
                provider.IsActive,
                ProviderRoles.IsClinical(provider.Role),
                provider.ProviderNumber,
                provider.IsOwner,
                provider.Permissions))
            .ToList();
    }

    public async Task<Provider?> GetStaffMemberAsync(
        Guid providerId, CancellationToken ct = default) =>
        await _providers.GetByIdAsync(providerId, ct).ConfigureAwait(false);

    public async Task<string?> SaveStaffAsync(Provider provider, CancellationToken ct = default)
    {
        var refusal = await _guard
            .RefuseAsync(PracticePermissions.ManageStaff, ct)
            .ConfigureAwait(false);

        if (refusal is not null) return refusal;

        if (string.IsNullOrWhiteSpace(provider.FirstName)
            || string.IsNullOrWhiteSpace(provider.LastName))
        {
            return "A staff member needs a first and last name.";
        }

        if (!string.IsNullOrWhiteSpace(provider.Email)
            && !provider.Email.Contains('@', StringComparison.Ordinal))
        {
            return "That email address does not look right.";
        }

        var existing = await _providers.ListAsync(ct: ct).ConfigureAwait(false);

        // Two staff sharing an email is two logins for one person, and no way to tell from
        // a signed note or an audit entry which of them acted. Scoped to this clinic,
        // which is the right scope — the same clinician can work at two practices.
        //
        // Trimmed before comparing, or " jane@x.com" slips past the address it duplicates.
        provider.Email = string.IsNullOrWhiteSpace(provider.Email)
            ? null
            : provider.Email.Trim();

        if (provider.Email is { } email
            && existing.Any(entry => entry.Id != provider.Id
                && string.Equals(entry.Email, email, StringComparison.OrdinalIgnoreCase)))
        {
            return $"{email} is already used by another staff member.";
        }

        var isNew = provider.Id == Guid.Empty
            || existing.All(entry => entry.Id != provider.Id);

        // ---- access, which ManageStaff does not grant --------------------
        //
        // Checked separately and against the stored row, because ManageStaff is a
        // permission an owner hands out and this is the one thing it must not confer.
        // Without this, anybody who could add a colleague could tick "owns the practice"
        // on their own record and take the practice — the permission would have been a
        // route to ownership rather than a subset of it.
        var stored = isNew
            ? null
            : existing.FirstOrDefault(entry => entry.Id == provider.Id);

        var wasOwner = stored?.IsOwner ?? false;
        var wasPermissions = stored?.Permissions ?? PracticePermissions.None;

        var accessChanged =
            provider.IsOwner != wasOwner || provider.Permissions != wasPermissions;

        if (accessChanged && !await _guard.IsOwnerAsync(ct).ConfigureAwait(false))
        {
            // Reverted rather than refused outright, so a manager editing a colleague's
            // phone number is not blocked by a field they never touched and cannot see as
            // editable. The rest of the save goes through; the access does not move.
            provider.IsOwner = wasOwner;
            provider.Permissions = wasPermissions;
        }

        // The last owner cannot be demoted. There is no password reset and no vendor
        // override in this app, so a practice with nobody holding ownership is locked out
        // of its own settings permanently, and nothing can let it back in.
        if (wasOwner && !provider.IsOwner)
        {
            var otherOwners = existing.Count(entry =>
                entry.Id != provider.Id && entry.IsOwner && entry.IsActive);

            if (otherOwners == 0)
            {
                return $"{provider.FullName} is the only owner. Make somebody else an "
                    + "owner first — nothing can restore access to these settings "
                    + "afterwards.";
            }
        }

        provider.FirstName = provider.FirstName.Trim();
        provider.LastName = provider.LastName.Trim();

        // Null as well as empty. The field is nullable, so a new staff member arrives with
        // no site at all rather than with a zeroed one.
        if (provider.PrimaryLocationId is null || provider.PrimaryLocationId == Guid.Empty)
        {
            provider.PrimaryLocationId = _session.LocationId;
        }

        await _providers.SaveAsync(provider, ct).ConfigureAwait(false);

        await RecordAsync(
            isNew ? AuditAction.Created : AuditAction.Updated,
            nameof(Provider),
            provider.Id,
            isNew
                ? $"Added {provider.FullName} as {provider.Role}"
                : $"Updated {provider.FullName}",
            ct)
            .ConfigureAwait(false);

        return null;
    }

    /// <summary>
    /// Sets a staff member's password, and clears any lockout with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The practice's only route back in for somebody who has forgotten theirs. Until this
    /// existed the sign-in screen was telling the truth when it said a forgotten password
    /// "has to be reset by editing the staff record directly" — there was no control
    /// anywhere in the app that could do it, so a locked-out receptionist needed somebody
    /// with the database file.
    /// </para>
    /// <para>
    /// No old password is asked for. This is an administrative reset, not somebody changing
    /// their own — the whole point is that the person cannot supply the old one. Which is
    /// also why it needs <see cref="PracticePermissions.ManageStaff"/> and why the
    /// permission's own description says out loud that it means being able to sign in as
    /// anybody: setting a password is impersonation with extra steps.
    /// </para>
    /// <para>
    /// The lockout goes too. Five wrong attempts lock the account for fifteen minutes, and
    /// those attempts are almost always the person trying to remember — leaving the lock in
    /// place would hand them a new password they then cannot use for a quarter of an hour.
    /// </para>
    /// </remarks>
    /// <returns>Null on success, or why it was refused.</returns>
    public async Task<PasswordResetResult> SetStaffPasswordAsync(
        Guid providerId, string password, CancellationToken ct = default)
    {
        var refusal = await _guard
            .RefuseAsync(PracticePermissions.ManageStaff, ct)
            .ConfigureAwait(false);

        if (refusal is not null) return PasswordResetResult.Refused(refusal);

        var provider = await _providers.GetByIdAsync(providerId, ct).ConfigureAwait(false);

        if (provider is null)
        {
            return PasswordResetResult.Refused("That staff member no longer exists.");
        }

        // Resetting an OWNER's password takes ownership, not ManageStaff.
        //
        // Without this, ManageStaff was a route to ownership after all — the long way
        // round. Set the owner's password, sign in as them, and every permission follows.
        // The escalation check on SaveStaffAsync guards the IsOwner and Permissions fields
        // and would have caught the direct attempt; this is the same attack through the one
        // door that was left open, and the permission's own description says why it exists:
        // setting somebody's password is being able to sign in as them.
        if (provider.IsOwner
            && !await _guard.IsOwnerAsync(ct).ConfigureAwait(false))
        {
            return PasswordResetResult.Refused(
                $"{provider.FullName} owns the practice, so only another owner can reset "
                    + "their password.");
        }

        // Untrimmed on purpose. A leading or trailing space is a legitimate part of a
        // passphrase, and trimming it here would store a hash of something other than what
        // was typed — which fails at the next sign-in with no clue why.
        if (string.IsNullOrEmpty(password) || password.Length < MinimumStaffPasswordLength)
        {
            return PasswordResetResult.Refused(
                $"Use at least {MinimumStaffPasswordLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(provider.Username))
        {
            return PasswordResetResult.Refused(
                $"{provider.FullName} has no username yet, so there is nothing to sign "
                    + "in with. Give them one first.");
        }

        if (!provider.IsActive)
        {
            // Refused rather than allowed quietly. A password on a deactivated account
            // cannot be used, so setting one looks like restoring access and does not.
            return PasswordResetResult.Refused(
                $"{provider.FullName} is deactivated. Reactivate them first — a "
                    + "password on a deactivated account cannot sign in.");
        }

        provider.PasswordHash = _hasher.Hash(password);
        provider.PasswordUpdatedUtc = _clock.UtcNow;

        provider.FailedSignInCount = 0;
        provider.LockedUntilUtc = null;

        await _providers.SaveAsync(provider, ct).ConfigureAwait(false);

        // Told before the audit entry is written, so the entry can record whether it
        // reached them. "They were emailed" and "nobody could tell them" are different
        // facts about the same reset.
        var (sent, problem) = await NotifyPasswordResetAsync(provider, ct)
            .ConfigureAwait(false);

        // The password is never in the entry, obviously — but the fact of the reset is,
        // and so is who did it. "Who gave themselves my login" is the question this answers.
        await RecordAsync(
                AuditAction.Updated,
                nameof(Provider),
                provider.Id,
                $"Password set for {provider.FullName} ({provider.Username}) by an "
                    + "administrator. Any lockout was cleared. "
                    + (sent ? $"Notified at {provider.Email}." : "They were not notified."),
                ct)
            .ConfigureAwait(false);

        // The reason goes in its own entry rather than into the one above. Both readings
        // matter and they are different readings: the reset entry says at a glance whether
        // the person was told, and this one is what the "Email failed" filter finds when
        // somebody asks days later why nothing arrived.
        if (!sent)
        {
            await RecordAsync(
                    AuditAction.NotificationFailed,
                    nameof(Provider),
                    provider.Id,
                    $"Could not tell {provider.FullName} their password was reset — "
                        + $"{problem}.",
                    ct)
                .ConfigureAwait(false);
        }

        return new PasswordResetResult(null, sent, problem);
    }

    public async Task<string?> SetStaffActiveAsync(
        Guid providerId, bool isActive, CancellationToken ct = default)
    {
        var refusal = await _guard
            .RefuseAsync(PracticePermissions.ManageStaff, ct)
            .ConfigureAwait(false);

        if (refusal is not null) return refusal;

        var provider = await _providers.GetByIdAsync(providerId, ct).ConfigureAwait(false);
        if (provider is null) return "That staff member no longer exists.";

        if (!isActive)
        {
            var providers = await _providers.ListAsync(ct: ct).ConfigureAwait(false);

            // The app signs in as the first active clinical provider, so deactivating the
            // last one leaves nothing able to author a clinical record — the session falls
            // back to the receptionist, which is the exact bug that had every note in the
            // app attributed to the front desk.
            var clinicalRemaining = providers.Count(entry => entry.Id != providerId
                && entry.IsActive && ProviderRoles.IsClinical(entry.Role));

            if (ProviderRoles.IsClinical(provider.Role) && clinicalRemaining == 0)
            {
                return $"{provider.FullName} is the last active clinician. Deactivating "
                    + "them would leave nobody able to author a clinical record.";
            }

            // Deactivating the last owner locks the practice out of its own settings just
            // as surely as demoting them — the guard reads IsActive, so a switched-off
            // owner grants nothing. Refused here as well as there, because the two paths
            // reach the same state and only one of them looks like it is about access.
            var ownersRemaining = providers.Count(entry => entry.Id != providerId
                && entry.IsActive && entry.IsOwner);

            if (provider.IsOwner && ownersRemaining == 0)
            {
                return $"{provider.FullName} is the only owner. Make somebody else an "
                    + "owner first — a practice with no active owner cannot reach its own "
                    + "settings, and nothing can restore that.";
            }
        }

        if (provider.IsActive == isActive)
        {
            return isActive
                ? $"{provider.FullName} is already active."
                : $"{provider.FullName} is already deactivated.";
        }

        provider.IsActive = isActive;

        await _providers.SaveAsync(provider, ct).ConfigureAwait(false);

        await RecordAsync(
            isActive ? AuditAction.Updated : AuditAction.Deleted,
            nameof(Provider),
            provider.Id,
            isActive
                ? $"Reactivated {provider.FullName}"
                : $"Deactivated {provider.FullName} — records stay attributed to them",
            ct)
            .ConfigureAwait(false);

        return null;
    }

    // ---- sites -----------------------------------------------------------

    // ---- consent templates -----------------------------------------------

    public async Task<IReadOnlyList<ConsentTemplateRow>> GetConsentTemplatesAsync(
        CancellationToken ct = default)
    {
        var templates = await _consentTemplates.ListAsync(ct: ct).ConfigureAwait(false);

        if (templates.Count == 0) return [];

        // Signed consents only. A pending form raised this morning says nothing about how
        // much history is sitting behind the wording.
        var signed = await _consents
            .ListAsync(form => form.Status == ConsentStatus.Signed, ct)
            .ConfigureAwait(false);

        var codes = await _codes.ListAsync(ct: ct).ConfigureAwait(false);

        return templates
            .OrderBy(template => template.DisplayOrder)
            .ThenBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
            .Select(template => new ConsentTemplateRow(
                template.Id,
                template.Name,
                template.Category,
                template.IsActive,
                CountUse(template, signed, codes)))
            .ToList();
    }

    /// <summary>
    /// How many signed consents came from this template's category.
    /// </summary>
    /// <remarks>
    /// Counted through the procedure rather than stored on the form. A consent does not
    /// record which template it was raised from, deliberately — the wording is copied onto
    /// it, so the template is not part of what was agreed and a link would imply it was.
    /// </remarks>
    private static int CountUse(
        ConsentTemplate template,
        IReadOnlyList<ConsentForm> signed,
        IReadOnlyList<ProcedureCode> codes)
    {
        if (template.Category is not { Length: > 0 } category) return 0;

        var titles = codes
            .Where(code => string.Equals(code.Category, category, StringComparison.OrdinalIgnoreCase))
            .Select(code => code.Description)
            .ToList();

        return signed.Count(form =>
            titles.Any(title => form.Title.StartsWith(title, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<ConsentTemplate?> GetConsentTemplateAsync(
        Guid templateId, CancellationToken ct = default) =>
        _consentTemplates.GetByIdAsync(templateId, ct);

    public async Task<string?> SaveConsentTemplateAsync(
        ConsentTemplate template, CancellationToken ct = default)
    {
        var refusal = await _guard
            .RefuseAsync(PracticePermissions.ManageSettings, ct)
            .ConfigureAwait(false);

        if (refusal is not null) return refusal;

        if (string.IsNullOrWhiteSpace(template.Name)) return "Give the template a name.";

        // An empty template is worse than no template: it would raise consent forms with
        // nothing on them to read, which look signed and say nothing.
        if (string.IsNullOrWhiteSpace(template.Body))
        {
            return "Write the wording. A consent form with no wording is not consent.";
        }

        var name = template.Name.Trim();
        var category = string.IsNullOrWhiteSpace(template.Category)
            ? null
            : template.Category.Trim();

        var existing = await _consentTemplates.ListAsync(ct: ct).ConfigureAwait(false);

        // One active template per category, because booking picks by category and a second
        // match would make which wording a patient signs depend on row order.
        if (category is not null && existing.Any(other =>
            other.Id != template.Id
            && other.IsActive
            && string.Equals(other.Category, category, StringComparison.OrdinalIgnoreCase)))
        {
            return $"\"{category}\" already has an active template. Retire that one first, "
                + "or leave this template with no category so it is only ever chosen by hand.";
        }

        template.Name = name;
        template.Category = category;
        template.Body = template.Body.Trim();

        await _consentTemplates.SaveAsync(template, ct).ConfigureAwait(false);

        return null;
    }

    public async Task<string?> SetConsentTemplateActiveAsync(
        Guid templateId, bool isActive, CancellationToken ct = default)
    {
        var refusal = await _guard
            .RefuseAsync(PracticePermissions.ManageSettings, ct)
            .ConfigureAwait(false);

        if (refusal is not null) return refusal;

        var template = await _consentTemplates
            .GetByIdAsync(templateId, ct)
            .ConfigureAwait(false);

        if (template is null) return "That template no longer exists.";

        if (isActive && template.Category is { Length: > 0 } category)
        {
            var existing = await _consentTemplates.ListAsync(ct: ct).ConfigureAwait(false);

            if (existing.Any(other => other.Id != templateId
                && other.IsActive
                && string.Equals(other.Category, category, StringComparison.OrdinalIgnoreCase)))
            {
                return $"\"{category}\" already has an active template.";
            }
        }

        template.IsActive = isActive;

        await _consentTemplates.SaveAsync(template, ct).ConfigureAwait(false);

        return null;
    }

    public async Task<IReadOnlyList<string>> GetProcedureCategoriesAsync(
        CancellationToken ct = default)
    {
        var codes = await _codes.ListAsync(ct: ct).ConfigureAwait(false);

        // Read off the fee schedule rather than listed here. A practice that adds an
        // implants category to its schedule should find it offered on this screen without
        // anybody editing the app.
        return codes
            .Select(code => code.Category)
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Select(category => category!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ---- sites -----------------------------------------------------------

    public async Task<IReadOnlyList<SiteRow>> GetSitesAsync(CancellationToken ct = default)
    {
        // Closed sites included. They used to be filtered out, which was fine for a
        // read-only pane and is not for an editable one: a site closed by mistake would
        // vanish from the only screen that could reopen it.
        var locations = await _locations.ListAsync(ct: ct).ConfigureAwait(false);
        var operatories = await _operatories.ListAsync(ct: ct).ConfigureAwait(false);

        var providers = await _providers
            .ListAsync(provider => provider.IsActive, ct)
            .ConfigureAwait(false);

        // Among the open sites only. The first site by order is the practice's main one,
        // and a closed site keeping that title would be the odd answer.
        var primaryOrder = locations
            .Where(location => location.IsActive)
            .Select(location => (int?)location.DisplayOrder)
            .Min();

        return locations
            .OrderBy(location => location.IsActive ? 0 : 1)
            .ThenBy(location => location.DisplayOrder)
            .Select(location => new SiteRow(
                location.Id,
                location.Name,
                location.ShortName,
                Address(location),
                location.Phone,
                location.Abn,
                location.TimeZoneId,
                operatories.Count(o => o.PracticeLocationId == location.Id && o.IsActive),
                operatories.Count(o => o.PracticeLocationId == location.Id && !o.IsActive),
                providers.Count(p => p.PrimaryLocationId == location.Id),

                // Lowest display order, rather than a flag. The entity has no "primary"
                // field, and inventing one here would be a second answer to a question the
                // ordering already settles.
                location.IsActive && location.DisplayOrder == primaryOrder,
                location.IsActive,
                location.Hours))
            .ToList();
    }

    public async Task<PracticeLocation?> GetSiteAsync(
        Guid locationId, CancellationToken ct = default) =>
        await _locations.GetByIdAsync(locationId, ct).ConfigureAwait(false);

    public async Task<string?> SaveSiteAsync(
        PracticeLocation site, CancellationToken ct = default)
    {
        var refusal = await _guard
            .RefuseAsync(PracticePermissions.ManageSites, ct)
            .ConfigureAwait(false);

        if (refusal is not null) return refusal;

        if (string.IsNullOrWhiteSpace(site.Name)) return "A site needs a name.";

        if (!string.IsNullOrWhiteSpace(site.Email)
            && !site.Email.Contains('@', StringComparison.Ordinal))
        {
            return "That email address does not look right.";
        }

        // Checked, not trusted. Every appointment time in the diary is converted through
        // this id; an unknown one throws at the point of reading the day, which is a long
        // way from the screen that accepted it.
        if (!IsKnownTimeZone(site.TimeZoneId))
        {
            return $"\"{site.TimeZoneId}\" is not a time zone this machine knows. "
                + "Australia/Sydney is the usual one.";
        }

        var existing = await _locations.ListAsync(ct: ct).ConfigureAwait(false);
        var name = site.Name.Trim();

        // Two sites sharing a name makes every site picker in the app ambiguous, and the
        // pickers are how a booking is attached to a place.
        if (existing.Any(entry => entry.Id != site.Id
            && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return $"There is already a site called \"{name}\".";
        }

        var isNew = site.Id == Guid.Empty || existing.All(entry => entry.Id != site.Id);

        site.Name = name;
        site.ShortName = Trim(site.ShortName);
        site.AddressLine = Trim(site.AddressLine);
        site.Suburb = Trim(site.Suburb);
        site.State = Trim(site.State);
        site.Postcode = Trim(site.Postcode);
        site.Phone = Trim(site.Phone);
        site.Email = Trim(site.Email);
        site.Abn = Trim(site.Abn);

        if (isNew)
        {
            // Added to the end of the diary's site order. Inserting it anywhere else would
            // renumber sites nobody asked to move, and the first site by order is the one
            // the pane calls primary.
            site.DisplayOrder = existing.Count == 0
                ? 0
                : existing.Max(entry => entry.DisplayOrder) + 1;
        }

        await _locations.SaveAsync(site, ct).ConfigureAwait(false);

        await RecordAsync(
            isNew ? AuditAction.Created : AuditAction.Updated,
            nameof(PracticeLocation),
            site.Id,
            isNew ? $"Added the site {site.Name}" : $"Updated the site {site.Name}",
            ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string?> SetSiteActiveAsync(
        Guid locationId, bool isActive, CancellationToken ct = default)
    {
        var refusal = await _guard
            .RefuseAsync(PracticePermissions.ManageSites, ct)
            .ConfigureAwait(false);

        if (refusal is not null) return refusal;

        var site = await _locations.GetByIdAsync(locationId, ct).ConfigureAwait(false);
        if (site is null) return "That site no longer exists.";

        if (site.IsActive == isActive)
        {
            return isActive
                ? $"{site.Name} is already open."
                : $"{site.Name} is already closed.";
        }

        if (!isActive)
        {
            var sites = await _locations.ListAsync(ct: ct).ConfigureAwait(false);

            var openRemaining = sites.Count(entry =>
                entry.Id != locationId && entry.IsActive);

            if (openRemaining == 0)
            {
                return $"{site.Name} is the only open site. Closing it would leave the "
                    + "practice with nowhere to book into.";
            }

            var upcoming = await UpcomingAtSiteAsync(locationId, ct).ConfigureAwait(false);

            if (upcoming > 0)
            {
                return $"{site.Name} has {upcoming} "
                    + (upcoming == 1 ? "appointment" : "appointments")
                    + " still ahead of it. Move or cancel them first — closing the site "
                    + "would hide bookings the practice has made with patients.";
            }
        }

        site.IsActive = isActive;

        await _locations.SaveAsync(site, ct).ConfigureAwait(false);

        await RecordAsync(
            isActive ? AuditAction.Updated : AuditAction.Deleted,
            nameof(PracticeLocation),
            site.Id,
            isActive
                ? $"Reopened the site {site.Name}"
                : $"Closed the site {site.Name} — its history stays attached to it",
            ct)
            .ConfigureAwait(false);

        return null;
    }

    // ---- chairs ----------------------------------------------------------

    public async Task<IReadOnlyList<ChairRow>> GetChairsAsync(
        Guid locationId, CancellationToken ct = default)
    {
        var chairs = await _operatories
            .ListAsync(operatory => operatory.PracticeLocationId == locationId, ct)
            .ConfigureAwait(false);

        var now = _clock.UtcNow;

        // Cancelled bookings excluded. They are still rows with a future date, and
        // counting them would refuse to close a chair over appointments that are not
        // happening.
        var upcoming = await _appointments
            .ListAsync(
                appointment => appointment.PracticeLocationId == locationId
                    && appointment.StartUtc >= now
                    && appointment.Status != AppointmentStatus.Cancelled,
                ct)
            .ConfigureAwait(false);

        return chairs
            .OrderBy(chair => chair.IsActive ? 0 : 1)
            .ThenBy(chair => chair.DisplayOrder)
            .ThenBy(chair => chair.Name)
            .Select(chair => new ChairRow(
                chair.Id,
                chair.PracticeLocationId,
                chair.Name,
                chair.DisplayOrder,
                chair.IsSurgical,
                chair.IsActive,
                upcoming.Count(appointment => appointment.OperatoryId == chair.Id)))
            .ToList();
    }

    public async Task<Operatory?> GetChairAsync(
        Guid operatoryId, CancellationToken ct = default) =>
        await _operatories.GetByIdAsync(operatoryId, ct).ConfigureAwait(false);

    public async Task<string?> SaveChairAsync(
        Operatory chair, CancellationToken ct = default)
    {
        var refusal = await _guard
            .RefuseAsync(PracticePermissions.ManageSites, ct)
            .ConfigureAwait(false);

        if (refusal is not null) return refusal;

        if (string.IsNullOrWhiteSpace(chair.Name)) return "A chair needs a name.";

        if (chair.PracticeLocationId == Guid.Empty)
        {
            return "A chair has to belong to a site.";
        }

        var site = await _locations
            .GetByIdAsync(chair.PracticeLocationId, ct)
            .ConfigureAwait(false);

        if (site is null) return "That site no longer exists.";

        var siblings = await _operatories
            .ListAsync(
                operatory => operatory.PracticeLocationId == chair.PracticeLocationId, ct)
            .ConfigureAwait(false);

        var name = chair.Name.Trim();

        // Per site, not practice-wide. Two practices' worth of "Chair 1" is normal and
        // reads fine in a diary that only ever shows one site at a time.
        if (siblings.Any(entry => entry.Id != chair.Id
            && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return $"{site.ShortName ?? site.Name} already has a chair called \"{name}\".";
        }

        var isNew = chair.Id == Guid.Empty || siblings.All(entry => entry.Id != chair.Id);

        chair.Name = name;

        if (isNew)
        {
            chair.DisplayOrder = siblings.Count == 0
                ? 0
                : siblings.Max(entry => entry.DisplayOrder) + 1;
        }

        await _operatories.SaveAsync(chair, ct).ConfigureAwait(false);

        await RecordAsync(
            isNew ? AuditAction.Created : AuditAction.Updated,
            nameof(Operatory),
            chair.Id,
            isNew
                ? $"Added {chair.Name} at {site.ShortName ?? site.Name}"
                : $"Updated {chair.Name} at {site.ShortName ?? site.Name}",
            ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string?> SetChairActiveAsync(
        Guid operatoryId, bool isActive, CancellationToken ct = default)
    {
        var refusal = await _guard
            .RefuseAsync(PracticePermissions.ManageSites, ct)
            .ConfigureAwait(false);

        if (refusal is not null) return refusal;

        var chair = await _operatories.GetByIdAsync(operatoryId, ct).ConfigureAwait(false);
        if (chair is null) return "That chair no longer exists.";

        if (chair.IsActive == isActive)
        {
            return isActive
                ? $"{chair.Name} is already in service."
                : $"{chair.Name} is already out of service.";
        }

        if (!isActive)
        {
            var now = _clock.UtcNow;

            var booked = await _appointments
                .ListAsync(
                    appointment => appointment.OperatoryId == operatoryId
                        && appointment.StartUtc >= now
                        && appointment.Status != AppointmentStatus.Cancelled,
                    ct)
                .ConfigureAwait(false);

            if (booked.Count > 0)
            {
                return $"{chair.Name} has {booked.Count} "
                    + (booked.Count == 1 ? "booking" : "bookings")
                    + " still ahead of it. Move them to another chair first — a booking in "
                    + "a chair that takes no bookings is one nobody will be shown.";
            }

            var siblings = await _operatories
                .ListAsync(
                    operatory => operatory.PracticeLocationId == chair.PracticeLocationId,
                    ct)
                .ConfigureAwait(false);

            if (siblings.Count(entry => entry.Id != operatoryId && entry.IsActive) == 0)
            {
                return $"{chair.Name} is the only chair in service at this site. Taking it "
                    + "out would leave the site with no diary column to book into.";
            }
        }

        chair.IsActive = isActive;

        await _operatories.SaveAsync(chair, ct).ConfigureAwait(false);

        await RecordAsync(
            isActive ? AuditAction.Updated : AuditAction.Deleted,
            nameof(Operatory),
            chair.Id,
            isActive
                ? $"Put {chair.Name} back in service"
                : $"Took {chair.Name} out of service — its history stays with it",
            ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string?> MoveChairAsync(
        Guid operatoryId, int delta, CancellationToken ct = default)
    {
        var refusal = await _guard
            .RefuseAsync(PracticePermissions.ManageSites, ct)
            .ConfigureAwait(false);

        if (refusal is not null) return refusal;

        if (delta is not (-1 or 1)) return "A chair moves one place at a time.";

        var chair = await _operatories.GetByIdAsync(operatoryId, ct).ConfigureAwait(false);
        if (chair is null) return "That chair no longer exists.";

        var ordered = (await _operatories
            .ListAsync(
                operatory => operatory.PracticeLocationId == chair.PracticeLocationId, ct)
            .ConfigureAwait(false))
            .Where(entry => entry.IsActive)
            .OrderBy(entry => entry.DisplayOrder)
            .ThenBy(entry => entry.Name)
            .ToList();

        var at = ordered.FindIndex(entry => entry.Id == operatoryId);
        var to = at + delta;

        if (at < 0 || to < 0 || to >= ordered.Count) return null;

        // Reordered as a list and then renumbered from zero, rather than swapping the two
        // stored numbers. Seeded rows can share a DisplayOrder — and swapping two equal
        // numbers is not a move, so the chair would sit there refusing to budge.
        (ordered[at], ordered[to]) = (ordered[to], ordered[at]);

        for (var index = 0; index < ordered.Count; index++)
        {
            ordered[index].DisplayOrder = index;

            await _operatories.SaveAsync(ordered[index], ct).ConfigureAwait(false);
        }

        await RecordAsync(
            AuditAction.Updated,
            nameof(Operatory),
            chair.Id,
            $"Moved {chair.Name} {(delta < 0 ? "left" : "right")} in the diary",
            ct)
            .ConfigureAwait(false);

        return null;
    }

    // ---- registrations ---------------------------------------------------

    public async Task<IReadOnlyList<RegistrationRow>> GetRegistrationsAsync(
        CancellationToken ct = default)
    {
        var providers = await _providers.ListAsync(ct: ct).ConfigureAwait(false);
        var today = _clock.Today;

        return providers
            .Where(provider => ProviderRoles.IsClinical(provider.Role))
            .Select(provider => new RegistrationRow(
                provider.Id,
                provider.FullName,
                provider.Role,
                provider.AhpraNumber,
                provider.AhpraExpiresOn,
                provider.AhpraExpiresOn is { } expires
                    ? expires.DayNumber - today.DayNumber
                    : null))

            // Worst first: missing, then expired, then lapsing. The order the practice
            // manager has to act in.
            .OrderByDescending(row => row.IsMissing)
            .ThenByDescending(row => row.IsExpired)
            .ThenBy(row => row.DaysRemaining ?? int.MaxValue)
            .ToList();
    }

    public async Task<string?> SaveRegistrationAsync(
        Guid providerId,
        string? ahpraNumber,
        DateOnly? expiresOn,
        CancellationToken ct = default)
    {
        var refusal = await _guard
            .RefuseAsync(PracticePermissions.ManageStaff, ct)
            .ConfigureAwait(false);

        if (refusal is not null) return refusal;

        var provider = await _providers.GetByIdAsync(providerId, ct).ConfigureAwait(false);
        if (provider is null) return "That staff member no longer exists.";

        if (!ProviderRoles.IsClinical(provider.Role))
        {
            return $"{provider.FullName} is not a clinician, so there is no registration "
                + "to record.";
        }

        var trimmed = ahpraNumber?.Trim();

        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            // AHPRA numbers are three letters then ten digits — DEN0001234. Checked
            // because a mistyped number looks exactly as valid as a real one, and it is
            // what gets printed on a certificate.
            if (trimmed.Length != 13
                || !trimmed[..3].All(char.IsAsciiLetter)
                || !trimmed[3..].All(char.IsAsciiDigit))
            {
                return "An AHPRA number is three letters and ten digits — DEN0001234.";
            }

            trimmed = trimmed.ToUpperInvariant();
        }

        if (expiresOn is { } expires && expires < _clock.Today.AddYears(-5))
        {
            return "That expiry date looks like a typo — it is more than five years past.";
        }

        provider.AhpraNumber = string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        provider.AhpraExpiresOn = expiresOn;

        await _providers.SaveAsync(provider, ct).ConfigureAwait(false);

        await RecordAsync(
            AuditAction.Updated,
            nameof(Provider),
            provider.Id,
            $"Registration for {provider.FullName} set to "
                + $"{provider.AhpraNumber ?? "none"}"
                + (expiresOn is { } date ? $", expires {date:d MMM yyyy}" : string.Empty),
            ct)
            .ConfigureAwait(false);

        return null;
    }

    // ---- audit and data --------------------------------------------------

    public async Task<PagedResult<AuditRow>> GetAuditAsync(
        AuditAction? action = null,
        int page = 0,
        CancellationToken ct = default)
    {
        // Ordered in the query, not after it. A page taken from an unordered read is a page
        // whose contents depend on what SQLite felt like returning — a row can appear twice
        // and another never.
        var result = await _audit
            .GetPageAsync(
                page,
                AuditPageSize,
                orderBy: entry => entry.OccurredUtc,
                descending: true,
                predicate: action is { } filter ? entry => entry.Action == filter : null,
                ct)
            .ConfigureAwait(false);

        var entries = result.Items;

        if (entries.Count == 0) return PagedResult<AuditRow>.Empty(AuditPageSize);

        var patientIds = entries
            .Where(entry => entry.PatientId is not null)
            .Select(entry => entry.PatientId!.Value)
            .ToHashSet();

        var patients = patientIds.Count == 0
            ? []
            : await _patients
                .ListAsync(patient => patientIds.Contains(patient.Id), ct)
                .ConfigureAwait(false);

        var names = patients.ToDictionary(patient => patient.Id, patient => patient.FullName);

        // Names resolved for this page's rows only. The old version looked up every patient
        // named anywhere in the log to render twenty-five lines of it.
        var rows = entries
            .Select(entry => new AuditRow(
                entry.Id,
                entry.Action,
                entry.EntityName,
                entry.EntityId,
                entry.ProviderName,
                entry.PatientId is { } patientId ? names.GetValueOrDefault(patientId) : null,
                entry.OccurredUtc,
                entry.DeviceId,
                entry.Detail))
            .ToList();

        // The total comes from the paged read, not from rows.Count — which is this page's
        // own size and would make every page look like the last one.
        return new PagedResult<AuditRow>(
            rows, result.TotalCount, result.Page, result.PageSize);
    }

    public async Task<DatabaseInfo> GetDatabaseInfoAsync(CancellationToken ct = default)
    {
        var path = _path.GetDatabasePath();
        var file = new FileInfo(path);

        var deviceId = await _database.GetDeviceIdAsync(ct).ConfigureAwait(false);

        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var version = await db.Metadata
            .Where(entry => entry.Key == LocalMetadata.SchemaVersionKey)
            .Select(entry => entry.Value)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        // The newest snapshot sitting beside the live file, if any. Read from disk rather
        // than remembered: a backup taken in a previous run still counts, and one deleted
        // by hand must stop counting.
        var backup = LatestBackup(path);

        return new DatabaseInfo(
            path,
            OnDiskBytes(path),
            deviceId,
            int.TryParse(version, out var parsed) ? parsed : 0,
            file.Exists ? file.LastWriteTimeUtc : null,
            await _patients.CountAsync(ct: ct).ConfigureAwait(false),
            await _appointments.CountAsync(ct: ct).ConfigureAwait(false),
            await _invoices.CountAsync(ct: ct).ConfigureAwait(false),
            await _audit.CountAsync(ct: ct).ConfigureAwait(false),
            backup?.FullName,
            backup?.LastWriteTimeUtc);
    }

    public async Task<BackupResult> BackupAsync(CancellationToken ct = default)
    {
        // Owner only, and no permission grants it. The file this writes is a complete
        // unencrypted copy of every patient record the practice holds — whoever can
        // produce one can walk out with the practice.
        if (await _guard.RefuseUnlessOwnerAsync(ct).ConfigureAwait(false) is { } refused)
        {
            return new BackupResult(null, 0L, refused);
        }

        var path = _path.GetDatabasePath();

        if (!File.Exists(path))
        {
            return new BackupResult(null, 0L, "There is no database file to back up yet.");
        }

        var folder = Path.Combine(Path.GetDirectoryName(path)!, BackupFolder);
        Directory.CreateDirectory(folder);

        var target = Path.Combine(
            folder,
            $"molargo-{_clock.UtcNow.ToLocalTime():yyyy-MM-dd-HHmmss}.db3");

        try
        {
            await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

            // VACUUM INTO, not a file copy.
            //
            // SQLite keeps recent writes in a write-ahead log, so copying the .db3 while
            // the app holds it open can produce a file missing the newest transactions —
            // a backup that restores to a slightly earlier practice with no sign that
            // anything is missing. VACUUM INTO asks SQLite itself for a consistent
            // snapshot.
            //
            // The path travels as a bound parameter rather than being pasted into the
            // statement. SQLite takes an expression there, so this needs no quoting — and
            // quoting by hand is how an apostrophe in a Windows profile name becomes a
            // syntax error for exactly those users.
            await db.Database
                .ExecuteSqlRawAsync("VACUUM INTO {0}", [target], ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Surfaced verbatim. A backup that silently did not happen is worse than one
            // that failed loudly, and the message names the cause — a full disk, a
            // read-only folder.
            return new BackupResult(null, 0L, $"The backup did not complete: {ex.Message}");
        }

        var written = new FileInfo(target);

        await RecordAsync(
            AuditAction.Exported,
            nameof(MolargoDatabase),
            entityId: null,
            $"Local backup written to {Path.GetFileName(target)} "
                + $"({Math.Round(written.Length / 1024m / 1024m, 2)} MB)",
            ct)
            .ConfigureAwait(false);

        return new BackupResult(target, written.Length, null);
    }

    /// <summary>Where snapshots are kept, beside the live file.</summary>
    public const string BackupFolder = "backups";

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// How much disk the database really occupies.
    /// </summary>
    /// <remarks>
    /// The main file plus the write-ahead log. The shared-memory file is left out: it is a
    /// fixed-size coordination scratchpad, not data, and it disappears when the last
    /// connection closes.
    /// </remarks>
    private static long OnDiskBytes(string databasePath)
    {
        long total = 0;

        foreach (var candidate in new[] { databasePath, databasePath + "-wal" })
        {
            var file = new FileInfo(candidate);
            if (file.Exists) total += file.Length;
        }

        return total;
    }

    private static FileInfo? LatestBackup(string databasePath)
    {
        var folder = Path.Combine(Path.GetDirectoryName(databasePath)!, BackupFolder);

        if (!Directory.Exists(folder)) return null;

        return new DirectoryInfo(folder)
            .GetFiles("molargo-*.db3")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Whether this machine can resolve the id the diary would convert through.</summary>
    /// <remarks>
    /// .NET accepts IANA ids like "Australia/Sydney" on Windows as well as Linux, so the
    /// check is the same on both heads. Caught rather than allowed to throw: an unknown id
    /// is a typing mistake to be reported on the form, not a crash on whichever screen
    /// next reads a time.
    /// </remarks>
    private static bool IsKnownTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            // The id names a zone whose data is corrupt. Still not usable, and still the
            // form's problem rather than the diary's.
            return false;
        }
    }

    /// <summary>Bookings still ahead at a site, cancellations aside.</summary>
    private async Task<int> UpcomingAtSiteAsync(Guid locationId, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        var upcoming = await _appointments
            .ListAsync(
                appointment => appointment.PracticeLocationId == locationId
                    && appointment.StartUtc >= now
                    && appointment.Status != AppointmentStatus.Cancelled,
                ct)
            .ConfigureAwait(false);

        return upcoming.Count;
    }

    private static string? Address(PracticeLocation location)
    {
        var parts = new[] { location.AddressLine, location.Suburb, location.State, location.Postcode }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>
    /// Tells the account's owner their password was reset, using the practice's own mail
    /// account.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Best effort, and never able to fail the reset. The password is already changed and
    /// the person is already locked out of the old one by the time this runs — throwing
    /// here, or returning a refusal, would report a failure for something that did happen
    /// and leave the administrator retrying a reset that worked.
    /// </para>
    /// <para>
    /// Whether it went is reported back rather than swallowed, because "they have been
    /// emailed" changes what the administrator does next. The success line on the screen
    /// says which happened.
    /// </para>
    /// <para>
    /// The wording comes from the Comms template on <see cref="MessageTrigger.PasswordReset"/>
    /// so a practice can change it, with a built-in fallback for the practice that deletes
    /// or deactivates it — a security notice is not something to stop sending because
    /// somebody tidied the template list.
    /// </para>
    /// </remarks>
    private async Task<(bool Sent, string? Problem)> NotifyPasswordResetAsync(
        Provider provider, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(provider.Email))
        {
            return (false, "they have no email address on file");
        }

        var settings = await _notifications.GetAsync(ct).ConfigureAwait(false);

        if (!settings.IsConfigured)
        {
            return (false, "no mail account is set up under Admin → Settings");
        }

        if (!settings.EmailEnabled)
        {
            return (false, "email notifications are switched off");
        }

        var template = await FindResetTemplateAsync(ct).ConfigureAwait(false);

        var actor = _session.ProviderId is { } actorId
            ? await _providers.GetByIdAsync(actorId, ct).ConfigureAwait(false)
            : null;

        // Read by id through the repository, which is safe here precisely because Tenant is
        // one of the two entities excluded from the tenancy filter — so the id has to be
        // supplied rather than implied, and the one supplied is the session's own.
        var tenant = await _tenants
            .GetByIdAsync(_tenant.TenantId, ct)
            .ConfigureAwait(false);

        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["StaffName"] = provider.FullName,
            ["StaffUsername"] = provider.Username,
            ["ClinicCode"] = tenant?.Slug,
            ["ResetBy"] = actor?.FullName ?? _session.UserDisplayName ?? "an administrator",
            ["ResetAt"] = MolargoFormat.DateTime(_clock.UtcNow),
            ["PracticeName"] = tenant?.Name ?? "your practice",
            ["PracticePhone"] = null,
        };

        var subject = MergeFields
            .Render(template?.Subject ?? FallbackResetSubject, values)
            .Text;

        var body = MergeFields.Render(template?.Body ?? FallbackResetBody, values).Text;

        try
        {
            var result = await _email
                .SendAsync(settings, provider.Email!, subject, body, ct)
                .ConfigureAwait(false);

            return result.Succeeded ? (true, null) : (false, result.Detail);
        }
        catch (Exception ex)
        {
            // Caught rather than allowed to surface. The reset is committed; an SMTP
            // failure is worth reporting and is not worth turning a completed reset into
            // an error banner.
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// The practice's own reset wording, if it still has one.
    /// </summary>
    /// <remarks>
    /// Matched on the trigger rather than on the seeded id, so a practice that writes its
    /// own replacement is used instead of the one that shipped.
    /// </remarks>
    private async Task<MessageTemplate?> FindResetTemplateAsync(CancellationToken ct)
    {
        var templates = await _templates
            .ListAsync(
                template => template.Trigger == MessageTrigger.PasswordReset
                    && template.IsActive,
                ct)
            .ConfigureAwait(false);

        return templates.FirstOrDefault(
            template => template.Channel == CommunicationChannel.Email);
    }

    /// <summary>
    /// The wording used when the practice has no active reset template.
    /// </summary>
    /// <remarks>
    /// Deliberately duplicated from the seeded template rather than read from it. The seed
    /// runs once, and a practice can edit or delete what it produced — so the fallback has
    /// to be code, or the notice stops going out the day somebody tidies the template list.
    /// </remarks>
    private const string FallbackResetSubject = "Your Molargo password was reset";

    private const string FallbackResetBody =
        "Hello {{StaffName}},\n\n"
        + "Your Molargo password was reset by {{ResetBy}} on {{ResetAt}}.\n\n"
        + "Ask them for the new one — it is not in this email. You will need it with "
        + "your username {{StaffUsername}} and the clinic code {{ClinicCode}}.\n\n"
        + "If you were not expecting this, tell {{ResetBy}} straight away.\n\n"
        + "{{PracticeName}}";

    /// <summary>
    /// Writes one audit entry.
    /// </summary>
    /// <remarks>
    /// The acting person's name is stamped alongside their id, because a provider can be
    /// renamed and the trail has to say who it was at the time. The device id comes from
    /// the installation rather than from a request header — there is no server session,
    /// and the tablet a change was made on is what an offline-first practice can actually
    /// trace.
    /// </remarks>
    private async Task RecordAsync(
        AuditAction action,
        string entityName,
        Guid? entityId,
        string detail,
        CancellationToken ct)
    {
        var providerId = _session.ProviderId;

        var actor = providerId is { } id
            ? await _providers.GetByIdAsync(id, ct).ConfigureAwait(false)
            : null;

        await _audit
            .SaveAsync(
                new AuditEntry
                {
                    Action = action,
                    EntityName = entityName,
                    EntityId = entityId,
                    ProviderId = providerId,
                    ProviderName = actor?.FullName ?? _session.UserDisplayName,
                    OccurredUtc = _clock.UtcNow,
                    DeviceId = await _database.GetDeviceIdAsync(ct).ConfigureAwait(false),
                    Detail = detail,
                },
                ct)
            .ConfigureAwait(false);
    }

    /// <summary>The words staff use for an audited action.</summary>
    public static string ActionLabel(AuditAction action) => action switch
    {
        AuditAction.Created => "Created",
        AuditAction.Updated => "Changed",
        AuditAction.Deleted => "Removed",
        AuditAction.Viewed => "Viewed",
        AuditAction.Exported => "Exported",
        AuditAction.NotificationFailed => "Email failed",
        AuditAction.SignedIn => "Signed in",
        AuditAction.SignedOut => "Signed out",
        AuditAction.SignInFailed => "Sign-in failed",
        _ => action.ToString(),
    };

    /// <summary>Formats a size for a person rather than for a machine.</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024m:0.#} KB",
        _ => string.Create(
            CultureInfo.InvariantCulture,
            $"{bytes / 1024m / 1024m:0.##} MB"),
    };
}
