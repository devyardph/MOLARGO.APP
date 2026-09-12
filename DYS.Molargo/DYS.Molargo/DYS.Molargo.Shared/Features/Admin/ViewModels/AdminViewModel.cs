using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Components;
using DYS.Molargo.Shared.Features.Admin.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Admin.ViewModels;

/// <summary>Which pane of the admin screen is showing.</summary>
public enum AdminTab
{
    Users = 0,
    Roles = 1,
    Sites = 2,
    Registrations = 3,
    Hr = 4,
    Licensing = 5,
    Audit = 6,
    Security = 7,
    Data = 8,
    Settings = 9,
}

/// <summary>
/// Staff, sites, registrations, the audit trail and the local database.
/// </summary>
public sealed class AdminViewModel : BaseViewModel, IDisposable
{
    private readonly IAdminService _admin;
    private readonly INotificationSettingsService _notificationSettings;
    private readonly IPrinterSettingsService _printerSettings;
    private readonly ISessionService _session;

    private AdminTab _tab = AdminTab.Users;
    private string? _lastAction;

    private IReadOnlyList<StaffRow> _staff = [];
    private Provider? _editing;
    private bool _isSettingPassword;
    private string? _staffPassword;
    private bool _showStaffPassword;
    private bool _editingIsNew;

    private IReadOnlyList<SiteRow> _sites = [];
    private Guid? _selectedSiteId;
    private PracticeLocation? _editingSite;
    private bool _editingSiteIsNew;

    private IReadOnlyList<ChairRow> _chairs = [];
    private Operatory? _editingChair;
    private bool _editingChairIsNew;

    private IReadOnlyList<RegistrationRow> _registrations = [];
    private Guid? _editingRegistrationId;
    private string? _draftAhpra;
    private DateOnly? _draftExpiry;

    private PagedResult<AuditRow> _audit = PagedResult<AuditRow>.Empty(50);
    private int _auditPage;
    private AuditAction? _auditFilter;

    private DatabaseInfo? _database;
    private BackupResult? _backup;

    private ProviderRole _selectedRole = ProviderRole.Dentist;

    private NotificationSettings? _notifications;
    private string? _draftAppPassword;
    private string? _testRecipient;
    private EmailResult? _testResult;

    private PrinterSettings? _printer;
    private TestPrint? _testPrint;

    public AdminViewModel(
        IAdminService admin,
        INotificationSettingsService notifications,
        IPrinterSettingsService printers,
        ISessionService session)
    {
        _admin = admin;
        _notificationSettings = notifications;
        _printerSettings = printers;
        _session = session;

        // Built once, in the constructor — never rebuilt per render.
        SelectTabCommand = new MvxAsyncCommand<AdminTab>(SelectTabAsync);

        NewStaffCommand = new MvxCommand(StartNewStaff);
        SelectStaffCommand = new MvxAsyncCommand<Guid>(SelectStaffAsync);
        CancelStaffCommand = new MvxCommand(() => ClearStaffEditor());
        SetStaffRoleCommand = new MvxCommand<ProviderRole>(SetStaffRole);
        StartStaffPasswordCommand = new MvxCommand(StartStaffPassword);
        CloseStaffPasswordCommand = new MvxCommand(CloseStaffPassword);
        ToggleShowStaffPasswordCommand = new MvxCommand(ToggleShowStaffPassword);
        SetStaffPasswordCommand = new MvxAsyncCommand(SetStaffPasswordAsync);
        ToggleOwnerCommand = new MvxCommand(ToggleOwner);
        TogglePermissionCommand = new MvxCommand<PracticePermissions>(TogglePermission);
        SetStaffSiteCommand = new MvxCommand<Guid>(SetStaffSite);
        SaveStaffCommand = new MvxAsyncCommand(SaveStaffAsync);
        DeactivateStaffCommand = new MvxAsyncCommand(() => SetActiveAsync(false));
        ReactivateStaffCommand = new MvxAsyncCommand(() => SetActiveAsync(true));

        SelectRoleCommand = new MvxCommand<ProviderRole>(SelectRole);

        SelectSiteCommand = new MvxAsyncCommand<Guid>(SelectSiteAsync);
        EditSiteCommand = new MvxAsyncCommand(EditSelectedSiteAsync);
        NewSiteCommand = new MvxCommand(StartNewSite);
        CancelSiteCommand = new MvxCommand(ClearSiteEditor);
        SaveSiteCommand = new MvxAsyncCommand(SaveSiteAsync);
        CloseSiteCommand = new MvxAsyncCommand(() => SetSiteActiveAsync(false));
        ReopenSiteCommand = new MvxAsyncCommand(() => SetSiteActiveAsync(true));

        NewChairCommand = new MvxCommand(StartNewChair);
        SelectChairCommand = new MvxAsyncCommand<Guid>(SelectChairAsync);
        CancelChairCommand = new MvxCommand(ClearChairEditor);
        SaveChairCommand = new MvxAsyncCommand(SaveChairAsync);
        ToggleChairSurgicalCommand = new MvxCommand(ToggleChairSurgical);
        SetChairActiveCommand = new MvxAsyncCommand<Guid>(ToggleChairActiveAsync);
        MoveChairLeftCommand = new MvxAsyncCommand<Guid>(id => MoveChairAsync(id, -1));
        MoveChairRightCommand = new MvxAsyncCommand<Guid>(id => MoveChairAsync(id, 1));

        StartRegistrationCommand = new MvxCommand<RegistrationRow>(row => StartRegistration(row!));
        CancelRegistrationCommand = new MvxCommand(() => StartRegistration(null));
        SaveRegistrationCommand = new MvxAsyncCommand(SaveRegistrationAsync);

        SetAuditFilterCommand = new MvxAsyncCommand<string>(SetAuditFilterAsync);
        NextAuditPageCommand = new MvxAsyncCommand(() => GoToAuditPageAsync(_auditPage + 1));
        PreviousAuditPageCommand = new MvxAsyncCommand(() => GoToAuditPageAsync(_auditPage - 1));
        BackupCommand = new MvxAsyncCommand(BackupAsync);

        ToggleEmailEnabledCommand = new MvxCommand(ToggleEmailEnabled);
        SaveNotificationsCommand = new MvxAsyncCommand(SaveNotificationsAsync);
        ClearAppPasswordCommand = new MvxAsyncCommand(ClearAppPasswordAsync);
        SendTestEmailCommand = new MvxAsyncCommand(SendTestEmailAsync);

        SetPrinterConnectionCommand = new MvxCommand<PrinterConnection>(SetPrinterConnection);
        SetReceiptPaperCommand = new MvxCommand<ReceiptPaper>(SetReceiptPaper);
        SetDocumentPaperCommand = new MvxCommand<DocumentPaper>(SetDocumentPaper);
        SavePrinterCommand = new MvxAsyncCommand(SavePrinterAsync);
        TestPrintCommand = new MvxAsyncCommand(TestPrintAsync);

        _session.Changed += OnSessionChanged;
    }

    public IMvxAsyncCommand<AdminTab> SelectTabCommand { get; }

    public IMvxCommand NewStaffCommand { get; }

    public IMvxAsyncCommand<Guid> SelectStaffCommand { get; }

    public IMvxCommand CancelStaffCommand { get; }

    public IMvxCommand<ProviderRole> SetStaffRoleCommand { get; }

    /// <summary>Opens the password box for the selected staff member.</summary>
    public IMvxCommand StartStaffPasswordCommand { get; }

    public IMvxCommand CloseStaffPasswordCommand { get; }

    public IMvxCommand ToggleShowStaffPasswordCommand { get; }

    /// <summary>Sets it, and clears any lockout with it.</summary>
    public IMvxAsyncCommand SetStaffPasswordCommand { get; }

    /// <summary>Makes this person an owner, or stops them being one.</summary>
    public IMvxCommand ToggleOwnerCommand { get; }

    /// <summary>Grants or withdraws one administrative area.</summary>
    public IMvxCommand<PracticePermissions> TogglePermissionCommand { get; }

    public IMvxCommand<Guid> SetStaffSiteCommand { get; }

    public IMvxAsyncCommand SaveStaffCommand { get; }

