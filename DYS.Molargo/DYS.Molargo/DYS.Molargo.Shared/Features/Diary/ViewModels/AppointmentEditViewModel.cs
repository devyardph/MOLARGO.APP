using System.Globalization;
using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Dtos;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Features.Diary.Services;
using DYS.Molargo.Shared.Features.Treatment.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Diary.ViewModels;

/// <summary>Where the booking form is in the save sequence.</summary>
public enum AppointmentEditStage
{
    /// <summary>Being filled in.</summary>
    Editing = 0,

    /// <summary>Written.</summary>
    Saved = 1,

    /// <summary>Cancelled rather than saved.</summary>
    Cancelled = 2,
}

/// <summary>
/// Booking a new appointment, or editing an existing one — the prototype's single form for
/// both, with its pre-booking checks panel.
/// </summary>
/// <remarks>
/// One view model for both, taking the appointment's id or <see cref="Guid.Empty"/> for a
/// new booking. The form is the same form; splitting it would mean two of every field, two
/// validators and two save paths that have to stay in step.
/// </remarks>
public sealed class AppointmentEditViewModel : BaseViewModel<Guid>
{
    /// <summary>
    /// The durations the design offers as a segmented control.
    /// </summary>
    /// <remarks>
    /// Fixed buttons rather than a free number field, which is what the design shows and
    /// what the front desk wants: these are the lengths the practice actually books, and a
    /// typed number invites the 37-minute appointment that makes a day impossible to plan.
    /// An appointment type's own default is applied on top when a type is picked.
    /// </remarks>
    public static readonly int[] StandardDurations = [15, 20, 30, 45, 60, 90];

    /// <summary>
    /// The durations this booking offers — the standard set, plus the one it is already
    /// on where that is something else.
    /// </summary>
    /// <remarks>
    /// A treatment plan's visit is as long as its items need, and those do not land on the
    /// practice's six round numbers — a clean and a crown together come to 135 minutes.
    /// Without this the segmented control showed nothing selected for exactly the bookings
    /// that arrived with a length already worked out, which reads as a broken form and
    /// invites somebody to "fix" it by picking 90.
    /// </remarks>
    public IReadOnlyList<int> Durations =>
        StandardDurations.Contains(_form.DurationMinutes)
            ? StandardDurations
            : StandardDurations.Append(_form.DurationMinutes).Order().ToList();

    private readonly IAppointmentService _appointments;
    private readonly ISessionService _session;
    private readonly IAppNavigator _navigator;
    private readonly IClock _clock;

    /// <summary>Only to read the plan visit a booking was started from.</summary>
    private readonly ITreatmentPlanService _plans;

    private Guid _id;

    /// <summary>
    /// Whether this screen was opened to create rather than to edit.
    /// </summary>
    /// <remarks>
    /// Held separately from <c>_form.IsNew</c>, which stops being true the moment a save
    /// assigns the new id — so the heading retitled itself "Edit appointment" and the
    /// confirmation said "Saved" about a booking that had just been created.
    /// </remarks>
    private bool _openedAsNew = true;

    private AppointmentForm _form = new();
    private AppointmentOptions _options = new();
    private PreBookingChecks _checks = new();
    private AppointmentEditStage _stage = AppointmentEditStage.Editing;
    private IReadOnlyDictionary<string, string> _errors = new Dictionary<string, string>();

    private string _patientSearch = string.Empty;
    private IReadOnlyList<PatientListItemDto> _searchResults = [];
    private bool _notFound;
    private bool _isCancelling;
    private string? _cancelReason;

    /// <summary>Prefill from the diary slot that was clicked, before Initialize runs.</summary>
    private DateOnly? _initialDate;
    private TimeOnly? _initialTime;
    private Guid? _initialOperatoryId;

    /// <summary>The patient whose record the booking was started from, if any.</summary>
    private Guid? _initialPatientId;

    /// <summary>The treatment-plan visit this booking delivers, where it came from one.</summary>
    private Guid? _initialPlanId;
    private int? _initialStage;

    /// <summary>The plan visit this booking delivers, once read back. Null for a plain booking.</summary>
    private PlanVisitBooking? _planVisit;

    /// <summary>
    /// What this visit needs consent for, and what has been signed.
    /// </summary>
    /// <remarks>
    /// Only loaded for an appointment that exists. A booking still being typed has no id
    /// for its items to hang off, and nothing to consent to until it is saved.
    /// </remarks>
    private VisitConsent? _visitConsent;

