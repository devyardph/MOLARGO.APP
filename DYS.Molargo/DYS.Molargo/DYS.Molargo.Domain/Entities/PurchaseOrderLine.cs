namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One item on a purchase order.
/// </summary>
/// <remarks>
/// Carries what was ordered and what has actually turned up, separately. A delivery short
/// of the order is the normal case rather than an exception, and a line that only records
/// the ordered figure cannot tell anyone what is still owed.
/// </remarks>
public sealed class PurchaseOrderLine : EntityBase
{
    public Guid PurchaseOrderId { get; set; }

    public Guid StockItemId { get; set; }

    /// <summary>Copied at order time, so the line still reads correctly if the item is renamed.</summary>
    public string Description { get; set; } = string.Empty;

    public decimal QuantityOrdered { get; set; }

    public decimal QuantityReceived { get; set; }

    public decimal? UnitCost { get; set; }

    /// <summary>Recorded on receipt, and carried onto the stock movement.</summary>
    public string? BatchNumber { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    public decimal Outstanding => QuantityOrdered - QuantityReceived;

    public bool IsFullyReceived => Outstanding <= 0m;

    public decimal LineTotal => QuantityOrdered * (UnitCost ?? 0m);
}
