using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Repositories;
using DYS.Molargo.Shared.Services;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Features.Diary.Services;

/// <summary>
/// Reading the diary — day, week and month — and the two worklists that feed it.
/// </summary>
public interface IDiaryService
{
    Task<DiaryDay> GetDayAsync(Guid locationId, DateOnly day, CancellationToken ct = default);

    /// <summary>
    /// The trading week containing <paramref name="anyDay"/>, Monday to Saturday.
    /// </summary>
    Task<IReadOnlyList<DiaryWeekDay>> GetWeekAsync(
        Guid locationId, DateOnly anyDay, CancellationToken ct = default);

    /// <summary>
    /// The calendar month containing <paramref name="anyDay"/>, padded to whole weeks.
    /// </summary>
    /// <summary>
    /// Every appointment at this site, newest first, one page at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one diary view with no date window. Day, week and month all answer "what is on
    /// then"; this answers "where is that appointment", which is a different question and
    /// the reason a search box belongs on it and on none of the others.
    /// </para>
    /// <para>
    /// Paged rather than whole, because this is the query that grows without bound. Every
    /// other diary read is capped by the calendar it draws; a practice two years in has
    /// tens of thousands of these.
    /// </para>
    /// </remarks>
    Task<DiaryList> GetListAsync(
        Guid locationId,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<IReadOnlyList<DiaryMonthCell>> GetMonthAsync(
        Guid locationId, DateOnly anyDay, CancellationToken ct = default);

    /// <summary>
    /// Recalls worth acting on now: overdue, or due inside the next month. Soonest first.
    /// </summary>
    /// <remarks>
    /// Horizoned rather than listing every pending recall. A practice carries a recall for
    /// nearly every active patient, so the unfiltered list is the patient list again — and
    /// a worklist that contains everything is not a worklist.
    /// </remarks>
    Task<IReadOnlyList<DiaryRecallRow>> GetRecallsAsync(
        Guid locationId, CancellationToken ct = default);

    Task<IReadOnlyList<DiaryWaitlistRow>> GetWaitlistAsync(
        Guid locationId, CancellationToken ct = default);

    /// <summary>Whether this practice has appointment reminders switched on.</summary>
    Task<bool> AreRemindersOnAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves the reminder cadence and whether it runs. Null on success, or the refusal.
    /// </summary>
    /// <param name="cadence">
    /// Days before, as typed — "7, 1". Parsed rather than validated into a structure,
    /// because the box is small and the only wrong answers are ones that parse to nothing.
    /// </param>
    Task<string?> SaveReminderCadenceAsync(
        string? cadence, bool enabled, CancellationToken ct = default);

    /// <summary>
    /// Puts a patient on the short-notice list. Null on success, or the refusal.
    /// </summary>
    /// <remarks>
    /// The piece that was missing. The list was seeded and had no way in, so it emptied as
    /// entries were fulfilled and could never refill — and an empty short-notice list is a
    /// cancelled slot nobody fills.
    /// </remarks>
    Task<string?> AddToWaitlistAsync(
        Guid patientId,
        string? wants,
        string? availability,
        WaitlistPriority priority,
        CancellationToken ct = default);

    /// <summary>
    /// Takes a patient off the list — booked, or no longer wanted.
    /// </summary>
    /// <param name="booked">
    /// True where a slot was found. Recorded rather than deleted either way: "we offered
    /// and they took it" and "they asked to come off" are different answers to why somebody
    /// is no longer being rung.
    /// </param>
    Task<string?> RemoveFromWaitlistAsync(
        Guid entryId, bool booked, CancellationToken ct = default);

    /// <summary>
    /// Moves an appointment to a new chair and start time.
    /// </summary>
    /// <returns>
    /// Null on success, or the reason the move was refused — shown to the user rather than
    /// thrown, because a refused reschedule is an ordinary outcome of dragging something
    /// somewhere it cannot go.
    /// </returns>
    Task<string?> MoveAsync(
        Guid appointmentId,
        Guid operatoryId,
        DateTime startLocal,
        CancellationToken ct = default);

    /// <summary>Records the patient as arrived. Idempotent.</summary>
    Task MarkArrivedAsync(Guid appointmentId, CancellationToken ct = default);

    /// <summary>
    /// Records that someone chased a recall, and counts the attempt.
    /// </summary>
    /// <remarks>
    /// Logs the attempt; it does not send anything. The design's button sends an SMS, and
    /// there is now a sender for that — <c>ISmsSender</c> — but chasing a recall is mostly
    /// a phone call somebody has just made, and the count exists to stop the same patient
    /// being rung twice. Wiring the button to the gateway is worth doing on its own, with
    /// the choice of "texted" or "rang" recorded, rather than silently turning every
    /// logged attempt into a charged message.
    /// </remarks>
    Task LogRecallContactAsync(Guid recallId, CancellationToken ct = default);

    /// <summary>Records that a short-notice slot was offered to a waiting patient.</summary>
    Task LogWaitlistContactAsync(Guid entryId, CancellationToken ct = default);
}

/// <inheritdoc cref="IDiaryService"/>
public sealed class DiaryService : IDiaryService
{
    private readonly IRepository<Appointment> _appointments;
    private readonly IRepository<PatientEntity> _patients;
    private readonly IRepository<Provider> _providers;
    private readonly IRepository<AppointmentType> _appointmentTypes;
    private readonly IRepository<Operatory> _operatories;
    private readonly IRepository<Recall> _recalls;
    private readonly IRepository<WaitlistEntry> _waitlist;

