using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Data;
using DYS.Molargo.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace DYS.Molargo.Shared.Features.Platform.Services;

/// <summary>
/// One clinic's line in the billing table.
/// </summary>
/// <param name="Latest">
/// The most recent charge, or null where none has been raised. Null is not "unpaid" — it
/// is "never billed", and the table shows the two differently because they need different
/// follow-up.
/// </param>
/// <param name="Outstanding">
/// Everything still owed across every period, in the clinic's own currency. Summed rather
/// than stored: a running balance that drifts from the charges behind it is worse than no
/// balance at all.
/// </param>
public sealed record BillingRow(
    Guid TenantId,
    string Clinic,
    string Slug,
    string CountryCode,
    string? PlanName,
    string CurrencyCode,
    decimal? CurrentMonthly,
    SubscriptionCharge? Latest,
    decimal Outstanding,
    int FailedCount,
    int PaidCount,
    bool TenantIsActive)
{
    public ChargeStatus? Status => Latest?.Status;

    /// <summary>No charge has ever been raised for this clinic.</summary>
    public bool NeverBilled => Latest is null;

    public bool HasOutstanding => Outstanding > 0;

    /// <summary>
    /// A clinic still able to sign in while owing money on a failed payment.
    /// </summary>
    /// <remarks>
    /// The one combination worth flagging: it is where a suspension conversation starts,
    /// and nothing in the app does it automatically.
    /// </remarks>
    public bool FailedAndActive =>
        TenantIsActive && Latest is { Status: ChargeStatus.Failed };
}

/// <summary>What one attempt to raise charges produced.</summary>
public sealed record ChargeRun(int Raised, int Skipped, string? Refusal);

/// <summary>
/// The vendor's subscription billing: what each clinic owes, and whether it was paid.
/// </summary>
/// <remarks>
/// <para>
/// A record, not a payment processor. There is no gateway, no card on file, no retry
/// schedule and no dunning email. Raising a charge writes a row; marking it paid or failed
/// records what a person observed happening in a bank account. Every screen that shows
/// these says so, because a green "Paid" is exactly the kind of thing a reader assumes was
/// automatic.
/// </para>
/// <para>
/// Guarded by <see cref="PlatformGuard"/> like the rest of the vendor's services, and it
/// crosses the tenant boundary deliberately — charges are stamped with the clinic they
/// belong to so a practice can be shown its own later.
/// </para>
/// </remarks>
public interface ISubscriptionBillingService
{
    /// <summary>One row per subscribing clinic, worst first.</summary>
    Task<IReadOnlyList<BillingRow>> GetBillingAsync(CancellationToken ct = default);

    /// <summary>Every charge raised against one clinic, newest first.</summary>
    Task<IReadOnlyList<SubscriptionCharge>> GetHistoryAsync(
        Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Raises this month's charge for every active clinic that does not have one.
    /// </summary>
    /// <remarks>
    /// Idempotent by period: running it twice in a month raises nothing the second time.
    /// It has to be, because there is no scheduler — a person presses this, and a person
    /// will press it twice.
    /// </remarks>
    Task<ChargeRun> RaiseMonthlyChargesAsync(CancellationToken ct = default);

    /// <summary>Records that the money arrived.</summary>
    Task<string?> MarkPaidAsync(
        Guid chargeId, string? reference, CancellationToken ct = default);

    /// <summary>Records that collection was attempted and failed.</summary>
    Task<string?> MarkFailedAsync(
        Guid chargeId, string reason, CancellationToken ct = default);

    /// <summary>Writes a charge off without payment.</summary>
    Task<string?> WaiveAsync(
        Guid chargeId, string reason, CancellationToken ct = default);
}

/// <inheritdoc cref="ISubscriptionBillingService"/>
public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private readonly MolargoDatabase _database;
    private readonly ISessionService _session;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public SubscriptionBillingService(
        MolargoDatabase database,
        ISessionService session,
        ITenantContext tenant,
        IClock clock)
    {
        _database = database;
        _session = session;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<IReadOnlyList<BillingRow>> GetBillingAsync(
        CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false)) return [];

