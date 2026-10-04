using System.Diagnostics;
using DYS.Molargo.Domain.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace DYS.Molargo.Api.Infrastructure;

/// <summary>
/// Brings the database's schema up to the code's.
/// </summary>
/// <remarks>
/// <para>
/// Nothing applied migrations before this. The app never called <c>Migrate()</c>, there is
/// no deployment script, and so every release depended on somebody remembering to run
/// <c>dotnet ef database update</c> from a developer's machine against the right
/// connection string. The failure when they forgot was not a clear one: the app started
/// normally and then threw <c>42703: column does not exist</c> on whichever screen happened
/// to touch the new column first, which reads as a bug in that screen.
/// </para>
/// <para>
/// A verb rather than something the app does at startup, and the distinction matters once
/// there is more than one instance. Migrating on boot means every instance in a rolling
/// deployment races the others to alter the same tables, and the old instances are still
/// serving against the new schema while it changes underneath them. A verb is a deployment
/// step: run it once, then start the instances. <see cref="EnsureSchemaIsCurrentAsync"/> is
/// the other half — it makes an instance that starts against an un-migrated database say so
/// instead of serving.
/// </para>
/// <para>
/// Unlike <c>seed</c> and <c>scale</c>, this runs in every environment. Applying migrations
/// to production is the whole point of it.
/// </para>
/// </remarks>
internal static class MigrateCommand
{
    /// <summary>The verb that triggers it.</summary>
    public const string Verb = "migrate";

    /// <summary>
    /// The lock two deployments would otherwise collide on.
    /// </summary>
    /// <remarks>
    /// An arbitrary constant, and it only has to be the same number in every copy of this
    /// program. PostgreSQL advisory locks are a single namespace shared by whatever asks,
    /// so the one risk is another application picking the same number — unlikely, and the
    /// cost would be one of them waiting rather than anything being corrupted.
    /// </remarks>
    private const long LockKey = 7_265_651_201;

    public static async Task<int> RunAsync(
        string[] args, IConfiguration configuration, IHostEnvironment environment)
    {
        var connection = configuration.GetConnectionString("Molargo");

        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.Error.WriteLine("Refused: ConnectionStrings:Molargo is not set.");

            return 1;
        }

        var checking = args.Contains("--check", StringComparer.OrdinalIgnoreCase);

        // MigrationsAssembly, and it is load-bearing. The context lives in
        // DYS.Molargo.Domain, which is provider-free on purpose, so EF looks for migrations
        // beside it and finds none — and "no migrations defined" and "no migrations
        // pending" are the same empty list.
        //
        // Without this line the verb reported "the schema is up to date" against a database
        // with no tables in it at all, which is the worst answer it could give: the next
        // step after it is to start the API.
        var options = new DbContextOptionsBuilder<MolargoDbContext>()
            .UseNpgsql(
                connection,
                npgsql => npgsql.MigrationsAssembly(
                    typeof(MigrateCommand).Assembly.FullName))
            .Options;

        await using var db = new MolargoDbContext(options);

        List<string> pending;

        try
        {
            pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        }
        catch (Exception ex)
        {
            // The connection, the credentials or the database itself. Said plainly here
            // because this verb is the first thing a deployment runs, so it is the first
            // place a wrong connection string shows up — and the raw provider exception
            // names a socket rather than a setting.
            Console.Error.WriteLine(
                $"Refused: could not reach the database to read its migration history. "
                + $"Check ConnectionStrings:Molargo. The provider said: {ex.Message}");

            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"Environment: {environment.EnvironmentName}");

        if (pending.Count == 0)
        {
            Console.WriteLine("The schema is up to date. Nothing to apply.");
            Console.WriteLine();

            return 0;
        }

        Console.WriteLine($"{pending.Count} migration(s) pending:");

        foreach (var migration in pending) Console.WriteLine($"  {migration}");

        Console.WriteLine();

        if (checking)
        {
            // A distinct code, so a deployment can branch on it. 1 is "something is wrong";
            // this is "nothing is wrong and there is work to do", which a pipeline gate
            // wants to tell apart from a bad connection string.
            Console.WriteLine("--check: nothing applied.");
            Console.WriteLine();

            return 2;
        }

        var watch = Stopwatch.StartNew();

