using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Repositories;
using DYS.Molargo.Shared.Services;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Features.Inventory.Services;

/// <summary>
/// Why a stock line needs attention. Worst first — the order matters, because a row can be
/// two of these at once and the screen shows the one that decides what to do.
/// </summary>
public enum StockFlag
{
    Ok = 0,

    /// <summary>Expiring within the warning window but still usable.</summary>
    ExpiringSoon = 1,

    /// <summary>At or below the reorder level.</summary>
    Low = 2,

    /// <summary>Past its expiry date. Not usable, whatever the count says.</summary>
    Expired = 3,

    /// <summary>Nothing on the shelf.</summary>
    OutOfStock = 4,
}

/// <summary>One row of the stock list.</summary>
public sealed record StockRow(
    Guid StockItemId,
    string Name,
    string? Category,
    string? SupplierName,
    decimal OnHand,
    string? Unit,
    decimal ReorderLevel,
    decimal ReorderQuantity,
    string? BatchNumber,
    DateOnly? EarliestExpiry,
    StockFlag Flag,
    bool IsOnOrder)
{
    /// <summary>How many to order to get back to a working level.</summary>
    public decimal SuggestedOrder => ReorderQuantity > 0m
        ? ReorderQuantity
        : Math.Max(ReorderLevel - OnHand, 1m);

    public bool NeedsOrdering => Flag is StockFlag.Low or StockFlag.OutOfStock;
}

/// <summary>One purchase order as the list shows it.</summary>
public sealed record PurchaseOrderRow(
    Guid PurchaseOrderId,
    string? OrderNumber,
    string SupplierName,
    int LineCount,
    decimal Total,
    PurchaseOrderStatus Status,
    DateOnly? ExpectedOn);

/// <summary>A purchase order with its lines.</summary>
public sealed record PurchaseOrderDetail(
    PurchaseOrder Order,
    string SupplierName,
    IReadOnlyList<PurchaseOrderLine> Lines)
{
    public decimal Total => Lines.Sum(line => line.LineTotal);

    public bool IsDraft => Order.Status == PurchaseOrderStatus.Draft;

    public bool CanReceive => Order.Status is PurchaseOrderStatus.Ordered
        or PurchaseOrderStatus.PartiallyReceived;
}

/// <summary>One lab case in flight.</summary>
public sealed record LabCaseRow(
    Guid LabCaseId,
    Guid PatientId,
    string PatientName,
    string Description,
    string? ToothNumber,
    string? LabName,
    DateTime? SentUtc,
    DateOnly? DueOn,
    LabCaseStatus Status)
{
    public bool CanReceive => Status is LabCaseStatus.SentToLab
        or LabCaseStatus.InProduction or LabCaseStatus.Remake;

    /// <summary>Past due and still not back — the row that costs a fit appointment.</summary>
    public bool IsOverdue(DateOnly today) =>
        CanReceive && DueOn is { } due && due < today;
}

/// <summary>One autoclave cycle, with what it was used on.</summary>
public sealed record CycleRow(
    Guid CycleId,
    string SterilisorName,
    int CycleNumber,
    DateTime StartedUtc,
    DateTime? CompletedUtc,
    string? CycleType,
    decimal? PeakTemperatureCelsius,
    decimal? HoldTimeMinutes,
    bool ChemicalIndicatorPassed,
    bool? BiologicalIndicatorPassed,
    SterilisationResult Result,
    string? LoadContents,
    string? ReleasedBy,
    DateTime? ReleasedUtc,
    IReadOnlyList<CycleUseRow> Uses)
{
    /// <summary>
    /// A finished cycle nobody has signed off. The reason this list exists.
    /// </summary>
    public bool AwaitingRelease => CompletedUtc is not null && ReleasedUtc is null
        && Result is not (SterilisationResult.Failed or SterilisationResult.Aborted);

    /// <summary>"134°C · 3.5 min" — the parameters an auditor reads.</summary>
    public string Parameters
    {
        get
        {
            var parts = new List<string>();

            if (PeakTemperatureCelsius is { } peak) parts.Add($"{peak:0.#}°C");
            if (HoldTimeMinutes is { } hold) parts.Add($"{hold:0.#} min");
            if (CycleType is { Length: > 0 } type) parts.Add(type);

            return parts.Count == 0 ? "—" : string.Join(" · ", parts);
        }
    }
}

