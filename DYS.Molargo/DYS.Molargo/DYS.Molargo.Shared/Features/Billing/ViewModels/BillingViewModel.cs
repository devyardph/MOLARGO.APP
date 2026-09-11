using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Components;
using DYS.Molargo.Domain.Dtos;
using DYS.Molargo.Shared.Features.Billing.Services;
using DYS.Molargo.Shared.Features.Patient.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Billing.ViewModels;

/// <summary>Which pane of the billing screen is showing.</summary>
public enum BillingTab
{
    Invoices = 0,
    Catalogue = 1,
    Claims = 2,
    Debtors = 3,
    Plans = 4,
    EndOfDay = 5,
}

/// <summary>One edit to a draft invoice line.</summary>
public sealed record LineEdit(Guid LineId, int Quantity, decimal UnitFee, decimal Discount);

/// <summary>Which adjustment the invoice pane is asking about.</summary>
public enum AdjustmentKind
{
    None = 0,
    Credit = 1,
    WriteOff = 2,
    Refund = 3,
}

/// <summary>
/// The practice's money: invoices and payments, claims, debtors, the fee list and the
/// day's banking.
/// </summary>
public sealed class BillingViewModel : BaseViewModel, IDisposable
{
    private readonly IBillingService _billing;
    private readonly IPatientService _patients;
    private readonly ISessionService _session;
    private readonly IAppNavigator _navigator;
    private readonly IClock _clock;

    private BillingTab _tab = BillingTab.Invoices;
    private DateOnly _day;

    private IReadOnlyList<InvoiceRow> _invoices = [];
    private InvoiceDetail? _selected;

    // Two lists, priced differently on purpose. The catalogue tab shows the practice-wide
    // schedule with each site's exceptions named; the draft's picker shows what THIS
    // invoice's site charges, because that is the number about to land on the line. One
    // shared list would have made the picker show practice fees at a site that does not
    // charge them.
    private IReadOnlyList<CatalogueItem> _catalogue = [];
    private IReadOnlyList<CatalogueItem> _lineChoices = [];
    private Guid _lineChoicesFor;
    private string _catalogueSearch = string.Empty;
    private bool _showWithdrawn;

    private ProcedureCode? _editingItem;
    private CatalogueEntry? _editingEntry;
    private bool _editingIsNew;
    private string? _editingFee;
    private string? _editingDuration;

    // Keyed by location, holding the raw text of each box. Raw rather than parsed, so a
    // half-typed "12." is not silently rounded or rejected under the user's fingers; the
    // parse happens once, on save.
    private readonly Dictionary<Guid, string?> _siteFeeDrafts = [];

    private IReadOnlyList<ClaimRow> _claims = [];
    private IReadOnlyList<DebtorRow> _debtors = [];
    private BankingSheet? _banking;

    private AdjustmentKind _adjustment = AdjustmentKind.None;
    private string? _adjustmentAmount;
    private string? _adjustmentReason;
    private string? _lastAction;

    private bool _isRaising;
    private bool _isChangingPatient;
    private string _patientSearch = string.Empty;
    private IReadOnlyList<PatientListItemDto> _patientResults = [];
    private IReadOnlyList<BillableVisit> _visits = [];
    private Guid? _attachedVisit;
    private string? _lineTooth;
    private int _termDays;

    private Guid? _assessingClaimId;
    private string? _assessAmount;
    private string? _assessMessage;

    public BillingViewModel(
        IBillingService billing,
        IPatientService patients,
        ISessionService session,
        IAppNavigator navigator,
        IClock clock)
    {
        _billing = billing;
        _patients = patients;
        _session = session;
        _navigator = navigator;
        _clock = clock;

        _day = clock.Today;

        // Built once, in the constructor — never rebuilt per render.
        SelectTabCommand = new MvxAsyncCommand<BillingTab>(SelectTabAsync);
        StepDayCommand = new MvxAsyncCommand<int>(StepDayAsync);
        TodayCommand = new MvxAsyncCommand(() => GoToAsync(_clock.Today));
        SelectInvoiceCommand = new MvxAsyncCommand<Guid>(SelectInvoiceAsync);
        TakePaymentCommand = new MvxAsyncCommand<PaymentMethod>(TakePaymentAsync);
        StartAdjustmentCommand = new MvxCommand<AdjustmentKind>(StartAdjustment);
        CancelAdjustmentCommand = new MvxCommand(() => StartAdjustment(AdjustmentKind.None));
        ApplyAdjustmentCommand = new MvxAsyncCommand(ApplyAdjustmentAsync);
        OpenPatientCommand = new MvxCommand<Guid>(id => _navigator.ToPatientRecord(id));
        StartRaisingCommand = new MvxAsyncCommand(StartRaisingAsync);
        ChangeDraftPatientCommand = new MvxCommand(StartChangingPatient);
        CancelRaisingCommand = new MvxCommand(CancelRaising);
        SearchPatientsCommand = new MvxAsyncCommand(SearchPatientsAsync);
        ChooseBillPatientCommand = new MvxAsyncCommand<Guid>(ChooseBillPatientAsync);
        AttachVisitCommand = new MvxAsyncCommand<Guid?>(AttachVisitAsync);
        AddLineCommand = new MvxAsyncCommand<Guid>(AddLineAsync);
        RemoveLineCommand = new MvxAsyncCommand<Guid>(RemoveLineAsync);
        UpdateLineCommand = new MvxAsyncCommand<LineEdit>(edit => UpdateLineAsync(edit!));
        IssueInvoiceCommand = new MvxAsyncCommand(IssueInvoiceAsync);
        DiscardDraftCommand = new MvxAsyncCommand(DiscardDraftAsync);

        NewCatalogueItemCommand = new MvxCommand(NewCatalogueItem);
        SelectCatalogueItemCommand = new MvxAsyncCommand<Guid>(SelectCatalogueItemAsync);
        CancelCatalogueEditCommand = new MvxCommand(CancelCatalogueEdit);
        SaveCatalogueItemCommand = new MvxAsyncCommand(SaveCatalogueItemAsync);
        ToggleCatalogueItemCommand = new MvxAsyncCommand<Guid>(ToggleCatalogueItemAsync);
        ToggleShowWithdrawnCommand = new MvxCommand(ToggleShowWithdrawn);

        StartAssessCommand = new MvxCommand<ClaimRow>(row => StartAssess(row!));
        CancelAssessCommand = new MvxCommand(() => StartAssess(null));
        ApplyAssessCommand = new MvxAsyncCommand(ApplyAssessAsync);
        ReopenClaimCommand = new MvxAsyncCommand<Guid>(ReopenClaimAsync);

        _session.Changed += OnSessionChanged;
    }

