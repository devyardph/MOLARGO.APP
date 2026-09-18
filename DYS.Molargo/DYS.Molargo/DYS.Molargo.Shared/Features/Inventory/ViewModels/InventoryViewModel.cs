using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Components;
using DYS.Molargo.Shared.Features.Inventory.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Inventory.ViewModels;

/// <summary>Which pane of the inventory screen is showing.</summary>
public enum InventoryTab
{
    Stock = 0,
    Orders = 1,
    Lab = 2,
    Sterilisation = 3,
    Equipment = 4,

    /// <summary>
    /// Appended rather than slotted next to Stock, where it reads best.
    /// </summary>
    /// <remarks>
    /// The tab goes in the URL, so renumbering the ones above would land a shared link on
    /// somebody else's pane — the same reason Admin appends its later tabs.
    /// </remarks>
    Suppliers = 5,

    /// <summary>
    /// Stock categories, on their own tab beside Suppliers.
    /// </summary>
    /// <remarks>
    /// Separate rather than a second panel on the Suppliers pane. They are managed the
    /// same way and used by the same screens, but they are not the same list — and a tab
    /// named for one thing that holds two is a tab nobody finds the second thing in.
    /// </remarks>
    Categories = 6,
}

/// <summary>
/// Stock, purchase orders, lab cases and sterilisation records.
/// </summary>
public sealed class InventoryViewModel : BaseViewModel, IDisposable
{
    private readonly IInventoryService _inventory;
    private readonly ISessionService _session;
    private readonly IAppNavigator _navigator;
    private readonly IClock _clock;

    private InventoryTab _tab = InventoryTab.Stock;

    private IReadOnlyList<StockRow> _stock = [];
    private string? _stockSearch;
    private int _stockPage;
    private IReadOnlyList<PurchaseOrderRow> _orders = [];
    private IReadOnlyList<Supplier> _suppliers = [];
    private IReadOnlyList<Supplier> _allSuppliers = [];
    private IReadOnlyList<StockCategory> _categories = [];
    private Supplier? _editingSupplier;
    private StockCategory? _editingCategory;
    private PurchaseOrderDetail? _selectedOrder;
    private IReadOnlyList<LabCaseRow> _labCases = [];
    private IReadOnlyList<CycleRow> _cycles = [];

    private Guid? _receivingLineId;
    private string? _receiveQuantity;
    private string? _receiveBatch;
    private string? _receiveExpiry;

    private Guid? _movingItemId;
    private IReadOnlyList<StockMovement> _movements = [];
    private StockMovementKind _moveKind = StockMovementKind.Consumed;
    private string? _moveQuantity;
    private string? _moveBatch;

    private string? _lastAction;

    public InventoryViewModel(
        IInventoryService inventory,
        ISessionService session,
        IAppNavigator navigator,
        IClock clock)
    {
        _inventory = inventory;
        _session = session;
        _navigator = navigator;
        _clock = clock;

        // Built once, in the constructor — never rebuilt per render.
        SelectTabCommand = new MvxAsyncCommand<InventoryTab>(SelectTabAsync);
        NewItemCommand = new MvxCommand(() => _navigator.ToNewStockItem());
        EditItemCommand = new MvxCommand<Guid>(id => _navigator.ToStockItem(id));
        AddToOrderCommand = new MvxAsyncCommand<Guid>(AddToOrderAsync);

        StartMoveCommand = new MvxAsyncCommand<Guid>(StartMoveAsync);
        CancelMoveCommand = new MvxCommand(() => StartMove(Guid.Empty));
        SetMoveKindCommand = new MvxCommand<StockMovementKind>(SetMoveKind);
        ApplyMoveCommand = new MvxAsyncCommand(ApplyMoveAsync);

        SelectOrderCommand = new MvxAsyncCommand<Guid>(SelectOrderAsync);
        RemoveOrderLineCommand = new MvxAsyncCommand<Guid>(RemoveOrderLineAsync);
        PlaceOrderCommand = new MvxAsyncCommand(PlaceOrderAsync);
        StartReceiveCommand = new MvxCommand<PurchaseOrderLine>(line => StartReceive(line!));
        CancelReceiveCommand = new MvxCommand(() => StartReceive(null));
        ApplyReceiveCommand = new MvxAsyncCommand(ApplyReceiveAsync);

        NewSupplierCommand = new MvxCommand(NewSupplier);
        EditSupplierCommand = new MvxCommand<Guid>(EditSupplier);
        CloseSupplierCommand = new MvxCommand(() => OpenSupplier(null));
        ToggleSupplierIsLaboratoryCommand = new MvxCommand(ToggleSupplierIsLaboratory);
        SaveSupplierCommand = new MvxAsyncCommand(SaveSupplierAsync);
        SetSupplierActiveCommand = new MvxAsyncCommand<Guid>(SetSupplierActiveAsync);

        NewCategoryCommand = new MvxCommand(() => OpenCategory(new StockCategory()));
        SelectCategoryCommand = new MvxCommand<Guid>(SelectCategory);
        CloseCategoryCommand = new MvxCommand(() => OpenCategory(null));
        SaveCategoryCommand = new MvxAsyncCommand(SaveCategoryAsync);
        SetCategoryActiveCommand = new MvxAsyncCommand(SetCategoryActiveAsync);
        MoveCategoryUpCommand = new MvxAsyncCommand(() => MoveCategoryAsync(-1));
        MoveCategoryDownCommand = new MvxAsyncCommand(() => MoveCategoryAsync(1));

        ReceiveLabCaseCommand = new MvxAsyncCommand<Guid>(ReceiveLabCaseAsync);
        ReleaseCycleCommand = new MvxAsyncCommand<Guid>(ReleaseCycleAsync);
        OpenPatientCommand = new MvxCommand<Guid>(id => _navigator.ToPatientRecord(id));

        _session.Changed += OnSessionChanged;
    }

