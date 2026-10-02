using DYS.Molargo.Api.Contracts;
using DYS.Molargo.Data;
using DYS.Molargo.Domain.Entities;

namespace DYS.Molargo.Api.Endpoints;

/// <summary>Stock, suppliers, orders, lab cases and sterilisation.</summary>
/// <inheritdoc cref="PatientsEndpoint" path="/remarks"/>
public sealed class InventoryEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/inventory").WithTags("Inventory");

        group.MapGet("/stock", StockAsync)
            .WithSummary("Stock on hand. Filter to what is below its reorder level.");

        group.MapGet("/stock/{id:guid}/movements", MovementsAsync)
            .WithSummary("What has happened to one item, newest first.");

        group.MapGet("/suppliers", SuppliersAsync)
            .WithSummary("Suppliers and laboratories.");

        group.MapGet("/orders", OrdersAsync)
            .WithSummary("Purchase orders. Recorded here, never sent — see the README.");

        group.MapGet("/lab-cases", LabCasesAsync)
            .WithSummary("Lab cases out and back.");

        group.MapGet("/sterilisation", SterilisationAsync)
            .WithSummary("Sterilisation cycles, newest first.");
    }

    /// <summary>GET /inventory/stock</summary>
    private static async Task<IResult> StockAsync(
        string? search,
        bool? belowReorderOnly,
        int? page,
        IRepository<StockItem> stock,
        CancellationToken ct)
    {
        var rows = await stock.ListAsync(item => item.IsActive, ct);

        var term = search?.Trim();

        if (term is { Length: > 0 })
        {
            rows = rows
                .Where(item => item.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (item.Sku ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (item.Category ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (belowReorderOnly == true)
        {
            rows = rows.Where(item => item.QuantityOnHand <= item.ReorderLevel).ToList();
        }

        var ordered = rows
            // Short first. The list is opened to find out what needs ordering, and
            // alphabetical order buries exactly that.
            .OrderByDescending(item => item.QuantityOnHand <= item.ReorderLevel)
            .ThenBy(item => item.Name)
            .ToList();

        return Results.Ok(ApiDefaults.Page(ordered, page, item => new StockItemDto(
            item.Id,
            item.Name,
            item.Sku,
            item.Category,
            item.SupplierId,
            item.UnitOfMeasure,
            item.QuantityOnHand,
            item.ReorderLevel,
            item.ReorderQuantity,
            item.UnitCost,
            item.EarliestExpiry,
            item.QuantityOnHand <= item.ReorderLevel)));
    }

    /// <summary>GET /inventory/stock/{id}/movements</summary>
    private static async Task<IResult> MovementsAsync(
        Guid id, int? page, IRepository<StockMovement> movements, CancellationToken ct)
    {
        var rows = await movements.ListAsync(movement => movement.StockItemId == id, ct);

        var ordered = rows.OrderByDescending(movement => movement.OccurredUtc).ToList();

        return Results.Ok(ApiDefaults.Page(ordered, page, movement => new StockMovementDto(
            movement.Id,
            movement.Kind.ToString(),
            movement.QuantityChange,
            movement.BalanceAfter,
            movement.OccurredUtc,
            movement.BatchNumber,
            movement.ExpiryDate,
            movement.PatientId,
            movement.Reference)));
    }

    /// <summary>GET /inventory/suppliers</summary>
    private static async Task<IResult> SuppliersAsync(
        bool? includeInactive, IRepository<Supplier> suppliers, CancellationToken ct)
    {
        var rows = await suppliers.ListAsync(
            supplier => includeInactive == true || supplier.IsActive, ct);

        return Results.Ok(rows
            .OrderBy(supplier => supplier.Name)
            .Select(supplier => new SupplierDto(
                supplier.Id,
                supplier.Name,
                supplier.AccountNumber,
                supplier.ContactName,
                supplier.Phone,
                supplier.Email,
                supplier.LeadTimeDays,
                supplier.IsLaboratory,
                supplier.IsActive))
            .ToList());
    }

    /// <summary>GET /inventory/orders</summary>
    private static async Task<IResult> OrdersAsync(
        int? page, IRepository<PurchaseOrder> orders, CancellationToken ct)
    {
        var rows = await orders.ListAsync(null, ct);

        var ordered = rows
            .OrderByDescending(order => order.OrderedUtc ?? order.CreatedUtc)
            .ToList();

        return Results.Ok(ApiDefaults.Page(ordered, page, order => new PurchaseOrderDto(
            order.Id,
            order.OrderNumber,
            order.SupplierId,
            order.Status.ToString(),
            order.OrderedUtc,
            order.ExpectedOn,
            order.ReceivedUtc)));
    }

    /// <summary>GET /inventory/lab-cases</summary>
    private static async Task<IResult> LabCasesAsync(
        Guid? patientId, int? page, IRepository<LabCase> cases, CancellationToken ct)
    {
        var rows = await cases.ListAsync(
            labCase => patientId == null || labCase.PatientId == patientId, ct);

        var ordered = rows
            // Due soonest first, and undated last. The list answers "what is coming back
            // and when", which a creation-order list does not.
            .OrderBy(labCase => labCase.DueOn ?? DateOnly.MaxValue)
            .ToList();

        return Results.Ok(ApiDefaults.Page(ordered, page, labCase => new LabCaseDto(
            labCase.Id,
            labCase.PatientId,
            labCase.SupplierId,
            labCase.Status.ToString(),
            labCase.Description,
            labCase.ToothNumber,
            labCase.LabReference,
            labCase.SentUtc,
            labCase.DueOn,
            labCase.ReceivedUtc,
            labCase.FitAppointmentId,
            labCase.LabFee)));
    }

    /// <summary>GET /inventory/sterilisation</summary>
    private static async Task<IResult> SterilisationAsync(
        int? page, IRepository<SterilisationCycle> cycles, CancellationToken ct)
    {
        var rows = await cycles.ListAsync(null, ct);

        var ordered = rows.OrderByDescending(cycle => cycle.StartedUtc).ToList();

        return Results.Ok(ApiDefaults.Page(ordered, page, cycle => new SterilisationCycleDto(
            cycle.Id,
            cycle.SterilisorName,
            cycle.CycleNumber,
            cycle.StartedUtc,
            cycle.CompletedUtc,
            cycle.Result.ToString(),
            cycle.CycleType,
            cycle.ChemicalIndicatorPassed,
            cycle.BiologicalIndicatorPassed,
            cycle.LoadContents,

            // Released is the fact that matters for a recall: an unreleased load is one
            // whose instruments should not have been used.
            cycle.ReleasedUtc is not null,
            cycle.ReleasedUtc,
            cycle.FailureNotes)));
    }
}

public sealed record StockItemDto(
    Guid Id,
    string Name,
    string? Sku,
    string? Category,
    Guid? SupplierId,
    string? UnitOfMeasure,
    decimal QuantityOnHand,
    decimal ReorderLevel,
    decimal ReorderQuantity,
    decimal? UnitCost,
    DateOnly? EarliestExpiry,
    bool IsBelowReorderLevel);

public sealed record StockMovementDto(
    Guid Id,
    string Kind,
    decimal QuantityChange,
    decimal BalanceAfter,
    DateTime OccurredUtc,
    string? BatchNumber,
    DateOnly? ExpiryDate,
    Guid? PatientId,
    string? Reference);

public sealed record SupplierDto(
    Guid Id,
    string Name,
    string? AccountNumber,
    string? ContactName,
    string? Phone,
    string? Email,
    int? LeadTimeDays,
    bool IsLaboratory,
    bool IsActive);

public sealed record PurchaseOrderDto(
    Guid Id,
    string? OrderNumber,
    Guid SupplierId,
    string Status,
    DateTime? OrderedUtc,
    DateOnly? ExpectedOn,
    DateTime? ReceivedUtc);

public sealed record LabCaseDto(
    Guid Id,
    Guid PatientId,
    Guid? SupplierId,
    string Status,
    string Description,
    string? ToothNumber,
    string? LabReference,
    DateTime? SentUtc,
    DateOnly? DueOn,
    DateTime? ReceivedUtc,
    Guid? FitAppointmentId,
    decimal? LabFee);

public sealed record SterilisationCycleDto(
    Guid Id,
    string SterilisorName,
    int CycleNumber,
    DateTime StartedUtc,
    DateTime? CompletedUtc,
    string Result,
    string? CycleType,
    bool ChemicalIndicatorPassed,
    bool? BiologicalIndicatorPassed,
    string? LoadContents,
    bool IsReleased,
    DateTime? ReleasedUtc,
    string? FailureNotes);
