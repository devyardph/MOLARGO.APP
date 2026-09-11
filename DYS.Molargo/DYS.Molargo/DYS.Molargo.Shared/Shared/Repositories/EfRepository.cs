using System.Linq.Expressions;
using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Shared.Data;
using DYS.Molargo.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace DYS.Molargo.Shared.Repositories;

/// <summary>
/// The EF Core implementation of <see cref="IRepository{TEntity}"/>, over local SQLite.
/// </summary>
/// <remarks>
/// A fresh <c>DbContext</c> per call, from <see cref="MolargoDatabase"/>. A context is not
/// thread-safe and is cheap to create, and a repository registered once for the app's
/// lifetime cannot safely hold one — two screens loading at the same time would share it.
/// </remarks>
/// <typeparam name="TEntity">The entity type this instance serves.</typeparam>
public class EfRepository<TEntity> : IRepository<TEntity>
    where TEntity : EntityBase
{
    private readonly MolargoDatabase _database;
    private readonly IClock _clock;
    private readonly ITenantContext _tenant;

    public EfRepository(MolargoDatabase database, IClock clock, ITenantContext tenant)
    {
        _database = database;
        _clock = clock;
        _tenant = tenant;
    }

    public async Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        return await Query(db)
            .FirstOrDefaultAsync(entity => entity.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<TEntity?> FindAsync(
        Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        return await Query(db).FirstOrDefaultAsync(predicate, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TEntity>> ListAsync(
        Expression<Func<TEntity, bool>>? predicate = null, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var query = Query(db);
        if (predicate is not null) query = query.Where(predicate);

        return await query.ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<PagedResult<TEntity>> GetPageAsync(
        int page,
        int pageSize,
        Expression<Func<TEntity, object>> orderBy,
        bool descending = false,
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var (ordered, total, resolvedPage, resolvedSize) =
            await PrepareAsync(db, page, pageSize, orderBy, descending, predicate, ct)
                .ConfigureAwait(false);

        var items = await ordered
            .Skip(resolvedPage * resolvedSize)
            .Take(resolvedSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<TEntity>(items, total, resolvedPage, resolvedSize);
    }

    public async Task<PagedResult<TResult>> GetPageAsync<TResult>(
        int page,
        int pageSize,
        Expression<Func<TEntity, object>> orderBy,
        Expression<Func<TEntity, TResult>> selector,
        bool descending = false,
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var (ordered, total, resolvedPage, resolvedSize) =
            await PrepareAsync(db, page, pageSize, orderBy, descending, predicate, ct)
                .ConfigureAwait(false);

        // Select after Skip/Take, so the projection is part of the SQL and only the
        // page's columns are read. Projecting first would still work but reads the whole
        // ordered set into the shape before paging it.
        var items = await ordered
            .Skip(resolvedPage * resolvedSize)
            .Take(resolvedSize)
            .Select(selector)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<TResult>(items, total, resolvedPage, resolvedSize);
    }

    public async Task<int> CountAsync(
        Expression<Func<TEntity, bool>>? predicate = null, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var query = Query(db);
        if (predicate is not null) query = query.Where(predicate);

        return await query.CountAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync(
        Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        return await Query(db).AnyAsync(predicate, ct).ConfigureAwait(false);
    }

    public async Task<Guid> SaveAsync(TEntity entity, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();

        // Probed before stamping. Deciding insert-versus-update from CreatedUtc would not
        // work, because Stamp fills it in — so the check has to be the id against the
        // table, and it has to happen first.
        var exists = await db.Set<TEntity>()
            .AsNoTracking()
            .AnyAsync(existing => existing.Id == entity.Id, ct)
            .ConfigureAwait(false);

        Stamp(entity);

        // Update rather than Add for an existing row: the caller handed us a detached
        // entity it built itself, so there is nothing tracked to apply values onto.
        if (exists) db.Set<TEntity>().Update(entity);
        else db.Set<TEntity>().Add(entity);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return entity.Id;
    }

    public async Task SaveRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var list = entities as IList<TEntity> ?? entities.ToList();
        if (list.Count == 0) return;

        foreach (var entity in list)
        {
            if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();
            Stamp(entity);
        }

        var ids = list.Select(entity => entity.Id).ToList();
        var existingIds = await db.Set<TEntity>()
            .AsNoTracking()
            .Where(existing => ids.Contains(existing.Id))
            .Select(existing => existing.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var existing = existingIds.ToHashSet();

        foreach (var entity in list)
        {
            if (existing.Contains(entity.Id)) db.Set<TEntity>().Update(entity);
            else db.Set<TEntity>().Add(entity);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        // ExecuteUpdate issues a single UPDATE without loading the row, and returns the
        // number affected — which also answers whether the row was there.
        var affected = await db.Set<TEntity>()
            .Where(entity => entity.Id == id && !entity.IsDeleted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(entity => entity.IsDeleted, true)
                .SetProperty(entity => entity.DeletedUtc, _clock.UtcNow)
                .SetProperty(entity => entity.UpdatedUtc, _clock.UtcNow), ct)
            .ConfigureAwait(false);

        return affected > 0;
    }

    public async Task<bool> RestoreAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var affected = await db.Set<TEntity>()
            .Where(entity => entity.Id == id && entity.IsDeleted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(entity => entity.IsDeleted, false)
                .SetProperty(entity => entity.DeletedUtc, (DateTime?)null)
                .SetProperty(entity => entity.UpdatedUtc, _clock.UtcNow), ct)
            .ConfigureAwait(false);

        return affected > 0;
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// The base query every read starts from: no change tracking, no deleted rows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>AsNoTracking</c> throughout. These rows are read to render a screen and are
    /// never edited in place — a save takes a detached entity — so the change tracker is
    /// pure overhead.
    /// </para>
    /// <para>
    /// The soft-delete filter is applied here rather than as a global query filter on the
    /// model. A global filter is silently applied to every query including the ones inside
    /// <c>ExecuteUpdate</c> and to navigation loads, and it then takes an
    /// <c>IgnoreQueryFilters</c> to see a deleted row at all — which is easy to forget in
    /// exactly the admin screens that need it. One visible <c>Where</c> is clearer about
    /// what it does.
    /// </para>
    /// </remarks>
    protected static IQueryable<TEntity> Query(MolargoDbContext db) =>
        db.Set<TEntity>().AsNoTracking().Where(entity => !entity.IsDeleted);

    /// <summary>
    /// Resolves the page index against the real total and returns the ordered query.
    /// Shared by both <c>GetPageAsync</c> overloads so the clamping rule cannot drift
    /// between them.
    /// </summary>
    private static async Task<(IQueryable<TEntity> Ordered, int Total, int Page, int PageSize)> PrepareAsync(
        MolargoDbContext db,
        int page,
        int pageSize,
        Expression<Func<TEntity, object>> orderBy,
        bool descending,
        Expression<Func<TEntity, bool>>? predicate,
        CancellationToken ct)
    {
        var query = Query(db);
        if (predicate is not null) query = query.Where(predicate);

        var total = await query.CountAsync(ct).ConfigureAwait(false);

        var resolvedSize = Math.Max(1, pageSize);
        var pageCount = total == 0 ? 1 : (int)Math.Ceiling(total / (double)resolvedSize);

        // Clamp rather than trust the caller. A stale page index — the last row of a
        // filter was just archived — would otherwise return an empty screen that reads as
        // "no patients" instead of "you are past the end".
        var resolvedPage = Math.Clamp(page, 0, pageCount - 1);

        var ordered = descending ? query.OrderByDescending(orderBy) : query.OrderBy(orderBy);

        return (ordered, total, resolvedPage, resolvedSize);
    }

    /// <summary>
    /// Sets the audit dates. Done here rather than left to callers: a screen that forgets
    /// leaves a row looking untouched, and <c>UpdatedUtc</c> is what the sync layer will
    /// eventually follow.
    /// </summary>
    private void Stamp(TEntity entity)
    {
        var now = _clock.UtcNow;

        // Only fill CreatedUtc when it is genuinely unset, so re-saving an existing row
        // does not restate when it was created.
        if (entity.CreatedUtc == default) entity.CreatedUtc = now;

        entity.UpdatedUtc = now;

        // The clinic, filled in the same way and for the same reason: a screen that
        // forgets would write a row nobody can read back, because the tenant filter would
        // exclude it immediately.
        //
        // Only when unset. An existing row keeps the tenant it was created under — this
        // must never quietly move a record between clinics, which is what re-stamping on
        // every save would do the moment the current tenant differed.
        if (entity.TenantId == Guid.Empty) entity.TenantId = _tenant.TenantId;
    }
}
