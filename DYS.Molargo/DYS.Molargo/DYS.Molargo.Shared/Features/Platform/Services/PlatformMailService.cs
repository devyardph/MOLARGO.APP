using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Data;
using DYS.Molargo.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace DYS.Molargo.Shared.Features.Platform.Services;

/// <summary>The vendor's sending account, with the password masked for the screen.</summary>
/// <param name="HasPassword">
/// Whether one is stored. The value never leaves the service, like the SMS key beside it.
/// </param>
public sealed record PlatformMailRow(
    string? SenderAddress,
    string? SenderName,
    string SmtpHost,
    int SmtpPort,
    bool HasPassword,
    bool IsEnabled,
    DateTime? LastTestUtc,
    string? LastTestResult,
    bool LastTestSucceeded)
{
    /// <summary>True where a message could actually go out.</summary>
    public bool IsUsable =>
        IsEnabled
        && HasPassword
        && !string.IsNullOrWhiteSpace(SenderAddress)
        && !string.IsNullOrWhiteSpace(SmtpHost);
}

/// <summary>
/// The mail account the platform itself sends from.
/// </summary>
/// <remarks>
/// <para>
/// Separate from a clinic's own account under Admin → Settings, and it has to be: the first
/// message this product ever sends somebody is the code that verifies their address at
/// signup, and at that moment there is no clinic, no staff record and nowhere for a
/// practice's own SMTP details to have been entered. Somebody has to own that send, and it
/// is the vendor — the same reasoning that puts the SMS gateway on the platform.
/// </para>
/// <para>
/// Stored as a <see cref="NotificationSettings"/> row against the vendor's own tenant
/// rather than as a new table. It is the same six fields doing the same job, and a second
/// entity would be a second place to fix the day SMTP handling changes.
/// </para>
/// </remarks>
public interface IPlatformMailService
{
    /// <summary>The account as it stands, or null where none has been set up.</summary>
    Task<PlatformMailRow?> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves the account. Null on success, or the refusal.
    /// </summary>
    /// <param name="appPassword">
    /// Left blank to keep the stored one. Blank is not "clear it" — a save that wiped the
    /// credential because a masked box was not retyped would stop every signup silently.
    /// </param>
    Task<string?> SaveAsync(
        string senderAddress,
        string? senderName,
        string smtpHost,
        int smtpPort,
        bool isEnabled,
        string? appPassword,
        CancellationToken ct = default);

    /// <summary>Sends a real message to one address, and records what came back.</summary>
    Task<string> TestAsync(string toAddress, CancellationToken ct = default);
}

/// <inheritdoc cref="IPlatformMailService"/>
public sealed class PlatformMailService : IPlatformMailService
{
    private readonly MolargoDatabase _database;
    private readonly ISessionService _session;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly IEmailSender _email;

    public PlatformMailService(
        MolargoDatabase database,
        ISessionService session,
        ITenantContext tenant,
        IClock clock,
        IEmailSender email)
    {
        _database = database;
        _session = session;
        _tenant = tenant;
        _clock = clock;
        _email = email;
    }

