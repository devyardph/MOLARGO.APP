using DYS.Molargo.Shared.Entities;
using DYS.Molargo.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace DYS.Molargo.Shared.Data;

/// <summary>
/// Owns the local database file: first-use creation, the schema-version check, seeding,
/// and this installation's device id. Hands out a fresh <see cref="MolargoDbContext"/> per
/// unit of work rather than sharing a long-lived one, because a <c>DbContext</c> is not
/// thread-safe and two screens can load at once.
/// </summary>
public sealed class MolargoDatabase
{
    /// <summary>
    /// Bump on any change to the local schema.
    /// </summary>
    /// <remarks>
    /// See <see cref="InitialiseAsync"/>: a version this build cannot read is currently
    /// dropped and recreated, which is only acceptable while the database holds nothing
    /// but seeded sample data. Once real patient records live here and there is no server
    /// copy, this has to become an EF Core migration instead — that change is the price of
    /// the app being offline-only, and it is due before the first real user.
    /// </remarks>
    private const int SchemaVersion = 38;

    private readonly IDbContextFactory<MolargoDbContext> _factory;
    private readonly IClock _clock;
    private readonly ITenantContext _tenant;
    private readonly IPasswordHasher _hasher;
    private readonly bool _seedSampleData;

    /// <summary>
    /// Serialises first-use initialisation. Several callers arrive together on startup —
    /// the layout and the first screen both load — and two threads running
    /// <c>EnsureCreated</c> against the same new file race each other.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _ready;

    public MolargoDatabase(
        IDbContextFactory<MolargoDbContext> factory,
        IClock clock,
        ITenantContext tenant,
        IPasswordHasher hasher,
        bool seedSampleData = false)
    {
        _factory = factory;
        _clock = clock;
        _tenant = tenant;
        _hasher = hasher;
        _seedSampleData = seedSampleData;
    }

    /// <summary>
    /// A ready-to-use context, already confined to the current clinic. Dispose it when the
    /// unit of work is done.
    /// </summary>
    /// <remarks>
    /// The tenant is attached here, on the single gateway every caller goes through, rather
    /// than left to each caller to set. A context handed out without it reads nothing —
    /// which is the safe failure, but it is still a bug, and the way to have no such bug is
    /// for no caller to be able to forget.
    /// </remarks>
    public async Task<MolargoDbContext> CreateContextAsync(CancellationToken ct = default)
    {
        if (!_ready) await InitialiseAsync(ct).ConfigureAwait(false);

        var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.TenantSource = _tenant;
        return db;
    }

    /// <summary>
    /// This installation's id, minted and stored on first use. Meaningful even offline: it
    /// is how an audit entry is traced to a specific tablet.
    /// </summary>
    public async Task<string> GetDeviceIdAsync(CancellationToken ct = default)
    {
        await using var db = await CreateContextAsync(ct).ConfigureAwait(false);

        var row = await db.Metadata
            .FirstOrDefaultAsync(m => m.Key == LocalMetadata.DeviceIdKey, ct)
            .ConfigureAwait(false);

        if (row is not null) return row.Value;

        var deviceId = Guid.NewGuid().ToString("n");
        db.Metadata.Add(new LocalMetadata { Key = LocalMetadata.DeviceIdKey, Value = deviceId });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return deviceId;
    }

    private async Task InitialiseAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_ready) return;

            await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var created = await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);

            if (!created && await ReadVersionAsync(db, ct).ConfigureAwait(false) != SchemaVersion)
            {
                // EnsureCreated is a no-op once the file exists — it will not add or alter
                // a table. So a database written by an older build has to be dropped
                // outright.
                //
                // Acceptable only because this file currently holds seeded sample data and
                // nothing else. See the note on SchemaVersion: this must become a
                // migration before anyone's real records are in here, because offline-only
                // means there is no second copy to re-pull from.
                await db.Database.EnsureDeletedAsync(ct).ConfigureAwait(false);
                await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);
                created = true;
            }

            // Resolved before anything else runs, and before the flag is set. Every
            // tenant-scoped table is unreadable until this has happened, so seeding — which
            // reads back what it wrote — has to come after it.
            await ResolveTenantAsync(db, ct).ConfigureAwait(false);

            if (created && _seedSampleData)
            {
                db.TenantSource = _tenant;

                await SampleData.SeedAsync(db, _clock, _tenant.TenantId, _hasher, ct)
                    .ConfigureAwait(false);
            }

            // Stamped last, and that ordering matters more than it looks.
            //
            // It used to be stamped before seeding. So when a seed threw, the version was
            // already current: the next start found an up-to-date schema, skipped seeding,
            // and brought the app up against a completely empty database that let nobody
            // sign in — with nothing on screen or in the log pointing at the seed. Leaving
            // the stamp until the seed has actually finished means a failure is retried on
            // the next start, and keeps failing visibly instead of once, silently.
            if (created)
            {
                await StampVersionAsync(db, ct).ConfigureAwait(false);
            }

            _ready = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Works out which clinic this installation belongs to, minting one on first run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One clinic per installation while the app is offline-only, so the answer lives in
    /// local metadata beside the device id. When a server exists, the tenant will come from
    /// the signed-in user instead and this becomes the offline fallback — the seam is
    /// <see cref="ITenantContext"/>, and nothing above it changes.
    /// </para>
    /// <para>
    /// The name is read from the tenant row afterwards, with the filter deliberately
    /// bypassed: the clinic's own record is the one thing that cannot be found by filtering
    /// on the clinic.
    /// </para>
    /// </remarks>
    private async Task ResolveTenantAsync(MolargoDbContext db, CancellationToken ct)
    {
        var row = await db.Metadata
            .FirstOrDefaultAsync(m => m.Key == LocalMetadata.TenantIdKey, ct)
            .ConfigureAwait(false);

        Guid tenantId;

        if (row is not null && Guid.TryParse(row.Value, out var stored))
        {
            tenantId = stored;
        }
        else
        {
            tenantId = Guid.NewGuid();

            db.Metadata.Add(new LocalMetadata
            {
                Key = LocalMetadata.TenantIdKey,
                Value = tenantId.ToString(),
            });

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Id == tenantId, ct)
            .ConfigureAwait(false);

        _tenant.Use(tenantId, tenant?.Name, tenant?.CurrencyCode);
    }

    private static async Task<int?> ReadVersionAsync(MolargoDbContext db, CancellationToken ct)
    {
        try
        {
            var row = await db.Metadata
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Key == LocalMetadata.SchemaVersionKey, ct)
                .ConfigureAwait(false);

            return row is not null && int.TryParse(row.Value, out var version) ? version : null;
        }
        catch (Exception)
        {
            // A file old enough to have no metadata table at all throws rather than
            // returning nothing. Treated as an unreadable version, which drops and
            // recreates — the same outcome, without a separate table-existence probe.
            return null;
        }
    }

    private async Task StampVersionAsync(MolargoDbContext db, CancellationToken ct)
    {
        db.Metadata.Add(new LocalMetadata
        {
            Key = LocalMetadata.SchemaVersionKey,
            Value = SchemaVersion.ToString(),
        });

        db.Metadata.Add(new LocalMetadata
        {
            Key = LocalMetadata.DeviceIdKey,
            Value = Guid.NewGuid().ToString("n"),
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
