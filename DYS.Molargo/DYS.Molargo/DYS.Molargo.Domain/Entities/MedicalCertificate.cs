namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A certificate of attendance and unfitness for work or study.
/// </summary>
/// <remarks>
/// Its own record rather than a stored document, because it is a legal statement made by a
/// named clinician about named dates: who signed it, for whom, and for exactly which days
/// all have to be answerable later. A PDF in a folder answers none of those without being
/// opened and read.
/// </remarks>
public sealed class MedicalCertificate : EntityBase
{
    public Guid PatientId { get; set; }

    /// <summary>The clinician certifying. Their AHPRA number appears on the certificate.</summary>
    public Guid ProviderId { get; set; }

    /// <summary>The visit it relates to, where there was one.</summary>
    public Guid? AppointmentId { get; set; }

    /// <summary>The day the patient actually attended.</summary>
    public DateOnly AttendedOn { get; set; }

    /// <summary>First day unfit. Usually the attendance date.</summary>
    public DateOnly UnfitFrom { get; set; }

    /// <summary>
    /// Last day unfit, inclusive. Same as <see cref="UnfitFrom"/> for a single day.
    /// </summary>
    public DateOnly UnfitTo { get; set; }

    /// <summary>Work or study — the wording differs and employers ask for the right one.</summary>
    public bool IsForStudy { get; set; }

    /// <summary>The certificate text as issued, kept verbatim.</summary>
    public string? Body { get; set; }

    public DateTime? IssuedUtc { get; set; }

    public bool IsIssued => IssuedUtc is not null;

    /// <summary>Days covered, inclusive of both ends.</summary>
    public int Days => UnfitTo.DayNumber - UnfitFrom.DayNumber + 1;
}
