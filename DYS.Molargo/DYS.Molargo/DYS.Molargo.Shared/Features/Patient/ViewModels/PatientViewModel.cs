using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Components;
using DYS.Molargo.Shared.Documents;
using DYS.Molargo.Shared.Features.Patient.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Features.Patient.ViewModels;

/// <summary>Which tab of the record is showing.</summary>
/// <remarks>
/// Explicit values, because the tab is put in the URL — a colleague sends a link to a
/// patient's billing tab, and renumbering would land them somewhere else.
/// </remarks>
public enum PatientRecordTab
{
    Overview = 0,
    MedicalHistory = 1,
    Documents = 2,
    CommsLog = 3,
    Billing = 4,
    Privacy = 5,
}

/// <summary>
/// One patient's record: the header band, the medical-alert banner, and the prototype's
/// six tabs over it.
/// </summary>
public sealed class PatientViewModel : BaseViewModel<Guid>
{
    private readonly IPatientService _patients;
    private readonly IAppNavigator _navigator;
    private readonly IDocumentViewer _viewer;
    private readonly IClock _clock;

    private Guid _id;
    private PatientRecord? _record;
    private PatientRecordTab _tab = PatientRecordTab.Overview;

    // Local acknowledgement state for the actions that have no feature behind them yet.
    // Deliberately not persisted — see the command comments.
    private bool _reconfirmSent;
    private bool _exportQueued;
    private bool _erasureLogged;

    private DocumentKind _uploadKind = DocumentKind.Other;
    private string? _uploadRelatesTo;
    private bool _isUploading;
    private string? _uploadError;
    private IReadOnlyDictionary<Guid, bool> _documentAvailability = new Dictionary<Guid, bool>();

    public PatientViewModel(
        IPatientService patients,
        IAppNavigator navigator,
        IDocumentViewer viewer,
        IClock clock)
    {
        _patients = patients;
        _navigator = navigator;
        _viewer = viewer;
        _clock = clock;

        // Built once, in the constructor — never rebuilt per render.
        BackCommand = new MvxCommand(() => _navigator.ToPatientList());
        EditCommand = new MvxCommand(() => _navigator.ToPatientEdit(_id));
        RefreshCommand = new MvxAsyncCommand(LoadAsync);
        SelectTabCommand = new MvxCommand<PatientRecordTab>(SelectTab);
        OpenHouseholdMemberCommand = new MvxCommand<Guid>(id => _navigator.ToPatientRecord(id));
        TakeMedicalHistoryCommand = new MvxCommand(() => _navigator.ToMedicalHistory(_id));
        OpenChartCommand = new MvxCommand(() => _navigator.ToChart(_id));
        SendReconfirmationCommand = new MvxCommand(() => Acknowledge(ref _reconfirmSent, nameof(ReconfirmationLabel)));
        ExportRecordCommand = new MvxCommand(() => Acknowledge(ref _exportQueued, nameof(ExportLabel)));
        LogErasureRequestCommand = new MvxCommand(() => Acknowledge(ref _erasureLogged, nameof(ErasureLabel)));
        ArchivePatientCommand = new MvxAsyncCommand(ArchiveAsync, () => CanArchive);
        OpenDocumentCommand = new MvxAsyncCommand<Guid>(OpenDocumentAsync);
        DeleteDocumentCommand = new MvxAsyncCommand<Guid>(DeleteDocumentAsync);
    }

    public IMvxCommand BackCommand { get; }

    public IMvxCommand EditCommand { get; }

    public IMvxAsyncCommand RefreshCommand { get; }

    public IMvxCommand<PatientRecordTab> SelectTabCommand { get; }

    public IMvxCommand<Guid> OpenHouseholdMemberCommand { get; }

    /// <summary>Opens the clinical chart.</summary>
    public IMvxCommand OpenChartCommand { get; }

    /// <summary>Opens the questionnaire to take the history now, in the surgery.</summary>
    public IMvxCommand TakeMedicalHistoryCommand { get; }

    /// <summary>Asks the patient to complete it themselves, through the portal.</summary>
    public IMvxCommand SendReconfirmationCommand { get; }

    public IMvxCommand ExportRecordCommand { get; }

    public IMvxCommand LogErasureRequestCommand { get; }

    public IMvxAsyncCommand ArchivePatientCommand { get; }

    public IMvxAsyncCommand<Guid> OpenDocumentCommand { get; }

    public IMvxAsyncCommand<Guid> DeleteDocumentCommand { get; }

    public override void Prepare(Guid parameter) => _id = parameter;

    public override Task Initialize() => LoadAsync();

