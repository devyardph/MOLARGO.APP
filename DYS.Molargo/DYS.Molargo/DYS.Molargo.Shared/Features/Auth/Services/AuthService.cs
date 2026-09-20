using System.Security.Cryptography;
using DYS.Molargo.Domain;
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
    DateTime? LockedUntilUtc,

    /// <summary>
    /// The password was right, and a code has been emailed. Not signed in yet.
    /// </summary>
    /// <remarks>
    /// A third outcome rather than a success the screen then qualifies. Succeeded means a
    /// session exists; if this were folded into it, every caller that checks Succeeded —
    /// the view model, the handoff, anything added later — would let somebody through on a
    /// password alone, and the bug would look exactly like working code.
    /// </remarks>
    bool NeedsCode = false,

    /// <summary>Where the code went, masked. Null unless one was sent.</summary>
    string? CodeSentTo = null)
{
    public static SignInResult Refused(string failure, DateTime? lockedUntil = null) =>
        new(false, failure, Guid.Empty, null, Guid.Empty, null, lockedUntil);

    /// <summary>The password was accepted; the second step is outstanding.</summary>
    public static SignInResult AwaitingCode(string maskedAddress) =>
        new(false, null, Guid.Empty, null, Guid.Empty, null, null, true, maskedAddress);

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
    /// <remarks>
    /// Where the staff member has two-step sign-in switched on, a correct password does not
    /// start a session: it emails a six-digit code and returns
    /// <see cref="SignInResult.NeedsCode"/>. <see cref="CompleteWithCodeAsync"/> finishes it.
    /// </remarks>
    Task<SignInResult> SignInAsync(
        string tenantCode, string username, string password, CancellationToken ct = default);

    /// <summary>
    /// Finishes a sign-in that is waiting on an emailed code, and starts the session.
    /// </summary>
    /// <remarks>
    /// Takes the clinic code and username again rather than trusting anything held between
    /// the two steps. There is nothing to hold it in: the web head has no session until
    /// sign-in creates one, and a half-authenticated cookie would be a new credential with
    /// its own way of going wrong. The cost is re-finding the account, which is one indexed
    /// read.
    /// </remarks>
    Task<SignInResult> CompleteWithCodeAsync(
        string tenantCode, string username, string code, CancellationToken ct = default);

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

    /// <summary>
    /// How long an emailed sign-in code lasts.
    /// </summary>
    /// <remarks>
    /// Five minutes. It only has to cover glancing at a phone while standing at the
    /// machine, and every minute it stays live is more time for guesses to be thrown at it.
    /// </remarks>
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Wrong codes before every live code on the account is burned.
    /// </summary>
    /// <remarks>
    /// The same five the password gets, and counted across all live codes rather than per
    /// row — an attacker does not know which outstanding code they are guessing at, so
    /// counting per row would multiply their attempts by however many they can cause to
    /// exist.
    /// </remarks>
    private const int CodeAttemptLimit = 5;

    /// <summary>
    /// The refusal for a wrong, expired or spent code.
    /// </summary>
    /// <remarks>
    /// One sentence for all three. "Expired" tells an attacker a real code existed and
    /// roughly when, which over a five-minute window is most of what they would want.
    /// </remarks>
    private const string CodeFailure =
        "That code is wrong or has expired. Sign in again to get a new one.";

    /// <summary>
    /// What a staff member with two-step sign-in sees when the mail will not go.
    /// </summary>
    /// <remarks>
    /// Named plainly, unlike every other failure here. The vague message exists so an
    /// attacker learns nothing from a refusal; this one is reached only after a correct
    /// password, and leaving the right person staring at "those details do not match" when
    /// the password was in fact right is how an outage becomes an afternoon of resets.
    /// </remarks>
    private const string MailFailure =
        "Your password was accepted, but the code could not be emailed. Ask somebody with "
        + "admin access to check the practice's mail account under Admin → Settings.";

    private readonly MolargoDatabase _database;
    private readonly IPasswordHasher _hasher;
    private readonly ITenantContext _tenant;
    private readonly ISessionService _session;
    private readonly ISessionHandoff _handoff;
    private readonly IEmailSender _email;
    private readonly IClock _clock;

    public AuthService(
        MolargoDatabase database,
        IPasswordHasher hasher,
        ITenantContext tenant,
        ISessionService session,
        ISessionHandoff handoff,
        IEmailSender email,
        IClock clock)
    {
        _database = database;
        _hasher = hasher;
        _tenant = tenant;
        _session = session;
        _handoff = handoff;
        _email = email;
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

        // The password is right. Whether that is a sign-in depends on the second step.
        if (user.TwoFactorEnabled)
        {
            // The failure count is cleared here, not after the code. The password was
            // correct, and leaving the count standing would let somebody who cannot reach
            // their email walk into a lockout by retrying the one thing they got right.
            //
            // Attached first. The staff list above is read AsNoTracking, so assigning to
            // these three on a detached entity saves nothing and reports nothing — the
            // count would simply never clear for anybody with two-step on, and they would
            // lock themselves out on their fifth correct password.
            user.FailedSignInCount = 0;
            user.LockedUntilUtc = null;
            user.UpdatedUtc = _clock.UtcNow;

            db.Providers.Attach(user);
            db.Entry(user).State = EntityState.Modified;

            return await IssueCodeAsync(db, user, tenant, ct).ConfigureAwait(false);
        }

        await RecordSuccessAsync(db, user, tenant, ct).ConfigureAwait(false);

        // The tenant first, then the session. Everything the shell loads next reads
        // through the tenant filter, so a session established before its tenant would spend
        // its first render reading nothing.
        _tenant.Use(tenant.Id, tenant.Name, tenant.CurrencyCode);
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

        // Then the currency, once there is a context to read it through. It is not in the
        // handoff cookie on purpose: the cookie is what the browser holds, and a currency
        // carried there could be edited into one the practice does not charge in — which
        // would render every figure on the screen with the wrong symbol.
        var practice = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Id == stored.TenantId, ct)
            .ConfigureAwait(false);

        _tenant.Use(stored.TenantId, stored.TenantName, practice?.CurrencyCode);

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
    /// <summary>
    /// Mints a code, stores its verifier and emails it.
    /// </summary>
    /// <remarks>
    /// Fails closed. If the mail will not go, no session is started and the person is told
    /// plainly — see <see cref="MailFailure"/>. The alternative, letting them through when
    /// the second factor cannot be delivered, would mean the protection quietly switches
    /// itself off exactly when the practice's mail account breaks.
    /// </remarks>
    private async Task<SignInResult> IssueCodeAsync(
        MolargoDbContext db, Provider user, Tenant tenant, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        var settings = await db.NotificationSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.TenantId == tenant.Id && !row.IsDeleted, ct)
            .ConfigureAwait(false);

        if (settings is null || !settings.IsConfigured || string.IsNullOrWhiteSpace(user.Email))
        {
            await AuditAsync(db, AuditAction.NotificationFailed, user, tenant,
                    $"Two-step sign-in code for {user.Username} could not be sent: "
                        + (string.IsNullOrWhiteSpace(user.Email)
                            ? "no email address on the staff record"
                            : "no mail account configured"),
                    ct)
                .ConfigureAwait(false);

            return SignInResult.Refused(MailFailure);
        }

        // Every code already outstanding dies first. Two live codes mean two chances for a
        // guess to land, and the person is about to be handed the only one they need.
        var live = await db.SignInCodes
            .IgnoreQueryFilters()
            .Where(row => row.TenantId == tenant.Id
                && row.ProviderId == user.Id
                && row.UsedUtc == null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var row in live)
        {
            row.UsedUtc = now;
            row.UpdatedUtc = now;
        }

        var code = NewCode();

        db.SignInCodes.Add(new SignInCode
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            ProviderId = user.Id,
            CodeHash = _hasher.Hash(code),
            ExpiresUtc = now.Add(CodeLifetime),
            CreatedUtc = now,
            UpdatedUtc = now,
        });

        // Saved before the email goes. A code that arrives and does not work looks like a
        // broken app; one that never arrives looks like a request that did not land, and
        // only the second prompts the person to try again.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var (subject, body) = await ComposeAsync(db, tenant, user, code, ct)
            .ConfigureAwait(false);

        try
        {
            var sent = await _email
                .SendAsync(settings, user.Email!, subject, body, ct,
                    purpose: "Two-step sign-in code")
                .ConfigureAwait(false);

            if (!sent.Succeeded)
            {
                await AuditAsync(db, AuditAction.NotificationFailed, user, tenant,
                        $"Two-step sign-in code for {user.Username} was not delivered: "
                            + sent.Detail,
                        ct)
                    .ConfigureAwait(false);

                return SignInResult.Refused(MailFailure);
            }
        }
        catch (Exception ex)
        {
            // Caught rather than allowed to escape, because an exception here would reach
            // the sign-in screen as a stack trace on the one page strangers can see. The
            // reason is kept where the practice can read it.
            await AuditAsync(db, AuditAction.NotificationFailed, user, tenant,
                    $"Two-step sign-in code for {user.Username} was not delivered: "
                        + ex.Message,
                    ct)
                .ConfigureAwait(false);

            return SignInResult.Refused(MailFailure);
        }

        await AuditAsync(db, AuditAction.Updated, user, tenant,
                $"Two-step sign-in code emailed to {user.Username}", ct)
            .ConfigureAwait(false);

        return SignInResult.AwaitingCode(Mask(user.Email!));
    }

    public async Task<SignInResult> CompleteWithCodeAsync(
        string tenantCode, string username, string code, CancellationToken ct = default)
    {
        var typed = (code ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(tenantCode)
            || string.IsNullOrWhiteSpace(username)
            || typed.Length == 0)
        {
            return SignInResult.Refused(CodeFailure);
        }

        var slug = tenantCode.Trim().ToLowerInvariant();
        var name = username.Trim();

        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Slug == slug && !entry.IsDeleted, ct)
            .ConfigureAwait(false);

        if (tenant is null || !tenant.IsActive) return SignInResult.Refused(CodeFailure);

        var user = await db.Providers
            .IgnoreQueryFilters()
            .Where(provider => provider.TenantId == tenant.Id && !provider.IsDeleted)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var staff = user.FirstOrDefault(provider =>
            string.Equals(provider.Username, name, StringComparison.OrdinalIgnoreCase));

        // Two-step off means there is nothing here to complete. Checked rather than
        // assumed: without it, turning the setting off while somebody held a live code
        // would leave a path into the account that never asks for a password.
        if (staff is null || !staff.CanSignIn || !staff.TwoFactorEnabled)
        {
            return SignInResult.Refused(CodeFailure);
        }

        var now = _clock.UtcNow;

        if (staff.LockedUntilUtc is { } until && until > now)
        {
            return SignInResult.Refused(
                $"That account is locked until {until.ToLocalTime():h:mm tt} after too "
                    + "many failed attempts.",
                until);
        }

        var candidates = await db.SignInCodes
            .IgnoreQueryFilters()
            .Where(row => row.TenantId == tenant.Id
                && row.ProviderId == staff.Id
                && row.UsedUtc == null
                && row.Attempts < CodeAttemptLimit
                && row.ExpiresUtc > now)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var match = candidates.FirstOrDefault(row => _hasher.Verify(typed, row.CodeHash));

        if (match is null)
        {
            // The miss counts against every live code, for the reason on
            // <see cref="CodeAttemptLimit"/>.
            foreach (var row in candidates)
            {
                row.Attempts++;
                row.UpdatedUtc = now;

                // Burned rather than left to expire. A code out of attempts is spent, and
                // leaving it live would let the count be reset by getting a fresh one.
                if (row.Attempts >= CodeAttemptLimit) row.UsedUtc = now;
            }

            await AuditAsync(db, AuditAction.SignInFailed, staff, tenant,
                    $"Wrong two-step code for {staff.Username}", ct)
                .ConfigureAwait(false);

            return SignInResult.Refused(CodeFailure);
        }

        match.UsedUtc = now;
        match.UpdatedUtc = now;

        staff.FailedSignInCount = 0;
        staff.LockedUntilUtc = null;
        staff.LastSignInUtc = now;
        staff.UpdatedUtc = now;

        await AuditAsync(db, AuditAction.SignedIn, staff, tenant,
                $"Signed in as {staff.Username}, confirmed by an emailed code", ct)
            .ConfigureAwait(false);

        _tenant.Use(tenant.Id, tenant.Name, tenant.CurrencyCode);

        await _session
            .SignInAsync(
                staff.Id, staff.FullName, staff.Role, staff.IsOwner, staff.Permissions, ct)
            .ConfigureAwait(false);

        await _handoff
            .CompleteSignInAsync(
                new SignedInUser(tenant.Id, tenant.Name, staff.Id, staff.FullName), ct)
            .ConfigureAwait(false);

        return new SignInResult(
            true, null, tenant.Id, tenant.Name, staff.Id, staff.FullName, null);
    }

    /// <summary>Six digits, from the cryptographic generator.</summary>
    /// <remarks>
    /// Not <c>Random</c>: a seeded sequence is reproducible by anybody who works out when
    /// it was generated, and over a million possibilities that is the whole attack.
    ///
    /// A string, and stored as one. "004821" is a perfectly good code and becomes 4821 the
    /// moment anything treats it as a number.
    /// </remarks>
    private static string NewCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("000000");

    /// <summary>
    /// An address as much of it as the person needs to recognise their own inbox.
    /// </summary>
    /// <remarks>
    /// Shown on the sign-in screen, which anybody can reach — so it has to be enough for
    /// "yes, that is my work address" and not enough to harvest. First letter and domain:
    /// r••••@molargo.test.
    /// </remarks>
    private static string Mask(string address)
    {
        var at = address.IndexOf('@', StringComparison.Ordinal);

        if (at <= 0) return "your email address";

        var first = address[0];
        var domain = address[at..];

        return $"{first}{new string('\u2022', Math.Max(1, at - 1))}{domain}";
    }

    /// <summary>
    /// The wording, from Comms → Templates where the practice has one.
    /// </summary>
    /// <remarks>
    /// A template rather than fixed text, for the same reason the reset code has one: this
    /// is the practice's own email, arriving under their name, and "Molargo" appearing in
    /// it is the vendor speaking for them. It also lets a practice say the one thing only
    /// they know — which colleague to ring at 7am when the code will not come.
    ///
    /// Falls back to the constants below rather than refusing. A practice that switched
    /// the template off, or deleted it, has changed the wording — not revoked everybody's
    /// ability to sign in, which is what an empty body would do.
    /// </remarks>
    private async Task<(string Subject, string Body)> ComposeAsync(
        MolargoDbContext db,
        Tenant tenant,
        Provider staff,
        string code,
        CancellationToken ct)
    {
        var template = await db.MessageTemplates
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                row => row.TenantId == tenant.Id
                    && row.Trigger == MessageTrigger.SignInCode
                    && row.Channel == CommunicationChannel.Email
                    && row.IsActive
                    && !row.IsDeleted,
                ct)
            .ConfigureAwait(false);

        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["StaffName"] = staff.FullName,
            ["StaffUsername"] = staff.Username,
            ["ClinicCode"] = tenant.Slug,
            ["SignInCode"] = code,
            ["CodeExpiry"] = $"{CodeLifetime.TotalMinutes:0} minutes",
            ["PracticeName"] = tenant.Name,
            ["PracticePhone"] = null,
        };

        var subject = MergeFields.Render(template?.Subject ?? CodeSubject, values).Text;

        return (subject, MergeFields.Render(template?.Body ?? CodeBody, values).Text);
    }

    private const string CodeSubject = "Your Molargo sign-in code";

    private const string CodeBody =
        "Hello {{StaffName}},\n\n"
        + "Your sign-in code is {{Code}}.\n\n"
        + "It works once and expires in {{Minutes}} minutes. Type it into the screen that "
        + "asked for it.\n\n"
        + "If you were not signing in, somebody has your password. Change it, and tell "
        + "whoever administers {{PracticeName}}.\n\n"
        + "{{PracticeName}}";

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