    public IMvxAsyncCommand<InventoryTab> SelectTabCommand { get; }

    public IMvxCommand NewItemCommand { get; }

    public IMvxCommand<Guid> EditItemCommand { get; }

    public IMvxAsyncCommand<Guid> AddToOrderCommand { get; }

    public IMvxAsyncCommand<Guid> StartMoveCommand { get; }

    public IMvxCommand CancelMoveCommand { get; }

    public IMvxCommand<StockMovementKind> SetMoveKindCommand { get; }

    public IMvxAsyncCommand ApplyMoveCommand { get; }

    public IMvxAsyncCommand<Guid> SelectOrderCommand { get; }

    public IMvxAsyncCommand<Guid> RemoveOrderLineCommand { get; }

    public IMvxAsyncCommand PlaceOrderCommand { get; }

    public IMvxCommand<PurchaseOrderLine> StartReceiveCommand { get; }

    public IMvxCommand CancelReceiveCommand { get; }

    public IMvxAsyncCommand ApplyReceiveCommand { get; }

    public IMvxCommand NewSupplierCommand { get; }

    public IMvxCommand<Guid> EditSupplierCommand { get; }

    public IMvxCommand CloseSupplierCommand { get; }

    public IMvxCommand ToggleSupplierIsLaboratoryCommand { get; }

    public IMvxAsyncCommand SaveSupplierCommand { get; }

    /// <summary>Retires an active supplier, or restores a retired one.</summary>
    public IMvxAsyncCommand<Guid> SetSupplierActiveCommand { get; }

    public IMvxCommand NewCategoryCommand { get; }

    public IMvxCommand<Guid> SelectCategoryCommand { get; }

    public IMvxCommand CloseCategoryCommand { get; }

    public IMvxAsyncCommand SaveCategoryCommand { get; }

    public IMvxAsyncCommand SetCategoryActiveCommand { get; }

    public IMvxAsyncCommand MoveCategoryUpCommand { get; }

    public IMvxAsyncCommand MoveCategoryDownCommand { get; }

    public IMvxAsyncCommand<Guid> ReceiveLabCaseCommand { get; }

    public IMvxAsyncCommand<Guid> ReleaseCycleCommand { get; }

    public IMvxCommand<Guid> OpenPatientCommand { get; }

    public override Task Initialize() => LoadAsync();

    public InventoryTab Tab => _tab;

    public DateOnly Today => _clock.Today;

    public string? LastAction => _lastAction;

    // ---- stock -----------------------------------------------------------

    /// <summary>
    /// How many stock rows a page shows.
    /// </summary>
    /// <remarks>
    /// Seventeen, the same as the patients list, the audit log and a patient's alerts. The
    /// number is shared on purpose: all four use the one pager, and a control that appears
    /// at a different depth on each screen is one people learn not to trust.
    /// </remarks>
    public const int StockPageSize = 17;

    /// <summary>
    /// What the stock list is being searched for.
    /// </summary>
    /// <remarks>
    /// Matched over the rows already loaded, not in the database — the one place in this
    /// app where that is the right answer. The sort is worst-first on
    /// <see cref="StockFlag"/>, which is computed from an item's level, its reorder point
    /// and its earliest batch expiry rather than stored, so a database page would have to
    /// be taken in some other order and then re-sorted into this one. That is a page of
    /// arbitrary rows wearing the right sort: the item that is actually out of stock could
    /// sit on page four.
    ///
    /// Which is affordable because the whole list is read anyway. Flagging a row needs its
    /// movements, so <c>GetStockAsync</c> already loads every item at the site; paging here
    /// is for reading, not for avoiding a read.
    /// </remarks>
    public string? StockSearch
    {
        get => _stockSearch;
        set
        {
            if (!SetProperty(ref _stockSearch, value)) return;

            // Back to the first page. Searching from page three and staying there shows an
            // empty table whenever the match has fewer pages than that, which reads as "no
            // such item" for a term that has plenty.
            _stockPage = 0;

            RaiseStock();
        }
    }

