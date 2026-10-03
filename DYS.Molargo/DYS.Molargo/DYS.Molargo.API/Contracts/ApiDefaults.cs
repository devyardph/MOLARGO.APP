using DYS.Molargo.Domain;

namespace DYS.Molargo.Api.Contracts;

public static class ApiDefaults
{
    /// <summary>
    /// Rows a page.
    /// </summary>
    /// <remarks>
    /// Seventeen, the same as every list in the app. One number across the device and the
    /// server means a client paging through an API result and a person paging through the
    /// same list on screen are looking at the same rows.
    /// </remarks>
    public const int PageSize = 17;

    /// <summary>
    /// One page of an already-ordered list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For the reads that cannot go through <c>IRepository.GetPageAsync</c> — those that
    /// join a second table for a name, or order on something best sorted in memory.
    ///
    /// Money used to be on that list: it was TEXT on the device's SQLite, where "1000"
    /// sorts below "9", so no ordering rule could hold for both stores. That store is gone
    /// and money is numeric everywhere, so money ordering can go back to the database.
    /// </para>
    /// <para>
    /// The page is clamped rather than trusted. A caller asking for page nine of a
    /// three-page result gets the last page, not an empty list that reads as "nothing
    /// here".
    /// </para>
    /// </remarks>
    /// <param name="page">
    /// Nullable because that is what makes it optional to the minimal-API binder. A
    /// plain int is a required query parameter, and every paged route here answered 400
    /// to a caller who simply asked for the first page.
    /// </param>
    public static PageDto<TOut> Page<TIn, TOut>(
        IReadOnlyList<TIn> rows, int? page, Func<TIn, TOut> map, int pageSize = PageSize)
    {
        var size = Math.Max(1, pageSize);
        var pages = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)size));
        var current = Math.Clamp(page ?? 0, 0, pages - 1);

        return new PageDto<TOut>(
            rows.Skip(current * size).Take(size).Select(map).ToList(),
            rows.Count,
            current,
            size);
    }
}
