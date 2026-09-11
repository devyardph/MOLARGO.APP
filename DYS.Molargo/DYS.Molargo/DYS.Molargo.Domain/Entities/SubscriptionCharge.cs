using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One period's subscription charge against one clinic.
/// </summary>
/// <remarks>
/// <para>
/// The vendor's billing record, and the only place a payment outcome exists. Nothing
/// collects money — there is no gateway, no card on file and no retry — so every status on
/// one of these was recorded by a person after the money did or did not arrive somewhere
/// else. The billing screen says so rather than letting a green tick imply an integration.
/// </para>
/// <para>
/// Its <c>TenantId</c> is the <em>clinic's</em>, not the vendor's, so a practice can be
/// shown its own charges later without any of this moving. The vendor reads across the
/// boundary the way it reads counts — deliberately, per query.
/// </para>
/// </remarks>
public sealed class SubscriptionCharge : EntityBase
{
    /// <summary>The plan the clinic was on when this was raised.</summary>
    /// <remarks>
    /// Recorded alongside the amount so an old charge can still say what it was for after
    /// the clinic moves plan — or after the plan is withdrawn.
    /// </remarks>
    public Guid? PlanId { get; set; }

    /// <summary>What the plan was called at the time.</summary>
    /// <remarks>
    /// A snapshot of the name, unusually for this codebase, and for the reason a stored
    /// name is normally wrong: an invoice must keep saying "Practice" even after the plan
    /// is renamed. History is the one thing a denormalised label is right for.
    /// </remarks>
    public string? PlanName { get; set; }

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    /// <summary>
    /// The amount charged, frozen when the charge was raised.
    /// </summary>
    /// <remarks>
    /// Never recomputed from the plan. The clinic's clinician count and site count move,
    /// and a charge that repriced itself would quietly rewrite what a practice was billed
    /// last March — which is the one number in the system that must not change.
    /// </remarks>
    public decimal Amount { get; set; }

    public string CurrencyCode { get; set; } = "AUD";

    /// <summary>Sites and clinicians the amount was worked out from.</summary>
    /// <remarks>
    /// Kept so a disputed invoice can be explained without reconstructing the clinic's
    /// staff list as it stood that month.
    /// </remarks>
    public int Sites { get; set; }

    public int Clinicians { get; set; }

    public ChargeStatus Status { get; set; } = ChargeStatus.Due;

    /// <summary>When collection was last attempted, or null if never.</summary>
    public DateTime? AttemptedUtc { get; set; }

    /// <summary>When the money was recorded as received.</summary>
    public DateTime? SettledUtc { get; set; }

    /// <summary>
    /// Why it failed, in the words of whoever recorded it.
    /// </summary>
    /// <remarks>
    /// Free text rather than a code list. The reasons come from a bank statement, an email
    /// or a phone call, not from an API, and an enum would force every one of them into
    /// the nearest wrong box.
    /// </remarks>
    public string? FailureReason { get; set; }

    /// <summary>The vendor's own receipt or transfer reference.</summary>
    public string? Reference { get; set; }

    public bool IsPaid => Status == ChargeStatus.Paid;

    /// <summary>Still owed — raised or attempted, and not settled or written off.</summary>
    public bool IsOutstanding => Status is ChargeStatus.Due or ChargeStatus.Failed;

    /// <summary>"Mar 2026", for a table that shows one row a month.</summary>
    public string PeriodLabel => PeriodStart.ToString("MMM yyyy");
}
