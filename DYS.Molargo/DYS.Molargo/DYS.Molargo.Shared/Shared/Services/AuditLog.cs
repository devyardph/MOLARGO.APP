using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Data;
using DYS.Molargo.Shared.Repositories;

namespace DYS.Molargo.Shared.Services;

/// <summary>
/// Writes the audit trail for the clinical and financial side of the app.
/// </summary>
/// <remarks>
/// <para>
/// Cross-cutting rather than per feature. Charting, billing, inventory and the patient
/// record all have to write entries now, and an audit writer copied into four services is
/// a writer that will eventually stop stamping the device id in one of them — which is
/// exactly the field nobody notices missing until they need it.
/// </para>
/// <para>
/// Admin and the platform services still have their own private copies of this. They were
/// left alone deliberately: rewriting ten working call sites to prove a point is a change
/// with risk and no benefit, and they can move across the next time one of them is opened
/// for another reason.
/// </para>
/// <para>
/// Every write here is best-effort in the sense that it happens <em>after</em> the thing it
/// records, never before. An audit entry that fails must not take the clinical write with
/// it: a note that cannot be signed because the log is unhappy is a worse outcome than a
/// signature with no entry beside it.
/// </para>
/// </remarks>
public interface IAuditLog
{
    /// <summary>
    /// Records one action.
    /// </summary>
    /// <param name="patientId">
    /// Set wherever the action touched a patient. It is what makes "who has been near this
    /// record" answerable, and it is stored as the id rather than the name so a renamed
    /// patient does not orphan their own history.
    /// </param>
    Task RecordAsync(
        AuditAction action,
        string entityName,
        Guid? entityId,
        string detail,
        Guid? patientId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Records that somebody opened a patient's record, at most once per window.
    /// </summary>
    /// <remarks>
    /// Windowed because an access log answers "who opened this record, and when" — not
    /// "how many times did the screen re-read it". A single visit reloads the record on
    /// every save, every tab, and every return from a child screen, so the unwindowed
    /// version wrote a dozen identical rows per patient per sitting and buried the one
    /// access somebody was actually looking for under its own noise.
    ///
    /// The window is a ceiling on duplicates, not on detection: the first open is always
    /// recorded, which is the event that matters.
    /// </remarks>
    Task RecordViewAsync(
        Guid patientId,
        string entityName,
        string detail,
        CancellationToken ct = default);
}

/// <inheritdoc cref="IAuditLog" />
public sealed class AuditLog : IAuditLog
{
    /// <summary>
    /// How long one person's repeat opens of the same record collapse into one entry.
    /// </summary>
    /// <remarks>
    /// Fifteen minutes: longer than a screen's worth of reloads, shorter than an
    /// appointment, so a second genuine visit to the same record in the same session still
    /// leaves its own line.
    /// </remarks>
    private static readonly TimeSpan ViewWindow = TimeSpan.FromMinutes(15);

    private readonly IRepository<AuditEntry> _audit;
    private readonly IRepository<Provider> _providers;
    private readonly MolargoDatabase _database;
    private readonly ISessionService _session;
    private readonly IClock _clock;

    public AuditLog(
        IRepository<AuditEntry> audit,
        IRepository<Provider> providers,
        MolargoDatabase database,
        ISessionService session,
        IClock clock)
    {
        _audit = audit;
        _providers = providers;
        _database = database;
        _session = session;
        _clock = clock;
    }

    public async Task RecordAsync(
        AuditAction action,
        string entityName,
        Guid? entityId,
        string detail,
        Guid? patientId = null,
        CancellationToken ct = default)
    {
        var providerId = _session.ProviderId;

        // The acting person's name is stamped beside their id, because a provider can be
        // renamed and the trail has to say who it was at the time. Falls back to the
        // display name so a receptionist, who has no provider row, is not logged as
        // "unknown" doing things.
        var actor = providerId is { } id
            ? await _providers.GetByIdAsync(id, ct).ConfigureAwait(false)
            : null;

        await _audit
            .SaveAsync(
                new AuditEntry
                {
                    Action = action,
                    EntityName = entityName,
                    EntityId = entityId,
                    PatientId = patientId,
                    ProviderId = providerId,
                    ProviderName = actor?.FullName ?? _session.UserDisplayName,
                    OccurredUtc = _clock.UtcNow,

                    // From the installation, not from a request header. There is no server
                    // session to read one from, and the tablet a record was opened on is
                    // what an offline-first practice can actually trace.
                    DeviceId = await _database.GetDeviceIdAsync(ct).ConfigureAwait(false),
                    Detail = detail,
                },
                ct)
            .ConfigureAwait(false);
    }

    public async Task RecordViewAsync(
        Guid patientId,
        string entityName,
        string detail,
        CancellationToken ct = default)
    {
        var providerId = _session.ProviderId;
        var since = _clock.UtcNow - ViewWindow;

        // Nobody signed in means this is not a person opening a record — it is seeding, a
        // background load, or a test. Logging those as accesses would put entries with no
        // actor into the one report that exists to name an actor.
        if (providerId is null && string.IsNullOrWhiteSpace(_session.UserDisplayName)) return;

        var seenAlready = await _audit
            .ExistsAsync(
                entry => entry.Action == AuditAction.Viewed
                    && entry.PatientId == patientId
                    && entry.ProviderId == providerId
                    && entry.OccurredUtc >= since,
                ct)
            .ConfigureAwait(false);

        if (seenAlready) return;

        await RecordAsync(
                AuditAction.Viewed, entityName, patientId, detail, patientId, ct)
            .ConfigureAwait(false);
    }
}
