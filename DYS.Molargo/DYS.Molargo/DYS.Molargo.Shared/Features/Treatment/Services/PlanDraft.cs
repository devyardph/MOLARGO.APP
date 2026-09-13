using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Shared.Features.Treatment.Services;

/// <summary>
/// A treatment plan while it is being built — the plan builder's working copy.
/// </summary>
/// <remarks>
/// <para>
/// Mutable, and not the entity. <see cref="TreatmentPlan"/> carries the numbers that were
/// quoted to a patient and the timestamps of when they were quoted; those must be written
/// by the act of presenting, not by somebody typing in a form. This is the half-finished
/// state of a clinician thinking, which is a different thing.
/// </para>
/// <para>
/// It also holds what the entity deliberately splits across two tables — the plan and its
/// lines — because the builder edits them as one document and saving half of it is never
/// what anyone wants.
/// </para>
/// </remarks>
public sealed class PlanDraft
{
    /// <summary>
    /// How many visits a plan may be staged over.
    /// </summary>
    /// <remarks>
    /// Three, which is what the design's visit-sequence panel shows and what covers the
    /// courses a general practice actually plans — crown prep/fit, RCT over three visits.
    /// A free number would let a line be assigned to visit 9 of a plan with three cards
    /// and then vanish from the panel that is supposed to show every line.
    /// </remarks>
    public const int MaxStages = 3;

    /// <summary>Empty for a plan that has not been saved yet.</summary>
    public Guid Id { get; set; }

    public Guid PatientId { get; set; }

    /// <summary>Shown in the builder's heading. Not persisted on the plan.</summary>
    public string PatientName { get; set; } = string.Empty;

    /// <summary>
    /// The clinician the plan is under, who is accountable for it.
    /// </summary>
    /// <remarks>
    /// Always a clinical role. Reception routinely prepares an estimate, but the plan is a
    /// clinical proposal and has to be attributed to somebody who could have made it.
    /// </remarks>
    public Guid ProviderId { get; set; }

    public Guid PracticeLocationId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Why this and not the alternatives. Read out on the presentation screen, because
    /// informed consent rests on the alternatives having been put.
    /// </summary>
    public string? Rationale { get; set; }

    public TreatmentPlanStatus Status { get; set; } = TreatmentPlanStatus.Draft;

    public bool IsRecommended { get; set; }

    /// <summary>
    /// What the funds and Medicare are expected to pay, entered rather than calculated.
    /// </summary>
    /// <remarks>
    /// Zero means "not estimated", not "your fund pays nothing" — the presentation screen
    /// says so rather than printing a gap equal to the full fee. Working it out properly
    /// needs each fund's item-by-item rebate at the patient's level of cover, which is a
    /// table this app does not have; guessing it here would put a number in front of a
    /// patient that the practice cannot stand behind.
    /// </remarks>
    public decimal EstimatedBenefit { get; set; }

    /// <summary>When the quote stops being reliable. Set on presentation if left empty.</summary>
    public DateOnly? EstimateValidUntil { get; set; }

    public List<PlanDraftItem> Items { get; set; } = [];

    public bool IsNew => Id == Guid.Empty;

    /// <summary>The sum of the lines. What <c>QuotedTotal</c> is locked to on presentation.</summary>
    public decimal Total => Items.Sum(item => item.Fee);

    /// <summary>What the patient would pay, where a benefit has been estimated.</summary>
    public decimal Gap => Total - EstimatedBenefit;

    /// <summary>Whether a benefit has actually been estimated. See <see cref="EstimatedBenefit"/>.</summary>
    public bool HasBenefitEstimate => EstimatedBenefit > 0m;

    /// <summary>The lines in one visit, in the order they are delivered.</summary>
    public IReadOnlyList<PlanDraftItem> Stage(int stageNumber) =>
        Items
            .Where(item => item.StageNumber == stageNumber)
            .OrderBy(item => item.DisplayOrder)
            .ToList();

    public decimal StageTotal(int stageNumber) => Stage(stageNumber).Sum(item => item.Fee);

    /// <summary>Chair time for one visit, from the items' typical durations.</summary>
    /// <remarks>
    /// The sum, rounded up to the practice's slot length, so the figure the plan shows is
    /// a slot that could actually be booked rather than "95 min".
    /// </remarks>
    public int StageMinutes(int stageNumber)
    {
        var minutes = Stage(stageNumber).Sum(item => item.DurationMinutes);

        if (minutes == 0) return 0;

        var slot = PracticeHours.SlotMinutes;

        return (minutes + slot - 1) / slot * slot;
    }

