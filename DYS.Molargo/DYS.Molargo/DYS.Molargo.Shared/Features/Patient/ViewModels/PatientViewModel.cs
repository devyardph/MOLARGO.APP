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

/// <summary>One labelled fact on the overview's patient-details panel.</summary>
public sealed record PatientDetailRow(string Label, string Value);

/// <summary>A headed run of them — "Contact", "Funding", and so on.</summary>
public sealed record PatientDetailGroup(string Heading, IReadOnlyList<PatientDetailRow> Rows);

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

    /// <summary>
    /// Only to name the patient's usual site on the details panel.
    /// </summary>
    /// <remarks>
    /// The session already holds every location the practice runs, so naming one costs
    /// nothing. Loading the locations into the patient record instead would add a query
    /// to a read that already makes a dozen, to answer a question the session has
    /// answered since sign-in.
    /// </remarks>
    private readonly ISessionService _session;

    private Guid _id;
    private PatientRecord? _record;
    private PatientRecordTab _tab = PatientRecordTab.Overview;

    // Local acknowledgement state for the actions that have no feature behind them yet.
    // Deliberately not persisted — see the command comments.
    private bool _reconfirmSent;
    private bool _exportQueued;
    private bool _erasureLogged;

    // The medical-history tab's alert table: which page, and the remove confirm.
    private int _alertPage;
    private string? _alertRemovalRefusal;

    // Raising and signing a consent form. Held here rather than in the component because
    // these are fields being filled in, not "which row is confirming" — the component
    // keeps that, as the documents delete already does.
    private string? _consentRefusal;

    private DocumentKind _uploadKind = DocumentKind.Other;
    private string? _uploadRelatesTo;
    private bool _isUploading;
    private string? _uploadError;
    private IReadOnlyDictionary<Guid, bool> _documentAvailability = new Dictionary<Guid, bool>();

    public PatientViewModel(
        IPatientService patients,
        IAppNavigator navigator,
        IDocumentViewer viewer,
        IClock clock,
        ISessionService session)
    {
        _patients = patients;
        _navigator = navigator;
        _viewer = viewer;
        _clock = clock;
        _session = session;

        // Built once, in the constructor — never rebuilt per render.
        BackCommand = new MvxCommand(() => _navigator.ToPatientList());
        EditCommand = new MvxCommand(() => _navigator.ToPatientEdit(_id));
        RefreshCommand = new MvxAsyncCommand(LoadAsync);
        SelectTabCommand = new MvxCommand<PatientRecordTab>(SelectTab);
        OpenHouseholdMemberCommand = new MvxCommand<Guid>(id => _navigator.ToPatientRecord(id));
        TakeMedicalHistoryCommand = new MvxCommand(() => _navigator.ToMedicalHistory(_id));
        OpenChartCommand = new MvxCommand(() => _navigator.ToChart(_id));
        BookAppointmentCommand = new MvxCommand(() => _navigator.ToNewAppointment(patientId: _id));
        NewPlanCommand = new MvxCommand(() => _navigator.ToPlanBuilder(_id));
        EditPlanCommand = new MvxCommand<Guid>(planId => _navigator.ToPlanBuilder(_id, planId));
        PresentPlanCommand = new MvxCommand(PresentPlan, () => CanPresentPlan);

        OpenPlanCommand = new MvxCommand<Guid>(planId => _navigator.ToPlanPresentation(planId));
        SendReconfirmationCommand = new MvxCommand(() => Acknowledge(ref _reconfirmSent, nameof(ReconfirmationLabel)));
        ExportRecordCommand = new MvxCommand(() => Acknowledge(ref _exportQueued, nameof(ExportLabel)));
        LogErasureRequestCommand = new MvxCommand(() => Acknowledge(ref _erasureLogged, nameof(ErasureLabel)));
        ArchivePatientCommand = new MvxAsyncCommand(ArchiveAsync, () => CanArchive);
        NextAlertPageCommand = new MvxCommand(() => StepAlertPage(1));
        PreviousAlertPageCommand = new MvxCommand(() => StepAlertPage(-1));
        RemoveAlertCommand = new MvxAsyncCommand<Guid>(RemoveAlertAsync);
        OpenDocumentCommand = new MvxAsyncCommand<Guid>(OpenDocumentAsync);
        DeleteDocumentCommand = new MvxAsyncCommand<Guid>(DeleteDocumentAsync);
        WithdrawConsentCommand = new MvxAsyncCommand<Guid>(id => CloseConsentAsync(id, true));
        NewConsentCommand = new MvxCommand(() => _navigator.ToConsent(_id));
        OpenConsentCommand = new MvxCommand<Guid>(consentId => _navigator.ToConsent(_id, consentId));
    }

    public IMvxCommand BackCommand { get; }

    public IMvxCommand EditCommand { get; }

    public IMvxAsyncCommand RefreshCommand { get; }

    public IMvxCommand<PatientRecordTab> SelectTabCommand { get; }

    public IMvxCommand<Guid> OpenHouseholdMemberCommand { get; }

    /// <summary>Opens the clinical chart.</summary>
    public IMvxCommand OpenChartCommand { get; }

    /// <summary>
    /// Books the next visit for this patient, without going back to the diary to find
    /// them again.
    /// </summary>
    /// <remarks>
    /// Sends only the id — the booking form reads the name back from it, and picks its
    /// own default slot. Deciding the slot here would mean this screen owning a second
    /// copy of the diary's rules about what "next" means.
    /// </remarks>
    public IMvxCommand BookAppointmentCommand { get; }

    /// <summary>
    /// Opens the plan builder on a new plan, seeded from the charted findings.
    /// </summary>
    public IMvxCommand NewPlanCommand { get; }

    /// <summary>Reopens a draft plan in the builder.</summary>
    public IMvxCommand<Guid> EditPlanCommand { get; }

    /// <summary>Opens one plan chairside — the row action for a plan already presented.</summary>
    public IMvxCommand<Guid> OpenPlanCommand { get; }

    /// <summary>
    /// Opens the chairside presentation for the plan the practice would show first.
    /// </summary>
    /// <remarks>
    /// Which plan that is, is a decision — see <see cref="PlanToPresent"/>. The button
    /// disables itself rather than presenting nothing when there is no candidate, because
    /// a chairside screen opened on an empty plan is opened in front of a patient.
    /// </remarks>
    public IMvxCommand PresentPlanCommand { get; }

    /// <summary>Opens the questionnaire to take the history now, in the surgery.</summary>
    public IMvxCommand TakeMedicalHistoryCommand { get; }

    /// <summary>Asks the patient to complete it themselves, through the portal.</summary>
    public IMvxCommand SendReconfirmationCommand { get; }

    public IMvxCommand ExportRecordCommand { get; }

    public IMvxCommand LogErasureRequestCommand { get; }

    public IMvxAsyncCommand ArchivePatientCommand { get; }

    /// <summary>Steps the medical-history alert table a page at a time.</summary>
    public IMvxCommand NextAlertPageCommand { get; }

    public IMvxCommand PreviousAlertPageCommand { get; }

    /// <summary>Removes one alert recorded today. Confirmed in the row, as documents are.</summary>
    public IMvxAsyncCommand<Guid> RemoveAlertCommand { get; }

    public IMvxAsyncCommand<Guid> OpenDocumentCommand { get; }

    public IMvxAsyncCommand<Guid> DeleteDocumentCommand { get; }

    /// <summary>They signed and have since taken it back.</summary>
    public IMvxAsyncCommand<Guid> WithdrawConsentCommand { get; }

    /// <summary>
    /// Opens the patient-facing consent screen to raise a new one.
    /// </summary>
    /// <remarks>
    /// A screen, not a panel on this tab. Raising a consent is a clinician's act and
    /// signing it is the patient's; doing both inside the documents table asked one person
    /// at one desk to be both, with the file vault and the rest of the record still on
    /// screen while somebody read the risks of their own surgery.
    /// </remarks>
    public IMvxCommand NewConsentCommand { get; }

    /// <summary>Opens a raised consent to be signed.</summary>
    public IMvxCommand<Guid> OpenConsentCommand { get; }

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

    /// <summary>
    /// The overview's patient-details panel, as headed groups of labelled facts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Assembled here rather than as twenty conditional blocks in the markup. Which rows
    /// appear is a judgement about the record — a health fund the patient does not hold
    /// is not a fact worth a line — and a judgement belongs somewhere it can be read in
    /// one place, not spread through <c>@if</c> wrappers in a template.
    /// </para>
    /// <para>
    /// Two kinds of row, and the difference matters. The core contact facts are always
    /// present and say "Not recorded" when blank, because a patient with no email on file
    /// is precisely what the front desk needs to see before promising them a reminder —
    /// absent is not the same as empty. Everything conditional — a fund, a DVA card, an
    /// emergency contact — is dropped when absent instead: a column of dashes reads as a
    /// broken screen rather than as a record with nothing in it.
    /// </para>
    /// <para>
    /// Nothing here is masked. The design's admin screen promises Medicare numbers hidden
    /// from everyone but billing roles; that is field-level permissioning, which is not
    /// built, and half-masking one number here would suggest a protection the record does
    /// not have. Anyone who can open this screen can already read the same number on the
    /// edit form.
    /// </para>
    /// </remarks>
    public IReadOnlyList<PatientDetailGroup> DetailGroups
    {
        get
        {
            if (Patient is not { } patient) return [];

            var groups = new List<PatientDetailGroup>();

            Add(groups, "Contact",
                Always("Mobile", patient.Mobile),
                Optional("Home phone", patient.HomePhone),
                Always("Email", patient.Email),
                Always("Address", Address(patient)));

            Add(groups, "Personal",
                new PatientDetailRow("Date of birth", BirthLine(patient)),
                patient.Sex == Sex.Unspecified
                    ? null
                    : new PatientDetailRow("Sex", SexLabel(patient.Sex)),

                // Only where it differs. "Known as: Thomas" under the name Thomas is a
                // row that costs a line and settles nothing.
                patient.PreferredName is { Length: > 0 } preferred
                    && !string.Equals(preferred, patient.FirstName, StringComparison.OrdinalIgnoreCase)
                        ? new PatientDetailRow("Known as", preferred)
                        : null,

                // English is the default every record carries, so printing it on all of
                // them would hide the handful where the answer is not English.
                patient.PreferredLanguage is { Length: > 0 } language
                    && !string.Equals(language, "English", StringComparison.OrdinalIgnoreCase)
                        ? new PatientDetailRow("Language", language)
                        : null);

            Add(groups, "Funding",
                Optional("Health fund", Joined(patient.HealthFund, patient.HealthFundMemberNumber)),
                Optional("Medicare", Joined(
                    patient.MedicareNumber,
                    patient.MedicareReferenceNumber is { } reference ? $"ref {reference}" : null)),
                Optional("DVA", patient.DvaNumber),
                Optional("Concession", patient.ConcessionCardNumber));

            Add(groups, "Emergency contact",
                Optional("Name", patient.EmergencyContactName),
                Optional("Relationship", patient.EmergencyContactRelationship),
                Optional("Phone", patient.EmergencyContactPhone));

            Add(groups, "At the practice",
                Optional("Usual site", SiteName(patient.PracticeLocationId)),
                Optional("Provider", PreferredProviderName),

                // "Never attended" rather than a dash: a lead who has not been in yet is
                // a different thing from a patient whose last visit was not recorded.
                new PatientDetailRow("Last seen", patient.LastSeenUtc is null
                    ? "Never attended"
                    : MolargoFormat.ShortDate(patient.LastSeenUtc)),

                Optional("Referral source", patient.ReferralSource));

            // Its own group, and last. A note is a sentence rather than a value, and
            // sitting it under "At the practice" among one-word answers buries it. It is
            // written on the edit form and, until now, was readable on no screen at all.
            Add(groups, "Front-desk note", Optional("Note", patient.Notes));

            return groups;
        }
    }

    public IReadOnlyList<PatientEntity> Household => _record?.Household ?? [];

    public decimal Balance => Patient?.Balance ?? 0m;

    /// <summary>"Thu 3 Sep", or "None booked" — the header's next-visit figure.</summary>
    public string NextVisitLabel
    {
        get
        {
            if (_record?.NextAppointment(_clock.UtcNow) is not { } next) return "-";

            return MolargoFormat.DayLabel(next.StartUtc);
        }
    }

    // ---- medical alert banner --------------------------------------------

    public bool HasCriticalAlerts => _record?.CriticalAlerts.Any() ?? false;

    public IReadOnlyList<PatientAlert> Allergies => _record?.Allergies.ToList() ?? [];

    public IReadOnlyList<PatientAlert> Medications => _record?.Medications.ToList() ?? [];

    public IReadOnlyList<PatientAlert> Conditions => _record?.Conditions.ToList() ?? [];

    public IReadOnlyList<PatientAlert> Alerts => _record?.Alerts ?? [];

    // ---- the medical-history tab's alert list ------------------------------

    /// <summary>
    /// How many alerts one page of the medical-history table shows.
    /// </summary>
    /// <remarks>
    /// Twenty-five. A patient's list is usually under ten, but a long-standing one on
    /// several medications accumulates without limit, and a table that just keeps growing
    /// is one nobody reads to the bottom of.
    /// </remarks>
    public const int AlertPageSize = 25;

    /// <summary>
    /// The alerts on the current page.
    /// </summary>
    /// <remarks>
    /// Paged in memory rather than through <c>GetPageAsync</c>, which is the exception to
    /// how this app pages and worth saying why. The header band needs every alert anyway —
    /// the allergies, medications and conditions it prints, and whether any is critical —
    /// so the record already holds the whole set. A paged query here would be a second
    /// round trip that fetched a subset of rows already in hand.
    /// </remarks>
    public IReadOnlyList<PatientAlert> AlertPage =>
        Alerts.Skip(_alertPage * AlertPageSize).Take(AlertPageSize).ToList();

    public int AlertPageIndex => _alertPage;

    public int AlertPageCount =>
        Math.Max(1, (Alerts.Count + AlertPageSize - 1) / AlertPageSize);

    public int AlertCount => Alerts.Count;

    /// <summary>
    /// Whether this alert can still be removed — only on the day it was recorded.
    /// </summary>
    /// <remarks>
    /// The same rule the service enforces, so the button never offers what the save would
    /// refuse. Duplicated on purpose rather than asked of the service per row: this is
    /// called once per rendered row, and a service call per row is a round trip per row.
    /// </remarks>
    public bool CanRemoveAlert(PatientAlert alert) =>
        DateOnly.FromDateTime(alert.CreatedUtc.ToLocalTime()) == Today;

    public string? AlertRemovalRefusal => _alertRemovalRefusal;

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

    /// <summary>
    /// The plan "Present plan" opens: the recommended one, else the first still open.
    /// </summary>
    /// <remarks>
    /// <c>OpenPlans</c> already orders recommended first, so this is its head — but it is
    /// named rather than inlined because the button's enabled state and its destination
    /// have to be the same decision. Two expressions that agreed today would eventually
    /// disagree, and the failure would be a disabled button on a screen with a plan on it.
    /// </remarks>
    public TreatmentPlan? PlanToPresent => Plans.Count > 0 ? Plans[0] : null;

    public bool CanPresentPlan => PlanToPresent is not null;


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

    // ---- raising and signing consent --------------------------------------

    /// <summary>The default signer, and by far the commonest: the patient themselves.</summary>
    public const string SelfRelationship = "Self";

    /// <summary>
    /// Who else signs, where it is not the patient.
    /// </summary>
    /// <remarks>
    /// The same short list the chairside plan acceptance offers, deliberately — two
    /// screens recording the same fact should not offer two different vocabularies for it.
    /// </remarks>
    public static readonly string[] Relationships =
        [SelfRelationship, "Parent", "Guardian", "Carer"];

    /// <summary>A refusal from the service — why the consent could not be recorded.</summary>
    public string? ConsentRefusal => _consentRefusal;

    /// <summary>
    /// The uploads that could be a signed original — the ones filed as consent.
    /// </summary>
    /// <remarks>
    /// Narrowed to the consent kind rather than offering every file on the record. A
    /// radiograph is not a signed form, and a picker listing forty documents makes
    /// attaching the wrong one easy.
    /// </remarks>
    public IReadOnlyList<PatientDocument> ConsentScans =>
        Documents.Where(document => document.Kind == DocumentKind.Consent).ToList();

    /// <summary>Whether a form is still waiting for an answer.</summary>
    public static bool AwaitsSignature(ConsentForm consent) =>
        consent.Status is ConsentStatus.Pending or ConsentStatus.Refused;

    /// <summary>The scan attached to a signed consent, where one was.</summary>
    public PatientDocument? ScanFor(ConsentForm consent) =>
        consent.DocumentId is { } id
            ? Documents.FirstOrDefault(document => document.Id == id)
            : null;

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

    /// <summary>
    /// Opens the record on a named tab, from the URL.
    /// </summary>
    /// <remarks>
    /// Matched on the label the tab strip already shows, lowercased and hyphenated, so the
    /// link reads as what it opens — "?tab=medical-history". Matching on the enum's number
    /// would have made every shared link break the day somebody reordered the strip.
    ///
    /// An unrecognised name leaves the record on Overview rather than failing. A link with
    /// a stale tab name is still a link to the right patient, and refusing to open it helps
    /// nobody.
    /// </remarks>
    public void SetInitialTab(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        foreach (var tab in Enum.GetValues<PatientRecordTab>())
        {
            if (string.Equals(TabSlug(tab), name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                _tab = tab;
                return;
            }
        }
    }

    /// <summary>The tab's name as it appears in a URL.</summary>
    public static string TabSlug(PatientRecordTab tab) => tab switch
    {
        PatientRecordTab.MedicalHistory => "medical-history",
        PatientRecordTab.CommsLog => "comms-log",
        _ => tab.ToString().ToLowerInvariant(),
    };

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

        // ReloadAsync, not LoadAsync: this is already inside the guard, and the guarded
        // one returns without doing anything while busy. The row stayed on screen after
        // the file had gone — the fourth time this trap has bitten in this codebase.
        await ReloadAsync().ConfigureAwait(false);
    });

    /// <summary>
    /// Removes one medical alert, if it was recorded today.
    /// </summary>
    /// <remarks>
    /// Takes the id rather than reading a pending-confirm field, so it matches
    /// <see cref="DeleteDocumentCommand"/>: the documents tab keeps "which row is asking"
    /// as component view state, because it is about that component's rendering and means
    /// nothing to the record. The two tabs now delete the same way.
    /// </remarks>
    private Task RemoveAlertAsync(Guid alertId) => RunGuardedAsync(async () =>
    {
        var refusal = await _patients.RemoveAlertAsync(alertId).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            _alertRemovalRefusal = refusal;
            RaiseAlertList();
            return;
        }

        _alertRemovalRefusal = null;

        await ReloadAsync().ConfigureAwait(false);

        // Clamped, because removing the only row on the last page would otherwise leave
        // the table paged past its own end and looking empty.
        _alertPage = Math.Clamp(_alertPage, 0, AlertPageCount - 1);

        RaiseAlertList();
    });

    private Task LoadAsync() => RunGuardedAsync(ReloadAsync);

    /// <summary>
    /// The load itself, without the busy guard.
    /// </summary>
    /// <remarks>
    /// Split out because <c>RunGuardedAsync</c> refuses to nest: it returns immediately
    /// while already busy, so a guarded command calling <see cref="LoadAsync"/> silently
    /// skips the reload. That has bitten this codebase three times now — the plan
    /// presentation kept offering Accept after the acceptance was written, and the booking
    /// form's pre-booking checks went stale on every picker — and the symptom is always
    /// the same: the database is right and the screen is not.
    /// </remarks>
    private async Task ReloadAsync()
    {
        _record = await _patients.GetRecordAsync(_id).ConfigureAwait(false);

        // Which rows still have a file behind them. Checked on load rather than per
        // render: it touches the filesystem once per document, and a render can happen
        // many times a second.
        _documentAvailability = _record is null
            ? new Dictionary<Guid, bool>()
            : await _patients.GetDocumentAvailabilityAsync(_record.Documents).ConfigureAwait(false);

        RaiseAllDerived();
    }

    private Task ArchiveAsync() => RunGuardedAsync(async () =>
    {
        await _patients.SetStatusAsync(_id, PatientStatus.Archived).ConfigureAwait(false);

        // Re-read rather than mutating the loaded copy: archiving is a real write, and
        // the screen should show what the database now holds.
        _record = await _patients.GetRecordAsync(_id).ConfigureAwait(false);

        RaiseAllDerived();
        ArchivePatientCommand.RaiseCanExecuteChanged();

        PresentPlanCommand.RaiseCanExecuteChanged();
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

    private void PresentPlan()
    {
        if (PlanToPresent is { } plan) _navigator.ToPlanPresentation(plan.Id);
    }


    private void StepAlertPage(int delta)
    {
        var next = Math.Clamp(_alertPage + delta, 0, AlertPageCount - 1);

        if (next == _alertPage) return;

        _alertPage = next;

        RaiseAlertList();
    }

    private void RaiseAlertList()
    {
        foreach (var name in new[]
        {
            nameof(Alerts), nameof(AlertPage), nameof(AlertPageIndex),
            nameof(AlertPageCount), nameof(AlertCount), nameof(AlertRemovalRefusal), nameof(Allergies), nameof(Medications),
            nameof(Conditions), nameof(HasCriticalAlerts),
        })
        {
            RaisePropertyChanged(name);
        }
    }

    private Task CloseConsentAsync(Guid consentId, bool withdrawn) => RunGuardedAsync(async () =>
    {
        var refusal = await _patients
            .CloseConsentAsync(consentId, withdrawn)
            .ConfigureAwait(false);

        _consentRefusal = refusal;

        await ReloadAsync().ConfigureAwait(false);

        RaiseConsent();
    });

    private void RaiseConsent()
    {
        foreach (var name in new[]
        {
            nameof(Consents), nameof(ConsentRefusal),
        })
        {
            RaisePropertyChanged(name);
        }
    }

    private static IReadOnlyList<PatientTags> TagsOf(PatientTags tags) =>
        Enum.GetValues<PatientTags>()
            .Where(tag => tag != PatientTags.None && tags.HasFlag(tag))
            .ToList();

    // ---- the details panel ------------------------------------------------

    /// <summary>What a core field says when the practice has not captured it.</summary>
    private const string NotRecorded = "Not recorded";

    /// <summary>A row that is always shown, blank or not. See <see cref="DetailGroups"/>.</summary>
    private static PatientDetailRow Always(string label, string? value) =>
        new(label, string.IsNullOrWhiteSpace(value) ? NotRecorded : value.Trim());

    /// <summary>A row that disappears when there is nothing to put in it.</summary>
    private static PatientDetailRow? Optional(string label, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new PatientDetailRow(label, value.Trim());

    /// <summary>
    /// Adds a group, unless every row in it was dropped — so "Funding" is absent for a
    /// patient who pays their own way rather than appearing with nothing under it.
    /// </summary>
    private static void Add(
        List<PatientDetailGroup> groups, string heading, params PatientDetailRow?[] rows)
    {
        var kept = rows.OfType<PatientDetailRow>().ToList();

        if (kept.Count > 0) groups.Add(new PatientDetailGroup(heading, kept));
    }

    /// <summary>Joins the parts of a compound value, skipping the ones that are blank.</summary>
    /// <remarks>
    /// Returns null when nothing survives, so <see cref="Optional"/> drops the row. A fund
    /// with no member number still shows the fund; a member number with no fund is a data
    /// error, and showing the number alone is how it gets noticed.
    /// </remarks>
    private static string? Joined(params string?[] parts)
    {
        var kept = parts
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .ToList();

        return kept.Count == 0 ? null : string.Join(" · ", kept);
    }

    /// <summary>"9 Nov 1978 · 47", or the date alone where the age cannot be worked out.</summary>
    private string BirthLine(PatientEntity patient) =>
        patient.DateOfBirth is null
            ? NotRecorded
            : Joined(MolargoFormat.Date(patient.DateOfBirth), Age?.ToString())!;

    /// <summary>The postal address on one line, or null where none is on file.</summary>
    private static string? Address(PatientEntity patient)
    {
        // Suburb, state and postcode read as one place — "Newtown NSW 2042" — so they are
        // spaced rather than separated by the dot that divides the street from the suburb.
        var locality = string.Join(" ", new[] { patient.Suburb, patient.State, patient.Postcode }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));

        return Joined(patient.AddressLine, locality);
    }

    /// <summary>The clinician the patient normally sees, or null where none is set.</summary>
    /// <remarks>
    /// Distinct from <see cref="ProviderName"/>, which answers with an em dash for a table
    /// cell that must stay aligned. Here an unknown provider means the row should not
    /// exist at all.
    /// </remarks>
    private string? PreferredProviderName =>
        Patient?.PreferredProviderId is { } id
            && (_record?.ProviderNames.TryGetValue(id, out var name) ?? false)
                ? name
                : null;

    /// <summary>
    /// Names one of the practice's sites, or null where the id names nothing.
    /// </summary>
    /// <remarks>
    /// Read from every site the practice runs, not the ones this user may switch to: a
    /// receptionist scoped to one site still has to be able to read that a patient
    /// normally attends another.
    /// </remarks>
    private string? SiteName(Guid? locationId) =>
        locationId is { } id
            ? _session.AllLocations.FirstOrDefault(site => site.Id == id)?.Name
            : null;

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
            nameof(Balance), nameof(NextVisitLabel), nameof(DetailGroups),
            nameof(HasCriticalAlerts),
            nameof(Allergies), nameof(Medications), nameof(Conditions), nameof(Alerts),
            nameof(AlertPage), nameof(AlertPageIndex), nameof(AlertPageCount),
            nameof(AlertCount), nameof(AlertRemovalRefusal),
            nameof(MedicalHistoryAnswers),
            nameof(IsMedicalHistoryOverdue), nameof(MedicalHistoryStatus),
            nameof(ReconfirmationLabel), nameof(IsReconfirmationSent), nameof(Plans),
            nameof(PlanToPresent), nameof(CanPresentPlan),
            nameof(InProgress), nameof(UpcomingAppointments), nameof(Documents),
            nameof(RecentDocuments), nameof(ConsentSummary), nameof(Communications),
            nameof(Billing), nameof(BillingOutstanding), nameof(Consents), nameof(Recalls),
            nameof(ConsentRefusal),
            nameof(ExportLabel), nameof(ErasureLabel), nameof(ArchiveLabel),
            nameof(CanArchive), nameof(IsExportQueued), nameof(IsErasureLogged),
            nameof(CanOpenDocuments),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
