using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One booking in the diary.
/// </summary>
public sealed class Appointment : EntityBase
{
    public Guid PatientId { get; set; }

    public Guid PracticeLocationId { get; set; }

    /// <summary>The clinician who owns the slot.</summary>
    public Guid ProviderId { get; set; }

    /// <summary>The chair, and therefore the diary column. Null for an unassigned booking.</summary>
    public Guid? OperatoryId { get; set; }

    public Guid? AppointmentTypeId { get; set; }

    /// <summary>
    /// Start of the appointment, in UTC. Stored in UTC even though a diary is an
    /// inherently local thing: a booking made the night before a daylight-saving change
    /// otherwise moves by an hour, and a multi-site practice cannot compare two sites'
    /// days at all.
    /// </summary>
    public DateTime StartUtc { get; set; }

    /// <summary>
    /// Length in minutes, rather than an end timestamp. The diary drags and resizes by
    /// duration, and two fields that must agree eventually disagree.
    /// </summary>
    public int DurationMinutes { get; set; } = 30;

    public DateTime EndUtc => StartUtc.AddMinutes(DurationMinutes);

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Scheduled;

    /// <summary>What the visit is for, shown as the second line of the diary block.</summary>
    public string? Reason { get; set; }

    /// <summary>Front-desk note — "running late", "bringing daughter".</summary>
    public string? Notes { get; set; }

    /// <summary>The plan this visit delivers, where it came from one.</summary>
    public Guid? TreatmentPlanId { get; set; }

    /// <summary>The course this visit belongs to, where it was booked as a series.</summary>
    public Guid? AppointmentSeriesId { get; set; }

    /// <summary>
    /// Which visit of the course this is — 1, 2, 3. Null outside a series.
    /// </summary>
    /// <remarks>
    /// Stored rather than worked out by sorting the course's appointments on date. The
    /// number means a position in the <em>treatment</em>, not in the calendar: moving the
    /// second visit later than the third does not make it the third, and a label that
    /// renumbered itself when a patient rescheduled would be actively misleading at the
    /// chair.
    /// </remarks>
    public int? SeriesPosition { get; set; }

    /// <summary>True where this booking is one visit of a course.</summary>
    public bool IsInSeries => AppointmentSeriesId is { } id && id != Guid.Empty;

    public DateTime? ConfirmedUtc { get; set; }

    public DateTime? CheckedInUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }

    /// <summary>
    /// Why it was cancelled or missed, captured at the time. Reconstructing it later from
    /// the status alone is impossible, and it is what the FTA follow-up call needs.
    /// </summary>
    public string? CancellationReason { get; set; }

    /// <summary>
    /// True where the slot was filled from the short-notice list. Worth measuring: it is
    /// the difference between a cancellation costing the practice the hour or not.
    /// </summary>
    public bool FilledFromWaitlist { get; set; }
}
