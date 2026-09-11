using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Features.Patient.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Features.Patient.ViewModels;

/// <summary>
/// Where the form is in the save sequence.
/// </summary>
public enum PatientEditStage
{
    /// <summary>Being filled in.</summary>
    Editing = 0,

    /// <summary>
    /// Save found existing patients that look like this one. Nothing has been written;
    /// the user chooses to merge or to keep them separate.
    /// </summary>
    DuplicateFound = 1,

    /// <summary>Written.</summary>
    Saved = 2,

    /// <summary>Merged into an existing record instead of creating a new one.</summary>
    Merged = 3,
}

/// <summary>
/// Creating a new patient, or editing an existing one — the prototype's single form for
/// both, with the duplicate check that runs on create.
/// </summary>
/// <typeparam name="TParameter">
/// The patient's id, or <see cref="Guid.Empty"/> for a new patient. One view model for
/// both because the form is the same form: splitting it would mean two of every field,
/// two validators and two save paths that have to stay in step.
/// </typeparam>
public sealed class PatientEditViewModel : BaseViewModel<Guid>
{
    private readonly IPatientService _patients;
    private readonly IAppNavigator _navigator;
    private readonly IClock _clock;

    private readonly Dictionary<string, string> _errors = [];

    private Guid _id;
    private PatientForm _form = new();
    private PatientEditStage _stage = PatientEditStage.Editing;
    private IReadOnlyList<DuplicateCandidate> _duplicates = [];
    private IReadOnlyList<HouseholdOption> _households = [];
    private IReadOnlyList<PatientAlert> _existingAlerts = [];
    private Guid _savedId;
    private int _addedAlertCount;

    public PatientEditViewModel(IPatientService patients, IAppNavigator navigator, IClock clock)
    {
        _patients = patients;
        _navigator = navigator;
        _clock = clock;

        // Built once, in the constructor — never rebuilt per render.
        SaveCommand = new MvxAsyncCommand(SaveAsync);
        CancelCommand = new MvxCommand(Cancel);
        ToggleTagCommand = new MvxCommand<PatientTags>(ToggleTag);
        SetSexCommand = new MvxCommand<Sex>(SetSex);
        MergeCommand = new MvxAsyncCommand<Guid>(MergeIntoAsync);
        KeepSeparateCommand = new MvxAsyncCommand(KeepSeparateAsync);
        DoneCommand = new MvxCommand(Done);
        OpenSavedCommand = new MvxCommand(() => _navigator.ToPatientRecord(_savedId));
    }

    public IMvxAsyncCommand SaveCommand { get; }

    public IMvxCommand CancelCommand { get; }

    public IMvxCommand<PatientTags> ToggleTagCommand { get; }

    public IMvxCommand<Sex> SetSexCommand { get; }

    public IMvxAsyncCommand<Guid> MergeCommand { get; }

    public IMvxAsyncCommand KeepSeparateCommand { get; }

    public IMvxCommand DoneCommand { get; }

    public IMvxCommand OpenSavedCommand { get; }

    public override void Prepare(Guid parameter) => _id = parameter;

    public override Task Initialize() => LoadAsync();

    /// <summary>The form the view binds to. Mutated in place by the bound inputs.</summary>
    public PatientForm Form => _form;

    public PatientEditStage Stage => _stage;

    /// <summary>True for a new patient, false when editing one that exists.</summary>
    public bool IsNew => _id == Guid.Empty;

    /// <summary>False when the requested patient does not exist.</summary>
    public bool NotFound { get; private set; }

    /// <summary>"New patient", or "Edit patient — Margaret Yuen · #10201".</summary>
    public string Title
    {
        get
        {
            if (IsNew) return "New patient";

            var number = _form.PatientNumber is { Length: > 0 } value ? $" · #{value}" : string.Empty;
            return $"Edit patient — {_form.FirstName} {_form.LastName}{number}".Replace("  ", " ");
        }
    }

    /// <summary>The line under the title, telling the user what save will do.</summary>
    public string SaveNote => IsNew
        ? "Duplicate check runs on save — name, date of birth and mobile"
        : "Changes are audit-logged against your login";

    public IReadOnlyList<HouseholdOption> Households => _households;

    /// <summary>
    /// The alerts already on file. Shown read-only when editing — see the remarks on
    /// <see cref="PatientForm.Allergies"/> for why they are not editable here.
    /// </summary>
    public IReadOnlyList<PatientAlert> ExistingAlerts => _existingAlerts;

