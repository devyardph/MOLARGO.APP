using DYS.Molargo.Domain.Dtos;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Components;
using DYS.Molargo.Shared.Features.Patient.Services;
using DYS.Molargo.Shared.Features.Prescribing.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Features.Prescribing.ViewModels;

/// <summary>Which pane of the Rx &amp; referrals screen is showing.</summary>
public enum RxTab
{
    NewPrescription = 0,
    History = 1,
    Outbound = 2,
    Inbound = 3,
    Certificates = 4,

    /// <summary>
    /// The practice formulary itself, rather than a document about a patient.
    /// </summary>
    /// <remarks>
    /// Here rather than under Admin because it is the prescriber's list, not the practice's
    /// configuration: the person who notices the directions are wrong is the one writing
    /// the script, and they are already on this screen.
    /// </remarks>
    Formulary = 5,
}

/// <summary>
/// Prescriptions, referral letters and certificates for one patient.
/// </summary>
/// <remarks>
/// Patient-scoped, like the design's screen — its header names the patient and their
/// allergies, because everything on it is a document written about one person. Inbound
/// referrals are the exception: they arrive before anyone has picked a patient, so that
/// pane is practice-wide.
/// </remarks>
public sealed class PrescribingViewModel : BaseViewModel, IDisposable
{
    private readonly IPrescribingService _prescribing;
    private readonly IReferralService _referrals;
    private readonly IPatientService _patients;
    private readonly ISessionService _session;
    private readonly ITenantContext _tenant;
    private readonly IAppNavigator _navigator;

    private RxTab _tab = RxTab.NewPrescription;

    private Guid _patientId;
    private PatientEntity? _patient;
    private IReadOnlyList<PatientAlert> _alerts = [];

    private string _patientSearch = string.Empty;
    private IReadOnlyList<PatientListItemDto> _searchResults = [];

    private IReadOnlyList<FormularyMedicine> _formulary = [];
    private string _formularySearch = string.Empty;
    private PrescriptionDraft? _draft;
    private string? _overrideReason;
    private bool _issued;
    /// <summary>
    /// The script currently shown as a printable sheet, or null while none is.
    /// </summary>
    /// <remarks>
    /// Its own copy rather than a flag over the draft. The sheet is opened from two places
    /// — the script just issued, and one picked out of the history — and a flag would have
    /// made the second show the first.
    /// </remarks>
    private PrescriptionDraft? _scriptShown;

    private IReadOnlyList<PrescriptionSummary> _history = [];
    private PrescriptionDraft? _selectedScript;

    private IReadOnlyList<ReferralRow> _outbound = [];
    private IReadOnlyList<ReferralRow> _inbound = [];
    private Referral? _letter;

    private IReadOnlyList<MedicalCertificate> _certificates = [];
    private MedicalCertificate? _certificateDraft;
    private int _certificateDays = 1;
    private bool _certificateForStudy;

    // ---- managing the formulary ------------------------------------------
    private IReadOnlyList<FormularyMedicine> _allMedicines = [];
    private string _manageSearch = string.Empty;
    private Guid _editingMedicineId;
    private bool _isEditingMedicine;
    private string? _medicineAction;

    private string? _mGeneric;
    private string? _mBrand;
    private string? _mStrength;
    private string? _mForm;
    private MedicineClass _mClass = MedicineClass.Other;
    private string? _mDirections;
    private int _mQuantity = 1;
    private int _mRepeats;
    private string? _mAllergyClasses;
    private string? _mInteractsWith;
    private string? _mInteractionCaution;
    private string? _mConditionCautions;
    private string? _mConditionCaution;

    public PrescribingViewModel(
        IPrescribingService prescribing,
        IReferralService referrals,
        IPatientService patients,
        ISessionService session,
        ITenantContext tenant,
        IAppNavigator navigator)
    {
        _prescribing = prescribing;
        _referrals = referrals;
        _patients = patients;
        _session = session;
        _tenant = tenant;
        _navigator = navigator;

        // Built once, in the constructor — never rebuilt per render.
        SelectTabCommand = new MvxAsyncCommand<RxTab>(SelectTabAsync);
        SearchPatientsCommand = new MvxAsyncCommand(SearchPatientsAsync);
        ChoosePatientCommand = new MvxAsyncCommand<Guid>(ChoosePatientAsync);
        ClearPatientCommand = new MvxAsyncCommand(ClearPatientAsync);
        OpenPatientCommand = new MvxCommand(OpenPatient);

        AddMedicineCommand = new MvxAsyncCommand<Guid>(AddMedicineAsync);
        RemoveItemCommand = new MvxAsyncCommand<Guid>(RemoveItemAsync);
        UpdateItemCommand = new MvxAsyncCommand<ItemEdit>(edit => UpdateItemAsync(edit!));
        ResetItemCommand = new MvxAsyncCommand<Guid>(ResetItemAsync);
        IssueCommand = new MvxAsyncCommand(IssueAsync);
        StartAnotherCommand = new MvxAsyncCommand(StartAnotherAsync);
        EditPrescriptionCommand = new MvxAsyncCommand(EditPrescriptionAsync);
        OpenScriptCommand = new MvxAsyncCommand<Guid>(OpenScriptAsync);
        CloseScriptCommand = new MvxCommand(CloseScript);
        ClearSignatureCommand = new MvxAsyncCommand(ClearSignatureAsync);

        ViewScriptCommand = new MvxAsyncCommand<Guid>(ViewScriptAsync);
        ReissueCommand = new MvxAsyncCommand<Guid>(ReissueAsync);

        DraftReferralCommand = new MvxAsyncCommand<string>(key => DraftReferralAsync(key!));
        SaveLetterCommand = new MvxAsyncCommand(SaveLetterAsync);
        SendReferralCommand = new MvxAsyncCommand(SendReferralAsync);
        OpenLetterCommand = new MvxAsyncCommand<Guid>(OpenLetterAsync);
        MarkReportReceivedCommand = new MvxAsyncCommand<Guid>(MarkReportReceivedAsync);

        DraftCertificateCommand = new MvxAsyncCommand(DraftCertificateAsync);
        IssueCertificateCommand = new MvxAsyncCommand(IssueCertificateAsync);

        NewMedicineCommand = new MvxCommand(NewMedicine);
        EditMedicineCommand = new MvxCommand<Guid>(EditMedicine);
        CancelMedicineCommand = new MvxCommand(CancelMedicine);
        SaveMedicineCommand = new MvxAsyncCommand(SaveMedicineAsync);
        SetMedicineActiveCommand = new MvxAsyncCommand<Guid>(SetMedicineActiveAsync);
        MoveMedicineCommand = new MvxAsyncCommand<MedicineMove>(move => MoveMedicineAsync(move!));

        _session.Changed += OnSessionChanged;
    }

