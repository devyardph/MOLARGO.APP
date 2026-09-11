using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Data;
using DYS.Molargo.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace DYS.Molargo.Shared.Features.Platform.Services;

/// <summary>
/// One plan as the vendor's pricing screen shows it.
/// </summary>
/// <param name="Subscribers">
/// Clinics currently on this plan, counted across the tenant boundary. It is the number
/// that decides whether a price can still be changed or has to be grandfathered.
/// </param>
public sealed record PlanRow(
    Guid PlanId,
    string Name,
    string Code,
    string CountryCode,
    string CurrencyCode,
    decimal MonthlyBase,
    int IncludedSites,
    int IncludedSeatsPerSite,
    decimal PricePerExtraSite,
    decimal PricePerExtraSeat,
    int? AnnualMonthsCharged,
    int Subscribers,
    bool IsActive)
{
    public bool HasSubscribers => Subscribers > 0;
}

/// <summary>
/// The plans the vendor sells, per country.
/// </summary>
/// <remarks>
/// Plans are the vendor's own records, like tenants, so this service is guarded by the
/// same <see cref="PlatformGuard"/> check and returns nothing to anybody else. It reads
/// subscriber counts across the tenant boundary — a count, never a clinic's content.
/// </remarks>
public interface IPlanService
{
    /// <summary>Every plan, newest country groupings included, retired ones last.</summary>
    Task<IReadOnlyList<PlanRow>> GetPlansAsync(CancellationToken ct = default);

    /// <summary>The countries plans exist for, in alphabetical order.</summary>
    Task<IReadOnlyList<string>> GetCountriesAsync(CancellationToken ct = default);

    Task<Plan?> GetPlanAsync(Guid planId, CancellationToken ct = default);

    /// <summary>Creates or updates a plan. Returns a refusal, or null.</summary>
    Task<string?> SavePlanAsync(Plan plan, CancellationToken ct = default);

    /// <summary>
    /// Withdraws a plan from sale, or puts it back.
    /// </summary>
    /// <remarks>
    /// Never deletes. Clinics already on a withdrawn plan keep it and keep its price —
    /// grandfathering is the usual reason to withdraw one, and deleting the row would
    /// leave their subscription pointing at nothing.
    /// </remarks>
    Task<string?> SetPlanActiveAsync(
        Guid planId, bool isActive, CancellationToken ct = default);

    /// <summary>Plans a clinic in this country can be put on.</summary>
    Task<IReadOnlyList<Plan>> GetSellablePlansAsync(
        string countryCode, CancellationToken ct = default);
}

/// <inheritdoc cref="IPlanService"/>
public sealed class PlanService : IPlanService
{
    private readonly MolargoDatabase _database;
    private readonly ISessionService _session;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public PlanService(
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

    public async Task<IReadOnlyList<PlanRow>> GetPlansAsync(CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false)) return [];

