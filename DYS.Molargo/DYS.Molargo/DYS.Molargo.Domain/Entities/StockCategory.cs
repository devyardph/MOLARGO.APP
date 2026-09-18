namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One grouping a practice files its stock under.
/// </summary>
/// <remarks>
/// <para>
/// A row rather than the compiled-in list this replaced. A fixed list is right about the
/// problem — the stock table groups by category, and two spellings of "consumables" split
/// one group into two that each look half-stocked — but wrong about the fix. It made the
/// vendor the authority on how a practice files its own shelves, and an orthodontic
/// practice with no use for four of the eight had no way to say so.
/// </para>
/// <para>
/// The name is the link. <see cref="StockItem.Category"/> holds the text, not an id, which
/// is what let categories exist before this table did and what lets an item survive its
/// category being removed. The cost is that renaming one has to carry every item using it,
/// and that is done in the one service method that owns the rename rather than left to a
/// caller to remember.
/// </para>
/// </remarks>
public sealed class StockCategory : EntityBase
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Where it sits among the chips.
    /// </summary>
    /// <remarks>
    /// Explicit rather than alphabetical, because the order people want is by how often
    /// they reach for it: consumables first, orthodontic last in a practice that barely
    /// stocks it.
    /// </remarks>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// False where the category is retired.
    /// </summary>
    /// <remarks>
    /// Retired rather than deleted, and only ever while nothing uses it. An item whose
    /// category vanished would sort into a group with no name, which is how stock quietly
    /// stops being findable.
    /// </remarks>
    public bool IsActive { get; set; } = true;
}