    /// <summary>Only for the reminder cadence, which lives with the mail account.</summary>
    private readonly IRepository<NotificationSettings> _notifications;
    private readonly IClock _clock;

    /// <summary>Only for the selected site's trading days and hours.</summary>
    private readonly ISessionService _session;
    private readonly IAuditLog _audit;

    public DiaryService(
        IRepository<Appointment> appointments,
        IRepository<PatientEntity> patients,
        IRepository<Provider> providers,
        IRepository<AppointmentType> appointmentTypes,
        IRepository<Operatory> operatories,
        IRepository<Recall> recalls,
        IRepository<WaitlistEntry> waitlist,
        IRepository<NotificationSettings> notifications,
        IClock clock,
        ISessionService session,
        IAuditLog audit)
    {
        _appointments = appointments;
        _patients = patients;
        _providers = providers;
        _appointmentTypes = appointmentTypes;
        _operatories = operatories;
        _recalls = recalls;
        _waitlist = waitlist;
        _notifications = notifications;
        _clock = clock;
        _session = session;
        _audit = audit;
    }

    public async Task<DiaryDay> GetDayAsync(
        Guid locationId, DateOnly day, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = LocalDayToUtc(day);

        var appointments = await _appointments
            .ListAsync(a => a.PracticeLocationId == locationId
                && a.StartUtc >= fromUtc && a.StartUtc < toUtc, ct)
            .ConfigureAwait(false);

        var operatories = await _operatories
            .ListAsync(o => o.PracticeLocationId == locationId && o.IsActive, ct)
            .ConfigureAwait(false);

        var providers = await _providers.ListAsync(ct: ct).ConfigureAwait(false);
        var types = await _appointmentTypes.ListAsync(ct: ct).ConfigureAwait(false);

        var patientIds = appointments.Select(a => a.PatientId).ToHashSet();
        var patients = await _patients
            .ListAsync(p => patientIds.Contains(p.Id), ct)
            .ConfigureAwait(false);

        var patientNames = patients.ToDictionary(p => p.Id, p => p.FullName);
        var providerNames = providers.ToDictionary(p => p.Id, p => p.FullName);
        var typesById = types.ToDictionary(t => t.Id);

        // A lost slot is not drawn in the chair: the chair is free and re-sellable, and
        // showing a cancellation occupying it is how a fillable hour goes unsold.
        var live = appointments.Where(a => !AppointmentProgress.IsLostSlot(a.Status)).ToList();

        var chairIds = operatories.Select(o => o.Id).ToHashSet();

        var placeable = live
            .Where(a => a.OperatoryId is { } chair && chairIds.Contains(chair) && FitsTheDay(a, day))
            .ToList();

        var blocks = AssignLanes(placeable, patientNames, typesById);

        var unplaced = live
            .Except(placeable)
            .OrderBy(a => a.StartUtc)
            .Select(a => Describe(a, patientNames, typesById, lane: 0, lanes: 1))
            .ToList();

        var bookedByChair = placeable
            .GroupBy(a => a.OperatoryId!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(a => a.DurationMinutes));

        // The clinician with the most booked time in the chair, which is what the column
        // heading names. Ties break on the name so the heading does not flicker between
        // two providers on reload.
        var chairProvider = placeable
            .GroupBy(a => a.OperatoryId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(a => a.ProviderId)
                    .OrderByDescending(byProvider => byProvider.Sum(a => a.DurationMinutes))
                    .ThenBy(byProvider => providerNames.GetValueOrDefault(byProvider.Key, string.Empty))
                    .Select(byProvider => providerNames.GetValueOrDefault(byProvider.Key, "Unassigned"))
                    .First());

        return new DiaryDay
        {
            Day = day,
            Hours = _session.Hours,
            Columns = operatories
                .OrderBy(o => o.DisplayOrder)
                .Select(o => new DiaryColumn(
                    o.Id,
                    o.Name,
                    chairProvider.GetValueOrDefault(o.Id),
                    bookedByChair.GetValueOrDefault(o.Id),
                    _session.Hours))
                .ToList(),
            Blocks = blocks,
            Unplaced = unplaced,
            BookedCount = live.Count,
            LostCount = appointments.Count(a => AppointmentProgress.IsLostSlot(a.Status)),
        };
    }

