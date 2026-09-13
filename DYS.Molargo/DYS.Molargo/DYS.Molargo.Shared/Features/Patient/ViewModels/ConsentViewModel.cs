using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Components;
using DYS.Molargo.Shared.Features.Patient.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Patient.ViewModels;

/// <summary>Which patient, and which consent — empty to raise a new one.</summary>
public readonly record struct ConsentArgs(Guid PatientId, Guid ConsentId);

/// <summary>Where the screen is in the taking of one consent.</summary>
public enum ConsentStage
{
    /// <summary>The clinician settling what is being consented to.</summary>
    Prepare = 0,

    /// <summary>Handed over. The patient reads the wording and signs.</summary>
    Sign = 1,

    /// <summary>Answered, either way.</summary>
    Done = 2,
}

/// <summary>
/// Taking one consent, on its own screen.
/// </summary>
/// <remarks>
/// <para>
/// Separated from the documents tab because of who is holding the device. Raising a
/// consent is a clinician's act and signing it is the patient's, and the inline panel
/// asked one person at one desk to do both — with the rest of the record, the file
/// uploads and every other patient's navigation still on screen while somebody read the
/// risks of their own surgery.
/// </para>
/// <para>
/// So it follows the medical-history questionnaire: a card handed across, large targets,
/// nothing else on the page. The two screens do the same job — put something in front of
/// a patient, have them agree to it, timestamp it — and they should not feel like
/// different products.
/// </para>
/// </remarks>
public sealed class ConsentViewModel : BaseViewModel<ConsentArgs>
{
    /// <summary>Who can sign, matching the plan presentation and the questionnaire.</summary>
    public static readonly string[] Relationships =
        [PatientViewModel.SelfRelationship, "Parent", "Guardian", "Carer"];

    private readonly IPatientService _patients;
    private readonly IAppNavigator _navigator;

    private Guid _patientId;
    private Guid _consentId;

    private PatientRecord? _record;
    private IReadOnlyList<ConsentTemplate> _templates = [];
    private ConsentForm? _consent;

    private ConsentStage _stage = ConsentStage.Prepare;
    private bool _notFound;

    private Guid? _templateId;
    private string? _title;
    private string? _body;

    private string _relationship = PatientViewModel.SelfRelationship;
    private string? _signedBy;
    private Guid? _documentId;
    private string? _signatureImage;

    private string? _refusal;
    private bool _wasRefused;

    public ConsentViewModel(IPatientService patients, IAppNavigator navigator)
    {
        _patients = patients;
        _navigator = navigator;

        // Built once, in the constructor — never rebuilt per render.
        SelectTemplateCommand = new MvxCommand<Guid>(SelectTemplate);
        HandOverCommand = new MvxAsyncCommand(HandOverAsync);
        BackToPrepareCommand = new MvxCommand(() => SetStage(ConsentStage.Prepare));
        SelectRelationshipCommand = new MvxCommand<string>(SelectRelationship);
        SelectDocumentCommand = new MvxCommand<Guid>(SelectDocument);
        SignCommand = new MvxCommand(Sign);
        ClearSignatureCommand = new MvxCommand(ClearSignature);
        ConfirmCommand = new MvxAsyncCommand(ConfirmAsync);
        RefuseCommand = new MvxAsyncCommand(RefuseAsync);
        CancelCommand = new MvxCommand(BackToRecord);
        DoneCommand = new MvxCommand(BackToRecord);
    }

    public IMvxCommand<Guid> SelectTemplateCommand { get; }

    /// <summary>Raises the form and hands the device to the patient.</summary>
    public IMvxAsyncCommand HandOverCommand { get; }

    public IMvxCommand BackToPrepareCommand { get; }

    public IMvxCommand<string> SelectRelationshipCommand { get; }

    /// <summary>Picks the scanned original, where they signed on paper.</summary>
    public IMvxCommand<Guid> SelectDocumentCommand { get; }

    public IMvxCommand SignCommand { get; }

    public IMvxCommand ClearSignatureCommand { get; }

    public IMvxAsyncCommand ConfirmCommand { get; }

    /// <summary>The patient said no. A refusal is an answer, not an absence.</summary>
    public IMvxAsyncCommand RefuseCommand { get; }

    public IMvxCommand CancelCommand { get; }

    public IMvxCommand DoneCommand { get; }

    public override void Prepare(ConsentArgs parameter)
    {
        _patientId = parameter.PatientId;
        _consentId = parameter.ConsentId;
    }

    public override Task Initialize() => LoadAsync();

    // ---- what the view binds --------------------------------------------

    public bool NotFound => _notFound;

    public ConsentStage Stage => _stage;

    public bool IsPreparing => _stage == ConsentStage.Prepare;

    public bool IsSigning => _stage == ConsentStage.Sign;

