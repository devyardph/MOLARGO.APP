namespace DYS.Molargo.Shared.Entities;

/// <summary>
/// A single-row key/value table for facts about the local database file itself — the
/// schema version it was written with, and the device identity that stamps pushes.
/// </summary>
/// <remarks>
/// A key/value shape rather than typed columns precisely because this table must be
/// readable by a build whose schema differs: adding a typed column here would mean the
/// version check itself could fail to run against an older file, which is the one thing
/// it exists to survive.
/// </remarks>
public sealed class LocalMetadata
{
    /// <summary>The schema version the file was created with. See <c>MolargoDatabase</c>.</summary>
    public const string SchemaVersionKey = "schema_version";

    /// <summary>
    /// This installation's identity, minted on first run. Stored rather than derived from
    /// a hardware id: the platform APIs for those need permissions, differ per OS, and
    /// change on a factory reset anyway.
    /// </summary>
    public const string DeviceIdKey = "device_id";

    /// <summary>
    /// The clinic this installation belongs to.
    /// </summary>
    /// <remarks>
    /// Here rather than inferred from the data, because it has to be readable before any
    /// tenant-scoped table can be queried at all — the filter needs a tenant to filter
    /// by. This table is deliberately the one thing not scoped by tenant, which is what
    /// makes it able to hold the answer.
    /// </remarks>
    public const string TenantIdKey = "tenant_id";

    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