    public IMvxAsyncCommand DeactivateStaffCommand { get; }

    public IMvxAsyncCommand ReactivateStaffCommand { get; }

    public IMvxCommand<ProviderRole> SelectRoleCommand { get; }

    public IMvxAsyncCommand<Guid> SelectSiteCommand { get; }

    public IMvxAsyncCommand EditSiteCommand { get; }

    public IMvxCommand NewSiteCommand { get; }

    public IMvxCommand CancelSiteCommand { get; }

    public IMvxAsyncCommand SaveSiteCommand { get; }

    public IMvxAsyncCommand CloseSiteCommand { get; }

    public IMvxAsyncCommand ReopenSiteCommand { get; }

    public IMvxCommand NewChairCommand { get; }

    public IMvxAsyncCommand<Guid> SelectChairCommand { get; }

    public IMvxCommand CancelChairCommand { get; }

    public IMvxAsyncCommand SaveChairCommand { get; }

    public IMvxCommand ToggleChairSurgicalCommand { get; }

    public IMvxAsyncCommand<Guid> SetChairActiveCommand { get; }

    /// <summary>
    /// Moves one chair a column left, or right.
    /// </summary>
    /// <remarks>
    /// Two commands taking the chair's id, rather than one taking an id and a direction.
    /// The buttons are per row, so the id has to be the parameter, and a second parameter
    /// would have to be a tuple that reads as <c>(id, -1)</c> at every call site.
    /// </remarks>
    public IMvxAsyncCommand<Guid> MoveChairLeftCommand { get; }

    /// <inheritdoc cref="MoveChairLeftCommand"/>
    public IMvxAsyncCommand<Guid> MoveChairRightCommand { get; }

    public IMvxCommand<RegistrationRow> StartRegistrationCommand { get; }

    public IMvxCommand CancelRegistrationCommand { get; }

    public IMvxAsyncCommand SaveRegistrationCommand { get; }

    public IMvxAsyncCommand<string> SetAuditFilterCommand { get; }

    public IMvxAsyncCommand NextAuditPageCommand { get; }

    public IMvxAsyncCommand PreviousAuditPageCommand { get; }

    public IMvxAsyncCommand BackupCommand { get; }

    public IMvxCommand ToggleEmailEnabledCommand { get; }

    public IMvxAsyncCommand SaveNotificationsCommand { get; }

    public IMvxAsyncCommand ClearAppPasswordCommand { get; }

    public IMvxAsyncCommand SendTestEmailCommand { get; }

    public IMvxCommand<PrinterConnection> SetPrinterConnectionCommand { get; }

    public IMvxCommand<ReceiptPaper> SetReceiptPaperCommand { get; }

    public IMvxCommand<DocumentPaper> SetDocumentPaperCommand { get; }

    public IMvxAsyncCommand SavePrinterCommand { get; }

    public IMvxAsyncCommand TestPrintCommand { get; }

    public override Task Initialize() => LoadAsync();

    public AdminTab Tab => _tab;

    public string? LastAction => _lastAction;

    /// <summary>
    /// Who is signed in, for the header.
    /// </summary>
    /// <remarks>
    /// A real signed-in person now. This used to name whichever clinician the session
    /// picked for want of a sign-in, and the header said so — both are stale since the
    /// sign-in screen landed.
    /// </remarks>
    public string ActingAs => _session.UserDisplayName ?? "nobody";

    // ---- staff -----------------------------------------------------------

    public IReadOnlyList<StaffRow> Staff => _staff;

    public int ActiveStaffCount => _staff.Count(row => row.IsActive);

    public Provider? Editing => _editing;

    public bool HasEditor => _editing is not null;

    public bool EditingIsNew => _editingIsNew;

    public string EditorTitle => _editing is null
        ? "Staff member"
        : _editingIsNew ? "New staff member" : _editing.FullName;

    public bool IsSelected(Guid providerId) => _editing?.Id == providerId;

    /// <summary>Every role a staff member can hold, in the enum's order.</summary>
    public static readonly ProviderRole[] Roles = ProviderRoles.Assignable;

    /// <summary>
    /// Every site in the clinic, for assigning a staff member to one.
    /// </summary>
    /// <remarks>
    /// The full list, not the signed-in user's own site. The app bar shows only the site
    /// its user works at, and reusing that here would make it impossible to put anybody at
    /// a site the administrator does not personally work at — which is most of them.
    /// </remarks>
    public IReadOnlyList<SessionLocation> Locations => _session.AllLocations;

    public string? StaffFirstName
    {
        get => _editing?.FirstName;
        set
        {
            if (_editing is null) return;

            _editing.FirstName = value ?? string.Empty;
            RaisePropertyChanged();
        }
    }

    public string? StaffLastName
    {
        get => _editing?.LastName;
        set
        {
            if (_editing is null) return;

            _editing.LastName = value ?? string.Empty;
            RaisePropertyChanged();
        }
    }

    public string? StaffDisplayName
    {
        get => _editing?.DisplayName;
        set
        {
            if (_editing is null) return;

            _editing.DisplayName = string.IsNullOrWhiteSpace(value) ? null : value;
            RaisePropertyChanged();
        }
    }

    public string? StaffEmail
    {
        get => _editing?.Email;
        set
        {
            if (_editing is null) return;

            _editing.Email = string.IsNullOrWhiteSpace(value) ? null : value;
            RaisePropertyChanged();
        }
    }

    public string? StaffProviderNumber
    {
        get => _editing?.ProviderNumber;
        set
        {
            if (_editing is null) return;

            _editing.ProviderNumber = string.IsNullOrWhiteSpace(value) ? null : value;
            RaisePropertyChanged();
        }
    }

    public ProviderRole? StaffRole => _editing?.Role;

    // ---- password ---------------------------------------------------------

    /// <summary>
    /// Whether the password box is showing.
    /// </summary>
    /// <remarks>
    /// Behind a button rather than always present. A password field on every staff record
    /// invites an administrator to set one where none was asked for, and an unexplained
    /// new password is a colleague locked out until somebody works out why.
    /// </remarks>
    public bool IsSettingPassword => _isSettingPassword;

    /// <summary>
    /// Whether the signed-in person may set this staff member's password.
    /// </summary>
    /// <remarks>
    /// The same permission that governs the rest of the Users pane, read for rendering
    /// only — AdminService refuses the write regardless. Kept as its own property rather
    /// than reusing CanGrantAccess: granting access is owner-only, and setting a password
    /// is not, so one standing in for the other would have hidden the practice's recovery
    /// route from the manager it was meant for.
    ///
    /// An owner's own password is the exception, and takes ownership to reset. Otherwise
    /// ManageStaff would reach ownership the long way round: set the owner's password, sign
    /// in as them, take everything.
    /// </remarks>
    public bool CanGrantStaffPassword =>
        _session.Can(PracticePermissions.ManageStaff)
        && (!EditorIsOwner || _session.IsOwner);

    /// <summary>When this person's password was last set, or null if it never has been.</summary>
    public string? EditorLastPasswordChange => _editing?.PasswordUpdatedUtc is { } when
        ? MolargoFormat.Date(DateOnly.FromDateTime(when.ToLocalTime()))
        : null;

    /// <summary>
    /// The new password, as typed.
    /// </summary>
    /// <remarks>
    /// Held on the view model only as long as the panel is open, and cleared the moment it
    /// closes or the save succeeds — see <see cref="CloseStaffPassword"/>. A view model
    /// outlives a keystroke, and this is the one field worth not leaving lying in one.
    /// </remarks>
    public string? StaffPassword
    {
        get => _staffPassword;
        set
        {
            if (SetProperty(ref _staffPassword, value)) RaisePropertyChanged(nameof(CanSetPassword));
        }
    }

    /// <summary>True while the typed password is shown as text rather than dots.</summary>
    /// <remarks>
    /// Worth having, because the person setting it has to read it out or write it down for
    /// somebody else — there is no email loop to send it through.
    /// </remarks>
    public bool ShowStaffPassword => _showStaffPassword;

    public bool CanSetPassword =>
        !IsBusy
        && _editing is not null
        && (_staffPassword?.Length ?? 0) >= MinimumTypedPassword;

