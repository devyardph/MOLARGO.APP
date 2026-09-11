using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A due-date for the patient's next routine visit, and the practice's main source of
/// repeat work.
/// </summary>
public sealed class Recall : EntityBase
{
    public Guid PatientId { get; set; }

    /// <summary>What is due — "Exam &amp; clean", "Perio maintenance".</summary>
    public string RecallType { get; set; } = string.Empty;

    /// <summary>
    /// Months between visits. Per recall rather than a practice default: a periodontal
    /// patient is on three months where a low-risk adult is on twelve.
    /// </summary>
    public int IntervalMonths { get; set; } = 6;

    public DateOnly DueOn { get; set; }

    public RecallStatus Status { get; set; } = RecallStatus.Pending;

    /// <summary>The visit that set this recall running.</summary>
    public Guid? LastAppointmentId { get; set; }

    /// <summary>Set once a booking exists, which closes the recall.</summary>
    public Guid? BookedAppointmentId { get; set; }

    public DateTime? LastContactedUtc { get; set; }

    /// <summary>
    /// How many times the patient has been chased. The practice stops after a configured
    /// number rather than contacting someone indefinitely, which is both a nuisance and a
    /// Spam Act problem.
    /// </summary>
    public int ContactAttempts { get; set; }

    public string? Notes { get; set; }
}
