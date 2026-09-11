using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Repositories;
using DYS.Molargo.Shared.Services;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Features.FrontDesk.Services;

/// <summary>
/// Assembles the front-desk dashboard: the day's figures, the arrivals list, the task
/// list and the short-notice list.
/// </summary>
public interface IFrontDeskService
{
    /// <summary>Everything the screen shows, for one location on one day.</summary>
    Task<FrontDeskDay> GetDayAsync(Guid locationId, DateOnly day, CancellationToken ct = default);

    /// <summary>
    /// Advances an appointment along the day's progression — booked, arrived, seated, in
    /// chair, completed — and wraps back to booked. Wrapping is deliberate: the front desk
    /// taps these in a hurry and needs a way back from an accidental tap.
    /// </summary>
    Task AdvanceStatusAsync(Guid appointmentId, CancellationToken ct = default);

    /// <summary>Ticks a task off, or un-ticks it.</summary>
    Task ToggleTaskAsync(Guid taskId, CancellationToken ct = default);
}

/// <inheritdoc cref="IFrontDeskService"/>
public sealed class FrontDeskService : IFrontDeskService
{
    /// <summary>
    /// Unbooked runs shorter than this are not reported as gaps. A practice never fills a
    /// ten-minute hole, and counting them makes the figure meaningless — the prototype's
    /// "3 gaps" is three fillable gaps, not every seam between appointments.
    /// </summary>
    private const int MinimumFillableGapMinutes = 15;

    private readonly IRepository<Appointment> _appointments;
    private readonly IRepository<PatientEntity> _patients;
    private readonly IRepository<Provider> _providers;
    private readonly IRepository<AppointmentType> _appointmentTypes;
    private readonly IRepository<Operatory> _operatories;
    private readonly IRepository<PracticeTask> _tasks;
    private readonly IRepository<WaitlistEntry> _waitlist;
    private readonly IClock _clock;

    public FrontDeskService(
        IRepository<Appointment> appointments,
        IRepository<PatientEntity> patients,
        IRepository<Provider> providers,
        IRepository<AppointmentType> appointmentTypes,
        IRepository<Operatory> operatories,
        IRepository<PracticeTask> tasks,
        IRepository<WaitlistEntry> waitlist,
        IClock clock)
    {
        _appointments = appointments;
        _patients = patients;
        _providers = providers;
        _appointmentTypes = appointmentTypes;
        _operatories = operatories;
        _tasks = tasks;
        _waitlist = waitlist;
        _clock = clock;
    }

