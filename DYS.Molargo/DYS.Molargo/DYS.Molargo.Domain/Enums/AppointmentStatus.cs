namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// Where an appointment sits on the day. Drives the diary's colour coding and, for
/// <see cref="FailedToAttend"/>, the practice's FTA follow-up worklist.
/// </summary>
public enum AppointmentStatus
{
    /// <summary>Booked, not yet arrived.</summary>
    Scheduled = 0,

    /// <summary>Reminder sent and the patient replied to confirm.</summary>
    Confirmed = 1,

    /// <summary>Arrived and waiting.</summary>
    CheckedIn = 2,

    /// <summary>In the chair.</summary>
    InProgress = 3,

    /// <summary>Treatment finished; may still owe payment.</summary>
    Completed = 4,

    /// <summary>Cancelled with notice, so the slot could be offered on.</summary>
    Cancelled = 5,

    /// <summary>
    /// Did not arrive and did not cancel. Tracked separately from
    /// <see cref="Cancelled"/> because it costs the practice the slot and feeds the
    /// patient's FTA count.
    /// </summary>
    FailedToAttend = 6,

    /// <summary>
    /// In the chair, clinician not yet started. The front desk's cue that the room is
    /// occupied but the appointment is not yet running late.
    /// </summary>
    /// <remarks>
    /// Numbered 7 although it belongs between <see cref="CheckedIn"/> and
    /// <see cref="InProgress"/>. Added after the others, and the values are persisted, so
    /// renumbering to put it in order would reinterpret every stored row. Anything that
    /// needs the day's real order must use <c>AppointmentProgress</c> rather than sorting
    /// on this value.
    /// </remarks>
    Seated = 7,
}
