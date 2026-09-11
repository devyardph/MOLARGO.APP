namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// Why stock on hand changed. Every change is a movement rather than an edit to a running
/// total, so the level is always explainable — an unexplained count is how shrinkage hides.
/// </summary>
public enum StockMovementKind
{
    /// <summary>The opening count when an item was first added.</summary>
    OpeningBalance = 0,

    /// <summary>Received against a purchase order.</summary>
    Received = 1,

    /// <summary>Used in the surgery.</summary>
    Consumed = 2,

    /// <summary>A stocktake correction. Positive or negative.</summary>
    Adjustment = 3,

    /// <summary>Discarded — expired, contaminated or damaged.</summary>
    Wastage = 4,

    /// <summary>Sent to another practice location.</summary>
    TransferOut = 5,

    /// <summary>Received from another practice location.</summary>
    TransferIn = 6,

    /// <summary>Returned to the supplier.</summary>
    ReturnedToSupplier = 7,
}