    /// <summary>
    /// Mirrors the service's minimum so the button is disabled rather than refused.
    /// </summary>
    /// <remarks>
    /// Duplicated deliberately, and the service still checks: this one is for the button's
    /// state, and the one that matters is the one a caller cannot skip.
    /// </remarks>
    private const int MinimumTypedPassword = 10;

    public string PasswordHint =>
        $"At least {MinimumTypedPassword} characters. Tell them out of band — nothing here "
        + "emails it.";

    private void StartStaffPassword()
    {
        if (_editing is null) return;

        _isSettingPassword = true;
        _staffPassword = null;
        _showStaffPassword = false;
        _lastAction = null;

        RaiseAll();
    }

    private void CloseStaffPassword()
    {
        _isSettingPassword = false;
        _staffPassword = null;
        _showStaffPassword = false;

        RaiseAll();
    }

    private void ToggleShowStaffPassword()
    {
        _showStaffPassword = !_showStaffPassword;

        RaisePropertyChanged(nameof(ShowStaffPassword));
    }

    private Task SetStaffPasswordAsync() => RunGuardedAsync(async () =>
    {
        if (_editing is null || _staffPassword is null) return;

        var name = _editing.FullName;

        var result = await _admin
            .SetStaffPasswordAsync(_editing.Id, _staffPassword)
            .ConfigureAwait(false);

        if (result.Refusal is { Length: > 0 } refusal)
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        // Cleared before anything else is raised, so the typed value does not survive the
        // render that reports success.
        _staffPassword = null;
        _showStaffPassword = false;
        _isSettingPassword = false;

        // Which of the two happened, said plainly. "Emailed" and "you will have to tell
        // them" lead to different next actions, and a single cheerful line covering both
        // is how somebody walks away from a colleague who never found out.
        _lastAction = result.Emailed
            ? $"Password set for {name}, and they have been emailed. The email does not "
                + "contain the password — read it out to them."
            : $"Password set for {name}. They were not emailed — "
                + $"{result.MailProblem ?? "no reason given"}. Tell them yourself.";

        await LoadTabAsync().ConfigureAwait(false);
    });

    // ---- access ----------------------------------------------------------

    /// <summary>
    /// Whether the signed-in person may change who owns the practice and who administers
    /// what.
    /// </summary>
    /// <remarks>
    /// Owner only, and deliberately not one of the grantable permissions: if it were, the
    /// holder could grant themselves the rest and then ownership, and the whole scheme
    /// would be decoration. Read from the session for rendering; <c>IPracticeGuard</c> is
    /// what refuses the write.
    /// </remarks>
    public bool CanGrantAccess => PracticeAccess.CanGrantAccess(_session.IsOwner);

    public bool EditorIsOwner => _editing?.IsOwner ?? false;

    /// <summary>
    /// True where the person being edited holds <paramref name="permission"/> explicitly.
    /// </summary>
    /// <remarks>
    /// Explicitly, not effectively — an owner's boxes read unticked even though ownership
    /// grants everything. That is deliberate: the boxes record what was granted, and
    /// showing them all ticked for an owner would suggest demoting them leaves that access
    /// behind. The panel says "owns the practice" instead, which is the real reason.
    /// </remarks>
    public bool EditorHas(PracticePermissions permission) =>
        _editing?.Permissions.HasFlag(permission) ?? false;

    /// <summary>How many administrative areas this person has been granted.</summary>
    public int EditorPermissionCount =>
        PracticeAccess.Grantable.Count(EditorHas);

    /// <summary>
    /// True where this is the practice's only remaining owner.
    /// </summary>
    /// <remarks>
    /// Drives the disabled state on the ownership toggle. Demoting the last owner locks
    /// the practice out of its own settings permanently — there is no password reset and no
    /// vendor override — so the service refuses it and the screen does not offer it.
    /// </remarks>
    public bool EditorIsLastOwner =>
        _editing is { IsOwner: true }
        && _staff.Count(row => row.IsOwner && row.IsActive) <= 1;

    /// <summary>
    /// True where the ownership switch cannot be moved at all.
    /// </summary>
    /// <remarks>
    /// One property rather than the two conditions repeated across the indicator, its
    /// label and both disabled attributes — four places for them to disagree, and the
    /// disagreement would look like a control that highlights but does nothing.
    /// </remarks>
    public bool OwnerToggleIsLocked => EditorIsOwner && EditorIsLastOwner;

    private void ToggleOwner()
    {
        if (_editing is null || !CanGrantAccess) return;

        // Refused here as well as in the service. Both, not either: the service is the
        // boundary, and this is what stops the screen offering a click that can only fail.
        if (_editing.IsOwner && EditorIsLastOwner) return;

        _editing.IsOwner = !_editing.IsOwner;

        RaisePropertyChanged(nameof(EditorIsOwner));
        RaisePropertyChanged(nameof(EditorIsLastOwner));
        RaisePropertyChanged(nameof(OwnerToggleIsLocked));
        RaisePropertyChanged(nameof(EditorPermissionCount));

        // The permission boxes below need no notification of their own. They are drawn
        // from EditorHas, which is a method rather than a bound property, and any
        // PropertyChanged re-renders the whole component — which is what repaints their
        // inert state once ownership covers them.
    }

    private void TogglePermission(PracticePermissions permission)
    {
        if (_editing is null || !CanGrantAccess) return;

        _editing.Permissions = _editing.Permissions.HasFlag(permission)
            ? _editing.Permissions & ~permission
            : _editing.Permissions | permission;

        RaisePropertyChanged(nameof(EditorPermissionCount));
    }

    /// <summary>The words staff use for one administrative permission.</summary>
    public static string PermissionLabel(PracticePermissions permission) => permission switch
    {
        PracticePermissions.ManageStaff => "Staff and access",
        PracticePermissions.ManagePricing => "Fees and the item catalogue",
        PracticePermissions.ManageSites => "Sites and chairs",
        PracticePermissions.ManageInventory => "Stock costs and ordering",
        PracticePermissions.ManageSettings => "Mail, notifications and printers",
        _ => permission.ToString(),
    };

    /// <summary>What granting one permission actually lets somebody do.</summary>
    /// <remarks>
    /// Spelled out beside each box rather than left to the label. "Staff and access" does
    /// not obviously include setting another person's password, and setting a password is
    /// the ability to sign in as them — an owner ticking a box deserves to know that
    /// before they tick it, not after.
    /// </remarks>
    public static string PermissionNote(PracticePermissions permission) => permission switch
    {
        PracticePermissions.ManageStaff =>
            "Add staff, change their details and set their passwords. Setting a password "
            + "means being able to sign in as that person, so this is the strongest of "
            + "these.",
        PracticePermissions.ManagePricing =>
            "Change fees, add and withdraw items, and set a different price per site. "
            + "Everybody can already read the catalogue to quote from.",
        PracticePermissions.ManageSites =>
            "Add and edit locations, and the chairs that make up each site's diary.",
        PracticePermissions.ManageInventory =>
            "Set cost prices and suppliers, and place orders. Receiving stock and marking "
            + "it used needs no permission.",
        PracticePermissions.ManageSettings =>
            "The practice's mail account, the notification rules and the printers. The "
            + "mail app password is stored in plain text, so this grants sending mail as "
            + "the practice.",
        _ => string.Empty,
    };

    public Guid? StaffSiteId => _editing?.PrimaryLocationId;

    public bool StaffIsActive => _editing?.IsActive ?? false;

    /// <summary>
    /// Whether the editor's role can author clinical records.
    /// </summary>
    /// <remarks>
    /// Shown while choosing the role, because it is the consequence that matters and it is
    /// invisible otherwise: a non-clinical role cannot sign a note, write a script or be
    /// the session's author, which is what made every clinical record in the app read as
    /// the receptionist's.
    /// </remarks>
    public bool EditorIsClinical => _editing is not null && ProviderRoles.IsClinical(_editing.Role);

    public bool EditorIsBookable => _editing is not null && ProviderRoles.IsBookable(_editing.Role);

    // ---- roles -----------------------------------------------------------

    public ProviderRole SelectedRole => _selectedRole;

