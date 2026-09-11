using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Data;
using DYS.Molargo.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace DYS.Molargo.Shared.Features.Auth.Services;

/// <summary>What came of a sign-in attempt.</summary>
public sealed record SignInResult(
    bool Succeeded,
    string? Failure,
    Guid TenantId,
    string? TenantName,
    Guid ProviderId,
    string? DisplayName,
    DateTime? LockedUntilUtc)
{
    public static SignInResult Refused(string failure, DateTime? lockedUntil = null) =>
        new(false, failure, Guid.Empty, null, Guid.Empty, null, lockedUntil);

    public bool IsLockedOut => LockedUntilUtc is not null;
}

/// <summary>
/// Signing in with a clinic code, a username and a password.
/// </summary>
/// <remarks>
/// <para>
/// The clinic code comes first because it decides which set of users the name is looked up
/// in. Usernames are unique per clinic, not globally — two practices can each have an
/// "rvance" — so a username alone is not an identity.
/// </para>
/// <para>
/// This is local authentication against the practice's own staff records. It is not an
/// identity provider: there is no MFA, no SSO, no password reset and no session token, and
/// the sign-in screen says so. What it does give is a real credential check with a real
/// password hash, which is the part that has to be right first.
/// </para>
/// </remarks>
public interface IAuthService
{
    /// <summary>
    /// Verifies a clinic code, username and password, and starts the session on success.
    /// </summary>
    Task<SignInResult> SignInAsync(
        string tenantCode, string username, string password, CancellationToken ct = default);

    /// <summary>The clinic code, if this installation already belongs to one.</summary>
    Task<string?> GetInstalledTenantCodeAsync(CancellationToken ct = default);

    /// <summary>
    /// Restores a session the browser or device already holds. True when one was restored.
    /// </summary>
    /// <remarks>
    /// Called by the shell on every new circuit, which is what makes a page refresh keep
    /// the user signed in rather than bouncing them to the sign-in screen.
    /// </remarks>
    Task<bool> RestoreAsync(CancellationToken ct = default);

    Task SignOutAsync(CancellationToken ct = default);
}

/// <inheritdoc cref="IAuthService"/>
public sealed class AuthService : IAuthService
{
    /// <summary>
    /// Failures before the account is locked.
    /// </summary>
    /// <remarks>
    /// Five, then a short lock that grows nothing — enough to stop online guessing without
    /// letting a rival lock a whole practice out by trying names. The real defence against
    /// an offline attack is the hash's iteration count, not this.
    /// </remarks>
    private const int MaxFailures = 5;

    private const int LockoutMinutes = 15;

    /// <summary>
    /// One message for every kind of failure.
    /// </summary>
    /// <remarks>
    /// Deliberately identical whether the clinic code is wrong, the username is unknown,
    /// the account has no password set or the password is wrong. Distinguishing them tells
    /// an attacker which clinics exist and who works there, which is exactly the
    /// enumeration a practice should not hand out.
    /// </remarks>
    private const string GenericFailure =
        "Those details do not match. Check the clinic code, username and password.";

    private readonly MolargoDatabase _database;
    private readonly IPasswordHasher _hasher;
    private readonly ITenantContext _tenant;
    private readonly ISessionService _session;
    private readonly ISessionHandoff _handoff;
    private readonly IClock _clock;

    public AuthService(
        MolargoDatabase database,
        IPasswordHasher hasher,
        ITenantContext tenant,
        ISessionService session,
        ISessionHandoff handoff,
        IClock clock)
    {
        _database = database;
        _hasher = hasher;
        _tenant = tenant;
        _session = session;
        _handoff = handoff;
        _clock = clock;
    }

