using System.Security.Cryptography;
using DYS.Molargo.Domain.Data;
using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace DYS.Molargo.Api.Infrastructure;

/// <summary>
/// Puts a first clinic and a first user into an empty server database.
/// </summary>
/// <remarks>
/// <para>
/// Without this the API cannot be tested at all. The device creates and seeds its own file
/// on first run; a server database is created by a migration and is then <em>empty</em>, so
/// there is no clinic to name at sign-in and no user to be — and every endpoint correctly
/// answers 401 forever.
/// </para>
/// <para>
/// A command-line verb, not an endpoint. Something that creates an account with full
/// ownership of a practice should not be reachable over HTTP at all, whatever it is guarded
/// by — the guard is the one thing that can be misconfigured.
/// </para>
/// </remarks>
public static class SeedCommand
{
    /// <summary>The verb that triggers it.</summary>
    public const string Verb = "seed";

    public static async Task<int> RunAsync(
        string[] args, IConfiguration configuration, IHostEnvironment environment)
    {
        // Refused outside development. The connection string is whatever the environment
        // says, and that could be production — a seeder that ran there would put an account
        // nobody created into a live practice's database.
        //
        // Asked of the host rather than read out of configuration by key. The host is what
        // every other part of the app means by "the environment", and a second way of
        // working it out is a second answer waiting to disagree.
        if (!environment.IsDevelopment())
        {
            Console.Error.WriteLine(
                "Refused: seeding runs in Development only, and the environment is "
                + $"{environment.EnvironmentName}. Set ASPNETCORE_ENVIRONMENT=Development "
                + "if this really is "
                + "a development database.");

            return 1;
        }

        var connection = configuration.GetConnectionString("Molargo");

        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.Error.WriteLine("Refused: ConnectionStrings:Molargo is not set.");

            return 1;
        }

        // --demo seeds the app's own demo clinic, which brings its own code, its own staff
        // and its own shared password. Honouring --clinic or --user alongside it would be
        // offering a choice the seed does not take.
        if (args.Contains("--demo", StringComparer.OrdinalIgnoreCase))
        {
            return await SeedDemoClinicAsync(connection);
        }

        var clinicCode = Argument(args, "--clinic") ?? "molargo-dev";
        var practiceName = Argument(args, "--name") ?? "Molargo Development Clinic";
        var username = Argument(args, "--user") ?? "owner";
        var country = Argument(args, "--country") ?? "AU";

        var options = new DbContextOptionsBuilder<MolargoDbContext>()
            .UseNpgsql(connection)
            .Options;

        await using var db = new MolargoDbContext(options);

        if (!await db.Database.CanConnectAsync())
        {
            Console.Error.WriteLine(
                "Refused: cannot reach the database. Is PostgreSQL running, and has "
                + "'dotnet ef database update' been run?");

            return 1;
        }

        // IgnoreQueryFilters throughout: there is no tenant yet, and the context's filters
        // compare against Guid.Empty — which matches nothing, so an existing clinic would
        // look absent and be created a second time.
        if (await db.Tenants.IgnoreQueryFilters().AnyAsync(row => row.Slug == clinicCode))
        {
            Console.Error.WriteLine(
                $"Refused: a clinic with the code \"{clinicCode}\" already exists. "
                + "Pass --clinic with a different code.");

            return 1;
        }

        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var tenantId = Guid.NewGuid();

        var tenant = new Tenant
        {
            Id = tenantId,
            TenantId = tenantId,
            Name = practiceName,
            Slug = clinicCode,
            CountryCode = country,
            CurrencyCode = PracticeCurrency.ForCountry(country),
            SubscribedOn = today,
            TrialEndsOn = today.AddDays(29),
            TermsAcceptedUtc = now,
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        var site = new PracticeLocation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = practiceName,
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        var chair = new Operatory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PracticeLocationId = site.Id,
            Name = "Chair 1",
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        // Generated, never taken as an argument. A password typed on a command line is a
        // password in the shell history — and this one owns the whole practice.
        var password = GeneratePassword();

        var owner = new Provider
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FirstName = "Development",
            LastName = "Owner",
            DisplayName = "Development Owner",
            Role = ProviderRole.Dentist,
            IsOwner = true,
            Username = username,
            PasswordHash = new PasswordHasher().Hash(password),
            PasswordUpdatedUtc = now,
            PrimaryLocationId = site.Id,
            LicenceNumber = "DEV-0001",
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        db.Tenants.Add(tenant);
        db.PracticeLocations.Add(site);
        db.Operatories.Add(chair);
        db.Providers.Add(owner);

        await db.SaveChangesAsync();

        Console.WriteLine();
        Console.WriteLine("Seeded a development clinic.");
        Console.WriteLine();
        Console.WriteLine($"  clinic code : {clinicCode}");
        Console.WriteLine($"  username    : {username}");
        Console.WriteLine($"  password    : {password}");
        Console.WriteLine();
        Console.WriteLine("Shown once and stored only as a hash. Run the verb again with a");
        Console.WriteLine("different --clinic if you lose it.");
        Console.WriteLine();

        return 0;
    }

