namespace DYS.Molargo.Domain;

/// <summary>
/// One page of a list query. Carries the total so a view model can render "1–10 of 25"
/// and size its pager without a second round trip.
/// </summary>
/// <param name="Items">The rows on this page.</param>
/// <param name="TotalCount">Rows matching the query across every page.</param>
/// <param name="Page">Zero-based index of the page actually returned, which may differ
/// from the one requested — the repository clamps a stale index rather than answering
/// with an empty screen.</param>
/// <param name="PageSize">Rows per page.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public static PagedResult<T> Empty(int pageSize) =>
        new(Array.Empty<T>(), TotalCount: 0, Page: 0, PageSize: pageSize);

    public int PageCount => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
