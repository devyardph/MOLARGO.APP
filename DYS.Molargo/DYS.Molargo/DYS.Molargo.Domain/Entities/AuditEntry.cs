using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One recorded action against a record.
/// </summary>
/// <remarks>
/// Append-only and never soft-deleted in practice — an audit trail that can be edited is
/// not an audit trail. Reads are logged as well as writes: health records carry an
/// access-logging obligation, and "who looked at this patient" is what a privacy
/// complaint actually asks.
/// </remarks>
public sealed class AuditEntry : EntityBase
{
    public AuditAction Action { get; set; }

    /// <summary>
    /// The type acted on, as its plain name — "Patient". A string rather than an enum so
    /// a new entity needs no change here, and so an old entry stays readable after a type
    /// is renamed.
    /// </summary>
    public string EntityName { get; set; } = string.Empty;

    public Guid? EntityId { get; set; }

    /// <summary>
    /// The patient whose record was touched, denormalised even where the entity acted on
    /// was a child of theirs. Without it, "everything anyone did to this patient" is a
    /// union across every table.
    /// </summary>
    public Guid? PatientId { get; set; }

    public Guid? ProviderId { get; set; }

    /// <summary>
    /// The acting user's name as at the time. Kept alongside the id because a provider row
    /// can be renamed, and the trail has to say who it was then.
    /// </summary>
    public string? ProviderName { get; set; }

    public DateTime OccurredUtc { get; set; }

    /// <summary>
    /// Which installation the action happened on. Meaningful even offline: it is how a
    /// change is traced to a specific tablet.
    /// </summary>
    public string? DeviceId { get; set; }

    /// <summary>What changed, or why the action was taken. Never the record's full contents.</summary>
    public string? Detail { get; set; }
}