    /// <summary>
    /// What the chosen role actually changes today.
    /// </summary>
    /// <remarks>
    /// Two behaviours, not a permission matrix. The design shows read/write toggles per
    /// module, and there is no permission model behind them — a screen implying Reception
    /// cannot open a clinical note would be describing enforcement that does not exist.
    /// </remarks>
    public bool SelectedRoleIsClinical => ProviderRoles.IsClinical(_selectedRole);

    public bool SelectedRoleIsBookable => ProviderRoles.IsBookable(_selectedRole);

    public int SelectedRoleStaffCount => _staff.Count(row => row.Role == _selectedRole);

    // ---- sites -----------------------------------------------------------

    public IReadOnlyList<SiteRow> Sites => _sites;

    /// <summary>Trading hours, read from the one place the whole app uses.</summary>
    public static string TradingHours =>
        $"{PracticeHours.Open:h:mm tt} – {PracticeHours.Close:h:mm tt}, Monday to Saturday";

    public Guid? SelectedSiteId => _selectedSiteId;

    public bool IsSelectedSite(Guid locationId) => _selectedSiteId == locationId;

    public SiteRow? SelectedSite =>
        _sites.FirstOrDefault(row => row.LocationId == _selectedSiteId);

    public PracticeLocation? EditingSite => _editingSite;

    public bool HasSiteEditor => _editingSite is not null;

    public bool EditingSiteIsNew => _editingSiteIsNew;

    public string SiteEditorTitle => _editingSite is null
        ? "Site"
        : _editingSiteIsNew ? "New site" : _editingSite.Name;

    /// <summary>
    /// Whether the site being edited is open.
    /// </summary>
    /// <remarks>
    /// Read from the row rather than the entity, so it reflects what the database last
    /// said rather than an unsaved edit — this is what the Close and Reopen buttons key
    /// off, and they act on the stored state.
    /// </remarks>
    public bool EditingSiteIsOpen => SelectedSite?.IsActive ?? true;

    public string? SiteName
    {
        get => _editingSite?.Name;
        set
        {
            if (_editingSite is null) return;

            _editingSite.Name = value ?? string.Empty;
            RaisePropertyChanged();
        }
    }

    public string? SiteShortName
    {
        get => _editingSite?.ShortName;
        set
        {
            if (_editingSite is null) return;

            _editingSite.ShortName = value;
            RaisePropertyChanged();
        }
    }

    public string? SiteAddressLine
    {
        get => _editingSite?.AddressLine;
        set
        {
            if (_editingSite is null) return;

            _editingSite.AddressLine = value;
            RaisePropertyChanged();
        }
    }

    public string? SiteSuburb
    {
        get => _editingSite?.Suburb;
        set
        {
            if (_editingSite is null) return;

            _editingSite.Suburb = value;
            RaisePropertyChanged();
        }
    }

    public string? SiteState
    {
        get => _editingSite?.State;
        set
        {
            if (_editingSite is null) return;

            _editingSite.State = value;
            RaisePropertyChanged();
        }
    }

    public string? SitePostcode
    {
        get => _editingSite?.Postcode;
        set
        {
            if (_editingSite is null) return;

            _editingSite.Postcode = value;
            RaisePropertyChanged();
        }
    }

    public string? SitePhone
    {
        get => _editingSite?.Phone;
        set
        {
            if (_editingSite is null) return;

            _editingSite.Phone = value;
            RaisePropertyChanged();
        }
    }

    public string? SiteEmail
    {
        get => _editingSite?.Email;
        set
        {
            if (_editingSite is null) return;

            _editingSite.Email = value;
            RaisePropertyChanged();
        }
    }

    public string? SiteAbn
    {
        get => _editingSite?.Abn;
        set
        {
            if (_editingSite is null) return;

            _editingSite.Abn = value;
            RaisePropertyChanged();
        }
    }

    public string? SiteTimeZoneId
    {
        get => _editingSite?.TimeZoneId;
        set
        {
            if (_editingSite is null) return;

            _editingSite.TimeZoneId = value ?? string.Empty;
            RaisePropertyChanged();
        }
    }

    /// <summary>
    /// The zones offered, rather than every zone the machine knows.
    /// </summary>
    /// <remarks>
    /// A short list because a practice's sites are all in one country, and the full set is
    /// several hundred entries. Typed values still work — the service checks any id
    /// against the machine, so this is a shortcut rather than a restriction.
    /// </remarks>
    public static readonly string[] TimeZones =
    [
        "Australia/Sydney",
        "Australia/Melbourne",
        "Australia/Brisbane",
        "Australia/Adelaide",
        "Australia/Perth",
        "Australia/Hobart",
        "Australia/Darwin",
    ];

    // ---- chairs ----------------------------------------------------------

    public IReadOnlyList<ChairRow> Chairs => _chairs;

    public int ChairsInService => _chairs.Count(row => row.IsActive);

    public Operatory? EditingChair => _editingChair;

    public bool HasChairEditor => _editingChair is not null;

    public bool EditingChairIsNew => _editingChairIsNew;

    public bool IsEditingChair(Guid operatoryId) => _editingChair?.Id == operatoryId;

    public string? ChairName
    {
        get => _editingChair?.Name;
        set
        {
            if (_editingChair is null) return;

            _editingChair.Name = value ?? string.Empty;
            RaisePropertyChanged();
        }
    }

    public bool ChairIsSurgical => _editingChair?.IsSurgical ?? false;

    /// <summary>Whether a chair can be moved further left in the diary.</summary>
    /// <remarks>
    /// Both of these consider only chairs in service. An out-of-service chair has no
    /// column, so moving one past it would be a press that changed nothing visible.
    /// </remarks>
    public bool CanMoveChairLeft(Guid operatoryId) =>
        InServiceOrder(operatoryId) > 0;

    public bool CanMoveChairRight(Guid operatoryId)
    {
        var at = InServiceOrder(operatoryId);

        return at >= 0 && at < ChairsInService - 1;
    }

    private int InServiceOrder(Guid operatoryId)
    {
        var inService = _chairs.Where(row => row.IsActive).ToList();

        return inService.FindIndex(row => row.OperatoryId == operatoryId);
    }

    // ---- registrations ---------------------------------------------------

    public IReadOnlyList<RegistrationRow> Registrations => _registrations;

    public int BlockedCount => _registrations.Count(row => !row.CanSign);

    public int LapsingCount => _registrations.Count(row => row.IsExpiringSoon);

    public int UncheckedCount => _registrations.Count(row => row.ExpiryUnknown);

    public bool IsEditingRegistration(Guid providerId) => _editingRegistrationId == providerId;

    public string? DraftAhpra
    {
        get => _draftAhpra;
        set => SetProperty(ref _draftAhpra, value);
    }

    public DateOnly? DraftExpiry
    {
        get => _draftExpiry;
        set => SetProperty(ref _draftExpiry, value);
    }

    // ---- audit -----------------------------------------------------------

    public IReadOnlyList<AuditRow> Audit => _audit.Items;

    public int AuditPage => _audit.Page;

    public int AuditPageCount => _audit.PageCount;

    public bool HasNextAuditPage => _audit.Page + 1 < _audit.PageCount;

    public bool HasPreviousAuditPage => _audit.Page > 0;

    /// <summary>
    /// "51–100 of 312 entries", so the count itself says there is more.
    /// </summary>
    /// <remarks>
    /// The whole reason this screen was changed. A capped list and a complete one look
    /// identical, and the reader has no way to tell which they are looking at — least of
    /// all on an audit trail, where the entry being searched for is usually the old one.
    /// </remarks>
    public string AuditRangeLabel
    {
        get
        {
            if (_audit.TotalCount == 0) return "No entries";

            var first = (_audit.Page * _audit.PageSize) + 1;
            var last = Math.Min(first + _audit.Items.Count - 1, _audit.TotalCount);

            // En dash. It is a range, not a subtraction.
            return $"{first}–{last} of {_audit.TotalCount} entries";
        }
    }

    public AuditAction? AuditFilter => _auditFilter;