        var tenants = await db.Tenants
            .AsNoTracking()
            .Where(tenant => !tenant.IsDeleted && !tenant.IsPlatform)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var plans = await db.Plans
            .AsNoTracking()
            .ToDictionaryAsync(plan => plan.Id, plan => plan, ct)
            .ConfigureAwait(false);

        // Charges across every clinic. The filter is bypassed on purpose: these are
        // stamped with the clinic they bill, and this is the vendor reading its own ledger.
        var charges = await db.SubscriptionCharges
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(charge => !charge.IsDeleted)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var byTenant = charges
            .GroupBy(charge => charge.TenantId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var clinicians = await ClinicianCountsAsync(db, ct).ConfigureAwait(false);
        var sites = await SiteCountsAsync(db, ct).ConfigureAwait(false);

        return tenants
            .Select(tenant =>
            {
                var own = byTenant.GetValueOrDefault(tenant.Id) ?? [];

                var latest = own
                    .OrderByDescending(charge => charge.PeriodStart)
                    .ThenByDescending(charge => charge.CreatedUtc)
                    .FirstOrDefault();

                var plan = tenant.PlanId is { } planId
                    ? plans.GetValueOrDefault(planId)
                    : null;

                var quote = plan is null
                    ? null
                    : Pricing.Quote(
                        plan,
                        sites.GetValueOrDefault(tenant.Id),
                        clinicians.GetValueOrDefault(tenant.Id));

                return new BillingRow(
                    tenant.Id,
                    tenant.Name,
                    tenant.Slug,
                    tenant.CountryCode,
                    plan?.Name,

                    // The charge's own currency wins where there is one, because that is
                    // what the money was in. The plan's is the fallback for a clinic that
                    // has never been billed.
                    latest?.CurrencyCode ?? plan?.CurrencyCode ?? "AUD",

                    // Null, not zero, where the plan cannot price them — see
                    // PlanQuote.SitesNotOffered.
                    quote is null or { SitesNotOffered: true } ? null : quote.Monthly,
                    latest,
                    own.Where(charge => charge.IsOutstanding).Sum(charge => charge.Amount),
                    own.Count(charge => charge.Status == ChargeStatus.Failed),
                    own.Count(charge => charge.Status == ChargeStatus.Paid),
                    tenant.IsActive);
            })

            // Failures first, then anything else owing, then never-billed, then the
            // clinics that are fine. The order somebody has to work through.
            .OrderByDescending(row => row.FailedAndActive)
            .ThenByDescending(row => row.Status == ChargeStatus.Failed)
            .ThenByDescending(row => row.HasOutstanding)
            .ThenByDescending(row => row.NeverBilled)
            .ThenBy(row => row.Clinic)
            .ToList();
    }