    /// <summary>True where a search is narrowing the list.</summary>
    public bool IsStockNarrowed => !string.IsNullOrWhiteSpace(_stockSearch);

    /// <summary>
    /// The rows matching the search, still in worst-first order.
    /// </summary>
    /// <remarks>
    /// Name, category, supplier and batch number. Batch especially: a recall names a batch,
    /// and the question "do we still have any of this" is asked with that number in hand.
    /// </remarks>
    public IReadOnlyList<StockRow> FilteredStock
    {
        get
        {
            if (_stockSearch is not { } term || string.IsNullOrWhiteSpace(term)) return _stock;

            var needle = term.Trim();

            return _stock
                .Where(row =>
                    Has(row.Name, needle)
                    || Has(row.Category, needle)
                    || Has(row.SupplierName, needle)
                    || Has(row.BatchNumber, needle))
                .ToList();
        }
    }

    /// <summary>Case-insensitive, because nobody types a batch number's case from memory.</summary>
    private static bool Has(string? value, string term) =>
        value is { Length: > 0 }
        && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    /// <summary>The page of rows on screen.</summary>
    public IReadOnlyList<StockRow> Stock =>
        FilteredStock.Skip(_stockPage * StockPageSize).Take(StockPageSize).ToList();

    public int StockPage => _stockPage;

    public int StockPageCount =>
        Math.Max(1, (FilteredStock.Count + StockPageSize - 1) / StockPageSize);

    /// <summary>"1–17 of 42 items", or of the matches while searching.</summary>
    public string StockRangeLabel
    {
        get
        {
            var total = FilteredStock.Count;

            if (total == 0) return IsStockNarrowed ? "No matches" : "No items";

            var first = (_stockPage * StockPageSize) + 1;
            var last = Math.Min(total, (_stockPage + 1) * StockPageSize);

            return IsStockNarrowed
                ? $"{first}–{last} of {total} matching"
                : $"{first}–{last} of {total} items";
        }
    }

    public void GoToStockPage(int page)
    {
        var clamped = Math.Clamp(page, 0, StockPageCount - 1);

        if (clamped == _stockPage) return;

        _stockPage = clamped;
        RaiseStock();
    }

    private void RaiseStock()
    {
        foreach (var name in new[]
        {
            nameof(Stock), nameof(StockCaption), nameof(StockSearch),
            nameof(IsStockNarrowed), nameof(StockPage), nameof(StockPageCount),
            nameof(StockRangeLabel),
        })
        {
            RaisePropertyChanged(name);
        }
    }

    /// <summary>"3 low · 1 expiring · 1 out of stock" — the line under the heading.</summary>
    /// <remarks>
    /// Counted over the matches while searching, not over the whole site. Every number on
    /// the screen then describes the same set of rows — a caption reading "3 low" above a
    /// filtered table showing none of them is two facts that look like one.
    ///
    /// The site's own total is stated alongside, so narrowing never hides how much there is.
    /// </remarks>
    public string StockCaption
    {
        get
        {
            if (_stock.Count == 0) return "No stock items at this location";

            var rows = FilteredStock;

            if (rows.Count == 0)
            {
                return $"Nothing matches — {_stock.Count} items at this location";
            }

            var parts = new List<string>
            {
                IsStockNarrowed
                    ? $"{rows.Count} of {_stock.Count} items"
                    : $"{rows.Count} items",
            };

            var out_ = rows.Count(row => row.Flag == StockFlag.OutOfStock);
            var low = rows.Count(row => row.Flag == StockFlag.Low);
            var expired = rows.Count(row => row.Flag == StockFlag.Expired);
            var expiring = rows.Count(row => row.Flag == StockFlag.ExpiringSoon);

            if (out_ > 0) parts.Add($"{out_} out of stock");
            if (low > 0) parts.Add($"{low} low");
            if (expired > 0) parts.Add($"{expired} expired");
            if (expiring > 0) parts.Add($"{expiring} expiring within {InventoryService.ExpiryWarningDays} days");

            return string.Join(" · ", parts);
        }
    }

    // ---- suppliers and categories ----------------------------------------

