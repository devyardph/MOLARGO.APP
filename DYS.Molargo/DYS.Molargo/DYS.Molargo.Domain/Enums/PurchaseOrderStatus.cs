namespace DYS.Molargo.Domain.Enums;

/// <summary>Where a purchase order sits between being built and being put away.</summary>
public enum PurchaseOrderStatus
{
    /// <summary>Being assembled. Lines can still be added and removed.</summary>
    Draft = 0,

    /// <summary>Sent to the supplier. Nothing has arrived.</summary>
    Ordered = 1,

    /// <summary>Some lines have come in, some have not.</summary>
    PartiallyReceived = 2,

    /// <summary>Everything ordered has arrived and been put on the shelf.</summary>
    Received = 3,

    Cancelled = 4,
}
