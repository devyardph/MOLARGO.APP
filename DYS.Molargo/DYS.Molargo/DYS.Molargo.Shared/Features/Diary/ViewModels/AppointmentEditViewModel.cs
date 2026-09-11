using System.Globalization;
using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Dtos;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Features.Diary.Services;
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
    public static readonly int[] Durations = [15, 20, 30, 45, 60, 90];

    private readonly IAppointmentService _appointments;
    private readonly ISessionService _session;
    private readonly IAppNavigator _navigator;
    private readonly IClock _clock;

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

    public AppointmentEditViewModel(
        IAppointmentService appointments,
        ISessionService session,
        IAppNavigator navigator,
        IClock clock)
    {
        _appointments = appointments;
        _session = session;
        _navigator = navigator;
        _clock = clock;

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
        StartCancelCommand = new MvxCommand(() => SetCancelling(true));
        AbandonCancelCommand = new MvxCommand(() => SetCancelling(false));
        ConfirmCancelCommand = new MvxAsyncCommand<bool>(ConfirmCancelAsync);
        OpenPatientCommand = new MvxCommand(OpenPatient);
    }

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
    /// whether they are already busy elsewhere.
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

    private TimeOnly NextQuarterHour()
    {
        var now = TimeOnly.FromDateTime(_clock.UtcNow.ToLocalTime());

        var minutes = now.Hour * 60 + now.Minute;
        var rounded = ((minutes + PracticeHours.SlotMinutes - 1) / PracticeHours.SlotMinutes)
            * PracticeHours.SlotMinutes;

        // Clamped into the working day, so a booking started at 19:00 opens on tomorrow's
        // first slot rather than at a time the form would immediately refuse.
        var clamped = Math.Clamp(rounded, PracticeHours.OpenMinutes, PracticeHours.CloseMinutes - 60);

        return new TimeOnly(0, 0).AddMinutes(clamped);
    }

    private Task SaveAsync() => RunGuardedAsync(async () =>
    {
        var result = await _appointments.SaveAsync(_form).ConfigureAwait(false);

        _errors = result.Errors;

        if (!result.Succeeded)
        {
            RaiseAll();
            return;
        }

        _form.Id = result.AppointmentId;
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

        await RefreshChecksAsync().ConfigureAwait(false);
    });

    private Task SelectProviderAsync(Guid providerId) => RunGuardedAsync(async () =>
    {
        _form.ProviderId = providerId;

        await RefreshChecksAsync().ConfigureAwait(false);
    });

    private Task SelectChairAsync(Guid operatoryId) => RunGuardedAsync(async () =>
    {
        _form.OperatoryId = operatoryId;

        await RefreshChecksAsync().ConfigureAwait(false);
    });

    private Task SelectDurationAsync(int minutes) => RunGuardedAsync(async () =>
    {
        _form.DurationMinutes = minutes;

        await RefreshChecksAsync().ConfigureAwait(false);
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

    private Task RefreshChecksAsync() => RunGuardedAsync(async () =>
    {
        _checks = await _appointments.GetChecksAsync(_form).ConfigureAwait(false);

        RaiseAll();
    });

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
            nameof(ChairName), nameof(SlotLabel), nameof(Errors), nameof(HasErrors),
            nameof(FormError), nameof(IsCancelling), nameof(CancelReason),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