    private string? _consentRefusal;

    public AppointmentEditViewModel(
        IAppointmentService appointments,
        ISessionService session,
        IAppNavigator navigator,
        IClock clock,
        ITreatmentPlanService plans)
    {
        _appointments = appointments;
        _session = session;
        _navigator = navigator;
        _clock = clock;
        _plans = plans;

        // Built once, in the constructor — never rebuilt per render.
        SaveCommand = new MvxAsyncCommand(SaveAsync);
        BackCommand = new MvxCommand(() => _navigator.ToDiary());
        SelectTypeCommand = new MvxAsyncCommand<Guid>(SelectTypeAsync);
        SelectProviderCommand = new MvxAsyncCommand<Guid>(SelectProviderAsync);
        SelectChairCommand = new MvxAsyncCommand<Guid>(SelectChairAsync);
        SelectDurationCommand = new MvxAsyncCommand<int>(SelectDurationAsync);
        SearchPatientsCommand = new MvxAsyncCommand(SearchPatientsAsync);
        ChoosePatientCommand = new MvxAsyncCommand<Guid>(ChoosePatientAsync);
        ClearPatientCommand = new MvxCommand(ClearPatient);
        SlotChangedCommand = new MvxAsyncCommand(RefreshChecksAsync);
        RaiseVisitConsentCommand = new MvxAsyncCommand(RaiseVisitConsentAsync);
        TakeConsentCommand = new MvxCommand(TakeConsent);
        StartCancelCommand = new MvxCommand(() => SetCancelling(true));
        AbandonCancelCommand = new MvxCommand(() => SetCancelling(false));
        ConfirmCancelCommand = new MvxAsyncCommand<bool>(ConfirmCancelAsync);
        OpenPatientCommand = new MvxCommand(OpenPatient);
    }

    /// <summary>The site's trading pattern, for the hints the form prints.</summary>
    public PracticeHours Hours => _session.Hours;

    public IMvxAsyncCommand SaveCommand { get; }

    public IMvxCommand BackCommand { get; }

    public IMvxAsyncCommand<Guid> SelectTypeCommand { get; }

    public IMvxAsyncCommand<Guid> SelectProviderCommand { get; }

    /// <summary>
    /// Chooses the chair, which is a separate decision from the clinician.
    /// </summary>
    /// <remarks>
    /// Both re-run the pre-booking checks, because both change what the slot clashes with:
    /// the chair decides which existing bookings occupy it, and the provider decides
    /// whether they are already busy elsewhere. The second half of that was a description
    /// of an intention rather than of the code for a while — the clash query filtered on
    /// the chair alone — which is worth remembering the next time a comment and a method
    /// disagree.
    /// </remarks>
    public IMvxAsyncCommand<Guid> SelectChairCommand { get; }

    public IMvxAsyncCommand<int> SelectDurationCommand { get; }

    public IMvxAsyncCommand SearchPatientsCommand { get; }

    public IMvxAsyncCommand<Guid> ChoosePatientCommand { get; }

    public IMvxCommand ClearPatientCommand { get; }

    /// <summary>Re-runs the checks after the date, time or duration changes.</summary>
    public IMvxAsyncCommand SlotChangedCommand { get; }

    public IMvxCommand StartCancelCommand { get; }

    public IMvxCommand AbandonCancelCommand { get; }

    /// <summary>True for a no-show, false for a cancellation with notice.</summary>
    public IMvxAsyncCommand<bool> ConfirmCancelCommand { get; }

    public IMvxCommand OpenPatientCommand { get; }

    public override void Prepare(Guid parameter)
    {
        _id = parameter;
        _openedAsNew = parameter == Guid.Empty;
    }

    public override Task Initialize() => LoadAsync();

    /// <summary>
    /// Seeds a new booking from the diary slot that was clicked.
    /// </summary>
    /// <remarks>
    /// Called from the view's <c>OnInitialized</c>, before <c>Initialize</c>, because the
    /// slot arrives as a query string that only the view can read. Applying it after the
    /// load would overwrite an existing appointment's own date and chair.
    /// </remarks>
    public void SetInitialSlot(DateOnly? date, TimeOnly? time, Guid? operatoryId)
    {
        _initialDate = date;
        _initialTime = time;
        _initialOperatoryId = operatoryId;
    }

