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
    /// that needs a gateway — which is online work, deliberately not built yet. Recording
    /// the call the front desk just made is the part that works offline, and it is what
    /// stops the same patient being rung twice.
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
    private readonly IClock _clock;

    public DiaryService(
        IRepository<Appointment> appointments,
        IRepository<PatientEntity> patients,
        IRepository<Provider> providers,
        IRepository<AppointmentType> appointmentTypes,
        IRepository<Operatory> operatories,
        IRepository<Recall> recalls,
        IRepository<WaitlistEntry> waitlist,
        IClock clock)
    {
        _appointments = appointments;
        _patients = patients;
        _providers = providers;
        _appointmentTypes = appointmentTypes;
        _operatories = operatories;
        _recalls = recalls;
        _waitlist = waitlist;
        _clock = clock;
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
            Columns = operatories
                .OrderBy(o => o.DisplayOrder)
                .Select(o => new DiaryColumn(
                    o.Id,
                    o.Name,
                    chairProvider.GetValueOrDefault(o.Id),
                    bookedByChair.GetValueOrDefault(o.Id)))
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
                            group.Sum(a => a.DurationMinutes)))
                        .OrderByDescending(load => load.BookedMinutes)
                        .ThenBy(load => load.ProviderName)
                        .ToList());
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
                date.Month == first.Month && date.Year == first.Year));
        }

        return cells;
    }

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

        if (PracticeHours.RefuseSlot(startLocal, appointment.DurationMinutes) is { } refusal)
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
    }

    public async Task LogRecallContactAsync(Guid recallId, CancellationToken ct = default)
    {
        var recall = await _recalls.GetByIdAsync(recallId, ct).ConfigureAwait(false);
        if (recall is null) return;

        recall.LastContactedUtc = _clock.UtcNow;
        recall.ContactAttempts += 1;

        await _recalls.SaveAsync(recall, ct).ConfigureAwait(false);
    }

    public async Task LogWaitlistContactAsync(Guid entryId, CancellationToken ct = default)
    {
        var entry = await _waitlist.GetByIdAsync(entryId, ct).ConfigureAwait(false);
        if (entry is null) return;

        entry.LastContactedUtc = _clock.UtcNow;

        await _waitlist.SaveAsync(entry, ct).ConfigureAwait(false);
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>Whether an appointment falls inside the day the grid can draw.</summary>
    private static bool FitsTheDay(Appointment appointment, DateOnly day)
    {
        var start = appointment.StartUtc.ToLocalTime();

        if (DateOnly.FromDateTime(start) != day) return false;

        var startMinutes = (int)start.TimeOfDay.TotalMinutes;

        // A booking that starts before opening or ends after closing has nowhere on the
        // grid to go. It is reported as unplaced rather than clipped, because a block
        // silently trimmed to fit misstates when the patient is actually coming.
        return startMinutes >= PracticeHours.OpenMinutes
            && startMinutes + appointment.DurationMinutes <= PracticeHours.CloseMinutes;
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
    private static List<DiaryBlock> AssignLanes(
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

    private static IEnumerable<DiaryBlock> LayOutCluster(
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

    private static DiaryBlock Describe(
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
            lanes);
    }

    private static DateOnly MondayOf(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static (DateTime Start, DateTime End) LocalDayToUtc(DateOnly day)
    {
        var start = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
        return (start.ToUniversalTime(), start.AddDays(1).ToUniversalTime());
    }
}