    public async Task<IReadOnlyList<DiaryWeekDay>> GetWeekAsync(
        Guid locationId, DateOnly anyDay, CancellationToken ct = default)
    {
        var monday = MondayOf(anyDay);

        // Monday to Saturday. Sunday is excluded rather than rendered closed: the design
        // shows six columns, and a seventh that is always empty wastes a sixth of the
        // width on every practice that does not trade then.
        var days = Enumerable.Range(0, 6).Select(monday.AddDays).ToList();

        var fromUtc = monday.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var toUtc = monday.AddDays(6).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local)
            .ToUniversalTime();

        var appointments = await _appointments
            .ListAsync(a => a.PracticeLocationId == locationId
                && a.StartUtc >= fromUtc && a.StartUtc < toUtc, ct)
            .ConfigureAwait(false);

        var providers = await _providers.ListAsync(ct: ct).ConfigureAwait(false);
        var providerNames = providers.ToDictionary(p => p.Id, p => p.FullName);

        var live = appointments.Where(a => !AppointmentProgress.IsLostSlot(a.Status)).ToList();

        return days
            .Select(date =>
            {
                var onDay = live
                    .Where(a => DateOnly.FromDateTime(a.StartUtc.ToLocalTime()) == date)
                    .ToList();

                return new DiaryWeekDay(
                    date,
                    onDay.Count,
                    onDay
                        .GroupBy(a => a.ProviderId)
                        .Select(group => new DiaryProviderLoad(
                            providerNames.GetValueOrDefault(group.Key, "Unassigned"),
                            group.Sum(a => a.DurationMinutes),
                            _session.Hours))
                        .OrderByDescending(load => load.BookedMinutes)
                        .ThenBy(load => load.ProviderName)
                        .ToList(),
                    _session.Hours);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<DiaryMonthCell>> GetMonthAsync(
        Guid locationId, DateOnly anyDay, CancellationToken ct = default)
    {
        var first = new DateOnly(anyDay.Year, anyDay.Month, 1);
        var gridStart = MondayOf(first);

        // Whole weeks from the Monday on or before the 1st to the Sunday on or after the
        // last day, so every row has the same number of cells.
        var last = first.AddMonths(1).AddDays(-1);
        var gridEnd = MondayOf(last).AddDays(6);

        var fromUtc = gridStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var toUtc = gridEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local)
            .ToUniversalTime();

        var appointments = await _appointments
            .ListAsync(a => a.PracticeLocationId == locationId
                && a.StartUtc >= fromUtc && a.StartUtc < toUtc, ct)
            .ConfigureAwait(false);

        var counts = appointments
            .Where(a => !AppointmentProgress.IsLostSlot(a.Status))
            .GroupBy(a => DateOnly.FromDateTime(a.StartUtc.ToLocalTime()))
            .ToDictionary(group => group.Key, group => group.Count());

        var cells = new List<DiaryMonthCell>();

        for (var date = gridStart; date <= gridEnd; date = date.AddDays(1))
        {
            cells.Add(new DiaryMonthCell(
                date,
                counts.GetValueOrDefault(date),
                date.Month == first.Month && date.Year == first.Year,
                _session.Hours));
        }

        return cells;
    }

    public async Task<DiaryList> GetListAsync(
        Guid locationId,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var size = Math.Max(1, pageSize);

        var appointments = await _appointments
            .ListAsync(a => a.PracticeLocationId == locationId, ct)
            .ConfigureAwait(false);

        var patients = await _patients.ListAsync(ct: ct).ConfigureAwait(false);
        var providers = await _providers.ListAsync(ct: ct).ConfigureAwait(false);
        var chairs = await _operatories.ListAsync(ct: ct).ConfigureAwait(false);
        var types = await _appointmentTypes.ListAsync(ct: ct).ConfigureAwait(false);

        var patientsById = patients.ToDictionary(p => p.Id);
        var providerNames = providers.ToDictionary(
            p => p.Id, p => p.DisplayName ?? $"{p.FirstName} {p.LastName}".Trim());
        var chairNames = chairs.ToDictionary(o => o.Id, o => o.Name);
        var typesById = types.ToDictionary(t => t.Id);

        var rows = appointments
            .Select(a =>
            {
                var patient = patientsById.GetValueOrDefault(a.PatientId);
                var type = a.AppointmentTypeId is { } typeId
                    ? typesById.GetValueOrDefault(typeId)
                    : null;

                return new DiaryListRow(
                    a.Id,
                    a.PatientId,
                    a.StartUtc.ToLocalTime(),
                    a.DurationMinutes,
                    patient?.DisplayName ?? "Unknown patient",
                    patient?.PatientNumber,
                    providerNames.GetValueOrDefault(a.ProviderId, "Unassigned"),

                    // A booking with no chair is legitimate — see DiaryDay.Unplaced — and
                    // saying so is better than an empty cell somebody reads as a chair that
                    // failed to load.
                    a.OperatoryId is { } chair
                        ? chairNames.GetValueOrDefault(chair, "Unknown chair")
                        : "No chair",

                    // The appointment's own reason, falling back to the type's name, which
                    // is the same rule the diary blocks use.
                    string.IsNullOrWhiteSpace(a.Reason)
                        ? type?.Name ?? "Appointment"
                        : a.Reason!,
                    type?.Colour,
                    a.Status);
            })
            .ToList();

        var terms = (search ?? string.Empty).Trim();

        if (terms.Length > 0)
        {
            // Matched in memory against the fields already resolved above. The names a
            // person searches by live on three different tables, so a database-side filter
            // would be three joins to search what this method has already assembled.
            //
            // Every word has to match something, rather than the whole phrase matching one
            // field: "yuen crown" finds Margaret Yuen's crown prep, which is how somebody
            // actually remembers an appointment.
            var words = terms.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            rows = rows
                .Where(row => words.All(word => Matches(row, word)))
                .ToList();
        }

        // Newest first. The far future sits above today and today above last year, which
        // puts what a practice is about to do at the top and walks backwards through what
        // it has already done.
        rows = rows.OrderByDescending(row => row.StartLocal).ToList();

        var total = rows.Count;
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)size));