    public IMvxAsyncCommand<RxTab> SelectTabCommand { get; }

    public IMvxAsyncCommand SearchPatientsCommand { get; }

    public IMvxAsyncCommand<Guid> ChoosePatientCommand { get; }

    public IMvxAsyncCommand ClearPatientCommand { get; }

    public IMvxCommand OpenPatientCommand { get; }

    public IMvxAsyncCommand<Guid> AddMedicineCommand { get; }

    public IMvxAsyncCommand<Guid> RemoveItemCommand { get; }

    public IMvxAsyncCommand<ItemEdit> UpdateItemCommand { get; }

    public IMvxAsyncCommand<Guid> ResetItemCommand { get; }

    public IMvxAsyncCommand IssueCommand { get; }

    public IMvxAsyncCommand StartAnotherCommand { get; }

    /// <summary>Puts the script just issued back into draft so it can be corrected.</summary>
    public IMvxAsyncCommand EditPrescriptionCommand { get; }

    /// <summary>Opens one script as a printable sheet, where the prescriber signs it.</summary>
    public IMvxAsyncCommand<Guid> OpenScriptCommand { get; }

    public IMvxCommand CloseScriptCommand { get; }

    public IMvxAsyncCommand ClearSignatureCommand { get; }

    public IMvxAsyncCommand<Guid> ViewScriptCommand { get; }

    public IMvxAsyncCommand<Guid> ReissueCommand { get; }

    public IMvxAsyncCommand<string> DraftReferralCommand { get; }

    public IMvxAsyncCommand SaveLetterCommand { get; }

    public IMvxAsyncCommand SendReferralCommand { get; }

    public IMvxAsyncCommand<Guid> OpenLetterCommand { get; }

    public IMvxAsyncCommand<Guid> MarkReportReceivedCommand { get; }

    public IMvxAsyncCommand DraftCertificateCommand { get; }

    public IMvxAsyncCommand IssueCertificateCommand { get; }

    public IMvxCommand NewMedicineCommand { get; }

    public IMvxCommand<Guid> EditMedicineCommand { get; }

    public IMvxCommand CancelMedicineCommand { get; }

    public IMvxAsyncCommand SaveMedicineCommand { get; }

    public IMvxAsyncCommand<Guid> SetMedicineActiveCommand { get; }

    public IMvxAsyncCommand<MedicineMove> MoveMedicineCommand { get; }

    public override Task Initialize() => LoadAsync();

    public RxTab Tab => _tab;

    // ---- the patient the documents are about -----------------------------

    public bool HasPatient => _patient is not null;

    public string PatientName => _patient?.FullName ?? string.Empty;

    /// <summary>
    /// "Margaret Yuen · allergies Penicillin, Latex · Warfarin 5mg" — the design's header.
    /// </summary>
    /// <remarks>
    /// Allergies in the header of every pane, not only on the prescribing one. This is the
    /// screen where a missed allergy becomes a prescription, and the fact belongs where it
    /// cannot be scrolled past.
    /// </remarks>
    public string PatientBanner
    {
        get
        {
            if (_patient is null) return "No patient chosen";

            var parts = new List<string> { _patient.FullName };

            var allergies = _alerts
                .Where(alert => alert.Kind == AlertKind.Allergy)
                .Select(alert => alert.Summary)
                .ToList();

            parts.Add(allergies.Count > 0
                ? $"allergies {string.Join(", ", allergies)}"
                : "no recorded allergies");

            var medicines = _alerts
                .Where(alert => alert.Kind == AlertKind.Medication)
                .Select(alert => alert.Summary)
                .ToList();

            if (medicines.Count > 0) parts.Add(string.Join(", ", medicines));

            return string.Join(" · ", parts);
        }
    }

