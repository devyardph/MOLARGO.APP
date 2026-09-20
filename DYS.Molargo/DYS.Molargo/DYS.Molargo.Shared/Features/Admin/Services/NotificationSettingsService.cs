using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Repositories;
using DYS.Molargo.Shared.Services;

namespace DYS.Molargo.Shared.Features.Admin.Services;

/// <summary>
/// The clinic's outgoing-mail account: reading it, saving it, and proving it works.
/// </summary>
/// <remarks>
/// Its own service rather than more of <c>AdminService</c>, because this one holds a
/// credential and reaches the network. Both are worth keeping in a file somebody can read
/// end to end.
/// </remarks>
public interface INotificationSettingsService
{
    /// <summary>
    /// The clinic's settings, creating an unconfigured row on first read.
    /// </summary>
    /// <remarks>
    /// Never returns null, so the screen has no empty state to special-case. The row it
    /// creates is not saved until something is actually set.
    /// </remarks>
    Task<NotificationSettings> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves the settings. Returns a refusal, or null.
    /// </summary>
    /// <param name="appPassword">
    /// The new app password, or null to leave the stored one alone. Null is how the form
    /// saves the other fields without the password having to be retyped — and without the
    /// screen ever having to render it back in order to post it.
    /// </param>
    Task<string?> SaveAsync(
        NotificationSettings settings,
        string? appPassword,
        CancellationToken ct = default);

    /// <summary>Forgets the stored app password.</summary>
    Task<string?> ClearPasswordAsync(CancellationToken ct = default);

    /// <summary>
    /// Sends one message to prove the account works, and records the outcome.
    /// </summary>
    Task<EmailResult> SendTestAsync(string toAddress, CancellationToken ct = default);
}

/// <inheritdoc cref="INotificationSettingsService"/>
public sealed class NotificationSettingsService : INotificationSettingsService
{
    private readonly IRepository<NotificationSettings> _settings;
    private readonly IRepository<PracticeLocation> _locations;
    private readonly IRepository<AuditEntry> _audit;
    private readonly IRepository<Provider> _providers;
    private readonly IEmailSender _email;
    private readonly ISessionService _session;
    private readonly IPracticeGuard _guard;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public NotificationSettingsService(
        IRepository<NotificationSettings> settings,
        IRepository<PracticeLocation> locations,
        IRepository<AuditEntry> audit,
        IRepository<Provider> providers,
        IEmailSender email,
        ISessionService session,
        ITenantContext tenant,
        IPracticeGuard guard,
        IClock clock)
    {
        _settings = settings;
        _locations = locations;
        _audit = audit;
        _providers = providers;
        _email = email;
        _session = session;
        _tenant = tenant;
        _guard = guard;
        _clock = clock;
    }

    public async Task<NotificationSettings> GetAsync(CancellationToken ct = default)
    {
        var rows = await _settings.ListAsync(ct: ct).ConfigureAwait(false);

        // One row per clinic, and the tenant filter already confines this to one. Taking
        // the first rather than asserting there is exactly one: a duplicate would be a bug
        // worth fixing, not a reason to fail the screen closed.
        return rows.FirstOrDefault() ?? new NotificationSettings();
    }

    public async Task<string?> SaveAsync(
        NotificationSettings settings,
        string? appPassword,
        CancellationToken ct = default)
    {
        var denied = await _guard
            .RefuseAsync(PracticePermissions.ManageSettings, ct)
            .ConfigureAwait(false);

        if (denied is not null) return denied;

        if (settings.EmailEnabled && string.IsNullOrWhiteSpace(settings.SenderAddress))
        {
            return "Add the sending address before turning notifications on.";
        }

        foreach (var (label, address) in new[]
        {
            ("sending", settings.SenderAddress),
            ("reply-to", settings.ReplyToAddress),
            ("alerts", settings.AlertsToAddress),
        })
        {
            if (!string.IsNullOrWhiteSpace(address) && !LooksLikeEmail(address))
            {
                return $"That {label} address does not look like an email address.";
            }
        }

        if (settings.SmtpPort is < 1 or > 65535)
        {
            return "The SMTP port has to be between 1 and 65535. Gmail uses 587.";
        }

        var existing = await GetAsync(ct).ConfigureAwait(false);

        // Carried over when the form did not supply one, which is the normal case: the
        // screen never renders the stored password, so it has nothing to post back.
        var password = string.IsNullOrWhiteSpace(appPassword)
            ? existing.AppPassword
            : Normalise(appPassword);

        if (settings.EmailEnabled && string.IsNullOrWhiteSpace(password))
        {
            return "Add the app password before turning notifications on.";
        }

        var changed = Describe(existing, settings, appPassword);

        existing.EmailEnabled = settings.EmailEnabled;
        existing.SenderAddress = Trim(settings.SenderAddress);
        existing.SenderName = Trim(settings.SenderName);
        existing.ReplyToAddress = Trim(settings.ReplyToAddress);
        existing.AlertsToAddress = Trim(settings.AlertsToAddress);
        existing.NotifyReceipts = settings.NotifyReceipts;
        existing.NotifyLowStock = settings.NotifyLowStock;
        existing.NotifyDailySummary = settings.NotifyDailySummary;
        existing.DailySummaryRecipients = Trim(settings.DailySummaryRecipients);
        existing.DailySummaryAt = settings.DailySummaryAt;
        existing.SmtpHost = Trim(settings.SmtpHost) ?? "smtp.gmail.com";
        existing.SmtpPort = settings.SmtpPort;
        existing.AppPassword = password;

        // A new password has never been proved, so the previous test result no longer says
        // anything about the credential in place.
        if (!string.IsNullOrWhiteSpace(appPassword))
        {
            existing.LastTestUtc = null;
            existing.LastTestResult = null;
            existing.LastTestSucceeded = false;
        }

        await _settings.SaveAsync(existing, ct).ConfigureAwait(false);

        await RecordAsync(existing.Id, changed, ct).ConfigureAwait(false);

        return null;
    }

