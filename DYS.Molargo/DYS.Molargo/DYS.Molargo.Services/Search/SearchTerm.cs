namespace DYS.Molargo.Services.Search;

/// <summary>
/// What a search box's contents become before they reach a query.
/// </summary>
/// <remarks>
/// <para>
/// One place, because this has now been got wrong twice for opposite reasons and the
/// history is the point of the file.
/// </para>
/// <para>
/// Originally every search used <c>string.Contains</c>, which SQLite translated to
/// <c>instr()</c> — case-sensitive, so "yuen" found nobody and "Yuen" found Margaret. The
/// fix was <c>EF.Functions.Like</c>, because SQLite's <c>LIKE</c> folds case for ASCII.
/// Then the app moved to PostgreSQL, where <c>LIKE</c> is case-<em>sensitive</em> and the
/// same bug came back in the same words, in four places at once.
/// </para>
/// <para>
/// So neither. The rule is now <c>column.ToLower().Contains(needle)</c> with the needle
/// lowered here: EF translates <c>ToLower()</c> to SQL <c>lower()</c> on every provider,
/// which makes the behaviour the provider's business rather than something each query has
/// to know. <c>ILIKE</c> would be shorter and is PostgreSQL-only — it would put the
/// provider back inside the query, which is the thing that broke.
/// </para>
/// <para>
/// It also drops the wildcard stripping <c>LIKE</c> needed. <c>Contains</c> has no
/// wildcards, so a "%" typed into the box is now matched as a per-cent sign instead of
/// being silently deleted from the term.
/// </para>
/// <para>
/// What this is not for: looking a record up by a key that happens to be text — a clinic
/// code, an email used as an identity. Those are normalised when they are stored and
/// compared with <c>==</c>, and folding case at read time would hide a record saved wrongly.
/// </para>
/// </remarks>
public static class SearchTerm
{
    /// <summary>
    /// A typed term as a query should hold it, or null when there is nothing to match.
    /// </summary>
    /// <remarks>
    /// Null rather than an empty string, so a predicate can test <c>needle == null</c> and
    /// skip the comparison entirely. An empty needle passed into <c>Contains</c> matches
    /// every row, which reads as a filter that has stopped working.
    ///
    /// <c>ToLowerInvariant</c> here and <c>ToLower()</c> in the expression: the second is
    /// not a real call, it is what EF turns into SQL <c>lower()</c>, and
    /// <c>ToLowerInvariant</c> has no translation at all — a query using it throws at
    /// runtime rather than failing to compile.
    /// </remarks>
    public static string? Normalise(string? term) =>
        string.IsNullOrWhiteSpace(term) ? null : term.Trim().ToLowerInvariant();
}
