using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A proposed course of treatment, with its estimate — what the patient is shown and asked
/// to accept.
/// </summary>
public sealed class TreatmentPlan : EntityBase
{
    public Guid PatientId { get; set; }

    public Guid ProviderId { get; set; }

    public Guid PracticeLocationId { get; set; }

    /// <summary>What the patient sees at the top of the estimate.</summary>
    public string Title { get; set; } = string.Empty;

    public TreatmentPlanStatus Status { get; set; } = TreatmentPlanStatus.Draft;

    /// <summary>
    /// Clinical rationale and the alternatives discussed. Part of the plan rather than a
    /// separate note, because informed consent rests on the alternatives having been put.
    /// </summary>
    public string? Rationale { get; set; }

    public DateTime? PresentedUtc { get; set; }

    public DateTime? DecidedUtc { get; set; }

    /// <summary>
    /// An estimate stops being reliable once the fee schedule moves or a fund's annual
    /// limits reset, so it carries its own expiry rather than standing indefinitely.
    /// </summary>
    public DateOnly? EstimateValidUntil { get; set; }

    /// <summary>
    /// Total fee, copied from the items when the plan is presented rather than summed on
    /// read. The number the patient was quoted has to survive a later change to the fee
    /// schedule or to the plan's own lines.
    /// </summary>
    public decimal QuotedTotal { get; set; }

    /// <summary>Expected fund and Medicare benefit, as estimated at presentation.</summary>
    public decimal EstimatedBenefit { get; set; }

    /// <summary>What the patient was told they would pay: quoted total less benefit.</summary>
    public decimal EstimatedGap => QuotedTotal - EstimatedBenefit;

    /// <summary>
    /// Presentation order among several alternatives — plan A, plan B. Real practice
    /// offers a choice, and which one was recommended matters to the record.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>The plan the clinician recommended, where several were offered.</summary>
    public bool IsRecommended { get; set; }
}
