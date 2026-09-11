using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Data;
using DYS.Molargo.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace DYS.Molargo.Shared.Features.Platform.Services;

/// <summary>
/// One of the vendor's own operators.
/// </summary>
/// <param name="IsSelf">
/// The account doing the looking. Named so the screen can refuse the two things nobody
/// should do to themselves — deactivate, or change their own password from the admin form
/// rather than knowing the old one.
/// </param>
/// <param name="LockedUntilUtc">
/// When a run of failed sign-ins locked them out, or null. Held because there is no
/// password-reset flow in this app: without it, an operator who mistyped five times waits
/// out the lockout with nobody able to help.
/// </param>
public sealed record OperatorRow(
    Guid ProviderId,
    string Name,
    string? Username,
    string? Email,
    bool IsActive,
    bool CanSignIn,
    DateTime? LastSignInUtc,
    DateTime? LockedUntilUtc,
    bool IsSelf)
{
    /// <summary>An account with no password set cannot sign in yet.</summary>
    public bool NeedsPassword => IsActive && !CanSignIn;

    public bool IsLockedOut => LockedUntilUtc is not null;
}

/// <summary>
/// The vendor's own operator accounts — who can administer the platform.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IPlatformService"/>, which is about the clinics. This one
/// never leaves the platform tenant: every account it touches is a provider in the
/// vendor's own tenant, so the ambient query filter already scopes it correctly and
/// nothing here bypasses the filter. That is worth stating, because it is the one platform
/// service that does <em>not</em> need to cross the boundary.
/// </para>
/// <para>
/// Every account it creates is a <see cref="ProviderRole.SuperAdmin"/>. There is no
/// lesser platform role to offer: the vendor's tenant has no patients, no diary and no
/// invoices, so an operator who was not a super admin could do nothing at all.
/// </para>
/// </remarks>
public interface IPlatformUserService
{
    Task<IReadOnlyList<OperatorRow>> GetOperatorsAsync(CancellationToken ct = default);

    Task<Provider?> GetOperatorAsync(Guid providerId, CancellationToken ct = default);

    /// <summary>
    /// Creates or updates an operator. Returns a refusal, or null.
    /// </summary>
    /// <param name="password">
    /// The password to set, or null to leave an existing one alone. Required for a new
    /// account, which otherwise exists but cannot sign in.
    /// </param>
    Task<string?> SaveOperatorAsync(
        Provider operatorAccount, string? password, CancellationToken ct = default);

    /// <summary>Sets an operator's password, without needing the old one.</summary>
    /// <remarks>
    /// An administrative reset, and the only one this app has — there is no "forgot
    /// password" flow, no email loop and no recovery code. Which means a second operator
    /// is the vendor's whole disaster plan, and the screen says so.
    /// </remarks>
    Task<string?> SetPasswordAsync(
        Guid providerId, string password, CancellationToken ct = default);

    Task<string?> SetOperatorActiveAsync(
        Guid providerId, bool isActive, CancellationToken ct = default);

    /// <summary>Clears a lockout after too many failed sign-ins.</summary>
    Task<string?> UnlockOperatorAsync(Guid providerId, CancellationToken ct = default);
}

/// <inheritdoc cref="IPlatformUserService"/>
public sealed class PlatformUserService : IPlatformUserService
{
    /// <summary>
    /// The shortest password this will accept.
    /// </summary>
    /// <remarks>
    /// Twelve, and longer than a clinic's would need to be, because one of these accounts
    /// can suspend every practice on the platform. Length rather than a character-class
    /// rule: the hash is PBKDF2 at 210,000 iterations, and against that a long passphrase
    /// is worth more than a mandated punctuation mark.
    /// </remarks>
    private const int MinimumPasswordLength = 12;

    private readonly MolargoDatabase _database;
    private readonly IPasswordHasher _hasher;
    private readonly ISessionService _session;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public PlatformUserService(
        MolargoDatabase database,
        IPasswordHasher hasher,
        ISessionService session,
        ITenantContext tenant,
        IClock clock)
    {
        _database = database;
        _hasher = hasher;
        _session = session;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<IReadOnlyList<OperatorRow>> GetOperatorsAsync(
        CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false)) return [];

        // No filter bypass and none wanted: these are providers in the vendor's own
        // tenant, which is the tenant this session is in.
        var operators = await db.Providers
            .AsNoTracking()
            .Where(provider => provider.Role == ProviderRole.SuperAdmin)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var now = _clock.UtcNow;