    /// <summary>
    /// Names the patient a booking was started for, from their record.
    /// </summary>
    /// <remarks>
    /// Called from the view's <c>OnInitialized</c> alongside the slot, and for the same
    /// reason: the id arrives as a query string only the view can read, and applying it
    /// after the load would overwrite the patient an existing appointment is already for.
    /// </remarks>
    public void SetInitialPatient(Guid? patientId) => _initialPatientId = patientId;

    /// <summary>
    /// Names the treatment-plan visit this booking is delivering.
    /// </summary>
    /// <remarks>
    /// Only the plan id and the visit number arrive; everything shown — the patient, the
    /// clinician, the length, what the visit is for — is read back from the plan. Same
    /// reason as the patient id: a query string is editable, and a form that booked
    /// whatever it was handed would put one patient's treatment under another's name.
    /// </remarks>
    public void SetInitialPlanVisit(Guid? planId, int? stage)
    {
        _initialPlanId = planId;
        _initialStage = stage;
    }

    public bool NotFound => _notFound;

    public AppointmentForm Form => _form;

    public AppointmentOptions Options => _options;

    public PreBookingChecks Checks => _checks;

    public AppointmentEditStage Stage => _stage;

    public bool IsEditing => _stage == AppointmentEditStage.Editing;

    public bool IsExisting => !_openedAsNew;

    public string Title => _openedAsNew ? "New appointment" : "Edit appointment";

    /// <summary>The design's save-button label, which names what will happen.</summary>
    public string SaveLabel => _openedAsNew ? "Book appointment" : "Save changes";

    /// <summary>The banner text after a successful save.</summary>
    public string SavedMessage
    {
        get
        {
            var when = _form.StartLocal is { } start
                ? start.ToString("ddd d MMM 'at' HH:mm", CultureInfo.InvariantCulture)
                : "the chosen slot";

            var chair = ChairName is { Length: > 0 } name ? $" in {name}" : string.Empty;

            return _openedAsNew
                ? $"Booked for {_form.PatientLabel} — {when}{chair}."
                : $"Saved. {_form.PatientLabel} — {when}{chair}.";
        }
    }

    public string CancelledMessage { get; private set; } = string.Empty;

    /// <summary>
    /// Says this booking is delivering a plan visit, where it is.
    /// </summary>
    /// <remarks>
    /// Stated on the form rather than left implicit in the prefilled fields. Somebody
    /// editing the length or the clinician here is changing what the plan said, and they
    /// should be able to see that is what they are doing.
    /// </remarks>
    /// <remarks>
    /// Falls back to the consent load for an appointment opened from the diary. The plan
    /// arrives in the query string only when the booking is started from the plan itself,
    /// so an existing plan visit reopened later knew nothing about its own plan — the one
    /// case where somebody is most likely to be looking at it on the day.
    /// </remarks>
    public string? PlanVisitLabel => _planVisit is { } visit
        ? $"Visit {visit.Stage} of a treatment plan — {visit.Reason}"
        : _visitConsent is { } booked
            ? $"Visit {booked.Stage} of {booked.PlanTitle}"
            : null;

    /// <summary>Raises the forms for procedures booked before consent was tracked.</summary>
    public IMvxAsyncCommand RaiseVisitConsentCommand { get; }

    /// <summary>Opens the patient's documents tab, where the signature is taken.</summary>
    public IMvxCommand TakeConsentCommand { get; }

    /// <summary>True where the plan's items will be booked against this appointment.</summary>
    public bool LinksPlanVisit => _form.IsPlanVisit;

    /// <summary>True where this visit already has an appointment, so nothing will be linked.</summary>
    public bool PlanVisitAlreadyBooked => _planVisit is { NeedsBooking: false };

    // ---- consent for this visit -----------------------------------------

    /// <summary>True where this appointment delivers planned treatment.</summary>
    public bool IsBookedPlanVisit => _visitConsent is not null;

    /// <summary>The procedures at this visit that need their own signed form.</summary>
    public IReadOnlyList<VisitConsentRow> ConsentRows => _visitConsent?.Rows ?? [];

    /// <summary>True where a plan visit needs no separate consent, which is worth saying.</summary>
    public bool ConsentNotNeeded => _visitConsent is { Rows.Count: 0 };

    /// <summary>Every procedure here has a signed form.</summary>
    public bool ConsentIsClear => _visitConsent is { Rows.Count: > 0 } consent && consent.IsClear;

