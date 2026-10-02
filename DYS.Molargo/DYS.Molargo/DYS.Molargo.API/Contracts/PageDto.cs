namespace DYS.Molargo.Api.Contracts;

/// <summary>
/// One page of results, and the total behind it.
/// </summary>
/// <param name="Total">
/// Every match, not just this page. A caller that only knew its own page could not tell
/// somebody whether their search found one record or four hundred.
/// </param>
public sealed record PageDto<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