    /// <summary>
    /// The app's own demo clinic — the same records the device seeds on first run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SampleData"/>, not a second set written for the server. It used to be two
    /// patients invented here, on the grounds that the real one lived in
    /// DYS.Molargo.Shared which this project cannot reference. The seed moved to
    /// DYS.Molargo.Domain.Data instead, so both sides run the same one — which is the whole point:
    /// a second demo set drifts from the first, and then a bug reproduces on one and not
    /// the other.
    /// </para>
    /// <para>
    /// It brings patients, appointments, invoices, payments, clinical notes, the chart,
    /// perio, prescriptions, referrals, stock, lab cases and sterilisation — enough that
    /// every read endpoint returns something, which is what tells a working query from one
    /// that returns an empty list because it is wrong.
    /// </para>
    /// </remarks>
    private static async Task<int> SeedDemoClinicAsync(string connection)
    {
        var options = new DbContextOptionsBuilder<MolargoDbContext>()
            .UseNpgsql(connection)
            .Options;

        await using var db = new MolargoDbContext(options);

        if (!await db.Database.CanConnectAsync())
        {
            Console.Error.WriteLine(
                "Refused: cannot reach the database. Is PostgreSQL running, and has "
                + "'dotnet ef database update' been run?");

            return 1;
        }

        // SampleData's own guard is "are there patients already", which it checks through
        // the tenant filter — and this context has no tenant, so the filter would hide
        // them and it would seed a second copy of everything. Asked without the filter
        // here instead.
        if (await db.Patients.IgnoreQueryFilters().AnyAsync())
        {
            Console.Error.WriteLine(
                "Refused: this database already holds patients. The demo clinic seeds once "
                + "into an empty database — drop and re-create it, or use the plain seed "
                + "verb to add another clinic alongside.");

            return 1;
        }

        var tenantId = Guid.NewGuid();

        await SampleData
            .SeedAsync(db, new SystemClock(), tenantId, new PasswordHasher(), CancellationToken.None)
            .ConfigureAwait(false);

        // Read back rather than restated. The clinic's code and its staff are SampleData's
        // to decide, and a second copy of them here would be wrong the first time it
        // changed.
        var clinic = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(row => row.Id == tenantId);

        var staff = await db.Providers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.Username != null)
            .OrderByDescending(row => row.IsOwner)
            .ThenBy(row => row.LastName)
            .ToListAsync();

        var patients = await db.Patients.IgnoreQueryFilters().CountAsync(p => p.TenantId == tenantId);
        var appointments = await db.Appointments.IgnoreQueryFilters().CountAsync(a => a.TenantId == tenantId);

        Console.WriteLine();
        Console.WriteLine($"Seeded the demo clinic: {clinic.Name}");
        Console.WriteLine();
        Console.WriteLine($"  clinic code : {clinic.Slug}");
        Console.WriteLine($"  password    : {SampleData.DemoPassword}   (the same for everyone)");
        Console.WriteLine();
        Console.WriteLine("  sign in as:");

        foreach (var person in staff)
        {
            var role = person.IsOwner ? $"{person.Role}, owner" : person.Role.ToString();

            Console.WriteLine($"    {person.Username,-12} {person.FullName,-22} {role}");
        }

        Console.WriteLine();
        Console.WriteLine($"  {patients} patients, {appointments} appointments, plus invoices,");
        Console.WriteLine("  notes, charting, prescriptions, stock and lab cases.");
        Console.WriteLine();
        Console.WriteLine("The same records the desktop app seeds on first run — one seed, so a");
        Console.WriteLine("bug reproduces on both or neither.");
        Console.WriteLine();

        return 0;
    }

    private static void AddMinimalDemoData(
        MolargoDbContext db, Guid tenantId, Guid siteId, Guid providerId, DateTime now)
    {
        var type = new AppointmentType
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Exam & clean",
            DefaultDurationMinutes = 45,
            RecallIntervalMonths = 6,
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        db.AppointmentTypes.Add(type);

        var people = new[]
        {
            ("Margaret", "Yuen", "0400 111 222"),
            ("Dan", "Marsh", "0400 333 444"),
        };

        var day = now.Date.AddDays(1).AddHours(9);

        foreach (var (first, last, mobile) in people)
        {
            var patient = new Domain.Entities.Patient
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                FirstName = first,
                LastName = last,
                Mobile = mobile,
                PracticeLocationId = siteId,
                DateOfBirth = new DateOnly(1980, 1, 1),
                CreatedUtc = now,
                UpdatedUtc = now,
            };

            db.Patients.Add(patient);

            db.Appointments.Add(new Appointment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PatientId = patient.Id,
                PracticeLocationId = siteId,
                ProviderId = providerId,
                AppointmentTypeId = type.Id,

                // Kind=Utc, and it matters: Npgsql maps DateTime to timestamptz and throws
                // on Kind=Unspecified. A seed that got this wrong would fail at the first
                // SaveChanges rather than at the first read, which is the better place.
                StartUtc = DateTime.SpecifyKind(day, DateTimeKind.Utc),
                DurationMinutes = 45,
                Status = AppointmentStatus.Scheduled,
                Reason = $"{first}'s check-up",
                CreatedUtc = now,
                UpdatedUtc = now,
            });

            day = day.AddHours(1);
        }
    }

    /// <summary>A password worth typing once and not guessing.</summary>
    /// <remarks>
    /// From <see cref="RandomNumberGenerator"/>, not <c>Random</c>. This one signs in as the
    /// owner of a practice, and a predictable sequence is the kind of shortcut that survives
    /// into an environment somebody can reach.
    /// </remarks>
    private static string GeneratePassword()
    {
        // No look-alike characters. The password is read off a console and typed into a
        // request by hand, and l/1/I and O/0 are the pairs that waste the first attempt.
        const string alphabet = "abcdefghijkmnpqrstuvwxyzACDEFGHJKLMNPQRSTUVWXYZ23456789";

        return RandomNumberGenerator.GetString(alphabet, 20);
    }

    private static string? Argument(string[] args, string name)
    {
        var index = Array.FindIndex(
            args, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