    public int ConsentOutstandingCount => _visitConsent?.OutstandingCount ?? 0;

    /// <summary>True where some procedure has no form raised against it at all.</summary>
    public bool CanRaiseConsent => _visitConsent is { } consent && consent.HasUnraised;

    /// <summary>Why the forms could not be raised.</summary>
    public string? ConsentRefusal => _consentRefusal;

    /// <summary>
    /// The line above the consent list — what still stands between this visit and treatment.
    /// </summary>
    public string ConsentHeadline => ConsentOutstandingCount switch
    {
        0 => "Consent signed for everything booked at this visit.",
        1 => "One procedure at this visit has no signed consent.",
        var many => $"{many} procedures at this visit have no signed consent.",
    };

    /// <summary>How one row reads — the state, not just the word.</summary>
    public static string ConsentRowNote(VisitConsentRow row) => row.Status switch
    {
        ConsentStatus.Signed => "Signed",
        ConsentStatus.Pending => "Waiting for a signature",
        ConsentStatus.Refused => "Refused — treatment must not go ahead",
        ConsentStatus.Withdrawn => "Withdrawn — treatment must not go ahead",
        ConsentStatus.Expired => "Expired — needs taking again",
        _ => "No form raised",
    };

    // ---- fields the view binds ------------------------------------------

    public string PatientSearch
    {
        get => _patientSearch;
        set => SetProperty(ref _patientSearch, value);
    }

    public IReadOnlyList<PatientListItemDto> SearchResults => _searchResults;

    public bool HasPatient => _form.PatientId != Guid.Empty;

    public DateOnly? Date
    {
        get => _form.Date;
        set
        {
            _form.Date = value;
            RaisePropertyChanged();
        }
    }

    public TimeOnly? Time
    {
        get => _form.Time;
        set
        {
            _form.Time = value;
            RaisePropertyChanged();
        }
    }

    /// <summary>
    /// The earliest date the picker offers.
    /// </summary>
    /// <remarks>
    /// Today for a new booking, so the calendar does not open on a past month. An existing
    /// appointment keeps its own date as the floor — clamping to today would make a
    /// historic booking unopenable in the very picker meant to correct it.
    /// </remarks>
    public DateOnly EarliestDate => _openedAsNew
        ? _clock.Today
        : _form.Date is { } existing && existing < _clock.Today ? existing : _clock.Today;

    public string? Reason
    {
        get => _form.Reason;
        set
        {
            _form.Reason = value;
            RaisePropertyChanged();
        }
    }

    public string? Notes
    {
        get => _form.Notes;
        set
        {
            _form.Notes = value;
            RaisePropertyChanged();
        }
    }

    public string? CancelReason
    {
        get => _cancelReason;
        set => SetProperty(ref _cancelReason, value);
    }

    public bool IsCancelling => _isCancelling;

    public int DurationMinutes => _form.DurationMinutes;

    public Guid? SelectedTypeId => _form.AppointmentTypeId;

    public bool IsProviderSelected(Guid providerId) => _form.ProviderId == providerId;

    public bool IsChairSelected(Guid operatoryId) => _form.OperatoryId == operatoryId;

    public string? ChairName => _options.Chairs
        .FirstOrDefault(chair => chair.Id == _form.OperatoryId)
        ?.Name;

    /// <summary>The chosen clinician's name, for the checks panel.</summary>
    public string? ProviderName => _options.Providers
        .FirstOrDefault(provider => provider.Id == _form.ProviderId)
        ?.Name;

    /// <summary>
    /// Why the chosen slot cannot be booked, or null when it can.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Worked out live from the form rather than read out of the save's errors. As a save
    /// error it was stale the moment anything changed: refusing a Sunday and then moving
    /// to the Saturday left "The practice is closed that day" sitting under a date that is
    /// open, contradicting the field directly above it until the next save.
    /// </para>
    /// <para>
    /// The same <see cref="PracticeHours.RefuseSlot"/> the service validates with, so the
    /// two cannot disagree — and the service still checks it, because a view model is not
    /// where a rule gets enforced.
    /// </para>
    /// </remarks>
    public string? SlotProblem => _form.StartLocal is { } start
        ? _session.Hours.RefuseSlot(start, _form.DurationMinutes)
        : null;

