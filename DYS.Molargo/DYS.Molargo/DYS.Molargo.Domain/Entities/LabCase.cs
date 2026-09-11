using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// Work sent to a dental laboratory — a crown, a denture, an appliance.
/// </summary>
public sealed class LabCase : EntityBase
{
    public Guid PatientId { get; set; }

    public Guid ProviderId { get; set; }

    public Guid? SupplierId { get; set; }

    public LabCaseStatus Status { get; set; } = LabCaseStatus.Prepared;

    /// <summary>What is being made — "Crown, e.max, tooth 46".</summary>
    public string Description { get; set; } = string.Empty;

    public string? ToothNumber { get; set; }

    /// <summary>Shade, material and any specific instruction to the technician.</summary>
    public string? Specification { get; set; }

    /// <summary>The lab's own docket number, quoted when chasing.</summary>
    public string? LabReference { get; set; }

    public DateTime? SentUtc { get; set; }

    /// <summary>
    /// When the lab has promised it. The field the diary needs: the fit appointment
    /// cannot be booked before it, and booking one anyway is how a patient arrives to
    /// find their crown is not there.
    /// </summary>
    public DateOnly? DueOn { get; set; }

    public DateTime? ReceivedUtc { get; set; }

    /// <summary>The visit it will be fitted at.</summary>
    public Guid? FitAppointmentId { get; set; }

    public DateTime? FittedUtc { get; set; }

    public decimal? LabFee { get; set; }

    /// <summary>Why it went back, for a remake. Tracked because remakes are a lab-quality signal.</summary>
    public string? RemakeReason { get; set; }

    public bool IsOverdue(DateOnly today) =>
        DueOn is { } due && due < today && Status is not (LabCaseStatus.Received or LabCaseStatus.Fitted or LabCaseStatus.Cancelled);
}
