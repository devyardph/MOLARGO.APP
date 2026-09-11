namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A clinical note for one visit.
/// </summary>
/// <remarks>
/// Notes lock. Once <see cref="LockedUtc"/> is set the text is immutable and a correction
/// has to be a new note referencing this one — an editable clinical record is worth
/// nothing in a dispute, which is the only time anyone reads it.
/// </remarks>
public sealed class ClinicalNote : EntityBase
{
    public Guid PatientId { get; set; }

    public Guid? AppointmentId { get; set; }

    public Guid ProviderId { get; set; }

    /// <summary>When the care described happened, not when the note was typed.</summary>
    public DateTime TreatmentDateUtc { get; set; }

    /// <summary>The patient's reported reason for attending.</summary>
    public string? Presenting { get; set; }

    /// <summary>What was found on examination.</summary>
    public string? Examination { get; set; }

    /// <summary>What was diagnosed.</summary>
    public string? Diagnosis { get; set; }

    /// <summary>What was done, including materials, anaesthetic and batch numbers.</summary>
    public string? TreatmentProvided { get; set; }

    /// <summary>Advice given, and what is planned next.</summary>
    public string? Plan { get; set; }

    /// <summary>
    /// Set when the note is signed off. Null means still a draft, editable by its author
    /// and nobody else.
    /// </summary>
    public DateTime? LockedUtc { get; set; }

    public bool IsLocked => LockedUtc is not null;

    /// <summary>
    /// Set on a note that corrects an earlier locked one, pointing at what it amends. An
    /// amendment never rewrites the original.
    /// </summary>
    public Guid? AmendsNoteId { get; set; }
}
