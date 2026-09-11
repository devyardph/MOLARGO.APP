namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A consumable or material the practice holds.
/// </summary>
/// <remarks>
/// <see cref="QuantityOnHand"/> is maintained here and explained by the
/// <see cref="StockMovement"/> rows behind it. Both, rather than one: the running total is
/// what every list and low-stock check reads, and the movements are what make an
/// unexpected total explainable instead of merely wrong.
/// </remarks>
public sealed class StockItem : EntityBase
{
    public Guid PracticeLocationId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>The practice's own code, and what is scanned or typed at the bench.</summary>
    public string? Sku { get; set; }

    public string? Category { get; set; }

    public Guid? SupplierId { get; set; }

    /// <summary>The supplier's catalogue number, for placing the order.</summary>
    public string? SupplierItemCode { get; set; }

    /// <summary>What one unit is — "box of 100", "each", "50ml bottle".</summary>
    public string? UnitOfMeasure { get; set; }

    public decimal QuantityOnHand { get; set; }

    /// <summary>
    /// Level at which the item appears on the reorder list. Held per item because a
    /// practice-wide threshold either over-orders gloves or runs out of composite.
    /// </summary>
    public decimal ReorderLevel { get; set; }

    /// <summary>How many to order when it drops.</summary>
    public decimal ReorderQuantity { get; set; }

    public decimal? UnitCost { get; set; }

    /// <summary>
    /// Earliest expiry among the stock held. Denormalised so the expiry worklist is one
    /// query on this table rather than a scan of every batch.
    /// </summary>
    public DateOnly? EarliestExpiry { get; set; }

    /// <summary>
    /// True where the item has to be tracked by batch — anything implanted or that a
    /// recall could reach, which has to be traceable to the patient it was used on.
    /// </summary>
    public bool RequiresBatchTracking { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsBelowReorderLevel => QuantityOnHand <= ReorderLevel;
}
