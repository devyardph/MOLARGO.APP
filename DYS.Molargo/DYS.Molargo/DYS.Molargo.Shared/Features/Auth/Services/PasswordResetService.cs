using System.Security.Cryptography;
using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Components;
using DYS.Molargo.Shared.Data;
using DYS.Molargo.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace DYS.Molargo.Shared.Features.Auth.Services;

/// <summary>
/// Who a live reset link belongs to.
/// </summary>
/// <param name="ClinicCode">Prefilled on the sign-in form afterwards, so they need not recall it.</param>
public sealed record ResetSubject(
    Guid ProviderId, string Name, string Username, string ClinicCode, string PracticeName);

/// <summary>
/// Self-service password reset, for somebody who cannot sign in.
/// </summary>
/// <remarks>
/// <para>
/// The practice's answer for a sole owner, who is the one person no colleague can reset —
/// resetting an owner's password takes ownership, and a solo practice has nobody else.
/// </para>
/// <para>
/// Every method here runs with no session and no resolved tenant, which is what makes it
/// the most dangerous service in the app. So it resolves the clinic from the code it was
/// given and applies that tenant by hand to every subsequent query, exactly as
/// <c>AuthService</c> does — the ambient filter cannot help when there is nothing signed in
/// to derive it from, and leaving the filter off without applying a tenant would search
/// every practice on the platform.
/// </para>
/// </remarks>
public interface IPasswordResetService
{
    /// <summary>
    /// Issues a link and emails it, if everything lines up.
    /// </summary>
    /// <remarks>
    /// Returns nothing about whether it did. The caller says the same thing either way —
    /// see <c>ForgotPassword.razor</c> — because any difference in the reply is an oracle
    /// telling a stranger which clinic codes and usernames are real.
    /// </remarks>
    Task RequestAsync(string clinicCode, string username, CancellationToken ct = default);

    /// <summary>Who the link is for, or null where it is spent, expired or invented.</summary>
    Task<ResetSubject?> ValidateAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Sets the new password and burns the link.
    /// </summary>
    /// <returns>Null on success, or why it was refused.</returns>
    Task<string?> CompleteAsync(
        string token, string newPassword, CancellationToken ct = default);
}

/// <inheritdoc cref="IPasswordResetService"/>
public sealed class PasswordResetService : IPasswordResetService
{
    /// <summary>
    /// How long a link lives.
    /// </summary>
    /// <remarks>
    /// Thirty minutes. Long enough to walk to an inbox on a practice's connection, short
    /// enough that a link left sitting in a mailbox stops being a working key to the
    /// account before the day is out.
    /// </remarks>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How many live links one account may have at once.
    /// </summary>
    /// <remarks>
    /// Three. The limit is not about brute force — the token is 256 bits — it is about not
    /// being a free mail cannon: without it, anybody who guesses a username can have this
    /// app send that inbox a message per click, from the practice's own mail account, until
    /// the practice is reported for spam.
    /// </remarks>
    private const int MaximumLiveTokens = 3;

    /// <summary>Matches the administrative reset's minimum, and for the same reasons.</summary>
    private const int MinimumPasswordLength = 10;

    private readonly MolargoDatabase _database;
    private readonly IPasswordHasher _hasher;
    private readonly IEmailSender _email;
    private readonly IAppLinks _links;
    private readonly IClock _clock;

    public PasswordResetService(
        MolargoDatabase database,
        IPasswordHasher hasher,
        IEmailSender email,
        IAppLinks links,
        IClock clock)
    {
        _database = database;
        _hasher = hasher;
        _email = email;
        _links = links;
        _clock = clock;
    }