    public bool IsDone => _stage == ConsentStage.Done;

    /// <summary>True where the screen opened on a form somebody else already raised.</summary>
    public bool IsExisting => _consentId != Guid.Empty;

    public string PatientName => _record?.Patient.FullName ?? "Patient";

    public string Subtitle => IsExisting ? "CONSENT" : "NEW CONSENT";

    public IReadOnlyList<ConsentTemplate> Templates => _templates;

    public bool IsTemplate(Guid templateId) => _templateId == templateId;

    public string? Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string? Body
    {
        get => _body;
        set => SetProperty(ref _body, value);
    }

    public string Relationship => _relationship;

    public bool IsRelationship(string value) =>
        string.Equals(_relationship, value, StringComparison.OrdinalIgnoreCase);

    public bool IsSigningForSelf =>
        string.Equals(_relationship, PatientViewModel.SelfRelationship, StringComparison.OrdinalIgnoreCase);

    public string? SignedByName
    {
        get => _signedBy;
        set => SetProperty(ref _signedBy, value);
    }

    /// <summary>
    /// The drawn signature, as a PNG data URI.
    /// </summary>
    /// <remarks>
    /// Holding the image rather than a bool is what makes "signed" mean something. The
    /// flag it replaced was set by a button press, so a consent could be signed by
    /// somebody who had drawn nothing — the state said signed and the record held no
    /// signature.
    /// </remarks>
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

    public bool IsSigned => _signatureImage is { Length: > 0 };

    /// <summary>The uploads that could be a signed original — the ones filed as consent.</summary>
    public IReadOnlyList<PatientDocument> Scans =>
        _record?.Documents.Where(document => document.Kind == DocumentKind.Consent).ToList() ?? [];

    public bool IsDocument(Guid documentId) => _documentId == documentId;

    public string? Refusal => _refusal;

    /// <summary>True where the answer recorded was "no", which reads differently.</summary>
    public bool WasRefused => _wasRefused;

    public string DoneHeadline => _wasRefused ? "Consent refused." : "Consent recorded.";

    public string DoneMessage => _wasRefused
        ? "Kept on the record as a refusal, not as a gap. Treatment must not go ahead on "
            + "this consent, and a clinician decides what happens next."
        : "Timestamped and locked to the wording above, witnessed by whoever is signed in. "
            + "It can be withdrawn later, which keeps the signature and stops it "
            + "authorising treatment.";

    /// <summary>
    /// What the patient is being asked to agree to, for the card header.
    /// </summary>
    public string Heading => _title is { Length: > 0 } title ? title : "Consent";

    private void SetStage(ConsentStage stage)
    {
        _stage = stage;
        _refusal = null;

        RaiseAll();
    }

    private async Task LoadAsync() => await RunGuardedAsync(async () =>
    {
        _record = await _patients.GetRecordAsync(_patientId).ConfigureAwait(false);

        if (_record is null)
        {
            _notFound = true;
            RaiseAll();
            return;
        }

        _templates = await _patients.GetConsentTemplatesAsync().ConfigureAwait(false);

        if (_consentId != Guid.Empty)
        {
            _consent = _record.Consents.FirstOrDefault(form => form.Id == _consentId);

            if (_consent is null)
            {
                _notFound = true;
                RaiseAll();
                return;
            }

            _title = _consent.Title;
            _body = _consent.Body;

            // Straight to signing. The wording was settled when the form was raised —
            // usually by booking the visit — and re-opening the compose step would invite
            // an edit to a consent somebody is already standing there to sign.
            _stage = ConsentStage.Sign;
        }

        RaiseAll();
    }).ConfigureAwait(false);

    /// <summary>Copies a template's wording in, or clears it when the same one is chosen again.</summary>
    /// <remarks>
    /// Copied into editable fields rather than referenced. A clinician who discussed
    /// something extra has to be able to say so before the patient signs; wording that
    /// could not be edited would push that into a note nobody signs.
    /// </remarks>
    private void SelectTemplate(Guid templateId)
    {
        if (_templateId == templateId)
        {
            _templateId = null;
            RaiseAll();
            return;
        }

        if (_templates.FirstOrDefault(template => template.Id == templateId) is not { } picked)
        {
            return;
        }

        // A title somebody typed survives. Only a blank one, or one still holding another
        // template's name, is replaced.
        var typed = _title is { Length: > 0 }
            && !_templates.Any(template =>
                string.Equals(template.Name, _title, StringComparison.OrdinalIgnoreCase));

        if (!typed) _title = picked.Name;

        _body = picked.Body;
        _templateId = templateId;

        RaiseAll();
    }