    /// <summary>The filters the pane offers, plus "everything".</summary>
    public static readonly AuditAction?[] AuditFilters =
    [
        null,
        AuditAction.Created,
        AuditAction.Updated,
        AuditAction.Deleted,
        AuditAction.Exported,

        // Worth a chip of its own. "Nothing is arriving" is a complaint a practice makes
        // days after the fact, and this is the filter that answers it in one click.
        AuditAction.NotificationFailed,
    ];

    public static string FilterLabel(AuditAction? action) =>
        action is { } value ? AdminService.ActionLabel(value) : "Everything";

    // ---- data ------------------------------------------------------------

    public DatabaseInfo? Database => _database;

    public BackupResult? Backup => _backup;

    // ---- notification settings -------------------------------------------

    public NotificationSettings? Notifications => _notifications;

    public bool EmailEnabled => _notifications?.EmailEnabled ?? false;

    /// <summary>
    /// True where a password is stored, without saying what it is.
    /// </summary>
    /// <remarks>
    /// The screen shows this instead of the password. A field pre-filled with the stored
    /// secret has to render it into the page to do so, and then anything that can read the
    /// DOM can read the credential.
    /// </remarks>
    public bool HasAppPassword => !string.IsNullOrWhiteSpace(_notifications?.AppPassword);

    public bool IsEmailConfigured => _notifications?.IsConfigured ?? false;

    public string? SenderAddress
    {
        get => _notifications?.SenderAddress;
        set
        {
            if (_notifications is null) return;

            _notifications.SenderAddress = value;
            RaisePropertyChanged();
        }
    }

    public string? SenderName
    {
        get => _notifications?.SenderName;
        set
        {
            if (_notifications is null) return;

            _notifications.SenderName = value;
            RaisePropertyChanged();
        }
    }

    public string? ReplyToAddress
    {
        get => _notifications?.ReplyToAddress;
        set
        {
            if (_notifications is null) return;

            _notifications.ReplyToAddress = value;
            RaisePropertyChanged();
        }
    }

    public string? AlertsToAddress
    {
        get => _notifications?.AlertsToAddress;
        set
        {
            if (_notifications is null) return;

            _notifications.AlertsToAddress = value;
            RaisePropertyChanged();
        }
    }

    public string? DailySummaryRecipients
    {
        get => _notifications?.DailySummaryRecipients;
        set
        {
            if (_notifications is null) return;

            _notifications.DailySummaryRecipients = value;
            RaisePropertyChanged();
        }
    }

    public TimeOnly DailySummaryAt
    {
        get => _notifications?.DailySummaryAt ?? new TimeOnly(19, 0);
        set
        {
            if (_notifications is null) return;

            _notifications.DailySummaryAt = value;
            RaisePropertyChanged();
        }
    }

    public bool NotifyReceipts => _notifications?.NotifyReceipts ?? false;

    public bool NotifyLowStock => _notifications?.NotifyLowStock ?? false;

    public bool NotifyDailySummary => _notifications?.NotifyDailySummary ?? false;

    /// <summary>
    /// Flips one notification on or off.
    /// </summary>
    /// <remarks>
    /// Keyed by name rather than three commands, because the pane renders them from a list
    /// and three near-identical commands is three places for one to be wired to the wrong
    /// flag.
    /// </remarks>
    public void ToggleNotification(string which)
    {
        if (_notifications is null) return;

        switch (which)
        {
            case nameof(NotifyReceipts):
                _notifications.NotifyReceipts = !_notifications.NotifyReceipts;
                RaisePropertyChanged(nameof(NotifyReceipts));
                break;

            case nameof(NotifyLowStock):
                _notifications.NotifyLowStock = !_notifications.NotifyLowStock;
                RaisePropertyChanged(nameof(NotifyLowStock));
                break;

            case nameof(NotifyDailySummary):
                _notifications.NotifyDailySummary = !_notifications.NotifyDailySummary;
                RaisePropertyChanged(nameof(NotifyDailySummary));
                break;
        }
    }

    /// <summary>Whether the typed password is shown in the clear while being entered.</summary>
    /// <remarks>
    /// Only ever reveals what the person is typing now. The stored password is never
    /// rendered into the page at all, so there is nothing here that could reveal it.
    /// </remarks>
    public bool ShowAppPassword { get; private set; }

    public void ToggleShowAppPassword()
    {
        ShowAppPassword = !ShowAppPassword;

        RaisePropertyChanged(nameof(ShowAppPassword));
    }

    public string? SmtpHost
    {
        get => _notifications?.SmtpHost;
        set
        {
            if (_notifications is null) return;

            _notifications.SmtpHost = value ?? string.Empty;
            RaisePropertyChanged();
        }
    }

    public int SmtpPort
    {
        get => _notifications?.SmtpPort ?? 587;
        set
        {
            if (_notifications is null) return;

            _notifications.SmtpPort = value;
            RaisePropertyChanged();
        }
    }

    /// <summary>
    /// The password as typed. Never populated from what is stored.
    /// </summary>
    /// <remarks>
    /// Left empty means "keep the one you have", which is what lets the other fields be
    /// saved without the password making a round trip through the page.
    /// </remarks>
    public string? DraftAppPassword
    {
        get => _draftAppPassword;
        set => SetProperty(ref _draftAppPassword, value);
    }

    public string? TestRecipient
    {
        get => _testRecipient;
        set => SetProperty(ref _testRecipient, value);
    }

    public EmailResult? TestResult => _testResult;

    /// <summary>What the last recorded test said, from an earlier session.</summary>
    public string? LastTestSummary
    {
        get
        {
            if (_notifications?.LastTestUtc is not { } when) return null;

            var verdict = _notifications.LastTestSucceeded ? "succeeded" : "failed";

            return $"Last test {verdict} — {when.ToLocalTime():d MMM yyyy, h:mm tt}";
        }
    }

    public bool LastTestSucceeded => _notifications?.LastTestSucceeded ?? false;

    public string? LastTestDetail => _notifications?.LastTestResult;

    public bool CanSendTest =>
        !IsBusy && IsEmailConfigured && !string.IsNullOrWhiteSpace(_testRecipient);

    // ---- printer settings -------------------------------------------------

    public PrinterSettings? Printer => _printer;

    public PrinterConnection PrinterConnectionKind =>
        _printer?.Connection ?? PrinterConnection.Network;

    /// <summary>The three ways a receipt printer can be attached, in the enum's order.</summary>
    public static readonly PrinterConnection[] Connections =
        Enum.GetValues<PrinterConnection>();

    public static readonly ReceiptPaper[] Papers = Enum.GetValues<ReceiptPaper>();

    public static readonly DocumentPaper[] DocumentPapers = Enum.GetValues<DocumentPaper>();

    /// <summary>
    /// What the address field is called for the chosen connection.
    /// </summary>
    /// <remarks>
    /// Read from the service so the label on screen and the words in its refusal are the
    /// same three strings. Separate copies drift, and then a refusal names a field that is
    /// not on the screen.
    /// </remarks>
    public string DeviceAddressLabel =>
        PrinterSettingsService.AddressLabel(PrinterConnectionKind);

    /// <summary>The port only matters for a network printer, so the field only shows there.</summary>
    public bool ShowsNetworkPort => PrinterConnectionKind == PrinterConnection.Network;

    /// <summary>
    /// True where a Bluetooth printer is chosen — which no browser can reach.
    /// </summary>
    /// <remarks>
    /// Surfaced so the pane can say it at the moment the choice is made, rather than
    /// letting a practice configure a printer the head they are using could never open.
    /// </remarks>
    public bool ConnectionNeedsDeviceHead =>
        PrinterConnectionKind is PrinterConnection.Bluetooth or PrinterConnection.Usb;

    public string? DeviceAddress
    {
        get => _printer?.DeviceAddress;
        set
        {
            if (_printer is null) return;

            _printer.DeviceAddress = value;
            RaisePropertyChanged();
        }
    }

    public int NetworkPort
    {
        get => _printer?.NetworkPort ?? 9100;
        set
        {
            if (_printer is null) return;

            _printer.NetworkPort = value;
            RaisePropertyChanged();
        }
    }

    public string? ReceiptPrinterName
    {
        get => _printer?.ReceiptPrinterName;
        set
        {
            if (_printer is null) return;

            _printer.ReceiptPrinterName = value;
            RaisePropertyChanged();
        }
    }

