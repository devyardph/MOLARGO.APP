using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// Money received — or, with a negative amount, refunded.
/// </summary>
/// <remarks>
/// Payments are never edited or deleted. A mistake is corrected by a reversing payment, so
/// the day's takings always reconcile to what was actually banked.
/// </remarks>
public sealed class Payment : EntityBase
{
    public Guid PatientId { get; set; }

    public Guid PracticeLocationId { get; set; }

    /// <summary>
    /// The invoice it settles. Nullable: a patient can pay money onto their account
    /// before anything is invoiced, which is common for orthodontic plans.
    /// </summary>
    public Guid? InvoiceId { get; set; }

    public PaymentMethod Method { get; set; }

    /// <summary>Negative for a refund or a reversal.</summary>
    public decimal Amount { get; set; }

    public DateTime ReceivedUtc { get; set; }

    /// <summary>Terminal or bank reference, for matching against the merchant statement.</summary>
    public string? Reference { get; set; }

    /// <summary>The claim, where the money came from a fund or Medicare rather than the patient.</summary>
    public Guid? ClaimId { get; set; }

    /// <summary>Who took it, for the end-of-day reconciliation.</summary>
    public Guid? ReceivedByProviderId { get; set; }

    /// <summary>Set on a payment that reverses an earlier one, naming it.</summary>
    public Guid? ReversesPaymentId { get; set; }

    public string? Notes { get; set; }
}
