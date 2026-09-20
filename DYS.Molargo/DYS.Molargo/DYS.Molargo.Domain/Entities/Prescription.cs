using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A prescription written for a patient.
/// </summary>
public sealed class Prescription : EntityBase
{
    public Guid PatientId { get; set; }

    /// <summary>The prescriber. Their provider number appears on the script.</summary>
    public Guid ProviderId { get; set; }

    public Guid? AppointmentId { get; set; }

    public PrescriptionStatus Status { get; set; } = PrescriptionStatus.Draft;

    public DateTime? IssuedUtc { get; set; }

    /// <summary>
    /// Validity. A script is not indefinitely dispensable, and an expired one presented
    /// at a pharmacy is refused — worth showing on the record before the patient goes.
    /// </summary>
    public DateOnly? ValidUntil { get; set; }

    public string? PharmacyName { get; set; }

    /// <summary>
    /// The allergy check performed before writing, recorded as done. Prescribing against a
    /// recorded allergy is the highest-consequence mistake in this app; noting that the
    /// alerts were seen is what makes the check auditable.
    /// </summary>
    public DateTime? AllergyCheckedUtc { get; set; }

    public string? Notes { get; set; }

    /// <summary>Set once dispensing is confirmed.</summary>
    public DateTime? DispensedUtc { get; set; }

    /// <summary>
    /// The prescriber's drawn signature, as a PNG data URI.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Inline rather than a document, like the consent form's — it is a few kilobytes and
    /// it is meaningless apart from the script it signs. A row in the documents table would
    /// make it possible for the two to be separated, which for a signature is the one thing
    /// that must not happen.
    /// </para>
    /// <para>
    /// Not what makes a script legal. Nothing in this app is an eRx token and no gateway
    /// carries it to a pharmacy — this is the prescriber signing the paper the patient
    /// walks out with, captured so the practice's own copy shows the same signature as the
    /// printed one.
    /// </para>
    /// </remarks>
    public string? PrescriberSignature { get; set; }

    /// <summary>When the prescriber signed, which is not when they issued.</summary>
    /// <remarks>
    /// Separate from <see cref="IssuedUtc"/> because they are separate acts and can be
    /// minutes apart: issuing records the decision, signing happens on the printed script.
    /// A single timestamp would make a script that was issued and never signed
    /// indistinguishable from one that was.
    /// </remarks>
    public DateTime? SignedUtc { get; set; }

    /// <summary>True where the prescriber has put a signature on it.</summary>
    public bool IsSigned => !string.IsNullOrWhiteSpace(PrescriberSignature);
}
