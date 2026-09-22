using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Data;
using DYS.Molargo.Shared.Features.Platform.Services;
using DYS.Molargo.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace DYS.Molargo.Shared.Features.Help.Services;

/// <summary>One page of the knowledge base, with the total behind it.</summary>
public sealed record HelpPage(
    IReadOnlyList<HelpArticle> Rows,
    int Total,
    int Page,
    int PageSize);

/// <summary>
/// The help knowledge base: read by every practice, written by the vendor.
/// </summary>
/// <remarks>
/// <para>
/// One service for both sides, because they are the same rows read two ways. A practice
/// sees published articles and nothing else; the vendor sees everything and can change it.
/// Splitting them would be two queries over one table that could disagree about what
/// "published" means.
/// </para>
/// <para>
/// Every read bypasses the tenant filter. The rows carry the platform tenant's id, and a
/// clinic reading only its own would find an empty manual — the same arrangement plans and
/// SMS gateways use.
/// </para>
/// </remarks>
public interface IHelpService
{
    /// <summary>
    /// Articles a practice can read, searched and paged.
    /// </summary>
    /// <param name="category">Null or empty for every category.</param>
    Task<HelpPage> GetPublishedAsync(
        string? search = null,
        string? category = null,
        int page = 0,
        int pageSize = 12,
        CancellationToken ct = default);

    /// <summary>Every article including unpublished ones. Vendor only.</summary>
    Task<HelpPage> GetAllAsync(
        string? search = null,
        int page = 0,
        int pageSize = 17,
        CancellationToken ct = default);

    /// <summary>The categories in use, for the filter chips.</summary>
    Task<IReadOnlyList<string>> GetCategoriesAsync(
        bool publishedOnly = true, CancellationToken ct = default);

    Task<HelpArticle?> GetAsync(Guid articleId, CancellationToken ct = default);

    /// <summary>One by its slug, which is what a link carries.</summary>
    Task<HelpArticle?> GetBySlugAsync(string? slug, CancellationToken ct = default);

    /// <summary>Adds or updates an article. Null on success, or the refusal.</summary>
    Task<string?> SaveAsync(HelpArticle article, CancellationToken ct = default);

    Task<string?> DeleteAsync(Guid articleId, CancellationToken ct = default);
}

/// <inheritdoc cref="IHelpService"/>
public sealed class HelpService : IHelpService
{
    private readonly MolargoDatabase _database;
    private readonly ISessionService _session;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;

    public HelpService(
        MolargoDatabase database,
        ISessionService session,
        ITenantContext tenant,
        IClock clock,
        IAuditLog audit)
    {
        _database = database;
        _session = session;
        _tenant = tenant;
        _clock = clock;
        _audit = audit;
    }

    public async Task<HelpPage> GetPublishedAsync(
        string? search = null,
        string? category = null,
        int page = 0,
        int pageSize = 12,
        CancellationToken ct = default)
    {
        var rows = await ReadAsync(publishedOnly: true, ct).ConfigureAwait(false);

        if (category is { Length: > 0 })
        {
            rows = rows
                .Where(row => string.Equals(row.Category, category, StringComparison.Ordinal))
                .ToList();
        }

        return Paginate(Filter(rows, search), page, pageSize);
    }

