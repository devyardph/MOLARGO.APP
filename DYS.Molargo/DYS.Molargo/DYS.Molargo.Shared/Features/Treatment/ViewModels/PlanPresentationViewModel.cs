using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Components;
using DYS.Molargo.Shared.Features.Treatment.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Treatment.ViewModels;

/// <summary>
/// The design's chairside presentation: the plan as the patient is shown it, the
/// alternatives beside it, and the acceptance.
/// </summary>
/// <remarks>
/// <para>
/// Built for a tablet held in front of a patient, which is why the figures are large and
/// the buttons are not small. The one thing it must never do is show a number the practice
/// cannot stand behind — so where no fund benefit has been estimated it says so, rather
/// than printing a gap equal to the full fee and letting the patient believe their fund
/// pays nothing.
/// </para>
/// <para>
/// Acceptance is typed, not drawn. The design has a signature pad taking a finger or a
/// stylus; that needs a canvas and pointer events through JS interop, and this app uses
/// none anywhere. A typed name with the signer's relationship, a witness and a timestamp is
/// a real consent record — a signature box that captured nothing would not be.
/// </para>
/// </remarks>
public sealed class PlanPresentationViewModel : BaseViewModel<Guid>
{
    private readonly ITreatmentPlanService _plans;
    private readonly IAppNavigator _navigator;

    private Guid _planId;
    private PlanPresentation? _presentation;
    private bool _notFound;
    private bool _isAccepting;
    private bool _isDeclining;
    private string? _signedByName;
    private string? _signatureImage;
    private string _relationship = SelfRelationship;
    private string? _declineReason;
    private string? _refusal;
    private string? _outcome;

    // Withdrawing, which is the way out for a plan that has been accepted. Inline here
    // rather than on the record's list: this screen already knows which visits are booked,
    // and that is the thing somebody has to be warned about before they commit.
    private bool _isWithdrawing;
    private string? _withdrawReason;

    /// <summary>The default, and by far the commonest: the patient agreeing for themselves.</summary>
    public const string SelfRelationship = "Self";

    /// <summary>
    /// Who else signs, where it is not the patient.
    /// </summary>
    /// <remarks>
    /// A short list rather than a free-text box. Consent for a child is given by a parent
    /// or guardian and the record has to say which; "Carer" covers an adult who cannot
    /// consent for themselves. Anything rarer than these is a conversation with a
    /// practice manager, not a dropdown entry.
    /// </remarks>
    public static readonly string[] Relationships =
        [SelfRelationship, "Parent", "Guardian", "Carer"];

    public PlanPresentationViewModel(ITreatmentPlanService plans, IAppNavigator navigator)
    {
        _plans = plans;
        _navigator = navigator;

        // Built once, in the constructor — never rebuilt per render.
        BackCommand = new MvxCommand(BackToRecord);
        OpenOptionCommand = new MvxCommand<Guid>(OpenOption);
        StartAcceptCommand = new MvxCommand(() => SetAccepting(true));
        CancelAcceptCommand = new MvxCommand(() => SetAccepting(false));
        ConfirmAcceptCommand = new MvxAsyncCommand(ConfirmAcceptAsync);
        StartDeclineCommand = new MvxCommand(() => SetDeclining(true));
        CancelDeclineCommand = new MvxCommand(() => SetDeclining(false));
        ConfirmDeclineCommand = new MvxAsyncCommand(ConfirmDeclineAsync);
        SelectRelationshipCommand = new MvxCommand<string>(SelectRelationship);
        EditPlanCommand = new MvxCommand(EditPlan);
        BookVisitCommand = new MvxCommand<int>(BookVisit);
        OpenVisitCommand = new MvxCommand<int>(OpenVisit);
        StartWithdrawCommand = new MvxCommand(() => SetWithdrawing(true));
        CancelWithdrawCommand = new MvxCommand(() => SetWithdrawing(false));
        ConfirmWithdrawCommand = new MvxAsyncCommand(ConfirmWithdrawAsync);
    }

