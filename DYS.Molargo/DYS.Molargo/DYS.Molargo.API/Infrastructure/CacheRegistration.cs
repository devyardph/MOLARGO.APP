using DYS.Molargo.Domain.Data;
using DYS.Molargo.Services.Features.Admin;
using DYS.Molargo.Services.Features.Billing;
using Microsoft.Extensions.Caching.Hybrid;

namespace DYS.Molargo.Api.Infrastructure;

/// <summary>
/// Wires the cache, with or without Redis.
/// </summary>
public static class CacheRegistration
{
    /// <summary>
    /// Adds the cache described by the <c>Cache</c> configuration section.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>HybridCache</c> rather than <c>IMemoryCache</c> or <c>IDistributedCache</c>
    /// directly, because it is the one abstraction that makes the switch free: it is always
    /// an in-process first tier, and it uses a registered <c>IDistributedCache</c> as a
    /// second tier if one exists. So turning Redis on is this method registering Redis, and
    /// nothing that reads the cache changes or even knows.
    /// </para>
    /// <para>
    /// It also solves the thing a hand-rolled cache usually gets wrong: when twenty requests
    /// miss the same key at once, one of them runs the query and the rest wait for it,
    /// instead of twenty identical queries hitting a database that is already the reason
    /// somebody added a cache.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMolargoCache(
        this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(CacheOptions.Section).Get<CacheOptions>()
            ?? new CacheOptions();

        services.AddSingleton(options);

        if (options.Redis.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.Redis.ConnectionString))
            {
                // Refused rather than quietly falling back to in-process. Somebody who
                // switched Redis on meant it, and a server that silently ignores the switch
                // is a server whose cache behaviour nobody can reason about.
                throw new InvalidOperationException(
                    "Cache:Redis:Enabled is true but no connection string is set. Supply "
                    + "Cache__Redis__ConnectionString from the deployment's secret store, or "
                    + "set Cache:Redis:Enabled to false to use the in-process cache alone.");
            }

            services.AddStackExchangeRedisCache(redis =>
            {
                redis.Configuration = options.Redis.ConnectionString;

                // Every key this deployment writes is prefixed, so two environments sharing
                // one Redis cannot read each other's answers.
                redis.InstanceName = $"{options.Redis.KeyPrefix}:";
            });
        }

        services
            .AddHybridCache(hybrid =>
            {
                hybrid.DefaultEntryOptions = new HybridCacheEntryOptions
                {
                    Expiration = TimeSpan.FromSeconds(Math.Max(1, options.LifetimeSeconds)),

                    // The in-process tier expires first. A server that has just written a
                    // change should notice it before one that has not, and the shared tier
                    // is what the rest catch up from.
                    LocalCacheExpiration =
                        TimeSpan.FromSeconds(Math.Max(1, options.LifetimeSeconds) / 2),
                };
            });

        return services;
    }

    /// <summary>
    /// The reference data worth holding, and what empties it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Short on purpose. Everything here is a handful of rows read on nearly every screen
    /// and edited a few times a year — the catalogue of procedure codes and their fees, and
    /// the currency every figure on every screen is rendered in. That ratio is what makes a
    /// cache worth having.
    /// </para>
    /// <para>
    /// A report is the opposite and is deliberately absent: caching one would make the
    /// second open fast, leave the first as slow as it was, and hold a report's worth of
    /// rows per clinic per period per site. The fix for a slow report is to read less, not
    /// to remember more.
    /// </para>
    /// </remarks>
    private static readonly CachePlan[] Plans =
    [
        new(
            typeof(IAdminService),
            Reads: ["GetCurrencyAsync"],
            Writes: ["SetCurrencyAsync"]),

        new(
            typeof(IBillingService),
            Reads: ["GetCatalogueAsync", "GetCatalogueEntryAsync"],
            Writes:
            [
                "SaveCatalogueItemAsync",
                "SetSiteFeeAsync",
                "SetCatalogueItemActiveAsync",
            ]),
    ];

    /// <summary>
    /// Puts the caching proxy in front of the services named above.
    /// </summary>
    /// <remarks>
    /// After the services are registered, so this registration is the one that resolves.
    /// The inner instance is built by hand from the same container, which is what the proxy
    /// forwards to — everything not named in a plan goes straight through.
    /// </remarks>
    public static IServiceCollection AddMolargoCachedReads(this IServiceCollection services)
    {
        foreach (var plan in Plans)
        {
            var implementation = services
                .Last(registration => registration.ServiceType == plan.Contract)
                .ImplementationType
                ?? throw new InvalidOperationException(
                    $"{plan.Contract.Name} is not registered by its implementation type, so "
                    + "there is nothing for the caching proxy to wrap.");

            services.AddScoped(plan.Contract, provider => CachingServiceProxy.Create(
                ActivatorUtilities.CreateInstance(provider, implementation),
                provider.GetRequiredService<HybridCache>(),
                provider.GetRequiredService<ITenantContext>(),
                plan,
                provider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger($"Molargo.Cache.{plan.Contract.Name}")));
        }

        return services;
    }
}