    public ReceiptPaper ReceiptPaperSize => _printer?.Paper ?? ReceiptPaper.Roll80mm;

    /// <summary>How many characters fit on a receipt line at the chosen roll width.</summary>
    /// <remarks>
    /// Shown beside the paper choice because it is the consequence, and it is the reason
    /// the two widths are not interchangeable: 48 characters of description become 32.
    /// </remarks>
    public int ReceiptColumns => _printer?.ReceiptColumns ?? 48;

    public bool PrintReceiptAutomatically => _printer?.PrintReceiptAutomatically ?? false;

    public bool OpenCashDrawer => _printer?.OpenCashDrawer ?? false;

    public bool PrintLogo => _printer?.PrintLogo ?? false;

    public string? ReceiptFooter
    {
        get => _printer?.ReceiptFooter;
        set
        {
            if (_printer is null) return;

            _printer.ReceiptFooter = value;
            RaisePropertyChanged();
        }
    }

    public string? DocumentPrinterName
    {
        get => _printer?.DocumentPrinterName;
        set
        {
            if (_printer is null) return;

            _printer.DocumentPrinterName = value;
            RaisePropertyChanged();
        }
    }

    public DocumentPaper DocumentPaperSize => _printer?.DocumentPaper ?? DocumentPaper.A4;

    public int PrescriptionCopies
    {
        get => _printer?.PrescriptionCopies ?? 1;
        set
        {
            if (_printer is null) return;

            _printer.PrescriptionCopies = value;
            RaisePropertyChanged();
        }
    }

    public bool PrintPrescriberDetails => _printer?.PrintPrescriberDetails ?? true;

    /// <summary>The rendered test receipt, and the note saying it was not printed.</summary>
    public TestPrint? TestPrintResult => _testPrint;

    /// <summary>
    /// Flips one printer switch on or off, keyed by name.
    /// </summary>
    /// <remarks>
    /// Same shape as <see cref="ToggleNotification"/>, for the same reason: the pane
    /// renders these from a list, and four near-identical commands is four places for one
    /// to be wired to the wrong flag.
    /// </remarks>
    public void TogglePrinterOption(string which)
    {
        if (_printer is null) return;

        switch (which)
        {
            case nameof(PrintReceiptAutomatically):
                _printer.PrintReceiptAutomatically = !_printer.PrintReceiptAutomatically;
                RaisePropertyChanged(nameof(PrintReceiptAutomatically));
                break;

            case nameof(OpenCashDrawer):
                _printer.OpenCashDrawer = !_printer.OpenCashDrawer;
                RaisePropertyChanged(nameof(OpenCashDrawer));
                break;

            case nameof(PrintLogo):
                _printer.PrintLogo = !_printer.PrintLogo;
                RaisePropertyChanged(nameof(PrintLogo));
                break;

            case nameof(PrintPrescriberDetails):
                _printer.PrintPrescriberDetails = !_printer.PrintPrescriberDetails;
                RaisePropertyChanged(nameof(PrintPrescriberDetails));
                break;
        }
    }

    // ---- loading ---------------------------------------------------------

    private Task LoadAsync() => RunGuardedAsync(LoadTabAsync);

    private async Task SelectTabAsync(AdminTab tab)
    {
        _tab = tab;
        _lastAction = null;
        _backup = null;

        await RaisePropertyChanged(nameof(Tab)).ConfigureAwait(false);
        await LoadAsync().ConfigureAwait(false);
    }

    private async Task LoadTabAsync()
    {
        switch (_tab)
        {
            case AdminTab.Users:
            case AdminTab.Roles:
                _staff = await _admin.GetStaffAsync().ConfigureAwait(false);
                break;

            case AdminTab.Sites:
                _sites = await _admin.GetSitesAsync().ConfigureAwait(false);

                // Settles on a site so the chairs list below has something to show. The
                // open one the user is working at, since that is the one whose chairs they
                // are most likely to be changing.
                _selectedSiteId ??= _sites
                    .FirstOrDefault(row => row.LocationId == _session.LocationId)
                    ?.LocationId
                    ?? _sites.FirstOrDefault()?.LocationId;

                if (_selectedSiteId is { } siteId)
                {
                    _chairs = await _admin.GetChairsAsync(siteId).ConfigureAwait(false);
                }

                break;

            case AdminTab.Registrations:
                _registrations = await _admin.GetRegistrationsAsync().ConfigureAwait(false);
                break;

            case AdminTab.Audit:
                _audit = await _admin
                    .GetAuditAsync(_auditFilter, _auditPage)
                    .ConfigureAwait(false);
                break;

            case AdminTab.Data:
                _database = await _admin.GetDatabaseInfoAsync().ConfigureAwait(false);
                break;

            case AdminTab.Settings:
                _notifications = await _notificationSettings.GetAsync().ConfigureAwait(false);

                // Seeded with the practice inbox, so the commonest test — "does this
                // reach us" — needs nothing typed.
                _testRecipient ??= _notifications.AlertsToAddress
                    ?? _notifications.SenderAddress;

                // Read once and then left alone. Saving the mail account beside it also
                // runs through here, and re-reading would throw away printer fields
                // somebody had typed but not yet saved.
                _printer ??= await _printerSettings.GetAsync().ConfigureAwait(false);
                break;
        }

        RaiseAll();
    }

    // ---- staff -----------------------------------------------------------

    private void StartNewStaff()
    {
        _editing = new Provider
        {
            Role = ProviderRole.Dentist,
            PrimaryLocationId = _session.LocationId,
            IsActive = true,
        };

        _editingIsNew = true;
        _lastAction = null;

        RaiseAll();
    }

    private Task SelectStaffAsync(Guid providerId) => RunGuardedAsync(async () =>
    {
        _editing = await _admin.GetStaffMemberAsync(providerId).ConfigureAwait(false);
        _editingIsNew = false;
        _lastAction = null;

        RaiseAll();
    });

    private void ClearStaffEditor()
    {
        _editing = null;
        _editingIsNew = false;

        RaiseAll();
    }

    private void SetStaffRole(ProviderRole role)
    {
        if (_editing is null) return;

        _editing.Role = role;

        RaisePropertyChanged(nameof(StaffRole));
        RaisePropertyChanged(nameof(EditorIsClinical));
        RaisePropertyChanged(nameof(EditorIsBookable));
    }

    private void SetStaffSite(Guid locationId)
    {
        if (_editing is null) return;

        _editing.PrimaryLocationId = locationId;

        RaisePropertyChanged(nameof(StaffSiteId));
    }

    private Task SaveStaffAsync() => RunGuardedAsync(async () =>
    {
        if (_editing is null) return;

        var refusal = await _admin.SaveStaffAsync(_editing).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = _editingIsNew
            ? $"{_editing.FullName} added. They cannot sign in — there is no sign-in yet."
            : $"{_editing.FullName} updated.";

        _editingIsNew = false;

        await LoadTabAsync().ConfigureAwait(false);
    });

    private Task SetActiveAsync(bool isActive) => RunGuardedAsync(async () =>
    {
        if (_editing is null) return;

        var refusal = await _admin
            .SetStaffActiveAsync(_editing.Id, isActive)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = isActive
            ? $"{_editing.FullName} reactivated."
            : $"{_editing.FullName} deactivated. Their notes, scripts and invoices stay "
                + "attributed to them.";

        // Reloaded rather than patched in memory, so the row and the editor agree about
        // the state the database is actually in.
        _editing = await _admin.GetStaffMemberAsync(_editing.Id).ConfigureAwait(false);

        await LoadTabAsync().ConfigureAwait(false);
    });

    // ---- roles -----------------------------------------------------------

    private void SelectRole(ProviderRole role)
    {
        _selectedRole = role;

        RaisePropertyChanged(nameof(SelectedRole));
        RaisePropertyChanged(nameof(SelectedRoleIsClinical));
        RaisePropertyChanged(nameof(SelectedRoleIsBookable));
        RaisePropertyChanged(nameof(SelectedRoleStaffCount));
    }

    // ---- sites -----------------------------------------------------------