    public IReadOnlyList<DuplicateCandidate> Duplicates => _duplicates;

    /// <summary>The best match, which is the one the banner compares against.</summary>
    public DuplicateCandidate? BestDuplicate => _duplicates.FirstOrDefault();

    // ---- stage flags the view branches on ---------------------------------

    public bool IsEditing => _stage == PatientEditStage.Editing;

    public bool IsDuplicateFound => _stage == PatientEditStage.DuplicateFound;

    public bool IsSaved => _stage == PatientEditStage.Saved;

    public bool IsMerged => _stage == PatientEditStage.Merged;

    /// <summary>
    /// The chip line on the saved banner. Reports what actually happened rather than the
    /// prototype's fixed sentence — "household unchanged" is a lie if a household was
    /// just linked.
    /// </summary>
    public string SavedNote
    {
        get
        {
            var parts = new List<string>
            {
                _form.ReminderConsent ? "reminders on" : "reminders off",
                _form.MarketingConsent ? "marketing on" : "marketing off",
            };

            parts.Add(_form.HouseholdId is null ? "no household" : "household linked");

            if (_addedAlertCount > 0)
            {
                parts.Add(_addedAlertCount == 1 ? "1 alert added" : $"{_addedAlertCount} alerts added");
            }

            return string.Join(" · ", parts);
        }
    }

    // ---- validation ------------------------------------------------------

    public bool HasValidationErrors => _errors.Count > 0;

    /// <summary>The message for a field, or null when it is fine.</summary>
    public string? Error(string field) => _errors.GetValueOrDefault(field);

    /// <summary>Adds the error styling to an input that failed validation.</summary>
    public bool IsInvalid(string field) => _errors.ContainsKey(field);

    public bool IsTagSet(PatientTags tag) => _form.Tags.HasFlag(tag);

    private Task LoadAsync() => RunGuardedAsync(async () =>
    {
        _households = await _patients.GetHouseholdsAsync().ConfigureAwait(false);

        if (IsNew)
        {
            _form = new PatientForm
            {
                // Reserved up front so the number is on screen before saving. It is only
                // a preview: two people creating a patient at once would both see the
                // same one, and the second save quietly takes the next. Worth it for the
                // front desk being able to read the number to the patient immediately.
                PatientNumber = await _patients.GetNextPatientNumberAsync().ConfigureAwait(false),
            };

            _existingAlerts = [];
            RaiseAllDerived();
            return;
        }

        var record = await _patients.GetRecordAsync(_id).ConfigureAwait(false);

        if (record is null)
        {
            NotFound = true;
            RaiseAllDerived();
            return;
        }

        _form = PatientForm.From(record.Patient);
        _existingAlerts = record.Alerts;
        RaiseAllDerived();
    });

    private Task SaveAsync() => RunGuardedAsync(async () =>
    {
        if (!Validate()) return;

        // The email check needs a read, so it cannot live in Validate, which is
        // synchronous. It still reports as a field error on Email, because that is where
        // the person has to go to fix it.
        if (!await EmailIsFreeAsync().ConfigureAwait(false))
        {
            RaiseAllDerived();
            return;
        }

        // The duplicate check runs on create only. Editing an existing patient into
        // looking like another one is a real situation, but interrupting the edit is the
        // wrong moment to raise it — the record is already there either way.
        if (IsNew)
        {
            _duplicates = await _patients
                .FindDuplicatesAsync(
                    _form.FirstName,
                    _form.LastName,
                    _form.ParsedDateOfBirth,
                    _form.Mobile)
                .ConfigureAwait(false);

            if (_duplicates.Count > 0)
            {
                _stage = PatientEditStage.DuplicateFound;
                RaiseAllDerived();
                return;
            }
        }

        await WriteAsync().ConfigureAwait(false);
    });

    /// <summary>
    /// Saves anyway, having been shown the possible duplicates. The practice's judgement
    /// beats the score.
    /// </summary>
    private Task KeepSeparateAsync() => RunGuardedAsync(WriteAsync);