    /// <summary>
    /// Why the chosen clinician is not available for this slot, or null if they are.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="SlotProblem"/> and shown below it, because they are
    /// different problems with different fixes — the practice being shut is fixed by
    /// moving the day, a clinician being off is also fixed by picking someone else. Only
    /// reported when the slot itself is fine, so a Sunday booking does not additionally
    /// announce that the dentist is not in.
    /// </para>
    /// <para>
    /// A warning and never a refusal, unlike the practice's own hours. A weekly pattern is
    /// the usual case rather than a rule: clinicians come in on a day off for an emergency,
    /// and a practice that cannot record what it actually did starts keeping the real
    /// diary somewhere else. The same reasoning the chair clash already used.
    /// </para>
    /// </remarks>
    public string? ProviderProblem
    {
        get
        {
            if (SlotProblem is not null) return null;
            if (_form.StartLocal is not { } start) return null;

            if (_options.Providers.FirstOrDefault(p => p.Id == _form.ProviderId)
                is not { } provider)
            {
                return null;
            }

            return ProviderAvailability.RefuseSlot(
                provider.Name,
                provider.WorkingDays,
                provider.WorkingFrom,
                provider.WorkingTo,
                start,
                _form.DurationMinutes);
        }
    }

    /// <summary>
    /// "not in Tue" for a clinician who does not work the chosen day, else null.
    /// </summary>
    /// <remarks>
    /// On the button rather than hidden behind selecting them, so the front desk can see
    /// who is in before choosing rather than after. Only the day, not the hours: a caption
    /// on every button saying "finishes 15:00" is noise on the days it does not bite.
    /// </remarks>
    public string? UnavailableNote(ProviderOption provider)
    {
        if (_form.Date is not { } date) return null;

        return ProviderAvailability.WorksOn(provider.WorkingDays, date)
            ? null
            : $"not in {date.DayOfWeek.ToString()[..3]}";
    }

    /// <summary>"08:45–09:45" — the slot, once date and time parse.</summary>
    public string SlotLabel
    {
        get
        {
            if (_form.StartLocal is not { } start) return "—";

            var end = start.AddMinutes(_form.DurationMinutes);

            return $"{start.ToString("HH:mm", CultureInfo.InvariantCulture)}"
                + $"–{end.ToString("HH:mm", CultureInfo.InvariantCulture)}";
        }
    }

    // ---- validation -----------------------------------------------------

    public IReadOnlyDictionary<string, string> Errors => _errors;

    public bool HasErrors => _errors.Count > 0;

    public string? ErrorFor(string field) => _errors.GetValueOrDefault(field);

    /// <summary>The error that belongs to no particular field.</summary>
    public string? FormError => _errors.GetValueOrDefault(string.Empty);

    // ---- loading and saving ---------------------------------------------

    private Task LoadAsync() => RunGuardedAsync(async () =>
    {
        _options = await _appointments
            .GetOptionsAsync(_session.LocationId)
            .ConfigureAwait(false);

        if (_id == Guid.Empty)
        {
            _form = NewForm();

            // The plan visit first, and the loose patient id only if there was no plan.
            // A plan already names its patient, and applying both would have the weaker
            // source able to overwrite the stronger one.
            if (!await ApplyInitialPlanVisitAsync().ConfigureAwait(false))
            {
                await ApplyInitialPatientAsync().ConfigureAwait(false);
            }
        }
        else
        {
            var existing = await _appointments.GetFormAsync(_id).ConfigureAwait(false);

            if (existing is null)
            {
                _notFound = true;
                RaiseAll();
                return;
            }

            _form = existing;

            // Already cancelled or missed: the screen opens read-only rather than
            // offering a form it would refuse on save. Inviting an edit and then
            // rejecting it wastes the retyping and teaches staff to distrust the button.
            if (AppointmentProgress.IsLostSlot(existing.Status))
            {
                CancelledMessage = existing.Status == AppointmentStatus.FailedToAttend
                    ? "This appointment was recorded as a no-show. It is kept on the record; "
                        + "book a new one instead of editing it."
                    : "This appointment was cancelled. It is kept on the record; book a new "
                        + "one instead of editing it.";

                _stage = AppointmentEditStage.Cancelled;
            }

            await ReloadVisitConsentAsync().ConfigureAwait(false);
        }

        _checks = await _appointments.GetChecksAsync(_form).ConfigureAwait(false);

        RaiseAll();
    });