    public async Task<IReadOnlyList<SubscriptionCharge>> GetHistoryAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false)) return [];

        return await db.SubscriptionCharges
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(charge => charge.TenantId == tenantId && !charge.IsDeleted)
            .OrderByDescending(charge => charge.PeriodStart)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<ChargeRun> RaiseMonthlyChargesAsync(CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false))
        {
            return new ChargeRun(0, 0, PlatformGuard.NotPermitted);
        }

        var today = _clock.Today;
        var start = new DateOnly(today.Year, today.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var now = _clock.UtcNow;

        var tenants = await db.Tenants
            .AsNoTracking()
            .Where(tenant => !tenant.IsDeleted && !tenant.IsPlatform && tenant.IsActive)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var plans = await db.Plans
            .AsNoTracking()
            .ToDictionaryAsync(plan => plan.Id, plan => plan, ct)
            .ConfigureAwait(false);

        var existing = await db.SubscriptionCharges
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(charge => charge.PeriodStart == start && !charge.IsDeleted)
            .Select(charge => charge.TenantId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var already = existing.ToHashSet();

        var clinicians = await ClinicianCountsAsync(db, ct).ConfigureAwait(false);
        var sites = await SiteCountsAsync(db, ct).ConfigureAwait(false);

        var raised = 0;
        var skipped = 0;

        foreach (var tenant in tenants)
        {
            if (already.Contains(tenant.Id))
            {
                skipped++;
                continue;
            }

            // Nothing is charged during the free trial. This is the one place the trial
            // does anything at all — it is otherwise a date on the record — and it is
            // checked against the period being raised rather than against today, so
            // back-billing a past month does not accidentally bill a trial that was
            // running at the time.
            if (tenant.IsInTrial(start))
            {
                skipped++;
                continue;
            }

            // A clinic with no plan cannot be charged. Skipped rather than billed zero,
            // which would look settled and hide that nobody has sold them anything.
            if (tenant.PlanId is not { } planId
                || plans.GetValueOrDefault(planId) is not { } plan)
            {
                skipped++;
                continue;
            }

            var quote = Pricing.Quote(
                plan,
                sites.GetValueOrDefault(tenant.Id),
                clinicians.GetValueOrDefault(tenant.Id));

            // Likewise a clinic the plan cannot serve. There is no correct amount, so
            // there is no charge to raise — the Clinics screen flags it as "plan too
            // small" and somebody has to move them first.
            if (quote.SitesNotOffered)
            {
                skipped++;
                continue;
            }

            db.SubscriptionCharges.Add(new SubscriptionCharge
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                PlanId = plan.Id,
                PlanName = plan.Name,
                PeriodStart = start,
                PeriodEnd = end,
                Amount = quote.Monthly,
                CurrencyCode = plan.CurrencyCode,
                Sites = quote.Sites,
                Clinicians = quote.Clinicians,
                Status = ChargeStatus.Due,
                CreatedUtc = now,
                UpdatedUtc = now,
            });

            raised++;
        }

        if (raised > 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            await AuditAsync(
                db,
                null,
                AuditAction.Created,
                $"Raised {raised} subscription "
                    + (raised == 1 ? "charge" : "charges")
                    + $" for {start:MMMM yyyy}"
                    + (skipped > 0 ? $"; skipped {skipped}" : string.Empty),
                ct)
                .ConfigureAwait(false);
        }

        return new ChargeRun(raised, skipped, null);
    }

    public async Task<string?> MarkPaidAsync(
        Guid chargeId, string? reference, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var charge = await FindAsync(db, chargeId, ct).ConfigureAwait(false);
        if (charge is null) return await RefusalAsync(db, ct).ConfigureAwait(false);

        if (charge.Status == ChargeStatus.Paid)
        {
            return $"{charge.PeriodLabel} is already recorded as paid.";
        }

        var now = _clock.UtcNow;

        charge.Status = ChargeStatus.Paid;
        charge.AttemptedUtc ??= now;
        charge.SettledUtc = now;
        charge.Reference = Trim(reference);

        // Cleared, because it is no longer true. A paid charge still showing why it once
        // failed is how a clinic gets chased for money it has already sent.
        charge.FailureReason = null;
        charge.UpdatedUtc = now;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await AuditAsync(
            db,
            charge.Id,
            AuditAction.Updated,
            $"Recorded payment of {Pricing.Money(charge.Amount, charge.CurrencyCode)} for "
                + $"{charge.PeriodLabel}"
                + (charge.Reference is { Length: > 0 } reference2
                    ? $" (ref {reference2})"
                    : string.Empty),
            ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string?> MarkFailedAsync(
        Guid chargeId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            // Required, because "failed" with no reason is the status that generates a
            // phone call to ask what this means.
            return "Say what went wrong — a declined card and an unpaid invoice need "
                + "different follow-up.";
        }

        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var charge = await FindAsync(db, chargeId, ct).ConfigureAwait(false);
        if (charge is null) return await RefusalAsync(db, ct).ConfigureAwait(false);

        if (charge.Status == ChargeStatus.Paid)
        {
            return $"{charge.PeriodLabel} is recorded as paid. Reverse that first if the "
                + "payment did not actually clear.";
        }

        var now = _clock.UtcNow;

        charge.Status = ChargeStatus.Failed;
        charge.AttemptedUtc = now;
        charge.SettledUtc = null;
        charge.FailureReason = reason.Trim();
        charge.UpdatedUtc = now;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await AuditAsync(
            db,
            charge.Id,
            AuditAction.Updated,
            $"Recorded a failed payment for {charge.PeriodLabel}: {charge.FailureReason}",
            ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string?> WaiveAsync(
        Guid chargeId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "Say why it is being written off. This is money the vendor decided not "
                + "to collect, and next quarter nobody will remember why.";
        }

        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var charge = await FindAsync(db, chargeId, ct).ConfigureAwait(false);
        if (charge is null) return await RefusalAsync(db, ct).ConfigureAwait(false);

        if (charge.Status == ChargeStatus.Paid)
        {
            return $"{charge.PeriodLabel} is already paid, so there is nothing to waive.";
        }

        var now = _clock.UtcNow;

        charge.Status = ChargeStatus.Waived;
        charge.FailureReason = reason.Trim();
        charge.UpdatedUtc = now;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await AuditAsync(
            db,
            charge.Id,
            AuditAction.Updated,
            $"Waived {Pricing.Money(charge.Amount, charge.CurrencyCode)} for "
                + $"{charge.PeriodLabel}: {charge.FailureReason}",
            ct)
            .ConfigureAwait(false);

        return null;
    }

    // ---- helpers ---------------------------------------------------------

    private Task<bool> IsSuperAdminAsync(MolargoDbContext db, CancellationToken ct) =>
        PlatformGuard.IsSuperAdminAsync(db, _session.ProviderId, ct);

    /// <summary>
    /// The charge, tracked, or null where the caller may not have it.
    /// </summary>
    /// <remarks>
    /// Returns null for both "not permitted" and "not found" so the caller cannot
    /// accidentally distinguish them; <see cref="RefusalAsync"/> then decides which message
    /// to give. A vendor deserves "no longer exists"; anybody else gets the generic
    /// refusal and learns nothing about what ids are real.
    /// </remarks>
    private async Task<SubscriptionCharge?> FindAsync(
        MolargoDbContext db, Guid chargeId, CancellationToken ct)
    {
        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false)) return null;

        return await db.SubscriptionCharges
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(charge => charge.Id == chargeId && !charge.IsDeleted, ct)
            .ConfigureAwait(false);
    }

    private async Task<string> RefusalAsync(MolargoDbContext db, CancellationToken ct) =>
        await IsSuperAdminAsync(db, ct).ConfigureAwait(false)
            ? "That charge no longer exists."
            : PlatformGuard.NotPermitted;

    /// <summary>
    /// Active clinicians per tenant.
    /// </summary>
    /// <remarks>
    /// The roles are spelled out rather than filtered through
    /// <c>ProviderRoles.IsClinical</c> so the count happens in SQL. The predicate cannot be
    /// translated, and pulling every provider row across every tenant into memory to test
    /// it is exactly the cross-tenant read this service is careful not to do.
    /// </remarks>
    private static async Task<Dictionary<Guid, int>> ClinicianCountsAsync(
        MolargoDbContext db, CancellationToken ct) =>
        await db.Providers
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(provider => !provider.IsDeleted
                && provider.IsActive
                && (provider.Role == ProviderRole.Dentist
                    || provider.Role == ProviderRole.Hygienist
                    || provider.Role == ProviderRole.OralHealthTherapist
                    || provider.Role == ProviderRole.DentalTherapist
                    || provider.Role == ProviderRole.Prosthetist
                    || provider.Role == ProviderRole.Specialist))
            .GroupBy(provider => provider.TenantId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, ct)
            .ConfigureAwait(false);

    private static async Task<Dictionary<Guid, int>> SiteCountsAsync(
        MolargoDbContext db, CancellationToken ct) =>
        await db.PracticeLocations
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(location => !location.IsDeleted && location.IsActive)
            .GroupBy(location => location.TenantId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, ct)
            .ConfigureAwait(false);

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Writes the entry into the vendor's own tenant.</summary>
    /// <remarks>
    /// The vendor's commercial record rather than the clinic's. A suspension goes in the
    /// practice's log because that is where they look for it; who marked an invoice paid is
    /// the vendor's own book-keeping.
    /// </remarks>
    private async Task AuditAsync(
        MolargoDbContext db,
        Guid? chargeId,
        AuditAction action,
        string detail,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;

        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            Action = action,
            EntityName = nameof(SubscriptionCharge),
            EntityId = chargeId,
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
