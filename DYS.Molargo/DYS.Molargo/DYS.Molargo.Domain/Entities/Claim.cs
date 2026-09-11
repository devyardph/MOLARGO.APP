using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A claim against a health fund, Medicare or the DVA.
/// </summary>
public sealed class Claim : EntityBase
{
    public Guid PatientId { get; set; }

    public Guid InvoiceId { get; set; }

    public ClaimType Type { get; set; }

    public ClaimStatus Status { get; set; } = ClaimStatus.Draft;

    /// <summary>The fund or scheme claimed against.</summary>
    public string? PayerName { get; set; }

    /// <summary>The patient's membership or card number, as quoted on the claim.</summary>
    public string? MemberNumber { get; set; }

    /// <summary>
    /// The payer's own reference, returned on submission. The only thing to quote when
    /// chasing an assessment, so it is captured even for a claim still pending.
    /// </summary>
    public string? PayerReference { get; set; }

    public decimal AmountClaimed { get; set; }

    /// <summary>What the payer actually allowed. Zero until assessed.</summary>
    public decimal AmountApproved { get; set; }

    /// <summary>What falls back to the patient: claimed less approved.</summary>
    public decimal PatientGap => AmountClaimed - AmountApproved;

    public DateTime? SubmittedUtc { get; set; }

    public DateTime? AssessedUtc { get; set; }

    /// <summary>
    /// The payer's reason for rejecting or reducing it, verbatim. Verbatim because it is
    /// what gets read back to the payer on appeal.
    /// </summary>
    public string? AssessmentMessage { get; set; }

    public Guid? SubmittedByProviderId { get; set; }
}
