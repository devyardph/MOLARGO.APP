using DYS.Molargo.Shared.Components;
using DYS.Molargo.Shared.Features.Patient.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Features.Patient.ViewModels;

/// <summary>
/// One question and the answer being given to it.
/// </summary>
/// <remarks>
/// Mutable, and bound to directly by the view — the same reasoning as
/// <see cref="PatientForm"/>. This is the half-finished state of somebody answering, not
/// a value to pass around.
/// </remarks>
public sealed class MedicalHistoryRow
{
    public required MedicalHistoryQuestion Question { get; init; }

    /// <summary>Null until answered. Not defaulted to "no" — see the view model's remarks.</summary>
    public bool? YesNo { get; set; }

    public string? Detail { get; set; }

    /// <summary>True when a "yes" needs specifics that have not been given.</summary>
    public bool NeedsDetail =>
        YesNo == true && Question.PromptsForDetail && string.IsNullOrWhiteSpace(Detail);

    /// <summary>The detail box only appears once the answer is "yes".</summary>
    public bool ShowsDetail => YesNo == true && Question.PromptsForDetail;
}

/// <summary>
/// Taking a patient's medical history — the prototype's tablet questionnaire, used at
/// check-in and for the annual re-confirmation.
/// </summary>
public sealed class MedicalHistoryViewModel : BaseViewModel<Guid>
{
    private readonly IPatientService _patients;
    private readonly IAppNavigator _navigator;
    private readonly IClock _clock;

    private Guid _id;
    private PatientEntity? _patient;
    private DateTime? _lastConfirmedUtc;
    private List<MedicalHistoryRow> _rows = [];
    private MedicalHistoryResult? _result;
    private string? _signedByName;
    private string _signedByRelationship = PatientRelationship;
    private string? _additionalNotes;
    private bool _showValidation;

    /// <summary>The default relationship: most people sign for themselves.</summary>
    private const string PatientRelationship = "Patient";

    public MedicalHistoryViewModel(
        IPatientService patients, IAppNavigator navigator, IClock clock)
    {
        _patients = patients;
        _navigator = navigator;
        _clock = clock;

        // Built once, in the constructor — never rebuilt per render.
        AnswerCommand = new MvxCommand<(string Code, bool Value)>(Answer);
        SignCommand = new MvxCommand(Sign);
        ClearSignatureCommand = new MvxCommand(ClearSignature);
        SubmitCommand = new MvxAsyncCommand(SubmitAsync, () => !IsComplete);
        CancelCommand = new MvxCommand(() => _navigator.ToPatientRecord(_id));
        DoneCommand = new MvxCommand(() => _navigator.ToPatientRecord(_id));
    }

    public IMvxCommand<(string Code, bool Value)> AnswerCommand { get; }

    public IMvxCommand SignCommand { get; }

    public IMvxCommand ClearSignatureCommand { get; }

    public IMvxAsyncCommand SubmitCommand { get; }

    public IMvxCommand CancelCommand { get; }

    public IMvxCommand DoneCommand { get; }

    public override void Prepare(Guid parameter) => _id = parameter;

    public override Task Initialize() => LoadAsync();

    public PatientEntity? Patient => _patient;

    public bool NotFound { get; private set; }

    public IReadOnlyList<MedicalHistoryRow> Rows => _rows;

    /// <summary>The questionnaire has been recorded; the screen shows the confirmation.</summary>
    public bool IsComplete => _result is not null;

    public MedicalHistoryResult? Result => _result;

    public string PatientName => _patient?.FullName ?? string.Empty;

    /// <summary>
    /// "last confirmed 14 Jul 2025", or a note that there is nothing on file.
    /// </summary>
    public string HistoryStatus => _lastConfirmedUtc is { } confirmed
        ? $"last confirmed {MolargoFormat.Date(DateOnly.FromDateTime(confirmed.ToLocalTime()))}"
        : "no history on file";

    /// <summary>
    /// The heading. The prototype says "annual re-confirmation", which is wrong for a
    /// patient who has never given one.
    /// </summary>
    public string Subtitle => _lastConfirmedUtc is null
        ? "MEDICAL HISTORY"
        : "MEDICAL HISTORY — ANNUAL RE-CONFIRMATION";

    // ---- signature -------------------------------------------------------

    public bool IsSigned => _signedByName is { Length: > 0 };

    public string? SignedByName => _signedByName;

    public string SignedByRelationship
    {
        get => _signedByRelationship;
        set
        {
            if (!SetProperty(ref _signedByRelationship, value)) return;

            // Changing who is signing clears the signature. A declaration is made by a
            // specific person: signing as the patient and then switching to "Parent"
            // would leave the patient's name attached to a parent's declaration, which
            // misattributes the one field on this form that carries any weight.
            _signedByName = null;

            RaisePropertyChanged(nameof(IsSigningForSelf));
            RaiseSignatureState();
        }
    }

    public bool IsSigningForSelf => _signedByRelationship == PatientRelationship;

    public string? AdditionalNotes
    {
        get => _additionalNotes;
        set => SetProperty(ref _additionalNotes, value);
    }

    // ---- validation ------------------------------------------------------

    /// <summary>Questions still unanswered. Every one has to be answered before signing.</summary>
    public int UnansweredCount => _rows.Count(row => row.YesNo is null);

    /// <summary>"Yes" answers whose detail box is still empty.</summary>
    public int MissingDetailCount => _rows.Count(row => row.NeedsDetail);

    public bool CanSubmit => UnansweredCount == 0 && MissingDetailCount == 0 && IsSigned;

    /// <summary>
    /// Shown only once submit has been pressed. Marking every unanswered question in red
    /// the moment the form opens tells the patient they have done something wrong before
    /// they have done anything at all.
    /// </summary>
    public bool ShowValidation => _showValidation;

