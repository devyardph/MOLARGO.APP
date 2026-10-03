using DYS.Molargo.Domain.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DYS.Molargo.Api.Infrastructure;

/// <summary>
/// Builds a context for <c>dotnet ef</c> and the Package Manager Console.
/// </summary>
/// <remarks>
/// <para>
/// Migrations are how the server's schema is created and changed. The device gets away with
/// dropping and recreating on a version bump because its database is a file holding one
/// practice's own records; a shared server database holding every subscriber's cannot be
/// recreated by anybody, ever.
/// </para>
/// <para>
/// EF prefers a factory like this over building the application's host, so this is the only
/// place the tools get a connection string from. It reads the same configuration the API
/// does — <c>appsettings.json</c>, then the environment's file, then user secrets, then
/// environment variables — so <c>database update</c> applies migrations to exactly the
/// database the app would have talked to, and not a different one.
/// </para>
/// <para>
/// An earlier version used a hard-coded placeholder, on the reasoning that generating SQL
/// needs a provider rather than a server. True for <c>migrations add</c>, and wrong for
/// <c>database update</c>, which has to connect — it failed trying to resolve a host called
/// "design-time". The placeholder survives only as the fallback, so creating a migration
/// still works on a machine with no database configured at all.
/// </para>
/// </remarks>
public sealed class DesignTimeContextFactory : IDesignTimeDbContextFactory<MolargoDbContext>
{
    /// <summary>
    /// What a connection string falls back to when configuration has none.
    /// </summary>
    /// <remarks>
    /// Unresolvable on purpose. <c>migrations add</c> never opens it, and anything that
    /// does should fail by name rather than reach a real server somebody did not mean to
    /// touch.
    /// </remarks>
    private const string NoDatabaseConfigured = "Host=design-time-no-database-configured";

    public MolargoDbContext CreateDbContext(string[] args)
    {
        // Development unless told otherwise. The tools are run by a person at a command
        // prompt, and the alternative default — Production — would have the first
        // "database update" of the day quietly reach for production configuration.
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Development";

        // AppContext.BaseDirectory, not the current directory. The EF tools load this
        // assembly from bin and run it with the working directory set there, so a
        // base path of "wherever the command was typed" found no settings file at all
        // and silently fell through to the unresolvable placeholder.
        //
        // The web SDK copies appsettings.*.json to the output, so this is where they are.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddUserSecrets<DesignTimeContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connection = configuration.GetConnectionString("Molargo");

        if (string.IsNullOrWhiteSpace(connection)) connection = NoDatabaseConfigured;

        var options = new DbContextOptionsBuilder<MolargoDbContext>()
            .UseNpgsql(
                connection,
                npgsql => npgsql.MigrationsAssembly(typeof(DesignTimeContextFactory).Assembly.FullName))
            .Options;

        return new MolargoDbContext(options);
    }
}