    private Task SelectSiteAsync(Guid locationId) => RunGuardedAsync(async () =>
    {
        _selectedSiteId = locationId;
        _editingSite = null;
        _editingSiteIsNew = false;
        _editingChair = null;
        _lastAction = null;

        await LoadTabAsync().ConfigureAwait(false);
    });

    private void StartNewSite()
    {
        _editingSite = new PracticeLocation
        {
            // The practice's own zone, not the machine's. A second surgery is nearly
            // always in the same one, and it is the field somebody would forget to set.
            TimeZoneId = _sites.FirstOrDefault()?.TimeZoneId ?? "Australia/Sydney",
            IsActive = true,
        };

        _editingSiteIsNew = true;
        _editingChair = null;
        _lastAction = null;

        RaiseAll();
    }

    /// <summary>Opens the selected site for editing.</summary>
    private Task EditSelectedSiteAsync() => RunGuardedAsync(async () =>
    {
        if (_selectedSiteId is not { } locationId) return;

        _editingSite = await _admin.GetSiteAsync(locationId).ConfigureAwait(false);
        _editingSiteIsNew = false;
        _lastAction = null;

        RaiseAll();
    });

    private void ClearSiteEditor()
    {
        _editingSite = null;
        _editingSiteIsNew = false;

        RaiseAll();
    }

    private Task SaveSiteAsync() => RunGuardedAsync(async () =>
    {
        if (_editingSite is null) return;

        var refusal = await _admin.SaveSiteAsync(_editingSite).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = _editingSiteIsNew
            ? $"{_editingSite.Name} added. It takes no bookings until it has a chair."
            : $"{_editingSite.Name} updated.";

        // Selected so the chairs list below is the new site's, which is the next thing
        // anybody adding a site needs.
        _selectedSiteId = _editingSite.Id;
        _editingSiteIsNew = false;
        _editingSite = null;

        // The app bar and every site picker read the session's list, which does not
        // re-read on its own. Without this the site just added is invisible everywhere
        // except this pane.
        await _session.ReloadLocationsAsync().ConfigureAwait(false);

        await LoadTabAsync().ConfigureAwait(false);
    });

    private Task SetSiteActiveAsync(bool isActive) => RunGuardedAsync(async () =>
    {
        if (_selectedSiteId is not { } locationId) return;

        var refusal = await _admin
            .SetSiteActiveAsync(locationId, isActive)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = isActive
            ? "Site reopened."
            : "Site closed. Its appointments, invoices and charting stay attached to it.";

        _editingSite = null;

        await _session.ReloadLocationsAsync().ConfigureAwait(false);

        await LoadTabAsync().ConfigureAwait(false);
    });

    // ---- chairs ----------------------------------------------------------

    private void StartNewChair()
    {
        if (_selectedSiteId is not { } locationId) return;

        _editingChair = new Operatory
        {
            PracticeLocationId = locationId,
            IsActive = true,
        };

        _editingChairIsNew = true;
        _lastAction = null;

        RaiseAll();
    }

    private Task SelectChairAsync(Guid operatoryId) => RunGuardedAsync(async () =>
    {
        _editingChair = await _admin.GetChairAsync(operatoryId).ConfigureAwait(false);
        _editingChairIsNew = false;
        _lastAction = null;

        RaiseAll();
    });

    private void ClearChairEditor()
    {
        _editingChair = null;
        _editingChairIsNew = false;

        RaiseAll();
    }

    private void ToggleChairSurgical()
    {
        if (_editingChair is null) return;

        _editingChair.IsSurgical = !_editingChair.IsSurgical;

        RaisePropertyChanged(nameof(ChairIsSurgical));
    }

    private Task SaveChairAsync() => RunGuardedAsync(async () =>
    {
        if (_editingChair is null) return;

        var refusal = await _admin.SaveChairAsync(_editingChair).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = _editingChairIsNew
            ? $"{_editingChair.Name} added. It is a new column in the diary."
            : $"{_editingChair.Name} updated.";

        _editingChair = null;
        _editingChairIsNew = false;

        await LoadTabAsync().ConfigureAwait(false);
    });

    /// <summary>
    /// Flips one chair in or out of service, reading its current state from the row.
    /// </summary>
    /// <remarks>
    /// One command rather than two, because the button is a single toggle per row and the
    /// stored state is what decides which way it goes — a pair of commands would let the
    /// screen ask for a change the row does not need.
    /// </remarks>
    private Task ToggleChairActiveAsync(Guid operatoryId) => RunGuardedAsync(async () =>
    {
        if (_chairs.FirstOrDefault(row => row.OperatoryId == operatoryId) is not { } chair)
        {
            return;
        }

        var refusal = await _admin
            .SetChairActiveAsync(operatoryId, !chair.IsActive)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = chair.IsActive
            ? $"{chair.Name} is out of service and takes no bookings."
            : $"{chair.Name} is back in service.";

        await LoadTabAsync().ConfigureAwait(false);
    });

    private Task MoveChairAsync(Guid operatoryId, int delta) => RunGuardedAsync(async () =>
    {
        var refusal = await _admin.MoveChairAsync(operatoryId, delta).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = "Diary column order changed.";

        await LoadTabAsync().ConfigureAwait(false);
    });

    // ---- registrations ---------------------------------------------------

    private void StartRegistration(RegistrationRow? row)
    {
        _editingRegistrationId = row?.ProviderId;
        _draftAhpra = row?.AhpraNumber;
        _draftExpiry = row?.ExpiresOn;
        _lastAction = null;

        RaiseAll();
    }

    private Task SaveRegistrationAsync() => RunGuardedAsync(async () =>
    {
        if (_editingRegistrationId is not { } providerId) return;

        var refusal = await _admin
            .SaveRegistrationAsync(providerId, _draftAhpra, _draftExpiry)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = "Registration recorded.";
        _editingRegistrationId = null;

        await LoadTabAsync().ConfigureAwait(false);
    });

    // ---- audit and data --------------------------------------------------

    /// <summary>
    /// Takes the filter as a string, because the parameter can be "nothing".
    /// </summary>
    /// <remarks>
    /// <c>MvxAsyncCommand&lt;AuditAction?&gt;</c> would be the natural type, but a Razor
    /// lambda passing a null enum through the generic command loses which overload it
    /// meant. The empty string is unambiguous.
    /// </remarks>
    private Task SetAuditFilterAsync(string? action) => RunGuardedAsync(async () =>
    {
        _auditFilter = Enum.TryParse<AuditAction>(action, out var parsed) ? parsed : null;

        // Back to the first page. Staying on page four of the old filter lands on page four
        // of the new one, which for a narrower filter is usually past the end — an empty
        // screen that reads as "no matching entries" when there are plenty.
        _auditPage = 0;

        await LoadTabAsync().ConfigureAwait(false);
    });

    private Task GoToAuditPageAsync(int page) => RunGuardedAsync(async () =>
    {
        // Clamped rather than trusted. The commands are bound to buttons that are disabled
        // at the ends, but a disabled button is a rendering decision and this is the rule.
        if (page < 0 || page >= _audit.PageCount) return;

        _auditPage = page;

        await LoadTabAsync().ConfigureAwait(false);
    });