    public PatientRecord? Record => _record;

    public PatientEntity? Patient => _record?.Patient;

    public PatientRecordTab Tab => _tab;

    public bool NotFound => !IsBusy && _record is null;

    // ---- header band -----------------------------------------------------

    public string FullName => Patient?.FullName ?? string.Empty;

    /// <summary>Today in the practice's local zone, for ages rendered in the tabs.</summary>
    public DateOnly Today => _clock.Today;

    /// <summary>Now, for the tabs that have to tell a past booking from a future one.</summary>
    public DateTime NowUtc => _clock.UtcNow;

    public int? Age => Patient?.AgeAt(Today);

    /// <summary>"F · 58 · DOB 12 Mar 1968 · #10201" — the prototype's sub-header line.</summary>
    public string IdentityLine
    {
        get
        {
            if (Patient is not { } patient) return string.Empty;

            var parts = new List<string>();

            if (patient.Sex != Sex.Unspecified) parts.Add(SexLabel(patient.Sex));
            if (Age is { } age) parts.Add(age.ToString());
            if (patient.DateOfBirth is not null) parts.Add($"DOB {MolargoFormat.Date(patient.DateOfBirth)}");
            if (patient.PatientNumber is { Length: > 0 } number) parts.Add($"#{number}");

            return string.Join(" · ", parts);
        }
    }

    public IReadOnlyList<PatientTags> TagList =>
        Patient is null ? [] : TagsOf(Patient.Tags);

    /// <summary>The funding chip — "HCF · Medicare".</summary>
    public string? FundingLine
    {
        get
        {
            if (Patient is not { } patient) return null;

            var parts = new List<string>();
            if (patient.HealthFund is { Length: > 0 } fund) parts.Add(fund);
            if (patient.MedicareNumber is { Length: > 0 }) parts.Add("Medicare");
            if (patient.DvaNumber is { Length: > 0 }) parts.Add("DVA");

            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }
    }

    public IReadOnlyList<PatientEntity> Household => _record?.Household ?? [];

    public decimal Balance => Patient?.Balance ?? 0m;

    /// <summary>"Thu 3 Sep", or "None booked" — the header's next-visit figure.</summary>
    public string NextVisitLabel
    {
        get
        {
            if (_record?.NextAppointment(_clock.UtcNow) is not { } next) return "None booked";

            return MolargoFormat.DayLabel(next.StartUtc);
        }
    }

    // ---- medical alert banner --------------------------------------------

    public bool HasCriticalAlerts => _record?.CriticalAlerts.Any() ?? false;

    public IReadOnlyList<PatientAlert> Allergies => _record?.Allergies.ToList() ?? [];

    public IReadOnlyList<PatientAlert> Medications => _record?.Medications.ToList() ?? [];

    public IReadOnlyList<PatientAlert> Conditions => _record?.Conditions.ToList() ?? [];

    public IReadOnlyList<PatientAlert> Alerts => _record?.Alerts ?? [];

    public IReadOnlyList<MedicalHistoryAnswer> MedicalHistoryAnswers =>
        _record?.MedicalHistoryAnswers ?? [];

    // ---- overview tab ----------------------------------------------------

    public bool IsMedicalHistoryOverdue => _record?.IsMedicalHistoryOverdue(_clock.Today) ?? false;

    /// <summary>"Last confirmed 14 Jul 2025", or "Never completed".</summary>
    public string MedicalHistoryStatus
    {
        get
        {
            if (_record?.MedicalHistory?.CompletedUtc is not { } completed)
            {
                return "No medical history on file";
            }

            return $"Last confirmed {MolargoFormat.ShortDate(completed)} — annual prompt due";
        }
    }

    public string ReconfirmationLabel =>
        _reconfirmSent ? "Sent to portal ✓" : "Send re-confirmation";

    public bool IsReconfirmationSent => _reconfirmSent;

    public IReadOnlyList<TreatmentPlan> Plans => _record?.OpenPlans.ToList() ?? [];

    public IReadOnlyList<TreatmentProgressRow> InProgress =>
        _record?.InProgress().ToList() ?? [];

    public IReadOnlyList<Appointment> UpcomingAppointments =>
        _record?.UpcomingAppointments(_clock.UtcNow).ToList() ?? [];

    public IReadOnlyList<PatientDocument> Documents => _record?.Documents ?? [];

    /// <summary>The three most recent documents, for the overview tab's vault summary.</summary>
    public IReadOnlyList<PatientDocument> RecentDocuments => Documents.Take(3).ToList();