    public IMvxAsyncCommand<BillingTab> SelectTabCommand { get; }

    public IMvxAsyncCommand<int> StepDayCommand { get; }

    public IMvxAsyncCommand TodayCommand { get; }

    public IMvxAsyncCommand<Guid> SelectInvoiceCommand { get; }

    public IMvxAsyncCommand<PaymentMethod> TakePaymentCommand { get; }

    public IMvxCommand<AdjustmentKind> StartAdjustmentCommand { get; }

    public IMvxCommand CancelAdjustmentCommand { get; }

    public IMvxAsyncCommand ApplyAdjustmentCommand { get; }

    public IMvxCommand<Guid> OpenPatientCommand { get; }

    public IMvxAsyncCommand StartRaisingCommand { get; }

    /// <summary>Reopens the picker to move an existing draft to another patient.</summary>
    public IMvxCommand ChangeDraftPatientCommand { get; }

    public IMvxCommand CancelRaisingCommand { get; }

    public IMvxAsyncCommand SearchPatientsCommand { get; }

    public IMvxAsyncCommand<Guid> ChooseBillPatientCommand { get; }

    /// <summary>Attaches or detaches the visit the invoice bills. Null detaches.</summary>
    public IMvxAsyncCommand<Guid?> AttachVisitCommand { get; }

    public IMvxAsyncCommand<Guid> AddLineCommand { get; }

    public IMvxAsyncCommand<Guid> RemoveLineCommand { get; }

    public IMvxAsyncCommand<LineEdit> UpdateLineCommand { get; }

    public IMvxAsyncCommand IssueInvoiceCommand { get; }

    public IMvxAsyncCommand DiscardDraftCommand { get; }

    /// <summary>Opens an empty editor for an item the practice is adding itself.</summary>
    public IMvxCommand NewCatalogueItemCommand { get; }

    public IMvxAsyncCommand<Guid> SelectCatalogueItemCommand { get; }

    public IMvxCommand CancelCatalogueEditCommand { get; }

    /// <summary>Saves the item and every site fee box in one action.</summary>
    public IMvxAsyncCommand SaveCatalogueItemCommand { get; }

    /// <summary>Withdraws the item, or restores a withdrawn one.</summary>
    public IMvxAsyncCommand<Guid> ToggleCatalogueItemCommand { get; }

    public IMvxCommand ToggleShowWithdrawnCommand { get; }

    public IMvxCommand<ClaimRow> StartAssessCommand { get; }

    public IMvxCommand CancelAssessCommand { get; }

    public IMvxAsyncCommand ApplyAssessCommand { get; }

    public IMvxAsyncCommand<Guid> ReopenClaimCommand { get; }

    public override Task Initialize() => LoadAsync();

    public BillingTab Tab => _tab;

    public DateOnly Day => _day;

    public string DayLabel => MolargoFormat.DayLabel(_day);

    public bool IsToday => _day == _clock.Today;

    /// <summary>The methods the invoice pane offers. Refunds are not taken here.</summary>
    public static readonly PaymentMethod[] Methods =
    [
        PaymentMethod.EftposCard,
        PaymentMethod.CreditCard,
        PaymentMethod.Cash,
        PaymentMethod.BankTransfer,
    ];

    // ---- invoices --------------------------------------------------------

    public IReadOnlyList<InvoiceRow> Invoices => _invoices;

    public InvoiceDetail? Selected => _selected;

    public bool HasSelection => _selected is not null;

    public bool IsSelected(Guid invoiceId) => _selected?.Invoice.Id == invoiceId;

    /// <summary>"$4,280 outstanding across 11 accounts" — the caption under the list.</summary>
    public string InvoiceCaption
    {
        get
        {
            var owed = _invoices.Sum(row => row.Outstanding);

            var unpaid = _invoices.Count(row => row.Outstanding > 0m);

            var billed = _invoices.Sum(row => row.Total);

            return $"{_invoices.Count} invoiced {MolargoFormat.Money(billed)} · "
                + $"{MolargoFormat.Money(owed)} outstanding on {unpaid} of them";
        }
    }

    /// <summary>True while a patient is being chosen, for a new draft or an existing one.</summary>
    public bool IsRaising => _isRaising;

    /// <summary>
    /// True when the picker is moving an existing draft rather than starting one, so the
    /// heading can say which is happening.
    /// </summary>
    public bool IsChangingPatient => _isChangingPatient;

    public string PickerTitle => _isChangingPatient ? "Move this draft to" : "Which patient?";

    public string PickerPrompt => _isChangingPatient
        ? "The lines stay; any attached visit is cleared, because it belonged to the "
            + "patient you are moving away from."
        : "An invoice is a demand for money from a named person, so the patient comes first.";

    public string PatientSearch
    {
        get => _patientSearch;
        set => SetProperty(ref _patientSearch, value);
    }

    public IReadOnlyList<PatientListItemDto> PatientResults => _patientResults;

    /// <summary>
    /// Whether the selected invoice is a draft, and so freely editable.
    /// </summary>
    /// <remarks>
    /// The whole edit surface hangs off this. An issued invoice is a statement already
    /// given to a patient and possibly claimed against a fund, so it is corrected by
    /// credit note rather than rewritten.
    /// </remarks>
    public bool IsDraft => _selected?.Status == InvoiceStatus.Draft;