    private Task HandOverAsync() => RunGuardedAsync(async () =>
    {
        var refusal = await _patients
            .AddConsentAsync(_patientId, _title ?? string.Empty, _body)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            _refusal = refusal;
            RaiseAll();
            return;
        }

        // Re-read to find the form that was just written. AddConsentAsync returns only a
        // refusal, and the screen needs the id to sign against.
        await ReloadAsync().ConfigureAwait(false);

        _consent = _record?.Consents
            .Where(form => form.Status == ConsentStatus.Pending)
            .OrderByDescending(form => form.CreatedUtc)
            .FirstOrDefault();

        if (_consent is null)
        {
            _refusal = "The consent was raised but could not be reopened to sign. It is on "
                + "the record under Documents.";

            RaiseAll();
            return;
        }

        _consentId = _consent.Id;
        _stage = ConsentStage.Sign;

        RaiseAll();
    });

    private void SelectRelationship(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        _relationship = value;

        // A signature stands for a name. Changing who is signing invalidates it, and
        // leaving it would attribute the patient's own signature to their parent.
        _signatureImage = null;

        RaiseAll();
    }

    /// <summary>Picks the scan, or unpicks it when the same row is chosen again.</summary>
    private void SelectDocument(Guid documentId)
    {
        _documentId = _documentId == documentId ? null : documentId;

        RaisePropertyChanged(nameof(IsDocument));
    }

    /// <summary>
    /// Fills in who is signing, now that something has been drawn.
    /// </summary>
    /// <remarks>
    /// Signing for themselves puts the patient's own name on it, which is the name the
    /// record already holds. Anybody else types theirs, and the pad alone is not enough:
    /// a drawn squiggle with no name against it identifies nobody.
    /// </remarks>
    private void Sign()
    {
        if (IsSigningForSelf && string.IsNullOrWhiteSpace(_signedBy))
        {
            _signedBy = _record?.Patient.FullName;
        }

        RaiseAll();
    }

    private void ClearSignature()
    {
        _signatureImage = null;

        RaiseAll();
    }

    private Task ConfirmAsync() => RunGuardedAsync(async () =>
    {
        if (_consent is null) return;

        // Checked here rather than by greying the button. Refused in the patient's own
        // terms, because the patient is the one reading it.
        if (!IsSigned)
        {
            _refusal = "Sign in the box above before recording your consent.";

            RaiseAll();
            return;
        }

        // Both, always. The drawing is what a patient recognises as signing; the name is
        // what identifies them. A squiggle on its own names nobody, and for a parent
        // signing for a child that is the whole question.
        if (string.IsNullOrWhiteSpace(_signedBy))
        {
            _refusal = "Type the name of the person signing.";

            RaiseAll();
            return;
        }

        // Nothing to agree to. A consent raised without wording would be signed against a
        // blank page, which looks like a consent and is not one.
        if (string.IsNullOrWhiteSpace(_body))
        {
            _refusal = "This consent has no wording on it, so there is nothing to agree "
                + "to. A clinician has to write what was discussed first.";

            RaiseAll();
            return;
        }

        var refusal = await _patients
            .SignConsentAsync(
                _consent.Id, _signedBy, _relationship, _documentId, _signatureImage)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            _refusal = refusal;
            RaiseAll();
            return;
        }

        _wasRefused = false;
        _stage = ConsentStage.Done;

        await ReloadAsync().ConfigureAwait(false);

        RaiseAll();
    });

    private Task RefuseAsync() => RunGuardedAsync(async () =>
    {
        if (_consent is null) return;

        var refusal = await _patients
            .CloseConsentAsync(_consent.Id, withdrawn: false)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            _refusal = refusal;
            RaiseAll();
            return;
        }

        _wasRefused = true;
        _stage = ConsentStage.Done;

        await ReloadAsync().ConfigureAwait(false);

        RaiseAll();
    });

    /// <summary>
    /// Re-reads the record.
    /// </summary>
    /// <remarks>
    /// Unguarded, and called only from inside guarded commands. RunGuardedAsync returns
    /// silently while IsBusy, so a guarded reload nested in a guarded caller does nothing
    /// at all — the database is right and the screen is not.
    /// </remarks>
    private async Task ReloadAsync() =>
        _record = await _patients.GetRecordAsync(_patientId).ConfigureAwait(false);

    private void BackToRecord() =>
        _navigator.ToPatientRecord(_patientId, "documents");

    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(NotFound), nameof(Stage), nameof(IsPreparing), nameof(IsSigning),
            nameof(IsDone), nameof(IsExisting), nameof(PatientName), nameof(Subtitle),
            nameof(Templates), nameof(Title), nameof(Body), nameof(Relationship),
            nameof(IsSigningForSelf), nameof(SignedByName), nameof(IsSigned),
            nameof(SignatureImage),
            nameof(Scans), nameof(Refusal), nameof(WasRefused), nameof(DoneHeadline),
            nameof(DoneMessage), nameof(Heading),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