    public IReadOnlyList<Supplier> AllSuppliers => _allSuppliers;

    public IReadOnlyList<StockCategory> Categories => _categories;

    /// <summary>The supplier open in the editor, or null when the panel is closed.</summary>
    public Supplier? EditingSupplier => _editingSupplier;

    public bool IsEditingSupplier => _editingSupplier is not null;

    /// <summary>True where the editor is adding rather than changing.</summary>
    public bool SupplierIsNew =>
        _editingSupplier is { } row && _allSuppliers.All(other => other.Id != row.Id);

    public string SupplierName
    {
        get => _editingSupplier?.Name ?? string.Empty;
        set { if (_editingSupplier is { } row) { row.Name = value; Raise(nameof(SupplierName), nameof(CanSaveSupplier)); } }
    }

    public string? SupplierAccountNumber
    {
        get => _editingSupplier?.AccountNumber;
        set { if (_editingSupplier is { } row) { row.AccountNumber = value; Raise(nameof(SupplierAccountNumber)); } }
    }

    public string? SupplierContactName
    {
        get => _editingSupplier?.ContactName;
        set { if (_editingSupplier is { } row) { row.ContactName = value; Raise(nameof(SupplierContactName)); } }
    }

    public string? SupplierPhone
    {
        get => _editingSupplier?.Phone;
        set { if (_editingSupplier is { } row) { row.Phone = value; Raise(nameof(SupplierPhone)); } }
    }

    public string? SupplierEmail
    {
        get => _editingSupplier?.Email;
        set { if (_editingSupplier is { } row) { row.Email = value; Raise(nameof(SupplierEmail)); } }
    }

    public string? SupplierWebsite
    {
        get => _editingSupplier?.Website;
        set { if (_editingSupplier is { } row) { row.Website = value; Raise(nameof(SupplierWebsite)); } }
    }

    /// <summary>
    /// Lead time as text, because the box is empty until somebody types in it.
    /// </summary>
    /// <remarks>
    /// Not an int with a zero default. Zero days is a real answer — a local rep who drops
    /// in — and absent means nobody has said, which is what a reorder date cannot be
    /// calculated from. Binding an int would make every new supplier claim same-day
    /// delivery.
    /// </remarks>
    public string? SupplierLeadTime
    {
        get => _editingSupplier?.LeadTimeDays?.ToString();
        set
        {
            if (_editingSupplier is not { } row) return;

            row.LeadTimeDays = int.TryParse(value, out var days) ? days : null;
            Raise(nameof(SupplierLeadTime));
        }
    }

    public bool SupplierIsLaboratory => _editingSupplier?.IsLaboratory ?? false;

    public string? SupplierNotes
    {
        get => _editingSupplier?.Notes;
        set { if (_editingSupplier is { } row) { row.Notes = value; Raise(nameof(SupplierNotes)); } }
    }

    public bool CanSaveSupplier =>
        !IsBusy && !string.IsNullOrWhiteSpace(_editingSupplier?.Name);

    /// <summary>The category open in the editor, or null when nothing is selected.</summary>
    public StockCategory? EditingCategory => _editingCategory;

    public bool IsEditingCategory => _editingCategory is not null;

    public bool CategoryIsNew =>
        _editingCategory is { } row && _categories.All(other => other.Id != row.Id);

    public string CategoryName
    {
        get => _editingCategory?.Name ?? string.Empty;
        set
        {
            if (_editingCategory is not { } row) return;

            row.Name = value;
            Raise(nameof(CategoryName), nameof(CanSaveCategory));
        }
    }

    public bool CanSaveCategory =>
        !IsBusy && !string.IsNullOrWhiteSpace(_editingCategory?.Name);

    public bool CategoryIsActive => _editingCategory?.IsActive ?? true;

    /// <summary>How many active items are filed under a category.</summary>
    /// <remarks>
    /// Shown beside each one, because it is the number that decides whether it can be
    /// retired — and seeing it first is better than being refused after clicking.
    /// </remarks>
    public int ItemsIn(string category) =>
        _stock.Count(row => string.Equals(row.Category, category, StringComparison.Ordinal));

    public bool CanMoveCategoryUp =>
        _editingCategory is { } row && !CategoryIsNew
        && _categories.Count > 1 && _categories[0].Id != row.Id;

    public bool CanMoveCategoryDown =>
        _editingCategory is { } row && !CategoryIsNew
        && _categories.Count > 1 && _categories[^1].Id != row.Id;

    public Guid? MovingItemId => _movingItemId;

    public bool IsMoving(Guid stockItemId) => _movingItemId == stockItemId;