        // Clamped, because a search that shrinks the results while somebody is on page
        // nine would otherwise show an empty page with no way to tell it from no matches.
        var wanted = Math.Clamp(page, 0, pageCount - 1);

        return new DiaryList(
            rows.Skip(wanted * size).Take(size).ToList(), total, wanted, size);
    }

    /// <summary>Whether one search word appears anywhere on a row.</summary>
    /// <remarks>
    /// Case-insensitive and substring, so a partial surname finds the patient — somebody
    /// searching a diary rarely remembers the spelling and never remembers the case.
    /// </remarks>
    private static bool Matches(DiaryListRow row, string word) =>
        Contains(row.PatientName, word)
        || Contains(row.PatientNumber, word)
        || Contains(row.ProviderName, word)
        || Contains(row.ChairName, word)
        || Contains(row.Reason, word)
        || Contains(row.StartLocal.ToString("d MMM yyyy"), word)
        || Contains(row.Status.ToString(), word);

    private static bool Contains(string? value, string word) =>
        value is { Length: > 0 }
        && value.Contains(word, StringComparison.CurrentCultureIgnoreCase);

    public async Task<IReadOnlyList<DiaryRecallRow>> GetRecallsAsync(
        Guid locationId, CancellationToken ct = default)
    {
        var horizon = _clock.Today.AddDays(30);

        // Pending only. A recall that has been booked or written off is not work.
        var recalls = await _recalls
            .ListAsync(r => r.Status == RecallStatus.Pending && r.DueOn <= horizon, ct)
            .ConfigureAwait(false);

        var patientIds = recalls.Select(r => r.PatientId).ToHashSet();

        // Scoped by the patient's home location, because Recall itself carries none —
        // a recall belongs to a person, and people belong to a site.
        var patients = await _patients
            .ListAsync(p => patientIds.Contains(p.Id) && p.PracticeLocationId == locationId, ct)
            .ConfigureAwait(false);

        var byId = patients.ToDictionary(p => p.Id);

        return recalls
            .Where(r => byId.ContainsKey(r.PatientId))
            .OrderBy(r => r.DueOn)
            .ThenBy(r => byId[r.PatientId].LastName)
            .Select(r => new DiaryRecallRow(
                r.Id,
                r.PatientId,
                byId[r.PatientId].FullName,
                r.RecallType,
                r.DueOn,
                r.LastContactedUtc,
                r.ContactAttempts,
                r.BookedAppointmentId is not null))
            .ToList();
    }

    public async Task<IReadOnlyList<DiaryWaitlistRow>> GetWaitlistAsync(
        Guid locationId, CancellationToken ct = default)
    {
        var entries = await _waitlist
            .ListAsync(w => w.PracticeLocationId == locationId && w.FulfilledUtc == null, ct)
            .ConfigureAwait(false);

        var patientIds = entries.Select(w => w.PatientId).ToHashSet();
        var patients = await _patients
            .ListAsync(p => patientIds.Contains(p.Id), ct)
            .ConfigureAwait(false);

        var byId = patients.ToDictionary(p => p.Id);

        var types = await _appointmentTypes.ListAsync(ct: ct).ConfigureAwait(false);
        var typeNames = types.ToDictionary(t => t.Id, t => t.Name);

        return entries
            .OrderByDescending(w => w.Priority)

            // Never contacted first within a priority: someone who has already been rung
            // and not answered is a worse bet for a slot that expires in an hour.
            .ThenBy(w => w.LastContactedUtc ?? DateTime.MinValue)
            .Select(w => new DiaryWaitlistRow(
                w.Id,
                w.PatientId,
                byId.TryGetValue(w.PatientId, out var patient) ? patient.FullName : "Unknown patient",
                w.Reason
                    ?? (w.AppointmentTypeId is { } typeId ? typeNames.GetValueOrDefault(typeId) : null)
                    ?? "any slot",
                w.Availability ?? "no preference given",
                byId.TryGetValue(w.PatientId, out var contact) ? contact.Mobile : null,
                w.Priority,
                w.LastContactedUtc))
            .ToList();
    }

    public async Task<bool> AreRemindersOnAsync(CancellationToken ct = default)
    {
        var settings = await _notifications
            .ListAsync(ct: ct)
            .ConfigureAwait(false);

        return settings.FirstOrDefault()?.RemindersEnabled ?? false;
    }

    public async Task<string?> SaveReminderCadenceAsync(
        string? cadence, bool enabled, CancellationToken ct = default)
    {
        var settings = await _notifications.ListAsync(ct: ct).ConfigureAwait(false);
        var row = settings.FirstOrDefault();

        if (row is null)
        {
            return "This practice has no mail account yet — set one up under "
                + "Admin → Settings first.";
        }

        var offsets = (cadence ?? string.Empty)
            .Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, out var days) ? days : -1)
            .ToList();

        // Every part has to be a number. A cadence of "7, tomorrow" that quietly became
        // "7" would be a policy somebody believes they set and did not.
        if (offsets.Any(days => days < 0))
        {
            return "The cadence has to be whole numbers of days — 7, 1.";
        }

        // A year out is not a reminder, it is a recall, and the two have different screens
        // for a reason. The cap also bounds what the run has to look ahead over.
        if (offsets.Any(days => days > 60))
        {
            return "60 days is the furthest a reminder goes out. Anything beyond that is a "
                + "recall — see the Recalls tab.";
        }

        if (enabled && offsets.Count == 0)
        {
            return "Add at least one step before switching reminders on — 7, 1 sends a "
                + "week out and again the day before.";
        }

        row.ReminderOffsetsDays = offsets.Count == 0
            ? null
            : string.Join(",", offsets.Distinct().OrderByDescending(days => days));

        row.RemindersEnabled = enabled;

        await _notifications.SaveAsync(row, ct).ConfigureAwait(false);

        await _audit
            .RecordAsync(
                AuditAction.Updated,
                nameof(NotificationSettings),
                row.Id,
                enabled
                    ? $"Appointment reminders on — {row.ReminderOffsetsDays} days before"
                    : "Appointment reminders switched off",
                null,
                ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string?> AddToWaitlistAsync(
        Guid patientId,
        string? wants,
        string? availability,
        WaitlistPriority priority,
        CancellationToken ct = default)
    {
        if (patientId == Guid.Empty) return "Choose a patient first.";

        var patient = await _patients.GetByIdAsync(patientId, ct).ConfigureAwait(false);

        if (patient is null) return "That patient no longer exists.";

        var existing = await _waitlist
            .ListAsync(w => w.PatientId == patientId && w.FulfilledUtc == null, ct)
            .ConfigureAwait(false);

        // One entry per patient. Two rows for the same person is two calls about the same
        // gap, and the second one arrives after they have already said yes to the first.
        if (existing.Count > 0)
        {
            return $"{patient.FullName} is already on the short-notice list.";
        }

        var entry = new WaitlistEntry
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            PracticeLocationId = _session.LocationId,
            Priority = priority,
            Reason = Clean(wants),
            Availability = Clean(availability),
        };

        await _waitlist.SaveAsync(entry, ct).ConfigureAwait(false);

        await _audit
            .RecordAsync(
                AuditAction.Created,
                nameof(WaitlistEntry),
                entry.Id,
                $"Added to the short-notice list — {entry.Reason ?? "any slot"}",
                patientId,
                ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string?> RemoveFromWaitlistAsync(
        Guid entryId, bool booked, CancellationToken ct = default)
    {
        var entry = await _waitlist.GetByIdAsync(entryId, ct).ConfigureAwait(false);

        if (entry is null) return null;

        // Stamped rather than deleted. The list reads "not yet fulfilled", so a time here
        // takes the row off it while leaving the evidence that somebody was offered a slot
        // — which is what a patient asking "you never call me" is answered with.
        entry.FulfilledUtc = _clock.UtcNow;

        await _waitlist.SaveAsync(entry, ct).ConfigureAwait(false);

        await _audit
            .RecordAsync(
                AuditAction.Updated,
                nameof(WaitlistEntry),
                entryId,
                booked
                    ? "Taken off the short-notice list — a slot was found"
                    : "Taken off the short-notice list — no longer waiting",
                entry.PatientId,
                ct)
            .ConfigureAwait(false);

        return null;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task<string?> MoveAsync(
        Guid appointmentId,
        Guid operatoryId,
        DateTime startLocal,
        CancellationToken ct = default)
    {
        var appointment = await _appointments.GetByIdAsync(appointmentId, ct).ConfigureAwait(false);
        if (appointment is null) return "That appointment no longer exists.";

        if (AppointmentProgress.IsLostSlot(appointment.Status))
        {
            return "A cancelled appointment cannot be moved. Book a new one instead.";
        }

        if (_session.Hours.RefuseSlot(startLocal, appointment.DurationMinutes) is { } refusal)
        {
            return refusal;
        }

        // Overlaps are allowed, deliberately. Double-booking is sometimes intentional —
        // a check squeezed alongside a long procedure — and a diary that refuses it sends
        // staff to work around the software. The grid draws the clash side by side so the
        // decision is visible instead of silent.
        appointment.OperatoryId = operatoryId;
        appointment.StartUtc = DateTime.SpecifyKind(startLocal, DateTimeKind.Local).ToUniversalTime();

        await _appointments.SaveAsync(appointment, ct).ConfigureAwait(false);

        // A drag on the grid is still a reschedule. It writes no reason and asks for no
        // confirmation, which is exactly why it needs the entry: "nobody told me it moved"
        // has no other answer.
        await _audit
            .RecordAsync(
                AuditAction.Updated,
                nameof(Appointment),
                appointmentId,
                $"Moved in the diary to {startLocal:ddd d MMM, HH:mm}",
                appointment.PatientId,
                ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task MarkArrivedAsync(Guid appointmentId, CancellationToken ct = default)
    {
        var appointment = await _appointments.GetByIdAsync(appointmentId, ct).ConfigureAwait(false);
        if (appointment is null) return;

        // Anything past the door is left alone. Idempotent for a patient already here —
        // the first arrival time is the one the running-late arithmetic measures against —
        // and a refusal for a visit already finished, which must not be walked backwards.
        if (!AppointmentProgress.IsAwaitingArrival(appointment.Status)) return;

        appointment.Status = AppointmentStatus.CheckedIn;
        appointment.CheckedInUtc = _clock.UtcNow;

        await _appointments.SaveAsync(appointment, ct).ConfigureAwait(false);

        await _audit
            .RecordAsync(
                AuditAction.Updated,
                nameof(Appointment),
                appointmentId,
                "Marked arrived",
                appointment.PatientId,
                ct)
            .ConfigureAwait(false);
    }

    public async Task LogRecallContactAsync(Guid recallId, CancellationToken ct = default)
    {
        var recall = await _recalls.GetByIdAsync(recallId, ct).ConfigureAwait(false);
        if (recall is null) return;

        recall.LastContactedUtc = _clock.UtcNow;
        recall.ContactAttempts += 1;

        // Attempt number included. A recall marked contacted four times with nothing to
        // show for it is a different conversation from one contacted once.
        await _audit
            .RecordAsync(
                AuditAction.Updated,
                nameof(Recall),
                recallId,
                $"Recall contact logged (attempt {recall.ContactAttempts})",
                recall.PatientId,
                ct)
            .ConfigureAwait(false);
    }

    public async Task LogWaitlistContactAsync(Guid entryId, CancellationToken ct = default)
    {
        var entry = await _waitlist.GetByIdAsync(entryId, ct).ConfigureAwait(false);
        if (entry is null) return;

        entry.LastContactedUtc = _clock.UtcNow;

        await _waitlist.SaveAsync(entry, ct).ConfigureAwait(false);

        await _audit
            .RecordAsync(
                AuditAction.Updated,
                nameof(WaitlistEntry),
                entryId,
                "Waitlist contact logged",
                entry.PatientId,
                ct)
            .ConfigureAwait(false);
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>Whether an appointment falls inside the day the grid can draw.</summary>
    /// <summary>Not static any more: the day it has to fit is this site's.</summary>
    private bool FitsTheDay(Appointment appointment, DateOnly day)
    {
        var start = appointment.StartUtc.ToLocalTime();

        if (DateOnly.FromDateTime(start) != day) return false;

        var startMinutes = (int)start.TimeOfDay.TotalMinutes;

        // A booking that starts before opening or ends after closing has nowhere on the
        // grid to go. It is reported as unplaced rather than clipped, because a block
        // silently trimmed to fit misstates when the patient is actually coming.
        return startMinutes >= _session.Hours.OpenMinutes
            && startMinutes + appointment.DurationMinutes <= _session.Hours.CloseMinutes;
    }

    /// <summary>
    /// Works out which sub-column each block draws in, per chair.
    /// </summary>
    /// <remarks>
    /// Clusters of transitively-overlapping appointments share a lane count, so two
    /// clashing bookings each take half the chair's width while the rest of the day stays
    /// full width. Assigning lanes per pair instead produced blocks that changed width
    /// halfway down a column.
    /// </remarks>
    /// <summary>Not static: the blocks it builds carry this site's hours.</summary>
    private List<DiaryBlock> AssignLanes(
        IReadOnlyList<Appointment> placeable,
        IReadOnlyDictionary<Guid, string> patientNames,
        IReadOnlyDictionary<Guid, AppointmentType> typesById)
    {
        var blocks = new List<DiaryBlock>();

        foreach (var chair in placeable.GroupBy(a => a.OperatoryId!.Value))
        {
            var ordered = chair.OrderBy(a => a.StartUtc).ThenBy(a => a.DurationMinutes).ToList();

            var cluster = new List<Appointment>();
            var clusterEnd = DateTime.MinValue;

            foreach (var appointment in ordered)
            {
                // A start at or after the cluster's furthest end begins a new cluster: it
                // overlaps nothing already in it.
                if (cluster.Count > 0 && appointment.StartUtc >= clusterEnd)
                {
                    blocks.AddRange(LayOutCluster(cluster, patientNames, typesById));
                    cluster.Clear();
                    clusterEnd = DateTime.MinValue;
                }

                cluster.Add(appointment);

                if (appointment.EndUtc > clusterEnd) clusterEnd = appointment.EndUtc;
            }

            if (cluster.Count > 0)
            {
                blocks.AddRange(LayOutCluster(cluster, patientNames, typesById));
            }
        }

        return blocks;
    }

    private IEnumerable<DiaryBlock> LayOutCluster(
        List<Appointment> cluster,
        IReadOnlyDictionary<Guid, string> patientNames,
        IReadOnlyDictionary<Guid, AppointmentType> typesById)
    {
        // First lane whose last appointment has already finished, so a cluster of three
        // where only two ever overlap still only needs two lanes.
        var laneEnds = new List<DateTime>();
        var lanes = new int[cluster.Count];

        for (var i = 0; i < cluster.Count; i++)
        {
            var lane = laneEnds.FindIndex(end => end <= cluster[i].StartUtc);

            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(cluster[i].EndUtc);
            }
            else
            {
                laneEnds[lane] = cluster[i].EndUtc;
            }

            lanes[i] = lane;
        }

        return cluster.Select((appointment, i) =>
            Describe(appointment, patientNames, typesById, lanes[i], laneEnds.Count));
    }

    private DiaryBlock Describe(
        Appointment appointment,
        IReadOnlyDictionary<Guid, string> patientNames,
        IReadOnlyDictionary<Guid, AppointmentType> typesById,
        int lane,
        int lanes)
    {
        var type = appointment.AppointmentTypeId is { } typeId
            ? typesById.GetValueOrDefault(typeId)
            : null;

        return new DiaryBlock(
            appointment.Id,
            appointment.PatientId,
            appointment.OperatoryId ?? Guid.Empty,
            patientNames.GetValueOrDefault(appointment.PatientId, "Unknown patient"),
            appointment.StartUtc.ToLocalTime(),
            appointment.DurationMinutes,
            appointment.Reason ?? type?.Name ?? "Appointment",
            type?.Colour,
            appointment.Status,
            lane,
            lanes,

            // The grid offset each block is drawn at is measured from this site's opening
            // time, so the block has to carry the hours it was placed against.
            _session.Hours);
    }

    private static DateOnly MondayOf(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static (DateTime Start, DateTime End) LocalDayToUtc(DateOnly day)
    {
        var start = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
        return (start.ToUniversalTime(), start.AddDays(1).ToUniversalTime());
    }
}