    /// <summary>The visits that have anything in them, lowest first.</summary>
    public IReadOnlyList<int> UsedStages =>
        Enumerable.Range(1, MaxStages)
            .Where(stage => Items.Any(item => item.StageNumber == stage))
            .ToList();
}

/// <summary>
/// One line of a plan being built: a procedure, on a tooth, at a price, in a visit.
/// </summary>
public sealed class PlanDraftItem
{
    /// <summary>Empty for a line that has not been saved yet.</summary>
    public Guid Id { get; set; }

    public Guid ProcedureCodeId { get; set; }

    public string ItemNumber { get; set; } = string.Empty;

    /// <summary>
    /// The schedule's wording, which is what a claim has to carry.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>The practice's plain-English label, which is what the patient reads.</summary>
    public string? FriendlyName { get; set; }

    public string? ToothNumber { get; set; }

    public decimal Fee { get; set; }

    public int StageNumber { get; set; } = 1;

    public int DisplayOrder { get; set; }

    /// <summary>Typical chair time, for sizing the visit.</summary>
    public int DurationMinutes { get; set; }

    public TreatmentItemStatus Status { get; set; } = TreatmentItemStatus.Planned;

    /// <summary>
    /// The charted finding this line addresses, where it came from the odontogram.
    /// </summary>
    /// <remarks>
    /// Carried so saving can write the link back onto the finding. A chart entry that
    /// knows which plan item will deal with it is what stops the same decay being planned
    /// twice by two clinicians a fortnight apart.
    /// </remarks>
    public Guid? FromChartEntryId { get; set; }

    /// <summary>What the patient sees: the friendly name, and the tooth where there is one.</summary>
    public string PatientLabel => ToothNumber is { Length: > 0 } tooth
        ? $"{FriendlyName ?? Description} — tooth {tooth}"
        : FriendlyName ?? Description;

    /// <summary>
    /// Lines already delivered or booked are not the builder's to move or remove.
    /// </summary>
    /// <remarks>
    /// Editing the fee on a completed item would restate what the patient was charged, and
    /// moving a booked one between visits would silently disagree with the appointment
    /// that exists for it.
    /// </remarks>
    public bool IsLocked => Status is TreatmentItemStatus.Completed or TreatmentItemStatus.Scheduled;
}

/// <summary>
/// What the builder's pickers offer: the catalogue at this site's prices, and the
/// clinicians a plan can be attributed to.
/// </summary>
public sealed class PlanBuilderOptions
{
    public IReadOnlyList<PlanCodeOption> Codes { get; init; } = [];

    public IReadOnlyList<PlanProviderOption> Providers { get; init; } = [];

    /// <summary>Teeth the patient has a current finding on, for the tooth picker.</summary>
    public IReadOnlyList<string> ChartedTeeth { get; init; } = [];
}

/// <param name="Fee">
/// This site's price, not the practice fee, where the two differ. A plan quoted at one
/// site's prices and delivered at another's is a complaint waiting to happen.
/// </param>
public sealed record PlanCodeOption(
    Guid Id,
    string ItemNumber,
    string Description,
    string? FriendlyName,
    string? Category,
    decimal Fee,
    bool IsPerTooth,
    int DurationMinutes)
{
    public string Label => FriendlyName ?? Description;
}

public sealed record PlanProviderOption(Guid Id, string Name, string? Role);

/// <summary>
/// The outcome of a save.
/// </summary>
/// <param name="Errors">Field name to message; empty on success.</param>
public sealed record PlanSaveResult(Guid PlanId, IReadOnlyDictionary<string, string> Errors)
{
    public bool Succeeded => Errors.Count == 0;

    public static PlanSaveResult Refused(string field, string message) =>
        new(Guid.Empty, new Dictionary<string, string> { [field] = message });
}

