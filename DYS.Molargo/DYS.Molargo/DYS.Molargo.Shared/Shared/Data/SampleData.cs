using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Services;
using Microsoft.EntityFrameworkCore;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Data;

/// <summary>
/// First-run sample data: two locations, a handful of providers, and the patient list from
/// the prototype in designs/molargo.html.
/// </summary>
/// <remarks>
/// <para>
/// Real-looking data rather than "Test Patient 1". A list of placeholder names hides
/// exactly the problems this screen has — a surname column too narrow for "Fernandez", a
/// balance column that only ever shows $0, a status filter with nothing to filter.
/// </para>
/// <para>
/// Ids are deterministic, derived from a fixed namespace GUID and the row's own key, so
/// re-seeding a rebuilt database produces the same ids. Random ids would make every
/// rebuild look like a different practice, and nothing referencing a seeded row by id
/// would survive.
/// </para>
/// <para>
/// Written through the <c>DbContext</c> directly rather than through
/// <c>IRepository&lt;T&gt;</c>: seeding runs from inside <c>MolargoDatabase</c>'s own
/// initialisation, and a repository call from there would re-enter the initialisation gate
/// and deadlock.
/// </para>
/// </remarks>
internal static partial class SampleData
{
    /// <summary>
    /// Namespace for the deterministic ids below. An arbitrary constant, fixed forever:
    /// changing it renumbers every seeded row, which orphans anything that referenced one.
    /// </summary>
    private static readonly Guid Namespace = new("6d0f2a48-1c5e-4f2b-9a71-3f8c5b2e7d10");

