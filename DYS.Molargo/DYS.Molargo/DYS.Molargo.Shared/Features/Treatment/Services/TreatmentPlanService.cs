using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Components;
using DYS.Molargo.Shared.Features.Billing.Services;
using DYS.Molargo.Shared.Repositories;
using DYS.Molargo.Shared.Services;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Features.Treatment.Services;

/// <summary>
/// Building, presenting and deciding treatment plans.
/// </summary>
/// <remarks>
/// <para>
/// The two screens either side of this service are the design's plan builder and its
/// chairside presentation. What sits between them is the one rule that matters: a plan's
/// numbers are copied at the moment it is presented and never recomputed afterwards. An
/// estimate the patient was shown has to survive the fee schedule going up the following
/// week, and a total summed on read does not.
/// </para>
/// <para>
/// Two things the design shows are deliberately not here, and the screens say so rather
/// than faking them. There is no named fee-schedule picker — this app prices per site
/// (<c>ProcedureCodeFee</c>), and a plan carries the site it was quoted at. And there is
/// no per-item fund rebate: that needs each fund's schedule at the patient's level of
/// cover, and a plausible-looking guess in front of a patient is worse than no number.
/// </para>
/// </remarks>
public interface ITreatmentPlanService
{
    /// <summary>
    /// A new plan for this patient, with a line proposed for every charted finding that
    /// needs treating.
    /// </summary>
    /// <remarks>
    /// Nothing is written. The clinician edits and saves; a plan that was opened and
    /// abandoned should leave no trace on the record.
    /// </remarks>
    Task<PlanDraft?> StartFromChartAsync(Guid patientId, CancellationToken ct = default);

    /// <summary>An existing plan and its lines, for editing.</summary>
    Task<PlanDraft?> GetDraftAsync(Guid planId, CancellationToken ct = default);

    /// <summary>What the builder's pickers offer, at this site's prices.</summary>
    Task<PlanBuilderOptions> GetOptionsAsync(
        Guid patientId, Guid locationId, CancellationToken ct = default);

    /// <summary>Writes the plan and its lines, or returns the reasons it cannot be written.</summary>
    Task<PlanSaveResult> SaveAsync(PlanDraft draft, CancellationToken ct = default);

    /// <summary>
    /// Marks the plan as shown to the patient, locking the quoted figures to the lines as
    /// they stand.
    /// </summary>
    Task<string?> PresentAsync(Guid planId, CancellationToken ct = default);

    /// <summary>The plan as the chairside screen shows it, with the alternatives.</summary>
    Task<PlanPresentation?> GetPresentationAsync(Guid planId, CancellationToken ct = default);

    /// <summary>
    /// Records the patient accepting the plan, with who agreed and in what capacity.
    /// </summary>
    /// <param name="signedByName">
    /// Typed, not drawn. See the remarks on <see cref="TreatmentPlanService"/>.
    /// </param>
    /// <param name="relationship">
    /// "Self", "Parent", "Guardian". Recorded rather than assumed: consent for a child is
    /// given by somebody else, and a record that does not say who is not evidence.
    /// </param>
    /// <param name="signatureImage">
    /// The drawn signature as a PNG data URI, where one was captured on the tablet.
    /// </param>
    Task<string?> AcceptAsync(
        Guid planId,
        string? signedByName,
        string? relationship,
        string? signatureImage = null,
        CancellationToken ct = default);

    /// <summary>Records the patient declining. The plan is kept, as clinical record.</summary>
    Task<string?> DeclineAsync(Guid planId, string? reason, CancellationToken ct = default);

    /// <summary>
    /// Withdraws a plan the practice is no longer standing behind — superseded by a newer
    /// one, or no longer clinically appropriate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The way out for a plan that has been accepted, which cannot be declined: the
    /// patient did agree, and rewriting that to "they said no" would falsify the record.
    /// Voiding says the practice withdrew it, which is what actually happened.
    /// </para>
    /// <para>
    /// Appointments already booked against it are deliberately left alone. Cancelling
    /// somebody's Tuesday without telling them is not a side effect any button should
    /// have — <see cref="ITreatmentPlanService.GetVisitsAsync"/> is what the screen uses
    /// to warn first, and the visits are then cancelled from the diary one at a time.
    /// Delivered items keep their status too: voiding a plan does not un-do a filling.
    /// </para>
    /// </remarks>
    Task<string?> VoidAsync(Guid planId, string? reason, CancellationToken ct = default);

    /// <summary>
    /// Deletes a plan outright. Only ever a draft.
    /// </summary>
    /// <remarks>
    /// A plan that was shown to a patient is a clinical record and is kept whatever
    /// happens to it — that is what <see cref="VoidAsync"/> is for. A draft is different:
    /// nobody has seen it, nothing was quoted, and a mis-started plan that can only be
    /// voided leaves permanent clutter on a record to say that somebody once clicked the
    /// wrong button. Soft-deleted, like everything else in this domain.
    /// </remarks>
    Task<string?> DeleteDraftAsync(Guid planId, CancellationToken ct = default);

    /// <summary>
    /// One visit of a plan, as the booking form needs it to prefill itself.
    /// </summary>
    /// <returns>Null where the plan, or that visit of it, does not exist.</returns>
    Task<PlanVisitBooking?> GetVisitAsync(
        Guid planId, int stage, CancellationToken ct = default);

    /// <summary>Every visit of a plan, for the "book visit 1 / 2 / 3" row.</summary>
    Task<IReadOnlyList<PlanVisitBooking>> GetVisitsAsync(
        Guid planId, CancellationToken ct = default);

    /// <summary>
    /// Ties one visit's items to the appointment just booked for them.
    /// </summary>
    /// <remarks>
    /// The write that was missing. Until this existed, booking an accepted plan in the
    /// diary left the plan reading "Accepted — not booked" forever, kept its items out of
    /// the record's treatment-in-progress table, and left it on the unscheduled-treatment
    /// worklist that exists precisely to be drained.
    /// </remarks>
    Task<string?> LinkVisitAsync(
        Guid planId, int stage, Guid appointmentId, CancellationToken ct = default);

    /// <summary>
    /// Releases whatever plan items were booked into an appointment.
    /// </summary>
    /// <remarks>
    /// Called when the appointment is cancelled or missed. Without it the worklist breaks
    /// the other way — treatment counted as booked against a slot that no longer exists,
    /// which is a worse failure than the one linking fixed, because nothing would ever
    /// surface it again.
    /// </remarks>
    Task UnlinkAppointmentAsync(Guid appointmentId, CancellationToken ct = default);

    /// <summary>
    /// The consent position for a booked plan visit, or null where the appointment is not
    /// one.
    /// </summary>
    /// <remarks>
    /// Returns a record with no rows — rather than null — for a plan visit whose procedures
    /// need no separate consent. "Nothing to sign here" and "this is not a plan visit" look
    /// identical to a caller handed null for both, and the first of those is worth saying
    /// out loud to whoever is about to start treatment.
    /// </remarks>
    Task<VisitConsent?> VisitConsentAsync(
        Guid appointmentId, CancellationToken ct = default);

    /// <summary>
    /// Raises the pending consent forms for a booked visit's procedures.
    /// </summary>
    /// <remarks>
    /// Booking raises these already. This is for the visits booked before it did, and for
    /// items added to a visit afterwards — without it those appointments would have no
    /// route to a form except somebody retyping the wording by hand.
    /// </remarks>
    Task<string?> RaiseVisitConsentAsync(
        Guid appointmentId, CancellationToken ct = default);