    /// <summary>
    /// A blank booking, seeded from the slot the user clicked.
    /// </summary>
    /// <remarks>
    /// Defaults to the next quarter-hour today when nothing was passed, rather than an
    /// empty date field: the overwhelmingly common booking is "now-ish", and an empty date
    /// makes the form ask a question it could have answered.
    /// </remarks>
    private AppointmentForm NewForm()
    {
        var date = _initialDate ?? _clock.Today;

        var time = _initialTime ?? NextQuarterHour();


        // The first of each is the default, so the form is savable without touching either
        // row — the front desk overrides them when it matters. Independent now: choosing a
        // chair no longer implies a clinician, which is what made the old picker a grid.
        return new AppointmentForm
        {
            PracticeLocationId = _session.LocationId,
            Date = date,
            Time = time,
            ProviderId = _options.Providers.FirstOrDefault()?.Id ?? Guid.Empty,

            // The chair the empty slot was clicked in, where the diary passed one.
            OperatoryId = _initialOperatoryId ?? _options.Chairs.FirstOrDefault()?.Id,
            DurationMinutes = 30,
        };
    }

    /// <summary>
    /// Fills in the patient the booking was started for, where the record passed one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The label is read back from the id rather than carried in the URL. A query string
    /// is editable by anyone who can reach the address bar, and a form that displayed
    /// whatever name it was handed would print one person's name above another person's
    /// booking — which is the one mistake this screen must not be able to make.
    /// </para>
    /// <para>
    /// An id that resolves to nothing — another tenant's patient, or a deleted one —
    /// leaves the form empty rather than half-filled, so the search field asks the
    /// question instead of the form quietly booking for nobody. The read is tenant-scoped
    /// by the repository's own filter, so this needs no check of its own.
    /// </para>
    /// </remarks>
    private async Task ApplyInitialPatientAsync()
    {
        if (_initialPatientId is not { } patientId || patientId == Guid.Empty) return;

        var label = await _appointments
            .GetPatientLabelAsync(patientId)
            .ConfigureAwait(false);

        if (label is null) return;

        _form.PatientId = patientId;
        _form.PatientLabel = label;
    }

    /// <summary>
    /// Fills the form in from the treatment-plan visit it was started from.
    /// </summary>
    /// <returns>True where a plan visit was applied, so the caller skips the patient seed.</returns>
    /// <remarks>
    /// <para>
    /// The clinician and the length come from the plan rather than from this form's own
    /// defaults: the plan decided who is doing the work and how long its items take, and a
    /// booking that quietly re-guessed either would put the visit in the wrong diary
    /// column at the wrong length.
    /// </para>
    /// <para>
    /// A visit already booked is applied anyway, minus the link. Somebody who reaches this
    /// form for an already-booked visit is far more likely to be rebooking it than to want
    /// a second appointment for the same work, and linking would have moved the plan's
    /// items off the original appointment onto an empty new one.
    /// </para>
    /// </remarks>
    private async Task<bool> ApplyInitialPlanVisitAsync()
    {
        if (_initialPlanId is not { } planId || planId == Guid.Empty) return false;
        if (_initialStage is not { } stage || stage < 1) return false;

        var visit = await _plans.GetVisitAsync(planId, stage).ConfigureAwait(false);

        if (visit is null) return false;

        _form.PatientId = visit.PatientId;

        // Labelled by the booking form's own rule — "Margaret Yuen · #10201" — not by the
        // plan's, which is a bare name because that is what reads well in a plan heading.
        // The patient number is how the front desk confirms it has the right person, and
        // it should not go missing only on bookings that arrived from a plan.
        _form.PatientLabel = await _appointments
            .GetPatientLabelAsync(visit.PatientId)
            .ConfigureAwait(false) ?? visit.PatientLabel;

        _form.PracticeLocationId = visit.PracticeLocationId;
        _form.Reason = visit.Reason;

        if (visit.ProviderId != Guid.Empty) _form.ProviderId = visit.ProviderId;
        if (visit.Minutes > 0) _form.DurationMinutes = visit.Minutes;

        // Linked only where something still needs booking. Re-linking a visit that is
        // already on the books would move its items onto this new slot and leave the
        // original appointment holding nothing.
        if (visit.NeedsBooking)
        {
            _form.TreatmentPlanId = planId;
            _form.PlanStageNumber = stage;
        }

        _planVisit = visit;

        return true;
    }