    /// <param name="tenantId">
    /// The clinic every seeded row belongs to, minted by <c>MolargoDatabase</c> for this
    /// installation. Passed in rather than generated here: it is already recorded in local
    /// metadata by the time seeding runs, and a second one would leave the whole sample
    /// practice invisible behind the tenant filter.
    /// </param>
    public static async Task SeedAsync(
        MolargoDbContext db,
        IClock clock,
        Guid tenantId,
        IPasswordHasher hasher,
        CancellationToken ct)
    {
        // Guard against a partially-seeded file: if patients are already present, this has
        // run before and re-running would duplicate every row under new ids.
        if (await db.Patients.AnyAsync(ct).ConfigureAwait(false)) return;

        var now = clock.UtcNow;

        db.Tenants.Add(Clinic(tenantId, clock.Today));
        db.PracticeLocations.AddRange(Locations());
        db.Operatories.AddRange(Operatories());
        db.Providers.AddRange(Providers());
        db.AppointmentTypes.AddRange(AppointmentTypes());
        db.ProcedureCodes.AddRange(ProcedureCodes());
        db.ProcedureCodeFees.AddRange(SiteFees());
        db.Patients.AddRange(Patients());

        // The diary is seeded relative to the clock, not to a fixed date: the front desk
        // screen is "today", and a hard-coded August 2026 day would leave it permanently
        // empty. Everything else above is date-independent.
        db.Appointments.AddRange(TodaysAppointments(clock.Today));
        db.WaitlistEntries.AddRange(Waitlist(clock.Today));
        db.PracticeTasks.AddRange(Tasks(clock.Today));

        // One patient's record filled in properly — see SampleData.Records.cs. Depth on
        // a single record beats a shallow row for all 25: the record screen's six tabs
        // are only exercised by a patient who actually has alerts, plans, documents,
        // invoices and a comms history.
        AddRecordFor(db, clock.Today);

        foreach (var entry in db.ChangeTracker.Entries<EntityBase>())
        {
            // Stamped in one pass rather than on each factory call, so the whole seed
            // shares one timestamp and the audit dates are not spread over the
            // milliseconds the seed happened to take.
            entry.Entity.CreatedUtc = now;
            entry.Entity.UpdatedUtc = now;

            // And the clinic, for the same reason — one pass beats threading a tenant id
            // through forty factory methods, where the one that got missed would produce a
            // row the app can never read again.
            entry.Entity.TenantId = tenantId;
        }

        // After the tenant pass, so the usernames belong to staff that already carry a
        // clinic — a username is only unique within one.
        AlignCredentials(db, hasher, now);

        // The vendor's own tenant, added last and stamped by hand. It has to come after
        // the pass above, which sets every tracked row to the clinic's id: run before it,
        // the platform tenant and its operator would have been quietly moved into the
        // clinic, and a practice's Users screen would then list the vendor's super admin.
        AddPlatform(db, hasher, now, clock.Today);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The vendor's tenant and its one super admin.
    /// </summary>
    /// <remarks>
    /// A second tenant in the same file, which is what makes the tenant boundary
    /// demonstrable rather than theoretical: signing in as the clinic shows none of this,
    /// and signing in as the super admin shows none of the clinic's patients.
    /// </remarks>
    private static void AddPlatform(
        MolargoDbContext db, IPasswordHasher hasher, DateTime now, DateOnly today)
    {
        var platformId = Id("tenant:molargo-platform");

        db.Tenants.Add(new Tenant
        {
            Id = platformId,
            TenantId = platformId,
            Name = "Molargo (vendor)",
            Slug = PlatformSlug,
            ContactEmail = "support@molargo.example",
            IsPlatform = true,
            IsActive = true,
            SubscribedOn = today.AddYears(-3),
            CreatedUtc = now,
            UpdatedUtc = now,
        });

        db.Providers.Add(new Provider
        {
            Id = Id("provider:platform-owner"),
            TenantId = platformId,
            FirstName = "Sam",
            LastName = "Devyard",
            DisplayName = "Sam Devyard",
            Role = ProviderRole.SuperAdmin,
            Email = "support@molargo.example",
            IsActive = true,

            // The same demo password as the clinic's staff. A separate one would be a
            // second secret to pass around out of band for no gain — neither is a
            // credential that should exist outside a demonstration.
            Username = "superadmin",
            PasswordHash = hasher.Hash(DemoPassword),
            PasswordUpdatedUtc = now,
            CreatedUtc = now,
            UpdatedUtc = now,
        });

        foreach (var plan in Plans(platformId, now)) db.Plans.Add(plan);

        // The seeded clinic goes on the Australian Practice plan, so the vendor's screens
        // open with a real subscription rather than a list of prices nobody is on.
        // Materialised before the loop, and that ToList is load-bearing.
        //
        // The body adds rows to the context, and adding to a DbSet mutates the change
        // tracker — so iterating it lazily throws "collection was modified" part way
        // through. The seed then rolled back and the app came up with an empty database
        // that let nobody sign in, which is a long way from the line that caused it.
        var clinics = db.ChangeTracker.Entries<Tenant>()
            .Select(entry => entry.Entity)
            .Where(entry => !entry.IsPlatform)
            .ToList();

        foreach (var clinic in clinics)
        {
            clinic.PlanId = PlanId("practice", "AU");

            foreach (var charge in Charges(clinic.Id, now))
            {
                db.SubscriptionCharges.Add(charge);
            }
        }
    }

    /// <summary>
    /// Three months of subscription charges for the seeded clinic.
    /// </summary>
    /// <remarks>
    /// Two paid and the current one failed, so the billing table opens on the state that
    /// actually needs working through rather than a page of green ticks. The amount matches
    /// what the Practice plan prices this clinic at — two sites, three clinicians — because
    /// a seeded figure that disagreed with the live quote would look like a bug in the
    /// pricing rule.
    /// </remarks>
    private static IEnumerable<SubscriptionCharge> Charges(Guid tenantId, DateTime now)
    {
        var today = DateOnly.FromDateTime(now.ToLocalTime());
        var thisMonth = new DateOnly(today.Year, today.Month, 1);

        // 329 base + 229 for the second site. Three clinicians against eight included, so
        // no seat charge — see the Practice plan in Plans().
        const decimal monthly = 558m;

        for (var back = 2; back >= 0; back--)
        {
            var start = thisMonth.AddMonths(-back);
            var raised = now.AddMonths(-back);
            var current = back == 0;

            yield return new SubscriptionCharge
            {
                Id = Id($"charge:{tenantId:N}:{start:yyyy-MM}"),
                TenantId = tenantId,
                PlanId = PlanId("practice", "AU"),
                PlanName = "Practice",
                PeriodStart = start,
                PeriodEnd = start.AddMonths(1).AddDays(-1),
                Amount = monthly,
                CurrencyCode = "AUD",
                Sites = 2,
                Clinicians = 3,
                Status = current ? ChargeStatus.Failed : ChargeStatus.Paid,
                AttemptedUtc = raised.AddDays(1),
                SettledUtc = current ? null : raised.AddDays(1),
                FailureReason = current
                    ? "Card declined — expired. Practice manager notified by phone."
                    : null,
                Reference = current ? null : $"EFT-{start:yyyyMM}-4471",
                CreatedUtc = raised,
                UpdatedUtc = raised.AddDays(1),
            };
        }
    }

    private static Guid PlanId(string code, string country) => Id($"plan:{country}:{code}");

    /// <summary>
    /// The vendor's price list — three plans, in three countries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three countries rather than one because per-country pricing is the point: the same
    /// plan carries an independently-decided price in each, and a screen that only ever
    /// showed Australia would not demonstrate the thing it exists for. The figures are not
    /// currency conversions — GBP is a UK market price, not AUD divided by two.
    /// </para>
    /// <para>
    /// The shape is a base per subscription, sites beyond what it covers, and clinician
    /// seats beyond the allowance. Group expresses per-site pricing by setting its base and
    /// its extra-site price to the same figure, which needs no separate concept: two sites
    /// is simply the base twice.
    /// </para>
    /// </remarks>
    private static IEnumerable<Plan> Plans(Guid platformId, DateTime now)
    {
        // code, name, order, included sites, seats a site, base, extra site, extra seat
        var shapes = new (string Code, string Name, int Order, int Sites, int Seats)[]
        {
            ("solo", "Solo", 0, 1, 1),
            ("practice", "Practice", 1, 1, 4),
            ("group", "Group", 2, 1, 4),
        };

        var prices = new (string Country, string Currency, decimal[] Base, decimal[] Site, decimal[] Seat)[]
        {
            ("AU", "AUD", [149m, 329m, 279m], [0m, 229m, 279m], [69m, 69m, 59m]),
            ("NZ", "NZD", [169m, 369m, 315m], [0m, 259m, 315m], [79m, 79m, 69m]),
            ("GB", "GBP", [89m, 199m, 169m], [0m, 139m, 169m], [39m, 39m, 35m]),

            // The Philippines, priced for the local market rather than converted. A
            // straight exchange from the Australian figures would put Solo near ₱8,000,
            // which is far above what a Philippine practice pays for software — so these
            // sit at roughly a fifth of that. The shape is identical; only the numbers are
            // local, which is the entire point of a plan per country.
            ("PH", "PHP", [1490m, 3290m, 2790m], [0m, 2290m, 2790m], [690m, 690m, 590m]),
        };

        foreach (var (country, currency, bases, sites, seats) in prices)
        {
            for (var index = 0; index < shapes.Length; index++)
            {
                var shape = shapes[index];

                yield return new Plan
                {
                    Id = PlanId(shape.Code, country),
                    TenantId = platformId,
                    Code = shape.Code,
                    Name = shape.Name,
                    CountryCode = country,
                    CurrencyCode = currency,
                    MonthlyBase = bases[index],
                    IncludedSites = shape.Sites,
                    IncludedSeatsPerSite = shape.Seats,
                    PricePerExtraSite = sites[index],
                    PricePerExtraSeat = seats[index],

                    // Pay for ten, get twelve — the standard prepay lever, and worth
                    // having before sync infrastructure costs land.
                    AnnualMonthsCharged = 10,
                    DisplayOrder = shape.Order,
                    IsActive = true,
                    CreatedUtc = now,
                    UpdatedUtc = now,
                };
            }
        }
    }

    /// <summary>The vendor tenant's sign-in code.</summary>
    internal const string PlatformSlug = "molargo";

    /// <summary>
    /// The subscribing clinic itself — the tenant every other seeded row hangs off.
    /// </summary>
    /// <remarks>
    /// Its own <c>TenantId</c> is its <c>Id</c>, set by the pass above. The tenant table is
    /// not filtered by tenant, so the value is never read as a filter; it is set anyway so
    /// the column is never a surprising zero.
    /// </remarks>
    private static Tenant Clinic(Guid tenantId, DateOnly today) => new()
    {
        Id = tenantId,
        TenantId = tenantId,
        Name = "Molargo Dental Group",
        Slug = "molargo-dental",
        Abn = "51 824 753 556",
        ContactEmail = "practice@molargo.example",
        ContactPhone = "(02) 9000 1200",

        // Recorded, not enforced. Nothing checks it — there is no licence server — and the
        // admin screen's Plan tab says so.
        CountryCode = "AU",

        // The plan is attached after the platform's own rows are seeded, since it is one
        // of them — see AddPlatform.
        SubscribedOn = today.AddMonths(-19),
    };

    // ---- practice --------------------------------------------------------

    private static Guid SydneyCbd => Id("location:sydney-cbd");

    private static Guid Newtown => Id("location:newtown");

    private static IEnumerable<PracticeLocation> Locations()
    {
        yield return new PracticeLocation
        {
            Id = SydneyCbd,
            Name = "Molargo Dental — Sydney CBD",
            ShortName = "Sydney CBD",
            DisplayOrder = 0,
            AddressLine = "Level 3, 210 Pitt St",
            Suburb = "Sydney",
            State = "NSW",
            Postcode = "2000",
            Phone = "(02) 9000 1200",
            Email = "cbd@molargo.example",
            Abn = "51 824 753 556",
            TimeZoneId = "Australia/Sydney",
        };

        yield return new PracticeLocation
        {
            Id = Newtown,
            Name = "Molargo Dental — Newtown",
            ShortName = "Newtown",
            DisplayOrder = 1,
            AddressLine = "88 King St",
            Suburb = "Newtown",
            State = "NSW",
            Postcode = "2042",
            Phone = "(02) 9000 1300",
            Email = "newtown@molargo.example",
            Abn = "51 824 753 556",
            TimeZoneId = "Australia/Sydney",
        };
    }

    /// <summary>
    /// The three chairs the prototype's diary shows, in its column order.
    /// </summary>
    private static IEnumerable<Operatory> Operatories()
    {
        yield return Operatory("chair-1", "Chair 1", order: 0, surgical: false);
        yield return Operatory("chair-2", "Chair 2", order: 1, surgical: true);
        yield return Operatory("chair-3", "Chair 3", order: 2, surgical: false);
    }

    private static Operatory Operatory(string key, string name, int order, bool surgical) =>
        new()
        {
            Id = Id($"operatory:{key}"),
            PracticeLocationId = SydneyCbd,
            Name = name,
            DisplayOrder = order,
            IsSurgical = surgical,
        };

    /// <summary>
    /// The clinicians named in the prototype's diary: Dr Vance, Dr Ellery and H. Ito on
    /// hygiene, plus the front desk. Dr Osman appears in the prototype too, but as the
    /// external periodontist a referral is sent to rather than as staff here.
    /// </summary>
    private static IEnumerable<Provider> Providers()
    {
        // Every clinician carries an AHPRA registration as well as a Medicare provider
        // number. They are different things and both are needed: the provider number
        // bills, the registration signs. Without the registration a medical certificate
        // is refused, which is how the missing field first showed up.
        //
        // Three letters and ten digits, which is the real format. These were seeded seven
        // digits short, and the admin screen's format check refused every one of them —
        // a validation rule and its own sample data disagreeing.
        // The owner, and a dentist — which is the normal case and the whole reason
        // ownership is not a value of ProviderRole. She keeps the diary column, the
        // clinical authorship and the provider number, and runs the practice's settings
        // on top of them.
        yield return Provider("vance", "Rachel", "Vance", "Dr Vance",
            ProviderRole.Dentist, "4419721A", "DEN0001234567", "#ec3013",
            isOwner: true);

        yield return Provider("ellery", "James", "Ellery", "Dr Ellery",
            ProviderRole.Dentist, "4521883B", "DEN0004417891", "#e15b47");

        // Granted one area without being an owner — the middle case, and a realistic one:
        // the hygienist runs the stock at plenty of practices. Seeded so the permission
        // model ships with an example of a grant, not only of ownership and of nothing.
        yield return Provider("ito", "Haruka", "Ito", "H. Ito",
            ProviderRole.Hygienist, "4633910C", "DEH0009120334", "#7d7979",
            permissions: PracticePermissions.ManageInventory);

        // No registration: she is not a clinician, and the certificate guard has to be
        // able to tell the difference.
        //
        // And no administrative permission either, deliberately. She runs the front desk,
        // which is the case the permission model exists to restrict — so the seed ships
        // both sides of the rule rather than only the permitted one.
        yield return Provider("brennan", "Cathy", "Brennan", "Cathy Brennan",
            ProviderRole.Administration, providerNumber: null, ahpraNumber: null, "#444141");
    }

    private static Provider Provider(
        string key,
        string firstName,
        string lastName,
        string displayName,
        ProviderRole role,
        string? providerNumber,
        string? ahpraNumber,
        string colour,
        bool isOwner = false,
        PracticePermissions permissions = PracticePermissions.None) =>
        new()
        {
            Id = Id($"provider:{key}"),
            FirstName = firstName,
            LastName = lastName,
            DisplayName = displayName,
            Role = role,
            IsOwner = isOwner,
            Permissions = permissions,
            ProviderNumber = providerNumber,
            AhpraNumber = ahpraNumber,
            Email = $"{key}@molargo.example",
            PrimaryLocationId = SydneyCbd,
            DiaryColour = colour,
        };

    /// <summary>
    /// The colours are the prototype's own diary swatches, carried on the type rather
    /// than on each appointment: the prototype hard-codes a colour per row, but what it
    /// is actually encoding is the kind of visit — accent for anything operative, mid
    /// grey for routine, dark for a consult.
    /// </summary>
    private static IEnumerable<AppointmentType> AppointmentTypes()
    {
        yield return AppointmentType("exam", "Exam & clean", 45, Neutral500, online: true);
        yield return AppointmentType("hygiene", "Scale & polish", 40, Neutral500, online: true);
        yield return AppointmentType("consult", "Implant consult", 20, Neutral800, online: true);
        yield return AppointmentType("crown-prep", "Crown prep", 60, Accent, online: false);
        yield return AppointmentType("extraction", "Extraction", 30, Accent, online: false);
        yield return AppointmentType("rct", "Root canal", 90, Accent, online: false);
        yield return AppointmentType("emergency", "Emergency", 30, Accent, online: true);
        yield return AppointmentType("filling", "Filling", 45, Neutral500, online: false);
    }

    private const string Accent = "var(--color-accent)";
    private const string Neutral500 = "var(--color-neutral-500)";
    private const string Neutral800 = "var(--color-neutral-800)";

    private static AppointmentType AppointmentType(
        string key, string name, int minutes, string colour, bool online) =>
        new()
        {
            Id = Id($"appointment-type:{key}"),
            Name = name,
            DefaultDurationMinutes = minutes,
            Colour = colour,
            IsBookableOnline = online,
        };

    // ---- today's diary ---------------------------------------------------

    /// <summary>
    /// The prototype's "Today's arrivals" list, as real appointments on whatever day the
    /// app is first run.
    /// </summary>
    /// <remarks>
    /// Times are built from <paramref name="today"/> in local time and converted to UTC,
    /// because that is what the column stores. Constructing them as UTC directly would
    /// put an 8:00 clinic start at 6pm the previous day in Sydney.
    /// </remarks>
    private static IEnumerable<Appointment> TodaysAppointments(DateOnly today)
    {
        // The starting statuses reproduce the prototype's own: the first visit is done,
        // the next is in the chair, and the day tails off into not-yet-arrived.
        yield return Appointment("1", today, 8, 0, "10204", "vance", "chair-1", "exam",
            45, AppointmentStatus.Completed, "Exam & clean");

        yield return Appointment("2", today, 8, 45, "10201", "vance", "chair-1", "crown-prep",
            60, AppointmentStatus.InProgress, "Crown prep 46");

        yield return Appointment("3", today, 9, 30, "10205", "ellery", "chair-2", "extraction",
            30, AppointmentStatus.Seated, "Extraction 38");

        yield return Appointment("4", today, 9, 30, "10206", "ito", "chair-3", "hygiene",
            40, AppointmentStatus.CheckedIn, "Scale & polish");

        yield return Appointment("5", today, 10, 15, "10207", "ellery", "chair-2", "consult",
            20, AppointmentStatus.Scheduled, "Implant consult");

        yield return Appointment("6", today, 11, 45, "10208", "vance", "chair-1", "rct",
            90, AppointmentStatus.Scheduled, "RCT · visit 2 of 3");

        // Two missed visits earlier in the week, so the FTA figure on the front desk is a
        // real count rather than a constant. Dated back from today and clamped into this
        // week, so they land inside the window the screen measures whatever day it is.
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));