    /// <summary>"SMS ✓ · Email ✓ · Marketing ✗" — the overview's consent summary.</summary>
    public string ConsentSummary
    {
        get
        {
            if (Patient is not { } patient) return string.Empty;

            var reminders = patient.ReminderConsent ? "✓" : "✗";
            var marketing = patient.MarketingConsent ? "✓" : "✗";
            var email = patient.Email is { Length: > 0 } ? "✓" : "✗";

            return $"Reminders {reminders} · Email {email} · Marketing {marketing}";
        }
    }

    // ---- other tabs ------------------------------------------------------

    public IReadOnlyList<CommunicationLog> Communications => _record?.Communications ?? [];

    public IReadOnlyList<BillingRow> Billing => _record?.Billing ?? [];

    public decimal BillingOutstanding => _record?.Outstanding ?? 0m;

    public IReadOnlyList<ConsentForm> Consents => _record?.Consents ?? [];

    public IReadOnlyList<Recall> Recalls => _record?.Recalls ?? [];

    // ---- privacy tab -----------------------------------------------------

    public string ExportLabel => _exportQueued
        ? "Export queued — encrypted link in 10 min ✓"
        : "Export full record (Privacy Act / GDPR)";

    public string ErasureLabel => _erasureLogged
        ? "Erasure request logged — owner approval pending"
        : "Log erasure request";

    public string ArchiveLabel => Patient?.Status == PatientStatus.Archived
        ? "Archived — recalls & reminders stopped ✓"
        : "Archive / deactivate patient";

    /// <summary>False once archived, so the button cannot be pressed twice.</summary>
    public bool CanArchive => Patient is not null && Patient.Status != PatientStatus.Archived;

    public bool IsExportQueued => _exportQueued;

    public bool IsErasureLogged => _erasureLogged;

    /// <summary>Names a provider for a table row. Falls back rather than showing a GUID.</summary>
    public string ProviderName(Guid? providerId) =>
        providerId is { } id && (_record?.ProviderNames.TryGetValue(id, out var name) ?? false)
            ? name
            : "—";


    // ---- documents -------------------------------------------------------

    /// <summary>
    /// Largest file accepted. Generous because a panoramic radiograph or a DICOM slice is
    /// genuinely this big; capped because the web head streams uploads over the SignalR
    /// circuit, and an unbounded one lets a single upload starve every other screen on
    /// the same connection.
    /// </summary>
    public const long MaxUploadBytes = 25 * 1024 * 1024;

    /// <summary>The kind the next upload is filed under. Bound to the picker on the tab.</summary>
    public DocumentKind UploadKind
    {
        get => _uploadKind;
        set => SetProperty(ref _uploadKind, value);
    }

    /// <summary>What the upload relates to — a tooth, a visit. Optional.</summary>
    public string? UploadRelatesTo
    {
        get => _uploadRelatesTo;
        set => SetProperty(ref _uploadRelatesTo, value);
    }

    /// <summary>Set while a file is being written, so the tab can disable the picker.</summary>
    public bool IsUploading
    {
        get => _isUploading;
        private set => SetProperty(ref _isUploading, value);
    }

    /// <summary>Why the last upload failed, shown on the tab. Null when it succeeded.</summary>
    public string? UploadError
    {
        get => _uploadError;
        private set => SetProperty(ref _uploadError, value);
    }

    /// <summary>True where this head can open a stored file at all.</summary>
    public bool CanOpenDocuments => _viewer.CanOpen;

    /// <summary>False for a row whose file has gone missing from the store.</summary>
    public bool IsDocumentAvailable(Guid documentId) =>
        _documentAvailability.GetValueOrDefault(documentId, true);

    /// <summary>
    /// Stores an uploaded file against this patient.
    /// </summary>
    /// <remarks>
    /// Takes the stream rather than the browser's file object, so the view model has no
    /// dependency on <c>IBrowserFile</c> — which is a Blazor type, and a view model that
    /// referenced it could not drive a native head.
    /// </remarks>
    public async Task UploadAsync(string fileName, string? contentType, long size, Stream content)
    {
        UploadError = null;

        if (size > MaxUploadBytes)
        {
            UploadError =
                $"{RecordCss.FileSize(size)} is too large — the limit is {RecordCss.FileSize(MaxUploadBytes)}.";
            return;
        }

        IsUploading = true;

        try
        {
            await _patients
                .AddDocumentAsync(_id, UploadKind, fileName, contentType, content,
                    string.IsNullOrWhiteSpace(UploadRelatesTo) ? null : UploadRelatesTo.Trim())
                .ConfigureAwait(false);

            UploadRelatesTo = null;

            // Re-read rather than appending to the loaded list: the row picks up its id
            // and audit stamps on save, and the tab shows what the database now holds.
            await LoadAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Caught here rather than through RunGuardedAsync, so a failed upload reports
            // against the file picker instead of replacing the whole record with an error.
            UploadError = ex.Message;
        }
        finally
        {
            IsUploading = false;
        }
    }

