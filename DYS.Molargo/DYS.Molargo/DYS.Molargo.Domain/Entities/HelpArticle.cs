namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One article in the help knowledge base.
/// </summary>
/// <remarks>
/// <para>
/// The vendor's row, like a plan or an SMS gateway: it carries the platform tenant's id and
/// every practice reads the same one. A copy per clinic would go stale the first time a
/// screen moved, and would mean an edit had to be made once per customer.
/// </para>
/// <para>
/// A table rather than text compiled into the app, so the manual can be corrected without a
/// release — which matters most exactly when it is wrong. The shipped articles are the seed
/// for a new database, not the source of truth afterwards.
/// </para>
/// </remarks>
public sealed class HelpArticle : EntityBase
{
    /// <summary>
    /// The stable identifier, used by the URL.
    /// </summary>
    /// <remarks>
    /// Kept even when the title is reworded, so a link somebody bookmarked or pasted into a
    /// team chat does not rot. Unique across the platform, because it is the whole address.
    /// </remarks>
    public string Slug { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>One line under the title in the list.</summary>
    public string Summary { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Words somebody would search for that are not in the title or body.
    /// </summary>
    /// <remarks>
    /// The word a practice uses for a thing this app calls something else — "no-show" finds
    /// the article that only ever says "failed to attend". Without these, help is findable
    /// only by people who already know its vocabulary, which is the opposite of the point.
    /// </remarks>
    public string Keywords { get; set; } = string.Empty;

    /// <summary>Order within the category, lowest first.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// False while it is being written.
    /// </summary>
    /// <remarks>
    /// Unpublished articles are invisible to practices and visible to the vendor. An
    /// author needs somewhere to leave a half-written article that is not in front of every
    /// customer, and deleting it to hide it would lose the work.
    /// </remarks>
    public bool IsPublished { get; set; } = true;

    /// <summary>Whether a single search word appears anywhere worth matching.</summary>
    public bool Matches(string word) =>
        Has(Title, word) || Has(Summary, word) || Has(Body, word)
        || Has(Category, word) || Has(Keywords, word);

    private static bool Has(string? value, string word) =>
        value is { Length: > 0 }
        && value.Contains(word, StringComparison.CurrentCultureIgnoreCase);
}