        return operators
            // Active first, then by name. A disabled account sorted among the current ones
            // is how somebody gets asked to cover a shift they cannot sign in for.
            .OrderBy(provider => provider.IsActive ? 0 : 1)
            .ThenBy(provider => provider.LastName)
            .ThenBy(provider => provider.FirstName)
            .Select(provider => new OperatorRow(
                provider.Id,
                provider.FullName,
                provider.Username,
                provider.Email,
                provider.IsActive,
                provider.CanSignIn,
                provider.LastSignInUtc,

                // Only a lock still in force. An expired one is history, and showing it
                // would have somebody clearing a lockout that had already lapsed.
                provider.LockedUntilUtc is { } until && until > now ? until : null,
                provider.Id == _session.ProviderId))
            .ToList();
    }

    public async Task<Provider?> GetOperatorAsync(
        Guid providerId, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false)) return null;

        return await db.Providers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                provider => provider.Id == providerId
                    && provider.Role == ProviderRole.SuperAdmin,
                ct)
            .ConfigureAwait(false);
    }

    public async Task<string?> SaveOperatorAsync(
        Provider operatorAccount, string? password, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false))
        {
            return PlatformGuard.NotPermitted;
        }

        if (string.IsNullOrWhiteSpace(operatorAccount.FirstName)
            || string.IsNullOrWhiteSpace(operatorAccount.LastName))
        {
            return "An operator needs a first and last name.";
        }

        var username = (operatorAccount.Username ?? string.Empty).Trim();

        if (!IsUsableUsername(username))
        {
            return "The username has to be 3 to 40 characters of letters, numbers, dots, "
                + "hyphens or underscores, with no spaces.";
        }

        if (!string.IsNullOrWhiteSpace(operatorAccount.Email)
            && !operatorAccount.Email.Contains('@', StringComparison.Ordinal))
        {
            return "That email address does not look right.";
        }

        var existing = await db.Providers
            .FirstOrDefaultAsync(provider => provider.Id == operatorAccount.Id, ct)
            .ConfigureAwait(false);

        // Guarded even though the screen only lists super admins: this method takes an id,
        // and without the check it would edit a clinic's staff member if handed one.
        if (existing is not null && existing.Role != ProviderRole.SuperAdmin)
        {
            return PlatformGuard.NotPermitted;
        }

        var siblings = await db.Providers
            .AsNoTracking()
            .Where(provider => provider.Id != operatorAccount.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Unique within the tenant, which is all a username has to be — sign-in looks it
        // up inside one clinic code. A clinic may still have its own "sam.devyard".
        if (siblings.Any(provider =>
            string.Equals(provider.Username, username, StringComparison.OrdinalIgnoreCase)))
        {
            return $"\"{username}\" is already used by another account here.";
        }

        // And the email, for the same reason: two operator accounts on one address is one
        // person with two sets of credentials, and no way to tell from an audit entry
        // which of them acted.
        var email = Trim(operatorAccount.Email);

        if (email is not null && siblings.Any(provider =>
            string.Equals(provider.Email, email, StringComparison.OrdinalIgnoreCase)))
        {
            return $"{email} is already used by another operator account.";
        }

        var isNew = existing is null;

        if (isNew && string.IsNullOrWhiteSpace(password))
        {
            // Refused rather than allowed: an operator with no password is an account that
            // exists, appears in the list, and silently cannot sign in.
            return "Set a password for a new operator, or they cannot sign in.";
        }

        if (password is { Length: > 0 } && password.Length < MinimumPasswordLength)
        {
            return $"Use at least {MinimumPasswordLength} characters. This account can "
                + "suspend every clinic on the platform.";
        }

        var now = _clock.UtcNow;
        var row = existing ?? new Provider
        {
            Id = operatorAccount.Id == Guid.Empty ? Guid.NewGuid() : operatorAccount.Id,
            TenantId = _tenant.TenantId,
            Role = ProviderRole.SuperAdmin,
            IsActive = true,
            CreatedUtc = now,
        };

        // Written back so the caller can select what it just created.
        operatorAccount.Id = row.Id;

        row.FirstName = operatorAccount.FirstName.Trim();
        row.LastName = operatorAccount.LastName.Trim();
        row.DisplayName = Trim(operatorAccount.DisplayName);
        row.Email = Trim(operatorAccount.Email);
        row.Username = username;
        row.UpdatedUtc = now;

        // Always the platform role, whatever arrived on the object. There is no lesser
        // role that would do anything in the vendor's tenant.
        row.Role = ProviderRole.SuperAdmin;

        if (password is { Length: > 0 })
        {
            row.PasswordHash = _hasher.Hash(password);
            row.PasswordUpdatedUtc = now;

            // A new password ends a lockout. Leaving one in force after it was changed
            // means the fix for a forgotten password does not take effect for 15 minutes.
            row.FailedSignInCount = 0;
            row.LockedUntilUtc = null;
        }

        if (isNew) db.Providers.Add(row);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await AuditAsync(
            db,
            row.Id,
            isNew ? AuditAction.Created : AuditAction.Updated,
            isNew
                ? $"Added the operator {row.FullName} (\"{row.Username}\")"
                : $"Updated the operator {row.FullName}"
                    + (password is { Length: > 0 } ? "; password replaced" : string.Empty),
            ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string?> SetPasswordAsync(
        Guid providerId, string password, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false))
        {
            return PlatformGuard.NotPermitted;
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < MinimumPasswordLength)
        {
            return $"Use at least {MinimumPasswordLength} characters. This account can "
                + "suspend every clinic on the platform.";
        }

        var row = await db.Providers
            .FirstOrDefaultAsync(
                provider => provider.Id == providerId
                    && provider.Role == ProviderRole.SuperAdmin,
                ct)
            .ConfigureAwait(false);

        if (row is null) return "That operator no longer exists.";

        var now = _clock.UtcNow;

        row.PasswordHash = _hasher.Hash(password);
        row.PasswordUpdatedUtc = now;
        row.FailedSignInCount = 0;
        row.LockedUntilUtc = null;
        row.UpdatedUtc = now;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // The password is never in the entry, only the fact of it. An audit log that
        // quotes secrets is a second copy of them.
        await AuditAsync(
            db,
            row.Id,
            AuditAction.Updated,
            $"Password reset for the operator {row.FullName}",
            ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string?> SetOperatorActiveAsync(
        Guid providerId, bool isActive, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false))
        {
            return PlatformGuard.NotPermitted;
        }

        var row = await db.Providers
            .FirstOrDefaultAsync(
                provider => provider.Id == providerId
                    && provider.Role == ProviderRole.SuperAdmin,
                ct)
            .ConfigureAwait(false);

        if (row is null) return "That operator no longer exists.";

        if (row.IsActive == isActive)
        {
            return isActive
                ? $"{row.FullName} is already active."
                : $"{row.FullName} is already deactivated.";
        }

        if (!isActive)
        {
            // Deactivating yourself ends your own session on the next read, and if you are
            // also the last one it ends everybody's. Refused before either can happen.
            if (providerId == _session.ProviderId)
            {
                return "You cannot deactivate your own account. Ask another operator to "
                    + "do it.";
            }

            var remaining = await db.Providers
                .CountAsync(
                    provider => provider.Id != providerId
                        && provider.Role == ProviderRole.SuperAdmin
                        && provider.IsActive,
                    ct)
                .ConfigureAwait(false);

            if (remaining == 0)
            {
                return $"{row.FullName} is the last active operator. Deactivating them "
                    + "would leave nobody able to administer the platform, and there is no "
                    + "password reset to get back in with.";
            }
        }

        row.IsActive = isActive;
        row.UpdatedUtc = _clock.UtcNow;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await AuditAsync(
            db,
            row.Id,
            isActive ? AuditAction.Updated : AuditAction.Deleted,
            isActive
                ? $"Reactivated the operator {row.FullName}"
                : $"Deactivated the operator {row.FullName} — their audit entries stay "
                    + "attributed to them",
            ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string?> UnlockOperatorAsync(
        Guid providerId, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false))
        {
            return PlatformGuard.NotPermitted;
        }

        var row = await db.Providers
            .FirstOrDefaultAsync(
                provider => provider.Id == providerId
                    && provider.Role == ProviderRole.SuperAdmin,
                ct)
            .ConfigureAwait(false);

        if (row is null) return "That operator no longer exists.";

        if (row.LockedUntilUtc is null || row.LockedUntilUtc <= _clock.UtcNow)
        {
            return $"{row.FullName} is not locked out.";
        }

        row.LockedUntilUtc = null;
        row.FailedSignInCount = 0;
        row.UpdatedUtc = _clock.UtcNow;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await AuditAsync(
            db,
            row.Id,
            AuditAction.Updated,
            $"Cleared the sign-in lockout on {row.FullName}",
            ct)
            .ConfigureAwait(false);

        return null;
    }

    // ---- helpers ---------------------------------------------------------

    private Task<bool> IsSuperAdminAsync(MolargoDbContext db, CancellationToken ct) =>
        PlatformGuard.IsSuperAdminAsync(db, _session.ProviderId, ct);

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// A username somebody can type at a sign-in prompt without ambiguity.
    /// </summary>
    /// <remarks>
    /// No spaces above all: sign-in trims the field, so a name with a trailing space would
    /// be a login that works from one keyboard and not from another.
    /// </remarks>
    private static bool IsUsableUsername(string username) =>
        username.Length is >= 3 and <= 40
        && username.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '.' or '-' or '_');

    /// <summary>
    /// Writes the entry into the vendor's own tenant.
    /// </summary>
    /// <remarks>
    /// Unlike a change to a clinic's subscription, which goes in that clinic's log because
    /// that is where they would look for it. These are the vendor's own accounts, and no
    /// practice has any business seeing who works for the vendor.
    /// </remarks>
    private async Task AuditAsync(
        MolargoDbContext db,
        Guid providerId,
        AuditAction action,
        string detail,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;

        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            Action = action,
            EntityName = nameof(Provider),
            EntityId = providerId,
            ProviderId = _session.ProviderId,
            ProviderName = _session.UserDisplayName,
            OccurredUtc = now,
            CreatedUtc = now,
            UpdatedUtc = now,
            DeviceId = await _database.GetDeviceIdAsync(ct).ConfigureAwait(false),
            Detail = detail,
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