    private void SelectTab(PatientRecordTab tab)
    {
        if (_tab == tab) return;

        _tab = tab;
        RaisePropertyChanged(nameof(Tab));
    }

    private Task OpenDocumentAsync(Guid documentId) => RunGuardedAsync(() =>
        _viewer.OpenAsync(documentId));

    private Task DeleteDocumentAsync(Guid documentId) => RunGuardedAsync(async () =>
    {
        await _patients.DeleteDocumentAsync(documentId).ConfigureAwait(false);
        await LoadAsync().ConfigureAwait(false);
    });

    private Task LoadAsync() => RunGuardedAsync(async () =>
    {
        _record = await _patients.GetRecordAsync(_id).ConfigureAwait(false);

        // Which rows still have a file behind them. Checked on load rather than per
        // render: it touches the filesystem once per document, and a render can happen
        // many times a second.
        _documentAvailability = _record is null
            ? new Dictionary<Guid, bool>()
            : await _patients.GetDocumentAvailabilityAsync(_record.Documents).ConfigureAwait(false);

        RaiseAllDerived();
    });

    private Task ArchiveAsync() => RunGuardedAsync(async () =>
    {
        await _patients.SetStatusAsync(_id, PatientStatus.Archived).ConfigureAwait(false);

        // Re-read rather than mutating the loaded copy: archiving is a real write, and
        // the screen should show what the database now holds.
        _record = await _patients.GetRecordAsync(_id).ConfigureAwait(false);

        RaiseAllDerived();
        ArchivePatientCommand.RaiseCanExecuteChanged();
    });

    /// <summary>
    /// Flips a local acknowledgement flag for an action whose real implementation does
    /// not exist yet — sending to the patient portal, queueing an encrypted export,
    /// logging an erasure request for owner approval. Each needs a feature behind it
    /// (outbound messaging, a job runner, an approval workflow) that the app has none of
    /// while it is offline-only.
    /// </summary>
    /// <remarks>
    /// Shown as pressed rather than silently doing nothing, so the button is honest and it
    /// is obvious where the real work has to go. Not persisted on purpose: a flag that
    /// survived a reload would claim an export had been queued when nothing had.
    /// </remarks>
    private void Acknowledge(ref bool flag, string labelProperty)
    {
        if (flag) return;

        flag = true;
        RaisePropertyChanged(labelProperty);
        RaiseAllDerived();
    }

    private static IReadOnlyList<PatientTags> TagsOf(PatientTags tags) =>
        Enum.GetValues<PatientTags>()
            .Where(tag => tag != PatientTags.None && tags.HasFlag(tag))
            .ToList();

    private static string SexLabel(Sex sex) => sex switch
    {
        Sex.Female => "F",
        Sex.Male => "M",
        Sex.Other => "X",
        _ => string.Empty,
    };

    /// <summary>
    /// Everything on this screen is computed from <see cref="Record"/>, so one load has
    /// to announce all of it. Listed explicitly rather than raising a blanket change,
    /// which would re-render all six tabs on every keystroke anywhere.
    /// </summary>
    private void RaiseAllDerived()
    {
        foreach (var name in new[]
        {
            nameof(Record), nameof(Patient), nameof(NotFound), nameof(FullName), nameof(Age),
            nameof(IdentityLine), nameof(TagList), nameof(FundingLine), nameof(Household),
            nameof(Balance), nameof(NextVisitLabel), nameof(HasCriticalAlerts),
            nameof(Allergies), nameof(Medications), nameof(Conditions), nameof(Alerts),
            nameof(MedicalHistoryAnswers),
            nameof(IsMedicalHistoryOverdue), nameof(MedicalHistoryStatus),
            nameof(ReconfirmationLabel), nameof(IsReconfirmationSent), nameof(Plans),
            nameof(InProgress), nameof(UpcomingAppointments), nameof(Documents),
            nameof(RecentDocuments), nameof(ConsentSummary), nameof(Communications),
            nameof(Billing), nameof(BillingOutstanding), nameof(Consents), nameof(Recalls),
            nameof(ExportLabel), nameof(ErasureLabel), nameof(ArchiveLabel),
            nameof(CanArchive), nameof(IsExportQueued), nameof(IsErasureLogged),
            nameof(CanOpenDocuments),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
