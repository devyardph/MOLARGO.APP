namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// Records that instruments from one sterilisation cycle were used on one patient.
///
/// This is the join that makes a failed cycle actionable: given a cycle that did not
/// pass, it answers which patients have to be contacted — and given a patient, which
/// cycle their instruments came from.
/// </summary>
public sealed class SterilisationCycleUse : EntityBase
{
    public Guid SterilisationCycleId { get; set; }

    public Guid PatientId { get; set; }

    public Guid? AppointmentId { get; set; }

    public DateTime UsedUtc { get; set; }

    /// <summary>Which tray or pouch, where the load held several.</summary>
    public string? PackIdentifier { get; set; }

    public Guid? RecordedByProviderId { get; set; }
}