/// <summary>One tray from a cycle, traced to the patient it was used on.</summary>
public sealed record CycleUseRow(
    string? PackIdentifier, string PatientName, DateTime UsedUtc);

/// <summary>
/// Stock, purchase orders, lab cases and sterilisation records.
/// </summary>
public interface IInventoryService
{
    Task<IReadOnlyList<StockRow>> GetStockAsync(
        Guid locationId, CancellationToken ct = default);

    Task<StockItem?> GetItemAsync(Guid stockItemId, CancellationToken ct = default);

    Task<IReadOnlyList<Supplier>> GetSuppliersAsync(
        bool laboratories = false, CancellationToken ct = default);

    /// <summary>Creates or updates a stock item. Returns its id.</summary>
    /// <param name="openingBatch">
    /// The batch on the shelf at the opening count, for a batch-tracked item. Carried onto
    /// the opening movement rather than onto the item, so the batch that was counted stays
    /// attached to the count that named it.
    /// </param>
    Task<string?> SaveItemAsync(
        StockItem item,
        string? openingBatch = null,
        DateOnly? openingExpiry = null,
        CancellationToken ct = default);

    /// <summary>
    /// Archives an item rather than deleting it.
    /// </summary>
    /// <remarks>
    /// The design says as much: stock history, batch links and order lines are retained.
    /// Deleting the row would orphan the movements that trace a batch to a patient, which
    /// is the record a product recall depends on.
    /// </remarks>
    Task<string?> ArchiveItemAsync(Guid stockItemId, CancellationToken ct = default);

    /// <summary>
    /// Records a movement and re-derives the item's balance from the ledger.
    /// </summary>
    /// <param name="quantity">
    /// Always positive. The kind decides the sign, so a caller cannot accidentally add
    /// stock by passing a negative consumption.
    /// </param>
    Task<string?> MoveStockAsync(
        Guid stockItemId,
        StockMovementKind kind,
        decimal quantity,
        string? batchNumber = null,
        DateOnly? expiry = null,
        Guid? byProviderId = null,
        string? reference = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<StockMovement>> GetMovementsAsync(
        Guid stockItemId, CancellationToken ct = default);

    // ---- purchase orders ------------------------------------------------

    Task<IReadOnlyList<PurchaseOrderRow>> GetOrdersAsync(
        Guid locationId, CancellationToken ct = default);

    Task<PurchaseOrderDetail?> GetOrderAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>
    /// Puts an item on a draft order for its supplier, creating that draft if needed.
    /// </summary>
    /// <returns>The order it landed on, or a refusal.</returns>
    Task<(Guid? OrderId, string? Refusal)> AddToOrderAsync(
        Guid locationId, Guid stockItemId, Guid? byProviderId = null,
        CancellationToken ct = default);

    Task<string?> RemoveOrderLineAsync(Guid lineId, CancellationToken ct = default);

    /// <summary>Sends the order: assigns its number and stops the lines changing.</summary>
    Task<string?> PlaceOrderAsync(
        Guid orderId, int expectedInDays = 3, CancellationToken ct = default);

    /// <summary>
    /// Receives a line into stock, posting the movement that raises the balance.
    /// </summary>
    Task<string?> ReceiveLineAsync(
        Guid lineId,
        decimal quantity,
        string? batchNumber = null,
        DateOnly? expiry = null,
        Guid? byProviderId = null,
        CancellationToken ct = default);

    // ---- lab and sterilisation ------------------------------------------

    Task<IReadOnlyList<LabCaseRow>> GetLabCasesAsync(CancellationToken ct = default);

    Task<string?> ReceiveLabCaseAsync(Guid labCaseId, CancellationToken ct = default);

    Task<IReadOnlyList<CycleRow>> GetCyclesAsync(
        Guid locationId, DateOnly day, CancellationToken ct = default);

    /// <summary>
    /// Releases a finished load for use.
    /// </summary>
    /// <remarks>
    /// Refused on a failed cycle. Releasing a load whose indicators failed is the single
    /// worst thing this screen could allow — the instruments are not sterile and the
    /// record would say a named person said they were.
    /// </remarks>
    Task<string?> ReleaseCycleAsync(
        Guid cycleId, Guid? byProviderId = null, CancellationToken ct = default);
}

/// <inheritdoc cref="IInventoryService"/>
public sealed class InventoryService : IInventoryService
{
    /// <summary>
    /// How far ahead an expiry counts as imminent.
    /// </summary>
    /// <remarks>
    /// Sixty days, which is long enough to use the stock up or return it and short enough
    /// that the warning list stays worth reading.
    /// </remarks>
    public const int ExpiryWarningDays = 60;

