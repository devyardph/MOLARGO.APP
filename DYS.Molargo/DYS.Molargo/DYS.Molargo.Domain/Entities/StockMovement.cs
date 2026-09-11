using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One change to the quantity of a stock item. Append-only: a correction is another
/// movement, never an edit, so the running total always has an audit trail behind it.
/// </summary>
public sealed class StockMovement : EntityBase
{
    public Guid StockItemId { get; set; }

    public StockMovementKind Kind { get; set; }

    /// <summary>
    /// Signed change in units — negative for consumption, wastage and returns. Signed
    /// rather than a magnitude plus the kind: summing the column then reconciles against
    /// <c>StockItem.QuantityOnHand</c> directly, with no per-kind sign table to get wrong.
    /// </summary>
    public decimal QuantityChange { get; set; }

    /// <summary>
    /// Quantity on hand after this movement. Stored so a stocktake discrepancy can be
    /// traced to the movement where the drift began.
    /// </summary>
    public decimal BalanceAfter { get; set; }

    public DateTime OccurredUtc { get; set; }

    /// <summary>Batch or lot number, required where the item is batch-tracked.</summary>
    public string? BatchNumber { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    /// <summary>Unit cost at receipt, for stock valuation.</summary>
    public decimal? UnitCost { get; set; }

    /// <summary>
    /// The patient the material was used on, where the item is batch-tracked. This is the
    /// link a product recall follows.
    /// </summary>
    public Guid? PatientId { get; set; }

    public Guid? AppointmentId { get; set; }

    public Guid? RecordedByProviderId { get; set; }

    /// <summary>Purchase order or invoice reference, for a receipt.</summary>
    public string? Reference { get; set; }

    public string? Notes { get; set; }
}
