using System.Security.Cryptography;
using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Data;
using DYS.Molargo.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace DYS.Molargo.Shared.Features.Auth.Services;

/// <summary>
/// Self-service password reset, for somebody who cannot sign in.
/// </summary>
/// <remarks>
/// <para>
/// The practice's answer for a sole owner, who is the one person no colleague can reset —
/// resetting an owner's password takes ownership, and a solo practice has nobody else.
/// </para>
/// <para>
/// A six-digit code, not an emailed link. This app has no server, so anything issued lives
/// only in the database of the install that issued it; a link invites the recipient to open
/// it wherever their mail is, which is somewhere else, where it cannot be honoured. A code
/// brings them back to the same install. That is what makes this work on every head with no
/// deep linking at all.
/// </para>
/// <para>
/// Every method here runs with no session and no resolved tenant, which is what makes it the
/// most dangerous service in the app. So it resolves the clinic from the code it was given
/// and applies that tenant by hand to every query, exactly as <c>AuthService</c> does — the
/// ambient filter cannot help when there is nothing signed in to derive it from, and leaving
/// the filter off without applying a tenant would search every practice on the platform.
/// </para>
/// </remarks>
public interface IPasswordResetService
{
    /// <summary>
    /// Issues a code and emails it, if everything lines up.
    /// </summary>
    /// <remarks>
    /// Returns nothing about whether it did. The caller says the same thing either way,
    /// because any difference in the reply is an oracle telling a stranger which clinic
    /// codes and usernames are real.
    /// </remarks>
    Task RequestAsync(string clinicCode, string username, CancellationToken ct = default);

    /// <summary>
    /// Checks the code and sets the new password.
    /// </summary>
    /// <returns>Null on success, or why it was refused.</returns>
    Task<string?> ResetAsync(
        string clinicCode,
        string username,
        string code,
        string newPassword,
        CancellationToken ct = default);
}

/// <inheritdoc cref="IPasswordResetService"/>
public sealed class PasswordResetService : IPasswordResetService
{
    /// <summary>
    /// How long a code lives.
    /// </summary>
    /// <remarks>
    /// Ten minutes, where an emailed link could afford thirty. It only has to cover reading
    /// an inbox and coming back to the app, and over six digits a shorter window is the
    /// cheapest way to cut how many guesses an attacker can fit in.
    /// </remarks>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Wrong guesses before every live code on the account is burned.
    /// </summary>
    /// <remarks>
    /// The arithmetic that makes six digits safe enough. Five attempts against at most three
    /// live codes is roughly fifteen guesses per request cycle against a million — and only
    /// for somebody who already has a correct clinic code and username. Without this the
    /// space is small enough to walk in an afternoon.
    /// </remarks>
    private const int AttemptLimit = 5;

    /// <summary>
    /// How many live codes one account may have at once.
    /// </summary>
    /// <remarks>
    /// Three. Not about brute force — that is what <see cref="AttemptLimit"/> is for — but
    /// about not being a free mail cannon: without it, anybody who guesses a username can
    /// have this app send that inbox a message per click, from the practice's own mail
    /// account, until the practice is reported for spam.
    /// </remarks>
    private const int MaximumLiveCodes = 3;

    /// <summary>Matches the administrative reset's minimum, and for the same reasons.</summary>
    private const int MinimumPasswordLength = 10;

    /// <summary>
    /// One wording for every failure.
    /// </summary>
    /// <remarks>
    /// Wrong code, expired code, burned code, username that never existed — all the same
    /// sentence. The request step deliberately refuses to confirm whether an account is
    /// real, and a verify step that distinguished "too many attempts" from "no such user"
    /// would hand that back. The legitimate user's next action is identical in every case.
    /// </remarks>
    private const string GenericFailure =
        "That code is not right, or it has expired. Codes last ten minutes — ask for "
        + "another one.";

    private readonly MolargoDatabase _database;
    private readonly IPasswordHasher _hasher;
    private readonly IEmailSender _email;
    private readonly IClock _clock;

    public PasswordResetService(
        MolargoDatabase database,
        IPasswordHasher hasher,
        IEmailSender email,
        IClock clock)
    {
        _database = database;
        _hasher = hasher;
        _email = email;
        _clock = clock;
    }

