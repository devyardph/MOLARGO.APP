namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One periodontal examination — a full-mouth or partial probing on one date.
/// </summary>
/// <remarks>
/// An exam is its own record rather than readings hung straight off the patient, because
/// the whole clinical value of perio charting is the comparison between exams: "6mm at 16
/// mesial" means nothing until you know it was 4mm in February. Deleting or overwriting an
/// exam destroys the only evidence that treatment worked.
/// </remarks>
public sealed class PerioExam : EntityBase
{
    public Guid PatientId { get; set; }

    /// <summary>Who probed. Null for a legacy or imported exam with no author recorded.</summary>
    public Guid? ProviderId { get; set; }

    /// <summary>When the probing happened, not when it was typed up.</summary>
    public DateOnly ExamDate { get; set; }

    /// <summary>Free-text summary — "3-monthly maintenance, improvement in UR".</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Set when the clinician finishes. An unfinished exam is a draft: it is still shown,
    /// but it must not be compared against as though it were a complete picture.
    /// </summary>
    public DateTime? CompletedUtc { get; set; }

    public bool IsComplete => CompletedUtc is not null;
}