    /// <summary>
    /// Reads the visit's consent position back.
    /// </summary>
    /// <remarks>
    /// Unguarded on purpose, and called from inside the guarded loads. RunGuardedAsync
    /// returns silently while IsBusy, so a guarded reload nested in a guarded caller does
    /// nothing at all — the database is right and the screen is not.
    /// </remarks>
    private async Task ReloadVisitConsentAsync()
    {
        _visitConsent = await _plans.VisitConsentAsync(_id).ConfigureAwait(false);

        RaiseConsent();
    }

    private Task RaiseVisitConsentAsync() => RunGuardedAsync(async () =>
    {
        _consentRefusal = await _plans.RaiseVisitConsentAsync(_id).ConfigureAwait(false);

        await ReloadVisitConsentAsync().ConfigureAwait(false);
    });

    /// <summary>
    /// Opens the consent screen on the form this visit is waiting for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Straight to the form, not to the documents tab. Somebody pressing this has the
    /// patient in front of them; landing them on a file list to find the right row is one
    /// step too many at the moment it is least welcome.
    /// </para>
    /// <para>
    /// The first outstanding one, where a visit has several. They are signed one at a time
    /// — each procedure has its own risks — and the screen returns here to the record, so
    /// the next is picked up from the list rather than by this button remembering a queue.
    /// </para>
    /// </remarks>
    private void TakeConsent()
    {
        if (_visitConsent is not { } consent) return;

        var next = consent.Rows.FirstOrDefault(row => row.Outstanding && row.ConsentId is not null);

        if (next?.ConsentId is { } consentId)
        {
            _navigator.ToConsent(consent.PatientId, consentId);
            return;
        }

        // Nothing raised yet, so there is no form to open. The documents tab at least shows
        // what is on the record, and "Raise the forms" beside this button is the fix.
        _navigator.ToPatientRecord(consent.PatientId, "documents");
    }

    private void RaiseConsent()
    {
        foreach (var name in new[]
        {
            nameof(IsBookedPlanVisit), nameof(ConsentRows), nameof(ConsentNotNeeded),
            nameof(ConsentIsClear), nameof(ConsentOutstandingCount), nameof(CanRaiseConsent),
            nameof(ConsentRefusal), nameof(ConsentHeadline), nameof(PlanVisitLabel),
        })
        {
            RaisePropertyChanged(name);
        }
    }

    private TimeOnly NextQuarterHour()
    {
        var now = TimeOnly.FromDateTime(_clock.UtcNow.ToLocalTime());

        var minutes = now.Hour * 60 + now.Minute;
        var rounded = ((minutes + PracticeHours.SlotMinutes - 1) / PracticeHours.SlotMinutes)
            * PracticeHours.SlotMinutes;

        // Clamped into the working day, so a booking started at 19:00 opens on tomorrow's
        // first slot rather than at a time the form would immediately refuse.
        var clamped = Math.Clamp(rounded, _session.Hours.OpenMinutes, _session.Hours.CloseMinutes - 60);

        return new TimeOnly(0, 0).AddMinutes(clamped);
    }

    private Task SaveAsync() => RunGuardedAsync(async () =>
    {
        var result = await _appointments.SaveAsync(_form).ConfigureAwait(false);

        _errors = result.Errors;

        // The id first, and whatever the outcome. A save can now fail *after* writing the
        // appointment — the plan link is a second write and can be refused on its own — and
        // leaving the form thinking it was still new meant pressing Save again booked the
        // slot a second time.
        if (result.AppointmentId != Guid.Empty) _form.Id = result.AppointmentId;

        if (!result.Succeeded)
        {
            RaiseAll();
            return;
        }

        _stage = AppointmentEditStage.Saved;

        RaiseAll();
    });

    private Task SelectTypeAsync(Guid typeId) => RunGuardedAsync(async () =>
    {
        _form.AppointmentTypeId = typeId;

        // The type's default duration is applied, which is the whole point of picking one:
        // the design's caption says the type "sets colour & default duration", and the
        // biggest single cause of a diary running late is a length guessed by hand.
        if (_options.Types.FirstOrDefault(type => type.Id == typeId) is { } chosen)
        {
            _form.DurationMinutes = chosen.DefaultDurationMinutes;
        }

        await ReloadChecksAsync().ConfigureAwait(false);
    });

    private Task SelectProviderAsync(Guid providerId) => RunGuardedAsync(async () =>
    {
        _form.ProviderId = providerId;

        await ReloadChecksAsync().ConfigureAwait(false);
    });

