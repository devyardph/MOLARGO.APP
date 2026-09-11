using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Shared.Features.Diary.Services;

/// <summary>
/// One column of the day view: a chair, and who is working in it.
/// </summary>
/// <param name="ProviderName">
/// The clinician with the most booked time in this chair today, which is what the design's
/// column heading names. Null for a chair with nothing booked — no clinician is assigned
/// to an empty chair, and naming yesterday's would be a guess.
/// </param>
/// <param name="BookedMinutes">
/// Live bookings only. A cancellation frees the chair, so counting it would report a chair
/// as busy at the moment the front desk most needs to see it is free.
/// </param>
public sealed record DiaryColumn(
    Guid OperatoryId,
    string OperatoryName,
    string? ProviderName,
    int BookedMinutes)
{
    /// <summary>Booked share of the chair's working day, 0–100.</summary>
    public int UtilisationPercent => PracticeHours.WorkingMinutes == 0
        ? 0
        : (int)Math.Round(100m * BookedMinutes / PracticeHours.WorkingMinutes);
}

/// <summary>
/// One appointment as the day grid draws it.
/// </summary>
/// <param name="StartLocal">Local start, already converted from the stored UTC.</param>
/// <param name="Lane">
/// Which sub-column of its chair to draw in, for double-booked time.
/// </param>
/// <param name="Lanes">
/// How many appointments share this chair at this time, so the block knows how wide to be.
/// One where nothing overlaps.
/// </param>
/// <remarks>
/// Lanes exist because a diary must never hide a booking. Two appointments in one chair at
/// one time is a mistake, but drawing them on top of each other conceals the mistake
/// instead of showing it — and the hidden one is the appointment nobody prepares for.
/// </remarks>
public sealed record DiaryBlock(
    Guid AppointmentId,
    Guid PatientId,
    Guid OperatoryId,
    string PatientName,
    DateTime StartLocal,
    int Minutes,
    string Reason,
    string? Colour,
    AppointmentStatus Status,
    int Lane,
    int Lanes)
{
    public DateTime EndLocal => StartLocal.AddMinutes(Minutes);

    /// <summary>Minutes from the practice opening, which is where the grid starts.</summary>
    public int OffsetMinutes =>
        (int)(StartLocal.TimeOfDay.TotalMinutes) - PracticeHours.OpenMinutes;
}

/// <summary>
/// Everything the day view shows for one location on one date.
/// </summary>
public sealed class DiaryDay
{
    public required DateOnly Day { get; init; }

    public IReadOnlyList<DiaryColumn> Columns { get; init; } = [];

    public IReadOnlyList<DiaryBlock> Blocks { get; init; } = [];

    /// <summary>
    /// Bookings the day view cannot place: no chair assigned, or a time outside opening
    /// hours.
    /// </summary>
    /// <remarks>
    /// Surfaced rather than dropped. An appointment that exists in the database but on no
    /// screen is the worst outcome available here — the practice has committed to a
    /// patient and nobody can see it.
    /// </remarks>
    public IReadOnlyList<DiaryBlock> Unplaced { get; init; } = [];

    public int BookedCount { get; init; }

    /// <summary>Cancellations and no-shows, which hold no chair time but happened.</summary>
    public int LostCount { get; init; }

    public bool IsOpen => PracticeHours.IsOpenOn(Day);
}

/// <summary>One provider's load on one day, for the week view's bars.</summary>
public sealed record DiaryProviderLoad(string ProviderName, int BookedMinutes)
{
    public int UtilisationPercent => PracticeHours.WorkingMinutes == 0
        ? 0
        : (int)Math.Round(100m * BookedMinutes / PracticeHours.WorkingMinutes);

    /// <summary>"5h 15m" — the figure beside the bar.</summary>
    public string BookedLabel => BookedMinutes == 0
        ? "nothing booked"
        : $"{BookedMinutes / 60}h {BookedMinutes % 60:00}m";
}

/// <summary>One day of the week view.</summary>
public sealed record DiaryWeekDay(
    DateOnly Date,
    int BookedCount,
    IReadOnlyList<DiaryProviderLoad> Providers)
{
    public bool IsOpen => PracticeHours.IsOpenOn(Date);
}

/// <summary>
/// One cell of the month view.
/// </summary>
/// <param name="InMonth">
/// False for the leading and trailing days that pad the grid to whole weeks. Rendered
/// faded rather than blank, so the weeks stay aligned.
/// </param>
public sealed record DiaryMonthCell(DateOnly Date, int BookedCount, bool InMonth)
{
    public bool IsOpen => PracticeHours.IsOpenOn(Date);
}

/// <summary>One row of the recall worklist.</summary>
/// <param name="DueOn">Kept as a date, not a rendered string — the view formats it.</param>
public sealed record DiaryRecallRow(
    Guid RecallId,
    Guid PatientId,
    string PatientName,
    string RecallType,
    DateOnly DueOn,
    DateTime? LastContactedUtc,
    int ContactAttempts,
    bool IsBooked);

/// <summary>One row of the short-notice list.</summary>
public sealed record DiaryWaitlistRow(
    Guid EntryId,
    Guid PatientId,
    string PatientName,
    string Wants,
    string Availability,
    string? Mobile,
    WaitlistPriority Priority,
    DateTime? LastContactedUtc);
