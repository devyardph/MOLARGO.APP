using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Data;
using DYS.Molargo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using static DYS.Molargo.Services.Features.Platform.PlatformGuard;

namespace DYS.Molargo.Services.Features.Platform;

/// <summary>One country's payment gateway, as the vendor's screen reads it.</summary>
/// <remarks>
/// Neither secret comes back. The screen reports only whether each is stored — a key
/// rendered into a page is a key in a browser cache, a screenshot and a support ticket.
/// </remarks>
public sealed record PaymentGatewayRow(
    Guid GatewayId,
    string CountryCode,
    string ProviderName,
    string ApiUrl,
    string CurrencyCode,
    decimal PercentageFee,
    decimal FixedFee,
    bool IsLive,
    bool IsActive,

    /// <summary>Whether a secret key is stored. Never the key.</summary>
    bool HasSecretKey,

    /// <summary>
    /// Whether a webhook signing secret is stored.
    /// </summary>
    /// <remarks>
    /// Shown separately because its absence has its own symptom: payments succeed and
    /// nothing is ever marked paid, because every webhook is refused. That reads as the
    /// provider being broken and is a field nobody filled in.
    /// </remarks>
    bool HasWebhookSecret);

/// <summary>A country that has practices but nowhere for them to pay.</summary>
public sealed record PaymentCountryGap(string CountryCode, int Tenants);

/// <summary>
/// The vendor's payment gateways, one per country.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <c>ISmsGatewayService</c>, behind the same <see cref="PlatformGuard"/>
/// check. A clinic cannot read these at all: the key collects money for every practice in
/// the country, so one practice holding it could take payments as the vendor.
/// </para>
/// <para>
/// Both secrets are write-only through this contract. They go in on a save and never come
/// back; passing null on a later save leaves whatever is stored alone, so editing the fee
/// does not require re-entering a key nobody can read.
/// </para>
/// </remarks>
public interface IPaymentGatewayService
{
    /// <summary>Every gateway, newest country first. Empty for anybody but the vendor.</summary>
    Task<IReadOnlyList<PaymentGatewayRow>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// The provider names this build can actually serve.
    /// </summary>
    /// <remarks>
    /// Async and returning a Task even though the answer is a field, because this contract
    /// crosses the wire: the client is a proxy that turns a call into an HTTP request, and
    /// a member returning a bare value has nothing to await. The self-test refuses to pass
    /// with one, which is how this was caught rather than at the first call from a screen.
    /// </remarks>
    Task<IReadOnlyList<string>> GetProvidersAsync(CancellationToken ct = default);

    /// <summary>
    /// The countries the vendor sells in, taken from the plans.
    /// </summary>
    /// <remarks>
    /// So the country is picked rather than typed. A mistyped country is a gateway that
    /// matches no clinic and gives no sign of it — the practices in that country simply
    /// never get a payment link, and the row looks perfectly configured.
    /// </remarks>
    Task<IReadOnlyList<string>> GetCountriesAsync(CancellationToken ct = default);

    /// <summary>
    /// Countries with practices on the platform and no gateway configured.
    /// </summary>
    /// <remarks>
    /// The screen's reason for existing. A country that has signed up practices and no way
    /// to collect from them is money not being taken, and nothing else in the app would
    /// ever mention it.
    /// </remarks>
    Task<IReadOnlyList<PaymentCountryGap>> GetGapsAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates or updates one country's gateway. Null on success, or the refusal.
    /// </summary>
    /// <param name="secretKey">Null leaves the stored key alone.</param>
    /// <param name="webhookSecret">Null leaves the stored signing secret alone.</param>
    Task<string?> SaveAsync(
        Guid gatewayId,
        string countryCode,
        string providerName,
        string apiUrl,
        string currencyCode,
        decimal percentageFee,
        decimal fixedFee,
        bool isLive,
        string? secretKey,
        string? webhookSecret,
        CancellationToken ct = default);

    Task<string?> SetActiveAsync(Guid gatewayId, bool isActive, CancellationToken ct = default);

    Task<string?> DeleteAsync(Guid gatewayId, CancellationToken ct = default);
}

