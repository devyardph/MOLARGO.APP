using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Shared.Features.FrontDesk.Services;

/// <summary>
/// One arrival on the day's list.
/// </summary>
/// <param name="AppointmentId">Used to advance the status.</param>
/// <param name="PatientId">Used to open the record.</param>
/// <param name="StartLocal">Local start time, already converted from the stored UTC.</param>
/// <param name="Colour">
/// The appointment type's diary swatch, as a CSS colour. Null where the appointment has
/// no type, which the view renders as a neutral swatch rather than as nothing — a missing
/// dot would misalign the row.
/// </param>
public sealed record FrontDeskArrival(
    Guid AppointmentId,
    Guid PatientId,
    DateTime StartLocal,
    string PatientName,
    string ProviderName,
    string Reason,
    string? Colour,
    AppointmentStatus Status);

/// <summary>
/// An unbooked run of chair time inside the working day.
/// </summary>
/// <param name="Provider">
/// The clinician whose next appointment closes the gap, for the "Dr Ellery — cancellation
/// this morning" line. Null for a gap that runs to the end of the day, where there is no
/// next appointment to name.
/// </param>
public sealed record FrontDeskGap(
    DateTime StartLocal,
    int Minutes,
    string OperatoryName,
    string? Provider);

/// <summary>One row of the front-desk task list.</summary>
/// <param name="DueLabel">Already rendered relative to today — "Today", "Tue", "Overdue".</param>
public sealed record FrontDeskTask(Guid Id, string Label, string DueLabel, bool IsDone);

/// <summary>One row of the short-notice list.</summary>
public sealed record FrontDeskWaiting(
    Guid Id,
    Guid PatientId,
    string PatientName,
    string Wants,
    WaitlistPriority Priority);

/// <summary>
/// Everything the front-desk screen needs for one location on one day, fetched together.
/// </summary>
/// <remarks>
/// One aggregate rather than eight separate awaits from the view model. The screen is
/// either the day or it is not: eight independent loads means eight loading states and
/// eight ways for it to be half-right, which on a front desk is worse than being slow.
/// </remarks>
public sealed class FrontDeskDay
{
    public required DateOnly Day { get; init; }

    /// <summary>Appointments booked today, excluding cancellations but including no-shows.</summary>
    public int BookedCount { get; init; }

    /// <summary>Distinct clinicians working today, for the "· 4 providers" line.</summary>
    public int ProviderCount { get; init; }

    public IReadOnlyList<FrontDeskArrival> Arrivals { get; init; } = [];

    public IReadOnlyList<FrontDeskGap> Gaps { get; init; } = [];

    public decimal UnpaidTotal { get; init; }

    public int UnpaidAccountCount { get; init; }

    public int FailedToAttendThisWeek { get; init; }

    public IReadOnlyList<FrontDeskTask> Tasks { get; init; } = [];

    public IReadOnlyList<FrontDeskWaiting> Waiting { get; init; } = [];

    /// <summary>Total fillable minutes across every gap — the headline figure.</summary>
    public int GapMinutes => Gaps.Sum(gap => gap.Minutes);

    /// <summary>
    /// The gap worth offering the short-notice list: the longest one, earliest first on a
    /// tie. The prototype hard-codes a single banner; this picks the one that would
    /// actually be offered.
    /// </summary>
    public FrontDeskGap? PrimaryGap =>
        Gaps.OrderByDescending(gap => gap.Minutes).ThenBy(gap => gap.StartLocal).FirstOrDefault();
}