    /// <summary>
    /// Merges into an existing record rather than creating a second one.
    /// </summary>
    /// <remarks>
    /// What this does <em>not</em> do is combine history, documents and balances — the
    /// prototype's banner promises that, and a real merge is a large piece of work with a
    /// reversal window behind it. What it does is fill in whatever the existing record is
    /// missing from what was just typed, and create no second record. Nothing is
    /// overwritten and nothing is deleted, which is what makes it safe to offer now; the
    /// banner says as much rather than claiming the merge the prototype describes.
    /// </remarks>
    private Task MergeIntoAsync(Guid existingId) => RunGuardedAsync(async () =>
    {
        var existing = await _patients.GetAsync(existingId).ConfigureAwait(false);
        if (existing is null) return;

        // MergeInto, not ApplyTo: it fills blanks and leaves everything the existing
        // record already has. ApplyTo writes every field including the empty ones, which
        // would wipe this patient's email, address and fund details with the new entry's
        // blanks. The existing record also keeps its own number — renumbering someone
        // already on correspondence and in the ledger is not a merge, it is a mistake.
        _form.MergeInto(existing);

        _savedId = await _patients.SaveAsync(existing).ConfigureAwait(false);
        _addedAlertCount = await AddAlertsAsync(_savedId).ConfigureAwait(false);

        _stage = PatientEditStage.Merged;
        RaiseAllDerived();
    });

    private async Task WriteAsync()
    {
        var patient = IsNew
            ? new PatientEntity()
            : await _patients.GetAsync(_id).ConfigureAwait(false) ?? new PatientEntity();

        // Re-read rather than reserved-at-load, so two front-desk staff creating patients
        // at the same time do not both save the number they were shown.
        if (IsNew)
        {
            _form.PatientNumber = await _patients.GetNextPatientNumberAsync().ConfigureAwait(false);
        }

        _form.ApplyTo(patient);

        _savedId = await _patients.SaveAsync(patient).ConfigureAwait(false);
        _addedAlertCount = await AddAlertsAsync(_savedId).ConfigureAwait(false);

        _stage = PatientEditStage.Saved;
        RaiseAllDerived();
    }

    /// <summary>
    /// Turns whatever was typed into the three medical-summary boxes into alert rows.
    /// </summary>
    /// <remarks>
    /// Additive only, and it clears the boxes afterwards so a second save does not add
    /// everything twice. Severity is set from the kind rather than asked for: an allergy
    /// or an anticoagulant typed into this form has to interrupt prescribing, and making
    /// that a dropdown invites someone in a hurry to leave it on "information".
    /// </remarks>
    private async Task<int> AddAlertsAsync(Guid patientId)
    {
        var added = 0;

        added += await AddAlertsOfKindAsync(patientId, _form.Allergies, AlertKind.Allergy,
            AlertSeverity.Critical).ConfigureAwait(false);

        added += await AddAlertsOfKindAsync(patientId, _form.Medications, AlertKind.Medication,
            AlertSeverity.Critical).ConfigureAwait(false);

        added += await AddAlertsOfKindAsync(patientId, _form.Conditions, AlertKind.MedicalCondition,
            AlertSeverity.Warning).ConfigureAwait(false);

        _form.Allergies = null;
        _form.Medications = null;
        _form.Conditions = null;

        return added;
    }

    private async Task<int> AddAlertsOfKindAsync(
        Guid patientId, string? text, AlertKind kind, AlertSeverity severity)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        // Split on newlines and on the interpunct the design uses to separate them in a
        // single line — the prototype shows "Penicillin (rash, 2019) · Latex" as one
        // field, and staff will type it that way.
        var entries = text
            .Split(['\n', '\r', '·', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var entry in entries)
        {
            await _patients
                .AddAlertAsync(patientId, kind, severity, entry, _clock.Today)
                .ConfigureAwait(false);
        }

        return entries.Count;
    }

    /// <summary>
    /// Whether no other patient at this clinic already holds the typed email.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A blank email is always free. Most patients rung in from a waiting room have no
    /// address at all, and treating "no email" as a collision would refuse every one of
    /// them after the first.
    /// </para>
    /// <para>
    /// The refusal names the patient holding it, on purpose. A shared address is not
    /// always a mistake — a parent's email on a child's record is the norm in a family
    /// practice — so the front desk needs to see <em>who</em> has it to tell a duplicate
    /// record from a sibling. Naming them is what makes an otherwise blunt rule workable.
    /// </para>
    /// </remarks>
    private async Task<bool> EmailIsFreeAsync()
    {
        if (string.IsNullOrWhiteSpace(_form.Email)) return true;

        var holder = await _patients
            .FindEmailHolderAsync(_form.Email, IsNew ? null : _id)
            .ConfigureAwait(false);

        if (holder is null) return true;

        var number = holder.PatientNumber is { Length: > 0 } patientNumber
            ? $" (#{patientNumber})"
            : string.Empty;

        _errors[nameof(PatientForm.Email)] =
            $"{holder.Name}{number} already uses this email address. If this is the same "
                + "person, open their record instead of creating a second one.";

        return false;
    }