    /// <summary>
    /// How many procedures still lack signed consent, per appointment.
    /// </summary>
    /// <remarks>
    /// Takes the whole day at once and answers in three queries. The front desk asks this
    /// for every arrival on the list, and doing it one appointment at a time put a query
    /// per row on the screen that is loaded most often in the building.
    ///
    /// Appointments with nothing outstanding are absent from the dictionary rather than
    /// present with zero, so a caller reads it with TryGetValue and gets the right answer
    /// for an appointment that is not a plan visit at all.
    /// </remarks>
    Task<IReadOnlyDictionary<Guid, int>> OutstandingConsentAsync(
        IReadOnlyCollection<Guid> appointmentIds, CancellationToken ct = default);
}

/// <inheritdoc cref="ITreatmentPlanService"/>
public sealed class TreatmentPlanService : ITreatmentPlanService
{
    /// <summary>How long an estimate stands, where the clinician does not set a date.</summary>
    /// <remarks>
    /// Two months. Long enough to think it over, short enough that it expires before the
    /// fund year resets and the gap the patient was quoted stops being true.
    /// </remarks>
    private const int DefaultEstimateMonths = 2;

    private readonly IRepository<TreatmentPlan> _plans;
    private readonly IRepository<TreatmentPlanItem> _items;
    private readonly IRepository<ToothChartEntry> _chart;
    private readonly IRepository<ProcedureCode> _codes;
    private readonly IRepository<PatientEntity> _patients;
    private readonly IRepository<Provider> _providers;
    private readonly IRepository<ConsentForm> _consents;
    private readonly IRepository<ConsentTemplate> _consentTemplates;
    private readonly IBillingService _billing;
    private readonly ISessionService _session;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;

    public TreatmentPlanService(
        IRepository<TreatmentPlan> plans,
        IRepository<TreatmentPlanItem> items,
        IRepository<ToothChartEntry> chart,
        IRepository<ProcedureCode> codes,
        IRepository<PatientEntity> patients,
        IRepository<Provider> providers,
        IRepository<ConsentForm> consents,
        IRepository<ConsentTemplate> consentTemplates,
        IBillingService billing,
        ISessionService session,
        IClock clock,
        IAuditLog audit)
    {
        _plans = plans;
        _items = items;
        _chart = chart;
        _codes = codes;
        _patients = patients;
        _providers = providers;
        _consents = consents;
        _consentTemplates = consentTemplates;
        _billing = billing;
        _session = session;
        _clock = clock;
        _audit = audit;
    }

    public async Task<PlanDraft?> StartFromChartAsync(
        Guid patientId, CancellationToken ct = default)
    {
        var patient = await _patients.GetByIdAsync(patientId, ct).ConfigureAwait(false);

        if (patient is null) return null;

        var providerId = await DefaultProviderAsync(patient, ct).ConfigureAwait(false);

        var draft = new PlanDraft
        {
            PatientId = patientId,
            PatientName = patient.FullName,
            ProviderId = providerId,
            PracticeLocationId = _session.LocationId,
            Title = string.Empty,

            // Recommended only where nothing else already is. A first plan is the
            // practice's recommendation by definition, so defaulting it off left every
            // one presenting as an also-ran — but defaulting it on unconditionally put a
            // second "Recommended" badge on the chairside screen beside the first, which
            // tells the patient nothing and the clinician something false.
            IsRecommended =
                !await HasRecommendedPlanAsync(patientId, Guid.Empty, ct).ConfigureAwait(false),

            EstimateValidUntil = _clock.Today.AddMonths(DefaultEstimateMonths),
        };

        draft.Items.AddRange(
            await ProposeFromChartAsync(patientId, ct).ConfigureAwait(false));

        // Named from what is actually in it, so a plan saved without the clinician
        // touching the title is still findable in a list six months later.
        draft.Title = SuggestTitle(draft.Items);

        return draft;
    }

    public async Task<PlanDraft?> GetDraftAsync(Guid planId, CancellationToken ct = default)
    {
        var plan = await _plans.GetByIdAsync(planId, ct).ConfigureAwait(false);

        if (plan is null) return null;

        var patient = await _patients.GetByIdAsync(plan.PatientId, ct).ConfigureAwait(false);

        var lines = await _items
            .ListAsync(item => item.TreatmentPlanId == planId, ct)
            .ConfigureAwait(false);

        var codes = await _codes.ListAsync(ct: ct).ConfigureAwait(false);
        var byId = codes.ToDictionary(code => code.Id);

        var draft = new PlanDraft
        {
            Id = plan.Id,
            PatientId = plan.PatientId,
            PatientName = patient?.FullName ?? "Unknown patient",
            ProviderId = plan.ProviderId,
            PracticeLocationId = plan.PracticeLocationId,
            Title = plan.Title,
            Rationale = plan.Rationale,
            Status = plan.Status,
            IsRecommended = plan.IsRecommended,
            EstimatedBenefit = plan.EstimatedBenefit,
            EstimateValidUntil = plan.EstimateValidUntil,
        };

        draft.Items.AddRange(lines
            .OrderBy(item => item.StageNumber)
            .ThenBy(item => item.DisplayOrder)
            .Select(item => new PlanDraftItem
            {
                Id = item.Id,
                ProcedureCodeId = item.ProcedureCodeId,
                ItemNumber = item.ItemNumber,
                Description = item.Description,

                // The friendly name is read live rather than copied onto the line. It is
                // the practice's own wording for the patient, not the schedule's, so
                // improving it should improve every plan that shows it — unlike the
                // description and the fee, which are what the patient was quoted.
                FriendlyName = byId.GetValueOrDefault(item.ProcedureCodeId)?.PatientFriendlyName,
                ToothNumber = item.ToothNumber,
                Fee = item.Fee,
                StageNumber = Math.Clamp(item.StageNumber, 1, PlanDraft.MaxStages),
                DisplayOrder = item.DisplayOrder,
                DurationMinutes =
                    byId.GetValueOrDefault(item.ProcedureCodeId)?.TypicalDurationMinutes ?? 0,
                Status = item.Status,
            }));

        return draft;
    }