    /// <summary>Visits the draft could be attached to, so the work is traceable.</summary>
    public IReadOnlyList<BillableVisit> BillableVisits => _visits;

    public Guid? AttachedVisit => _attachedVisit;

    /// <summary>Tooth number for the next per-tooth item added.</summary>
    public string? LineTooth
    {
        get => _lineTooth;
        set => SetProperty(ref _lineTooth, value);
    }

    /// <summary>Payment terms offered at issue. Zero is due today, which is the norm.</summary>
    public static readonly int[] TermOptions = [0, 7, 14, 30];

    public int TermDays
    {
        get => _termDays;
        set => SetProperty(ref _termDays, value);
    }

    public AdjustmentKind Adjustment => _adjustment;

    public bool IsAdjusting => _adjustment != AdjustmentKind.None;

    public string AdjustmentTitle => _adjustment switch
    {
        AdjustmentKind.Credit => "Credit note",
        AdjustmentKind.WriteOff => "Write off the balance",
        AdjustmentKind.Refund => "Refund",
        _ => string.Empty,
    };

    /// <summary>
    /// What each adjustment actually does, said before it is done.
    /// </summary>
    /// <remarks>
    /// The three are routinely confused and the design offers them as three identical
    /// buttons. Spelling out the consequence is cheaper than unpicking the wrong one.
    /// </remarks>
    public string AdjustmentExplanation => _adjustment switch
    {
        AdjustmentKind.Credit =>
            "The practice should not have charged this. The invoice total comes down and "
                + "the patient never owed it.",
        AdjustmentKind.WriteOff =>
            "The practice gives up collecting the balance. The invoice keeps its total, so "
                + "the work still counts as production — the debt does not.",
        AdjustmentKind.Refund =>
            "Money already taken goes back to the patient. Recorded against this invoice "
                + "as a negative payment.",
        _ => string.Empty,
    };

    /// <summary>Whether the adjustment needs an amount, or applies to the whole balance.</summary>
    public bool AdjustmentNeedsAmount => _adjustment is AdjustmentKind.Credit
        or AdjustmentKind.Refund;

    public string? AdjustmentAmount
    {
        get => _adjustmentAmount;
        set => SetProperty(ref _adjustmentAmount, value);
    }

    public string? AdjustmentReason
    {
        get => _adjustmentReason;
        set => SetProperty(ref _adjustmentReason, value);
    }

    /// <summary>What the last money movement did, so the screen confirms it.</summary>
    public string? LastAction => _lastAction;

    // ---- catalogue -------------------------------------------------------

    /// <summary>
    /// The search term, shared by the catalogue tab's table and the draft's item picker.
    /// </summary>
    /// <remarks>
    /// One term for both, because they are never on screen together — the picker only
    /// exists inside an open draft, which lives on the invoices tab.
    /// </remarks>
    public string CatalogueSearch
    {
        get => _catalogueSearch;
        set
        {
            if (!SetProperty(ref _catalogueSearch, value)) return;

            RaisePropertyChanged(nameof(Catalogue));
            RaisePropertyChanged(nameof(LineChoiceResults));
        }
    }

    /// <summary>Whether withdrawn items are listed. Off by default.</summary>
    public bool ShowWithdrawn => _showWithdrawn;