    public async Task<HelpPage> GetAllAsync(
        string? search = null,
        int page = 0,
        int pageSize = 17,
        CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false))
        {
            return new HelpPage([], 0, 0, pageSize);
        }

        var rows = await ReadAsync(publishedOnly: false, ct).ConfigureAwait(false);

        return Paginate(Filter(rows, search), page, pageSize);
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(
        bool publishedOnly = true, CancellationToken ct = default)
    {
        var rows = await ReadAsync(publishedOnly, ct).ConfigureAwait(false);

        return rows
            .Select(row => row.Category)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public async Task<HelpArticle?> GetAsync(Guid articleId, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        return await db.HelpArticles
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.Id == articleId && !row.IsDeleted, ct)
            .ConfigureAwait(false);
    }

    public async Task<HelpArticle?> GetBySlugAsync(string? slug, CancellationToken ct = default)
    {
        var wanted = (slug ?? string.Empty).Trim();

        if (wanted.Length == 0) return null;

        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        return await db.HelpArticles
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.Slug == wanted && !row.IsDeleted, ct)
            .ConfigureAwait(false);
    }

    public async Task<string?> SaveAsync(HelpArticle article, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false))
        {
            return PlatformGuard.NotPermitted;
        }

        var title = (article.Title ?? string.Empty).Trim();

        if (title.Length == 0) return "An article needs a title.";

        var category = (article.Category ?? string.Empty).Trim();

        if (category.Length == 0)
        {
            return "Give it a category — that is what the filter chips are made from.";
        }

        var body = (article.Body ?? string.Empty).Trim();

        if (body.Length == 0) return "An article with no body is a title nobody can use.";

        var slug = Slugify(article.Slug, title);

        var clash = await db.HelpArticles
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                row => row.Slug == slug && row.Id != article.Id && !row.IsDeleted, ct)
            .ConfigureAwait(false);

        // Refused rather than made unique with a suffix. The slug is the address somebody
        // pastes into a chat, and silently turning it into "booking-2" hands them a link
        // that points somewhere they did not mean.
        if (clash is not null)
        {
            return $"\"{slug}\" is already used by \"{clash.Title}\". Give this one a "
                + "different address.";
        }

        var now = _clock.UtcNow;

        var row = article.Id == Guid.Empty
            ? null
            : await db.HelpArticles
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(entry => entry.Id == article.Id, ct)
                .ConfigureAwait(false);

        var isNew = row is null;

        row ??= new HelpArticle
        {
            Id = Guid.NewGuid(),

            // The vendor's own tenant, like a plan. Every practice reads this same row.
            TenantId = _tenant.TenantId,
            CreatedUtc = now,
        };

        row.Slug = slug;
        row.Category = category;
        row.Title = title;
        row.Summary = (article.Summary ?? string.Empty).Trim();
        row.Body = body;
        row.Keywords = (article.Keywords ?? string.Empty).Trim();
        row.DisplayOrder = article.DisplayOrder;
        row.IsPublished = article.IsPublished;
        row.UpdatedUtc = now;

        if (isNew) db.HelpArticles.Add(row);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await AuditAsync(db, row.Id,
            isNew ? AuditAction.Created : AuditAction.Updated,
            (isNew ? "Wrote" : "Edited") + $" the help article \"{title}\""
                + (row.IsPublished ? string.Empty : " (unpublished)"),
            ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string?> DeleteAsync(Guid articleId, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false))
        {
            return PlatformGuard.NotPermitted;
        }

        var row = await db.HelpArticles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(entry => entry.Id == articleId && !entry.IsDeleted, ct)
            .ConfigureAwait(false);

        if (row is null) return null;

        var title = row.Title;

        row.IsDeleted = true;
        row.DeletedUtc = _clock.UtcNow;
        row.UpdatedUtc = _clock.UtcNow;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await AuditAsync(db, articleId, AuditAction.Deleted,
            $"Removed the help article \"{title}\"", ct)
            .ConfigureAwait(false);

        return null;
    }

    // ---- helpers ---------------------------------------------------------

    private async Task<List<HelpArticle>> ReadAsync(bool publishedOnly, CancellationToken ct)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var query = db.HelpArticles
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(row => !row.IsDeleted);

        if (publishedOnly) query = query.Where(row => row.IsPublished);

        var rows = await query.ToListAsync(ct).ConfigureAwait(false);

        // Category order is the order the categories first appear, not alphabetical: the
        // list is a reading order and "Admin" is not where somebody starts.
        var categories = rows
            .OrderBy(row => row.DisplayOrder)
            .Select(row => row.Category)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return rows
            .OrderBy(row => categories.IndexOf(row.Category))
            .ThenBy(row => row.DisplayOrder)
            .ThenBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <remarks>
    /// Every word has to match something, rather than the whole phrase matching one field.
    /// "sms cost" finds the text-messages article, which mentions both but never as a
    /// phrase.
    /// </remarks>
    private static List<HelpArticle> Filter(List<HelpArticle> rows, string? search)
    {
        var terms = (search ?? string.Empty).Trim();

        if (terms.Length == 0) return rows;

        var words = terms.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return rows.Where(row => words.All(row.Matches)).ToList();
    }

    private static HelpPage Paginate(List<HelpArticle> rows, int page, int pageSize)
    {
        var size = Math.Max(1, pageSize);
        var total = rows.Count;
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)size));

        // Clamped, because a search that shrinks the results while somebody is on page
        // three would otherwise show an empty page with no way to tell it from no matches.
        var wanted = Math.Clamp(page, 0, pageCount - 1);

        return new HelpPage(rows.Skip(wanted * size).Take(size).ToList(), total, wanted, size);
    }

    /// <summary>
    /// The address, from what was typed or from the title.
    /// </summary>
    /// <remarks>
    /// Derived only when blank. An article that has been published has a slug people may
    /// have linked to, and regenerating it from a reworded title would break every one of
    /// those links — which is the whole reason the slug is separate from the title.
    /// </remarks>
    private static string Slugify(string? slug, string title)
    {
        var source = string.IsNullOrWhiteSpace(slug) ? title : slug;

        var cleaned = new string(source
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-')
            .ToArray());

        while (cleaned.Contains("--", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);
        }

        cleaned = cleaned.Trim('-');

        return cleaned.Length == 0 ? "article" : cleaned;
    }

    private Task<bool> IsSuperAdminAsync(MolargoDbContext db, CancellationToken ct) =>
        PlatformGuard.IsSuperAdminAsync(db, _session.ProviderId, ct);

    private async Task AuditAsync(
        MolargoDbContext db, Guid articleId, AuditAction action, string detail,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;

        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            Action = action,
            EntityName = nameof(HelpArticle),
            EntityId = articleId,
            ProviderId = _session.ProviderId,
            ProviderName = _session.UserDisplayName,
            OccurredUtc = now,
            CreatedUtc = now,
            UpdatedUtc = now,
            DeviceId = await _database.GetDeviceIdAsync(ct).ConfigureAwait(false),
            Detail = detail,
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