        yield return Appointment("fta-1", monday, 14, 0, "10212", "vance", "chair-1", "exam",
            45, AppointmentStatus.FailedToAttend, "Exam & clean",
            cancellationReason: "No answer on either number. Fee rule applied.");

        yield return Appointment("fta-2", monday.AddDays(1), 9, 0, "10223", "ito", "chair-3", "hygiene",
            40, AppointmentStatus.FailedToAttend, "Scale & polish",
            cancellationReason: "Did not attend, no contact.");
    }

    private static Appointment Appointment(
        string key,
        DateOnly day,
        int hour,
        int minute,
        string patientNumber,
        string providerKey,
        string operatoryKey,
        string typeKey,
        int durationMinutes,
        AppointmentStatus status,
        string reason,
        string? cancellationReason = null)
    {
        var localStart = day.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Local);

        return new Appointment
        {
            Id = Id($"appointment:{key}"),
            PatientId = Id($"patient:{patientNumber}"),
            PracticeLocationId = SydneyCbd,
            ProviderId = Id($"provider:{providerKey}"),
            OperatoryId = Id($"operatory:{operatoryKey}"),
            AppointmentTypeId = Id($"appointment-type:{typeKey}"),
            StartUtc = localStart.ToUniversalTime(),
            DurationMinutes = durationMinutes,
            Status = status,
            Reason = reason,
            CancellationReason = cancellationReason,
            CheckedInUtc = status is AppointmentStatus.CheckedIn or AppointmentStatus.Seated
                or AppointmentStatus.InProgress or AppointmentStatus.Completed
                ? localStart.ToUniversalTime()
                : null,
            CompletedUtc = status == AppointmentStatus.Completed
                ? localStart.AddMinutes(durationMinutes).ToUniversalTime()
                : null,
        };
    }

    /// <summary>The prototype's short-notice list.</summary>
    private static IEnumerable<WaitlistEntry> Waitlist(DateOnly today)
    {
        yield return Waiting("1", "10209", WaitlistPriority.Preferred, "any exam slot", today);
        yield return Waiting("2", "10210", WaitlistPriority.Routine, "hygiene, mornings", today);
        yield return Waiting("3", "10211", WaitlistPriority.Urgent, "emergency — toothache", today);
    }

    private static WaitlistEntry Waiting(
        string key, string patientNumber, WaitlistPriority priority, string wants, DateOnly today) =>
        new()
        {
            Id = Id($"waitlist:{key}"),
            PatientId = Id($"patient:{patientNumber}"),
            PracticeLocationId = SydneyCbd,
            Priority = priority,
            Reason = wants,
            AvailableFrom = today,
            AvailableUntil = today.AddMonths(1),
        };

    /// <summary>The prototype's front-desk task list.</summary>
    private static IEnumerable<PracticeTask> Tasks(DateOnly today)
    {
        yield return Task("1", "Call lab — Yuen crown due Wed", today, order: 0, patientNumber: "10201");
        yield return Task("2", "Rebook FTA: D. Marsh (fee applied)", today, order: 1, patientNumber: "10212");
        yield return Task("3", "Chase acct #10208 — $1,240, 62 days", today.AddDays(1), order: 2, patientNumber: "10208");
    }

    private static PracticeTask Task(
        string key, string label, DateOnly dueOn, int order, string? patientNumber) =>
        new()
        {
            Id = Id($"task:{key}"),
            PracticeLocationId = SydneyCbd,
            Label = label,
            DueOn = dueOn,
            DisplayOrder = order,
            PatientId = patientNumber is null ? null : Id($"patient:{patientNumber}"),
        };

    /// <summary>
    /// A handful of ADA item numbers, enough for charting and billing to have something
    /// real to reference. Fees are indicative, not a published schedule.
    /// </summary>
    private static IEnumerable<ProcedureCode> ProcedureCodes()
    {
        yield return Code("011", "Comprehensive oral examination", "Full check-up", "Diagnostic", 78m, 30);
        yield return Code("012", "Periodic oral examination", "Check-up", "Diagnostic", 62m, 20);
        yield return Code("022", "Intraoral periapical radiograph", "Small X-ray", "Diagnostic", 45m, 10);
        yield return Code("037", "Panoramic radiograph", "Full-mouth X-ray", "Diagnostic", 128m, 15);
        yield return Code("111", "Removal of plaque and stain", "Clean", "Preventive", 98m, 30);
        yield return Code("114", "Removal of calculus", "Deep clean", "Preventive", 145m, 45);
        yield return Code("121", "Topical application of remineralising agent", "Fluoride", "Preventive", 38m, 10);
        // Per tooth, all four. The ADA schedule charges restorative, endodontic,
        // surgical and prosthodontic items against a named tooth, and a line without one
        // cannot be audited or claimed — the flag was unset on every code, so nothing
        // ever asked for the tooth.
        yield return Code("311", "Removal of a tooth", "Extraction", "Oral surgery", 245m, 30,
            perTooth: true);
        yield return Code("415", "Complete chemomechanical preparation of root canal", "Root canal", "Endodontics", 420m, 90,
            perTooth: true);
        yield return Code("531", "Adhesive restoration, two surfaces, posterior", "Two-surface filling", "Restorative", 215m, 45,
            perTooth: true);
        yield return Code("613", "Full crown, veneered, indirect", "Crown", "Prosthodontics", 1850m, 90,
            perTooth: true);
        yield return Code("911", "Palliative care", "Emergency relief", "General", 95m, 20);
    }

    /// <summary>
    /// A few items Newtown charges differently, so the per-site pricing on the catalogue
    /// has something real behind it.
    /// </summary>
    /// <remarks>
    /// Three rows against a twelve-item schedule, which is the ratio the feature is built
    /// for: a site prices a handful of items for itself and takes the practice fee for
    /// everything else. Seeding one per item per site would have demonstrated the shape
    /// the entity deliberately avoids.
    ///
    /// Two down and one up on purpose. A second site is not automatically the cheap one —
    /// Newtown discounts the routine items that compete on price locally and charges more
    /// for the crown, where the lab bill is the same wherever the chair is.
    /// </remarks>
    private static IEnumerable<ProcedureCodeFee> SiteFees()
    {
        yield return SiteFee("012", Newtown, 55m);
        yield return SiteFee("111", Newtown, 88m);
        yield return SiteFee("613", Newtown, 1920m);
    }

    private static ProcedureCodeFee SiteFee(
        string itemNumber, Guid locationId, decimal fee) =>
        new()
        {
            Id = Id($"procedure-fee:{itemNumber}:{locationId}"),
            ProcedureCodeId = Id($"procedure:{itemNumber}"),
            PracticeLocationId = locationId,
            Fee = fee,
        };

    /// <param name="perTooth">
    /// True where the item is charged against one tooth, which makes the tooth number
    /// mandatory on an invoice line.
    /// </param>
    private static ProcedureCode Code(
        string itemNumber,
        string description,
        string friendlyName,
        string category,
        decimal fee,
        int minutes,
        bool perTooth = false) =>
        new()
        {
            Id = Id($"procedure:{itemNumber}"),
            ItemNumber = itemNumber,
            Description = description,
            PatientFriendlyName = friendlyName,
            Category = category,
            Fee = fee,
            TypicalDurationMinutes = minutes,
            IsPerTooth = perTooth,
        };

    // ---- patients --------------------------------------------------------

    private static IEnumerable<PatientEntity> Patients() =>
    [
        Patient(
            number: "10201",
            firstName: "Margaret",
            lastName: "Yuen",
            dateOfBirth: new DateOnly(1968, 3, 12),
            mobile: "0412 883 021",
            email: "m.yuen@example.com",
            status: PatientStatus.Active,
            tags: PatientTags.NervousPatient | PatientTags.InterpreterNeeded,
            lastSeenUtc: new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc),
            balance: 340m,
            failedToAttendCount: 0,
            householdId: Household(0)),
        Patient(
            number: "10202",
            firstName: "Kevin",
            lastName: "Yuen",
            dateOfBirth: new DateOnly(1965, 6, 3),
            mobile: "0412 883 021",
            email: "k.yuen@example.com",
            status: PatientStatus.Active,
            tags: PatientTags.None,
            lastSeenUtc: new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc),
            balance: 0m,
            failedToAttendCount: 0,
            householdId: Household(0)),
        Patient(
            number: "10203",
            firstName: "Amy",
            lastName: "Yuen",
            dateOfBirth: new DateOnly(2009, 9, 22),
            mobile: "0412 883 021",
            email: "a.yuen@example.com",
            status: PatientStatus.Active,
            tags: PatientTags.None,
            lastSeenUtc: new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc),
            balance: 0m,
            failedToAttendCount: 0,
            householdId: Household(0)),
        Patient(
            number: "10204",
            firstName: "Liam",
            lastName: "Okafor",
            dateOfBirth: new DateOnly(1990, 1, 30),
            mobile: "0433 210 977",
            email: "l.okafor@example.com",
            status: PatientStatus.Active,
            tags: PatientTags.None,
            lastSeenUtc: new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
            balance: 49m,
            failedToAttendCount: 0,
            householdId: null),
        Patient(
            number: "10205",
            firstName: "Sofia",
            lastName: "Reyes",
            dateOfBirth: new DateOnly(1984, 5, 17),
            mobile: "0401 556 208",
            email: "s.reyes@example.com",
            status: PatientStatus.Active,
            tags: PatientTags.HighRisk,
            lastSeenUtc: new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
            balance: 480m,
            failedToAttendCount: 0,
            householdId: null),
        Patient(
            number: "10206",
            firstName: "Tom",
            lastName: "Braddon",
            dateOfBirth: new DateOnly(1978, 11, 9),
            mobile: "0455 902 341",
            email: "t.braddon@example.com",
            status: PatientStatus.Active,
            tags: PatientTags.None,
            lastSeenUtc: new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
            balance: 0m,
            failedToAttendCount: 0,
            householdId: null),
        Patient(
            number: "10207",
            firstName: "Priya",
            lastName: "Nair",
            dateOfBirth: new DateOnly(1995, 2, 25),
            mobile: "0466 118 730",
            email: "p.nair@example.com",
            status: PatientStatus.Active,
            tags: PatientTags.None,
            lastSeenUtc: new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc),
            balance: 0m,
            failedToAttendCount: 0,
            householdId: null),
        Patient(
            number: "10208",
            firstName: "Jack",
            lastName: "Whitely",
            dateOfBirth: new DateOnly(1970, 8, 11),
            mobile: "0421 774 665",
            email: "j.whitely@example.com",
            status: PatientStatus.Active,
            tags: PatientTags.Debtor,
            lastSeenUtc: new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc),
            balance: 1240m,
            failedToAttendCount: 0,
            householdId: null),
        Patient(
            number: "10209",
            firstName: "Grace",
            lastName: "Papas",
            dateOfBirth: new DateOnly(1958, 4, 4),
            mobile: "0402 330 118",
            email: "g.papas@example.com",
            status: PatientStatus.Active,
            tags: PatientTags.None,
            lastSeenUtc: new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc),
            balance: 0m,
            failedToAttendCount: 0,
            householdId: null),
        Patient(
            number: "10210",
            firstName: "Ray",
            lastName: "Chen",
            dateOfBirth: new DateOnly(1988, 12, 19),
            mobile: "0477 665 209",
            email: "r.chen@example.com",
            status: PatientStatus.Active,
            tags: PatientTags.None,
            lastSeenUtc: new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc),
            balance: 0m,
            failedToAttendCount: 0,
            householdId: null),
        Patient(
            number: "10211",
            firstName: "Sara",
            lastName: "Ali",
            dateOfBirth: new DateOnly(1992, 7, 28),
            mobile: "0490 221 583",
            email: "s.ali@example.com",
            status: PatientStatus.Lead,
            tags: PatientTags.None,
            lastSeenUtc: null,
            balance: 0m,
            failedToAttendCount: 0,
            householdId: null),
        Patient(
            number: "10212",
            firstName: "Dan",
            lastName: "Marsh",
            dateOfBirth: new DateOnly(1981, 10, 15),
            mobile: "0413 908 442",
            email: "d.marsh@example.com",
            status: PatientStatus.Active,
            tags: PatientTags.Debtor,
            lastSeenUtc: new DateTime(2026, 6, 18, 0, 0, 0, DateTimeKind.Utc),
            balance: 620m,
            failedToAttendCount: 2,
            householdId: null),
        Patient(
            number: "10213",
            firstName: "Kofi",
            lastName: "Osei",
            dateOfBirth: new DateOnly(1975, 2, 2),
            mobile: "0438 557 190",
            email: "k.osei@example.com",
            status: PatientStatus.Active,
            tags: PatientTags.None,
            lastSeenUtc: new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc),
            balance: 0m,
            failedToAttendCount: 0,
            householdId: null),
        Patient(
            number: "10214",
            firstName: "Elena",
            lastName: "Silva",
            dateOfBirth: new DateOnly(1999, 9, 8),
            mobile: "0424 776 301",
            email: "e.silva@example.com",
            status: PatientStatus.Active,
            tags: PatientTags.None,
            lastSeenUtc: new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
            balance: 0m,
            failedToAttendCount: 0,
            householdId: null),
        Patient(
            number: "10215",
            firstName: "Marco",
            lastName: "Conte",
            dateOfBirth: new DateOnly(1962, 3, 21),
            mobile: "0409 887 254",
            email: "m.conte@example.com",
            status: PatientStatus.Active,
            tags: PatientTags.Vip,
            lastSeenUtc: new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
            balance: 0m,
            failedToAttendCount: 0,
            householdId: null),
        Patient(
            number: "10216",
            firstName: "Aisha",
            lastName: "Kaur",
            dateOfBirth: new DateOnly(1987, 1, 13),
            mobile: "0431 445 902",
            email: "a.kaur@example.com",
            status: PatientStatus.Active,
            tags: PatientTags.None,
            lastSeenUtc: new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
            balance: 27m,
            failedToAttendCount: 0,
            householdId: null),
        Patient(
            number: "10217",
            firstName: "Phuc",
            lastName: "Duong",
            dateOfBirth: new DateOnly(1993, 7, 30),
            mobile: "0450 662 187",
            email: "p.duong@example.com",
            status: PatientStatus.Active,
            tags: PatientTags.None,
            lastSeenUtc: new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
            balance: 0m,
            failedToAttendCount: 0,
            householdId: null),
        Patient(
            number: "10218",
            firstName: "Nina",
            lastName: "Kovac",
            dateOfBirth: new DateOnly(1979, 6, 6),
            mobile: "0417 220 953",
            email: "n.kovac@example.com",
            status: PatientStatus.Active,
            tags: PatientTags.None,
            lastSeenUtc: new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc),
            balance: 0m,
            failedToAttendCount: 0,
            householdId: null),
        Patient(
            number: "10219",
            firstName: "Oscar",
            lastName: "Byrne",
            dateOfBirth: new DateOnly(2014, 11, 27),
            mobile: "0402 118 664",
            email: "o.byrne@example.com",
            status: PatientStatus.Active,
            tags: PatientTags.None,
            lastSeenUtc: new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc),
            balance: 0m,
            failedToAttendCount: 0,
            householdId: Household(1)),
        Patient(
            number: "10220",
            firstName: "Helen",
            lastName: "Byrne",
            dateOfBirth: new DateOnly(1983, 5, 14),
            mobile: "0402 118 664",
            email: "h.byrne@example.com",
            status: PatientStatus.Active,
            tags: PatientTags.None,
            lastSeenUtc: new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc),
            balance: 0m,
            failedToAttendCount: 0,
            householdId: Household(1)),
        Patient(
            number: "10221",
            firstName: "George",
            lastName: "Stamos",
            dateOfBirth: new DateOnly(1949, 1, 1),
            mobile: "0419 003 271",
            email: "g.stamos@example.com",
            status: PatientStatus.Active,
            tags: PatientTags.None,
            lastSeenUtc: new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc),
            balance: 0m,
            failedToAttendCount: 0,
            householdId: null),
        Patient(
            number: "10222",
            firstName: "Lucy",
            lastName: "Tran",
            dateOfBirth: new DateOnly(2001, 2, 18),
            mobile: "0483 991 405",
            email: "l.tran@example.com",
            status: PatientStatus.Active,
            tags: PatientTags.None,
            lastSeenUtc: new DateTime(2026, 8, 28, 0, 0, 0, DateTimeKind.Utc),
            balance: 0m,
            failedToAttendCount: 0,
            householdId: null),
        Patient(
            number: "10223",
            firstName: "Ben",
            lastName: "Hollis",
            dateOfBirth: new DateOnly(1996, 3, 9),
            mobile: "0421 500 883",
            email: "b.hollis@example.com",
            status: PatientStatus.RecallDue,
            tags: PatientTags.None,
            lastSeenUtc: new DateTime(2025, 10, 10, 0, 0, 0, DateTimeKind.Utc),
            balance: 0m,
            failedToAttendCount: 0,
            householdId: null),
        Patient(
            number: "10224",
            firstName: "Rita",
            lastName: "Fernandez",
            dateOfBirth: new DateOnly(1971, 8, 23),
            mobile: "0434 226 719",
            email: "r.fernandez@example.com",
            status: PatientStatus.Active,
            tags: PatientTags.None,
            lastSeenUtc: new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc),
            balance: 0m,
            failedToAttendCount: 0,
            householdId: null),
        Patient(
            number: "10225",
            firstName: "Sam",
            lastName: "Waters",
            dateOfBirth: new DateOnly(1954, 5, 5),
            mobile: "0448 173 260",
            email: "s.waters@example.com",
            status: PatientStatus.Archived,
            tags: PatientTags.None,
            lastSeenUtc: new DateTime(2024, 2, 3, 0, 0, 0, DateTimeKind.Utc),
            balance: 0m,
            failedToAttendCount: 0,
            householdId: null),
    ];

    private static PatientEntity Patient(
        string number,
        string firstName,
        string lastName,
        DateOnly dateOfBirth,
        string mobile,
        string email,
        PatientStatus status,
        PatientTags tags,
        DateTime? lastSeenUtc,
        decimal balance,
        int failedToAttendCount,
        Guid? householdId) =>
        new()
        {
            Id = Id($"patient:{number}"),
            PatientNumber = number,
            FirstName = firstName,
            LastName = lastName,
            DateOfBirth = dateOfBirth,
            Mobile = mobile,
            Email = email,
            Status = status,
            Tags = tags,
            LastSeenUtc = lastSeenUtc,
            Balance = balance,
            FailedToAttendCount = failedToAttendCount,
            HouseholdId = householdId,
            PracticeLocationId = SydneyCbd,
            PreferredProviderId = Id("provider:vance"),
            PreferredLanguage = tags.HasFlag(PatientTags.InterpreterNeeded) ? "Cantonese" : "English",

            // Everyone seeded consents to reminders; only some to marketing. Both false
            // for everyone would leave the comms feature with nothing to send, and both
            // true would hide the opt-out handling the Spam Act requires.
            ReminderConsent = true,
            MarketingConsent = balance == 0m,
        };

    // ---- ids -------------------------------------------------------------

    private static Guid Household(int index) => Id($"household:{index}");

    /// <summary>
    /// A stable id for a seed key, so a rebuilt database reproduces the same ids.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four FNV-1a passes over the namespace and the name, each with a different offset
    /// basis, giving 16 deterministic bytes. Not a hash function anyone should reach for
    /// where collisions matter — but the input here is a few dozen literal keys written in
    /// this file, and a collision between two of them would show up immediately as a
    /// primary-key violation on the very first run.
    /// </para>
    /// <para>
    /// Deliberately not MD5 or SHA-1, and so deliberately not an RFC 4122 name-based
    /// UUID. This library declares <c>browser</c> as a supported platform, where
    /// <c>System.Security.Cryptography</c> is unavailable; using it here raises CA1416 for
    /// code that would never run in a browser, and suppressing that warning is worse than
    /// not needing it. If these ids ever have to match ids computed by another tool, that
    /// tool has to use this same function — which is the trade being made.
    /// </para>
    /// </remarks>
    private static Guid Id(string name)
    {
        const ulong prime = 1099511628211;

        // Four unrelated starting points, so the four 32-bit slices of the result are
        // independent rather than four views of one hash.
        var bases = new ulong[] { 14695981039346656037, 1469598103934665603, 146959810393466560, 14695981039346656 };

        var bytes = new byte[16];
        var namespaceBytes = Namespace.ToByteArray();

        for (var pass = 0; pass < 4; pass++)
        {
            var hash = bases[pass];

            foreach (var b in namespaceBytes)
            {
                hash = (hash ^ b) * prime;
            }

            foreach (var b in System.Text.Encoding.UTF8.GetBytes(name))
            {
                hash = (hash ^ b) * prime;
            }

            // Fold the 64-bit state down to the 32 bits this slice contributes, so the
            // high half is not simply discarded.
            var folded = (uint)(hash ^ (hash >> 32));
            BitConverter.TryWriteBytes(bytes.AsSpan(pass * 4, 4), folded);
        }

        return new Guid(bytes);
    }
}
