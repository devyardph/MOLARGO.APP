using System.Linq.Expressions;
using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;

namespace DYS.Molargo.Shared.Repositories;

/// <summary>
/// Data access for one entity type, in one generic contract.
/// </summary>
/// <remarks>
/// <para>
/// One generic repository rather than a hand-written interface per entity. With 30-odd
/// entities in this domain and a single SQLite store behind all of them, per-entity
/// contracts would be thirty near-identical files — and every one of them a place for the
/// paging, soft-delete and audit-stamp rules to drift apart.
/// </para>
/// <para>
/// The cost is that a caller expresses filters as expressions rather than in domain
/// language. Where a filter is non-obvious, or reused across screens, put it in a named
/// method on a feature-specific service (see <c>PatientService</c>) and let that call
/// through to here — the generic repository handles the plumbing, the service holds the
/// vocabulary.
/// </para>
/// <para>
/// Every method here excludes soft-deleted rows. Nothing in this domain is hard-deleted,
/// so "all patients" always means "all patients not deleted"; a caller that needs the
/// deleted ones is doing something unusual enough to warrant its own query.
/// </para>
/// </remarks>
/// <typeparam name="TEntity">The entity type this instance serves.</typeparam>
public interface IRepository<TEntity>
    where TEntity : EntityBase
{
    /// <summary>The row with this id, or null if there is none or it is deleted.</summary>
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>The first row matching <paramref name="predicate"/>, or null.</summary>
    Task<TEntity?> FindAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken ct = default);

    /// <summary>
    /// Every matching row. Use only where the result set is naturally bounded — a
    /// practice's locations, a patient's alerts. For anything that grows with the patient
    /// base, use <see cref="GetPageAsync"/>.
    /// </summary>
    Task<IReadOnlyList<TEntity>> ListAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken ct = default);

    /// <summary>
    /// One page of rows, with the total alongside so a pager can be sized without a
    /// second round trip.
    /// </summary>
    /// <param name="page">Zero-based. Clamped to the available range rather than trusted.</param>
    /// <param name="orderBy">
    /// Required, not optional. A paged query without an explicit order has no defined
    /// order at all, so a row can appear on two pages and never on a third — and SQLite
    /// will happily return a plausible-looking result that hides it.
    /// </param>
    Task<PagedResult<TEntity>> GetPageAsync(
        int page,
        int pageSize,
        Expression<Func<TEntity, object>> orderBy,
        bool descending = false,
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken ct = default);

    /// <summary>
    /// One page, projected to <typeparamref name="TResult"/> in the query rather than
    /// after it — so a list screen reads only the columns it shows.
    /// </summary>
    Task<PagedResult<TResult>> GetPageAsync<TResult>(
        int page,
        int pageSize,
        Expression<Func<TEntity, object>> orderBy,
        Expression<Func<TEntity, TResult>> selector,
        bool descending = false,
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken ct = default);

    Task<int> CountAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates, stamping the audit dates, and returns the id — which the caller
    /// may not have known for a row it just created.
    /// </summary>
    Task<Guid> SaveAsync(TEntity entity, CancellationToken ct = default);

    /// <summary>
    /// Saves many in one transaction. Not a loop over <see cref="SaveAsync"/>: one
    /// <c>SaveChanges</c> per batch is the difference between a seeded database in a
    /// second and one in a minute.
    /// </summary>
    Task SaveRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);

    /// <summary>
    /// Soft-deletes the row: sets the flag and the timestamp, leaving the data in place.
    /// Clinical records carry a statutory retention period, so this is as close to
    /// deletion as this domain gets.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Reverses a soft delete.</summary>
    Task<bool> RestoreAsync(Guid id, CancellationToken ct = default);
}
