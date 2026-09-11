using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One charged item on an invoice.
/// </summary>
public sealed class InvoiceLine : EntityBase
{
    public Guid InvoiceId { get; set; }

    public Guid? ProcedureCodeId { get; set; }

    /// <summary>
    /// Item number and description as charged. Copied, for the same reason the invoice
    /// stores its totals: a claim assessed next month must match the words sent today.
    /// </summary>
    public string ItemNumber { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? ToothNumber { get; set; }

    public ToothSurface Surfaces { get; set; } = ToothSurface.None;

    public int Quantity { get; set; } = 1;

    /// <summary>Fee for one unit, as charged.</summary>
    public decimal UnitFee { get; set; }

    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// Line total, stored rather than computed from quantity and unit fee. Per-line
    /// rounding has to be settled once and stay settled, or the lines stop summing to the
    /// invoice total by a cent.
    /// </summary>
    public decimal LineTotal { get; set; }

    /// <summary>The date of service, which is what a claim is assessed against.</summary>
    public DateOnly ServiceDate { get; set; }

    public Guid? ProviderId { get; set; }

    /// <summary>The plan line this fulfils, where it came from a plan.</summary>
    public Guid? TreatmentPlanItemId { get; set; }
}