        // Whether there is a database to lock against at all. EF answers "every migration
        // is pending" for one that does not exist — nothing is applied, which is true — so
        // the list above does not distinguish a first deployment from a stale one, and the
        // advisory lock below needs a database to be taken in.
        if (!await db.Database.CanConnectAsync())
        {
            Console.WriteLine("The database does not exist yet; creating it.");
            Console.WriteLine();

            try
            {
                // Unlocked, deliberately. There is nothing to lock in, and two first
                // deployments racing is not a case worth engineering for — PostgreSQL
                // refuses the second CREATE DATABASE anyway.
                await db.Database.MigrateAsync();

                watch.Stop();

                Console.WriteLine(
                    $"Created the database and applied {pending.Count} migration(s) "
                    + $"in {watch.Elapsed:mm\\:ss}.");
                Console.WriteLine();
                Console.WriteLine("It holds no clinic yet. In development, 'seed --demo' next.");
                Console.WriteLine();

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"Could not create the database: {ex.Message}");
                Console.Error.WriteLine();

                return 1;
            }
        }

        // Opened by hand and held for the whole migration. A session-level advisory lock
        // lives on its connection, so taking it on a connection EF then closes would
        // release it immediately and guard nothing.
        var guard = db.Database.GetDbConnection();

        try
        {
            await guard.OpenAsync();

            Console.WriteLine("Waiting for the migration lock…");

            await db.Database.ExecuteSqlRawAsync($"select pg_advisory_lock({LockKey});");

            // Re-read inside the lock. Another deployment may have been part way through
            // when this one asked, in which case the list above is already stale and
            // applying it would try to create what that one has just created.
            var stillPending = (await db.Database.GetPendingMigrationsAsync()).ToList();

            if (stillPending.Count == 0)
            {
                Console.WriteLine(
                    "Another deployment applied them while this one waited. Nothing to do.");
                Console.WriteLine();

                return 0;
            }

            await db.Database.MigrateAsync();

            watch.Stop();

            Console.WriteLine($"Applied {stillPending.Count} migration(s) in {watch.Elapsed:mm\\:ss}.");
            Console.WriteLine();

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Migration failed: {ex.Message}");
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                "The database is wherever the failing migration left it. Read the error "
                + "above before running this again — a migration that failed half way is "
                + "not made right by a second attempt.");
            Console.Error.WriteLine();

            return 1;
        }
        finally
        {
            // Released explicitly rather than left to the connection closing. It would be
            // released either way, but a lock whose lifetime depends on when a connection
            // happens to be disposed is one nobody can reason about.
            //
            // Swallowed, and that is the point of the try: this runs while an exception may
            // already be on its way out, and a failure to unlock would replace the real
            // error with a meaningless one. The lock is released by the session ending
            // regardless.
            try
            {
                if (guard.State == System.Data.ConnectionState.Open)
                {
                    await db.Database.ExecuteSqlRawAsync(
                        $"select pg_advisory_unlock({LockKey});");

                    await guard.CloseAsync();
                }
            }
            catch
            {
                // Nothing useful to say and nowhere useful to say it.
            }
        }
    }

    /// <summary>
    /// Refuses to start against a database the code is ahead of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other half of the verb. Without this, forgetting to migrate produces an app that
    /// starts, serves most screens correctly, and throws <c>column does not exist</c> on
    /// whichever one reaches the new column — at which point the symptom is a broken screen
    /// and the cause is a deployment step, which is a long way apart.
    /// </para>
    /// <para>
    /// One query, once, at startup. It costs a round trip on boot and turns a class of
    /// confusing runtime failures into a message naming the command to run.
    /// </para>
    /// <para>
    /// It does not migrate. An instance that fixed the schema itself on boot is the racing
    /// rolling deployment the verb exists to avoid.
    /// </para>
    /// </remarks>
    public static async Task EnsureSchemaIsCurrentAsync(IServiceProvider services)
    {
        var contexts = services.GetRequiredService<IDbContextFactory<MolargoDbContext>>();

        await using var db = await contexts.CreateDbContextAsync();

        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();

        if (pending.Count == 0)
        {
            // An empty pending list means either "up to date" or "this context can see no
            // migrations at all", and the second is a misconfiguration that looks exactly
            // like health. Asserted here because this is the check everything else trusts.
            if (db.Database.GetMigrations().Any()) return;

            throw new InvalidOperationException(
                "No migrations are visible to this build, so the schema cannot be checked. "
                + "The context is in DYS.Molargo.Domain and the migrations are in the API, "
                + "so the registration must name the migrations assembly — see the "
                + "AddDbContextFactory call in Program.cs.");
        }

        throw new InvalidOperationException(
            $"The database is {pending.Count} migration(s) behind this build "
            + $"({string.Join(", ", pending)}). Run 'dotnet run --project DYS.Molargo.API "
            + $"-- {Verb}' against this connection string before starting the API. Starting "
            + "anyway would serve most screens correctly and fail on whichever one reaches "
            + "the new schema first.");
    }
}
