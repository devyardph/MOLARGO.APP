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
    /// The subscription itself, frozen when the charge was raised.
    /// </summary>
    /// <remarks>
    /// The plan only. Text messages are <see cref="SmsAmount"/> and what the clinic owes is
    /// <see cref="Total"/> — kept apart rather than summed into one figure, because the two
    /// answer different questions. "Why is this month more than last" is a usage question
    /// nine times out of ten, and a single total cannot be asked it.
    ///
    /// Never recomputed from the plan. The clinic's clinician count and site count move,
    /// and a charge that repriced itself would quietly rewrite what a practice was billed
    /// last March — which is the one number in the system that must not change.
    /// </remarks>
    public decimal Amount { get; set; }

    /// <summary>
    /// Billable text messages included in this charge.
    /// </summary>
    /// <remarks>
    /// Counted from the communication log by the rule in <c>SmsBilling</c> — sent,
    /// delivered or replied to. Queued, failed and suppressed messages never left, so
    /// nobody is charged for them.
    /// </remarks>
    public int SmsCount { get; set; }

    /// <summary>
    /// The per-message rate used, or null where no rate could be found.
    /// </summary>
    /// <remarks>
    /// Nullable because null and zero are different answers, and both happen. Zero is a
    /// real rate — a country where messages are included in the plan. Null means the
    /// country's gateway was gone by the time the charge was raised, so the messages are
    /// recorded and not priced, and the billing screen says so rather than quietly billing
    /// them at nothing.
    ///
    /// Frozen here, like the amount. The rate that applies is the one in force when the
    /// charge is raised, which means changing a country's price mid-month reprices that
    /// month's messages up until the run — and never afterwards.
    /// </remarks>
    public decimal? SmsPricePerMessage { get; set; }

    /// <summary>
    /// Messages the plan covered, frozen at the rate the plan allowed that month.
    /// </summary>
    /// <remarks>
    /// Stored rather than recomputed from the plan, like every other figure on this row. A
    /// plan's allowance can be raised next quarter, and a charge that recalculated would
    /// retrospectively forgive messages a practice was billed for.
    /// </remarks>
    public int SmsIncluded { get; set; }

    /// <summary>What the messages came to.</summary>
    public decimal SmsAmount { get; set; }

    /// <summary>
    /// The month the messages were sent in, which is not this charge's own period.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Subscription in advance, usage in arrears — the split every metered bill uses, and
    /// for a reason worth stating. A charge raised on the first of March would find almost
    /// no March messages; one raised on the twentieth would bill a month the clinic is
    /// still adding to, and the figure would be wrong the moment it was stored. February's
    /// total is finished and cannot move, so March's charge carries it.
    /// </para>
    /// <para>
    /// Null on a charge raised before this existed, and on one that billed no messages.
    /// </para>
    /// </remarks>
    public DateOnly? SmsPeriodStart { get; set; }

    public DateOnly? SmsPeriodEnd { get; set; }

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

    /// <summary>What the clinic owes for this period — the plan plus its messages.</summary>
    public decimal Total => Amount + SmsAmount;

    /// <summary>Messages actually charged for — those beyond the plan's allowance.</summary>
    public int SmsBillable => Math.Max(0, SmsCount - SmsIncluded);

    /// <summary>True where the month's messages all fell inside the allowance.</summary>
    public bool SmsAllInclusive => SmsCount > 0 && SmsBillable == 0;

    /// <summary>True where this charge carries a usage line worth showing.</summary>
    public bool HasSmsLine => SmsCount > 0;

    /// <summary>
    /// Messages were counted but could not be priced.
    /// </summary>
    /// <remarks>
    /// The country's gateway was removed between the messages going out and the charge
    /// being raised. Worth surfacing: the clinic sent them and the carrier billed the
    /// vendor for them, so a zero here is money nobody has accounted for.
    /// </remarks>
    /// <remarks>
    /// Only where something was actually chargeable. A month entirely inside the allowance
    /// has no rate because none was needed, and flagging that as unpriced would report a
    /// problem where the plan worked exactly as sold.
    /// </remarks>
    public bool SmsNotPriced => SmsBillable > 0 && SmsPricePerMessage is null;

    /// <summary>"Feb 2026", for the usage line's own month.</summary>
    public string? SmsPeriodLabel => SmsPeriodStart?.ToString("MMM yyyy");

    public bool IsPaid => Status == ChargeStatus.Paid;

    /// <summary>Still owed — raised or attempted, and not settled or written off.</summary>
    public bool IsOutstanding => Status is ChargeStatus.Due or ChargeStatus.Failed;

    /// <summary>"Mar 2026", for a table that shows one row a month.</summary>
    public string PeriodLabel => PeriodStart.ToString("MMM yyyy");
}