    private Task BackupAsync() => RunGuardedAsync(async () =>
    {
        _backup = await _admin.BackupAsync().ConfigureAwait(false);

        if (_backup is { Refusal: { Length: > 0 } refusal })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
        }
        else
        {
            _lastAction = "Snapshot written. It is on this machine only.";
        }

        await LoadTabAsync().ConfigureAwait(false);
    });

    // ---- notification settings -------------------------------------------

    private void ToggleEmailEnabled()
    {
        if (_notifications is null) return;

        _notifications.EmailEnabled = !_notifications.EmailEnabled;

        RaisePropertyChanged(nameof(EmailEnabled));
    }

    private Task SaveNotificationsAsync() => RunGuardedAsync(async () =>
    {
        if (_notifications is null) return;

        var refusal = await _notificationSettings
            .SaveAsync(_notifications, _draftAppPassword)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        // Cleared on the way out, whatever happened. A secret sitting in a bound property
        // outlives the render that needed it for no reason.
        _draftAppPassword = null;
        _testResult = null;

        _lastAction = _notifications.EmailEnabled
            ? "Saved. Send a test to check the account works."
            : "Saved. Email notifications are off, so nothing will be sent.";

        await LoadTabAsync().ConfigureAwait(false);
    });

    private Task ClearAppPasswordAsync() => RunGuardedAsync(async () =>
    {
        var refusal = await _notificationSettings.ClearPasswordAsync().ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _draftAppPassword = null;
        _testResult = null;
        _lastAction = "App password removed, and notifications turned off with it.";

        await LoadTabAsync().ConfigureAwait(false);
    });

    private Task SendTestEmailAsync() => RunGuardedAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(_testRecipient)) return;

        _testResult = await _notificationSettings
            .SendTestAsync(_testRecipient)
            .ConfigureAwait(false);

        _lastAction = _testResult.Succeeded
            ? "Test email sent."
            : "The test email did not send — see the reason below.";

        await LoadTabAsync().ConfigureAwait(false);
    });

    // ---- printer settings -------------------------------------------------

    private void SetPrinterConnection(PrinterConnection connection)
    {
        if (_printer is null) return;

        _printer.Connection = connection;

        RaisePropertyChanged(nameof(PrinterConnectionKind));
        RaisePropertyChanged(nameof(DeviceAddressLabel));
        RaisePropertyChanged(nameof(ShowsNetworkPort));
        RaisePropertyChanged(nameof(ConnectionNeedsDeviceHead));
    }

    private void SetReceiptPaper(ReceiptPaper paper)
    {
        if (_printer is null) return;

        _printer.Paper = paper;

        RaisePropertyChanged(nameof(ReceiptPaperSize));
        RaisePropertyChanged(nameof(ReceiptColumns));

        // The rendered page was laid out for the old width, so it is now wrong about the
        // one thing it exists to show.
        _testPrint = null;
        RaisePropertyChanged(nameof(TestPrintResult));
    }

    private void SetDocumentPaper(DocumentPaper paper)
    {
        if (_printer is null) return;

        _printer.DocumentPaper = paper;

        RaisePropertyChanged(nameof(DocumentPaperSize));
    }

    private Task SavePrinterAsync() => RunGuardedAsync(async () =>
    {
        if (_printer is null) return;

        var refusal = await _printerSettings.SaveAsync(_printer).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = "Printer settings saved. Nothing prints yet — see the note.";

        // Dropped so the reload below reads the row back from the database rather than
        // keeping the draft. What was saved and what is on screen should be the same
        // object's worth of values, proven by re-reading rather than assumed.
        _printer = null;

        await LoadTabAsync().ConfigureAwait(false);
    });

    private Task TestPrintAsync() => RunGuardedAsync(async () =>
    {
        if (_printer is null) return;

        // The draft is passed in, so the page reflects the width on screen even when it
        // has not been saved — that is the change somebody makes and then tests.
        _testPrint = await _printerSettings.TestPrintAsync(_printer).ConfigureAwait(false);

        _lastAction = "Test receipt rendered. It was not sent to a printer.";

        // No reload: LoadTabAsync re-reads the row, which would discard the unsaved draft
        // the page was just rendered from.
        RaiseAll();
    });

    private void OnSessionChanged(object? sender, EventArgs e) => _ = LoadAsync();

    public void Dispose() => _session.Changed -= OnSessionChanged;

    /// <summary>The words a practice uses for a role, not the enum's.</summary>
    public static string RoleLabel(ProviderRole role) => role switch
    {
        ProviderRole.Dentist => "Dentist",
        ProviderRole.Hygienist => "Hygienist",
        ProviderRole.OralHealthTherapist => "Oral health therapist",
        ProviderRole.DentalTherapist => "Dental therapist",
        ProviderRole.Prosthetist => "Prosthetist",
        ProviderRole.Specialist => "Specialist",
        ProviderRole.Assistant => "Dental assistant",
        ProviderRole.Administration => "Reception / admin",

        // Never rendered by a clinic's own screens: the role is not in Roles, and the
        // tenant filter puts the accounts holding it outside a practice's rows. Labelled
        // anyway, so a stray render says what it is rather than "SuperAdmin".
        ProviderRole.SuperAdmin => "Molargo staff (vendor)",
        _ => role.ToString(),
    };

    /// <summary>
    /// Everything the screen reads. Listed explicitly rather than raising a blanket change,
    /// which would re-render the field being typed in.
    /// </summary>
    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(Tab), nameof(LastAction), nameof(ActingAs), nameof(Staff),
            nameof(ActiveStaffCount), nameof(Editing), nameof(HasEditor),
            nameof(EditingIsNew), nameof(EditorTitle), nameof(StaffFirstName),
            nameof(StaffLastName), nameof(StaffDisplayName), nameof(StaffEmail),
            nameof(StaffProviderNumber), nameof(StaffRole), nameof(StaffSiteId),
            nameof(StaffIsActive), nameof(EditorIsClinical), nameof(EditorIsBookable),
            nameof(CanGrantAccess), nameof(EditorIsOwner), nameof(EditorIsLastOwner),
            nameof(OwnerToggleIsLocked), nameof(EditorPermissionCount),
            nameof(IsSettingPassword), nameof(StaffPassword), nameof(ShowStaffPassword),
            nameof(CanGrantStaffPassword), nameof(EditorLastPasswordChange),
            nameof(CanSetPassword), nameof(PasswordHint),
            nameof(SelectedRole), nameof(SelectedRoleIsClinical),
            nameof(SelectedRoleIsBookable), nameof(SelectedRoleStaffCount),
            nameof(Sites), nameof(SelectedSiteId), nameof(SelectedSite),
            nameof(EditingSite), nameof(HasSiteEditor), nameof(EditingSiteIsNew),
            nameof(SiteEditorTitle), nameof(EditingSiteIsOpen), nameof(SiteName),
            nameof(SiteShortName), nameof(SiteAddressLine), nameof(SiteSuburb),
            nameof(SiteState), nameof(SitePostcode), nameof(SitePhone),
            nameof(SiteEmail), nameof(SiteAbn), nameof(SiteTimeZoneId),
            nameof(Chairs), nameof(ChairsInService), nameof(EditingChair),
            nameof(HasChairEditor), nameof(EditingChairIsNew), nameof(ChairName),
            nameof(ChairIsSurgical),
            nameof(Registrations), nameof(BlockedCount),
            nameof(LapsingCount), nameof(UncheckedCount), nameof(DraftAhpra),
            nameof(DraftExpiry), nameof(Audit), nameof(AuditFilter), nameof(AuditPage),
            nameof(AuditPageCount), nameof(HasNextAuditPage),
            nameof(HasPreviousAuditPage), nameof(AuditRangeLabel), nameof(Database),
            nameof(Backup), nameof(Notifications), nameof(EmailEnabled),
            nameof(HasAppPassword), nameof(IsEmailConfigured), nameof(SenderAddress),
            nameof(SenderName), nameof(ReplyToAddress), nameof(AlertsToAddress),
            nameof(DailySummaryRecipients), nameof(DailySummaryAt), nameof(NotifyReceipts),
            nameof(NotifyLowStock), nameof(NotifyDailySummary), nameof(ShowAppPassword),
            nameof(SmtpHost), nameof(SmtpPort), nameof(DraftAppPassword),
            nameof(TestRecipient), nameof(TestResult), nameof(LastTestSummary),
            nameof(LastTestSucceeded), nameof(LastTestDetail), nameof(CanSendTest),
            nameof(Printer), nameof(PrinterConnectionKind), nameof(DeviceAddressLabel),
            nameof(ShowsNetworkPort), nameof(ConnectionNeedsDeviceHead),
            nameof(DeviceAddress), nameof(NetworkPort), nameof(ReceiptPrinterName),
            nameof(ReceiptPaperSize), nameof(ReceiptColumns),
            nameof(PrintReceiptAutomatically), nameof(OpenCashDrawer), nameof(PrintLogo),
            nameof(ReceiptFooter), nameof(DocumentPrinterName), nameof(DocumentPaperSize),
            nameof(PrescriptionCopies), nameof(PrintPrescriberDetails),
            nameof(TestPrintResult),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
