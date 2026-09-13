using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A record of informed consent for a specific treatment.
///
/// Tied to the treatment it covers rather than held as a blanket agreement: consent is for
/// a named procedure, its risks and its cost, and a general form signed two years ago
/// covers nothing that happens today.
/// </summary>
public sealed class ConsentForm : EntityBase
{
    public Guid PatientId { get; set; }

    /// <summary>The plan or item this consent covers.</summary>
    public Guid? TreatmentPlanId { get; set; }

    public Guid? TreatmentPlanItemId { get; set; }

    /// <summary>What was consented to, in the words the patient saw.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The risks, alternatives and costs as presented. Stored, not referenced.</summary>
    public string? Body { get; set; }

    public ConsentStatus Status { get; set; } = ConsentStatus.Pending;

    public DateTime? SignedUtc { get; set; }

    /// <summary>
    /// Who signed. Often not the patient — a parent for a child, a guardian under a power
    /// of attorney — so the name and relationship are recorded rather than assumed.
    /// </summary>
    public string? SignedByName { get; set; }

    public string? SignedByRelationship { get; set; }

    /// <summary>
    /// The captured signature, as a PNG data URI. Held inline rather than as a document
    /// row: it is small, and it must never become separable from the consent it belongs to.
    /// </summary>
    /// <remarks>
    /// Nothing writes this yet. Capturing a drawn signature needs a canvas and pointer
    /// events through JS interop, which this app uses nowhere — consent signed in the app
    /// is typed, and consent signed on paper is scanned and linked through
    /// <see cref="DocumentId"/>.
    /// </remarks>
    public string? SignatureImage { get; set; }

    /// <summary>
    /// The scanned original, where the patient signed on paper.
    /// </summary>
    /// <remarks>
    /// A reference to the uploaded <see cref="PatientDocument"/> rather than a copy of the
    /// bytes. The file lives in the document store like every other upload — it is the
    /// same scan the Documents tab lists — and this says which consent it is evidence for.
    /// Without the link the two were unrelated rows that happened to share a patient, and
    /// a form marked signed had nothing behind it but a typed name.
    /// </remarks>
    public Guid? DocumentId { get; set; }

    /// <summary>The clinician who obtained consent, who is accountable for the discussion.</summary>
    public Guid? WitnessedByProviderId { get; set; }

    /// <summary>Set for a consent that lapses if the treatment is not carried out in time.</summary>
    public DateOnly? ExpiresOn { get; set; }
}
