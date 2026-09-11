namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// Base for every persisted entity: identity, audit stamps and a soft delete.
/// </summary>
/// <remarks>
/// <para>
/// The app is offline-only for now — SQLite is the only store — but the id is still
/// client-assigned rather than a database identity. A record created on a tablet has its
/// final identity immediately, which is what will let the same rows be reconciled with a
/// server later without renumbering anything.
/// </para>
/// <para>
/// Deletion is soft throughout. Clinical records carry a statutory retention period, so
/// nothing in this domain is ever really removed; every query filters
/// <see cref="IsDeleted"/> instead.
/// </para>
/// </remarks>
public abstract class EntityBase
{
    public Guid Id { get; set; }

    /// <summary>
    /// The clinic this row belongs to — the business that subscribes to the app.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not the site. A clinic may run several <see cref="PracticeLocation"/>s and the
    /// patient record follows the patient between them, so tenancy sits one level above
    /// location: <c>Tenant</c> owns locations, locations scope a day's work.
    /// </para>
    /// <para>
    /// On every entity rather than only the roots. Reaching a child's tenant through its
    /// parent means a query that forgets the join returns another clinic's rows, and the
    /// only safe filter is one that needs no join to apply.
    /// </para>
    /// <para>
    /// Stamped by the repository from the current tenant, never by callers — the same
    /// reason <see cref="UpdatedUtc"/> is. A screen that forgets would write a row nobody
    /// can see, or worse, one everybody can.
    /// </para>
    /// </remarks>
    public Guid TenantId { get; set; }

    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Last modification, in UTC. Stamped by the repository rather than by callers, so a
    /// screen that forgets cannot leave a row looking untouched.
    /// </summary>
    public DateTime UpdatedUtc { get; set; }

    /// <summary>
    /// Soft-delete flag. Kept as a bool beside <see cref="DeletedUtc"/> rather than
    /// deriving from it: a bool is what the query filter tests on every read, and an
    /// indexed bool is cheaper than a null check on a date.
    /// </summary>
    public bool IsDeleted { get; set; }

    public DateTime? DeletedUtc { get; set; }
}