    /// <summary>
    /// Checks the form and fills <see cref="_errors"/>.
    /// </summary>
    /// <remarks>
    /// Only the fields that genuinely cannot be wrong are required. A new patient rung in
    /// from a waiting room has a name and nothing else, and a form that demands an address
    /// before it will save one is a form the front desk works around by typing rubbish.
    /// </remarks>
    private bool Validate()
    {
        _errors.Clear();

        if (string.IsNullOrWhiteSpace(_form.FirstName))
        {
            _errors[nameof(PatientForm.FirstName)] = "A first name is required.";
        }

        if (string.IsNullOrWhiteSpace(_form.LastName))
        {
            _errors[nameof(PatientForm.LastName)] = "A last name is required.";
        }

        if (_form.HasUnparseableDateOfBirth)
        {
            _errors[nameof(PatientForm.DateOfBirth)] = "Use DD/MM/YYYY.";
        }
        else if (_form.ParsedDateOfBirth is { } dob)
        {
            if (dob > _clock.Today)
            {
                _errors[nameof(PatientForm.DateOfBirth)] = "That date is in the future.";
            }
            else if (dob < _clock.Today.AddYears(-130))
            {
                // A sanity bound, not an age limit. It catches the transposed year —
                // 1068 for 1968 — which otherwise saves silently and makes the patient
                // 958 years old on every screen that shows an age.
                _errors[nameof(PatientForm.DateOfBirth)] = "Check the year.";
            }
        }

        if (_form.Email is { Length: > 0 } email && !LooksLikeEmail(email))
        {
            _errors[nameof(PatientForm.Email)] = "That does not look like an email address.";
        }

        if (_form.Mobile is { Length: > 0 } mobile && DigitCount(mobile) < 8)
        {
            // Digits, not a pattern. Numbers arrive as "0412 883 021", "+61 412 883 021"
            // and "(02) 9000 1200", and a regex strict enough to reject a typo rejects
            // half of those too.
            _errors[nameof(PatientForm.Mobile)] = "That is too short to be a phone number.";
        }

        // Reminders need a way to reach the patient. Consented with neither a mobile nor
        // an email on file is a promise the practice cannot keep, and it surfaces later
        // as a reminder that was never sent.
        if (_form.ReminderConsent
            && string.IsNullOrWhiteSpace(_form.Mobile)
            && string.IsNullOrWhiteSpace(_form.Email))
        {
            _errors[nameof(PatientForm.ReminderConsent)] =
                "Reminders need a mobile or an email address.";
        }

        RaisePropertyChanged(nameof(HasValidationErrors));
        return _errors.Count == 0;
    }

    private void ToggleTag(PatientTags tag)
    {
        _form.Tags = _form.Tags.HasFlag(tag) ? _form.Tags & ~tag : _form.Tags | tag;
        RaisePropertyChanged(nameof(Form));
    }

    private void SetSex(Sex sex)
    {
        _form.Sex = sex;
        RaisePropertyChanged(nameof(Form));
    }

    /// <summary>
    /// Leaves without saving. Back to the record when editing, to the list when creating —
    /// where the user came from either way.
    /// </summary>
    private void Cancel()
    {
        if (IsNew) _navigator.ToPatientList();
        else _navigator.ToPatientRecord(_id);
    }

    private void Done() => _navigator.ToPatientRecord(_savedId);

    private static bool LooksLikeEmail(string value)
    {
        // Deliberately loose. Full RFC validation rejects addresses that work, and the
        // only mistake worth catching here is a missing @ or domain.
        var at = value.IndexOf('@');
        return at > 0 && value.IndexOf('.', at) > at + 1 && !value.EndsWith('.');
    }

    private static int DigitCount(string value) => value.Count(char.IsDigit);

    /// <summary>
    /// Everything the view branches on. Listed explicitly rather than raising a blanket
    /// change, which on a form would re-render the field the user is typing in.
    /// </summary>
    private void RaiseAllDerived()
    {
        foreach (var name in new[]
        {
            nameof(Form), nameof(Stage), nameof(IsNew), nameof(NotFound), nameof(Title),
            nameof(SaveNote), nameof(Households), nameof(ExistingAlerts),
            nameof(Duplicates), nameof(BestDuplicate), nameof(IsEditing),
            nameof(IsDuplicateFound), nameof(IsSaved), nameof(IsMerged), nameof(SavedNote),
            nameof(HasValidationErrors),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