    private readonly IRepository<StockItem> _items;
    private readonly IRepository<StockMovement> _movements;
    private readonly IRepository<Supplier> _suppliers;
    private readonly IRepository<PurchaseOrder> _orders;
    private readonly IRepository<PurchaseOrderLine> _orderLines;
    private readonly IRepository<LabCase> _labCases;
    private readonly IRepository<SterilisationCycle> _cycles;
    private readonly IRepository<SterilisationCycleUse> _cycleUses;
    private readonly IRepository<PatientEntity> _patients;
    private readonly IRepository<Provider> _providers;
    private readonly IPracticeGuard _guard;
    private readonly IClock _clock;

    public InventoryService(
        IRepository<StockItem> items,
        IRepository<StockMovement> movements,
        IRepository<Supplier> suppliers,
        IRepository<PurchaseOrder> orders,
        IRepository<PurchaseOrderLine> orderLines,
        IRepository<LabCase> labCases,
        IRepository<SterilisationCycle> cycles,
        IRepository<SterilisationCycleUse> cycleUses,
        IRepository<PatientEntity> patients,
        IRepository<Provider> providers,
        IPracticeGuard guard,
        IClock clock)
    {
        _items = items;
        _movements = movements;
        _suppliers = suppliers;
        _orders = orders;
        _orderLines = orderLines;
        _labCases = labCases;
        _cycles = cycles;
        _cycleUses = cycleUses;
        _patients = patients;
        _providers = providers;
        _guard = guard;
        _clock = clock;
    }

    public async Task<IReadOnlyList<StockRow>> GetStockAsync(
        Guid locationId, CancellationToken ct = default)
    {
        var items = await _items
            .ListAsync(item => item.PracticeLocationId == locationId && item.IsActive, ct)
            .ConfigureAwait(false);

        if (items.Count == 0) return [];

        var suppliers = await _suppliers.ListAsync(ct: ct).ConfigureAwait(false);
        var supplierNames = suppliers.ToDictionary(s => s.Id, s => s.Name);

        // Which items are already on an open order, so the screen does not invite the
        // same thing being ordered twice while the first delivery is in transit.
        var openOrders = await _orders
            .ListAsync(order => order.PracticeLocationId == locationId
                && (order.Status == PurchaseOrderStatus.Draft
                    || order.Status == PurchaseOrderStatus.Ordered
                    || order.Status == PurchaseOrderStatus.PartiallyReceived), ct)
            .ConfigureAwait(false);

        var openOrderIds = openOrders.Select(order => order.Id).ToHashSet();

        var openLines = await _orderLines
            .ListAsync(line => openOrderIds.Contains(line.PurchaseOrderId), ct)
            .ConfigureAwait(false);

        var onOrder = openLines
            .Where(line => !line.IsFullyReceived)
            .Select(line => line.StockItemId)
            .ToHashSet();

        var today = _clock.Today;

        // The batch showing is the one expiring first, which is the one that should be
        // used next and the one worth warning about.
        var itemIds = items.Select(item => item.Id).ToHashSet();

        var batches = await _movements
            .ListAsync(movement => itemIds.Contains(movement.StockItemId)
                && movement.ExpiryDate != null, ct)
            .ConfigureAwait(false);

        var earliestBatch = batches
            .GroupBy(movement => movement.StockItemId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(movement => movement.ExpiryDate).First());

        return items
            .Select(item =>
            {
                var batch = earliestBatch.GetValueOrDefault(item.Id);
                var expiry = item.EarliestExpiry ?? batch?.ExpiryDate;

                return new StockRow(
                    item.Id,
                    item.Name,
                    item.Category,
                    item.SupplierId is { } supplierId
                        ? supplierNames.GetValueOrDefault(supplierId)
                        : null,
                    item.QuantityOnHand,
                    item.UnitOfMeasure,
                    item.ReorderLevel,
                    item.ReorderQuantity,
                    batch?.BatchNumber,
                    expiry,
                    Flag(item, expiry, today),
                    onOrder.Contains(item.Id));
            })
            .OrderByDescending(row => row.Flag)
            .ThenBy(row => row.Category)
            .ThenBy(row => row.Name)
            .ToList();
    }

