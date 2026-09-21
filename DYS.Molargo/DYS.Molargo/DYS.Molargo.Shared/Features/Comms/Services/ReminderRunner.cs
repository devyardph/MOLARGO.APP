using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Data;
using DYS.Molargo.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace DYS.Molargo.Shared.Features.Comms.Services;

/// <summary>One appointment waiting on one step of the cadence.</summary>
public sealed record ReminderDue(
    Guid AppointmentId,
    Guid PatientId,
    string PatientName,
    DateTime StartLocal,
    string Reason,
    int OffsetDays,
    bool CanEmail,
    bool CanText,
    string? BlockedReason)
{
    /// <summary>True where neither channel can carry it.</summary>
    public bool IsBlocked => !CanEmail && !CanText;
}

/// <summary>One reminder the run has already dealt with.</summary>
public sealed record ReminderSent(
    Guid AppointmentId,
    string PatientName,
    DateTime StartLocal,
    int OffsetDays,
    DateTime SentUtc,
    bool Succeeded,
    string? Detail);

/// <summary>What one run produced.</summary>
public sealed record ReminderRun(int Sent, int Failed, int Skipped, string? Refusal);

/// <summary>
/// Sends the appointment reminders a practice's cadence has fallen due.
/// </summary>
/// <remarks>
/// <para>
/// The half that was missing. Sending a message has worked for a while — an email account,
/// an SMS gateway, a template — but only when somebody ticked a box on a booking. A
/// reminder nobody remembers to send is not a reminder, so the value is entirely in this
/// running without being asked.
/// </para>
/// <para>
/// Idempotent through stored facts rather than through care: every attempt writes an
/// <see cref="AppointmentReminder"/> row for that appointment and offset, and the run skips
/// anything that already has one. Running it twice in a morning, or again after a crash
/// halfway through, sends nothing a second time.
/// </para>
/// <para>
/// Nothing here is on a timer yet. The run is a button, which is honest about what it is —
/// a timer that silently stopped would be worse than one that was never claimed.
/// </para>
/// </remarks>
public interface IReminderRunner
{
    /// <summary>The practice's cadence, in days before — empty where none is set.</summary>
    Task<IReadOnlyList<int>> GetCadenceAsync(CancellationToken ct = default);

    /// <summary>Whether reminders are switched on and could actually go.</summary>
    Task<string?> GetBlockedReasonAsync(CancellationToken ct = default);

    /// <summary>Everything the cadence says is due and has not been attempted.</summary>
    Task<IReadOnlyList<ReminderDue>> GetDueAsync(CancellationToken ct = default);

    /// <summary>The recent attempts, newest first.</summary>
    Task<IReadOnlyList<ReminderSent>> GetRecentAsync(
        int take = 40, CancellationToken ct = default);

    /// <summary>Sends everything due. Safe to run twice.</summary>
    Task<ReminderRun> RunAsync(CancellationToken ct = default);
}

/// <inheritdoc cref="IReminderRunner"/>
public sealed class ReminderRunner : IReminderRunner
{
    /// <summary>
    /// How far ahead the run looks.
    /// </summary>
    /// <remarks>
    /// Sixty days, comfortably past any cadence a practice would set. It exists so the
    /// query is bounded rather than reading every appointment ever booked — the diary of a
    /// practice two years in is tens of thousands of rows and almost none of them are due
    /// a reminder today.
    /// </remarks>
    private const int HorizonDays = 60;

    private readonly MolargoDatabase _database;
    private readonly IAppointmentNotifier _notifier;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;

    public ReminderRunner(
        MolargoDatabase database,
        IAppointmentNotifier notifier,
        ITenantContext tenant,
        IClock clock,
        IAuditLog audit)
    {
        _database = database;
        _notifier = notifier;
        _tenant = tenant;
        _clock = clock;
        _audit = audit;
    }

    public async Task<IReadOnlyList<int>> GetCadenceAsync(CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var settings = await SettingsAsync(db, ct).ConfigureAwait(false);

        return ParseCadence(settings?.ReminderOffsetsDays);
    }