    /// <summary>
    /// The row being adjusted, for the panel beside the table.
    /// </summary>
    /// <remarks>
    /// Looked up in the unfiltered list rather than the page on screen. Recording a use
    /// reloads the stock and can drop the item off the current page — it may no longer
    /// match the search, or the flag it sorts by may have changed — and a panel that
    /// emptied itself at that moment would look like the save had failed.
    /// </remarks>
    public StockRow? MovingItem => _movingItemId is { } id
        ? _stock.FirstOrDefault(row => row.StockItemId == id)
        : null;

    /// <summary>
    /// What has already happened to the item being adjusted, newest first.
    /// </summary>
    /// <remarks>
    /// Shown under the form rather than only on the item's own page. The question somebody
    /// has while standing at the cupboard is "did I already record this?", and the answer
    /// was two clicks away on a screen that loses the form they were filling in.
    /// </remarks>
    public IReadOnlyList<StockMovement> Movements => _movements;

    /// <summary>A number, before the button will record anything.</summary>
    public bool CanApplyMove =>
        !IsBusy
        && decimal.TryParse(_moveQuantity, out var quantity)
        && quantity > 0m;

    public StockMovementKind MoveKind => _moveKind;

    /// <summary>
    /// The movements the screen offers. Receiving happens against a purchase order, so it
    /// is not on this list — a receipt with no order behind it is how deliveries stop
    /// being reconciled.
    /// </summary>
    public static readonly StockMovementKind[] MoveKinds =
    [
        StockMovementKind.Consumed,
        StockMovementKind.Wastage,
        StockMovementKind.Adjustment,
    ];

    public string? MoveQuantity
    {
        get => _moveQuantity;
        set
        {
            if (!SetProperty(ref _moveQuantity, value)) return;

            // The Record button reads this. Without the extra notification it stays
            // disabled until something else happens to re-render, which looks like a
            // button that has stopped working.
            RaisePropertyChanged(nameof(CanApplyMove));
        }
    }

    public string? MoveBatch
    {
        get => _moveBatch;
        set => SetProperty(ref _moveBatch, value);
    }

    // ---- orders ----------------------------------------------------------

    public IReadOnlyList<PurchaseOrderRow> Orders => _orders;

    /// <summary>The consumables merchants, for the panel beside the order list.</summary>
    public IReadOnlyList<Supplier> Suppliers => _suppliers;

    public PurchaseOrderDetail? SelectedOrder => _selectedOrder;

    public bool HasOrder => _selectedOrder is not null;

    public int DraftOrderCount => _orders.Count(row => row.Status == PurchaseOrderStatus.Draft);

    public Guid? ReceivingLineId => _receivingLineId;

    public bool IsReceiving(Guid lineId) => _receivingLineId == lineId;

    public string? ReceiveQuantity
    {
        get => _receiveQuantity;
        set => SetProperty(ref _receiveQuantity, value);
    }

    public string? ReceiveBatch
    {
        get => _receiveBatch;
        set => SetProperty(ref _receiveBatch, value);
    }

    public string? ReceiveExpiry
    {
        get => _receiveExpiry;
        set => SetProperty(ref _receiveExpiry, value);
    }

    // ---- lab and sterilisation ------------------------------------------

    public IReadOnlyList<LabCaseRow> LabCases => _labCases;

    public int OverdueLabCount => _labCases.Count(row => row.IsOverdue(_clock.Today));

    public IReadOnlyList<CycleRow> Cycles => _cycles;

    public int AwaitingReleaseCount => _cycles.Count(row => row.AwaitingRelease);

    /// <summary>Every tray from today's cycles, traced to the patient it was used on.</summary>
    public IReadOnlyList<(CycleRow Cycle, CycleUseRow Use)> Traceability => _cycles
        .SelectMany(cycle => cycle.Uses.Select(use => (cycle, use)))
        .OrderByDescending(entry => entry.use.UsedUtc)
        .ToList();

    // ---- loading ---------------------------------------------------------

    private Task LoadAsync() => RunGuardedAsync(LoadTabAsync);

    private async Task SelectTabAsync(InventoryTab tab)
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
            case InventoryTab.Stock:
                _stock = await _inventory
                    .GetStockAsync(_session.LocationId)
                    .ConfigureAwait(false);

                // Clamped rather than reset. A reload after using stock — which is what
                // every adjustment on this screen causes — would otherwise throw somebody
                // back to page one mid-count. It only moves when the list has shrunk past
                // where they were standing.
                _stockPage = Math.Clamp(_stockPage, 0, StockPageCount - 1);
                break;

            case InventoryTab.Categories:
            case InventoryTab.Suppliers:
                _allSuppliers = await _inventory.GetAllSuppliersAsync().ConfigureAwait(false);
                _categories = await _inventory.GetCategoriesAsync().ConfigureAwait(false);