    public async Task RequestAsync(
        string clinicCode, string username, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clinicCode) || string.IsNullOrWhiteSpace(username))
        {
            return;
        }

        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var (tenant, staff) = await FindAsync(db, clinicCode, username, ct)
            .ConfigureAwait(false);

        // Silent to the caller, who learns nothing either way — the difference between "no
        // such user" and "sent" is exactly the oracle this method is shaped to avoid.
        //
        // Not silent to the practice, though. Everything below this line is logged, because
        // every one of these stops ends with somebody waiting for a code that was never
        // going to come, and the audit log is where that gets explained days later.
        //
        // The one case deliberately NOT logged is an unknown clinic code or username. It is
        // the only branch an outsider can reach at will, so logging it would let anybody
        // fill the practice's audit log with entries by typing nonsense at the form.
        if (tenant is null || staff is null) return;

        if (string.IsNullOrWhiteSpace(staff.Email))
        {
            await RecordFailureAsync(
                    db, tenant, staff,
                    "there is no email address on their staff record",
                    ct)
                .ConfigureAwait(false);

            return;
        }

        var settings = await db.NotificationSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.TenantId == tenant.Id, ct)
            .ConfigureAwait(false);

        if (settings is null || !settings.IsConfigured)
        {
            await RecordFailureAsync(
                    db, tenant, staff,
                    "no mail account is set up under Admin → Settings",
                    ct)
                .ConfigureAwait(false);

            return;
        }

        if (!settings.EmailEnabled)
        {
            await RecordFailureAsync(
                    db, tenant, staff, "email notifications are switched off", ct)
                .ConfigureAwait(false);

            return;
        }

        var now = _clock.UtcNow;

        var live = await db.PasswordResetCodes
            .IgnoreQueryFilters()
            .CountAsync(
                row => row.TenantId == tenant.Id
                    && row.ProviderId == staff.Id
                    && row.UsedUtc == null
                    && row.Attempts < AttemptLimit
                    && row.ExpiresUtc > now,
                ct)
            .ConfigureAwait(false);

        if (live >= MaximumLiveCodes) return;

        var code = NewCode();

        db.PasswordResetCodes.Add(new PasswordResetCode
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            ProviderId = staff.Id,
            CodeHash = _hasher.Hash(code),
            ExpiresUtc = now.Add(Lifetime),
            CreatedUtc = now,
            UpdatedUtc = now,
        });

        // Saved before the email goes out. A code that arrives and does not work is worse
        // than one that never arrives: the first looks like the app is broken, the second
        // like the request was not received, and only the second prompts another try.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var (subject, body) = await ComposeAsync(db, tenant, staff, code, ct)
            .ConfigureAwait(false);

        try
        {
            var result = await _email
                .SendAsync(settings, staff.Email!, subject, body, ct,
                    purpose: "Password reset code")
                .ConfigureAwait(false);

            if (!result.Succeeded)
            {
                await RecordFailureAsync(db, tenant, staff, result.Detail, ct)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Still swallowed as far as the caller is concerned — an exception escaping
            // here would take the neutral reply with it and become the oracle this whole
            // method avoids. But it is no longer swallowed entirely: the practice can read
            // what went wrong, which is the difference between a mystery and a mail server
            // naming its own reason.
            await RecordFailureAsync(db, tenant, staff, ex.Message, ct)
                .ConfigureAwait(false);
        }
    }

    public async Task<string?> ResetAsync(
        string clinicCode,
        string username,
        string code,
        string newPassword,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(newPassword)
            || newPassword.Length < MinimumPasswordLength)
        {
            return $"Use at least {MinimumPasswordLength} characters.";
        }

        var typed = (code ?? string.Empty).Trim();

        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var (tenant, staff) = await FindAsync(db, clinicCode, username, ct)
            .ConfigureAwait(false);

        if (tenant is null || staff is null)
        {
            // The same sentence a wrong code gets. Saying "no such account" here would
            // undo the request step's refusal to confirm one.
            return GenericFailure;
        }

        var now = _clock.UtcNow;

        var candidates = await db.PasswordResetCodes
            .IgnoreQueryFilters()
            .Where(row => row.TenantId == tenant.Id
                && row.ProviderId == staff.Id
                && row.UsedUtc == null
                && row.Attempts < AttemptLimit
                && row.ExpiresUtc > now)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var match = candidates.FirstOrDefault(row => _hasher.Verify(typed, row.CodeHash));

        if (match is null)
        {
            // The miss counts against every live code, not just one. An attacker does not
            // know which of the outstanding codes they are guessing at, so counting per row
            // would multiply their attempts by however many they can cause to exist.
            foreach (var row in candidates)
            {
                row.Attempts++;
                row.UpdatedUtc = now;

                // Burned rather than left to expire. A code that has run out of attempts is
                // spent, and leaving it live would let the count be reset by requesting a
                // fresh one alongside it.
                if (row.Attempts >= AttemptLimit) row.UsedUtc = now;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return GenericFailure;
        }

        staff.PasswordHash = _hasher.Hash(newPassword);
        staff.PasswordUpdatedUtc = now;

        // The lockout goes with it. Somebody resetting their password has usually just spent
        // five attempts locking themselves out, and handing them a working password they
        // cannot use for fifteen minutes is not a reset.
        staff.FailedSignInCount = 0;
        staff.LockedUntilUtc = null;
        staff.UpdatedUtc = now;

        // Every live code dies, not only the one spent. A second outstanding code is a
        // second key to a door whose lock has just been changed at the owner's request.
        foreach (var row in candidates)
        {
            row.UsedUtc = now;
            row.UpdatedUtc = now;
        }

        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Action = AuditAction.Updated,
            EntityName = nameof(Provider),
            EntityId = staff.Id,
            ProviderId = staff.Id,
            ProviderName = staff.FullName,
            OccurredUtc = now,
            Detail = $"Password reset by {staff.FullName} using an emailed code. Any "
                + "lockout was cleared, and any other outstanding code was cancelled.",
            CreatedUtc = now,
            UpdatedUtc = now,
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return null;
    }

    /// <summary>
    /// The clinic and the staff member behind a code and a username, if both are real.
    /// </summary>
    /// <remarks>
    /// The tenant is looked up without the filter because it is the thing that decides what
    /// the filter would be; staff are then read with the filter bypassed and that tenant
    /// applied by hand. Bypassing without the Where would search every practice on the
    /// platform for that username.
    /// </remarks>
    private static async Task<(Tenant? Clinic, Provider? Staff)> FindAsync(
        MolargoDbContext db, string clinicCode, string username, CancellationToken ct)
    {
        var code = (clinicCode ?? string.Empty).Trim().ToLowerInvariant();
        var name = (username ?? string.Empty).Trim();

        if (code.Length == 0 || name.Length == 0) return (null, null);

        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Slug == code && !entry.IsDeleted, ct)
            .ConfigureAwait(false);

        if (tenant is null || !tenant.IsActive) return (null, null);

        var staff = await db.Providers
            .IgnoreQueryFilters()
            .Where(provider => provider.TenantId == tenant.Id && !provider.IsDeleted)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var user = staff.FirstOrDefault(provider =>
            string.Equals(provider.Username, name, StringComparison.OrdinalIgnoreCase));

        return user is { IsActive: true } ? (tenant, user) : (null, null);
    }

    /// <summary>
    /// Records that a reset code could not be delivered.
    /// </summary>
    /// <remarks>
    /// Attributed to the account it was for, not to an actor: nobody is signed in, and the
    /// person who asked is the person it was about. Saved on its own rather than folded
    /// into the caller's unit of work, because most of the paths that reach here return
    /// immediately afterwards and would otherwise never commit it.
    /// </remarks>
    private async Task RecordFailureAsync(
        MolargoDbContext db,
        Tenant tenant,
        Provider staff,
        string reason,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;

        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Action = AuditAction.NotificationFailed,
            EntityName = nameof(Provider),
            EntityId = staff.Id,
            ProviderId = staff.Id,
            ProviderName = staff.FullName,
            OccurredUtc = now,
            Detail = $"No reset code could be sent to {staff.FullName} "
                + $"({staff.Username}) — {reason}.",
            CreatedUtc = now,
            UpdatedUtc = now,
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The email, from the practice's own template where it has one.</summary>
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
                    && row.Trigger == MessageTrigger.PasswordResetRequested
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
            ["ResetCode"] = code,
            ["CodeExpiry"] = $"{Lifetime.TotalMinutes:0} minutes",
            ["PracticeName"] = tenant.Name,
            ["PracticePhone"] = null,
        };

        var subject = MergeFields
            .Render(template?.Subject ?? FallbackSubject, values)
            .Text;

        return (subject, MergeFields.Render(template?.Body ?? FallbackBody, values).Text);
    }

    /// <summary>
    /// Six digits, zero-padded.
    /// </summary>
    /// <remarks>
    /// <see cref="RandomNumberGenerator"/> rather than <c>Random</c>: a seeded pseudo-random
    /// sequence is reproducible by anybody who works out when it was generated, and over a
    /// space this small that is the whole attack.
    ///
    /// Returned as a string and stored as one. "004821" is a perfectly good code and becomes
    /// 4821 the moment anything treats it as a number.
    /// </remarks>
    private static string NewCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("000000");

    private const string FallbackSubject = "Your Molargo reset code";

    private const string FallbackBody =
        "Hello {{StaffName}},\n\n"
        + "Somebody asked to reset the password for {{StaffUsername}} at "
        + "{{PracticeName}}.\n\n"
        + "Your code is {{ResetCode}}\n\n"
        + "Type it back into the app on the same device you asked from. It expires in "
        + "{{CodeExpiry}}.\n\n"
        + "If that was not you, ignore this email — your password has not changed and the "
        + "code will lapse on its own.\n\n"
        + "{{PracticeName}}";
}
