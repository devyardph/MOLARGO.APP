using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// An order placed with one supplier.
/// </summary>
/// <remarks>
/// One order per supplier, which is why the reorder list groups by supplier rather than
/// producing a single basket: stock arrives in a box from a company, and an order spanning
/// three suppliers cannot be sent to any of them.
/// </remarks>
public sealed class PurchaseOrder : EntityBase
{
    public Guid PracticeLocationId { get; set; }

    public Guid SupplierId { get; set; }

    /// <summary>Assigned when the order is sent, not when the draft is started.</summary>
    public string? OrderNumber { get; set; }

    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;

    public DateTime? OrderedUtc { get; set; }

    /// <summary>What the supplier promised, for chasing a late delivery.</summary>
    public DateOnly? ExpectedOn { get; set; }

    public DateTime? ReceivedUtc { get; set; }

    public Guid? RaisedByProviderId { get; set; }

    public string? Notes { get; set; }
}