    public async Task<PlanBuilderOptions> GetOptionsAsync(
        Guid patientId, Guid locationId, CancellationToken ct = default)
    {
        var catalogue = await _billing
            .GetCatalogueAsync(locationId, includeWithdrawn: false, ct)
            .ConfigureAwait(false);

        var providers = await _providers.ListAsync(ct: ct).ConfigureAwait(false);

        var charted = await _chart
            .ListAsync(entry => entry.PatientId == patientId, ct)
            .ConfigureAwait(false);

        return new PlanBuilderOptions
        {
            Codes = catalogue
                .OrderBy(item => item.Code.Category)
                .ThenBy(item => item.ItemNumber, StringComparer.Ordinal)
                .Select(item => new PlanCodeOption(
                    item.Code.Id,
                    item.ItemNumber,
                    item.Description,
                    item.Code.PatientFriendlyName,
                    item.Code.Category,

                    // This site's price, which is the whole reason the catalogue is read
                    // through the billing service rather than off the code rows.
                    item.Fee,
                    item.Code.IsPerTooth,
                    item.Code.TypicalDurationMinutes ?? 0))
                .ToList(),

            Providers = providers
                .Where(provider => provider.IsActive && ProviderRoles.IsClinical(provider.Role))
                .OrderBy(provider => provider.FullName)
                .Select(provider => new PlanProviderOption(
                    provider.Id, provider.FullName, RoleLabel(provider.Role)))
                .ToList(),

            ChartedTeeth = charted
                .Where(entry => entry.IsCurrent)
                .Select(entry => entry.ToothNumber)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tooth => tooth, StringComparer.Ordinal)
                .ToList(),
        };
    }

    public async Task<PlanSaveResult> SaveAsync(
        PlanDraft draft, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(draft.Title))
        {
            return PlanSaveResult.Refused(
                nameof(PlanDraft.Title), "Give the plan a title the patient will recognise.");
        }

        if (draft.Items.Count == 0)
        {
            return PlanSaveResult.Refused(
                string.Empty, "A plan needs at least one item before it can be saved.");
        }

        // A plan is a clinical proposal, so it has to be attributable to somebody who
        // could have made it. Reception may build the estimate; it still goes out under a
        // clinician's name, and an unattributed one is not a plan.
        var provider = draft.ProviderId == Guid.Empty
            ? null
            : await _providers.GetByIdAsync(draft.ProviderId, ct).ConfigureAwait(false);

        if (provider is null || !ProviderRoles.IsClinical(provider.Role))
        {
            return PlanSaveResult.Refused(
                nameof(PlanDraft.ProviderId),
                "Choose the clinician this plan is under.");
        }

        if (draft.EstimatedBenefit < 0m)
        {
            return PlanSaveResult.Refused(
                nameof(PlanDraft.EstimatedBenefit),
                "A fund estimate cannot be negative.");
        }

        if (draft.EstimatedBenefit > draft.Total)
        {
            return PlanSaveResult.Refused(
                nameof(PlanDraft.EstimatedBenefit),
                "The fund estimate is more than the plan costs. A patient cannot be "
                    + "quoted a negative gap.");
        }

        var existing = draft.IsNew
            ? null
            : await _plans.GetByIdAsync(draft.Id, ct).ConfigureAwait(false);

        if (!draft.IsNew && existing is null)
        {
            return PlanSaveResult.Refused(string.Empty, "That plan no longer exists.");
        }

        // Presented figures are not the builder's to rewrite. Reopening a presented plan
        // and saving it would otherwise silently restate what the patient was quoted,
        // with the presentation timestamp still claiming they had seen this version.
        if (existing is not null && existing.PresentedUtc is not null)
        {
            return PlanSaveResult.Refused(
                string.Empty,
                "This plan has already been presented. Its figures are what the patient "
                    + "was quoted, so they are fixed — build a new plan instead.");
        }

        var plan = existing ?? new TreatmentPlan
        {
            Id = draft.Id,
            PatientId = draft.PatientId,
        };

        plan.ProviderId = draft.ProviderId;
        plan.PracticeLocationId = draft.PracticeLocationId;
        plan.Title = draft.Title.Trim();
        plan.Rationale = Blank(draft.Rationale);
        plan.IsRecommended = draft.IsRecommended;
        plan.EstimatedBenefit = draft.EstimatedBenefit;
        plan.EstimateValidUntil = draft.EstimateValidUntil;

        // Kept as a draft by the save. Presenting is what moves it on, and it is a
        // separate act with its own button, because it is the point at which the figures
        // stop being editable.
        plan.Status = existing?.Status ?? TreatmentPlanStatus.Draft;

        // Not summed on read. The entity's own comment says why, and this is the other
        // half of it: the running total is kept current so a draft list can show it, and
        // presentation freezes it.
        plan.QuotedTotal = draft.Total;

        var planId = await _plans.SaveAsync(plan, ct).ConfigureAwait(false);

        // The draft's id, so a second save of a plan just created updates it rather than
        // writing a second copy. Forgetting this is how the same plan ends up in the list
        // three times after three clicks of Save.
        draft.Id = planId;

        await SaveLinesAsync(draft, planId, ct).ConfigureAwait(false);

        if (draft.IsRecommended)
        {
            await DemoteOtherRecommendationsAsync(draft.PatientId, planId, ct)
                .ConfigureAwait(false);
        }

        return new PlanSaveResult(planId, new Dictionary<string, string>());
    }

    /// <summary>Whether the patient already has a live plan marked as the recommendation.</summary>
    private async Task<bool> HasRecommendedPlanAsync(
        Guid patientId, Guid excluding, CancellationToken ct)
    {
        var plans = await _plans
            .ListAsync(plan => plan.PatientId == patientId, ct)
            .ConfigureAwait(false);

        return plans.Any(plan =>
            plan.Id != excluding && plan.IsRecommended && IsLive(plan.Status));
    }

    /// <summary>
    /// Makes "recommended" mean one plan, by clearing the flag on the patient's others.
    /// </summary>
    /// <remarks>
    /// The flag is a statement about a choice — this is the one we advise — and two plans
    /// carrying it says nothing. Only live plans are touched: a declined or superseded
    /// plan was recommended at the time it was presented, and rewriting that would falsify
    /// what the patient was actually advised.
    /// </remarks>
    private async Task DemoteOtherRecommendationsAsync(
        Guid patientId, Guid keep, CancellationToken ct)
    {
        var plans = await _plans
            .ListAsync(plan => plan.PatientId == patientId, ct)
            .ConfigureAwait(false);

        foreach (var other in plans.Where(plan =>
            plan.Id != keep && plan.IsRecommended && IsLive(plan.Status)))
        {
            other.IsRecommended = false;

            await _plans.SaveAsync(other, ct).ConfigureAwait(false);
        }
    }

    /// <summary>A plan still in play — one the patient could still act on.</summary>
    private static bool IsLive(TreatmentPlanStatus status) =>
        status is not (TreatmentPlanStatus.Declined
            or TreatmentPlanStatus.Void
            or TreatmentPlanStatus.Completed);

    public async Task<string?> PresentAsync(Guid planId, CancellationToken ct = default)
    {
        var plan = await _plans.GetByIdAsync(planId, ct).ConfigureAwait(false);

        if (plan is null) return "That plan no longer exists.";

        if (plan.Status is TreatmentPlanStatus.Void or TreatmentPlanStatus.Declined)
        {
            return "That plan is closed. Build a new one rather than re-presenting it.";
        }

        var lines = await _items
            .ListAsync(item => item.TreatmentPlanId == planId, ct)
            .ConfigureAwait(false);

        if (lines.Count == 0) return "A plan with no items cannot be presented.";

        // Already presented: left exactly as it was. Re-presenting must not re-stamp the
        // total, because the figure on screen would then be today's fees while the
        // patient is looking at a quote they were given a fortnight ago.
        if (plan.PresentedUtc is not null) return null;

        plan.QuotedTotal = lines.Sum(item => item.Fee);
        plan.PresentedUtc = _clock.UtcNow;
        plan.Status = TreatmentPlanStatus.Presented;
        plan.EstimateValidUntil ??= _clock.Today.AddMonths(DefaultEstimateMonths);

        await _plans.SaveAsync(plan, ct).ConfigureAwait(false);

        await _audit
            .RecordAsync(
                AuditAction.Updated,
                nameof(TreatmentPlan),
                planId,
                $"Presented the plan {plan.Title} at {MolargoFormat.MoneyExact(plan.QuotedTotal)}",
                plan.PatientId,
                ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<PlanPresentation?> GetPresentationAsync(
        Guid planId, CancellationToken ct = default)
    {
        var draft = await GetDraftAsync(planId, ct).ConfigureAwait(false);

        if (draft is null) return null;

        var provider = await _providers
            .GetByIdAsync(draft.ProviderId, ct)
            .ConfigureAwait(false);

        var siblings = await _plans
            .ListAsync(plan => plan.PatientId == draft.PatientId, ct)
            .ConfigureAwait(false);

        var lines = await _items.ListAsync(ct: ct).ConfigureAwait(false);

        var alternatives = siblings
            .Where(plan => plan.Status is not (TreatmentPlanStatus.Void
                or TreatmentPlanStatus.Completed))
            .OrderByDescending(plan => plan.IsRecommended)
            .ThenBy(plan => plan.DisplayOrder)
            .ThenBy(plan => plan.CreatedUtc)
            .Select(plan =>
            {
                var own = lines.Where(item => item.TreatmentPlanId == plan.Id).ToList();

                return new PlanOption(
                    plan.Id,
                    plan.Title,
                    plan.Rationale,

                    // A presented plan shows what it was quoted at; a draft shows what it
                    // currently adds up to. The two are the same number until the fee
                    // schedule moves, and after that only the first one is honest.
                    plan.PresentedUtc is null ? own.Sum(item => item.Fee) : plan.QuotedTotal,
                    plan.EstimatedBenefit,
                    own.Select(item => item.StageNumber).Distinct().Count(),
                    plan.IsRecommended,
                    plan.Status);
            })
            .ToList();

        // The plan-level consent, not a per-procedure one. See RecordConsentAsync: a plan
        // carries several, and showing "accepted by…" from a consent signed for one
        // procedure would claim the whole plan had been agreed when it had not.
        var consent = await _consents
            .FindAsync(
                form => form.TreatmentPlanId == planId && form.TreatmentPlanItemId == null,
                ct)
            .ConfigureAwait(false);

        return new PlanPresentation(
            draft,
            draft.PatientName,
            provider?.FullName ?? "—",
            alternatives,
            await VisitsForAsync(draft, ct).ConfigureAwait(false),
            consent);
    }

    public async Task<string?> AcceptAsync(
        Guid planId,
        string? signedByName,
        string? relationship,
        string? signatureImage = null,
        CancellationToken ct = default)
    {
        var plan = await _plans.GetByIdAsync(planId, ct).ConfigureAwait(false);

        if (plan is null) return "That plan no longer exists.";

        if (plan.Status is TreatmentPlanStatus.Declined or TreatmentPlanStatus.Void)
        {
            return "That plan is closed and cannot be accepted.";
        }

        if (plan.Status is TreatmentPlanStatus.Accepted or TreatmentPlanStatus.InProgress
            or TreatmentPlanStatus.Completed)
        {
            return "That plan has already been accepted.";
        }

        var name = (signedByName ?? string.Empty).Trim();

        // Named, always. "The patient accepted" without a name is not a record of consent,
        // and for a child it is the parent who agreed — which is exactly the case where
        // an unnamed acceptance is worth nothing.
        if (name.Length < 2)
        {
            return "Type the name of the person accepting the plan.";
        }

        // Presentation is what locks the figures, so an acceptance that skipped it would
        // agree to a total nobody had frozen. Doing it here rather than refusing keeps
        // the chairside flow to one button when a clinician goes straight to accepting.
        if (plan.PresentedUtc is null)
        {
            var refusal = await PresentAsync(planId, ct).ConfigureAwait(false);

            if (refusal is not null) return refusal;

            plan = await _plans.GetByIdAsync(planId, ct).ConfigureAwait(false);

            if (plan is null) return "That plan no longer exists.";
        }

        plan.Status = TreatmentPlanStatus.Accepted;
        plan.DecidedUtc = _clock.UtcNow;

        await _plans.SaveAsync(plan, ct).ConfigureAwait(false);

        await RecordConsentAsync(plan, name, relationship, signatureImage, ct).ConfigureAwait(false);

        // The money the patient agreed to, as quoted on the day. The plan can be edited
        // afterwards; what they accepted cannot be re-derived from it once it has been.
        await _audit
            .RecordAsync(
                AuditAction.Updated,
                nameof(TreatmentPlan),
                planId,
                $"Plan {plan.Title} accepted by {name} "
                    + $"at {MolargoFormat.MoneyExact(plan.QuotedTotal)}",
                plan.PatientId,
                ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string?> DeclineAsync(
        Guid planId, string? reason, CancellationToken ct = default)
    {
        var plan = await _plans.GetByIdAsync(planId, ct).ConfigureAwait(false);

        if (plan is null) return "That plan no longer exists.";

        if (plan.Status is TreatmentPlanStatus.Accepted or TreatmentPlanStatus.InProgress
            or TreatmentPlanStatus.Completed)
        {
            return "That plan has been accepted. Declining it now would contradict the "
                + "record — withdraw it instead, on the patient's record.";
        }

        plan.Status = TreatmentPlanStatus.Declined;
        plan.DecidedUtc = _clock.UtcNow;

        // Onto the rationale, which is where the clinical reasoning already lives, rather
        // than into a decline-reason field that only this one path would ever write.
        plan.Rationale = AppendNote(plan.Rationale, "Declined", reason);

        await _plans.SaveAsync(plan, ct).ConfigureAwait(false);

        // A declined plan is the entry that matters most later: it is the practice's record
        // that treatment was offered and turned down, which is the defence when somebody
        // says it was never discussed.
        await _audit
            .RecordAsync(
                AuditAction.Updated,
                nameof(TreatmentPlan),
                planId,
                $"Plan {plan.Title} declined"
                    + (string.IsNullOrWhiteSpace(reason) ? string.Empty : $": {reason.Trim()}"),
                plan.PatientId,
                ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string?> VoidAsync(
        Guid planId, string? reason, CancellationToken ct = default)
    {
        var plan = await _plans.GetByIdAsync(planId, ct).ConfigureAwait(false);

        if (plan is null) return "That plan no longer exists.";

        if (plan.Status == TreatmentPlanStatus.Void) return "That plan is already void.";

        // Completed is not voidable. The work was delivered and in all likelihood charged;
        // marking the plan as withdrawn afterwards would contradict the invoice and the
        // clinical notes that reference it.
        if (plan.Status == TreatmentPlanStatus.Completed)
        {
            return "That plan is complete — the work was delivered. It cannot be withdrawn.";
        }

        var items = await _items
            .ListAsync(item => item.TreatmentPlanId == planId, ct)
            .ConfigureAwait(false);

        foreach (var item in items)
        {
            // Delivered work keeps its status and its appointment. The plan is withdrawn,
            // not the history of what was done under it.
            if (item.Status == TreatmentItemStatus.Completed) continue;

            // Everything outstanding stops being outstanding — which is what takes the
            // plan off the unscheduled-treatment worklist. The appointment link is cleared
            // with it, so a booking that survives the void is no longer counted as
            // delivering plan work.
            item.Status = TreatmentItemStatus.Void;
            item.AppointmentId = null;

            await _items.SaveAsync(item, ct).ConfigureAwait(false);
        }

        plan.Status = TreatmentPlanStatus.Void;
        plan.DecidedUtc ??= _clock.UtcNow;
        plan.Rationale = AppendNote(plan.Rationale, "Withdrawn", reason);

        await _plans.SaveAsync(plan, ct).ConfigureAwait(false);

        // The consent goes with it. A signed consent for treatment the practice has
        // withdrawn is not evidence of anything current, and leaving it Signed would show
        // the patient as having agreed to a plan nobody intends to deliver.
        await WithdrawConsentAsync(planId, ct).ConfigureAwait(false);

        await _audit
            .RecordAsync(
                AuditAction.Deleted,
                nameof(TreatmentPlan),
                planId,
                $"Plan {plan.Title} withdrawn"
                    + (string.IsNullOrWhiteSpace(reason) ? string.Empty : $": {reason.Trim()}"),
                plan.PatientId,
                ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string?> DeleteDraftAsync(Guid planId, CancellationToken ct = default)
    {
        var plan = await _plans.GetByIdAsync(planId, ct).ConfigureAwait(false);

        if (plan is null) return "That plan no longer exists.";

        // The one rule this method exists to enforce. Anything the patient has seen is a
        // clinical record: it is kept and withdrawn, never removed.
        if (plan.Status != TreatmentPlanStatus.Draft || plan.PresentedUtc is not null)
        {
            return "Only a draft can be deleted. This plan has been presented, so it is "
                + "part of the record — withdraw it instead.";
        }

        var items = await _items
            .ListAsync(item => item.TreatmentPlanId == planId, ct)
            .ConfigureAwait(false);

        foreach (var item in items)
        {
            await _items.DeleteAsync(item.Id, ct).ConfigureAwait(false);
        }

        await _plans.DeleteAsync(planId, ct).ConfigureAwait(false);

        return null;
    }

    /// <summary>
    /// Marks the plan's consent withdrawn, where one was signed.
    /// </summary>
    /// <remarks>
    /// Withdrawn rather than deleted: that the patient once agreed is a fact, and
    /// <c>ConsentStatus.Withdrawn</c> is the status the domain already has for "signed,
    /// then withdrawn before treatment". Per-procedure consents are left alone, for the
    /// same reason acceptance does not touch them.
    /// </remarks>
    private async Task WithdrawConsentAsync(Guid planId, CancellationToken ct)
    {
        var consent = await _consents
            .FindAsync(
                form => form.TreatmentPlanId == planId && form.TreatmentPlanItemId == null,
                ct)
            .ConfigureAwait(false);

        if (consent is not { Status: ConsentStatus.Signed }) return;

        consent.Status = ConsentStatus.Withdrawn;

        await _consents.SaveAsync(consent, ct).ConfigureAwait(false);
    }

    /// <summary>Appends a dated note to the plan's rationale, where a reason was given.</summary>
    private string? AppendNote(string? rationale, string heading, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return rationale;

        var note = $"{heading} {MolargoFormat.Date(_clock.Today)}: {reason.Trim()}";

        return string.IsNullOrWhiteSpace(rationale) ? note : $"{rationale}\n\n{note}";
    }

    // ---- booking the visits -----------------------------------------------

    public async Task<PlanVisitBooking?> GetVisitAsync(
        Guid planId, int stage, CancellationToken ct = default)
    {
        var visits = await GetVisitsAsync(planId, ct).ConfigureAwait(false);

        return visits.FirstOrDefault(visit => visit.Stage == stage);
    }

    public async Task<IReadOnlyList<PlanVisitBooking>> GetVisitsAsync(
        Guid planId, CancellationToken ct = default)
    {
        var draft = await GetDraftAsync(planId, ct).ConfigureAwait(false);

        return draft is null ? [] : await VisitsForAsync(draft, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The visit rows for a draft already in hand.
    /// </summary>
    /// <remarks>
    /// Split from <see cref="GetVisitsAsync"/> so the presentation screen, which has just
    /// built the draft, does not read the whole plan a second time to find out which of
    /// its visits are booked.
    /// </remarks>
    private async Task<IReadOnlyList<PlanVisitBooking>> VisitsForAsync(
        PlanDraft draft, CancellationToken ct)
    {
        var planId = draft.Id;

        var stored = await _items
            .ListAsync(item => item.TreatmentPlanId == planId, ct)
            .ConfigureAwait(false);

        var booked = stored
            .Where(item => item.AppointmentId is not null)
            .ToLookup(item => item.StageNumber, item => item.AppointmentId!.Value);

        return draft.UsedStages
            .Select(stage =>
            {
                var items = draft.Stage(stage);

                // The visit's appointment, where every one of its items points at the same
                // one. Items split across two appointments is not a state this offers to
                // create, but a plan edited by hand could reach it — and calling that
                // "booked" would hide half the work.
                var appointments = booked[stage].Distinct().ToList();

                return new PlanVisitBooking(
                    planId,
                    stage,
                    draft.PatientId,
                    draft.PatientName,
                    draft.ProviderId,
                    draft.PracticeLocationId,

                    // What the diary block will say. The plan's own wording for the visit,
                    // which is more use on a schedule than the appointment type's name.
                    Reason: VisitReason(draft.Title, items),
                    Minutes: draft.StageMinutes(stage),
                    Fee: draft.StageTotal(stage),

                    // Decided by the items, not by whether an appointment exists. A visit
                    // delivered long ago carries no appointment link in the seeded data
                    // and was being offered for booking — treatment already in the
                    // patient's mouth, proposed as work outstanding.
                    NeedsBooking: items.Any(item =>
                        item.Status == TreatmentItemStatus.Planned),

                    IsDelivered: items.Count > 0 && items.All(item =>
                        item.Status == TreatmentItemStatus.Completed),

                    AppointmentId: appointments.Count == 1 ? appointments[0] : null);
            })
            .ToList();
    }

    public async Task<string?> LinkVisitAsync(
        Guid planId, int stage, Guid appointmentId, CancellationToken ct = default)
    {
        var plan = await _plans.GetByIdAsync(planId, ct).ConfigureAwait(false);

        if (plan is null) return "That plan no longer exists.";

        // Booking is what a patient agreed to, so the plan has to have been agreed. A
        // declined or superseded plan being booked means somebody picked the wrong row.
        if (plan.Status is TreatmentPlanStatus.Declined or TreatmentPlanStatus.Void)
        {
            return "That plan is closed, so its visits cannot be booked.";
        }

        var items = await _items
            .ListAsync(item => item.TreatmentPlanId == planId, ct)
            .ConfigureAwait(false);

        var visit = items.Where(item => item.StageNumber == stage).ToList();

        if (visit.Count == 0) return "That visit has no items on it.";

        foreach (var item in visit)
        {
            // Completed items are left alone. A visit part-delivered and then rebooked for
            // the rest must not have finished work dragged back to "booked".
            if (item.Status == TreatmentItemStatus.Completed) continue;

            item.AppointmentId = appointmentId;
            item.Status = TreatmentItemStatus.Scheduled;

            await _items.SaveAsync(item, ct).ConfigureAwait(false);
        }

        // Raised at booking, not at acceptance and not on the day. At acceptance the
        // staging can still change, so a form would be raised against a visit that never
        // happens; on the day it is one more thing to remember while the patient is already
        // in the chair. Booking is the moment the procedure, the date and the clinician are
        // all settled.
        await RaiseConsentForItemsAsync(plan, visit, ct).ConfigureAwait(false);

        // "Accepted and at least partly booked", which is exactly what has just happened.
        // Until this write existed the status was unreachable from anywhere in the app.
        if (plan.Status is TreatmentPlanStatus.Accepted or TreatmentPlanStatus.Presented)
        {
            plan.Status = TreatmentPlanStatus.InProgress;

            await _plans.SaveAsync(plan, ct).ConfigureAwait(false);
        }

        return null;
    }

    public async Task<VisitConsent?> VisitConsentAsync(
        Guid appointmentId, CancellationToken ct = default)
    {
        var items = await _items
            .ListAsync(item => item.AppointmentId == appointmentId, ct)
            .ConfigureAwait(false);

        if (items.Count == 0) return null;

        var plan = await _plans
            .GetByIdAsync(items[0].TreatmentPlanId, ct)
            .ConfigureAwait(false);

        if (plan is null) return null;

        var rows = await ConsentRowsAsync(items, ct).ConfigureAwait(false);

        // The lowest stage on the appointment. A visit booked from the plan carries one
        // stage; an appointment that later collected items from two is mislabelled either
        // way, and the earlier number is the one the patient was quoted.
        var stage = items.Min(item => item.StageNumber);

        return new VisitConsent(
            appointmentId, plan.PatientId, plan.Id, plan.Title, stage, rows);
    }

    public async Task<string?> RaiseVisitConsentAsync(
        Guid appointmentId, CancellationToken ct = default)
    {
        var items = await _items
            .ListAsync(item => item.AppointmentId == appointmentId, ct)
            .ConfigureAwait(false);

        if (items.Count == 0)
        {
            return "There is no planned treatment booked into this appointment.";
        }

        var plan = await _plans
            .GetByIdAsync(items[0].TreatmentPlanId, ct)
            .ConfigureAwait(false);

        if (plan is null) return "That plan no longer exists.";

        var raised = await RaiseConsentForItemsAsync(plan, items, ct).ConfigureAwait(false);

        return raised == 0
            ? "Every procedure at this visit already has a consent form."
            : null;
    }

    public async Task<IReadOnlyDictionary<Guid, int>> OutstandingConsentAsync(
        IReadOnlyCollection<Guid> appointmentIds, CancellationToken ct = default)
    {
        var empty = new Dictionary<Guid, int>();

        if (appointmentIds.Count == 0) return empty;

        var ids = appointmentIds.ToList();

        var items = await _items
            .ListAsync(
                item => item.AppointmentId != null
                    && ids.Contains(item.AppointmentId.Value)
                    && item.Status != TreatmentItemStatus.Completed,
                ct)
            .ConfigureAwait(false);

        if (items.Count == 0) return empty;

        var codeIds = items.Select(item => item.ProcedureCodeId).Distinct().ToList();

        var codes = await _codes
            .ListAsync(code => codeIds.Contains(code.Id), ct)
            .ConfigureAwait(false);

        var templates = await ActiveTemplatesAsync(ct).ConfigureAwait(false);

        var consented = codes
            .Where(code => NeedsConsent(templates, code.Category))
            .Select(code => code.Id)
            .ToHashSet();

        var needing = items.Where(item => consented.Contains(item.ProcedureCodeId)).ToList();

        if (needing.Count == 0) return empty;

        var itemIds = needing.Select(item => item.Id).ToList();

        var forms = await _consents
            .ListAsync(
                form => form.TreatmentPlanItemId != null
                    && itemIds.Contains(form.TreatmentPlanItemId.Value),
                ct)
            .ConfigureAwait(false);

        // Signed is the only state that clears a procedure. Pending, refused, withdrawn and
        // expired all mean treatment must not start, and a check that counted any form as
        // consent would go quiet at exactly the moment it is meant to speak up.
        var signed = forms
            .Where(form => form.Status == ConsentStatus.Signed)
            .Select(form => form.TreatmentPlanItemId!.Value)
            .ToHashSet();

        return needing
            .Where(item => !signed.Contains(item.Id))
            .GroupBy(item => item.AppointmentId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
    }

    /// <summary>The procedures at a visit that need consent, each with the form covering it.</summary>
    private async Task<IReadOnlyList<VisitConsentRow>> ConsentRowsAsync(
        IReadOnlyList<TreatmentPlanItem> items, CancellationToken ct)
    {
        var rows = new List<VisitConsentRow>();
        var templates = await ActiveTemplatesAsync(ct).ConfigureAwait(false);

        foreach (var item in items)
        {
            // Work already in the patient's mouth is not waiting on a signature.
            if (item.Status == TreatmentItemStatus.Completed) continue;

            var code = await _codes
                .GetByIdAsync(item.ProcedureCodeId, ct)
                .ConfigureAwait(false);

            if (!NeedsConsent(templates, code?.Category)) continue;

            var form = await _consents
                .FindAsync(consent => consent.TreatmentPlanItemId == item.Id, ct)
                .ConfigureAwait(false);

            rows.Add(new VisitConsentRow(
                item.Id, form?.Id, ItemConsentTitle(item), form?.Status));
        }

        return rows;
    }

    /// <summary>Raises a pending form for each procedure that needs one and has none.</summary>
    private async Task<int> RaiseConsentForItemsAsync(
        TreatmentPlan plan, IReadOnlyList<TreatmentPlanItem> items, CancellationToken ct)
    {
        var raised = 0;
        var templates = await ActiveTemplatesAsync(ct).ConfigureAwait(false);

        foreach (var item in items)
        {
            if (item.Status == TreatmentItemStatus.Completed) continue;

            var code = await _codes
                .GetByIdAsync(item.ProcedureCodeId, ct)
                .ConfigureAwait(false);

            if (!NeedsConsent(templates, code?.Category)) continue;

            // Never a second form for the same procedure. Rebooking a visit runs through
            // here again, and a signed consent must not be joined by a fresh pending one
            // that makes the record look unconsented.
            var existing = await _consents
                .FindAsync(consent => consent.TreatmentPlanItemId == item.Id, ct)
                .ConfigureAwait(false);

            if (existing is not null) continue;

            await _consents
                .SaveAsync(
                    new ConsentForm
                    {
                        PatientId = plan.PatientId,
                        TreatmentPlanId = plan.Id,
                        TreatmentPlanItemId = item.Id,
                        Title = ItemConsentTitle(item),

                        // Copied onto the form, not read back through the template. What
                        // the patient signed is this wording; a template edited next year
                        // must not silently restate a consent already given.
                        Body = PickTemplate(templates, code?.Category)?.Body
                            ?? ConsentPolicy.Wording(code?.Category),
                        Status = ConsentStatus.Pending,
                    },
                    ct)
                .ConfigureAwait(false);

            raised++;
        }

        return raised;
    }

    /// <summary>
    /// The practice's consent wording, all of it, read once per operation.
    /// </summary>
    /// <remarks>
    /// A handful of rows, so the whole set is cheaper to hold than to query per procedure
    /// inside a loop. Read fresh each time rather than cached on the service: the service
    /// is scoped, and wording edited in Admin has to take effect on the next booking, not
    /// on the next deployment.
    /// </remarks>
    private async Task<IReadOnlyList<ConsentTemplate>> ActiveTemplatesAsync(
        CancellationToken ct) =>
        await _consentTemplates
            .ListAsync(template => template.IsActive, ct)
            .ConfigureAwait(false);

    /// <summary>The practice's wording for a schedule category, where they have set one.</summary>
    private static ConsentTemplate? PickTemplate(
        IReadOnlyList<ConsentTemplate> templates, string? category) =>
        category is { Length: > 0 }
            ? templates.FirstOrDefault(template =>
                string.Equals(template.Category, category, StringComparison.OrdinalIgnoreCase))
            : null;

    /// <summary>
    /// Whether a procedure in this category needs its own signed form.
    /// </summary>
    /// <remarks>
    /// The built-in list is a floor, not the whole rule. A practice that writes a template
    /// for a category adds a consent requirement; deleting every template cannot take one
    /// away, because the categories in <see cref="ConsentPolicy"/> still answer true. The
    /// alternative — letting the table alone decide — means an accidental delete quietly
    /// stops asking for consent on oral surgery, and nothing on any screen would say so.
    /// </remarks>
    private static bool NeedsConsent(
        IReadOnlyList<ConsentTemplate> templates, string? category) =>
        ConsentPolicy.NeedsConsent(category) || PickTemplate(templates, category) is not null;

    /// <summary>
    /// How a procedure names its consent — "Crown — tooth 46".
    /// </summary>
    /// <remarks>
    /// The schedule's description and the tooth, matching the titles already on the record.
    /// A consent titled by item number alone is unreadable to the person signing it, which
    /// defeats the point of asking them to read it.
    /// </remarks>
    private static string ItemConsentTitle(TreatmentPlanItem item) =>
        item.ToothNumber is { Length: > 0 } tooth
            ? $"{item.Description} — tooth {tooth}"
            : item.Description;

    public async Task UnlinkAppointmentAsync(
        Guid appointmentId, CancellationToken ct = default)
    {
        var booked = await _items
            .ListAsync(item => item.AppointmentId == appointmentId, ct)
            .ConfigureAwait(false);

        if (booked.Count == 0) return;

        foreach (var item in booked)
        {
            // Completed work stays completed and stays attributed to the visit it was
            // done at. Cancelling a later appointment does not un-do a filling.
            if (item.Status == TreatmentItemStatus.Completed) continue;

            item.AppointmentId = null;
            item.Status = TreatmentItemStatus.Planned;

            await _items.SaveAsync(item, ct).ConfigureAwait(false);
        }

        await RestoreStatusAsync(booked[0].TreatmentPlanId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Walks a plan back from <c>InProgress</c> once nothing of it is booked or delivered.
    /// </summary>
    /// <remarks>
    /// Not unconditional. A plan with one visit completed and the next one cancelled is
    /// still in progress, and demoting it to "accepted, not booked" would put finished
    /// treatment back on the worklist as work outstanding.
    /// </remarks>
    private async Task RestoreStatusAsync(Guid planId, CancellationToken ct)
    {
        var plan = await _plans.GetByIdAsync(planId, ct).ConfigureAwait(false);

        if (plan is null || plan.Status != TreatmentPlanStatus.InProgress) return;

        var items = await _items
            .ListAsync(item => item.TreatmentPlanId == planId, ct)
            .ConfigureAwait(false);

        var stillMoving = items.Any(item =>
            item.Status is TreatmentItemStatus.Scheduled or TreatmentItemStatus.Completed);

        if (stillMoving) return;

        plan.Status = TreatmentPlanStatus.Accepted;

        await _plans.SaveAsync(plan, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// What the diary block says for a booked visit.
    /// </summary>
    /// <remarks>
    /// The items rather than the plan title, because that is what the clinician needs to
    /// read off the day at a glance — "Crown — tooth 46" tells them what is happening;
    /// "Perio clean and crown on 46, visit 2" makes them open it to find out.
    /// </remarks>
    private static string VisitReason(string planTitle, IReadOnlyList<PlanDraftItem> items)
    {
        if (items.Count == 0) return planTitle;

        var named = string.Join(" · ", items.Take(2).Select(item => item.PatientLabel));

        return items.Count > 2 ? $"{named} +{items.Count - 2} more" : named;
    }

    // ---- writing the lines ------------------------------------------------

    /// <summary>
    /// Brings the stored lines into line with the draft: updates what is still there,
    /// adds what is new, soft-deletes what was removed.
    /// </summary>
    /// <remarks>
    /// Not delete-all-then-reinsert, which is the obvious way and the wrong one: it would
    /// break the <c>TreatmentPlanItemId</c> a chart entry holds, orphan the appointment a
    /// booked line points at, and give every line a new id on every save.
    /// </remarks>
    private async Task SaveLinesAsync(
        PlanDraft draft, Guid planId, CancellationToken ct)
    {
        var stored = await _items
            .ListAsync(item => item.TreatmentPlanId == planId, ct)
            .ConfigureAwait(false);

        var kept = draft.Items
            .Where(item => item.Id != Guid.Empty)
            .Select(item => item.Id)
            .ToHashSet();

        foreach (var gone in stored.Where(item => !kept.Contains(item.Id)))
        {
            await _items.DeleteAsync(gone.Id, ct).ConfigureAwait(false);
        }

        var byId = stored.ToDictionary(item => item.Id);
        var order = 0;

        foreach (var line in draft.Items
            .OrderBy(item => item.StageNumber)
            .ThenBy(item => item.DisplayOrder))
        {
            var entity = line.Id != Guid.Empty && byId.TryGetValue(line.Id, out var found)
                ? found
                : new TreatmentPlanItem { TreatmentPlanId = planId };

            entity.ProcedureCodeId = line.ProcedureCodeId;
            entity.ItemNumber = line.ItemNumber;
            entity.Description = line.Description;
            entity.ToothNumber = Blank(line.ToothNumber);
            entity.Fee = line.Fee;
            entity.StageNumber = Math.Clamp(line.StageNumber, 1, PlanDraft.MaxStages);
            entity.DisplayOrder = order++;

            var itemId = await _items.SaveAsync(entity, ct).ConfigureAwait(false);
            line.Id = itemId;

            await LinkChartEntryAsync(line, itemId, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Points the charted finding at the plan item that will deal with it.
    /// </summary>
    /// <remarks>
    /// So the same decay is not planned twice by two clinicians a fortnight apart — the
    /// odontogram can show that a finding is already spoken for. One-way on purpose: the
    /// link is cleared by nothing here, because a plan line being removed does not mean
    /// the decay went away.
    /// </remarks>
    private async Task LinkChartEntryAsync(
        PlanDraftItem line, Guid itemId, CancellationToken ct)
    {
        if (line.FromChartEntryId is not { } entryId) return;

        var entry = await _chart.GetByIdAsync(entryId, ct).ConfigureAwait(false);

        if (entry is null || entry.TreatmentPlanItemId == itemId) return;

        entry.TreatmentPlanItemId = itemId;

        await _chart.SaveAsync(entry, ct).ConfigureAwait(false);
    }

    private async Task RecordConsentAsync(
        TreatmentPlan plan,
        string name,
        string? relationship,
        string? signatureImage,
        CancellationToken ct)
    {
        // The plan's own consent, which is the one with no item on it. A plan carries
        // several — the design's panel lists consent per procedure, and the seeded record
        // has a signed one for the crown and a pending one for the root canal, each tied
        // to its item. Matching on the plan alone took whichever came back first and
        // rewrote it: a consent signed three weeks ago for one procedure had its title,
        // its wording and its signature timestamp replaced by this acceptance. Destroying
        // signed consent is the worst thing this service could do, so the scope is
        // explicit on both the read and the write.
        var existing = await _consents
            .FindAsync(
                form => form.TreatmentPlanId == plan.Id && form.TreatmentPlanItemId == null,
                ct)
            .ConfigureAwait(false);

        var form = existing ?? new ConsentForm
        {
            PatientId = plan.PatientId,
            TreatmentPlanId = plan.Id,
            TreatmentPlanItemId = null,
        };

        form.Title = plan.Title;

        // The wording as at acceptance, stored rather than referenced. What the patient
        // agreed to is this text and this figure; reading it back through the plan would
        // show them whatever the plan says next year.
        form.Body = ConsentBody(plan);
        form.Status = ConsentStatus.Signed;
        form.SignedUtc = _clock.UtcNow;
        form.SignedByName = name;
        form.SignedByRelationship = Blank(relationship);
        form.WitnessedByProviderId = _session.ProviderId ?? plan.ProviderId;
        form.ExpiresOn = plan.EstimateValidUntil;

        // The signature drawn on the tablet, where one was. Held inline on the consent
        // rather than as a document row: it is small, and it must never become separable
        // from the figures and the wording it was given against.
        form.SignatureImage = Blank(signatureImage);
        await _consents.SaveAsync(form, ct).ConfigureAwait(false);
    }

    private static string ConsentBody(TreatmentPlan plan)
    {
        var gap = plan.QuotedTotal - plan.EstimatedBenefit;

        var cost = plan.EstimatedBenefit > 0m
            ? $"Total {MolargoFormat.Money(plan.QuotedTotal)}, fund estimate "
                + $"{MolargoFormat.Money(plan.EstimatedBenefit)}, estimated gap "
                + $"{MolargoFormat.Money(gap)}."
            : $"Total {MolargoFormat.Money(plan.QuotedTotal)}. No fund benefit has been "
                + "estimated, so the whole fee may be payable.";

        return string.IsNullOrWhiteSpace(plan.Rationale)
            ? cost
            : $"{plan.Rationale}\n\n{cost}";
    }

    // ---- proposing from the chart -----------------------------------------

    /// <summary>
    /// Turns the charted findings that need treating into proposed lines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A suggestion, not a decision — every line is editable and removable before the plan
    /// is saved. The mapping is deliberately small: the findings where the treatment is
    /// not seriously in question. A missing tooth is not here, because replacing one is a
    /// choice between a bridge, an implant and leaving it, and that choice is the whole
    /// point of presenting alternatives.
    /// </para>
    /// <para>
    /// Findings already spoken for by a plan item are skipped, so opening the builder a
    /// second time does not propose the same filling again.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<PlanDraftItem>> ProposeFromChartAsync(
        Guid patientId, CancellationToken ct)
    {
        var charted = await _chart
            .ListAsync(entry => entry.PatientId == patientId, ct)
            .ConfigureAwait(false);

        var current = charted
            .Where(entry => entry.IsCurrent && entry.TreatmentPlanItemId is null)
            .OrderBy(entry => entry.ToothNumber, StringComparer.Ordinal)
            .ToList();

        if (current.Count == 0) return [];

        var catalogue = await _billing
            .GetCatalogueAsync(_session.LocationId, includeWithdrawn: false, ct)
            .ConfigureAwait(false);

        var byNumber = catalogue.ToDictionary(
            item => item.ItemNumber, StringComparer.OrdinalIgnoreCase);

        var proposed = new List<PlanDraftItem>();
        var wholeMouthUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in current)
        {
            if (SuggestedItemNumber(entry.Condition) is not { } number) continue;
            if (!byNumber.TryGetValue(number, out var item)) continue;

            // A whole-mouth item is charged once however many teeth show the finding.
            // Without this a patient with periodontal notes on six teeth was proposed six
            // deep cleans, which is both wrong and alarming to read.
            if (!item.Code.IsPerTooth && !wholeMouthUsed.Add(number)) continue;

            proposed.Add(new PlanDraftItem
            {
                ProcedureCodeId = item.Code.Id,
                ItemNumber = item.ItemNumber,
                Description = item.Description,
                FriendlyName = item.Code.PatientFriendlyName,
                ToothNumber = item.Code.IsPerTooth ? entry.ToothNumber : null,
                Fee = item.Fee,
                DurationMinutes = item.Code.TypicalDurationMinutes ?? 0,
                StageNumber = 1,
                DisplayOrder = proposed.Count,
                FromChartEntryId = entry.Id,
            });
        }

        return proposed;
    }

    /// <summary>
    /// The item number a charted finding suggests, or null where treatment is a judgement
    /// the chart cannot make.
    /// </summary>
    /// <remarks>
    /// Item numbers rather than ids, so the map reads as the schedule a dentist would
    /// recognise and survives the catalogue being reseeded. An unknown number simply
    /// proposes nothing.
    /// </remarks>
    private static string? SuggestedItemNumber(ToothCondition condition) => condition switch
    {
        ToothCondition.Caries => "531",
        ToothCondition.Fractured => "613",
        ToothCondition.Impacted => "311",
        ToothCondition.Periodontal => "114",

        // Everything else, deliberately. Sound and Restoration are findings, not needs;
        // Watch says in its own definition that it is for review rather than treatment;
        // Missing, Crown, Bridge, Veneer, Implant and RootCanalTreated are either existing
        // work or a decision with more than one defensible answer.
        _ => null,
    };

    /// <summary>A title from the lines, so an untitled plan is still recognisable.</summary>
    /// <remarks>
    /// Built from the leading line's own label — which already carries its own tooth — and
    /// a count of the rest. It used to pair that label with every tooth in the plan, which
    /// produced "Deep clean — tooth 46" for a whole-mouth clean sitting beside a crown on
    /// 46: a title that named a procedure and a tooth that had nothing to do with each
    /// other. A plan title is read back months later by someone deciding whether to open
    /// it, and one that is quietly wrong is worse than one that is vague.
    /// </remarks>
    private static string SuggestTitle(IReadOnlyList<PlanDraftItem> items)
    {
        if (items.Count == 0) return string.Empty;

        var lead = items[0].PatientLabel;

        return items.Count == 1 ? lead : $"{lead} and {items.Count - 1} more";
    }

    /// <summary>
    /// The clinician a new plan is attributed to.
    /// </summary>
    /// <remarks>
    /// The signed-in user where they are clinical, because the overwhelmingly common case
    /// is a dentist planning in the surgery. Reception gets the patient's usual clinician
    /// instead, which is who the plan would go out under anyway — and <c>Guid.Empty</c>
    /// where neither holds, so the builder asks rather than guessing wrong.
    /// </remarks>
    private async Task<Guid> DefaultProviderAsync(PatientEntity patient, CancellationToken ct)
    {
        if (_session.ProviderId is { } signedIn)
        {
            var me = await _providers.GetByIdAsync(signedIn, ct).ConfigureAwait(false);

            if (me is not null && ProviderRoles.IsClinical(me.Role)) return me.Id;
        }

        if (patient.PreferredProviderId is { } preferred)
        {
            var usual = await _providers.GetByIdAsync(preferred, ct).ConfigureAwait(false);

            if (usual is not null && usual.IsActive && ProviderRoles.IsClinical(usual.Role))
            {
                return usual.Id;
            }
        }

        return Guid.Empty;
    }

    private static string RoleLabel(ProviderRole role) => role switch
    {
        ProviderRole.Dentist => "Dentist",
        ProviderRole.Hygienist => "Hygienist",
        ProviderRole.OralHealthTherapist => "Therapist",
        ProviderRole.DentalTherapist => "Therapist",
        ProviderRole.Prosthetist => "Prosthetist",
        ProviderRole.Specialist => "Specialist",
        _ => role.ToString(),
    };

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
