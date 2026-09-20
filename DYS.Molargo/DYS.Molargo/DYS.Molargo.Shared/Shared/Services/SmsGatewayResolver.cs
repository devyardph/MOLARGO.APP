using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace DYS.Molargo.Shared.Services;

/// <summary>What a clinic needs to send one text, resolved from its country.</summary>
/// <remarks>
/// Carries the key, unlike the row the vendor's screen reads. This type never reaches a
/// page — it exists so a sender can be handed everything it needs in one object, and it is
/// built inside the service rather than returned to a view model.
/// </remarks>
public sealed record SmsCredentials(
    string CountryCode,
    string ProviderName,
    string ApiUrl,
    string? SenderId,
    decimal PricePerMessage,
    string CurrencyCode,

    // How to ask, not just where. Carried here so a sender needs nothing but this object:
    // the shape of the request is the gateway's, and looking it up separately would be a
    // second read that could disagree with the key it was fetched beside.
    string PayloadTemplate,
    string Headers,
    string ContentType,

    // The scheme and its fields travel with the key, for the same reason the template does:
    // a credential fetched apart from the shape that sends it is two reads that can
    // disagree about which gateway is being talked about.
    SmsAuth Auth)
{
    /// <summary>The request that sending this message would make.</summary>
    public SmsRequest Build(string to, string message) =>
        SmsPayload.Render(
            ApiUrl, ContentType, Headers, PayloadTemplate, to, message, SenderId, Auth);
}

/// <summary>What a clinic is charged per message, with no credential attached.</summary>
/// <remarks>
/// Separate from <see cref="SmsCredentials"/> so the practice's own screens can show the
/// price without the service that answers them ever holding the key. The two differ by one
/// field, and that field is the whole reason there are two.
/// </remarks>
public sealed record SmsPricing(
    bool Available,
    decimal PricePerMessage,
    string CurrencyCode);

/// <summary>
/// Finds the SMS gateway a clinic sends through.
/// </summary>
/// <remarks>
/// <para>
/// Split from the vendor's <c>ISmsGatewayService</c>, which manages the rows and is behind
/// the platform guard. This one is read by the app on a clinic's behalf, so it must work
/// for a practice user — but it answers only for <em>their</em> country, and the key never
/// leaves the send. A clinic can cause a message to go out; it cannot read the credential
/// that sent it.
/// </para>
/// <para>
/// Null rather than an exception where nothing is configured. A country the vendor has not
/// set up is a message that cannot go, which is a thing to report to the person trying to
/// send — not a crash in the middle of a save.
/// </para>
/// </remarks>
public interface ISmsGatewayResolver
{
    /// <summary>The gateway for this clinic's billing country, or null where there is none.</summary>
    Task<SmsCredentials?> ForCurrentTenantAsync(CancellationToken ct = default);

    /// <summary>
    /// What this clinic pays per message, for its own screens.
    /// </summary>
    /// <remarks>
    /// Reports unavailable rather than throwing where the vendor has no gateway for the
    /// country. A practice seeing "not available here" is being told something true; an
    /// error is not.
    /// </remarks>
    Task<SmsPricing> PricingForCurrentTenantAsync(CancellationToken ct = default);
}

/// <inheritdoc cref="ISmsGatewayResolver"/>
public sealed class SmsGatewayResolver : ISmsGatewayResolver
{
    private readonly MolargoDatabase _database;
    private readonly ITenantContext _tenant;

    public SmsGatewayResolver(MolargoDatabase database, ITenantContext tenant)
    {
        _database = database;
        _tenant = tenant;
    }

    public async Task<SmsPricing> PricingForCurrentTenantAsync(CancellationToken ct = default)
    {
        var gateway = await ForCurrentTenantAsync(ct).ConfigureAwait(false);

        // Built from the credentials and then dropping the key, rather than a second query.
        // One lookup means one definition of "which gateway serves this clinic", so the
        // price shown can never be from a different row than the one that would send.
        return gateway is null
            ? new SmsPricing(false, 0m, string.Empty)
            : new SmsPricing(true, gateway.PricePerMessage, gateway.CurrencyCode);
    }

    public async Task<SmsCredentials?> ForCurrentTenantAsync(CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        // The clinic's own row, read by id rather than through the filter, because the
        // gateway below belongs to the vendor and the two reads cannot share one filter.
        var tenant = await db.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.Id == _tenant.TenantId && !row.IsDeleted, ct)
            .ConfigureAwait(false);

        if (tenant is null) return null;

        var country = (tenant.CountryCode ?? string.Empty).Trim().ToUpperInvariant();

        if (country.Length != 2) return null;

        var gateway = await db.SmsGateways
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                row => row.CountryCode == country && row.IsActive && !row.IsDeleted, ct)
            .ConfigureAwait(false);

        // Configured but keyless counts as not configured. A row with no key would send
        // nothing and fail at the provider, which is a worse way to find out.
        if (gateway is null || !gateway.IsConfigured) return null;

        return new SmsCredentials(
            gateway.CountryCode,
            gateway.ProviderName,
            gateway.ApiUrl,
            gateway.SenderId,
            gateway.PricePerMessage,
            gateway.CurrencyCode,
            gateway.PayloadTemplate,
            gateway.Headers,
            gateway.ContentType,
            gateway.Auth);
    }
}