    public IReadOnlyList<CatalogueItem> Catalogue
    {
        get
        {
            var rows = _showWithdrawn
                ? _catalogue
                : _catalogue.Where(item => item.Code.IsActive).ToList();

            if (string.IsNullOrWhiteSpace(_catalogueSearch)) return rows;

            var term = _catalogueSearch.Trim();

            return rows
                .Where(item =>
                    item.ItemNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || item.Description.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (item.Code.PatientFriendlyName is { } friendly
                        && friendly.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
    }

    /// <summary>
    /// The items a line can be added from, priced for the open draft's own site.
    /// </summary>
    /// <remarks>
    /// Active items only. A withdrawn item is one the practice has decided to stop
    /// charging, so offering it here would be offering the one thing the withdrawal was
    /// meant to prevent.
    /// </remarks>
    public IReadOnlyList<CatalogueItem> LineChoices => _lineChoices;

    /// <summary>
    /// The picker's matches. Empty until something is typed — a list of every ADA item is
    /// not a picker, and the design searches rather than scrolls for exactly that reason.
    /// </summary>
    public IReadOnlyList<CatalogueItem> LineChoiceResults
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_catalogueSearch)) return [];

            var term = _catalogueSearch.Trim();

            return _lineChoices
                .Where(item =>
                    item.Code.IsActive
                    && (item.ItemNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                        || item.Description.Contains(term, StringComparison.OrdinalIgnoreCase)
                        || (item.Code.PatientFriendlyName is { } friendly
                            && friendly.Contains(term, StringComparison.OrdinalIgnoreCase))))
                .ToList();
        }
    }

    /// <summary>How many items the practice can currently charge.</summary>
    public int ActiveItemCount => _catalogue.Count(item => item.Code.IsActive);

    public int WithdrawnItemCount => _catalogue.Count(item => !item.Code.IsActive);

    /// <summary>
    /// The sites this practice runs, for the per-site fee boxes.
    /// </summary>
    /// <remarks>
    /// Every site, not the ones this user is assigned to. A fee schedule is a practice-wide
    /// document — somebody setting the price for a second site is the normal case, and a
    /// list filtered to the user's own site would make the other site's price unreachable
    /// from the only screen that sets it.
    /// </remarks>
    public IReadOnlyList<CatalogueSiteFee> EditorSites => _editingEntry?.Sites ?? [];

    /// <summary>
    /// Whether the signed-in person may change fees.
    /// </summary>
    /// <remarks>
    /// Reading the catalogue is never gated — the front desk quotes from it all day,
    /// and a price list they cannot see is a practice that cannot take a booking. This
    /// hides the editor and the New item button, and the service refuses the writes
    /// regardless.
    /// </remarks>
    public bool CanManagePricing => _session.Can(PracticePermissions.ManagePricing);

    /// <summary>True where the practice runs more than one site, so per-site pricing applies.</summary>
    public bool HasMultipleSites => _session.AllLocations.Count > 1;

    public bool IsEditingItem => _editingItem is not null;

    public bool EditingItemIsNew => _editingIsNew;

    public Guid? EditingItemId => _editingItem?.Id;

    public string EditorTitle => _editingItem is null
        ? "No item selected"
        : _editingIsNew
            ? "New catalogue item"
            : $"Item {_editingItem.ItemNumber}";

    public bool EditingItemIsActive => _editingItem?.IsActive ?? true;

    public bool IsOpenFor(Guid codeId) => _editingItem?.Id == codeId;

    public string? EditItemNumber
    {
        get => _editingItem?.ItemNumber;
        set
        {
            if (_editingItem is null) return;

            _editingItem.ItemNumber = value ?? string.Empty;
            RaisePropertyChanged(nameof(EditItemNumber));
        }
    }

    public string? EditDescription
    {
        get => _editingItem?.Description;
        set
        {
            if (_editingItem is null) return;

            _editingItem.Description = value ?? string.Empty;
            RaisePropertyChanged(nameof(EditDescription));
        }
    }

    public string? EditFriendlyName
    {
        get => _editingItem?.PatientFriendlyName;
        set
        {
            if (_editingItem is null) return;

            _editingItem.PatientFriendlyName = value;
            RaisePropertyChanged(nameof(EditFriendlyName));
        }
    }

    public string? EditCategory
    {
        get => _editingItem?.Category;
        set
        {
            if (_editingItem is null) return;

            _editingItem.Category = value;
            RaisePropertyChanged(nameof(EditCategory));
        }
    }

    /// <summary>
    /// The practice fee as typed.
    /// </summary>
    /// <remarks>
    /// A string, not a decimal. Bound to a decimal, clearing the box to retype a figure
    /// gives Blazor an empty string to parse, and the field snaps back to the old value
    /// mid-edit — so the number is held as text and parsed once, on save.
    /// </remarks>
    public string? EditFee
    {
        get => _editingFee;
        set
        {
            if (SetProperty(ref _editingFee, value)) RaisePropertyChanged(nameof(EditFeeIsValid));
        }
    }

    public bool EditFeeIsValid => ParseMoney(_editingFee) is not null;

    public string? EditDuration
    {
        get => _editingDuration;
        set => SetProperty(ref _editingDuration, value);
    }

    public bool EditIsPerTooth
    {
        get => _editingItem?.IsPerTooth ?? false;
        set
        {
            if (_editingItem is null) return;

            _editingItem.IsPerTooth = value;
            RaisePropertyChanged(nameof(EditIsPerTooth));
        }
    }

    public bool EditRequiresSurface
    {
        get => _editingItem?.RequiresSurface ?? false;
        set
        {
            if (_editingItem is null) return;

            _editingItem.RequiresSurface = value;
            RaisePropertyChanged(nameof(EditRequiresSurface));
        }
    }

    public bool EditIsCdbsEligible
    {
        get => _editingItem?.IsCdbsEligible ?? false;
        set
        {
            if (_editingItem is null) return;

            _editingItem.IsCdbsEligible = value;
            RaisePropertyChanged(nameof(EditIsCdbsEligible));
        }
    }

    /// <summary>What is typed in one site's fee box, empty meaning "the practice fee".</summary>
    public string? SiteFeeDraft(Guid locationId) =>
        _siteFeeDrafts.TryGetValue(locationId, out var text) ? text : null;

    public void SetSiteFeeDraft(Guid locationId, string? value)
    {
        _siteFeeDrafts[locationId] = value;
        RaisePropertyChanged(nameof(EditorSites));
    }

    /// <summary>
    /// True where a site's box holds something that is neither blank nor a number.
    /// </summary>
    /// <remarks>
    /// Blank is valid — it means "charge the practice fee" — so an empty box is not an
    /// error, and treating it as one would make clearing an override impossible.
    /// </remarks>
    public bool SiteFeeIsInvalid(Guid locationId) =>
        SiteFeeDraft(locationId) is { Length: > 0 } text
        && !string.IsNullOrWhiteSpace(text)
        && ParseMoney(text) is null;

    public bool CanSaveItem =>
        !IsBusy
        && _editingItem is not null
        && !string.IsNullOrWhiteSpace(_editingItem.ItemNumber)
        && !string.IsNullOrWhiteSpace(_editingItem.Description)
        && EditFeeIsValid
        && _editingEntry?.Sites.All(site => !SiteFeeIsInvalid(site.LocationId)) != false;

    // ---- claims ----------------------------------------------------------

    public IReadOnlyList<ClaimRow> Claims => _claims;

    public int OpenClaimCount => _claims.Count(row => row.IsOpen);

    public int RejectedClaimCount => _claims.Count(row => row.IsRejected);

    public Guid? AssessingClaimId => _assessingClaimId;

    public bool IsAssessing(Guid claimId) => _assessingClaimId == claimId;

    public string? AssessAmount
    {
        get => _assessAmount;
        set => SetProperty(ref _assessAmount, value);
    }

    public string? AssessMessage
    {
        get => _assessMessage;
        set => SetProperty(ref _assessMessage, value);
    }

    // ---- debtors and banking --------------------------------------------

    public IReadOnlyList<DebtorRow> Debtors => _debtors;

    public decimal DebtTotal => _debtors.Sum(row => row.Total);

    public BankingSheet? Banking => _banking;

    // ---- loading ---------------------------------------------------------

    private Task LoadAsync() => RunGuardedAsync(async () =>
    {
        await LoadTabAsync().ConfigureAwait(false);
    });

    private async Task SelectTabAsync(BillingTab tab)
    {
        _tab = tab;
        _lastAction = null;

        await RaisePropertyChanged(nameof(Tab)).ConfigureAwait(false);
        await LoadAsync().ConfigureAwait(false);
    }

    private async Task LoadTabAsync()
    {
        switch (_tab)
        {
            case BillingTab.Invoices:
                _invoices = await _billing
                    .GetDayAsync(_session.LocationId, _day)
                    .ConfigureAwait(false);

                // A selection from another day has to go, or the pane keeps offering to
                // take payment on an invoice the list no longer shows.
                if (_selected is { } current
                    && _invoices.All(row => row.InvoiceId != current.Invoice.Id))
                {
                    _selected = null;
                }
                else if (_selected is { } stale)
                {
                    _selected = await _billing
                        .GetInvoiceAsync(stale.Invoice.Id)
                        .ConfigureAwait(false);
                }

                break;

            case BillingTab.Catalogue:
                await ReloadCatalogueAsync().ConfigureAwait(false);
                break;

            case BillingTab.Claims:
                _claims = await _billing
                    .GetClaimsAsync(_session.LocationId)
                    .ConfigureAwait(false);
                break;

            case BillingTab.Debtors:
                _debtors = await _billing
                    .GetDebtorsAsync(_session.LocationId)
                    .ConfigureAwait(false);
                break;

            case BillingTab.EndOfDay:
                _banking = await _billing
                    .GetBankingSheetAsync(_session.LocationId, _day)
                    .ConfigureAwait(false);
                break;
        }

        RaiseAll();
    }

    private Task StepDayAsync(int direction) => GoToAsync(_day.AddDays(direction));

    private Task GoToAsync(DateOnly day) => RunGuardedAsync(async () =>
    {
        _day = day;
        _lastAction = null;

        await LoadTabAsync().ConfigureAwait(false);
    });

    private void StartChangingPatient()
    {
        _isRaising = true;
        _isChangingPatient = true;
        _patientSearch = string.Empty;
        _patientResults = [];

        RaiseAll();
    }

    private Task StartRaisingAsync() => RunGuardedAsync(async () =>
    {
        _isRaising = true;
        _isChangingPatient = false;
        _patientSearch = string.Empty;
        _patientResults = [];
        _selected = null;
        _lastAction = null;

        RaiseAll();

        await Task.CompletedTask.ConfigureAwait(false);
    });

    private void CancelRaising()
    {
        _isRaising = false;
        _isChangingPatient = false;
        _patientResults = [];
        _patientSearch = string.Empty;

        RaiseAll();
    }

    private Task SearchPatientsAsync() => RunGuardedAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(_patientSearch))
        {
            _patientResults = [];
        }
        else
        {
            var page = await _patients
                .SearchAsync(new PatientQuery { SearchTerm = _patientSearch.Trim(), PageSize = 8 })
                .ConfigureAwait(false);

            _patientResults = page.Items;
        }

        await RaisePropertyChanged(nameof(PatientResults)).ConfigureAwait(false);
    });

    private Task ChooseBillPatientAsync(Guid patientId) => RunGuardedAsync(async () =>
    {
        Guid invoiceId;

        if (_isChangingPatient && _selected is { } moving)
        {
            var refusal = await _billing
                .MoveDraftToPatientAsync(moving.Invoice.Id, patientId)
                .ConfigureAwait(false);

            if (refusal is { Length: > 0 })
            {
                ErrorMessage = refusal;
                await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
                return;
            }

            invoiceId = moving.Invoice.Id;
        }
        else
        {
            invoiceId = await _billing
                .CreateDraftAsync(_session.LocationId, patientId, providerId: _session.ProviderId)
                .ConfigureAwait(false);
        }

        _visits = await _billing
            .GetBillableVisitsAsync(_session.LocationId, patientId)
            .ConfigureAwait(false);

        _isRaising = false;
        _isChangingPatient = false;
        _patientResults = [];
        _patientSearch = string.Empty;
        _attachedVisit = null;

        // The draft is dated today, so the list has to be on today for it to be visible.
        // Landing on a draft the screen cannot show is worse than moving the date.
        _day = _clock.Today;

        await ReloadInvoicesAsync().ConfigureAwait(false);
        await LoadInvoiceAsync(invoiceId).ConfigureAwait(false);
    });

    private Task AttachVisitAsync(Guid? appointmentId) => RunGuardedAsync(async () =>
    {
        if (_selected is not { } detail) return;

        // Toggling: tapping the attached visit again detaches it.
        var wanted = _attachedVisit == appointmentId ? null : appointmentId;

        var refusal = await _billing
            .AttachVisitAsync(detail.Invoice.Id, wanted)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _attachedVisit = wanted;

        await ReloadInvoicesAsync().ConfigureAwait(false);
    });

    private Task AddLineAsync(Guid procedureCodeId) => RunGuardedAsync(async () =>
    {
        if (_selected is not { } detail) return;

        var refusal = await _billing
            .AddLineAsync(detail.Invoice.Id, procedureCodeId, _lineTooth)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        // Cleared after use. A tooth number left in the box silently attaches itself to
        // the next item, which is how a filling ends up charged against the wrong tooth.
        _lineTooth = null;

        await ReloadInvoicesAsync().ConfigureAwait(false);
    });

    private Task RemoveLineAsync(Guid lineId) => RunGuardedAsync(async () =>
    {
        var refusal = await _billing.RemoveLineAsync(lineId).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        await ReloadInvoicesAsync().ConfigureAwait(false);
    });

    private Task UpdateLineAsync(LineEdit edit) => RunGuardedAsync(async () =>
    {
        var refusal = await _billing
            .UpdateLineAsync(edit.LineId, edit.Quantity, edit.UnitFee, edit.Discount)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        await ReloadInvoicesAsync().ConfigureAwait(false);
    });

    private Task IssueInvoiceAsync() => RunGuardedAsync(async () =>
    {
        if (_selected is not { } detail) return;

        var refusal = await _billing
            .IssueInvoiceAsync(detail.Invoice.Id, _termDays)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        await ReloadInvoicesAsync().ConfigureAwait(false);

        _lastAction = _selected is { } issued
            ? $"Invoice {issued.Invoice.InvoiceNumber} issued for "
                + $"{MolargoFormat.MoneyExact(issued.Invoice.Total)}."
            : "Invoice issued.";

        RaiseAll();
    });

    private Task DiscardDraftAsync() => RunGuardedAsync(async () =>
    {
        if (_selected is not { } detail) return;

        var refusal = await _billing.DiscardDraftAsync(detail.Invoice.Id).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _selected = null;
        _lastAction = "Draft discarded. Nothing was billed.";

        await ReloadInvoicesAsync().ConfigureAwait(false);
    });

    private Task SelectInvoiceAsync(Guid invoiceId) =>
        RunGuardedAsync(() => LoadInvoiceAsync(invoiceId));

    /// <summary>
    /// Loads one invoice into the pane. Not guarded.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="SelectInvoiceCommand"/> because <c>RunGuardedAsync</c>
    /// returns immediately when it is already busy — so calling the guarded version from
    /// inside another guarded command did nothing at all, and raising a new invoice
    /// created the draft and then failed to open it.
    /// </remarks>
    private async Task LoadInvoiceAsync(Guid invoiceId)
    {
        _selected = await _billing.GetInvoiceAsync(invoiceId).ConfigureAwait(false);
        _adjustment = AdjustmentKind.None;
        _lastAction = null;

        // The catalogue is what a draft's lines are added from, so it is loaded whenever
        // a draft is opened rather than only when its own tab is visited — and priced for
        // the invoice's own site, which is the price about to go on the line.
        if (_selected?.Status == InvoiceStatus.Draft)
        {
            var invoiceSite = _selected.Invoice.PracticeLocationId;

            // Cached per site, not just "once". Keying the cache on emptiness alone meant
            // a draft at the second site was priced from whichever site was loaded first.
            if (_lineChoices.Count == 0 || _lineChoicesFor != invoiceSite)
            {
                _lineChoices = await _billing
                    .GetCatalogueAsync(invoiceSite)
                    .ConfigureAwait(false);

                _lineChoicesFor = invoiceSite;
            }

            _visits = await _billing
                .GetBillableVisitsAsync(_session.LocationId, _selected.Invoice.PatientId)
                .ConfigureAwait(false);

            _attachedVisit = _selected.Invoice.AppointmentId;
        }

        RaiseAll();
    }

    private Task TakePaymentAsync(PaymentMethod method) => RunGuardedAsync(async () =>
    {
        if (_selected is not { } detail) return;

        var refusal = await _billing
            .TakePaymentAsync(detail.Invoice.Id, method, receivedByProviderId: _session.ProviderId)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = $"{MolargoFormat.Money(detail.Outstanding)} taken by "
            + $"{BillingCss.MethodLabel(method)}.";

        await ReloadInvoicesAsync().ConfigureAwait(false);
    });

    private void StartAdjustment(AdjustmentKind kind)
    {
        _adjustment = kind;
        _adjustmentReason = null;
        _lastAction = null;

        // Pre-filled with the balance, which is what the amount almost always is.
        _adjustmentAmount = kind switch
        {
            AdjustmentKind.Credit => _selected?.Outstanding.ToString("0.00"),
            AdjustmentKind.Refund => _selected?.Paid.ToString("0.00"),
            _ => null,
        };

        foreach (var name in new[]
        {
            nameof(Adjustment), nameof(IsAdjusting), nameof(AdjustmentTitle),
            nameof(AdjustmentExplanation), nameof(AdjustmentNeedsAmount),
            nameof(AdjustmentAmount), nameof(AdjustmentReason), nameof(LastAction),
        })
        {
            RaisePropertyChanged(name);
        }
    }

    private Task ApplyAdjustmentAsync() => RunGuardedAsync(async () =>
    {
        if (_selected is not { } detail) return;

        var reason = _adjustmentReason ?? string.Empty;

        decimal amount = 0m;

        if (AdjustmentNeedsAmount)
        {
            if (!decimal.TryParse(_adjustmentAmount, out amount))
            {
                ErrorMessage = "Enter the amount as a number, like 120.50.";
                await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
                return;
            }
        }

        var refusal = _adjustment switch
        {
            AdjustmentKind.Credit => await _billing
                .CreditAsync(detail.Invoice.Id, amount, reason).ConfigureAwait(false),

            AdjustmentKind.WriteOff => await _billing
                .WriteOffAsync(detail.Invoice.Id, reason).ConfigureAwait(false),

            AdjustmentKind.Refund => await _billing
                .RefundAsync(detail.Invoice.Id, amount, reason, _session.ProviderId)
                .ConfigureAwait(false),

            _ => null,
        };

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = _adjustment switch
        {
            AdjustmentKind.Credit => $"{MolargoFormat.Money(amount)} credited.",
            AdjustmentKind.WriteOff => "Balance written off.",
            AdjustmentKind.Refund => $"{MolargoFormat.Money(amount)} refunded.",
            _ => null,
        };

        _adjustment = AdjustmentKind.None;
        _adjustmentAmount = null;
        _adjustmentReason = null;

        await ReloadInvoicesAsync().ConfigureAwait(false);
    });

    private async Task ReloadInvoicesAsync()
    {
        _invoices = await _billing.GetDayAsync(_session.LocationId, _day).ConfigureAwait(false);

        if (_selected is { } detail)
        {
            _selected = await _billing.GetInvoiceAsync(detail.Invoice.Id).ConfigureAwait(false);
        }

        RaiseAll();
    }

    // ---- claims ----------------------------------------------------------

    private void StartAssess(ClaimRow? row)
    {
        _assessingClaimId = row?.ClaimId;
        _assessAmount = row?.AmountClaimed.ToString("0.00");
        _assessMessage = null;

        foreach (var name in new[]
        {
            nameof(AssessingClaimId), nameof(AssessAmount), nameof(AssessMessage),
        })
        {
            RaisePropertyChanged(name);
        }
    }

    private Task ApplyAssessAsync() => RunGuardedAsync(async () =>
    {
        if (_assessingClaimId is not { } claimId) return;

        if (!decimal.TryParse(_assessAmount, out var approved))
        {
            ErrorMessage = "Enter the approved amount as a number, like 120.50.";
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        var refusal = await _billing
            .AssessClaimAsync(claimId, approved, _assessMessage)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _assessingClaimId = null;
        _claims = await _billing.GetClaimsAsync(_session.LocationId).ConfigureAwait(false);

        RaiseAll();
    });

    private Task ReopenClaimAsync(Guid claimId) => RunGuardedAsync(async () =>
    {
        var refusal = await _billing.ReopenClaimAsync(claimId).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _claims = await _billing.GetClaimsAsync(_session.LocationId).ConfigureAwait(false);

        RaiseAll();
    });

    private void OnSessionChanged(object? sender, EventArgs e) => _ = LoadAsync();

    public void Dispose() => _session.Changed -= OnSessionChanged;

    // ---- catalogue editing -----------------------------------------------

    private void NewCatalogueItem()
    {
        _editingIsNew = true;
        _editingItem = new ProcedureCode();

        // A new item has no row yet, so the site list is synthesised from the session's
        // locations. Same shape as GetCatalogueEntryAsync returns, so the editor binds to
        // one thing whether the item exists or not.
        _editingEntry = new CatalogueEntry(
            _editingItem,
            _session.AllLocations
                .Select(site => new CatalogueSiteFee(site.Id, site.Name, true, null, 0m))
                .ToList());

        _editingFee = null;
        _editingDuration = null;
        _siteFeeDrafts.Clear();
        _lastAction = null;

        RaiseAll();
    }

    private Task SelectCatalogueItemAsync(Guid codeId) => RunGuardedAsync(async () =>
    {
        if (!CanManagePricing) return;

        var entry = await _billing.GetCatalogueEntryAsync(codeId).ConfigureAwait(false);

        if (entry is null)
        {
            ErrorMessage = "That item is no longer in the catalogue.";
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _editingIsNew = false;
        _editingEntry = entry;

        // A copy, not the row the list is holding. Editing the listed instance would leave
        // half-typed changes showing in the table behind the panel, and a cancelled edit
        // would have no original left to fall back to.
        _editingItem = Clone(entry.Code);

        _editingFee = Text(entry.Code.Fee);
        _editingDuration = entry.Code.TypicalDurationMinutes?.ToString();

        _siteFeeDrafts.Clear();

        foreach (var site in entry.Sites)
        {
            _siteFeeDrafts[site.LocationId] = site.SiteFee is { } fee ? Text(fee) : null;
        }

        _lastAction = null;
        RaiseAll();
    });

    private void CancelCatalogueEdit()
    {
        _editingItem = null;
        _editingEntry = null;
        _editingIsNew = false;
        _editingFee = null;
        _editingDuration = null;
        _siteFeeDrafts.Clear();

        RaiseAll();
    }

    private Task SaveCatalogueItemAsync() => RunGuardedAsync(async () =>
    {
        if (_editingItem is null) return;

        if (ParseMoney(_editingFee) is not { } fee)
        {
            ErrorMessage = "The practice fee is not a number.";
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _editingItem.Fee = fee;

        _editingItem.TypicalDurationMinutes =
            int.TryParse(_editingDuration, out var minutes) && minutes > 0 ? minutes : null;

        var refusal = await _billing
            .SaveCatalogueItemAsync(_editingItem)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        // The item is saved before its site fees, because a new item has no id until it
        // is, and a site fee needs one to point at. The refusal above is what stops a
        // half-applied edit: nothing site-specific is written if the item itself failed.
        var savedId = _editingItem.Id;

        var siteRefusal = await ApplySiteFeesAsync(savedId).ConfigureAwait(false);

        if (siteRefusal is { Length: > 0 })
        {
            // The item saved and some site fees may not have. Said plainly rather than
            // reported as a clean save: the alternative is a practice believing a price is
            // set at a site that is still on the practice fee.
            ErrorMessage = siteRefusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            await ReloadCatalogueAsync().ConfigureAwait(false);
            return;
        }

        var wasNew = _editingIsNew;

        _lastAction = wasNew
            ? $"Item {_editingItem.ItemNumber} added to the catalogue."
            : $"Item {_editingItem.ItemNumber} updated.";

        _editingIsNew = false;

        await ReloadCatalogueAsync().ConfigureAwait(false);

        // Reopened from the database rather than kept in memory, so the panel shows what
        // was actually stored — including the site rows that were cleared rather than
        // saved, which the in-memory draft cannot know about.
        await ReopenEditorAsync(savedId).ConfigureAwait(false);
    });

    /// <summary>
    /// Writes each site's box: a number sets that site's price, a blank clears it.
    /// </summary>
    /// <returns>Null when every site was applied, or why one was not.</returns>
    private async Task<string?> ApplySiteFeesAsync(Guid codeId)
    {
        if (_editingEntry is null) return null;

        foreach (var site in _editingEntry.Sites)
        {
            var text = SiteFeeDraft(site.LocationId);

            var wanted = string.IsNullOrWhiteSpace(text) ? null : ParseMoney(text);

            if (!string.IsNullOrWhiteSpace(text) && wanted is null)
            {
                return $"{site.LocationName}'s fee is not a number.";
            }

            // Nothing to do where the stored value already matches. Skipped rather than
            // written anyway, so a save that touched only the description does not stamp
            // an audit entry against every site.
            if (wanted == site.SiteFee) continue;

            var refusal = await _billing
                .SetSiteFeeAsync(codeId, site.LocationId, wanted)
                .ConfigureAwait(false);

            if (refusal is { Length: > 0 }) return refusal;
        }

        return null;
    }

    private Task ToggleCatalogueItemAsync(Guid codeId) => RunGuardedAsync(async () =>
    {
        var item = _catalogue.FirstOrDefault(row => row.Code.Id == codeId);
        if (item is null) return;

        var refusal = await _billing
            .SetCatalogueItemActiveAsync(codeId, !item.Code.IsActive)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = item.Code.IsActive
            ? $"Item {item.ItemNumber} withdrawn. It stays on invoices already raised."
            : $"Item {item.ItemNumber} restored. It can be charged again.";

        await ReloadCatalogueAsync().ConfigureAwait(false);

        if (_editingItem?.Id == codeId) await ReopenEditorAsync(codeId).ConfigureAwait(false);
    });

    private void ToggleShowWithdrawn()
    {
        _showWithdrawn = !_showWithdrawn;

        RaisePropertyChanged(nameof(ShowWithdrawn));
        RaisePropertyChanged(nameof(Catalogue));
    }

    private async Task ReloadCatalogueAsync()
    {
        // Withdrawn items included: the catalogue screen is where one is restored, so a
        // list that hides them has no way back. The Catalogue property filters them out
        // of the visible rows unless the user asks.
        _catalogue = await _billing
            .GetCatalogueAsync(includeWithdrawn: true)
            .ConfigureAwait(false);

        // The picker's copy is stale now — a fee may have moved — so it is dropped rather
        // than patched, and reloaded the next time a draft is opened.
        _lineChoices = [];
        _lineChoicesFor = Guid.Empty;
    }

    private async Task ReopenEditorAsync(Guid codeId)
    {
        var entry = await _billing.GetCatalogueEntryAsync(codeId).ConfigureAwait(false);

        if (entry is null)
        {
            CancelCatalogueEdit();
            return;
        }

        _editingEntry = entry;
        _editingItem = Clone(entry.Code);
        _editingFee = Text(entry.Code.Fee);
        _editingDuration = entry.Code.TypicalDurationMinutes?.ToString();

        _siteFeeDrafts.Clear();

        foreach (var site in entry.Sites)
        {
            _siteFeeDrafts[site.LocationId] = site.SiteFee is { } fee ? Text(fee) : null;
        }

        RaiseAll();
    }

    /// <summary>A detached copy, so a cancelled edit leaves the listed row untouched.</summary>
    private static ProcedureCode Clone(ProcedureCode code) => new()
    {
        Id = code.Id,
        TenantId = code.TenantId,
        CreatedUtc = code.CreatedUtc,
        UpdatedUtc = code.UpdatedUtc,
        ItemNumber = code.ItemNumber,
        Description = code.Description,
        PatientFriendlyName = code.PatientFriendlyName,
        Category = code.Category,
        Fee = code.Fee,
        IsPerTooth = code.IsPerTooth,
        RequiresSurface = code.RequiresSurface,
        TypicalDurationMinutes = code.TypicalDurationMinutes,
        IsCdbsEligible = code.IsCdbsEligible,
        IsActive = code.IsActive,
    };

    /// <summary>
    /// A money box's text as a number, or null where it is not one.
    /// </summary>
    /// <remarks>
    /// Invariant culture and a bare decimal: the boxes hold "145.50", not "$145.50", and
    /// accepting a currency symbol here would mean deciding what "$" means on a screen
    /// that prices in four countries. The label carries the currency instead.
    /// </remarks>
    private static decimal? ParseMoney(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        return decimal.TryParse(
            text.Trim(),
            System.Globalization.NumberStyles.AllowDecimalPoint,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value) && value >= 0m
            ? value
            : null;
    }

    /// <summary>The round-trip of <see cref="ParseMoney"/>, so an edit reopens as typed.</summary>
    private static string Text(decimal value) =>
        value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Everything the screen reads. Listed explicitly rather than raising a blanket change,
    /// which would re-render the amount box being typed in.
    /// </summary>
    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(Tab), nameof(Day), nameof(DayLabel), nameof(IsToday),
            nameof(Invoices), nameof(Selected), nameof(HasSelection), nameof(InvoiceCaption),
            nameof(Adjustment), nameof(IsAdjusting), nameof(AdjustmentTitle),
            nameof(AdjustmentExplanation), nameof(AdjustmentNeedsAmount), nameof(LastAction),
            nameof(IsRaising), nameof(IsChangingPatient), nameof(PickerTitle),
            nameof(PickerPrompt), nameof(PatientResults), nameof(PatientSearch),
            nameof(IsDraft), nameof(BillableVisits), nameof(AttachedVisit),
            nameof(LineTooth), nameof(TermDays),
            nameof(Catalogue), nameof(LineChoices), nameof(LineChoiceResults),
            nameof(ShowWithdrawn),
            nameof(ActiveItemCount), nameof(WithdrawnItemCount), nameof(HasMultipleSites), nameof(CanManagePricing),
            nameof(IsEditingItem), nameof(EditingItemIsNew), nameof(EditingItemId),
            nameof(EditorTitle), nameof(EditingItemIsActive), nameof(EditorSites),
            nameof(EditItemNumber), nameof(EditDescription), nameof(EditFriendlyName),
            nameof(EditCategory), nameof(EditFee), nameof(EditFeeIsValid),
            nameof(EditDuration), nameof(EditIsPerTooth), nameof(EditRequiresSurface),
            nameof(EditIsCdbsEligible), nameof(CanSaveItem),
            nameof(Claims), nameof(OpenClaimCount),
            nameof(RejectedClaimCount), nameof(AssessingClaimId), nameof(Debtors),
            nameof(DebtTotal), nameof(Banking),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