    /// <summary>
    /// Reads "7, 1" or "7,1,0" into offsets.
    /// </summary>
    /// <remarks>
    /// Descending and de-duplicated. The order is what the run walks, and the furthest-out
    /// reminder has to be considered first or a same-day run would send the day-before one
    /// and then decide the week-before one was also due.
    /// </remarks>
    private static IReadOnlyList<int> ParseCadence(string? raw) =>
        (raw ?? string.Empty)
            .Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, out var days) ? days : -1)
            .Where(days => days >= 0)
            .Distinct()
            .OrderByDescending(days => days)
            .ToList();

    public async Task<string?> GetBlockedReasonAsync(CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var settings = await SettingsAsync(db, ct).ConfigureAwait(false);

        if (settings is null || !settings.IsConfigured)
        {
            return "This practice has no mail account set up — Admin → Settings. Without "
                + "one there is nothing to send a reminder through.";
        }

        if (!settings.RemindersEnabled)
        {
            return "Reminders are switched off for this practice. Turn them on below.";
        }

        return ParseCadence(settings.ReminderOffsetsDays).Count == 0
            ? "No cadence is set, so nothing is ever due. Add at least one — 7, 1 sends a "
                + "week out and again the day before."
            : null;
    }

    public async Task<IReadOnlyList<ReminderDue>> GetDueAsync(CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var settings = await SettingsAsync(db, ct).ConfigureAwait(false);
        var cadence = ParseCadence(settings?.ReminderOffsetsDays);

        if (cadence.Count == 0) return [];

        var now = _clock.UtcNow;
        var horizon = now.AddDays(HorizonDays);

        var appointments = await db.Appointments
            .AsNoTracking()
            .Where(a => !a.IsDeleted
                && a.StartUtc > now
                && a.StartUtc <= horizon

                // Cancellations and no-shows are not reminded about. A text saying "see you
                // Tuesday" about a slot the patient cancelled on Monday is the message that
                // makes a practice look like it is not listening.
                && a.Status != AppointmentStatus.Cancelled
                && a.Status != AppointmentStatus.FailedToAttend)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (appointments.Count == 0) return [];

        var ids = appointments.Select(a => a.Id).ToList();

        var already = await db.AppointmentReminders
            .AsNoTracking()
            .Where(r => !r.IsDeleted && ids.Contains(r.AppointmentId))
            .Select(r => new { r.AppointmentId, r.OffsetDays })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var done = already
            .Select(r => (r.AppointmentId, r.OffsetDays))
            .ToHashSet();

        var patients = await db.Patients
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .Select(p => new { p.Id, p.FirstName, p.LastName, p.PreferredName })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var names = patients.ToDictionary(
            p => p.Id, p => $"{p.PreferredName ?? p.FirstName} {p.LastName}".Trim());

        var due = new List<ReminderDue>();

        foreach (var appointment in appointments.OrderBy(a => a.StartUtc))
        {
            foreach (var offset in cadence)
            {
                if (done.Contains((appointment.Id, offset))) continue;

                // Due once the window opens, not on the day itself. A run that only fired
                // on the exact date would miss every reminder for a weekend the practice
                // was shut, and those are the ones that matter most.
                if (appointment.StartUtc.AddDays(-offset) > now) continue;

                var reach = await _notifier
                    .OptionsForAsync(appointment.PatientId, ct)
                    .ConfigureAwait(false);

                due.Add(new ReminderDue(
                    appointment.Id,
                    appointment.PatientId,
                    names.GetValueOrDefault(appointment.PatientId, "Unknown patient"),
                    appointment.StartUtc.ToLocalTime(),
                    string.IsNullOrWhiteSpace(appointment.Reason)
                        ? "Appointment"
                        : appointment.Reason!,
                    offset,
                    reach.CanEmail,
                    reach.CanText,
                    reach.CanEmail || reach.CanText
                        ? null
                        : reach.EmailBlockedReason ?? reach.TextBlockedReason));

                // Only the most urgent outstanding step per appointment. Somebody booked
                // inside the whole cadence — a patient booked tomorrow when the practice
                // reminds at seven days and one — would otherwise get both messages at
                // once, minutes apart, saying the same thing.
                break;
            }
        }

        return due;
    }

    public async Task<IReadOnlyList<ReminderSent>> GetRecentAsync(
        int take = 40, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var rows = await db.AppointmentReminders
            .AsNoTracking()
            .Where(r => !r.IsDeleted)
            .OrderByDescending(r => r.SentUtc)
            .Take(Math.Max(1, take))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (rows.Count == 0) return [];

        var ids = rows.Select(r => r.AppointmentId).ToList();

        var appointments = await db.Appointments
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.StartUtc, a.PatientId })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var byId = appointments.ToDictionary(a => a.Id);

        var patients = await db.Patients
            .AsNoTracking()
            .Select(p => new { p.Id, p.FirstName, p.LastName, p.PreferredName })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var names = patients.ToDictionary(
            p => p.Id, p => $"{p.PreferredName ?? p.FirstName} {p.LastName}".Trim());

        return rows
            .Select(r => new ReminderSent(
                r.AppointmentId,
                names.GetValueOrDefault(r.PatientId, "Unknown patient"),
                byId.TryGetValue(r.AppointmentId, out var a)
                    ? a.StartUtc.ToLocalTime()
                    : r.SentUtc.ToLocalTime(),
                r.OffsetDays,
                r.SentUtc,
                r.Succeeded,
                r.Detail))
            .ToList();
    }

    public async Task<ReminderRun> RunAsync(CancellationToken ct = default)
    {
        if (await GetBlockedReasonAsync(ct).ConfigureAwait(false) is { } blocked)
        {
            return new ReminderRun(0, 0, 0, blocked);
        }

        var due = await GetDueAsync(ct).ConfigureAwait(false);

        var sent = 0;
        var failed = 0;
        var skipped = 0;

        foreach (var item in due)
        {
            if (item.IsBlocked)
            {
                // Recorded, not retried. The commonest block is a patient with no mobile
                // and no consent, and an unrecorded skip would have the next run try again
                // in an hour, and the one after that, until the appointment.
                await RecordAsync(item, false, item.BlockedReason ?? "No way to reach them.",
                    CommunicationChannel.Email, ct)
                    .ConfigureAwait(false);

                skipped++;
                continue;
            }

            var result = await _notifier
                .NotifyAsync(item.AppointmentId, item.CanEmail, item.CanText, ct)
                .ConfigureAwait(false);

            // Email where it can go, text otherwise — both where both can. The outcome
            // summary is the notifier's own words, which name the server's reason.
            var succeeded = result.DidAnything
                && !(result.Summary.Contains("failed", StringComparison.OrdinalIgnoreCase));

            await RecordAsync(item, succeeded, result.Summary,
                item.CanEmail ? CommunicationChannel.Email : CommunicationChannel.Sms, ct)
                .ConfigureAwait(false);

            if (succeeded) sent++; else failed++;
        }

        if (sent + failed + skipped > 0)
        {
            await _audit
                .RecordAsync(
                    AuditAction.NotificationSent,
                    nameof(AppointmentReminder),
                    null,
                    $"Reminder run — {sent} sent, {failed} failed, {skipped} skipped",
                    null,
                    ct)
                .ConfigureAwait(false);
        }

        return new ReminderRun(sent, failed, skipped, null);
    }

    // ---- helpers ---------------------------------------------------------

    private Task<NotificationSettings?> SettingsAsync(
        MolargoDbContext db, CancellationToken ct) =>
        db.NotificationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(row => !row.IsDeleted, ct);

    /// <summary>
    /// Writes the fact of the attempt, whatever came of it.
    /// </summary>
    /// <remarks>
    /// Always, and before anything else could go wrong. The row is what stops the next run
    /// repeating this message, so a send that happened and was not recorded is a patient
    /// reminded every hour until their appointment.
    /// </remarks>
    private async Task RecordAsync(
        ReminderDue item,
        bool succeeded,
        string detail,
        CommunicationChannel channel,
        CancellationToken ct)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        db.AppointmentReminders.Add(new AppointmentReminder
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            AppointmentId = item.AppointmentId,
            PatientId = item.PatientId,
            OffsetDays = item.OffsetDays,
            SentUtc = _clock.UtcNow,
            Succeeded = succeeded,
            Channel = channel,
            Detail = detail.Length > 500 ? detail[..500] : detail,
            CreatedUtc = _clock.UtcNow,
            UpdatedUtc = _clock.UtcNow,
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