    public string? ValidationMessage
    {
        get
        {
            if (!_showValidation || CanSubmit) return null;

            var problems = new List<string>();

            if (UnansweredCount > 0)
            {
                problems.Add(UnansweredCount == 1
                    ? "one question is unanswered"
                    : $"{UnansweredCount} questions are unanswered");
            }

            if (MissingDetailCount > 0) problems.Add("a \"yes\" answer needs details");
            if (!IsSigned) problems.Add("the form needs signing");

            // The problems only. The notice's headline states the outcome now, and
            // this used to lead with "Not submitted —" as well, which rendered as
            // "Not submitted. Not submitted — 7 questions are unanswered."
            if (problems.Count == 0) return null;

            var summary = string.Join(", ", problems);

            return $"{char.ToUpperInvariant(summary[0])}{summary[1..]}.";
        }
    }

    /// <summary>The submit button's label, which tells the user what is missing.</summary>
    public string SubmitLabel => IsSigned
        ? "Submit — updates alerts immediately"
        : "Sign below, then submit";

    public bool IsUnanswered(MedicalHistoryRow row) => _showValidation && row.YesNo is null;

    public bool IsMissingDetail(MedicalHistoryRow row) => _showValidation && row.NeedsDetail;

    private Task LoadAsync() => RunGuardedAsync(async () =>
    {
        var record = await _patients.GetRecordAsync(_id).ConfigureAwait(false);

        if (record is null)
        {
            NotFound = true;
            RaiseAllDerived();
            return;
        }

        _patient = record.Patient;
        _lastConfirmedUtc = record.MedicalHistory?.CompletedUtc;

        // A fresh set of unanswered questions, never pre-filled from the last one.
        //
        // Pre-filling is the obvious convenience and the wrong call: an annual
        // re-confirmation exists to catch what has changed, and a form that arrives
        // already agreeing with last year gets tapped through without being read.
        _rows = MedicalHistoryQuestionnaire.Questions
            .Select(question => new MedicalHistoryRow { Question = question })
            .ToList();

        RaiseAllDerived();
    });

    private void Answer((string Code, bool Value) answer)
    {
        var row = _rows.FirstOrDefault(candidate => candidate.Question.Code == answer.Code);
        if (row is null) return;

        row.YesNo = answer.Value;

        // Changing an answer to "no" drops the detail it no longer applies to, so a
        // patient who answers yes, types something, then corrects themselves does not
        // leave an orphaned detail behind to be recorded as an alert.
        if (!answer.Value) row.Detail = null;

        RaiseAnswerState();
    }

    /// <summary>
    /// Signs the form.
    /// </summary>
    /// <remarks>
    /// Captures the signer's name and the timestamp, which is the part that carries
    /// weight; it does not capture a drawn signature. The prototype renders a script
    /// flourish, and doing that for real needs a canvas with pointer capture and somewhere
    /// to put the resulting image — so <c>MedicalHistoryForm.SignatureImage</c> is left
    /// null rather than filled with something that only looks like one.
    /// </remarks>
    private void Sign()
    {
        _signedByName = _signedByRelationship == PatientRelationship
            ? _patient?.FullName
            : null;

        // Signing on someone else's behalf needs a name typed in, so the field is left
        // empty for them to fill rather than pre-filled with the patient's.
        RaiseSignatureState();
    }

    /// <summary>Sets the signer's name for someone signing on the patient's behalf.</summary>
    public void SetSignedByName(string? name)
    {
        _signedByName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        RaiseSignatureState();
    }

    private void ClearSignature()
    {
        _signedByName = null;
        RaiseSignatureState();
    }

    private Task SubmitAsync() => RunGuardedAsync(async () =>
    {
        _showValidation = true;
        RaiseValidationState();

        if (!CanSubmit) return;

        _result = await _patients
            .SaveMedicalHistoryAsync(new MedicalHistorySubmission(
                _id,
                _rows
                    .Select(row => new MedicalHistoryAnswerInput(
                        row.Question.Code, row.YesNo, row.Detail))
                    .ToList(),
                _signedByName!,
                _signedByRelationship,
                _additionalNotes))
            .ConfigureAwait(false);

        RaiseAllDerived();
        SubmitCommand.RaiseCanExecuteChanged();
    });

    private void RaiseAnswerState()
    {
        RaisePropertyChanged(nameof(Rows));
        RaiseValidationState();
    }

    private void RaiseSignatureState()
    {
        RaisePropertyChanged(nameof(IsSigned));
        RaisePropertyChanged(nameof(SignedByName));
        RaisePropertyChanged(nameof(SubmitLabel));
        RaiseValidationState();
    }

    private void RaiseValidationState()
    {
        RaisePropertyChanged(nameof(UnansweredCount));
        RaisePropertyChanged(nameof(MissingDetailCount));
        RaisePropertyChanged(nameof(CanSubmit));
        RaisePropertyChanged(nameof(ShowValidation));
        RaisePropertyChanged(nameof(ValidationMessage));
    }

    /// <summary>
    /// Everything the screen reads. Listed explicitly rather than raising a blanket
    /// change, which on a form would re-render the box being typed in.
    /// </summary>
    private void RaiseAllDerived()
    {
        foreach (var name in new[]
        {
            nameof(Patient), nameof(NotFound), nameof(Rows), nameof(IsComplete),
            nameof(Result), nameof(PatientName), nameof(HistoryStatus), nameof(Subtitle),
            nameof(IsSigned), nameof(SignedByName), nameof(SubmitLabel),
            nameof(UnansweredCount), nameof(MissingDetailCount), nameof(CanSubmit),
            nameof(ShowValidation), nameof(ValidationMessage), nameof(IsSigningForSelf),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