    public async Task<FrontDeskDay> GetDayAsync(
        Guid locationId, DateOnly day, CancellationToken ct = default)
    {
        // The column stores UTC, so the local day has to be converted to a UTC window
        // before it can be compared. Filtering on a local date directly is wrong by the
        // offset, which in Sydney silently shifts the day by ten or eleven hours.
        var (dayStartUtc, dayEndUtc) = LocalDayToUtc(day);
        var (weekStartUtc, weekEndUtc) = LocalWeekToUtc(day);

        var today = await _appointments
            .ListAsync(a => a.PracticeLocationId == locationId
                && a.StartUtc >= dayStartUtc && a.StartUtc < dayEndUtc, ct)
            .ConfigureAwait(false);

        var week = await _appointments
            .ListAsync(a => a.PracticeLocationId == locationId
                && a.StartUtc >= weekStartUtc && a.StartUtc < weekEndUtc
                && a.Status == AppointmentStatus.FailedToAttend, ct)
            .ConfigureAwait(false);

        // Loaded whole and joined in memory. These are reference tables of a handful of
        // rows each, so a lookup costs less than the round trips per appointment that
        // asking for them individually would take.
        var providers = await _providers.ListAsync(ct: ct).ConfigureAwait(false);
        var types = await _appointmentTypes.ListAsync(ct: ct).ConfigureAwait(false);
        var operatories = await _operatories
            .ListAsync(o => o.PracticeLocationId == locationId, ct)
            .ConfigureAwait(false);

        var providerNames = providers.ToDictionary(p => p.Id, p => p.FullName);
        var typesById = types.ToDictionary(t => t.Id);
        var operatoryNames = operatories.ToDictionary(o => o.Id, o => o.Name);

        var patientIds = today.Select(a => a.PatientId).ToHashSet();
        var patients = await _patients
            .ListAsync(p => patientIds.Contains(p.Id), ct)
            .ConfigureAwait(false);
        var patientNames = patients.ToDictionary(p => p.Id, p => p.FullName);

        // Cancelled and missed visits are not arrivals. They still count towards the
        // day's bookings and the FTA figure, which is why they are filtered here rather
        // than in the query.
        var arrivals = today
            .Where(a => a.Status is not (AppointmentStatus.Cancelled or AppointmentStatus.FailedToAttend))
            .OrderBy(a => a.StartUtc)
            .Select(a => new FrontDeskArrival(
                a.Id,
                a.PatientId,
                a.StartUtc.ToLocalTime(),
                patientNames.GetValueOrDefault(a.PatientId, "Unknown patient"),
                providerNames.GetValueOrDefault(a.ProviderId, "Unassigned"),
                a.Reason ?? typesById.GetValueOrDefault(a.AppointmentTypeId ?? Guid.Empty)?.Name ?? "Appointment",
                typesById.GetValueOrDefault(a.AppointmentTypeId ?? Guid.Empty)?.Colour,
                a.Status))
            .ToList();

        var gaps = FindGaps(today, day, operatoryNames, providerNames);

        var owing = await _patients
            .ListAsync(p => p.Balance != 0m && p.Status != PatientStatus.Archived, ct)
            .ConfigureAwait(false);

        var taskRows = await _tasks
            .ListAsync(t => t.PracticeLocationId == locationId, ct)
            .ConfigureAwait(false);

        var waitingRows = await _waitlist
            .ListAsync(w => w.PracticeLocationId == locationId && w.FulfilledUtc == null, ct)
            .ConfigureAwait(false);

        var waitingPatientIds = waitingRows.Select(w => w.PatientId).ToHashSet();
        var waitingPatients = await _patients
            .ListAsync(p => waitingPatientIds.Contains(p.Id), ct)
            .ConfigureAwait(false);
        var waitingNames = waitingPatients.ToDictionary(p => p.Id, p => p.FullName);

        return new FrontDeskDay
        {
            Day = day,
            BookedCount = today.Count(a => a.Status != AppointmentStatus.Cancelled),
            ProviderCount = today.Select(a => a.ProviderId).Distinct().Count(),
            Arrivals = arrivals,
            Gaps = gaps,

            // Summed in memory, not in SQL: the balance column is TEXT because SQLite has
            // no decimal type, so SUM() over it would concatenate or coerce rather than
            // add. The debtors list is short enough that this is not worth solving.
            UnpaidTotal = owing.Sum(p => p.Balance),
            UnpaidAccountCount = owing.Count,

            FailedToAttendThisWeek = week.Count,

            Tasks = taskRows
                .OrderBy(t => t.IsDone)
                .ThenBy(t => t.DisplayOrder)
                .Select(t => new FrontDeskTask(t.Id, t.Label, DueLabel(t.DueOn, day), t.IsDone))
                .ToList(),

            Waiting = waitingRows
                .OrderByDescending(w => w.Priority)
                .Select(w => new FrontDeskWaiting(
                    w.Id,
                    w.PatientId,
                    waitingNames.GetValueOrDefault(w.PatientId, "Unknown patient"),
                    w.Reason ?? "any slot",
                    w.Priority))
                .ToList(),
        };
    }

    public async Task AdvanceStatusAsync(Guid appointmentId, CancellationToken ct = default)
    {
        var appointment = await _appointments.GetByIdAsync(appointmentId, ct).ConfigureAwait(false);
        if (appointment is null) return;

        appointment.Status = NextStatus(appointment.Status);

        var now = _clock.UtcNow;

        // The timestamps are what the day's running-late arithmetic reads, so they are
        // maintained here rather than left for whoever notices. Cleared on the way back
        // round: a wrapped status with a stale check-in time claims the patient arrived
        // for a visit that is now merely booked.
        appointment.CheckedInUtc = appointment.Status switch
        {
            AppointmentStatus.Scheduled => null,
            AppointmentStatus.CheckedIn => now,
            _ => appointment.CheckedInUtc ?? now,
        };

        appointment.CompletedUtc = appointment.Status == AppointmentStatus.Completed ? now : null;

        await _appointments.SaveAsync(appointment, ct).ConfigureAwait(false);
    }

    public async Task ToggleTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        var task = await _tasks.GetByIdAsync(taskId, ct).ConfigureAwait(false);
        if (task is null) return;

        task.CompletedUtc = task.IsDone ? null : _clock.UtcNow;

        await _tasks.SaveAsync(task, ct).ConfigureAwait(false);
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// The day's progression, in the order the front desk taps through it. Explicit
    /// rather than arithmetic on the enum value, because <c>Seated</c> is numbered 7 and
    /// incrementing would jump from arrived straight past it.
    /// </summary>
    private static AppointmentStatus NextStatus(AppointmentStatus current) => current switch
    {
        AppointmentStatus.Scheduled or AppointmentStatus.Confirmed => AppointmentStatus.CheckedIn,
        AppointmentStatus.CheckedIn => AppointmentStatus.Seated,
        AppointmentStatus.Seated => AppointmentStatus.InProgress,
        AppointmentStatus.InProgress => AppointmentStatus.Completed,
        AppointmentStatus.Completed => AppointmentStatus.Scheduled,

        // A cancellation or a no-show is not part of the arrivals cycle and must not be
        // walked back into it by a stray tap.
        _ => current,
    };