    public IMvxCommand BackCommand { get; }

    /// <summary>Switches the presentation to one of the alternatives.</summary>
    public IMvxCommand<Guid> OpenOptionCommand { get; }

    public IMvxCommand StartAcceptCommand { get; }

    public IMvxCommand CancelAcceptCommand { get; }

    public IMvxAsyncCommand ConfirmAcceptCommand { get; }

    public IMvxCommand StartDeclineCommand { get; }

    public IMvxCommand CancelDeclineCommand { get; }

    public IMvxAsyncCommand ConfirmDeclineCommand { get; }

    public IMvxCommand<string> SelectRelationshipCommand { get; }

    /// <summary>Back to the builder, for a plan that has not been presented yet.</summary>
    public IMvxCommand EditPlanCommand { get; }

    /// <summary>
    /// Opens the booking form for one visit, already sized and staffed from the plan.
    /// </summary>
    /// <remarks>
    /// One visit at a time, not the course. Booking three visits as a series needs a
    /// course an appointment can belong to, which does not exist — and three appointments
    /// that merely happen to share a plan are not a series, whatever the button says.
    /// </remarks>
    public IMvxCommand<int> BookVisitCommand { get; }

    /// <summary>Opens the appointment a visit is already booked into.</summary>
    public IMvxCommand<int> OpenVisitCommand { get; }

    /// <summary>
    /// Opens the confirm for withdrawing this plan.
    /// </summary>
    /// <remarks>
    /// The only way out of an accepted plan. Declining is refused once the patient has
    /// agreed — they did agree, and rewriting that to "they said no" would falsify the
    /// record. Withdrawing says the practice pulled it, which is what happened.
    /// </remarks>
    public IMvxCommand StartWithdrawCommand { get; }

    public IMvxCommand CancelWithdrawCommand { get; }

    public IMvxAsyncCommand ConfirmWithdrawCommand { get; }

    public override void Prepare(Guid parameter) => _planId = parameter;

    public override Task Initialize() => LoadAsync();

    // ---- what the view reads ---------------------------------------------

    public bool NotFound => _notFound;

    public PlanPresentation? Presentation => _presentation;

    public PlanDraft? Plan => _presentation?.Plan;

    public string PatientName => _presentation?.PatientName ?? string.Empty;

    public string ProviderName => _presentation?.ProviderName ?? string.Empty;

    public string PlanTitle => Plan?.Title ?? string.Empty;

    public string? Rationale => Plan?.Rationale;

    public IReadOnlyList<PlanOption> Options => _presentation?.Alternatives ?? [];

    /// <summary>Only worth a row of cards where there is actually a choice.</summary>
    public bool HasChoice => Options.Count > 1;

    public bool IsSelected(Guid planId) => planId == _planId;

    public IReadOnlyList<int> Visits => Plan?.UsedStages ?? [];

    /// <summary>Each visit with its booking state, for the accepted panel's buttons.</summary>
    public IReadOnlyList<PlanVisitBooking> VisitBookings => _presentation?.Visits ?? [];

    public PlanVisitBooking? VisitBooking(int stage) =>
        VisitBookings.FirstOrDefault(visit => visit.Stage == stage);

    /// <summary>
    /// Whether the visits can be booked yet.
    /// </summary>
    /// <remarks>
    /// Only once accepted. Booking treatment a patient has not agreed to takes a chair
    /// from somebody who has, and the plan would then be part-booked while still reading
    /// as awaiting a decision.
    /// </remarks>
    public bool CanBookVisits => IsAccepted;

    /// <summary>How much of the plan is on the books, for the panel's caption.</summary>
    public string BookingProgress
    {
        get
        {
            var total = VisitBookings.Count;

            if (total == 0) return string.Empty;

            // Counted as settled rather than booked: a delivered visit needs no chair, and
            // "2 of 3 booked" on a plan whose third visit was done last year reads as a
            // job half finished when it is finished.
            var settled = VisitBookings.Count(visit => !visit.NeedsBooking);

            return settled == total
                ? "Every visit is booked."
                : $"{settled} of {total} visits booked.";
        }
    }