    public async Task<SignInResult> SignInAsync(
        string tenantCode, string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantCode)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password))
        {
            return SignInResult.Refused("Enter the clinic code, your username and password.");
        }

        var code = tenantCode.Trim().ToLowerInvariant();
        var name = username.Trim();

        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        // The clinic is looked up without the tenant filter, because it is the thing that
        // decides what the filter will be. This is the one query in the app that crosses
        // the tenant boundary, and it reads exactly one row by an indexed unique code.
        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Slug == code && !entry.IsDeleted, ct)
            .ConfigureAwait(false);

        if (tenant is null || !tenant.IsActive) return SignInResult.Refused(GenericFailure);

        // Staff are read with the filter bypassed and the tenant applied by hand. The
        // session has no tenant yet — that is what signing in establishes — so relying on
        // the ambient filter here would match nothing and refuse every correct password.
        var staff = await db.Providers
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(provider => provider.TenantId == tenant.Id && !provider.IsDeleted)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var user = staff.FirstOrDefault(provider =>
            string.Equals(provider.Username, name, StringComparison.OrdinalIgnoreCase));

        if (user is null || !user.CanSignIn)
        {
            // The password is still hashed against nothing before returning, so a missing
            // username costs the same time as a wrong password. Skipping it makes "no such
            // user" measurably faster, which is a username oracle.
            _hasher.Verify(password, DummyHash);

            return SignInResult.Refused(GenericFailure);
        }

        if (user.LockedUntilUtc is { } until && until > _clock.UtcNow)
        {
            return SignInResult.Refused(
                $"That account is locked until {until.ToLocalTime():h:mm tt} after too "
                    + "many failed attempts.",
                until);
        }

        if (!_hasher.Verify(password, user.PasswordHash))
        {
            await RecordFailureAsync(db, user, tenant, ct).ConfigureAwait(false);

            return SignInResult.Refused(GenericFailure);
        }

        await RecordSuccessAsync(db, user, tenant, ct).ConfigureAwait(false);

        // The tenant first, then the session. Everything the shell loads next reads
        // through the tenant filter, so a session established before its tenant would spend
        // its first render reading nothing.
        _tenant.Use(tenant.Id, tenant.Name);
        await _session
            .SignInAsync(
                user.Id, user.FullName, user.Role, user.IsOwner, user.Permissions, ct)
            .ConfigureAwait(false);

        // Then out to the host, which is what makes the sign-in survive a reload. On the
        // web this navigates through an endpoint that sets a cookie; on a device it does
        // nothing, because the in-memory session above is already enough.
        await _handoff
            .CompleteSignInAsync(
                new SignedInUser(tenant.Id, tenant.Name, user.Id, user.FullName), ct)
            .ConfigureAwait(false);

        return new SignInResult(
            true, null, tenant.Id, tenant.Name, user.Id, user.FullName, null);
    }

    public async Task<string?> GetInstalledTenantCodeAsync(CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        // Offered as a convenience on a device that already belongs to a clinic — the code
        // is not a secret, and retyping it at every sign-in on a surgery tablet is friction
        // with no security value.
        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Id == _tenant.TenantId, ct)
            .ConfigureAwait(false);

        return tenant?.Slug;
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        await _session.SignOutAsync(ct).ConfigureAwait(false);

        // The host's copy goes too, or a reload would sign the person straight back in
        // from a cookie the app thought it had discarded.
        await _handoff.CompleteSignOutAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Restores a session this browser or device already holds.
    /// </summary>
    /// <remarks>
    /// The other half of the handoff, and what makes a page refresh keep the user signed
    /// in. Trusts the host's persisted identity rather than re-checking a password — the
    /// host is the thing that verified it — but re-reads the staff record, so a person
    /// deactivated since they signed in does not stay in on a stale cookie.
    /// </remarks>
    public async Task<bool> RestoreAsync(CancellationToken ct = default)
    {
        var stored = await _handoff.RestoreAsync(ct).ConfigureAwait(false);
        if (stored is null) return false;

        // The tenant first, so the read below can see anything at all.
        _tenant.Use(stored.TenantId, stored.TenantName);

        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var user = await db.Providers
            .AsNoTracking()
            .FirstOrDefaultAsync(provider => provider.Id == stored.ProviderId, ct)
            .ConfigureAwait(false);

        if (user is null || !user.CanSignIn)
        {
            // Deactivated, renamed out of a login, or belonging to another clinic
            // altogether. Whatever the reason, the cookie no longer names someone who may
            // sign in, so it is discarded rather than honoured.
            await SignOutAsync(ct).ConfigureAwait(false);
            return false;
        }

        await _session
            .SignInAsync(
                user.Id, user.FullName, user.Role, user.IsOwner, user.Permissions, ct)
            .ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// A real verifier that no password matches, for the unknown-username path.
    /// </summary>
    /// <remarks>
    /// Its shape and iteration count match a live one so verifying it costs the same. A
    /// shorter or cheaper placeholder would restore the timing difference it exists to
    /// remove.
    /// </remarks>
    private const string DummyHash =
        "pbkdf2-sha256$210000$AAAAAAAAAAAAAAAAAAAAAA==$"
            + "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    private async Task RecordFailureAsync(
        MolargoDbContext db, Provider user, Tenant tenant, CancellationToken ct)
    {
        user.FailedSignInCount++;

        if (user.FailedSignInCount >= MaxFailures)
        {
            user.LockedUntilUtc = _clock.UtcNow.AddMinutes(LockoutMinutes);
            user.FailedSignInCount = 0;
        }

        db.Providers.Attach(user);
        db.Entry(user).State = EntityState.Modified;

        await AuditAsync(db, AuditAction.SignInFailed, user, tenant,
            user.LockedUntilUtc is not null
                ? $"Failed sign-in for {user.Username}; account locked for {LockoutMinutes} minutes"
                : $"Failed sign-in for {user.Username} ({user.FailedSignInCount} in a row)",
            ct)
            .ConfigureAwait(false);
    }

    private async Task RecordSuccessAsync(
        MolargoDbContext db, Provider user, Tenant tenant, CancellationToken ct)
    {
        user.FailedSignInCount = 0;
        user.LockedUntilUtc = null;
        user.LastSignInUtc = _clock.UtcNow;

        db.Providers.Attach(user);
        db.Entry(user).State = EntityState.Modified;

        await AuditAsync(db, AuditAction.SignedIn, user, tenant,
            $"Signed in as {user.Username}", ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the audit entry and saves, with the tenant set by hand.
    /// </summary>
    /// <remarks>
    /// By hand because the repository's stamping is not in play here: this runs before the
    /// session has a tenant, and an entry written without one would be invisible in the
    /// audit log it exists to appear in.
    /// </remarks>
    private async Task AuditAsync(
        MolargoDbContext db,
        AuditAction action,
        Provider user,
        Tenant tenant,
        string detail,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;

        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Action = action,
            EntityName = nameof(Provider),
            EntityId = user.Id,
            ProviderId = user.Id,
            ProviderName = user.FullName,
            OccurredUtc = now,
            CreatedUtc = now,
            UpdatedUtc = now,
            DeviceId = await _database.GetDeviceIdAsync(ct).ConfigureAwait(false),
            Detail = detail,
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
