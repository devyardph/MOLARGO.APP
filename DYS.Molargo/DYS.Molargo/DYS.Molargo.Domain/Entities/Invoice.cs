using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A charge raised against a patient's account.
/// </summary>
/// <remarks>
/// Totals are stored, not summed from the lines on read. An issued invoice is a financial
/// document: it has to keep saying what it said when it was issued, whatever later happens
/// to a line, a fee schedule or a rounding rule.
/// </remarks>
public sealed class Invoice : EntityBase
{
    public Guid PatientId { get; set; }

    public Guid PracticeLocationId { get; set; }

    /// <summary>The clinician whose provider number the charge is billed under.</summary>
    public Guid ProviderId { get; set; }

    /// <summary>
    /// Human-facing number, quoted over the phone and printed on the receipt. Assigned on
    /// issue rather than on creation, so drafts do not consume numbers and leave gaps an
    /// auditor will ask about.
    /// </summary>
    public string? InvoiceNumber { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    public Guid? AppointmentId { get; set; }

    public Guid? TreatmentPlanId { get; set; }

    public DateTime? IssuedUtc { get; set; }

    public DateOnly? DueOn { get; set; }

    /// <summary>Sum of the lines before any discount.</summary>
    public decimal Subtotal { get; set; }

    /// <summary>
    /// Discount as an amount, not a percentage. What was actually taken off is the fact
    /// that has to reconcile; a percentage re-derived against a changed subtotal does not.
    /// </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// GST. Most dental treatment is GST-free in Australia, so this is usually zero — but
    /// not always: whitening and some appliances attract it.
    /// </summary>
    public decimal TaxAmount { get; set; }

    public decimal Total { get; set; }

    /// <summary>
    /// Payments and benefits received against this invoice. Maintained as payments are
    /// recorded rather than summed on read, because the outstanding balance is read on
    /// every list row and the payments are not.
    /// </summary>
    public decimal AmountPaid { get; set; }

    public decimal AmountOutstanding => Total - AmountPaid;

    public bool IsSettled => AmountOutstanding <= 0m;

    public string? Notes { get; set; }

    /// <summary>Why it was voided or written off, which an auditor will ask for.</summary>
    public string? AdjustmentReason { get; set; }
}
