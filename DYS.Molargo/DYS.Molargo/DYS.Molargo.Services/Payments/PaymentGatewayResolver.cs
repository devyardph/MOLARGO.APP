using DYS.Molargo.Domain.Data;
using Microsoft.EntityFrameworkCore;

namespace DYS.Molargo.Services.Payments;

/// <summary>
/// Which payment gateway serves a given country, and the provider that speaks to it.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <c>ISmsGatewayResolver</c>, and it works the same way: the clinic's
/// billing country picks the row, the row carries the credentials, and nothing above here
/// knows which provider answered.
/// </para>
/// <para>
/// By country rather than for the current clinic, unlike the SMS resolver's main method.
/// The billing run has no signed-in clinic — it iterates every practice due a charge, each
/// of which may be in a different country — so the country has to be an argument.
/// </para>
/// <para>
/// Null rather than an exception where nothing is configured. A country the vendor has not
/// set up is a charge that cannot be collected online, which is a thing to report on the
/// billing screen — not a crash that abandons the practices after it in the run.
/// </para>
/// </remarks>
public interface IPaymentGatewayResolver
{
    /// <summary>The gateway for a billing country, or null where there is none.</summary>
    Task<PaymentCredentials?> ForCountryAsync(
        string? countryCode, CancellationToken ct = default);

    /// <summary>
    /// The provider that handles a set of credentials, or null when none is registered.
    /// </summary>
    /// <remarks>
    /// Separate from the lookup because a row naming a provider nobody has written is a
    /// different problem from a country with no row at all, and the two need different
    /// things said about them.
    /// </remarks>
    IPaymentProvider? ProviderFor(PaymentCredentials credentials);

    /// <summary>The provider names this build can actually serve.</summary>
    /// <remarks>
    /// Read by the vendor's screen so the provider field offers what exists rather than a
    /// free-text box whose typo is found weeks later by a practice at a checkout.
    /// </remarks>
    IReadOnlyList<string> KnownProviders { get; }
}

/// <inheritdoc cref="IPaymentGatewayResolver"/>
public sealed class PaymentGatewayResolver : IPaymentGatewayResolver
{
    private readonly IMolargoContextSource _database;
    private readonly IReadOnlyList<IPaymentProvider> _providers;

    public PaymentGatewayResolver(
        IMolargoContextSource database, IEnumerable<IPaymentProvider> providers)
    {
        _database = database;
        _providers = providers.ToList();
    }

    public IReadOnlyList<string> KnownProviders =>
        _providers.Select(provider => provider.Name).OrderBy(name => name).ToList();

    public async Task<PaymentCredentials?> ForCountryAsync(
        string? countryCode, CancellationToken ct = default)
    {
        var country = (countryCode ?? string.Empty).Trim().ToUpperInvariant();

        if (country.Length != 2) return null;

        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        // Filters bypassed, deliberately: these rows belong to the vendor rather than to a
        // clinic, so there is no tenant to filter on — and the caller is usually a billing
        // run with no clinic resolved at all.
        var row = await db.PaymentGateways
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                gateway => gateway.CountryCode == country
                    && gateway.IsActive
                    && !gateway.IsDeleted,
                ct)
            .ConfigureAwait(false);

        // Configured as well as present. A row saved with no key is a gateway somebody
        // started setting up, and treating it as ready means a practice meets the failure
        // instead of the vendor.
        if (row is null || !row.IsConfigured) return null;

        return new PaymentCredentials(
            row.CountryCode,
            row.ProviderName,
            row.ApiUrl,
            row.SecretKey,
            row.WebhookSecret,
            row.CurrencyCode,
            row.IsLive);
    }

    public IPaymentProvider? ProviderFor(PaymentCredentials credentials) =>
        _providers.FirstOrDefault(provider => string.Equals(
            provider.Name, credentials.ProviderName, StringComparison.OrdinalIgnoreCase));
}