/// <summary>
/// One plan as the chairside presentation shows it, with the alternatives beside it.
/// </summary>
/// <param name="Visits">
/// Each visit with its booking state, so the accepted panel can offer "book visit 2" and
/// "open visit 1" without a second round trip.
/// </param>
/// <param name="Alternatives">
/// The patient's other live plans, which is what the design's "Option A / B / C" cards
/// are. Held as plans rather than as a separate options concept: a practice that offers a
/// crown, a large filling and an extraction is offering three plans, and modelling them as
/// anything else means the one the patient picks cannot simply be accepted.
/// </param>
public sealed record PlanPresentation(
    PlanDraft Plan,
    string PatientName,
    string ProviderName,
    IReadOnlyList<PlanOption> Alternatives,
    IReadOnlyList<PlanVisitBooking> Visits,
    ConsentForm? Consent)
{
    /// <summary>Whether the plan can still be decided on.</summary>
    public bool AwaitsDecision =>
        Plan.Status is TreatmentPlanStatus.Draft
            or TreatmentPlanStatus.Presented;

    public bool IsAccepted =>
        Plan.Status is TreatmentPlanStatus.Accepted
            or TreatmentPlanStatus.InProgress
            or TreatmentPlanStatus.Completed;

    public bool IsDeclined => Plan.Status == TreatmentPlanStatus.Declined;
}

/// <summary>
/// One visit of an accepted plan, as the booking form needs it to prefill itself.
/// </summary>
/// <remarks>
/// Everything here is read back from the plan rather than carried in the URL that opened
/// the form. Only the plan id and the visit number travel, for the same reason the patient
/// id does: a query string is editable, and a form that booked whatever it was handed
/// would put one patient's treatment under another patient's name.
/// </remarks>
/// <param name="Minutes">
/// The visit's own chair time, rounded to a bookable slot. The single biggest cause of a
/// diary running late is a length guessed by hand, and the plan already knows this one.
/// </param>
/// <param name="NeedsBooking">
/// True while any item of the visit is still merely planned. This, not the presence of an
/// appointment, is what decides whether to offer a booking — a visit whose items were all
/// delivered years ago has no appointment attached and must not be offered as unbooked.
/// </param>
/// <param name="IsDelivered">
/// Every item completed. Historical: there is nothing to book and nothing to open.
/// </param>
/// <param name="AppointmentId">
/// The appointment this visit sits in, where all of it sits in one. Null where the visit
/// has never been booked, and also where a hand-edited plan split it across two — calling
/// that "booked" and linking to one of them would hide the other half.
/// </param>
public sealed record PlanVisitBooking(
    Guid PlanId,
    int Stage,
    Guid PatientId,
    string PatientLabel,
    Guid ProviderId,
    Guid PracticeLocationId,
    string Reason,
    int Minutes,
    decimal Fee,
    bool NeedsBooking,
    bool IsDelivered,
    Guid? AppointmentId)
{
    /// <summary>Booked and not yet delivered — the state with an appointment to open.</summary>
    public bool IsBooked => !NeedsBooking && !IsDelivered;
}

/// <summary>One of the plans on offer, as an option card.</summary>
public sealed record PlanOption(
    Guid Id,
    string Title,
    string? Rationale,
    decimal Total,
    decimal Benefit,
    int VisitCount,
    bool IsRecommended,
    TreatmentPlanStatus Status)
{
    public decimal Gap => Total - Benefit;

    public bool HasBenefitEstimate => Benefit > 0m;
}

/// <summary>
/// One procedure at a booked visit, with the consent that covers it.
/// </summary>
/// <param name="ConsentId">Null where no form has been raised for it yet.</param>
public sealed record VisitConsentRow(
    Guid ItemId,
    Guid? ConsentId,
    string Label,
    ConsentStatus? Status)
{
    /// <summary>
    /// Anything short of a signed form. Refused, withdrawn, expired and missing are all
    /// the same answer to "can this go ahead" — no — and collapsing them here keeps every
    /// caller from having to remember the four ways consent can be absent.
    /// </summary>
    public bool Outstanding => Status != ConsentStatus.Signed;
}

/// <summary>
/// The consent position for one booked plan visit: what is being done, and what has been
/// agreed to.
/// </summary>
/// <remarks>
/// Per procedure, not per appointment. Consent is for a named procedure and its risks —
/// one form covering "today's visit" says nothing about what was actually discussed, and
/// is the form that cannot be relied on in the one case where it matters.
/// </remarks>
public sealed record VisitConsent(
    Guid AppointmentId,
    Guid PatientId,
    Guid PlanId,
    string PlanTitle,
    int Stage,
    IReadOnlyList<VisitConsentRow> Rows)
{
    public int OutstandingCount => Rows.Count(row => row.Outstanding);

    /// <summary>Every procedure that needs consent has a signed form.</summary>
    public bool IsClear => OutstandingCount == 0;

    /// <summary>Some procedure has no form raised against it at all.</summary>
    public bool HasUnraised => Rows.Any(row => row.ConsentId is null);
}