    private Task SelectChairAsync(Guid operatoryId) => RunGuardedAsync(async () =>
    {
        _form.OperatoryId = operatoryId;

        await ReloadChecksAsync().ConfigureAwait(false);
    });

    private Task SelectDurationAsync(int minutes) => RunGuardedAsync(async () =>
    {
        _form.DurationMinutes = minutes;

        await ReloadChecksAsync().ConfigureAwait(false);
    });

    private Task SearchPatientsAsync() => RunGuardedAsync(async () =>
    {
        _searchResults = await _appointments
            .SearchPatientsAsync(_patientSearch)
            .ConfigureAwait(false);

        await RaisePropertyChanged(nameof(SearchResults)).ConfigureAwait(false);
    });

    private Task ChoosePatientAsync(Guid patientId) => RunGuardedAsync(async () =>
    {
        _form.PatientId = patientId;
        _form.PatientLabel = await _appointments
            .GetPatientLabelAsync(patientId)
            .ConfigureAwait(false) ?? "Unknown patient";

        // The results and the term are cleared: leaving a list of eight other people open
        // under a chosen patient is how the wrong one gets picked on the second glance.
        _searchResults = [];
        _patientSearch = string.Empty;

        _checks = await _appointments.GetChecksAsync(_form).ConfigureAwait(false);

        RaiseAll();
    });

    private void ClearPatient()
    {
        _form.PatientId = Guid.Empty;
        _form.PatientLabel = string.Empty;
        _checks = new PreBookingChecks();

        RaiseAll();
    }

    private Task RefreshChecksAsync() => RunGuardedAsync(ReloadChecksAsync);

    /// <summary>
    /// Re-runs the pre-booking checks, without the busy guard around it.
    /// </summary>
    /// <remarks>
    /// Split out because RunGuardedAsync refuses to nest — it returns immediately while
    /// already busy — and every one of the Select* commands is itself guarded. So picking
    /// a type, a provider, a chair or a duration set the field and then silently skipped
    /// the refresh: the panel went on showing the clashes and alerts computed for whatever
    /// was selected before. Only the date and time were ever right, because those call the
    /// guarded command directly instead of from inside another one.
    /// </remarks>
    private async Task ReloadChecksAsync()
    {
        _checks = await _appointments.GetChecksAsync(_form).ConfigureAwait(false);

        RaiseAll();
    }

    private void SetCancelling(bool value)
    {
        _isCancelling = value;

        if (!value) _cancelReason = null;

        RaisePropertyChanged(nameof(IsCancelling));
        RaisePropertyChanged(nameof(CancelReason));
    }

    private Task ConfirmCancelAsync(bool failedToAttend) => RunGuardedAsync(async () =>
    {
        var refusal = await _appointments
            .CancelAsync(_form.Id, _cancelReason, failedToAttend)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            _errors = new Dictionary<string, string> { [string.Empty] = refusal };
            RaiseAll();
            return;
        }

        CancelledMessage = failedToAttend
            ? $"Recorded as a no-show. {_form.PatientLabel}'s FTA count has gone up by one, "
                + "and the slot is free for the short-notice list."
            : $"Cancelled. The slot is free for the short-notice list.";

        _isCancelling = false;
        _stage = AppointmentEditStage.Cancelled;

        RaiseAll();
    });

    private void OpenPatient()
    {
        if (_form.PatientId != Guid.Empty) _navigator.ToPatientRecord(_form.PatientId);
    }

    /// <summary>
    /// Everything the screen reads. Listed explicitly rather than raising a blanket change,
    /// which would re-render the note box being typed in.
    /// </summary>
    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(NotFound), nameof(Form), nameof(Options), nameof(Checks), nameof(Stage),
            nameof(IsEditing), nameof(IsExisting), nameof(Title), nameof(SaveLabel),
            nameof(SavedMessage), nameof(CancelledMessage), nameof(HasPatient),
            nameof(SearchResults), nameof(PatientSearch), nameof(Date), nameof(Time),
            nameof(Reason), nameof(Notes), nameof(DurationMinutes), nameof(SelectedTypeId),
            nameof(ChairName), nameof(SlotLabel), nameof(SlotProblem), nameof(ProviderProblem), nameof(Errors),
            nameof(HasErrors),
            nameof(FormError), nameof(IsCancelling), nameof(CancelReason),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