    public IReadOnlyList<PlanDraftItem> VisitItems(int stage) =>
        Plan?.Stage(stage) ?? [];

    public int VisitMinutes(int stage) => Plan?.StageMinutes(stage) ?? 0;

    public decimal VisitTotal(int stage) => Plan?.StageTotal(stage) ?? 0m;

    public decimal Total => Plan?.Total ?? 0m;

    public decimal Benefit => Plan?.EstimatedBenefit ?? 0m;

    public decimal Gap => Plan?.Gap ?? 0m;

    /// <summary>
    /// Whether a fund benefit has been estimated at all. See the remarks on the class.
    /// </summary>
    public bool HasBenefitEstimate => Plan?.HasBenefitEstimate ?? false;

    public bool AwaitsDecision => _presentation?.AwaitsDecision ?? false;

    public bool IsAccepted => _presentation?.IsAccepted ?? false;

    public bool IsDeclined => _presentation?.IsDeclined ?? false;

    public bool IsWithdrawn => Plan?.Status == TreatmentPlanStatus.Void;

    /// <summary>The headline over the outcome notice, once a decision has been recorded.</summary>
    /// <remarks>
    /// Three outcomes, not two. It used to pick between "Accepted." and "Declined." on a
    /// bool, so a withdrawn plan announced itself as declined — which says the patient
    /// refused it when in fact the practice pulled it.
    /// </remarks>
    public string OutcomeHeadline =>
        IsAccepted ? "Accepted." : IsWithdrawn ? "Withdrawn." : "Declined.";

    public bool CanEdit => Plan?.Status == TreatmentPlanStatus.Draft;

    public bool IsAccepting => _isAccepting;

    public bool IsDeclining => _isDeclining;

    public bool IsWithdrawing => _isWithdrawing;

    /// <summary>Whether this plan can still be withdrawn.</summary>
    /// <remarks>
    /// Anything but a completed plan. A plan whose work was delivered and charged cannot
    /// be un-made — withdrawing it would contradict the invoice and the notes.
    /// </remarks>
    public bool CanWithdraw =>
        Plan is { } plan
        && plan.Status is not (TreatmentPlanStatus.Completed or TreatmentPlanStatus.Void);

    /// <summary>Visits still holding a chair, so the confirm can warn before it commits.</summary>
    public IReadOnlyList<PlanVisitBooking> BookedVisits =>
        VisitBookings.Where(visit => visit.IsBooked).ToList();

    public string? WithdrawReason
    {
        get => _withdrawReason;
        set => SetProperty(ref _withdrawReason, value);
    }

    public string? SignedByName
    {
        get => _signedByName;
        set => SetProperty(ref _signedByName, value);
    }

    /// <summary>The signature drawn on the tablet, as a PNG data URI.</summary>
    public string? SignatureImage
    {
        get => _signatureImage;
        set
        {
            if (_signatureImage == value) return;

            _signatureImage = value;

            RaisePropertyChanged(nameof(SignatureImage));
            RaisePropertyChanged(nameof(IsSigned));
        }
    }

    /// <remarks>
    /// Both halves. The drawing is what a patient recognises as agreeing; the name is what
    /// identifies who agreed, which for a parent accepting on a child's behalf is the
    /// whole point of asking.
    /// </remarks>
    public bool IsSigned =>
        _signedByName is { Length: > 0 } && _signatureImage is { Length: > 0 };

    /// <summary>Wipes the drawing, for a tablet handed to the wrong person.</summary>
    public void ClearSignature() => SignatureImage = null;

    public string Relationship => _relationship;

    public bool IsRelationshipSelected(string value) =>
        string.Equals(_relationship, value, StringComparison.OrdinalIgnoreCase);

    public string? DeclineReason
    {
        get => _declineReason;
        set => SetProperty(ref _declineReason, value);
    }