/// <inheritdoc cref="IPaymentGatewayService"/>
public sealed class PaymentGatewayService : IPaymentGatewayService
{
    private readonly IMolargoContextSource _database;
    private readonly ISessionService _session;
    private readonly ITenantContext _tenant;
    private readonly IPaymentGatewayResolver _resolver;
    private readonly IClock _clock;

    public PaymentGatewayService(
        IMolargoContextSource database,
        ISessionService session,
        ITenantContext tenant,
        IPaymentGatewayResolver resolver,
        IClock clock)
    {
        _database = database;
        _session = session;
        _tenant = tenant;
        _resolver = resolver;
        _clock = clock;
    }

    public Task<IReadOnlyList<string>> GetProvidersAsync(CancellationToken ct = default) =>
        Task.FromResult(_resolver.KnownProviders);

    public async Task<IReadOnlyList<PaymentGatewayRow>> GetAllAsync(
        CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, _session.ProviderId, ct).ConfigureAwait(false))
        {
            return [];
        }

        // The filter is bypassed on purpose: these rows are the vendor's and carry the
        // vendor's tenant id, while the ambient filter follows whichever clinic the
        // operator happens to be looking at.
        var rows = await db.PaymentGateways
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(gateway => !gateway.IsDeleted)
            .OrderBy(gateway => gateway.CountryCode)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows
            .Select(row => new PaymentGatewayRow(
                row.Id,
                row.CountryCode,
                row.ProviderName,
                row.ApiUrl,
                row.CurrencyCode,
                row.PercentageFee,
                row.FixedFee,
                row.IsLive,
                row.IsActive,
                !string.IsNullOrWhiteSpace(row.SecretKey),
                !string.IsNullOrWhiteSpace(row.WebhookSecret)))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetCountriesAsync(CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, _session.ProviderId, ct).ConfigureAwait(false))
        {
            return [];
        }

        return await db.Plans
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(plan => !plan.IsDeleted)
            .Select(plan => plan.CountryCode)
            .Distinct()
            .OrderBy(code => code)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PaymentCountryGap>> GetGapsAsync(
        CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, _session.ProviderId, ct).ConfigureAwait(false))
        {
            return [];
        }

        var served = await db.PaymentGateways
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(gateway => gateway.IsActive && !gateway.IsDeleted)
            .Select(gateway => gateway.CountryCode)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var counts = await db.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(tenant => !tenant.IsDeleted && tenant.IsActive)
            .GroupBy(tenant => tenant.CountryCode)
            .Select(group => new { Country = group.Key, Count = group.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return counts
            .Where(entry => !served.Contains(entry.Country, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.Count)
            .Select(entry => new PaymentCountryGap(entry.Country, entry.Count))
            .ToList();
    }

    public async Task<string?> SaveAsync(
        Guid gatewayId,
        string countryCode,
        string providerName,
        string apiUrl,
        string currencyCode,
        decimal percentageFee,
        decimal fixedFee,
        bool isLive,
        string? secretKey,
        string? webhookSecret,
        CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, _session.ProviderId, ct).ConfigureAwait(false))
        {
            return NotPermitted;
        }

        var country = (countryCode ?? string.Empty).Trim().ToUpperInvariant();

        if (country.Length != 2 || !country.All(char.IsAsciiLetterUpper))
        {
            return "The country has to be a two-letter code — PH, AU, NZ.";
        }

        var provider = (providerName ?? string.Empty).Trim();

        // Checked against what this build can actually do, not merely that something was
        // typed. A row naming a provider nobody has written is a country whose practices
        // meet a failure at the checkout — found by them, weeks later.
        if (!_resolver.KnownProviders.Contains(provider, StringComparer.OrdinalIgnoreCase))
        {
            return provider.Length == 0
                ? "Pick the provider that collects for this country."
                : $"\"{provider}\" is not a provider this build can use. "
                    + $"It knows: {string.Join(", ", _resolver.KnownProviders)}.";
        }

        var url = (apiUrl ?? string.Empty).Trim();

        // https only, and parsed rather than pattern-matched. This request carries a key
        // that can move money; over http it is read by anything in between.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps)
        {
            return "The API URL has to be an absolute https address — "
                + "https://api.paymongo.com/v1.";
        }

        var currency = (currencyCode ?? string.Empty).Trim().ToUpperInvariant();

        if (!PracticeCurrency.IsKnown(currency))
        {
            return $"\"{currency}\" is not a currency this platform knows. Pick one from "
                + "the list.";
        }

        if (percentageFee is < 0m or > 1m)
        {
            // A fraction, not a percentage figure. 0.035 is three and a half per cent, and
            // somebody typing 3.5 here would record a provider that keeps three hundred
            // and fifty per cent of every charge.
            return "The percentage fee is a fraction between 0 and 1 — 0.035 for 3.5%.";
        }

        if (fixedFee < 0m) return "A fixed fee cannot be negative.";

        // Whichever row is being edited, if any. Looked up before the duplicate check,
        // because "is this country taken" means "taken by a row that is not this one".
        var row = gatewayId == Guid.Empty
            ? null
            : await db.PaymentGateways
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    entry => entry.Id == gatewayId && !entry.IsDeleted, ct)
                .ConfigureAwait(false);

        if (gatewayId != Guid.Empty && row is null) return "That gateway is no longer here.";

        var existing = await db.PaymentGateways
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                entry => entry.CountryCode == country && !entry.IsDeleted, ct)
            .ConfigureAwait(false);

        // Caught here as well as by the unique index, so the message names the country
        // rather than surfacing a constraint violation nobody can read.
        //
        // The comparison is against the row being edited, not against the id passed in.
        // It was the other way round once, and the bug it hid was not a duplicate row: a
        // create for a country that already had one fell through to the existing row and
        // overwrote it — including its keys. A vendor adding a second gateway by mistake
        // would have destroyed the live merchant credential for that country.
        if (existing is not null && (row is null || existing.Id != row.Id))
        {
            return $"{country} already has a gateway. Edit that one rather than adding a "
                + "second — two rows would be two answers to where its practices pay.";
        }

        var now = _clock.UtcNow;
        var isNew = row is null;

        if (row is null)
        {
            row = new PaymentGateway
            {
                Id = Guid.NewGuid(),

                // The vendor's tenant, like the SMS gateways beside it. These rows are the
                // platform's, not a clinic's.
                TenantId = _tenant.TenantId,
                CreatedUtc = now,
            };

            db.PaymentGateways.Add(row);
        }

        row.CountryCode = country;
        row.ProviderName = provider;
        row.ApiUrl = url;
        row.CurrencyCode = currency;
        row.PercentageFee = percentageFee;
        row.FixedFee = fixedFee;
        row.IsLive = isLive;
        row.UpdatedUtc = now;

        // Null leaves what is stored alone; a value replaces it. That is what makes the
        // fee editable without re-entering a key the screen cannot show back.
        if (!string.IsNullOrWhiteSpace(secretKey)) row.SecretKey = secretKey.Trim();

        if (!string.IsNullOrWhiteSpace(webhookSecret))
        {
            row.WebhookSecret = webhookSecret.Trim();
        }

        if (isNew && string.IsNullOrWhiteSpace(row.SecretKey))
        {
            return "A new gateway needs its secret key — without one there is nothing to "
                + "collect with.";
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return null;
    }

    public async Task<string?> SetActiveAsync(
        Guid gatewayId, bool isActive, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, _session.ProviderId, ct).ConfigureAwait(false))
        {
            return NotPermitted;
        }

        var row = await db.PaymentGateways
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(entry => entry.Id == gatewayId && !entry.IsDeleted, ct)
            .ConfigureAwait(false);

        if (row is null) return "That gateway is no longer here.";

        row.IsActive = isActive;
        row.UpdatedUtc = _clock.UtcNow;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return null;
    }

    public async Task<string?> DeleteAsync(Guid gatewayId, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, _session.ProviderId, ct).ConfigureAwait(false))
        {
            return NotPermitted;
        }

        var row = await db.PaymentGateways
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(entry => entry.Id == gatewayId && !entry.IsDeleted, ct)
            .ConfigureAwait(false);

        if (row is null) return "That gateway is no longer here.";

        // Soft, like everything else here. Charges already raised through this gateway
        // hold references the provider issued, and a hard delete makes those unreadable.
        row.IsDeleted = true;
        row.DeletedUtc = _clock.UtcNow;
        row.UpdatedUtc = _clock.UtcNow;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return null;
    }
}