    public async Task<PlatformMailRow?> GetAsync(CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false)) return null;

        var row = await FindAsync(db, ct).ConfigureAwait(false);

        if (row is null) return null;

        return new PlatformMailRow(
            row.SenderAddress,
            row.SenderName,
            row.SmtpHost,
            row.SmtpPort,

            // Whether, never what.
            !string.IsNullOrWhiteSpace(row.AppPassword),
            row.EmailEnabled,
            row.LastTestUtc,
            row.LastTestResult,
            row.LastTestSucceeded);
    }

    public async Task<string?> SaveAsync(
        string senderAddress,
        string? senderName,
        string smtpHost,
        int smtpPort,
        bool isEnabled,
        string? appPassword,
        CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false))
        {
            return PlatformGuard.NotPermitted;
        }

        var address = (senderAddress ?? string.Empty).Trim();

        // Parsed rather than pattern-matched. A signup code posted to an address this
        // cannot form is a signup that dies at the last step with nothing on screen to
        // explain it.
        if (!IsAddress(address))
        {
            return "That is not an email address the platform can send from.";
        }

        var host = (smtpHost ?? string.Empty).Trim();

        if (host.Length == 0) return "The SMTP host is needed — smtp.gmail.com, and so on.";

        if (smtpPort is < 1 or > 65535) return "The SMTP port has to be between 1 and 65535.";

        var password = string.IsNullOrWhiteSpace(appPassword) ? null : appPassword.Trim();

        var row = await FindAsync(db, ct).ConfigureAwait(false);
        var isNew = row is null;
        var now = _clock.UtcNow;

        if (isNew && password is null)
        {
            return "A new sending account needs its app password.";
        }

        row ??= new NotificationSettings
        {
            Id = Guid.NewGuid(),

            // The vendor's own tenant, like a plan or a gateway.
            TenantId = _tenant.TenantId,
            CreatedUtc = now,
        };

        row.SenderAddress = address;
        row.SenderName = string.IsNullOrWhiteSpace(senderName) ? null : senderName.Trim();
        row.SmtpHost = host;
        row.SmtpPort = smtpPort;
        row.EmailEnabled = isEnabled;
        row.UpdatedUtc = now;

        // Only when one was typed. A blank box means "leave it", not "clear it".
        if (password is not null)
        {
            row.AppPassword = password;

            // The stored result described the old password, and would read as a pass for a
            // credential that has just been replaced.
            row.LastTestUtc = null;
            row.LastTestResult = null;
            row.LastTestSucceeded = false;
        }

        if (isNew) db.NotificationSettings.Add(row);

        await AuditAsync(db, row.Id,
            isNew ? AuditAction.Created : AuditAction.Updated,
            (isNew ? "Set up" : "Changed")
                + $" the platform's sending account ({address} via {host}:{smtpPort})"
                + (password is null ? string.Empty : ", with a new app password"),
            ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string> TestAsync(string toAddress, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false))
        {
            return PlatformGuard.NotPermitted;
        }

        var to = (toAddress ?? string.Empty).Trim();

        if (!IsAddress(to)) return "Give an address to send the test to.";

        var row = await FindAsync(db, ct).ConfigureAwait(false);

        if (row is null || !row.IsConfigured)
        {
            return "No sending account is set up yet.";
        }

        var now = _clock.UtcNow;
        string outcome;

        try
        {
            var sent = await _email
                .SendAsync(row, to, "Molargo platform test",
                    "This is a test from the Molargo platform's sending account. "
                        + "If it arrived, signup codes will too.", ct)
                .ConfigureAwait(false);

            row.LastTestSucceeded = sent.Succeeded;
            outcome = sent.Detail;
        }
        catch (Exception error)
        {
            // Caught, like every other send here. A misconfigured host must report itself
            // rather than throw out of a screen whose whole purpose is finding that out.
            row.LastTestSucceeded = false;
            outcome = error.Message;
        }

        row.LastTestUtc = now;
        row.LastTestResult = outcome.Length > 500 ? outcome[..500] : outcome;
        row.UpdatedUtc = now;

        await AuditAsync(db, row.Id, AuditAction.Updated,
            $"Tested the platform's sending account to {to} — "
                + (row.LastTestSucceeded ? "delivered" : "failed") + $": {row.LastTestResult}",
            ct)
            .ConfigureAwait(false);

        return row.LastTestSucceeded
            ? $"Sent to {to}."
            : $"Failed — {row.LastTestResult}";
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// The platform's own row, read past the filter.
    /// </summary>
    /// <remarks>
    /// By tenant id rather than through the ambient filter, which follows whichever clinic
    /// the operator is looking at — the same reason every other vendor read here bypasses
    /// it.
    /// </remarks>
    private Task<NotificationSettings?> FindAsync(
        MolargoDbContext db, CancellationToken ct) =>
        db.NotificationSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                row => row.TenantId == _tenant.TenantId && !row.IsDeleted, ct);

    private static bool IsAddress(string value) =>
        System.Net.Mail.MailAddress.TryCreate(value, out _);

    private Task<bool> IsSuperAdminAsync(MolargoDbContext db, CancellationToken ct) =>
        PlatformGuard.IsSuperAdminAsync(db, _session.ProviderId, ct);

    private async Task AuditAsync(
        MolargoDbContext db,
        Guid settingsId,
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
            EntityName = nameof(NotificationSettings),
            EntityId = settingsId,
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
