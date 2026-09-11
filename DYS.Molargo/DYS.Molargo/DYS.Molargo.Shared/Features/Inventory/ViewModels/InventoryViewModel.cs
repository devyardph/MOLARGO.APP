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
    private IReadOnlyList<PurchaseOrderRow> _orders = [];
    private IReadOnlyList<Supplier> _suppliers = [];
    private PurchaseOrderDetail? _selectedOrder;
    private IReadOnlyList<LabCaseRow> _labCases = [];
    private IReadOnlyList<CycleRow> _cycles = [];

    private Guid? _receivingLineId;
    private string? _receiveQuantity;
    private string? _receiveBatch;
    private string? _receiveExpiry;

    private Guid? _movingItemId;
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

        StartMoveCommand = new MvxCommand<Guid>(StartMove);
        CancelMoveCommand = new MvxCommand(() => StartMove(Guid.Empty));
        SetMoveKindCommand = new MvxCommand<StockMovementKind>(SetMoveKind);
        ApplyMoveCommand = new MvxAsyncCommand(ApplyMoveAsync);

        SelectOrderCommand = new MvxAsyncCommand<Guid>(SelectOrderAsync);
        RemoveOrderLineCommand = new MvxAsyncCommand<Guid>(RemoveOrderLineAsync);
        PlaceOrderCommand = new MvxAsyncCommand(PlaceOrderAsync);
        StartReceiveCommand = new MvxCommand<PurchaseOrderLine>(line => StartReceive(line!));
        CancelReceiveCommand = new MvxCommand(() => StartReceive(null));
        ApplyReceiveCommand = new MvxAsyncCommand(ApplyReceiveAsync);

        ReceiveLabCaseCommand = new MvxAsyncCommand<Guid>(ReceiveLabCaseAsync);
        ReleaseCycleCommand = new MvxAsyncCommand<Guid>(ReleaseCycleAsync);
        OpenPatientCommand = new MvxCommand<Guid>(id => _navigator.ToPatientRecord(id));

        _session.Changed += OnSessionChanged;
    }

    public IMvxAsyncCommand<InventoryTab> SelectTabCommand { get; }

    public IMvxCommand NewItemCommand { get; }

    public IMvxCommand<Guid> EditItemCommand { get; }

    public IMvxAsyncCommand<Guid> AddToOrderCommand { get; }

    public IMvxCommand<Guid> StartMoveCommand { get; }

    public IMvxCommand CancelMoveCommand { get; }

    public IMvxCommand<StockMovementKind> SetMoveKindCommand { get; }

    public IMvxAsyncCommand ApplyMoveCommand { get; }

    public IMvxAsyncCommand<Guid> SelectOrderCommand { get; }

    public IMvxAsyncCommand<Guid> RemoveOrderLineCommand { get; }

    public IMvxAsyncCommand PlaceOrderCommand { get; }

    public IMvxCommand<PurchaseOrderLine> StartReceiveCommand { get; }

    public IMvxCommand CancelReceiveCommand { get; }

    public IMvxAsyncCommand ApplyReceiveCommand { get; }

    public IMvxAsyncCommand<Guid> ReceiveLabCaseCommand { get; }

    public IMvxAsyncCommand<Guid> ReleaseCycleCommand { get; }

    public IMvxCommand<Guid> OpenPatientCommand { get; }

    public override Task Initialize() => LoadAsync();

    public InventoryTab Tab => _tab;

    public DateOnly Today => _clock.Today;

    public string? LastAction => _lastAction;

    // ---- stock -----------------------------------------------------------

    public IReadOnlyList<StockRow> Stock => _stock;

    /// <summary>"3 low · 1 expiring · 1 out of stock" — the line under the heading.</summary>
    public string StockCaption
    {
        get
        {
            if (_stock.Count == 0) return "No stock items at this location";

            var parts = new List<string> { $"{_stock.Count} items" };

            var out_ = _stock.Count(row => row.Flag == StockFlag.OutOfStock);
            var low = _stock.Count(row => row.Flag == StockFlag.Low);
            var expired = _stock.Count(row => row.Flag == StockFlag.Expired);
            var expiring = _stock.Count(row => row.Flag == StockFlag.ExpiringSoon);

            if (out_ > 0) parts.Add($"{out_} out of stock");
            if (low > 0) parts.Add($"{low} low");
            if (expired > 0) parts.Add($"{expired} expired");
            if (expiring > 0) parts.Add($"{expiring} expiring within {InventoryService.ExpiryWarningDays} days");

            return string.Join(" · ", parts);
        }
    }

    public Guid? MovingItemId => _movingItemId;

    public bool IsMoving(Guid stockItemId) => _movingItemId == stockItemId;

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
        set => SetProperty(ref _moveQuantity, value);
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

        RaiseAll();
    }

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
            nameof(MovingItemId), nameof(MoveKind), nameof(MoveQuantity), nameof(MoveBatch),
            nameof(Orders), nameof(Suppliers), nameof(SelectedOrder), nameof(HasOrder),
            nameof(DraftOrderCount),
            nameof(ReceivingLineId), nameof(ReceiveQuantity), nameof(ReceiveBatch),
            nameof(ReceiveExpiry), nameof(LabCases), nameof(OverdueLabCount),
            nameof(Cycles), nameof(AwaitingReleaseCount), nameof(Traceability),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