                // The stock too, for the item count beside each category. The tab is in
                // the URL, so this pane can be the first thing a session loads — and
                // without this every category would read "0 items", which is the number
                // that says retiring one is safe.
                _stock = await _inventory
                    .GetStockAsync(_session.LocationId)
                    .ConfigureAwait(false);
                break;

            case InventoryTab.Orders:
                _orders = await _inventory
                    .GetOrdersAsync(_session.LocationId)
                    .ConfigureAwait(false);

                _suppliers = await _inventory.GetSuppliersAsync().ConfigureAwait(false);

                if (_selectedOrder is { } open)
                {
                    _selectedOrder = await _inventory
                        .GetOrderAsync(open.Order.Id)
                        .ConfigureAwait(false);
                }

                break;

            case InventoryTab.Lab:
                _labCases = await _inventory.GetLabCasesAsync().ConfigureAwait(false);
                break;

            case InventoryTab.Sterilisation:
                _cycles = await _inventory
                    .GetCyclesAsync(_session.LocationId, _clock.Today)
                    .ConfigureAwait(false);
                break;
        }

        RaiseAll();
    }

    // ---- stock movements -------------------------------------------------

    private void StartMove(Guid stockItemId)
    {
        _movingItemId = stockItemId == Guid.Empty ? null : stockItemId;
        _moveKind = StockMovementKind.Consumed;
        _moveQuantity = "1";
        _moveBatch = null;
        _lastAction = null;

        // Cleared with the panel. A list left standing would belong to the last item looked
        // at, under the name of the one now open.
        if (_movingItemId is null) _movements = [];

        RaiseAll();
    }

    /// <summary>
    /// Opens the panel and reads the item's movement history.
    /// </summary>
    /// <remarks>
    /// The form appears first and the history fills in behind it, because the read is the
    /// slow half and the boxes are what somebody came to type in.
    /// </remarks>
    private Task StartMoveAsync(Guid stockItemId) => RunGuardedAsync(async () =>
    {
        StartMove(stockItemId);

        if (_movingItemId is not { } itemId)
        {
            return;
        }

        _movements = await _inventory.GetMovementsAsync(itemId).ConfigureAwait(false);

        await RaisePropertyChanged(nameof(Movements)).ConfigureAwait(false);
    });

    private void SetMoveKind(StockMovementKind kind)
    {
        _moveKind = kind;

        RaisePropertyChanged(nameof(MoveKind));
    }

    private Task ApplyMoveAsync() => RunGuardedAsync(async () =>
    {
        if (_movingItemId is not { } itemId) return;

        if (!decimal.TryParse(_moveQuantity, out var quantity))
        {
            ErrorMessage = "Enter the quantity as a number.";
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        var refusal = await _inventory
            .MoveStockAsync(itemId, _moveKind, quantity, _moveBatch,
                byProviderId: _session.ProviderId)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = $"{quantity:0.##} recorded as {KindLabel(_moveKind).ToLowerInvariant()}.";
        _movingItemId = null;

        await LoadTabAsync().ConfigureAwait(false);
    });

    // ---- suppliers -------------------------------------------------------

    private void NewSupplier() => OpenSupplier(new Supplier());

    private void EditSupplier(Guid supplierId)
    {
        // A copy, not the listed row. Binding straight to it would repaint the list as
        // somebody types, and cancelling would leave the edits showing in a panel that
        // claims to have discarded them.
        var source = _allSuppliers.FirstOrDefault(row => row.Id == supplierId);
        if (source is null) return;

        OpenSupplier(new Supplier
        {
            Id = source.Id,
            Name = source.Name,
            AccountNumber = source.AccountNumber,
            ContactName = source.ContactName,
            Phone = source.Phone,
            Email = source.Email,
            Website = source.Website,
            LeadTimeDays = source.LeadTimeDays,
            IsLaboratory = source.IsLaboratory,
            IsActive = source.IsActive,
            Notes = source.Notes,
            CreatedUtc = source.CreatedUtc,
        });
    }

    private void OpenSupplier(Supplier? supplier)
    {
        _editingSupplier = supplier;
        ErrorMessage = null;

        Raise(nameof(EditingSupplier), nameof(IsEditingSupplier), nameof(SupplierIsNew),
            nameof(SupplierName), nameof(SupplierAccountNumber), nameof(SupplierContactName),
            nameof(SupplierPhone), nameof(SupplierEmail), nameof(SupplierWebsite),
            nameof(SupplierLeadTime), nameof(SupplierIsLaboratory), nameof(SupplierNotes),
            nameof(CanSaveSupplier), nameof(HasError));
    }

    private void ToggleSupplierIsLaboratory()
    {
        if (_editingSupplier is not { } row) return;

        row.IsLaboratory = !row.IsLaboratory;
        Raise(nameof(SupplierIsLaboratory));
    }

    private Task SaveSupplierAsync() => RunGuardedAsync(async () =>
    {
        if (_editingSupplier is not { } supplier) return;

        var wasNew = SupplierIsNew;

        var refusal = await _inventory.SaveSupplierAsync(supplier).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = wasNew
            ? $"{supplier.Name} added."
            : $"{supplier.Name} updated.";

        _editingSupplier = null;

        await LoadTabAsync().ConfigureAwait(false);
    });

    private Task SetSupplierActiveAsync(Guid supplierId) => RunGuardedAsync(async () =>
    {
        var supplier = _allSuppliers.FirstOrDefault(row => row.Id == supplierId);
        if (supplier is null) return;

        var refusal = await _inventory
            .SetSupplierActiveAsync(supplierId, !supplier.IsActive)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = supplier.IsActive
            ? $"{supplier.Name} retired — they stay on past orders."
            : $"{supplier.Name} restored.";

        await LoadTabAsync().ConfigureAwait(false);
    });

    // ---- categories ------------------------------------------------------

    private void SelectCategory(Guid categoryId)
    {
        var source = _categories.FirstOrDefault(row => row.Id == categoryId);
        if (source is null) return;

        // A copy, for the same reason the supplier editor takes one: binding to the listed
        // row would rename it in the list as somebody types, and Cancel would leave the
        // change on screen in a list that claims to have discarded it.
        OpenCategory(new StockCategory
        {
            Id = source.Id,
            Name = source.Name,
            DisplayOrder = source.DisplayOrder,
            IsActive = source.IsActive,
            CreatedUtc = source.CreatedUtc,
        });
    }

    private void OpenCategory(StockCategory? category)
    {
        _editingCategory = category;
        ErrorMessage = null;

        Raise(nameof(EditingCategory), nameof(IsEditingCategory), nameof(CategoryIsNew),
            nameof(CategoryName), nameof(CategoryIsActive), nameof(CanSaveCategory),
            nameof(CanMoveCategoryUp), nameof(CanMoveCategoryDown), nameof(HasError));
    }

    private Task SaveCategoryAsync() => RunGuardedAsync(async () =>
    {
        if (_editingCategory is not { } category
            || string.IsNullOrWhiteSpace(category.Name))
        {
            return;
        }

        var wasNew = CategoryIsNew;
        var name = category.Name.Trim();

        var refusal = await _inventory
            .SaveCategoryAsync(wasNew ? Guid.Empty : category.Id, name)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = wasNew
            ? $"{name} added."
            : $"Renamed to {name} — every item filed under it moved too.";

        _editingCategory = null;

        await LoadTabAsync().ConfigureAwait(false);
    });

    private Task SetCategoryActiveAsync() => RunGuardedAsync(async () =>
    {
        if (_editingCategory is not { } category || CategoryIsNew) return;

        var refusal = await _inventory
            .SetCategoryActiveAsync(category.Id, !category.IsActive)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = category.IsActive
            ? $"{category.Name} retired — it is off the chips on the item editor."
            : $"{category.Name} restored.";

        _editingCategory = null;

        await LoadTabAsync().ConfigureAwait(false);
    });

    private Task MoveCategoryAsync(int direction) => RunGuardedAsync(async () =>
    {
        if (_editingCategory is not { } category || CategoryIsNew) return;

        var refusal = await _inventory
            .MoveCategoryAsync(category.Id, direction)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        await LoadTabAsync().ConfigureAwait(false);

        // Re-selected after the reload, so the row stays open and the arrows can be used
        // twice running. Dropping the selection would make every move a click-and-reopen.
        SelectCategory(category.Id);
    });

    /// <summary>Raises several properties at once, from a synchronous handler.</summary>
    private void Raise(params string[] names)
    {
        foreach (var name in names) RaisePropertyChanged(name);
    }

    private Task AddToOrderAsync(Guid stockItemId) => RunGuardedAsync(async () =>
    {
        var (_, refusal) = await _inventory
            .AddToOrderAsync(_session.LocationId, stockItemId, _session.ProviderId)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = "Added to a draft order for that supplier — see Purchase orders.";

        await LoadTabAsync().ConfigureAwait(false);
    });

    // ---- orders ----------------------------------------------------------

    private Task SelectOrderAsync(Guid orderId) => RunGuardedAsync(async () =>
    {
        _selectedOrder = await _inventory.GetOrderAsync(orderId).ConfigureAwait(false);
        _receivingLineId = null;
        _lastAction = null;

        RaiseAll();
    });

    private Task RemoveOrderLineAsync(Guid lineId) => RunGuardedAsync(async () =>
    {
        var refusal = await _inventory.RemoveOrderLineAsync(lineId).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        await LoadTabAsync().ConfigureAwait(false);
    });

    private Task PlaceOrderAsync() => RunGuardedAsync(async () =>
    {
        if (_selectedOrder is not { } order) return;

        var refusal = await _inventory.PlaceOrderAsync(order.Order.Id).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = "Order sent. Nothing was transmitted — email or phone it through as usual.";

        await LoadTabAsync().ConfigureAwait(false);
    });

    private void StartReceive(PurchaseOrderLine? line)
    {
        _receivingLineId = line?.Id;
        _receiveQuantity = line?.Outstanding.ToString("0.##");
        _receiveBatch = null;
        _receiveExpiry = null;
        _lastAction = null;

        RaiseAll();
    }

    private Task ApplyReceiveAsync() => RunGuardedAsync(async () =>
    {
        if (_receivingLineId is not { } lineId) return;

        if (!decimal.TryParse(_receiveQuantity, out var quantity))
        {
            ErrorMessage = "Enter the quantity as a number.";
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        DateOnly? expiry = DateOnly.TryParse(_receiveExpiry, out var parsed) ? parsed : null;

        var refusal = await _inventory
            .ReceiveLineAsync(lineId, quantity, _receiveBatch, expiry, _session.ProviderId)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = $"{quantity:0.##} received and added to stock.";
        _receivingLineId = null;

        await LoadTabAsync().ConfigureAwait(false);
    });

    // ---- lab and sterilisation ------------------------------------------

    private Task ReceiveLabCaseAsync(Guid labCaseId) => RunGuardedAsync(async () =>
    {
        var refusal = await _inventory.ReceiveLabCaseAsync(labCaseId).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = "Case marked back from the lab — the fit visit can go ahead.";

        await LoadTabAsync().ConfigureAwait(false);
    });

    private Task ReleaseCycleAsync(Guid cycleId) => RunGuardedAsync(async () =>
    {
        var refusal = await _inventory
            .ReleaseCycleAsync(cycleId, _session.ProviderId)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = "Load released for use, signed against your login.";

        await LoadTabAsync().ConfigureAwait(false);
    });

    private void OnSessionChanged(object? sender, EventArgs e) => _ = LoadAsync();

    public void Dispose() => _session.Changed -= OnSessionChanged;

    /// <summary>The words staff use for a movement, which are not the enum's.</summary>
    public static string KindLabel(StockMovementKind kind) => kind switch
    {
        StockMovementKind.OpeningBalance => "Opening count",
        StockMovementKind.Received => "Received",
        StockMovementKind.Consumed => "Used",
        StockMovementKind.Adjustment => "Stocktake adjustment",
        StockMovementKind.Wastage => "Wasted or expired",
        StockMovementKind.TransferOut => "Transferred out",
        StockMovementKind.TransferIn => "Transferred in",
        StockMovementKind.ReturnedToSupplier => "Returned to supplier",
        _ => kind.ToString(),
    };

    /// <summary>
    /// Everything the screen reads. Listed explicitly rather than raising a blanket change,
    /// which would re-render the quantity box being typed in.
    /// </summary>
    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(Tab), nameof(LastAction), nameof(Stock), nameof(StockCaption),
            nameof(StockSearch), nameof(IsStockNarrowed), nameof(StockPage),
            nameof(StockPageCount), nameof(StockRangeLabel),
            nameof(MovingItemId), nameof(MovingItem), nameof(CanApplyMove),
            nameof(Movements),
            nameof(MoveKind), nameof(MoveQuantity), nameof(MoveBatch),
            nameof(Orders), nameof(Suppliers), nameof(SelectedOrder), nameof(HasOrder),
            nameof(DraftOrderCount),
            nameof(ReceivingLineId), nameof(ReceiveQuantity), nameof(ReceiveBatch),
            nameof(ReceiveExpiry), nameof(LabCases), nameof(OverdueLabCount),
            nameof(Cycles), nameof(AwaitingReleaseCount), nameof(Traceability),
            nameof(AllSuppliers), nameof(Categories), nameof(EditingSupplier),
            nameof(IsEditingSupplier), nameof(SupplierIsNew), nameof(EditingCategory),
            nameof(IsEditingCategory), nameof(CategoryIsNew), nameof(CategoryName),
            nameof(CategoryIsActive), nameof(CanSaveCategory),
            nameof(CanMoveCategoryUp), nameof(CanMoveCategoryDown),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
