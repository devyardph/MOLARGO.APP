using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One line of a treatment plan: a procedure, on a tooth, at a price.
/// </summary>
public sealed class TreatmentPlanItem : EntityBase
{
    public Guid TreatmentPlanId { get; set; }

    public Guid ProcedureCodeId { get; set; }

    /// <summary>
    /// The item number and description as at planning. Copied rather than read through
    /// <see cref="ProcedureCodeId"/> so a schedule update does not silently reword a plan
    /// the patient has already signed.
    /// </summary>
    public string ItemNumber { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>The tooth, where the item is per-tooth.</summary>
    public string? ToothNumber { get; set; }

    public ToothSurface Surfaces { get; set; } = ToothSurface.None;

    public int Quantity { get; set; } = 1;

    /// <summary>The fee quoted for this line, again copied at planning time.</summary>
    public decimal Fee { get; set; }

    public decimal EstimatedBenefit { get; set; }

    public TreatmentItemStatus Status { get; set; } = TreatmentItemStatus.Planned;

    /// <summary>
    /// Which visit this line is delivered in — plans are routinely staged over several
    /// appointments, and the staging is what the patient is agreeing to.
    /// </summary>
    public int StageNumber { get; set; } = 1;

    /// <summary>Order within the stage.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>Set once booked.</summary>
    public Guid? AppointmentId { get; set; }

    public DateTime? CompletedUtc { get; set; }

    /// <summary>The invoice line raised for it, once charged.</summary>
    public Guid? InvoiceLineId { get; set; }

    public string? Notes { get; set; }
}