    public async Task RequestAsync(
        string clinicCode, string username, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clinicCode) || string.IsNullOrWhiteSpace(username))
        {
            return;
        }

        // Nothing is minted on a head whose links cannot come back. A token written here
        // could only ever be redeemed on this same install, so issuing one would put a
        // live credential in a mailbox that no click can spend — and would count against
        // the rate limit while it sat there.
        if (!_links.SupportsEmailedLinks) return;

        var code = clinicCode.Trim().ToLowerInvariant();
        var name = username.Trim();

        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Slug == code && !entry.IsDeleted, ct)
            .ConfigureAwait(false);

        if (tenant is null || !tenant.IsActive) return;

        // Filter bypassed, tenant applied by hand. Nothing is signed in, so the ambient
        // filter would match no rows at all — and bypassing it without the Where below
        // would search every practice on the platform for that username.
        var user = await db.Providers
            .IgnoreQueryFilters()
            .Where(provider => provider.TenantId == tenant.Id && !provider.IsDeleted)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var staff = user.FirstOrDefault(provider =>
            string.Equals(provider.Username, name, StringComparison.OrdinalIgnoreCase));

        // Every one of these is a silent stop. A caller that learned the difference between
        // "no such user", "no email on file" and "sent" could map the practice's staff list
        // from outside the app.
        if (staff is null || !staff.IsActive) return;
        if (string.IsNullOrWhiteSpace(staff.Email)) return;

        var settings = await db.NotificationSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.TenantId == tenant.Id, ct)
            .ConfigureAwait(false);

        if (settings is null || !settings.IsConfigured || !settings.EmailEnabled) return;

        var now = _clock.UtcNow;

        var live = await db.PasswordResetTokens
            .IgnoreQueryFilters()
            .CountAsync(
                row => row.TenantId == tenant.Id
                    && row.ProviderId == staff.Id
                    && row.UsedUtc == null
                    && row.ExpiresUtc > now,
                ct)
            .ConfigureAwait(false);

        if (live >= MaximumLiveTokens) return;

        var token = NewToken();

        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            ProviderId = staff.Id,
            TokenHash = HashToken(token),
            ExpiresUtc = now.Add(Lifetime),
            CreatedUtc = now,
            UpdatedUtc = now,
        });

        // Saved before the email goes out. A link that arrives and does not work is worse
        // than one that never arrives: the first looks like the app is broken, the second
        // like the request was not received, and only the second prompts another try.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var (subject, body) = await ComposeAsync(db, tenant, staff, token, ct)
            .ConfigureAwait(false);

        try
        {
            await _email
                .SendAsync(settings, staff.Email!, subject, body, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            // Swallowed on purpose, and the only place in this app where that is right.
            // The caller is told nothing either way, so there is nothing to report to —
            // and an exception escaping here would take the neutral reply with it and
            // become the oracle the whole method is shaped to avoid.
        }
    }

    public async Task<ResetSubject?> ValidateAsync(
        string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var (row, staff, clinic) = await FindLiveAsync(db, token, ct).ConfigureAwait(false);

        return row is not null && staff is not null && clinic is not null
            ? new ResetSubject(
                staff.Id,
                staff.FullName,
                staff.Username ?? string.Empty,
                clinic.Slug,
                clinic.Name)
            : null;
    }

    public async Task<string?> CompleteAsync(
        string token, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(newPassword)
            || newPassword.Length < MinimumPasswordLength)
        {
            return $"Use at least {MinimumPasswordLength} characters.";
        }

        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var (row, staff, tenant) = await FindLiveAsync(db, token, ct).ConfigureAwait(false);

        if (row is null || staff is null || tenant is null)
        {
            return "That link has expired or has already been used. Request another one.";
        }

        var now = _clock.UtcNow;

        staff.PasswordHash = _hasher.Hash(newPassword);
        staff.PasswordUpdatedUtc = now;

        // The lockout goes with it. Somebody resetting their password has usually just
        // spent five attempts locking themselves out, and handing them a working password
        // they cannot use for fifteen minutes is not a reset.
        staff.FailedSignInCount = 0;
        staff.LockedUntilUtc = null;
        staff.UpdatedUtc = now;

        row.UsedUtc = now;
        row.UpdatedUtc = now;

        // Every other live link for this account dies too. A second outstanding email is a
        // second key to a door whose lock has just been changed at the owner's request.
        var others = await db.PasswordResetTokens
            .IgnoreQueryFilters()
            .Where(entry => entry.TenantId == tenant.Id
                && entry.ProviderId == staff.Id
                && entry.Id != row.Id
                && entry.UsedUtc == null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var stale in others)
        {
            stale.UsedUtc = now;
            stale.UpdatedUtc = now;
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
            Detail = $"Password reset by {staff.FullName} using an emailed link. Any "
                + "lockout was cleared, and any other outstanding link was cancelled.",
            CreatedUtc = now,
            UpdatedUtc = now,
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return null;
    }

    /// <summary>
    /// The row behind a token, with its account and clinic, if it is still good.
    /// </summary>
    /// <remarks>
    /// Looked up by hash rather than by id, so the token in the URL is the only thing that
    /// identifies it — a row id in a link would be guessable and would need the token
    /// checked separately anyway.
    ///
    /// Filters bypassed throughout, because nothing is signed in. Safe here for the reason
    /// it is not safe generally: the hash is 256 bits of randomness, so a match identifies
    /// exactly one row and the tenant is read back from it rather than trusted from input.
    /// </remarks>
    private async Task<(PasswordResetToken? Row, Provider? Staff, Tenant? Clinic)>
        FindLiveAsync(MolargoDbContext db, string token, CancellationToken ct)
    {
        var hash = HashToken(token.Trim());
        var now = _clock.UtcNow;

        var row = await db.PasswordResetTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                entry => entry.TokenHash == hash
                    && entry.UsedUtc == null
                    && entry.ExpiresUtc > now
                    && !entry.IsDeleted,
                ct)
            .ConfigureAwait(false);

        if (row is null) return (null, null, null);

        var staff = await db.Providers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                provider => provider.Id == row.ProviderId && !provider.IsDeleted, ct)
            .ConfigureAwait(false);

        var clinic = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(entry => entry.Id == row.TenantId, ct)
            .ConfigureAwait(false);

        // A link outliving the account it belongs to. Deactivated between the request and
        // the click, or the clinic suspended — either way the reset must not complete, and
        // the link is treated as though it had never existed.
        return staff is { IsActive: true } && clinic is { IsActive: true }
            ? (row, staff, clinic)
            : (null, null, null);
    }

    /// <summary>The email, from the practice's own template where it has one.</summary>
    private async Task<(string Subject, string Body)> ComposeAsync(
        MolargoDbContext db,
        Tenant tenant,
        Provider staff,
        string token,
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
            ["ResetLink"] = _links.ResetPassword(token),
            ["LinkExpiry"] = $"{Lifetime.TotalMinutes:0} minutes",
            ["PracticeName"] = tenant.Name,
            ["PracticePhone"] = null,
        };

        var subject = MergeFields
            .Render(template?.Subject ?? FallbackSubject, values)
            .Text;

        return (subject, MergeFields.Render(template?.Body ?? FallbackBody, values).Text);
    }

    /// <summary>
    /// 256 bits, URL-safe.
    /// </summary>
    /// <remarks>
    /// <see cref="RandomNumberGenerator"/>, never <c>Random</c>. This value is the only
    /// thing standing between a stranger and the account, and a seeded pseudo-random
    /// sequence is reproducible by anybody who works out when it was generated.
    /// </remarks>
    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static string HashToken(string token) =>
        Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private const string FallbackSubject = "Reset your Molargo password";

    private const string FallbackBody =
        "Hello {{StaffName}},\n\n"
        + "Somebody asked to reset the password for {{StaffUsername}} at "
        + "{{PracticeName}}.\n\n"
        + "Open this link to set a new one. It works once and expires in "
        + "{{LinkExpiry}}:\n\n{{ResetLink}}\n\n"
        + "If that was not you, ignore this email — your password has not changed and the "
        + "link will lapse on its own.\n\n"
        + "{{PracticeName}}";
}