    public async Task<string?> ClearPasswordAsync(CancellationToken ct = default)
    {
        var denied = await _guard
            .RefuseAsync(PracticePermissions.ManageSettings, ct)
            .ConfigureAwait(false);

        if (denied is not null) return denied;

        var existing = await GetAsync(ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(existing.AppPassword))
        {
            return "There is no app password stored.";
        }

        existing.AppPassword = null;

        // Turned off with it. Leaving it on with no credential means every send fails
        // silently from the practice's point of view.
        existing.EmailEnabled = false;
        existing.LastTestUtc = null;
        existing.LastTestResult = null;
        existing.LastTestSucceeded = false;

        await _settings.SaveAsync(existing, ct).ConfigureAwait(false);

        await RecordAsync(
            existing.Id,
            "Removed the stored app password and turned email notifications off",
            ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<EmailResult> SendTestAsync(
        string toAddress, CancellationToken ct = default)
    {
        var settings = await GetAsync(ct).ConfigureAwait(false);

        if (!LooksLikeEmail(toAddress))
        {
            return EmailResult.Failed("That address does not look like an email address.");
        }

        var locations = await _locations.ListAsync(ct: ct).ConfigureAwait(false);
        var practice = locations.FirstOrDefault()?.Name ?? _tenant.TenantName ?? "Molargo";

        var result = await _email
            .SendAsync(
                settings,
                toAddress,
                $"Test from {practice}",
                $"""
                 This is a test from Molargo, sent to check the practice's outgoing mail
                 account.

                 Practice: {practice}
                 Sent from: {settings.SenderAddress}
                 Sent by: {_session.UserDisplayName ?? "unknown"}
                 At: {_clock.UtcNow.ToLocalTime():dddd d MMMM yyyy, h:mm tt}

                 If this arrived, notifications can be sent from this account.
                 """,
                ct,
                purpose: "Mail account test")
            .ConfigureAwait(false);

        // Recorded either way. A failure is the more useful of the two to keep: it is what
        // explains, days later, why nothing is arriving.
        settings.LastTestUtc = _clock.UtcNow;
        settings.LastTestResult = result.Detail;
        settings.LastTestSucceeded = result.Succeeded;

        await _settings.SaveAsync(settings, ct).ConfigureAwait(false);

        // A failure is filed as a failure. It used to land as "Changed", which put the one
        // entry that explains why nothing is arriving in among every settings edit.
        await RecordAsync(
            settings.Id,
            result.Succeeded
                ? $"Test email sent to {toAddress}"
                : $"Test email to {toAddress} failed: {result.Detail}",
            ct,
            result.Succeeded ? AuditAction.Updated : AuditAction.NotificationFailed)
            .ConfigureAwait(false);

        return result;
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// Strips the spaces Google shows an app password with.
    /// </summary>
    /// <remarks>
    /// Google displays the sixteen characters in groups of four, and people paste what they
    /// see. SMTP rejects it with the spaces in, which reads as a wrong password.
    /// </remarks>
    private static string Normalise(string password) =>
        password.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// A deliberately shallow check: something before an @, something after, and a dot.
    /// </summary>
    /// <remarks>
    /// Not a full grammar. The only address that matters is one the mail server accepts,
    /// and a stricter rule here rejects valid addresses while still not proving delivery —
    /// the test send is what proves it.
    /// </remarks>
    private static bool LooksLikeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var at = value.IndexOf('@', StringComparison.Ordinal);

        return at > 0
            && at < value.Length - 1
            && value.IndexOf('.', at) > at + 1
            && !value.Contains(' ', StringComparison.Ordinal);
    }

    /// <summary>
    /// What changed, in words, for the audit entry — and never the password itself.
    /// </summary>
    /// <remarks>
    /// The trail records that a credential was replaced, not what it was replaced with. An
    /// audit log that quotes secrets is a second copy of them.
    /// </remarks>
    private static string Describe(
        NotificationSettings before, NotificationSettings after, string? newPassword)
    {
        var parts = new List<string>();

        if (before.EmailEnabled != after.EmailEnabled)
        {
            parts.Add(after.EmailEnabled
                ? "turned email notifications on"
                : "turned email notifications off");
        }

        if (!string.Equals(before.SenderAddress, Trim(after.SenderAddress), StringComparison.Ordinal))
        {
            parts.Add($"sending address set to {Trim(after.SenderAddress) ?? "none"}");
        }

        if (!string.IsNullOrWhiteSpace(newPassword)) parts.Add("app password replaced");

        if (!string.Equals(before.SmtpHost, Trim(after.SmtpHost), StringComparison.Ordinal)
            || before.SmtpPort != after.SmtpPort)
        {
            parts.Add($"server set to {Trim(after.SmtpHost)}:{after.SmtpPort}");
        }

        return parts.Count == 0
            ? "Saved notification settings with no changes"
            : "Notification settings — " + string.Join("; ", parts);
    }

    private async Task RecordAsync(
        Guid settingsId,
        string detail,
        CancellationToken ct,
        AuditAction action = AuditAction.Updated)
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
                    EntityName = nameof(NotificationSettings),
                    EntityId = settingsId,
                    ProviderId = providerId,
                    ProviderName = actor?.FullName ?? _session.UserDisplayName,
                    OccurredUtc = _clock.UtcNow,
                    Detail = detail,
                },
                ct)
            .ConfigureAwait(false);
    }
}