        var plans = await db.Plans
            .AsNoTracking()
            .Where(plan => !plan.IsDeleted)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Subscribers per plan, grouped to a number. The tenant table is unfiltered so
        // this needs no bypass, and nothing of a clinic's is read beyond the count.
        var subscribers = await db.Tenants
            .AsNoTracking()
            .Where(tenant => !tenant.IsDeleted && !tenant.IsPlatform && tenant.PlanId != null)
            .GroupBy(tenant => tenant.PlanId!.Value)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, ct)
            .ConfigureAwait(false);

        return plans
            .OrderBy(plan => plan.CountryCode)
            .ThenBy(plan => plan.IsActive ? 0 : 1)
            .ThenBy(plan => plan.DisplayOrder)
            .ThenBy(plan => plan.MonthlyBase)
            .Select(plan => new PlanRow(
                plan.Id,
                plan.Name,
                plan.Code,
                plan.CountryCode,
                plan.CurrencyCode,
                plan.MonthlyBase,
                plan.IncludedSites,
                plan.IncludedSeatsPerSite,
                plan.PricePerExtraSite,
                plan.PricePerExtraSeat,
                plan.AnnualMonthsCharged,
                subscribers.GetValueOrDefault(plan.Id),
                plan.IsActive))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetCountriesAsync(CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false)) return [];

        return await db.Plans
            .AsNoTracking()
            .Where(plan => !plan.IsDeleted)
            .Select(plan => plan.CountryCode)
            .Distinct()
            .OrderBy(code => code)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Plan?> GetPlanAsync(Guid planId, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false)) return null;

        return await db.Plans
            .AsNoTracking()
            .FirstOrDefaultAsync(plan => plan.Id == planId, ct)
            .ConfigureAwait(false);
    }

    public async Task<string?> SavePlanAsync(Plan plan, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false))
        {
            return PlatformGuard.NotPermitted;
        }

        if (string.IsNullOrWhiteSpace(plan.Name)) return "A plan needs a name.";

        var code = (plan.Code ?? string.Empty).Trim().ToLowerInvariant();

        if (!IsUsableCode(code))
        {
            return "The plan code has to be 2 to 30 characters of lowercase letters, "
                + "numbers and hyphens — \"practice\".";
        }

        var country = (plan.CountryCode ?? string.Empty).Trim().ToUpperInvariant();

        if (country.Length != 2 || !country.All(char.IsAsciiLetterUpper))
        {
            return "The country has to be a two-letter code — AU, NZ, GB.";
        }

        var currency = (plan.CurrencyCode ?? string.Empty).Trim().ToUpperInvariant();

        if (currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper))
        {
            return "The currency has to be a three-letter code — AUD, NZD, GBP.";
        }

        if (plan.MonthlyBase < 0
            || plan.PricePerExtraSite < 0
            || plan.PricePerExtraSeat < 0)
        {
            return "Prices cannot be negative.";
        }

        if (plan.IncludedSites < 1) return "A plan has to include at least one site.";

        if (plan.IncludedSeatsPerSite < 0)
        {
            return "Included seats cannot be negative. Use zero for a plan that charges "
                + "for every clinician.";
        }

        if (plan.AnnualMonthsCharged is { } months && months is < 1 or > 12)
        {
            return "Annual prepay has to be between 1 and 12 months, or empty for monthly "
                + "only.";
        }

        var existing = await db.Plans
            .FirstOrDefaultAsync(row => row.Id == plan.Id, ct)
            .ConfigureAwait(false);

        // One plan per code per country. The pair is what a subscription is sold by, and a
        // duplicate would make "the Practice plan in Australia" two different prices.
        var clash = await db.Plans
            .AsNoTracking()
            .AnyAsync(
                row => row.Id != plan.Id
                    && row.Code == code
                    && row.CountryCode == country
                    && !row.IsDeleted,
                ct)
            .ConfigureAwait(false);

        if (clash)
        {
            return $"There is already a \"{code}\" plan for {country}. Plans are one per "
                + "code per country — that is how the same plan carries a different price "
                + "in each.";
        }

        var isNew = existing is null;
        var now = _clock.UtcNow;

        if (plan.Id == Guid.Empty) plan.Id = Guid.NewGuid();

        var row = existing ?? new Plan
        {
            Id = plan.Id,

            // The vendor owns plans, so they sit in the vendor's tenant. The table is not
            // filtered — a clinic has to be able to read the plan it is on — but the
            // column is stamped anyway rather than left as a surprising zero.
            TenantId = _tenant.TenantId,
            CreatedUtc = now,
            IsActive = true,
        };

        var priceChanged = existing is not null
            && (existing.MonthlyBase != plan.MonthlyBase
                || existing.PricePerExtraSeat != plan.PricePerExtraSeat
                || existing.PricePerExtraSite != plan.PricePerExtraSite);

        row.Name = plan.Name.Trim();
        row.Code = code;
        row.CountryCode = country;
        row.CurrencyCode = currency;
        row.MonthlyBase = plan.MonthlyBase;
        row.IncludedSites = plan.IncludedSites;
        row.IncludedSeatsPerSite = plan.IncludedSeatsPerSite;
        row.PricePerExtraSite = plan.PricePerExtraSite;
        row.PricePerExtraSeat = plan.PricePerExtraSeat;
        row.AnnualMonthsCharged = plan.AnnualMonthsCharged;
        row.DisplayOrder = plan.DisplayOrder;
        row.UpdatedUtc = now;

        if (isNew) db.Plans.Add(row);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var detail = isNew
            ? $"Added the {row.CountryCode} plan \"{row.Name}\" at "
                + Pricing.Money(row.MonthlyBase, row.CurrencyCode) + " a month"
            : $"Updated the {row.CountryCode} plan \"{row.Name}\""
                + (priceChanged
                    ? $"; price now {Pricing.Money(row.MonthlyBase, row.CurrencyCode)} a month"
                    : string.Empty);

        await AuditAsync(db, row.Id, isNew ? AuditAction.Created : AuditAction.Updated, detail, ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string?> SetPlanActiveAsync(
        Guid planId, bool isActive, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false))
        {
            return PlatformGuard.NotPermitted;
        }

        var plan = await db.Plans
            .FirstOrDefaultAsync(row => row.Id == planId, ct)
            .ConfigureAwait(false);

        if (plan is null) return "That plan no longer exists.";

        if (plan.IsActive == isActive)
        {
            return isActive
                ? $"\"{plan.Name}\" is already on sale."
                : $"\"{plan.Name}\" is already withdrawn.";
        }

        plan.IsActive = isActive;
        plan.UpdatedUtc = _clock.UtcNow;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await AuditAsync(
            db,
            plan.Id,
            isActive ? AuditAction.Updated : AuditAction.Deleted,
            isActive
                ? $"Put the {plan.CountryCode} plan \"{plan.Name}\" back on sale"
                : $"Withdrew the {plan.CountryCode} plan \"{plan.Name}\" from sale — "
                    + "clinics already on it keep it and keep its price",
            ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<IReadOnlyList<Plan>> GetSellablePlansAsync(
        string countryCode, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false)) return [];

        var country = (countryCode ?? string.Empty).Trim().ToUpperInvariant();

        return await db.Plans
            .AsNoTracking()
            .Where(plan => plan.CountryCode == country && plan.IsActive && !plan.IsDeleted)
            .OrderBy(plan => plan.DisplayOrder)
            .ThenBy(plan => plan.MonthlyBase)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    // ---- helpers ---------------------------------------------------------

    private Task<bool> IsSuperAdminAsync(MolargoDbContext db, CancellationToken ct) =>
        PlatformGuard.IsSuperAdminAsync(db, _session.ProviderId, ct);

    private static bool IsUsableCode(string code) =>
        code.Length is >= 2 and <= 30
        && code.All(character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')
        && !code.StartsWith('-')
        && !code.EndsWith('-');

    /// <summary>Writes the entry into the vendor's own tenant.</summary>
    /// <remarks>
    /// A price is the vendor's commercial record, not a clinic's — unlike a suspension,
    /// which goes in the affected clinic's log because that is where they look for it.
    /// </remarks>
    private async Task AuditAsync(
        MolargoDbContext db,
        Guid planId,
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
            EntityName = nameof(Plan),
            EntityId = planId,
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
