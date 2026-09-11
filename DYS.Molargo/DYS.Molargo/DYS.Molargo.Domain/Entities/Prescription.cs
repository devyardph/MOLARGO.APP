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
}