    public async Task<StockItem?> GetItemAsync(Guid stockItemId, CancellationToken ct = default) =>
        await _items.GetByIdAsync(stockItemId, ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<Supplier>> GetSuppliersAsync(
        bool laboratories = false, CancellationToken ct = default)
    {
        var suppliers = await _suppliers
            .ListAsync(supplier => supplier.IsActive && supplier.IsLaboratory == laboratories, ct)
            .ConfigureAwait(false);

        return suppliers.OrderBy(supplier => supplier.Name).ToList();
    }

    public async Task<string?> SaveItemAsync(
        StockItem item,
        string? openingBatch = null,
        DateOnly? openingExpiry = null,
        CancellationToken ct = default)
    {
        var denied = await _guard
            .RefuseAsync(PracticePermissions.ManageInventory, ct)
            .ConfigureAwait(false);

        if (denied is not null) return denied;

        await _items.SaveAsync(item, ct).ConfigureAwait(false);

        // A new item's opening count is a movement like any other, so the ledger and the
        // balance agree from the first day rather than from the first delivery.
        var existing = await _movements
            .ListAsync(movement => movement.StockItemId == item.Id, ct)
            .ConfigureAwait(false);

        if (existing.Count == 0 && item.QuantityOnHand != 0m)
        {
            await _movements
                .SaveAsync(
                    new StockMovement
                    {
                        StockItemId = item.Id,
                        Kind = StockMovementKind.OpeningBalance,
                        QuantityChange = item.QuantityOnHand,
                        BalanceAfter = item.QuantityOnHand,
                        OccurredUtc = _clock.UtcNow,
                        BatchNumber = string.IsNullOrWhiteSpace(openingBatch)
                            ? null
                            : openingBatch.Trim(),
                        ExpiryDate = openingExpiry,
                        UnitCost = item.UnitCost,
                        Notes = "Opening count",
                    },
                    ct)
                .ConfigureAwait(false);

            // Derived rather than trusted, even here. The form typed the count and the
            // expiry into two separate fields, and only the ledger knows they agree.
            await RebalanceAsync(item.Id, ct).ConfigureAwait(false);
        }

        return null;
    }

    public async Task<string?> ArchiveItemAsync(
        Guid stockItemId, CancellationToken ct = default)
    {
        var denied = await _guard
            .RefuseAsync(PracticePermissions.ManageInventory, ct)
            .ConfigureAwait(false);

        if (denied is not null) return denied;

        var item = await _items.GetByIdAsync(stockItemId, ct).ConfigureAwait(false);

        // Already gone is a success, not a refusal: the caller asked for it not to be in
        // the list, and it is not.
        if (item is null) return null;

        item.IsActive = false;

        await _items.SaveAsync(item, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> MoveStockAsync(
        Guid stockItemId,
        StockMovementKind kind,
        decimal quantity,
        string? batchNumber = null,
        DateOnly? expiry = null,
        Guid? byProviderId = null,
        string? reference = null,
        CancellationToken ct = default)
    {
        var item = await _items.GetByIdAsync(stockItemId, ct).ConfigureAwait(false);
        if (item is null) return "That item no longer exists.";

        if (quantity <= 0m) return "Enter a quantity greater than zero.";

        var change = IsIncrease(kind) ? quantity : -quantity;

        // Stock cannot go negative. A count below zero means the shelf and the ledger have
        // already diverged, and letting it through hides the moment they did.
        if (item.QuantityOnHand + change < 0m)
        {
            return $"Only {item.QuantityOnHand:0.##} {item.UnitOfMeasure ?? "units"} on hand. "
                + "Record an adjustment if the shelf disagrees with the count.";
        }

        if (item.RequiresBatchTracking && IsIncrease(kind)
            && string.IsNullOrWhiteSpace(batchNumber))
        {
            // Refused rather than saved blank: this item is batch-tracked precisely so a
            // recall can find which patients got which batch, and a blank breaks that
            // chain for everything it is later used on.
            return $"{item.Name} is batch-tracked. Record the batch number.";
        }

        var balance = item.QuantityOnHand + change;

        await _movements
            .SaveAsync(
                new StockMovement
                {
                    StockItemId = stockItemId,
                    Kind = kind,
                    QuantityChange = change,
                    BalanceAfter = balance,
                    OccurredUtc = _clock.UtcNow,
                    BatchNumber = string.IsNullOrWhiteSpace(batchNumber) ? null : batchNumber.Trim(),
                    ExpiryDate = expiry,
                    UnitCost = item.UnitCost,
                    RecordedByProviderId = byProviderId,
                    Reference = reference,
                },
                ct)
            .ConfigureAwait(false);

        await RebalanceAsync(stockItemId, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<IReadOnlyList<StockMovement>> GetMovementsAsync(
        Guid stockItemId, CancellationToken ct = default)
    {
        var movements = await _movements
            .ListAsync(movement => movement.StockItemId == stockItemId, ct)
            .ConfigureAwait(false);

        return movements
            .OrderByDescending(movement => movement.OccurredUtc)
            .Take(30)
            .ToList();
    }

    // ---- purchase orders ------------------------------------------------

    public async Task<IReadOnlyList<PurchaseOrderRow>> GetOrdersAsync(
        Guid locationId, CancellationToken ct = default)
    {
        var orders = await _orders
            .ListAsync(order => order.PracticeLocationId == locationId, ct)
            .ConfigureAwait(false);

        if (orders.Count == 0) return [];

        var ids = orders.Select(order => order.Id).ToHashSet();

        var lines = await _orderLines
            .ListAsync(line => ids.Contains(line.PurchaseOrderId), ct)
            .ConfigureAwait(false);

        var suppliers = await _suppliers.ListAsync(ct: ct).ConfigureAwait(false);
        var names = suppliers.ToDictionary(supplier => supplier.Id, supplier => supplier.Name);

        return orders
            .OrderBy(order => order.Status == PurchaseOrderStatus.Draft ? 0 : 1)
            .ThenByDescending(order => order.OrderedUtc ?? order.CreatedUtc)
            .Select(order =>
            {
                var own = lines.Where(line => line.PurchaseOrderId == order.Id).ToList();

                return new PurchaseOrderRow(
                    order.Id,
                    order.OrderNumber,
                    names.GetValueOrDefault(order.SupplierId, "Unknown supplier"),
                    own.Count,
                    own.Sum(line => line.LineTotal),
                    order.Status,
                    order.ExpectedOn);
            })
            .ToList();
    }

    public async Task<PurchaseOrderDetail?> GetOrderAsync(
        Guid orderId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct).ConfigureAwait(false);
        if (order is null) return null;

        var lines = await _orderLines
            .ListAsync(line => line.PurchaseOrderId == orderId, ct)
            .ConfigureAwait(false);

        var supplier = await _suppliers.GetByIdAsync(order.SupplierId, ct).ConfigureAwait(false);

        return new PurchaseOrderDetail(
            order,
            supplier?.Name ?? "Unknown supplier",
            lines.OrderBy(line => line.Description).ToList());
    }

    public async Task<(Guid? OrderId, string? Refusal)> AddToOrderAsync(
        Guid locationId, Guid stockItemId, Guid? byProviderId = null,
        CancellationToken ct = default)
    {
        var denied = await _guard
            .RefuseAsync(PracticePermissions.ManageInventory, ct)
            .ConfigureAwait(false);

        if (denied is not null) return (null, denied);

        var item = await _items.GetByIdAsync(stockItemId, ct).ConfigureAwait(false);
        if (item is null) return (null, "That item no longer exists.");

        if (item.SupplierId is not { } supplierId)
        {
            // An order has to go to somebody. Said plainly rather than creating an order
            // with no supplier that could never be sent.
            return (null, $"{item.Name} has no supplier set. Edit the item and choose one.");
        }

        var drafts = await _orders
            .ListAsync(order => order.PracticeLocationId == locationId
                && order.SupplierId == supplierId
                && order.Status == PurchaseOrderStatus.Draft, ct)
            .ConfigureAwait(false);

        var order = drafts.OrderByDescending(entry => entry.CreatedUtc).FirstOrDefault();

        if (order is null)
        {
            order = new PurchaseOrder
            {
                PracticeLocationId = locationId,
                SupplierId = supplierId,
                Status = PurchaseOrderStatus.Draft,
                RaisedByProviderId = byProviderId,
            };

            await _orders.SaveAsync(order, ct).ConfigureAwait(false);
        }

        var existing = await _orderLines
            .ListAsync(line => line.PurchaseOrderId == order.Id
                && line.StockItemId == stockItemId, ct)
            .ConfigureAwait(false);

        // Already on this draft is a no-op. Two lines for one item order twice as much as
        // intended, and nobody notices until the boxes arrive.
        if (existing.Count > 0) return (order.Id, null);

        var quantity = item.ReorderQuantity > 0m
            ? item.ReorderQuantity
            : Math.Max(item.ReorderLevel - item.QuantityOnHand, 1m);

        await _orderLines
            .SaveAsync(
                new PurchaseOrderLine
                {
                    PurchaseOrderId = order.Id,
                    StockItemId = stockItemId,
                    Description = item.Name,
                    QuantityOrdered = quantity,
                    UnitCost = item.UnitCost,
                },
                ct)
            .ConfigureAwait(false);

        return (order.Id, null);
    }

    public async Task<string?> RemoveOrderLineAsync(
        Guid lineId, CancellationToken ct = default)
    {
        var denied = await _guard
            .RefuseAsync(PracticePermissions.ManageInventory, ct)
            .ConfigureAwait(false);

        if (denied is not null) return denied;

        var line = await _orderLines.GetByIdAsync(lineId, ct).ConfigureAwait(false);
        if (line is null) return null;

        var order = await _orders.GetByIdAsync(line.PurchaseOrderId, ct).ConfigureAwait(false);

        if (order is null || order.Status != PurchaseOrderStatus.Draft)
        {
            return "That order has been sent. Its lines cannot be changed.";
        }

        await _orderLines.DeleteAsync(lineId, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> PlaceOrderAsync(
        Guid orderId, int expectedInDays = 3, CancellationToken ct = default)
    {
        var denied = await _guard
            .RefuseAsync(PracticePermissions.ManageInventory, ct)
            .ConfigureAwait(false);

        if (denied is not null) return denied;

        var order = await _orders.GetByIdAsync(orderId, ct).ConfigureAwait(false);
        if (order is null) return "That order no longer exists.";

        if (order.Status != PurchaseOrderStatus.Draft)
        {
            return "That order has already been sent.";
        }

        var lines = await _orderLines
            .ListAsync(line => line.PurchaseOrderId == orderId, ct)
            .ConfigureAwait(false);

        if (lines.Count == 0) return "Add at least one item before sending.";

        order.OrderNumber = await NextOrderNumberAsync(ct).ConfigureAwait(false);
        order.OrderedUtc = _clock.UtcNow;
        order.ExpectedOn = _clock.Today.AddDays(Math.Max(expectedInDays, 0));
        order.Status = PurchaseOrderStatus.Ordered;

        await _orders.SaveAsync(order, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> ReceiveLineAsync(
        Guid lineId,
        decimal quantity,
        string? batchNumber = null,
        DateOnly? expiry = null,
        Guid? byProviderId = null,
        CancellationToken ct = default)
    {
        var line = await _orderLines.GetByIdAsync(lineId, ct).ConfigureAwait(false);
        if (line is null) return "That line no longer exists.";

        var order = await _orders.GetByIdAsync(line.PurchaseOrderId, ct).ConfigureAwait(false);
        if (order is null) return "That order no longer exists.";

        if (order.Status is PurchaseOrderStatus.Draft)
        {
            return "Send the order before receiving against it.";
        }

        if (quantity <= 0m) return "Enter a quantity greater than zero.";

        // Over-receiving is refused. More in the box than on the order means the order is
        // wrong or the delivery is somebody else's, and quietly absorbing it loses both.
        if (quantity > line.Outstanding)
        {
            return $"Only {line.Outstanding:0.##} still outstanding on that line.";
        }

        var refusal = await MoveStockAsync(
                line.StockItemId,
                StockMovementKind.Received,
                quantity,
                batchNumber,
                expiry,
                byProviderId,
                reference: order.OrderNumber,
                ct)
            .ConfigureAwait(false);

        // The movement is what raises the balance, so a refused movement must not advance
        // the order — otherwise the order says received and the shelf does not.
        if (refusal is { Length: > 0 }) return refusal;

        line.QuantityReceived += quantity;
        line.BatchNumber = batchNumber ?? line.BatchNumber;
        line.ExpiryDate = expiry ?? line.ExpiryDate;

        await _orderLines.SaveAsync(line, ct).ConfigureAwait(false);

        var all = await _orderLines
            .ListAsync(entry => entry.PurchaseOrderId == order.Id, ct)
            .ConfigureAwait(false);

        var complete = all.All(entry => entry.IsFullyReceived);

        order.Status = complete
            ? PurchaseOrderStatus.Received
            : PurchaseOrderStatus.PartiallyReceived;

        order.ReceivedUtc = complete ? _clock.UtcNow : null;

        await _orders.SaveAsync(order, ct).ConfigureAwait(false);
        return null;
    }

    // ---- lab and sterilisation ------------------------------------------

    public async Task<IReadOnlyList<LabCaseRow>> GetLabCasesAsync(CancellationToken ct = default)
    {
        var cases = await _labCases
            .ListAsync(labCase => labCase.Status != LabCaseStatus.Cancelled, ct)
            .ConfigureAwait(false);

        if (cases.Count == 0) return [];

        var patientIds = cases.Select(labCase => labCase.PatientId).ToHashSet();

        var patients = await _patients
            .ListAsync(patient => patientIds.Contains(patient.Id), ct)
            .ConfigureAwait(false);

        var names = patients.ToDictionary(patient => patient.Id, patient => patient.FullName);

        var suppliers = await _suppliers.ListAsync(ct: ct).ConfigureAwait(false);
        var labs = suppliers.ToDictionary(supplier => supplier.Id, supplier => supplier.Name);

        return cases
            // Work still out at the lab first, and within that the soonest due — that is
            // the order the fit appointments come up in.
            .OrderBy(labCase => labCase.Status == LabCaseStatus.Fitted ? 1 : 0)
            .ThenBy(labCase => labCase.DueOn ?? DateOnly.MaxValue)
            .Select(labCase => new LabCaseRow(
                labCase.Id,
                labCase.PatientId,
                names.GetValueOrDefault(labCase.PatientId, "Unknown patient"),
                labCase.Description,
                labCase.ToothNumber,
                labCase.SupplierId is { } supplierId ? labs.GetValueOrDefault(supplierId) : null,
                labCase.SentUtc,
                labCase.DueOn,
                labCase.Status))
            .ToList();
    }

    public async Task<string?> ReceiveLabCaseAsync(Guid labCaseId, CancellationToken ct = default)
    {
        var labCase = await _labCases.GetByIdAsync(labCaseId, ct).ConfigureAwait(false);
        if (labCase is null) return "That case no longer exists.";

        if (labCase.ReceivedUtc is not null) return "That case is already back.";

        labCase.ReceivedUtc = _clock.UtcNow;
        labCase.Status = LabCaseStatus.Received;

        await _labCases.SaveAsync(labCase, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<IReadOnlyList<CycleRow>> GetCyclesAsync(
        Guid locationId, DateOnly day, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = LocalDayToUtc(day);

        var cycles = await _cycles
            .ListAsync(cycle => cycle.PracticeLocationId == locationId
                && cycle.StartedUtc >= fromUtc && cycle.StartedUtc < toUtc, ct)
            .ConfigureAwait(false);

        if (cycles.Count == 0) return [];

        var cycleIds = cycles.Select(cycle => cycle.Id).ToHashSet();

        var uses = await _cycleUses
            .ListAsync(use => cycleIds.Contains(use.SterilisationCycleId), ct)
            .ConfigureAwait(false);

        var patientIds = uses.Select(use => use.PatientId).ToHashSet();

        var patients = await _patients
            .ListAsync(patient => patientIds.Contains(patient.Id), ct)
            .ConfigureAwait(false);

        var names = patients.ToDictionary(patient => patient.Id, patient => patient.FullName);

        var providers = await _providers.ListAsync(ct: ct).ConfigureAwait(false);
        var staff = providers.ToDictionary(provider => provider.Id, provider => provider.FullName);

        return cycles
            .OrderByDescending(cycle => cycle.StartedUtc)
            .Select(cycle => new CycleRow(
                cycle.Id,
                cycle.SterilisorName,
                cycle.CycleNumber,
                cycle.StartedUtc,
                cycle.CompletedUtc,
                cycle.CycleType,
                cycle.PeakTemperatureCelsius,
                cycle.HoldTimeMinutes,
                cycle.ChemicalIndicatorPassed,
                cycle.BiologicalIndicatorPassed,
                cycle.Result,
                cycle.LoadContents,
                cycle.ReleasedByProviderId is { } releasedBy
                    ? staff.GetValueOrDefault(releasedBy)
                    : null,
                cycle.ReleasedUtc,
                uses
                    .Where(use => use.SterilisationCycleId == cycle.Id)
                    .OrderBy(use => use.UsedUtc)
                    .Select(use => new CycleUseRow(
                        use.PackIdentifier,
                        names.GetValueOrDefault(use.PatientId, "Unknown patient"),
                        use.UsedUtc))
                    .ToList()))
            .ToList();
    }

    public async Task<string?> ReleaseCycleAsync(
        Guid cycleId, Guid? byProviderId = null, CancellationToken ct = default)
    {
        var cycle = await _cycles.GetByIdAsync(cycleId, ct).ConfigureAwait(false);
        if (cycle is null) return "That cycle no longer exists.";

        if (cycle.IsReleased) return "That load has already been released.";

        if (cycle.CompletedUtc is null)
        {
            return "That cycle has not finished. Wait for it to complete.";
        }

        if (cycle.Result is SterilisationResult.Failed or SterilisationResult.Aborted)
        {
            return "That cycle failed. The load must be reprocessed, not released.";
        }

        if (!cycle.ChemicalIndicatorPassed)
        {
            return "The chemical indicator did not pass. The load cannot be released.";
        }

        cycle.ReleasedUtc = _clock.UtcNow;
        cycle.ReleasedByProviderId = byProviderId;

        if (cycle.Result == SterilisationResult.InProgress)
        {
            cycle.Result = SterilisationResult.Passed;
        }

        await _cycles.SaveAsync(cycle, ct).ConfigureAwait(false);
        return null;
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// Which movement kinds put stock on the shelf rather than take it off.
    /// </summary>
    /// <remarks>
    /// The sign lives here rather than at each call site, so a caller passes a plain
    /// positive quantity and cannot add stock by accidentally negating a consumption.
    /// </remarks>
    private static bool IsIncrease(StockMovementKind kind) => kind is
        StockMovementKind.OpeningBalance
        or StockMovementKind.Received
        or StockMovementKind.TransferIn;

    /// <summary>
    /// Re-derives the item's balance and earliest expiry from its movement ledger.
    /// </summary>
    /// <remarks>
    /// <c>QuantityOnHand</c> is a cache of the movements, exactly as an invoice's paid
    /// total is a cache of its payments. Summing the ledger after every change is what
    /// stops the shelf figure drifting from the history that explains it.
    /// </remarks>
    private async Task RebalanceAsync(Guid stockItemId, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(stockItemId, ct).ConfigureAwait(false);
        if (item is null) return;

        var movements = await _movements
            .ListAsync(movement => movement.StockItemId == stockItemId, ct)
            .ConfigureAwait(false);

        item.QuantityOnHand = movements.Sum(movement => movement.QuantityChange);

        // The soonest expiry still on the shelf. Consumed batches are not filtered out
        // individually — the ledger does not track which batch was used — so this is the
        // earliest expiry ever received, which errs towards warning too early rather than
        // too late.
        item.EarliestExpiry = movements
            .Where(movement => movement.ExpiryDate is not null
                && movement.QuantityChange > 0m)
            .Select(movement => movement.ExpiryDate)
            .DefaultIfEmpty(null)
            .Min();

        await _items.SaveAsync(item, ct).ConfigureAwait(false);
    }

    private static StockFlag Flag(StockItem item, DateOnly? expiry, DateOnly today)
    {
        if (item.QuantityOnHand <= 0m) return StockFlag.OutOfStock;

        if (expiry is { } date)
        {
            if (date < today) return StockFlag.Expired;

            if (date.DayNumber - today.DayNumber <= ExpiryWarningDays)
            {
                // Low beats expiring-soon: both need an order, and the count is the more
                // urgent of the two because running out stops treatment today.
                return item.IsBelowReorderLevel ? StockFlag.Low : StockFlag.ExpiringSoon;
            }
        }

        return item.IsBelowReorderLevel ? StockFlag.Low : StockFlag.Ok;
    }

    private async Task<string> NextOrderNumberAsync(CancellationToken ct)
    {
        var orders = await _orders.ListAsync(ct: ct).ConfigureAwait(false);

        var highest = orders
            .Select(order => int.TryParse(order.OrderNumber?.TrimStart('P', 'O', '-'),
                out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"PO-{Math.Max(highest, 1000) + 1}";
    }

    private static (DateTime Start, DateTime End) LocalDayToUtc(DateOnly day)
    {
        var start = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
        return (start.ToUniversalTime(), start.AddDays(1).ToUniversalTime());
    }
}