    /// <summary>A refusal from the service — why the decision could not be recorded.</summary>
    public string? Refusal => _refusal;

    /// <summary>The confirmation line after a decision has been recorded.</summary>
    public string? Outcome => _outcome;

    /// <summary>What the consent record says, once there is one.</summary>
    public string? ConsentLine
    {
        get
        {
            if (_presentation?.Consent is not { Status: ConsentStatus.Signed } consent)
            {
                return null;
            }

            var who = consent.SignedByName ?? "—";

            var capacity =
                consent.SignedByRelationship is { Length: > 0 } relationship
                && !string.Equals(relationship, SelfRelationship, StringComparison.OrdinalIgnoreCase)
                    ? $" ({relationship})"
                    : string.Empty;

            return $"Accepted by {who}{capacity} · "
                + $"{MolargoFormat.DateTime(consent.SignedUtc)} · witnessed by {ProviderName}";
        }
    }

    /// <summary>
    /// The design's reminder caption, saying what this screen does not do.
    /// </summary>
    /// <remarks>
    /// The design promises accepted visits book as a series. A course an appointment can
    /// belong to does not exist, so the visits have to be booked one at a time — and the
    /// screen says that rather than implying a series that nothing would create.
    /// </remarks>
    public const string BookingNote =
        "Accepting records the decision. The visits are booked individually from the "
            + "diary — booking a course as one series needs the multi-visit series that "
            + "is not built.";

    // ---- loading ----------------------------------------------------------

    private Task LoadAsync() => RunGuardedAsync(ReloadAsync);

    /// <summary>
    /// The load itself, without the busy guard around it.
    /// </summary>
    /// <remarks>
    /// Split out because <c>RunGuardedAsync</c> refuses to nest — it returns immediately
    /// while already busy — so calling <see cref="LoadAsync"/> from inside a guarded
    /// command did nothing at all, silently. Accepting a plan wrote the decision and the
    /// consent, and then the screen carried on showing the Accept button as though
    /// nothing had happened: the worst possible version of that bug, on the one screen in
    /// the app that is turned around to face a patient.
    /// </remarks>
    private async Task ReloadAsync()
    {
        _notFound = false;
        _refusal = null;

        _presentation = await _plans.GetPresentationAsync(_planId).ConfigureAwait(false);

        if (_presentation is null)
        {
            _notFound = true;
            RaiseAll();
            return;
        }

        // Pre-filled with the patient's own name, which is who signs almost every time.
        // Still editable, and still typed over for a parent — a name that arrived by
        // default and one that somebody confirmed look the same in the record, so the
        // relationship beside it is what carries the difference.
        _signedByName ??= _presentation.PatientName;

        RaiseAll();
    }

    // ---- deciding ---------------------------------------------------------

    private void SetAccepting(bool value)
    {
        _isAccepting = value;

        if (value) _isDeclining = false;

        _refusal = null;

        RaisePropertyChanged(nameof(IsAccepting));
        RaisePropertyChanged(nameof(IsDeclining));
        RaisePropertyChanged(nameof(Refusal));
    }

    private void SetDeclining(bool value)
    {
        _isDeclining = value;

        if (value) _isAccepting = false;

        if (!value) _declineReason = null;

        _refusal = null;

        RaisePropertyChanged(nameof(IsDeclining));
        RaisePropertyChanged(nameof(IsAccepting));
        RaisePropertyChanged(nameof(DeclineReason));
        RaisePropertyChanged(nameof(Refusal));
    }

    private void SelectRelationship(string? value)
    {
        // MvxCommand<string> hands the parameter through as nullable, so the guard is not
        // decoration: a null would blank the relationship on a record that has to say who
        // signed and in what capacity.
        if (string.IsNullOrWhiteSpace(value)) return;

        _relationship = value;

        RaisePropertyChanged(nameof(Relationship));
    }

