namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One completed medical-history questionnaire, as filled in on the check-in tablet.
///
/// A new row per completion rather than an update to one standing record. The clinical
/// question is "what did the patient tell us on the day", and overwriting last year's
/// answers destroys exactly that.
/// </summary>
public sealed class MedicalHistoryForm : EntityBase
{
    public Guid PatientId { get; set; }

    /// <summary>The visit it was completed for, where it was taken at check-in.</summary>
    public Guid? AppointmentId { get; set; }

    /// <summary>
    /// Which version of the question set was answered. Without it, an answer to question 7
    /// cannot be interpreted once the questionnaire changes.
    /// </summary>
    public int FormVersion { get; set; } = 1;

    public DateTime? CompletedUtc { get; set; }

    public bool IsComplete => CompletedUtc is not null;

    /// <summary>The patient's declaration that the answers are true and complete.</summary>
    public DateTime? SignedUtc { get; set; }

    public string? SignedByName { get; set; }

    public string? SignatureImage { get; set; }

    /// <summary>The clinician who went through it with the patient.</summary>
    public Guid? ReviewedByProviderId { get; set; }

    public DateTime? ReviewedUtc { get; set; }

    /// <summary>Free-text notes the patient added beyond the fixed questions.</summary>
    public string? AdditionalNotes { get; set; }
}
