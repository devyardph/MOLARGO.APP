using System.Diagnostics;
using DYS.Molargo.Domain.Data;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Api.Infrastructure;

/// <summary>
/// Grows the demo clinic to a realistic size, so a slow screen can be seen rather than
/// reasoned about.
/// </summary>
/// <remarks>
/// <para>
/// Every read fixed so far was verified against the demo clinic's twenty-five patients,
/// where a whole-table scan and a paged query are indistinguishable — both return instantly
/// and both look correct. That is the wrong size to check performance work at, and it is
/// also the wrong size to check correctness at: a date boundary off by an hour moves no row
/// when there are only ten.
/// </para>
/// <para>
/// This adds patients on top of the existing clinic rather than replacing it, so the demo
/// records the self-test asserts against stay exactly where they were.
/// </para>
/// <para>
/// Development only, like the seed. It writes a great many rows, and the connection string
/// is whatever the environment says.
/// </para>
/// </remarks>
internal static class ScaleCommand
{
    public const string Verb = "scale";

    /// <summary>
    /// Rows per insert batch.
    /// </summary>
    /// <remarks>
    /// EF tracks everything it has added until SaveChanges, so one batch for a hundred
    /// thousand patients is a change tracker holding a hundred thousand entities plus an
    /// original-values snapshot of each. Saving in blocks and clearing the tracker between
    /// them is the difference between a seed that finishes and one that runs out of memory —
    /// the same lesson the sync reference records about pulling pages.
    /// </remarks>
    private const int Batch = 2_000;

    public static async Task<int> RunAsync(
        string[] args, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
        {
            Console.Error.WriteLine("Refused: scale runs in Development only.");

            return 1;
        }

        var connection = configuration.GetConnectionString("Molargo");

        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.Error.WriteLine("Refused: no ConnectionStrings:Molargo configured.");

            return 1;
        }

        var wanted = int.TryParse(Argument(args, "--patients"), out var parsed) ? parsed : 100_000;

        var options = new DbContextOptionsBuilder<MolargoDbContext>()
            .UseNpgsql(connection)
            .Options;

        await using var db = new MolargoDbContext(options);

        // Filters bypassed throughout: this context has no tenant, and finding the clinic is
        // what establishes which one to grow.
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync();

        if (tenant is null)
        {
            Console.Error.WriteLine("Refused: no clinic. Run 'seed --demo' first.");

            return 1;
        }

        var site = await db.PracticeLocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.TenantId == tenant.Id);

        var provider = await db.Providers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.TenantId == tenant.Id);

        if (site is null || provider is null)
        {
            Console.Error.WriteLine("Refused: the clinic has no site or no staff.");

            return 1;
        }

        var existing = await db.Patients.IgnoreQueryFilters()
            .CountAsync(row => row.TenantId == tenant.Id);

        Console.WriteLine();
        Console.WriteLine($"Growing {tenant.Name} from {existing:N0} patients to {wanted:N0}.");
        Console.WriteLine("  (added alongside the demo records, which stay as they are)");
        Console.WriteLine();

        var clock = new SystemClock();
        var now = clock.UtcNow;
        var random = new Random(20261004);
        var watch = Stopwatch.StartNew();
        var added = 0;

        for (var start = existing; start < wanted; start += Batch)
        {
            var count = Math.Min(Batch, wanted - start);
            var patients = new List<PatientEntity>(count);
            var appointments = new List<Appointment>(count * 2);

            for (var i = 0; i < count; i++)
            {
                var number = start + i;

                var patient = new PatientEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.Id,
                    PracticeLocationId = site.Id,
                    FirstName = FirstNames[number % FirstNames.Length],
                    LastName = $"{LastNames[number % LastNames.Length]}{number}",
                    PatientNumber = (900_000 + number).ToString(),
                    DateOfBirth = new DateOnly(1950 + (number % 60), 1 + (number % 12), 1 + (number % 28)),
                    Mobile = $"04{number % 100_000_000:D8}",
                    Status = PatientStatus.Active,
                    CreatedUtc = now,
                    UpdatedUtc = now,
                };

                patients.Add(patient);

                // Two visits each: one in the past, one further back. Enough that the
                // first-visit calculation and the diary both have history to walk, which is
                // the shape that made the report slow.
                for (var visit = 0; visit < 2; visit++)
                {
                    appointments.Add(new Appointment
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenant.Id,
                        PatientId = patient.Id,
                        PracticeLocationId = site.Id,
                        ProviderId = provider.Id,
                        StartUtc = now.AddDays(-random.Next(1, 900)).AddHours(random.Next(8, 17)),
                        DurationMinutes = 30,
                        Status = AppointmentStatus.Completed,
                        CreatedUtc = now,
                        UpdatedUtc = now,
                    });
                }
            }

            db.Patients.AddRange(patients);
            db.Appointments.AddRange(appointments);

            await db.SaveChangesAsync();

            // Without this the tracker grows for the whole run and every later batch gets
            // slower as DetectChanges re-scans everything already saved.
            db.ChangeTracker.Clear();

            added += count;

            Console.Write($"\r  {added:N0} patients, {added * 2:N0} appointments — {watch.Elapsed:mm\\:ss}");
        }

        watch.Stop();

        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine($"Done in {watch.Elapsed:mm\\:ss}.");
        Console.WriteLine();
        Console.WriteLine("Now run the API and 'dotnet run --project DYS.Molargo.SelfTest',");
        Console.WriteLine("which times each screen. A whole-table read shows up here and");
        Console.WriteLine("nowhere else.");
        Console.WriteLine();

        return 0;
    }

    private static readonly string[] FirstNames =
        ["Ava", "Noah", "Mia", "Liam", "Zoe", "Ethan", "Ruby", "Leo", "Ivy", "Max"];

    private static readonly string[] LastNames =
        ["Nguyen", "Smith", "Patel", "Brown", "Wilson", "Tran", "Clarke", "Singh"];

    private static string? Argument(string[] args, string name)
    {
        var at = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }
}