    public bool HasAllergies => _alerts.Any(alert => alert.Kind == AlertKind.Allergy);

    public string PatientSearch
    {
        get => _patientSearch;
        set => SetProperty(ref _patientSearch, value);
    }

    public IReadOnlyList<PatientListItemDto> SearchResults => _searchResults;

    // ---- new prescription -----------------------------------------------

    public string FormularySearch
    {
        get => _formularySearch;
        set
        {
            if (SetProperty(ref _formularySearch, value)) RaisePropertyChanged(nameof(Formulary));
        }
    }

    /// <summary>The formulary, filtered by whatever is typed in the search box.</summary>
    public IReadOnlyList<FormularyMedicine> Formulary
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_formularySearch)) return _formulary;

            var term = _formularySearch.Trim();

            return _formulary
                .Where(medicine =>
                    medicine.GenericName.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (medicine.BrandName?.Contains(term, StringComparison.OrdinalIgnoreCase)
                        ?? false))
                .ToList();
        }
    }

    public PrescriptionDraft? Draft => _draft;

    public IReadOnlyList<DraftItem> DraftItems => _draft?.Items ?? [];

    public IReadOnlyList<PrescribingCheck> Checks => _draft?.Checks ?? [];

    public bool IsDraftEmpty => _draft?.IsEmpty ?? true;

    public bool IsBlocked => _draft?.IsBlocked ?? false;

    /// <summary>Whether an item is already on the draft, so the list can say so.</summary>
    public bool IsOnDraft(FormularyMedicine medicine) => DraftItems
        .Any(item => item.MedicineName == medicine.GenericName
            && item.Strength == medicine.Strength);

    public string? OverrideReason
    {
        get => _overrideReason;
        set => SetProperty(ref _overrideReason, value);
    }

    public bool WasIssued => _issued;

    public string IssueLabel => IsBlocked ? "Issue anyway — with reason" : "Issue prescription";

    // ---- what the printed script carries ---------------------------------

    /// <summary>
    /// The heading on the printed script.
    /// </summary>
    /// <remarks>
    /// The clinic's trading name, which is what a pharmacist checks the prescriber against.
    /// Not a letterhead: this app has no practice address or provider number to put on one,
    /// and inventing the shape of a legal script would be worse than printing a plain,
    /// obviously-internal record of what was written.
    /// </remarks>
    public string PracticeName => _tenant.TenantName ?? "Molargo";

    public string PrescriberName => _session.UserDisplayName ?? "Unknown prescriber";

    /// <summary>"4 March 1961 (64)" — what identifies a patient on a script.</summary>
    public string PatientDateOfBirth => _patient?.DateOfBirth is { } dob
        ? $"{dob:d MMMM yyyy}"
        : "date of birth not recorded";

    public string PatientAddress => _patient is null
        ? string.Empty
        : string.Join(", ", new[] { _patient.AddressLine, _patient.Suburb, _patient.Postcode }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>When the script on screen was issued, in local time.</summary>
    public string IssuedOn => _scriptShown?.Prescription.IssuedUtc is { } when
        ? when.ToLocalTime().ToString("d MMMM yyyy")
        : string.Empty;

    public string ValidUntil => _scriptShown?.Prescription.ValidUntil is { } until
        ? until.ToString("d MMMM yyyy")
        : string.Empty;

    /// <summary>
    /// True where the script just issued can still be taken back.
    /// </summary>
    /// <remarks>
    /// Issued and undispensed. The button is only on this screen — the one that has just
    /// issued it — because the case it exists for is reading the script back and seeing the
    /// wrong quantity, not revisiting a script from last month.
    /// </remarks>
    public bool CanEditIssued =>
        _issued
        && _draft?.Prescription is { Status: PrescriptionStatus.Issued, DispensedUtc: null };

    /// <summary>True while the printable script is on screen instead of the panes.</summary>
    public bool IsScriptOpen => _scriptShown is not null;

    /// <summary>The medicines on the sheet, which is not always the draft being written.</summary>
    public IReadOnlyList<DraftItem> ScriptItems => _scriptShown?.Items ?? [];

    /// <summary>The prescriber's signature on the script, as a PNG data URI.</summary>
    public string? SignatureImage => _scriptShown?.Prescription.PrescriberSignature;

    public bool IsScriptSigned => _scriptShown?.Prescription.IsSigned ?? false;

    /// <summary>"Signed 4 March 2026, 2:14 pm" — under the signature on the script.</summary>
    public string SignedOn => _scriptShown?.Prescription.SignedUtc is { } when
        ? when.ToLocalTime().ToString("d MMMM yyyy, h:mm tt")
        : string.Empty;

    // ---- history ---------------------------------------------------------

    public IReadOnlyList<PrescriptionSummary> History => _history;

    public PrescriptionDraft? SelectedScript => _selectedScript;

    public bool HasSelectedScript => _selectedScript is not null;

    /// <summary>"Issued 12 Aug 2026 · Dr Vance · 2 items" — the script detail heading.</summary>
    public string SelectedScriptMeta
    {
        get
        {
            if (_selectedScript is not { } script) return string.Empty;

            var row = _history.FirstOrDefault(entry =>
                entry.PrescriptionId == script.Prescription.Id);

            var issued = row?.IssuedOn is { } on ? MolargoFormat.Date(on) : "not issued";

            var items = script.Items.Count == 1 ? "1 item" : $"{script.Items.Count} items";

            return $"Issued {issued} · {row?.PrescriberName ?? "unknown"} · {items}";
        }
    }

    // ---- referrals -------------------------------------------------------

    public IReadOnlyList<ReferralTemplate> Templates => _referrals.Templates;

    public IReadOnlyList<ReferralRow> Outbound => _outbound;

    public IReadOnlyList<ReferralRow> Inbound => _inbound;

    public Referral? Letter => _letter;

    public bool HasLetter => _letter is not null;

    public bool IsLetterSent => _letter?.SentUtc is not null;

    public string? LetterBody
    {
        get => _letter?.LetterBody;
        set
        {
            if (_letter is null) return;

            _letter.LetterBody = value;
            RaisePropertyChanged();
        }
    }

    public string LetterCounterparty
    {
        get => _letter?.CounterpartyName ?? string.Empty;
        set
        {
            if (_letter is null) return;

            _letter.CounterpartyName = value;
            RaisePropertyChanged();
        }
    }

    /// <summary>Outbound referrals sent with no report back — the ones needing chasing.</summary>
    public int AwaitingReportCount => _outbound.Count(row => row.IsAwaitingReport);

    // ---- certificates ----------------------------------------------------

    public IReadOnlyList<MedicalCertificate> Certificates => _certificates;

    public MedicalCertificate? CertificateDraft => _certificateDraft;

    public bool HasCertificateDraft => _certificateDraft is not null;

    public int CertificateDays
    {
        get => _certificateDays;
        set => SetProperty(ref _certificateDays, value);
    }

    public bool CertificateForStudy
    {
        get => _certificateForStudy;
        set => SetProperty(ref _certificateForStudy, value);
    }

    /// <summary>The days a certificate may cover. Capped — see the service.</summary>
    public static readonly int[] DayOptions = [1, 2, 3, 4, 5];

    // ---- loading ---------------------------------------------------------


    // ---- managing the formulary ------------------------------------------

    /// <summary>Every medicine on the list, retired ones included, filtered by the search.</summary>
    public IReadOnlyList<FormularyMedicine> AllMedicines
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_manageSearch)) return _allMedicines;

            var term = _manageSearch.Trim();

            return _allMedicines
                .Where(medicine =>
                    medicine.GenericName.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (medicine.BrandName?.Contains(term, StringComparison.OrdinalIgnoreCase)
                        ?? false))
                .ToList();
        }
    }

    public string ManageSearch
    {
        get => _manageSearch;
        set
        {
            if (SetProperty(ref _manageSearch, value)) RaisePropertyChanged(nameof(AllMedicines));
        }
    }

    /// <summary>How many of the list a prescriber can actually reach.</summary>
    public int ActiveMedicineCount => _allMedicines.Count(medicine => medicine.IsActive);

    /// <summary>
    /// How many carry no allergy families.
    /// </summary>
    /// <remarks>
    /// Surfaced on the screen because the consequence is invisible from anywhere else: a
    /// medicine with this field empty is prescribed with no allergy screen at all, and the
    /// script says "checked" either way.
    /// </remarks>
    public int UnscreenedMedicineCount => _allMedicines
        .Count(medicine => medicine.IsActive
            && string.IsNullOrWhiteSpace(medicine.AllergyClasses));

    public bool IsEditingMedicine => _isEditingMedicine;

    public bool IsNewMedicine => _isEditingMedicine && _editingMedicineId == Guid.Empty;

    public string? MedicineAction => _medicineAction;

    /// <summary>The row being edited, or null while adding one.</summary>
    public FormularyMedicine? EditingMedicine => _editingMedicineId == Guid.Empty
        ? null
        : _allMedicines.FirstOrDefault(medicine => medicine.Id == _editingMedicineId);

    public string MedicineClasses => string.Join(", ", MedicineClassOptions.Select(ClassLabel));

    public static IReadOnlyList<MedicineClass> MedicineClassOptions { get; } =
        Enum.GetValues<MedicineClass>();

    /// <summary>The words a prescriber uses for a medicine class.</summary>
    public static string ClassLabel(MedicineClass value) => value switch
    {
        MedicineClass.AntiInflammatory => "Anti-inflammatory",
        _ => value.ToString(),
    };

    public string? MedicineGenericName
    {
        get => _mGeneric;
        set { if (SetProperty(ref _mGeneric, value)) RaisePropertyChanged(nameof(CanSaveMedicine)); }
    }

    public string? MedicineBrandName
    {
        get => _mBrand;
        set => SetProperty(ref _mBrand, value);
    }

    public string? MedicineStrength
    {
        get => _mStrength;
        set { if (SetProperty(ref _mStrength, value)) RaisePropertyChanged(nameof(CanSaveMedicine)); }
    }

    public string? MedicineForm
    {
        get => _mForm;
        set => SetProperty(ref _mForm, value);
    }

    public MedicineClass MedicineClassValue
    {
        get => _mClass;
        set => SetProperty(ref _mClass, value);
    }

    public string? MedicineDirections
    {
        get => _mDirections;
        set { if (SetProperty(ref _mDirections, value)) RaisePropertyChanged(nameof(CanSaveMedicine)); }
    }

    public int MedicineQuantity
    {
        get => _mQuantity;
        set { if (SetProperty(ref _mQuantity, value)) RaisePropertyChanged(nameof(CanSaveMedicine)); }
    }

    public int MedicineRepeats
    {
        get => _mRepeats;
        set => SetProperty(ref _mRepeats, value);
    }

    /// <summary>
    /// The allergy families this medicine belongs to, semicolon separated.
    /// </summary>
    /// <remarks>
    /// The field that decides whether a script is screened at all. Empty means no allergy
    /// check runs for this medicine, which the screen says out loud rather than leaving to
    /// be discovered.
    /// </remarks>
    public string? MedicineAllergyClasses
    {
        get => _mAllergyClasses;
        set
        {
            if (SetProperty(ref _mAllergyClasses, value))
            {
                RaisePropertyChanged(nameof(MedicineScreensNothing));
            }
        }
    }

    public string? MedicineInteractsWith
    {
        get => _mInteractsWith;
        set => SetProperty(ref _mInteractsWith, value);
    }

    public string? MedicineInteractionCaution
    {
        get => _mInteractionCaution;
        set => SetProperty(ref _mInteractionCaution, value);
    }

    public string? MedicineConditionCautions
    {
        get => _mConditionCautions;
        set => SetProperty(ref _mConditionCautions, value);
    }

    public string? MedicineConditionCaution
    {
        get => _mConditionCaution;
        set => SetProperty(ref _mConditionCaution, value);
    }

    /// <summary>True where this medicine would be prescribed with no allergy screen.</summary>
    public bool MedicineScreensNothing => string.IsNullOrWhiteSpace(_mAllergyClasses);

    public bool CanSaveMedicine =>
        !IsBusy
        && !string.IsNullOrWhiteSpace(_mGeneric)
        && !string.IsNullOrWhiteSpace(_mStrength)
        && !string.IsNullOrWhiteSpace(_mDirections)
        && _mQuantity >= 1;
    private Task LoadAsync() => RunGuardedAsync(async () =>
    {
        _formulary = await _prescribing.GetFormularyAsync().ConfigureAwait(false);
        _inbound = await _referrals.GetInboundAsync().ConfigureAwait(false);

        RaiseAll();
    });

    private async Task SelectTabAsync(RxTab tab)
    {
        _tab = tab;

        await RaisePropertyChanged(nameof(Tab)).ConfigureAwait(false);
        await LoadTabAsync().ConfigureAwait(false);
    }

    private Task LoadTabAsync() => RunGuardedAsync(async () =>
    {
        switch (_tab)
        {
            case RxTab.History when _patient is not null:
                _history = await _prescribing.GetHistoryAsync(_patientId).ConfigureAwait(false);
                break;

            case RxTab.Outbound when _patient is not null:
                _outbound = await _referrals.GetOutboundAsync(_patientId).ConfigureAwait(false);
                break;

            case RxTab.Inbound:
                _inbound = await _referrals.GetInboundAsync().ConfigureAwait(false);
                break;

            case RxTab.Formulary:
                _allMedicines = await _prescribing
                    .GetAllFormularyAsync()
                    .ConfigureAwait(false);
                break;

            case RxTab.Certificates when _patient is not null:
                _certificates = await _referrals
                    .GetCertificatesAsync(_patientId)
                    .ConfigureAwait(false);
                break;
        }

        RaiseAll();
    });

    private Task SearchPatientsAsync() => RunGuardedAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(_patientSearch))
        {
            _searchResults = [];
        }
        else
        {
            var page = await _patients
                .SearchAsync(new PatientQuery { SearchTerm = _patientSearch.Trim(), PageSize = 8 })
                .ConfigureAwait(false);

            _searchResults = page.Items;
        }

        await RaisePropertyChanged(nameof(SearchResults)).ConfigureAwait(false);
    });

    private Task ChoosePatientAsync(Guid patientId) => RunGuardedAsync(async () =>
    {
        var record = await _patients.GetRecordAsync(patientId).ConfigureAwait(false);

        if (record is null) return;

        _patientId = patientId;
        _patient = record.Patient;
        _alerts = record.Alerts;

        _searchResults = [];
        _patientSearch = string.Empty;
        _issued = false;
        _scriptShown = null;
        _selectedScript = null;
        _letter = null;
        _certificateDraft = null;

        _draft = await _prescribing
            .GetOrStartDraftAsync(patientId, _session.ProviderId)
            .ConfigureAwait(false);

        await LoadTabAsync().ConfigureAwait(false);
    });

    private Task ClearPatientAsync() => RunGuardedAsync(async () =>
    {
        _patientId = Guid.Empty;
        _patient = null;
        _alerts = [];
        _draft = null;
        _history = [];
        _outbound = [];
        _certificates = [];
        _selectedScript = null;
        _letter = null;
        _certificateDraft = null;
        _issued = false;
        _scriptShown = null;

        RaiseAll();

        await Task.CompletedTask.ConfigureAwait(false);
    });

    private void OpenPatient()
    {
        if (_patientId != Guid.Empty) _navigator.ToPatientRecord(_patientId);
    }

    // ---- prescribing -----------------------------------------------------

    private Task AddMedicineAsync(Guid medicineId) => RunGuardedAsync(async () =>
    {
        if (_draft is not { } draft) return;

        await _prescribing.AddItemAsync(draft.Prescription.Id, medicineId).ConfigureAwait(false);
        await ReloadDraftAsync().ConfigureAwait(false);
    });

    private Task RemoveItemAsync(Guid itemId) => RunGuardedAsync(async () =>
    {
        await _prescribing.RemoveItemAsync(itemId).ConfigureAwait(false);
        await ReloadDraftAsync().ConfigureAwait(false);
    });

    private Task UpdateItemAsync(ItemEdit edit) => RunGuardedAsync(async () =>
    {
        await _prescribing
            .UpdateItemAsync(edit.ItemId, edit.Directions, edit.Quantity, edit.Repeats)
            .ConfigureAwait(false);

        await ReloadDraftAsync().ConfigureAwait(false);
    });

    private Task ResetItemAsync(Guid itemId) => RunGuardedAsync(async () =>
    {
        await _prescribing.ResetItemAsync(itemId).ConfigureAwait(false);
        await ReloadDraftAsync().ConfigureAwait(false);
    });

    private Task IssueAsync() => RunGuardedAsync(async () =>
    {
        if (_draft is not { } draft) return;

        var refusal = await _prescribing
            .IssueAsync(draft.Prescription.Id, _overrideReason)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _issued = true;
        _overrideReason = null;

        _history = await _prescribing.GetHistoryAsync(_patientId).ConfigureAwait(false);
        _draft = await _prescribing.GetDraftAsync(draft.Prescription.Id).ConfigureAwait(false);

        RaiseAll();
    });


    /// <summary>

    /// <summary>
    /// Shows one script as a printable sheet.
    /// </summary>
    /// <remarks>
    /// Takes an id rather than reading the draft, because it is reached from two places:
    /// the script just issued, and one opened out of the history. Re-read from the service
    /// rather than reusing whichever copy the caller held, so the signature on the sheet is
    /// the one in the database.
    /// </remarks>
    private Task OpenScriptAsync(Guid prescriptionId) => RunGuardedAsync(async () =>
    {
        _scriptShown = await _prescribing.GetDraftAsync(prescriptionId).ConfigureAwait(false);

        ErrorMessage = null;
        RaiseAll();
    });

    private void CloseScript()
    {
        _scriptShown = null;

        ErrorMessage = null;
        RaiseAll();
    }

    /// <summary>
    /// Stores the signature the prescriber just drew.
    /// </summary>
    /// <remarks>
    /// Saved as it is drawn rather than behind a button. The pad reports per stroke and the
    /// script is already issued, so there is nothing to submit — and a signature that
    /// needed a second press is one somebody walks away from having drawn.
    /// </remarks>
    public Task SignAsync(string? signature) => RunGuardedAsync(async () =>
    {
        if (_scriptShown?.Prescription.Id is not { } prescriptionId) return;

        var refusal = await _prescribing
            .SignAsync(prescriptionId, signature)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _scriptShown = await _prescribing.GetDraftAsync(prescriptionId).ConfigureAwait(false);

        // The pane underneath shows the same script where it was just issued.
        if (_draft?.Prescription.Id == prescriptionId) _draft = _scriptShown;

        RaiseAll();
    });

    private Task ClearSignatureAsync() => SignAsync(null);

    /// <summary>
    /// Takes the script just issued back into draft.
    /// </summary>
    /// <remarks>
    /// The screen returns to the editing state it was in a moment ago, findings and all.
    /// The checks are not carried over — the service cleared the allergy stamp — so
    /// re-issuing runs them again against whatever the record says by then.
    /// </remarks>
    private Task EditPrescriptionAsync() => RunGuardedAsync(async () =>
    {
        if (_draft?.Prescription.Id is not { } prescriptionId) return;

        var refusal = await _prescribing.ReopenAsync(prescriptionId).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _issued = false;
        _scriptShown = null;
        _overrideReason = null;

        _draft = await _prescribing.GetDraftAsync(prescriptionId).ConfigureAwait(false);

        // The history list showed it as issued a moment ago, and it is a draft now.
        _history = await _prescribing.GetHistoryAsync(_patientId).ConfigureAwait(false);

        RaiseAll();
    });

    private Task StartAnotherAsync() => RunGuardedAsync(async () =>
    {
        _issued = false;
        _scriptShown = null;

        _draft = await _prescribing
            .GetOrStartDraftAsync(_patientId, _session.ProviderId)
            .ConfigureAwait(false);

        RaiseAll();
    });

    private Task ViewScriptAsync(Guid prescriptionId) => RunGuardedAsync(async () =>
    {
        _selectedScript = await _prescribing.GetDraftAsync(prescriptionId).ConfigureAwait(false);

        RaiseAll();
    });

    private Task ReissueAsync(Guid prescriptionId) => RunGuardedAsync(async () =>
    {
        _draft = await _prescribing
            .ReissueAsync(prescriptionId, _session.ProviderId)
            .ConfigureAwait(false);

        _issued = false;
        _scriptShown = null;
        _tab = RxTab.NewPrescription;

        RaiseAll();
    });

    private async Task ReloadDraftAsync()
    {
        if (_draft is not { } draft) return;

        _draft = await _prescribing.GetDraftAsync(draft.Prescription.Id).ConfigureAwait(false);

        RaiseAll();
    }

    // ---- referrals -------------------------------------------------------

    private Task DraftReferralAsync(string templateKey) => RunGuardedAsync(async () =>
    {
        if (_patient is null) return;

        _letter = await _referrals
            .DraftAsync(_patientId, _session.ProviderId ?? Guid.Empty, templateKey)
            .ConfigureAwait(false);

        _outbound = await _referrals.GetOutboundAsync(_patientId).ConfigureAwait(false);

        RaiseAll();
    });

    private Task SaveLetterAsync() => RunGuardedAsync(async () =>
    {
        if (_letter is not { } letter) return;

        await _referrals
            .SaveLetterAsync(
                letter.Id,
                letter.CounterpartyName,
                letter.Reason,
                letter.LetterBody ?? string.Empty,
                letter.IsUrgent)
            .ConfigureAwait(false);

        _outbound = await _referrals.GetOutboundAsync(_patientId).ConfigureAwait(false);

        RaiseAll();
    });

    private Task SendReferralAsync() => RunGuardedAsync(async () =>
    {
        if (_letter is not { } letter) return;

        // Saved first: the body on screen is what should be recorded as sent, and marking
        // it sent while the edits sat unsaved would file a different letter from the one
        // the specialist gets.
        await _referrals
            .SaveLetterAsync(
                letter.Id, letter.CounterpartyName, letter.Reason,
                letter.LetterBody ?? string.Empty, letter.IsUrgent)
            .ConfigureAwait(false);

        var refusal = await _referrals.MarkSentAsync(letter.Id).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _letter = await _referrals.GetAsync(letter.Id).ConfigureAwait(false);
        _outbound = await _referrals.GetOutboundAsync(_patientId).ConfigureAwait(false);

        RaiseAll();
    });

    private Task OpenLetterAsync(Guid referralId) => RunGuardedAsync(async () =>
    {
        _letter = await _referrals.GetAsync(referralId).ConfigureAwait(false);

        RaiseAll();
    });

    private Task MarkReportReceivedAsync(Guid referralId) => RunGuardedAsync(async () =>
    {
        await _referrals.MarkReportReceivedAsync(referralId).ConfigureAwait(false);

        _outbound = await _referrals.GetOutboundAsync(_patientId).ConfigureAwait(false);

        if (_letter?.Id == referralId)
        {
            _letter = await _referrals.GetAsync(referralId).ConfigureAwait(false);
        }

        RaiseAll();
    });

    // ---- certificates ----------------------------------------------------

    private Task DraftCertificateAsync() => RunGuardedAsync(async () =>
    {
        if (_patient is null) return;

        _certificateDraft = await _referrals
            .DraftCertificateAsync(
                _patientId, _session.ProviderId ?? Guid.Empty, _certificateDays,
                _certificateForStudy)
            .ConfigureAwait(false);

        RaiseAll();
    });

    private Task IssueCertificateAsync() => RunGuardedAsync(async () =>
    {
        if (_certificateDraft is not { } certificate) return;

        var refusal = await _referrals.IssueCertificateAsync(certificate.Id).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _certificates = await _referrals.GetCertificatesAsync(_patientId).ConfigureAwait(false);
        _certificateDraft = _certificates.FirstOrDefault(entry => entry.Id == certificate.Id);

        RaiseAll();
    });

    private void OnSessionChanged(object? sender, EventArgs e) => _ = LoadAsync();

    public void Dispose() => _session.Changed -= OnSessionChanged;

    /// <summary>
    /// Everything the screen reads. Listed explicitly rather than raising a blanket change,
    /// which would re-render the directions box being typed in.
    /// </summary>

    // ---- managing the formulary ------------------------------------------

    private void NewMedicine()
    {
        _isEditingMedicine = true;
        _editingMedicineId = Guid.Empty;
        _medicineAction = null;

        _mGeneric = null;
        _mBrand = null;
        _mStrength = null;
        _mForm = null;
        _mClass = MedicineClass.Other;
        _mDirections = null;

        // One, not zero. A quantity of zero is not a script anybody writes, and the save
        // refuses it — so the box opens on the smallest number that is actually a dose.
        _mQuantity = 1;
        _mRepeats = 0;

        _mAllergyClasses = null;
        _mInteractsWith = null;
        _mInteractionCaution = null;
        _mConditionCautions = null;
        _mConditionCaution = null;

        ErrorMessage = null;
        RaiseAll();
    }

    private void EditMedicine(Guid medicineId)
    {
        var medicine = _allMedicines.FirstOrDefault(row => row.Id == medicineId);

        if (medicine is null) return;

        _isEditingMedicine = true;
        _editingMedicineId = medicineId;
        _medicineAction = null;

        _mGeneric = medicine.GenericName;
        _mBrand = medicine.BrandName;
        _mStrength = medicine.Strength;
        _mForm = medicine.Form;
        _mClass = medicine.Class;
        _mDirections = medicine.DefaultDirections;
        _mQuantity = medicine.DefaultQuantity;
        _mRepeats = medicine.DefaultRepeats;
        _mAllergyClasses = medicine.AllergyClasses;
        _mInteractsWith = medicine.InteractsWith;
        _mInteractionCaution = medicine.InteractionCaution;
        _mConditionCautions = medicine.ConditionCautions;
        _mConditionCaution = medicine.ConditionCaution;

        ErrorMessage = null;
        RaiseAll();
    }

    private void CancelMedicine()
    {
        _isEditingMedicine = false;
        _editingMedicineId = Guid.Empty;

        ErrorMessage = null;
        RaiseAll();
    }

    private Task SaveMedicineAsync() => RunGuardedAsync(async () =>
    {
        var refusal = await _prescribing
            .SaveFormularyMedicineAsync(new FormularyMedicineEdit(
                _editingMedicineId,
                _mGeneric ?? string.Empty,
                _mBrand,
                _mStrength ?? string.Empty,
                _mForm,
                _mClass,
                _mDirections ?? string.Empty,
                _mQuantity,
                _mRepeats,
                _mAllergyClasses,
                _mInteractsWith,
                _mInteractionCaution,
                _mConditionCautions,
                _mConditionCaution))
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _medicineAction = $"{_mGeneric} {_mStrength} saved.";
        _isEditingMedicine = false;
        _editingMedicineId = Guid.Empty;

        await ReloadFormularyAsync().ConfigureAwait(false);
    });

    private Task SetMedicineActiveAsync(Guid medicineId) => RunGuardedAsync(async () =>
    {
        var medicine = _allMedicines.FirstOrDefault(row => row.Id == medicineId);

        if (medicine is null) return;

        var refusal = await _prescribing
            .SetFormularyActiveAsync(medicineId, !medicine.IsActive)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _medicineAction = medicine.IsActive
            ? $"{medicine.Label} retired — it can no longer be prescribed from the list."
            : $"{medicine.Label} is back on the list.";

        await ReloadFormularyAsync().ConfigureAwait(false);
    });

    private Task MoveMedicineAsync(MedicineMove move) => RunGuardedAsync(async () =>
    {
        await _prescribing
            .MoveFormularyMedicineAsync(move.MedicineId, move.Later)
            .ConfigureAwait(false);

        await ReloadFormularyAsync().ConfigureAwait(false);
    });

    /// <summary>
    /// Re-reads both views of the list.
    /// </summary>
    /// <remarks>
    /// Both, because the prescribing pane holds its own copy. Reloading only the manage
    /// list would leave a medicine edited here and the old directions still on the picker
    /// until the screen was left and come back to.
    /// </remarks>
    private async Task ReloadFormularyAsync()
    {
        _allMedicines = await _prescribing.GetAllFormularyAsync().ConfigureAwait(false);
        _formulary = await _prescribing.GetFormularyAsync().ConfigureAwait(false);

        RaiseAll();
    }
    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(Tab), nameof(HasPatient), nameof(PatientName), nameof(PatientBanner),
            nameof(HasAllergies), nameof(SearchResults), nameof(PatientSearch),
            nameof(Formulary), nameof(Draft), nameof(DraftItems), nameof(Checks),
            nameof(IsDraftEmpty), nameof(IsBlocked), nameof(WasIssued), nameof(IssueLabel),
            nameof(PracticeName), nameof(PrescriberName), nameof(PatientDateOfBirth),
            nameof(PatientAddress), nameof(IssuedOn), nameof(ValidUntil),
            nameof(CanEditIssued),
            nameof(IsScriptOpen), nameof(ScriptItems), nameof(SignatureImage),
            nameof(IsScriptSigned),
            nameof(SignedOn),
            nameof(OverrideReason), nameof(History), nameof(SelectedScript),
            nameof(HasSelectedScript), nameof(SelectedScriptMeta), nameof(Outbound),
            nameof(Inbound), nameof(Letter), nameof(HasLetter), nameof(IsLetterSent),
            nameof(LetterBody), nameof(LetterCounterparty), nameof(AwaitingReportCount),
            nameof(Certificates), nameof(CertificateDraft), nameof(HasCertificateDraft),
            nameof(AllMedicines), nameof(ManageSearch), nameof(ActiveMedicineCount),
            nameof(UnscreenedMedicineCount), nameof(IsEditingMedicine),
            nameof(IsNewMedicine), nameof(MedicineAction), nameof(EditingMedicine),
            nameof(MedicineGenericName), nameof(MedicineBrandName),
            nameof(MedicineStrength), nameof(MedicineForm), nameof(MedicineClassValue),
            nameof(MedicineDirections), nameof(MedicineQuantity), nameof(MedicineRepeats),
            nameof(MedicineAllergyClasses), nameof(MedicineInteractsWith),
            nameof(MedicineInteractionCaution), nameof(MedicineConditionCautions),
            nameof(MedicineConditionCaution), nameof(MedicineScreensNothing),
            nameof(CanSaveMedicine),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}

/// <summary>One edit to a draft prescription line.</summary>
public sealed record ItemEdit(Guid ItemId, string Directions, int Quantity, int Repeats);

/// <summary>One step of a formulary medicine up or down the prescriber's list.</summary>
/// <remarks>
/// A record because MvvmCross commands carry one argument, and the id alone does not say
/// which way it is going.
/// </remarks>
public sealed record MedicineMove(Guid MedicineId, bool Later);
