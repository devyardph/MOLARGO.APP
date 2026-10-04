namespace DYS.Molargo.Api.Infrastructure;

/// <summary>
/// Where cached answers are kept, and for how long.
/// </summary>
/// <remarks>
/// <para>
/// The cache is always on in-process. Redis is the second tier, and it is the thing this
/// switches: without it each server remembers on its own, which is correct and slightly
/// wasteful behind a load balancer; with it they share, and a practice's sites are read from
/// the database once rather than once per server.
/// </para>
/// <para>
/// Turning Redis off is a configuration change and nothing more. No calling code names it —
/// see <c>AddMolargoCache</c> — so a Redis that is down or not yet provisioned leaves an app
/// that still works, just with a colder cache.
/// </para>
/// </remarks>
public sealed class CacheOptions
{
    public const string Section = "Cache";

    /// <summary>
    /// How long a cached answer stands, in seconds.
    /// </summary>
    /// <remarks>
    /// Two minutes by default: long enough that a burst of calls from one screen costs one
    /// query, short enough that adding a site in Admin shows up without anybody restarting
    /// anything. Lengthening it trades staleness for load, and the things cached here are
    /// edited rarely — but "the site I just created is missing" reads as a bug, so it should
    /// not grow far.
    /// </remarks>
    public int LifetimeSeconds { get; set; } = 120;

    public RedisOptions Redis { get; set; } = new();

    public sealed class RedisOptions
    {
        /// <summary>
        /// Whether to use Redis as the shared second tier.
        /// </summary>
        /// <remarks>
        /// Off by default, and deliberately: a default that reaches for a server which may
        /// not exist turns a missing dependency into a startup failure for anybody who
        /// simply cloned the repository.
        /// </remarks>
        public bool Enabled { get; set; }

        /// <summary>The StackExchange.Redis connection string.</summary>
        /// <remarks>
        /// Not committed. It belongs in the deployment's secret store or in
        /// <c>Cache__Redis__ConnectionString</c>, for the same reason as the database
        /// credential: a connection string in appsettings.json is published to whoever can
        /// read the repository.
        /// </remarks>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// Prefixes every key this deployment writes.
        /// </summary>
        /// <remarks>
        /// So a staging server and a production server can share a Redis instance without
        /// reading each other's answers. Without it the first deployment to cache a clinic's
        /// sites would serve them to the other — which, given both may hold the same tenant
        /// ids from a restored dump, is a cross-environment data leak rather than a mix-up.
        /// </remarks>
        public string KeyPrefix { get; set; } = "molargo";
    }
}