    private Task ConfirmAcceptAsync() => RunGuardedAsync(async () =>
    {
        // Refused here rather than by greying the button. A primary button that looks
        // clickable and does nothing teaches people the tablet is broken; pressing it and
        // being told what is missing is the same answer, delivered.
        if (string.IsNullOrWhiteSpace(_signatureImage))
        {
            _refusal = "Sign in the box above before recording acceptance.";
            RaiseAll();
            return;
        }

        var refusal = await _plans
            .AcceptAsync(_planId, _signedByName, _relationship, _signatureImage)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            _refusal = refusal;
            RaiseAll();
            return;
        }

        _isAccepting = false;
        // The headline already says "Accepted." — the notice renders the two side by
        // side, so repeating it here read as "Accepted. Accepted. The plan is…".
        _outcome = "The plan is on the record and the consent is signed.";

        // Re-read rather than patched in place. The service sets the decision time, may
        // have presented the plan on the way through, and writes the consent row this
        // screen then prints — reconstructing all of that here would be a second copy of
        // the same rules, drifting from the first.
        await ReloadAsync().ConfigureAwait(false);
    });

    private Task ConfirmDeclineAsync() => RunGuardedAsync(async () =>
    {
        var refusal = await _plans
            .DeclineAsync(_planId, _declineReason)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            _refusal = refusal;
            RaiseAll();
            return;
        }

        _isDeclining = false;
        _outcome = "The plan stays on the record with the reason.";

        await ReloadAsync().ConfigureAwait(false);
    });

    private void SetWithdrawing(bool value)
    {
        _isWithdrawing = value;

        if (value)
        {
            _isAccepting = false;
            _isDeclining = false;
        }
        else
        {
            _withdrawReason = null;
        }

        _refusal = null;

        RaiseAll();
    }

    private Task ConfirmWithdrawAsync() => RunGuardedAsync(async () =>
    {
        var refusal = await _plans
            .VoidAsync(_planId, _withdrawReason)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            _refusal = refusal;
            RaiseAll();
            return;
        }

        _isWithdrawing = false;
        _outcome = "The plan stays on the record, and anything already delivered under "
            + "it is untouched.";

        await ReloadAsync().ConfigureAwait(false);
    });

    private void OpenOption(Guid planId)
    {
        if (planId == _planId) return;

        _navigator.ToPlanPresentation(planId);
    }

    private void EditPlan()
    {
        if (Plan is { } plan) _navigator.ToPlanBuilder(plan.PatientId, plan.Id);
    }

    private void BookVisit(int stage) =>
        _navigator.ToNewAppointment(planId: _planId, visit: stage);

    private void OpenVisit(int stage)
    {
        if (VisitBooking(stage)?.AppointmentId is { } appointmentId)
        {
            _navigator.ToAppointment(appointmentId);
        }
    }

    private void BackToRecord()
    {
        if (Plan is { } plan) _navigator.ToPatientRecord(plan.PatientId);
    }

    /// <summary>Everything the screen reads, listed rather than raised in bulk.</summary>
    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(NotFound), nameof(Presentation), nameof(Plan), nameof(PatientName),
            nameof(ProviderName), nameof(PlanTitle), nameof(Rationale), nameof(Options),
            nameof(HasChoice), nameof(Visits), nameof(Total), nameof(Benefit),
            nameof(Gap), nameof(HasBenefitEstimate), nameof(AwaitsDecision),
            nameof(IsAccepted), nameof(IsDeclined), nameof(IsWithdrawn),
            nameof(OutcomeHeadline), nameof(CanEdit), nameof(IsAccepting),
            nameof(IsDeclining), nameof(SignedByName), nameof(SignatureImage),
            nameof(IsSigned), nameof(Relationship),
            nameof(DeclineReason), nameof(Refusal), nameof(Outcome), nameof(ConsentLine),
            nameof(VisitBookings), nameof(CanBookVisits), nameof(BookingProgress),
            nameof(IsWithdrawing), nameof(CanWithdraw), nameof(BookedVisits),
            nameof(WithdrawReason),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
