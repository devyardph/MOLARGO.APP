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

    private IReadOnlyList<PrescriptionSummary> _history = [];
    private PrescriptionDraft? _selectedScript;

    private IReadOnlyList<ReferralRow> _outbound = [];
    private IReadOnlyList<ReferralRow> _inbound = [];
    private Referral? _letter;

    private IReadOnlyList<MedicalCertificate> _certificates = [];
    private MedicalCertificate? _certificateDraft;
    private int _certificateDays = 1;
    private bool _certificateForStudy;

    public PrescribingViewModel(
        IPrescribingService prescribing,
        IReferralService referrals,
        IPatientService patients,
        ISessionService session,
        IAppNavigator navigator)
    {
        _prescribing = prescribing;
        _referrals = referrals;
        _patients = patients;
        _session = session;
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

        ViewScriptCommand = new MvxAsyncCommand<Guid>(ViewScriptAsync);
        ReissueCommand = new MvxAsyncCommand<Guid>(ReissueAsync);

        DraftReferralCommand = new MvxAsyncCommand<string>(key => DraftReferralAsync(key!));
        SaveLetterCommand = new MvxAsyncCommand(SaveLetterAsync);
        SendReferralCommand = new MvxAsyncCommand(SendReferralAsync);
        OpenLetterCommand = new MvxAsyncCommand<Guid>(OpenLetterAsync);
        MarkReportReceivedCommand = new MvxAsyncCommand<Guid>(MarkReportReceivedAsync);

        DraftCertificateCommand = new MvxAsyncCommand(DraftCertificateAsync);
        IssueCertificateCommand = new MvxAsyncCommand(IssueCertificateAsync);

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

    public IMvxAsyncCommand<Guid> ViewScriptCommand { get; }

    public IMvxAsyncCommand<Guid> ReissueCommand { get; }

    public IMvxAsyncCommand<string> DraftReferralCommand { get; }

    public IMvxAsyncCommand SaveLetterCommand { get; }

    public IMvxAsyncCommand SendReferralCommand { get; }

    public IMvxAsyncCommand<Guid> OpenLetterCommand { get; }

    public IMvxAsyncCommand<Guid> MarkReportReceivedCommand { get; }

    public IMvxAsyncCommand DraftCertificateCommand { get; }

    public IMvxAsyncCommand IssueCertificateCommand { get; }

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

    private Task StartAnotherAsync() => RunGuardedAsync(async () =>
    {
        _issued = false;

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
    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(Tab), nameof(HasPatient), nameof(PatientName), nameof(PatientBanner),
            nameof(HasAllergies), nameof(SearchResults), nameof(PatientSearch),
            nameof(Formulary), nameof(Draft), nameof(DraftItems), nameof(Checks),
            nameof(IsDraftEmpty), nameof(IsBlocked), nameof(WasIssued), nameof(IssueLabel),
            nameof(OverrideReason), nameof(History), nameof(SelectedScript),
            nameof(HasSelectedScript), nameof(SelectedScriptMeta), nameof(Outbound),
            nameof(Inbound), nameof(Letter), nameof(HasLetter), nameof(IsLetterSent),
            nameof(LetterBody), nameof(LetterCounterparty), nameof(AwaitingReportCount),
            nameof(Certificates), nameof(CertificateDraft), nameof(HasCertificateDraft),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}

/// <summary>One edit to a draft prescription line.</summary>
public sealed record ItemEdit(Guid ItemId, string Directions, int Quantity, int Repeats);