    /// <summary>
    /// Fillable holes in the day: unbooked runs of chair time <em>between</em> two booked
    /// appointments in the same chair.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Interior gaps only. Measuring from the start of the working day to the first
    /// appointment, or from the last one to close of business, technically also finds
    /// unbooked chair time — and makes the figure useless: a practice with a quiet
    /// afternoon reported "22h 15m across 7 gaps", which is every empty chair-hour in the
    /// building rather than anything the front desk could ring someone about. A gap is a
    /// hole in a day that is otherwise working.
    /// </para>
    /// <para>
    /// Per operatory rather than per provider: what is being sold is chair time, and a
    /// clinician with a free hour is only a gap if there is a room to put them in.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<FrontDeskGap> FindGaps(
        IReadOnlyList<Appointment> day,
        DateOnly date,
        IReadOnlyDictionary<Guid, string> operatoryNames,
        IReadOnlyDictionary<Guid, string> providerNames)
    {
        // The same window the diary draws. These were a local 08:00-17:00 pair while the
        // diary ran to 18:00, so the last hour of every day was simultaneously bookable
        // and outside opening hours — and gaps in it went unreported.
        var opening = date.ToDateTime(PracticeHours.Open, DateTimeKind.Local);
        var closing = date.ToDateTime(PracticeHours.Close, DateTimeKind.Local);

        var gaps = new List<FrontDeskGap>();

        var byOperatory = day
            .Where(a => a.OperatoryId is not null
                && a.Status is not (AppointmentStatus.Cancelled or AppointmentStatus.FailedToAttend))
            .GroupBy(a => a.OperatoryId!.Value);

        foreach (var chair in byOperatory)
        {
            var booked = chair.OrderBy(a => a.StartUtc).ToList();

            // Nothing to be between. A chair with one appointment has no interior gap,
            // and an empty chair is a rostering question rather than a gap to fill.
            if (booked.Count < 2) continue;

            var cursor = booked[0].StartUtc.ToLocalTime()
                .AddMinutes(booked[0].DurationMinutes);

            foreach (var appointment in booked.Skip(1))
            {
                var start = appointment.StartUtc.ToLocalTime();
                var end = start.AddMinutes(appointment.DurationMinutes);

                var minutes = (int)(start - cursor).TotalMinutes;

                // Clamped to the working day, so a booking that runs past close does not
                // contribute a gap measured from outside opening hours.
                if (minutes >= MinimumFillableGapMinutes && cursor >= opening && start <= closing)
                {
                    gaps.Add(new FrontDeskGap(
                        cursor,
                        minutes,
                        operatoryNames.GetValueOrDefault(chair.Key, "Chair"),
                        providerNames.GetValueOrDefault(appointment.ProviderId, "Unassigned")));
                }

                // Max, not assignment: an appointment starting before the cursor overlaps
                // the previous one, and taking its end unconditionally would wind the
                // cursor backwards and invent a gap on the next pass.
                if (end > cursor) cursor = end;
            }
        }

        return gaps.OrderBy(gap => gap.StartLocal).ToList();
    }

    private static (DateTime Start, DateTime End) LocalDayToUtc(DateOnly day)
    {
        var start = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
        return (start.ToUniversalTime(), start.AddDays(1).ToUniversalTime());
    }

    /// <summary>Monday to Monday, in local time, converted to a UTC window.</summary>
    private static (DateTime Start, DateTime End) LocalWeekToUtc(DateOnly day)
    {
        // DayOfWeek starts at Sunday; the +6 %7 shifts it so Monday is day zero, which is
        // what an Australian practice week starts on.
        var monday = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
        var start = monday.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
        return (start.ToUniversalTime(), start.AddDays(7).ToUniversalTime());
    }

    private static string DueLabel(DateOnly? dueOn, DateOnly today)
    {
        if (dueOn is not { } due) return string.Empty;

        var days = due.DayNumber - today.DayNumber;

        return days switch
        {
            < 0 => "Overdue",
            0 => "Today",
            1 => "Tomorrow",

            // Inside the week, the weekday alone is what the front desk works to.
            < 7 => due.DayOfWeek.ToString()[..3],
            _ => due.ToString("d MMM"),
        };
    }
}
