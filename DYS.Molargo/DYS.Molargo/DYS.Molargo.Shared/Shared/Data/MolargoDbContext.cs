using System.Globalization;
using System.Linq.Expressions;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Shared.Entities;
using DYS.Molargo.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DYS.Molargo.Shared.Data;

/// <summary>
/// The local SQLite schema — the app's only store while it is offline-only.
/// </summary>
/// <remarks>
/// Configured entirely fluently. Attributes on the <c>DYS.Molargo.Domain</c> entities
/// would force a persistence dependency into the project every other project references,
/// and the same types then could not be reused by a future API without dragging EF Core
/// into it.
/// </remarks>
public sealed class MolargoDbContext : DbContext
{
    /// <summary>
    /// SQLite has no decimal type, and EF Core refuses to choose one silently because
    /// routing money through a float loses cents. Stored as TEXT via this converter.
    ///
    /// Spelled out with <see cref="CultureInfo.InvariantCulture"/> rather than left to
    /// <c>HasConversion&lt;string&gt;()</c>, which goes through
    /// <c>decimal.ToString()</c> and is culture-sensitive: a tablet set to a
    /// comma-decimal locale writes "340,50" where the front-desk PC wrote "340.50" — the
    /// same amount stored two ways, matching neither on equality nor on a re-read.
    ///
    /// TEXT means no numeric ordering in SQL. Equality against a fixed amount is safe
    /// because it compares the converted string; greater-than is NOT, since "1000" sorts
    /// below "9". Threshold and sum queries have to run in memory.
    /// </summary>
    private static readonly ValueConverter<decimal, string> MoneyConverter = new(
        amount => amount.ToString(CultureInfo.InvariantCulture),
        stored => decimal.Parse(stored, NumberStyles.Number, CultureInfo.InvariantCulture));

    private static readonly ValueConverter<decimal?, string?> NullableMoneyConverter = new(
        amount => amount.HasValue ? amount.Value.ToString(CultureInfo.InvariantCulture) : null,
        stored => stored == null
            ? null
            : decimal.Parse(stored, NumberStyles.Number, CultureInfo.InvariantCulture));

    public MolargoDbContext(DbContextOptions<MolargoDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Where this context learns which clinic it is confined to.
    /// </summary>
    /// <remarks>
    /// Set by <see cref="MolargoDatabase"/> on each context it hands out. A property
    /// rather than a constructor parameter because <c>AddDbContextFactory</c> builds the
    /// context from options alone.
    /// </remarks>
    public ITenantContext? TenantSource { get; set; }

    /// <summary>
    /// The clinic every read on this context is confined to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read through the service rather than held as a value here, and that indirection is
    /// deliberate. EF caches one compiled model per context type, so the tenant filter's
    /// reference to this context is fixed at the moment the model is first built — a
    /// plain <c>Guid</c> field would pin every later context to the tenant the very first
    /// one happened to carry. Going through the service means the filter always reads the
    /// tenant that is current, whichever context instance the expression was captured
    /// from.
    /// </para>
    /// <para>
    /// <see cref="Guid.Empty"/> when unresolved, which matches no rows. An unset tenant
    /// therefore reads nothing rather than everything — the failure worth guarding against
    /// is the one that silently widens the filter, so the default is the closed one.
    /// </para>
    /// </remarks>
    public Guid TenantId => TenantSource?.TenantId ?? Guid.Empty;

    // ---- tenancy ---------------------------------------------------------

    /// <summary>
    /// The clinics themselves. Not tenant-filtered — see <see cref="Tenant"/>.
    /// </summary>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    // ---- practice and people ---------------------------------------------
    public DbSet<PracticeLocation> PracticeLocations => Set<PracticeLocation>();
    public DbSet<Operatory> Operatories => Set<Operatory>();
    public DbSet<Provider> Providers => Set<Provider>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<PatientAlert> PatientAlerts => Set<PatientAlert>();
    public DbSet<PatientDocument> PatientDocuments => Set<PatientDocument>();
    public DbSet<ConsentForm> ConsentForms => Set<ConsentForm>();

    public DbSet<ConsentTemplate> ConsentTemplates => Set<ConsentTemplate>();

    public DbSet<MedicalHistoryQuestion> MedicalHistoryQuestions =>
        Set<MedicalHistoryQuestion>();
    public DbSet<MedicalHistoryForm> MedicalHistoryForms => Set<MedicalHistoryForm>();
    public DbSet<MedicalHistoryAnswer> MedicalHistoryAnswers => Set<MedicalHistoryAnswer>();

    // ---- diary -----------------------------------------------------------
    public DbSet<AppointmentType> AppointmentTypes => Set<AppointmentType>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<WaitlistEntry> WaitlistEntries => Set<WaitlistEntry>();
    public DbSet<Recall> Recalls => Set<Recall>();
    public DbSet<PracticeTask> PracticeTasks => Set<PracticeTask>();

    // ---- clinical --------------------------------------------------------
    public DbSet<ProcedureCode> ProcedureCodes => Set<ProcedureCode>();

    public DbSet<ProcedureCodeFee> ProcedureCodeFees => Set<ProcedureCodeFee>();
    public DbSet<ToothChartEntry> ToothChartEntries => Set<ToothChartEntry>();
    public DbSet<ClinicalNote> ClinicalNotes => Set<ClinicalNote>();
    public DbSet<PerioExam> PerioExams => Set<PerioExam>();
    public DbSet<PerioSiteReading> PerioSiteReadings => Set<PerioSiteReading>();
    public DbSet<PerioToothReading> PerioToothReadings => Set<PerioToothReading>();
    public DbSet<TreatmentPlan> TreatmentPlans => Set<TreatmentPlan>();
    public DbSet<TreatmentPlanItem> TreatmentPlanItems => Set<TreatmentPlanItem>();

    // ---- billing ---------------------------------------------------------
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Claim> Claims => Set<Claim>();

    // ---- prescribing and referrals ---------------------------------------
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<PrescriptionItem> PrescriptionItems => Set<PrescriptionItem>();
    public DbSet<Referral> Referrals => Set<Referral>();
    public DbSet<FormularyMedicine> FormularyMedicines => Set<FormularyMedicine>();
    public DbSet<MedicalCertificate> MedicalCertificates => Set<MedicalCertificate>();

    // ---- inventory, sterilisation and lab --------------------------------
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<SterilisationCycle> SterilisationCycles => Set<SterilisationCycle>();
    public DbSet<SterilisationCycleUse> SterilisationCycleUses => Set<SterilisationCycleUse>();
    public DbSet<LabCase> LabCases => Set<LabCase>();

    // ---- communications and audit ----------------------------------------
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();
    public DbSet<CommunicationLog> CommunicationLogs => Set<CommunicationLog>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<NotificationSettings> NotificationSettings => Set<NotificationSettings>();

    public DbSet<PasswordResetCode> PasswordResetCodes => Set<PasswordResetCode>();
    public DbSet<PrinterSettings> PrinterSettings => Set<PrinterSettings>();

    // ---- the vendor's own -------------------------------------------------
    public DbSet<Plan> Plans => Set<Plan>();

    /// <summary>
    /// Subscription charges. Stamped with the clinic they bill, not the vendor.
    /// </summary>
    /// <remarks>
    /// Which means the tenant filter applies to them, and the vendor's billing service
    /// bypasses it per query — the same deliberate crossing it makes to count patients. A
    /// practice can therefore be shown its own charges later without any of it moving.
    /// </remarks>
    public DbSet<SubscriptionCharge> SubscriptionCharges => Set<SubscriptionCharge>();

    // ---- local-only ------------------------------------------------------
    public DbSet<LocalMetadata> Metadata => Set<LocalMetadata>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureConventions(modelBuilder);
        ConfigureTenancy(modelBuilder);
        ConfigurePatients(modelBuilder);
        ConfigureDiary(modelBuilder);
        ConfigureClinical(modelBuilder);
        ConfigureBilling(modelBuilder);
        ConfigureInventory(modelBuilder);
        ConfigureCommunications(modelBuilder);

        modelBuilder.Entity<LocalMetadata>(metadata =>
        {
            metadata.ToTable("local_metadata");
            metadata.HasKey(m => m.Key);
        });
    }

    /// <summary>
    /// The rules that apply to every entity, applied by walking the model rather than
    /// repeated per type — 34 entities is far past the point where a copied
    /// <c>HasConversion</c> stays consistent.
    /// </summary>
    private static void ConfigureConventions(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(EntityBase).IsAssignableFrom(entity.ClrType)) continue;

            // Snake-cased table names, so the schema reads the same in a SQLite browser
            // as it will in whatever the server database turns out to be.
            entity.SetTableName(ToSnakeCase(entity.ClrType.Name));

            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(decimal))
                {
                    property.SetValueConverter(MoneyConverter);
                }
                else if (property.ClrType == typeof(decimal?))
                {
                    property.SetValueConverter(NullableMoneyConverter);
                }

                // Enums are left alone: EF Core already persists them as their underlying
                // integer, which is what the explicit values on each enum are for.
            }

            // Every read filters on this, on every table.
            entity.AddIndex(entity.FindProperty(nameof(EntityBase.IsDeleted))!);

            // And on this. Leading with the tenant because it is the most selective
            // column on every table once more than one clinic shares a database.
            entity.AddIndex(entity.FindProperty(nameof(EntityBase.TenantId))!);
        }
    }

    /// <summary>
    /// Confines every table to the current clinic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A model-level query filter rather than a predicate each repository method adds. EF
    /// applies it to every read, and to <c>ExecuteUpdate</c> and <c>ExecuteDelete</c> as
    /// well, so there is no query shape that can forget it. A rule that protects one
    /// clinic's records from another has to be impossible to omit rather than merely
    /// conventional.
    /// </para>
    /// <para>
    /// The filter reads <see cref="TenantId"/>, which resolves through the tenant service
    /// rather than a value on this instance — see the note there for why the model cache
    /// makes that necessary. Built by walking the model because these entities have no
    /// common mapped base type: <c>EntityBase</c> is not an entity, so the filter has to be
    /// set per table.
    /// </para>
    /// </remarks>
    private void ConfigureTenancy(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(tenant =>
        {
            tenant.Property(t => t.Name).IsRequired().HasMaxLength(200);
            tenant.Property(t => t.Slug).IsRequired().HasMaxLength(80);

            // The handle a future sign-in or sync endpoint resolves a clinic by, so two
            // clinics cannot share one.
            tenant.HasIndex(t => t.Slug).IsUnique();

            tenant.Property(t => t.CountryCode).IsRequired().HasMaxLength(2);
        });

        modelBuilder.Entity<Plan>(plan =>
        {
            plan.Ignore(p => p.IsPerSite);
            plan.Property(p => p.Name).IsRequired().HasMaxLength(120);
            plan.Property(p => p.Code).IsRequired().HasMaxLength(30);
            plan.Property(p => p.CountryCode).IsRequired().HasMaxLength(2);
            plan.Property(p => p.CurrencyCode).IsRequired().HasMaxLength(3);

            // Money as a fixed scale, not a float. A price that has been through binary
            // floating point invoices a cent out, and the cent is always noticed.
            plan.Property(p => p.MonthlyBase).HasPrecision(18, 2);
            plan.Property(p => p.PricePerExtraSite).HasPrecision(18, 2);
            plan.Property(p => p.PricePerExtraSeat).HasPrecision(18, 2);

            // One plan per code per country — the pair a subscription is sold by.
            plan.HasIndex(p => new { p.Code, p.CountryCode }).IsUnique();

            // The pricing page reads "every plan on sale in this country".
            plan.HasIndex(p => new { p.CountryCode, p.IsActive });
        });

        modelBuilder.Entity<SubscriptionCharge>(charge =>
        {
            charge.Ignore(c => c.IsPaid);
            charge.Ignore(c => c.IsOutstanding);
            charge.Ignore(c => c.PeriodLabel);

            charge.Property(c => c.Amount).HasPrecision(18, 2);
            charge.Property(c => c.CurrencyCode).IsRequired().HasMaxLength(3);
            charge.Property(c => c.PlanName).HasMaxLength(120);
            charge.Property(c => c.FailureReason).HasMaxLength(500);
            charge.Property(c => c.Reference).HasMaxLength(120);

            // One clinic's ledger, newest first — the billing table's only read.
            charge.HasIndex(c => new { c.TenantId, c.PeriodStart });

            // "Has this period already been raised", which is what makes running the
            // monthly charge twice harmless.
            charge.HasIndex(c => c.PeriodStart);
        });

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(EntityBase).IsAssignableFrom(entity.ClrType)) continue;

            // Two exceptions, both the vendor's rather than a clinic's.
            //
            // Tenant, because filtering it by the tenant it defines would make it
            // unreadable to the code that resolves which tenant is current. And Plan,
            // because a clinic has to be able to read the plan it is on — the row belongs
            // to the vendor, and scoping it to the vendor's tenant would hide every
            // price from every subscriber.
            if (entity.ClrType == typeof(Tenant) || entity.ClrType == typeof(Plan))
            {
                continue;
            }

            var row = Expression.Parameter(entity.ClrType, "row");

            var filter = Expression.Lambda(
                Expression.Equal(
                    Expression.Property(row, nameof(EntityBase.TenantId)),

                    // Member access on this context, which is the shape EF parameterises.
                    // A plain Expression.Constant of the id would be baked into the cached
                    // model and pin every later context to the first one's tenant.
                    Expression.Property(
                        Expression.Constant(this), nameof(TenantId))),
                row);

            entity.SetQueryFilter(filter);
        }
    }

    private static void ConfigurePatients(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Patient>(patient =>
        {
            patient.Property(p => p.FirstName).IsRequired().HasMaxLength(100);
            patient.Property(p => p.LastName).IsRequired().HasMaxLength(100);

            // Computed from other columns; nothing to store.
            patient.Ignore(p => p.FullName);
            patient.Ignore(p => p.DisplayName);

            // The patients list sorts by surname and filters by status — the two together
            // are what the default screen runs.
            patient.HasIndex(p => new { p.LastName, p.FirstName });
            patient.HasIndex(p => p.Status);

            // Quoted over the phone, so it is looked up directly and often.
            patient.HasIndex(p => p.PatientNumber);
            patient.HasIndex(p => p.Mobile);

            // "Who else lives here" is asked on every family booking.
            patient.HasIndex(p => p.HouseholdId);

            patient.Property(p => p.ConsentSource).HasMaxLength(200);

            // A campaign's audience is "marketing consent, at this location" — the whole
            // point of the index is that the send never scans opted-out patients.
            patient.HasIndex(p => new { p.PracticeLocationId, p.MarketingConsent });
        });

        modelBuilder.Entity<PatientAlert>(alert =>
        {
            alert.Property(a => a.Summary).IsRequired().HasMaxLength(200);
            alert.Ignore(a => a.IsResolved);

            // Loaded in full for the record header and the prescribing check.
            alert.HasIndex(a => new { a.PatientId, a.Kind });
        });

        modelBuilder.Entity<PatientDocument>(document =>
        {
            document.Property(d => d.Name).IsRequired().HasMaxLength(300);
            document.Property(d => d.RelativePath).IsRequired();
            document.HasIndex(d => new { d.PatientId, d.Kind });
        });

        modelBuilder.Entity<ConsentForm>(consent =>
        {
            consent.Property(c => c.Title).IsRequired().HasMaxLength(300);
            consent.HasIndex(c => new { c.PatientId, c.Status });
        });

        modelBuilder.Entity<ConsentTemplate>(template =>
        {
            template.Property(t => t.Name).IsRequired().HasMaxLength(200);
            template.Property(t => t.Category).HasMaxLength(100);

            // Looked up by category every time a plan visit is booked, which is the one
            // read on this table that happens in a loop.
            template.HasIndex(t => new { t.Category, t.IsActive });
        });

        modelBuilder.Entity<MedicalHistoryQuestion>(question =>
        {
            question.Property(q => q.Code).IsRequired().HasMaxLength(100);
            question.Property(q => q.Text).IsRequired().HasMaxLength(400);
            question.Property(q => q.DetailPrompt).HasMaxLength(200);

            // The code is the join to every answer ever given. Unique so two questions can
            // never both claim one — which would make "which patients are on
            // anticoagulants" return whichever the query happened to reach first.
            question.HasIndex(q => q.Code).IsUnique();
        });

        modelBuilder.Entity<MedicalHistoryForm>(form =>
        {
            form.Ignore(f => f.IsComplete);
            form.HasIndex(f => f.PatientId);
            form.HasIndex(f => f.AppointmentId);
        });

        modelBuilder.Entity<MedicalHistoryAnswer>(answer =>
        {
            answer.Property(a => a.QuestionCode).IsRequired().HasMaxLength(100);

            // "Which patients answered yes to X" is the whole reason these are rows.
            answer.HasIndex(a => new { a.MedicalHistoryFormId, a.QuestionCode });
            answer.HasIndex(a => a.QuestionCode);
        });

        modelBuilder.Entity<Provider>(provider =>
        {
            provider.Property(p => p.FirstName).IsRequired().HasMaxLength(100);
            provider.Property(p => p.LastName).IsRequired().HasMaxLength(100);
            provider.Ignore(p => p.FullName);
            provider.HasIndex(p => p.IsActive);
        });

        modelBuilder.Entity<PracticeLocation>(location =>
        {
            location.Property(l => l.Name).IsRequired().HasMaxLength(200);
            location.Property(l => l.TimeZoneId).IsRequired();
        });

        modelBuilder.Entity<Operatory>(operatory =>
        {
            operatory.Property(o => o.Name).IsRequired().HasMaxLength(100);
            operatory.HasIndex(o => new { o.PracticeLocationId, o.DisplayOrder });
        });
    }

    private static void ConfigureDiary(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Appointment>(appointment =>
        {
            appointment.Ignore(a => a.EndUtc);

            // The diary's own query: one location, one day, ordered by time. This index
            // is the difference between a day view that opens instantly and one that
            // scans every appointment the practice has ever taken.
            appointment.HasIndex(a => new { a.PracticeLocationId, a.StartUtc });
            appointment.HasIndex(a => new { a.ProviderId, a.StartUtc });
            appointment.HasIndex(a => new { a.PatientId, a.StartUtc });
            appointment.HasIndex(a => a.Status);
        });

        modelBuilder.Entity<AppointmentType>(type =>
        {
            type.Property(t => t.Name).IsRequired().HasMaxLength(150);
        });

        modelBuilder.Entity<WaitlistEntry>(entry =>
        {
            entry.Ignore(e => e.IsOpen);

            // The short-notice list is read as "open entries, most urgent first".
            entry.HasIndex(e => new { e.PracticeLocationId, e.Priority, e.FulfilledUtc });
            entry.HasIndex(e => e.PatientId);
        });

        modelBuilder.Entity<PracticeTask>(task =>
        {
            task.Property(t => t.Label).IsRequired().HasMaxLength(300);
            task.Ignore(t => t.IsDone);

            // The list is read as "this location's outstanding tasks, oldest due first".
            task.HasIndex(t => new { t.PracticeLocationId, t.CompletedUtc, t.DueOn });
        });

        modelBuilder.Entity<Recall>(recall =>
        {
            recall.Property(r => r.RecallType).IsRequired().HasMaxLength(150);

            // The recall worklist is "due before today, not yet closed".
            recall.HasIndex(r => new { r.Status, r.DueOn });
            recall.HasIndex(r => r.PatientId);
        });
    }

    private static void ConfigureClinical(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcedureCode>(code =>
        {
            code.Property(c => c.ItemNumber).IsRequired().HasMaxLength(20);
            code.Property(c => c.Description).IsRequired().HasMaxLength(500);

            // Charting looks an item up by number on every line entered.
            code.HasIndex(c => c.ItemNumber).IsUnique();
            code.HasIndex(c => c.Category);
        });

        modelBuilder.Entity<PasswordResetCode>(code =>
        {
            // Long enough for the PBKDF2 verifier, which carries its parameters and salt
            // alongside the hash — not for six digits.
            code.Property(row => row.CodeHash).IsRequired().HasMaxLength(256);

            // No Ignore for IsLive: it is a method taking the clock, not a property, and EF
            // never tries to map a method. Calling Ignore on one throws at model build with
            // a message about member access that does not mention the real cause.

            // No unique index on the hash, unlike the link token this replaced. Every code
            // is salted, so two accounts holding the same six digits produce different
            // verifiers — and a collision would be meaningless anyway, because a code is
            // only ever checked against the account the person named.
            code.HasIndex(row => new { row.ProviderId, row.UsedUtc });
        });

        modelBuilder.Entity<ProcedureCodeFee>(fee =>
        {
            fee.Property(f => f.Fee).HasPrecision(18, 2);

            // One override per item per site, enforced by the database rather than only by
            // the service that writes them: two rows for the same pair would make "this
            // site's fee" a question with two answers, and whichever the query happened to
            // read first would become the price on the invoice.
            fee.HasIndex(f => new { f.ProcedureCodeId, f.PracticeLocationId }).IsUnique();

            // Raising a line reads every override for the item; the catalogue screen reads
            // every override the practice has. The unique index above serves the first, so
            // this one is for the second.
            fee.HasIndex(f => f.PracticeLocationId);
        });

        modelBuilder.Entity<ToothChartEntry>(entry =>
        {
            entry.Property(e => e.ToothNumber).IsRequired().HasMaxLength(10);
            entry.Ignore(e => e.IsCurrent);

            // The chart loads every current finding for one patient at once.
            entry.HasIndex(e => new { e.PatientId, e.SupersededOn });
            entry.HasIndex(e => new { e.PatientId, e.ToothNumber });
        });

        modelBuilder.Entity<ClinicalNote>(note =>
        {
            note.Ignore(n => n.IsLocked);
            note.HasIndex(n => new { n.PatientId, n.TreatmentDateUtc });
            note.HasIndex(n => n.AppointmentId);
        });

        modelBuilder.Entity<PerioExam>(exam =>
        {
            exam.Ignore(e => e.IsComplete);

            // The perio pane opens on the newest exam and compares against the ones before
            // it, so every read is "this patient, newest first".
            exam.HasIndex(e => new { e.PatientId, e.ExamDate });
        });

        modelBuilder.Entity<PerioSiteReading>(reading =>
        {
            reading.Property(r => r.ToothNumber).IsRequired().HasMaxLength(10);
            reading.Ignore(r => r.AttachmentLossMm);

            // One exam's readings are loaded whole; the tooth is in the key because
            // charting writes and re-reads a single tooth at a time.
            reading.HasIndex(r => new { r.PerioExamId, r.ToothNumber });

            // A site is probed once per exam. Enforced rather than trusted: charting the
            // same site twice would double-count it in the mean depth and in the deep-site
            // total, which are the two figures the review actually turns on.
            reading.HasIndex(r => new { r.PerioExamId, r.ToothNumber, r.Site }).IsUnique();
        });

        modelBuilder.Entity<PerioToothReading>(reading =>
        {
            reading.Property(r => r.ToothNumber).IsRequired().HasMaxLength(10);
            reading.HasIndex(r => new { r.PerioExamId, r.ToothNumber }).IsUnique();
        });

        modelBuilder.Entity<TreatmentPlan>(plan =>
        {
            plan.Property(p => p.Title).IsRequired().HasMaxLength(300);
            plan.Ignore(p => p.EstimatedGap);

            // The unscheduled-plan worklist reads "accepted, nothing booked".
            plan.HasIndex(p => new { p.PatientId, p.Status });
            plan.HasIndex(p => p.Status);
        });

        modelBuilder.Entity<TreatmentPlanItem>(item =>
        {
            item.Property(i => i.ItemNumber).IsRequired().HasMaxLength(20);
            item.Property(i => i.Description).IsRequired().HasMaxLength(500);
            item.HasIndex(i => new { i.TreatmentPlanId, i.StageNumber, i.DisplayOrder });
            item.HasIndex(i => i.AppointmentId);
        });
    }

    private static void ConfigureBilling(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>(invoice =>
        {
            invoice.Ignore(i => i.AmountOutstanding);
            invoice.Ignore(i => i.IsSettled);

            invoice.HasIndex(i => new { i.PatientId, i.Status });
            invoice.HasIndex(i => i.InvoiceNumber);
            invoice.HasIndex(i => i.IssuedUtc);

            // The front desk's day query — this site's invoices for one day, where the day
            // is the issue timestamp or, for a draft, the created one.
            //
            // Both dates are in the index so COALESCE(IssuedUtc, CreatedUtc) can be
            // answered from it. Measured at a million invoices: reading every invoice for
            // the site and filtering in memory took 2.4s and materialised a million rows;
            // filtering in SQL against this takes 116ms at constant memory.
            invoice.HasIndex(i => new
            {
                i.TenantId,
                i.PracticeLocationId,
                i.IsDeleted,
                i.IssuedUtc,
                i.CreatedUtc,
            });
        });

        modelBuilder.Entity<InvoiceLine>(
line =>
        {
            line.Property(l => l.ItemNumber).IsRequired().HasMaxLength(20);
            line.Property(l => l.Description).IsRequired().HasMaxLength(500);
            line.HasIndex(l => l.InvoiceId);
            line.HasIndex(l => l.ServiceDate);
        });

        modelBuilder.Entity<Payment>(payment =>
        {
            // End-of-day reconciliation reads one location's takings for one day.
            payment.HasIndex(p => new { p.PracticeLocationId, p.ReceivedUtc });
            payment.HasIndex(p => p.PatientId);
            payment.HasIndex(p => p.InvoiceId);
        });

        modelBuilder.Entity<Claim>(claim =>
        {
            claim.Ignore(c => c.PatientGap);

            // The claims worklist is "submitted or pending", which is what needs chasing.
            claim.HasIndex(c => new { c.Status, c.SubmittedUtc });
            claim.HasIndex(c => c.InvoiceId);
            claim.HasIndex(c => c.PatientId);
        });

        modelBuilder.Entity<FormularyMedicine>(medicine =>
        {
            medicine.Property(m => m.GenericName).IsRequired().HasMaxLength(300);
            medicine.Property(m => m.Strength).HasMaxLength(150);
            medicine.Property(m => m.DefaultDirections).IsRequired().HasMaxLength(1000);
            medicine.Ignore(m => m.Label);

            // The formulary is read whole and searched in memory: a dozen rows, read on
            // every visit to the prescribing screen.
            medicine.HasIndex(m => new { m.IsActive, m.DisplayOrder });
        });

        modelBuilder.Entity<MedicalCertificate>(certificate =>
        {
            certificate.Ignore(c => c.IsIssued);
            certificate.Ignore(c => c.Days);
            certificate.HasIndex(c => new { c.PatientId, c.AttendedOn });
        });

        modelBuilder.Entity<Prescription>(prescription =>
        {
            prescription.HasIndex(p => new { p.PatientId, p.IssuedUtc });
            prescription.HasIndex(p => p.Status);
        });

        modelBuilder.Entity<PrescriptionItem>(item =>
        {
            item.Property(i => i.MedicineName).IsRequired().HasMaxLength(300);
            item.Property(i => i.Strength).HasMaxLength(150);
            item.Property(i => i.Directions).IsRequired().HasMaxLength(1000);
            item.HasIndex(i => i.PrescriptionId);
        });

        modelBuilder.Entity<Referral>(referral =>
        {
            referral.Property(r => r.CounterpartyName).IsRequired().HasMaxLength(300);
            referral.Property(r => r.Reason).IsRequired().HasMaxLength(1000);

            // An outbound referral with no report back is what the list exists to surface.
            referral.HasIndex(r => new { r.Direction, r.Status });
            referral.HasIndex(r => r.PatientId);
        });
    }

    private static void ConfigureInventory(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Supplier>(supplier =>
        {
            supplier.Property(s => s.Name).IsRequired().HasMaxLength(300);
            supplier.HasIndex(s => s.IsLaboratory);
        });

        modelBuilder.Entity<StockItem>(item =>
        {
            item.Property(i => i.Name).IsRequired().HasMaxLength(300);
            item.Ignore(i => i.IsBelowReorderLevel);

            item.HasIndex(i => new { i.PracticeLocationId, i.Category });
            item.HasIndex(i => i.Sku);

            // Expiry is a date, so it does order correctly in SQL — unlike the money
            // columns. The expiry worklist relies on that.
            item.HasIndex(i => i.EarliestExpiry);
        });

        modelBuilder.Entity<PurchaseOrder>(order =>
        {
            order.HasIndex(o => new { o.PracticeLocationId, o.Status });
            order.HasIndex(o => o.SupplierId);
        });

        modelBuilder.Entity<PurchaseOrderLine>(line =>
        {
            line.Property(l => l.Description).IsRequired().HasMaxLength(300);
            line.Ignore(l => l.Outstanding);
            line.Ignore(l => l.IsFullyReceived);
            line.Ignore(l => l.LineTotal);

            line.HasIndex(l => l.PurchaseOrderId);
            line.HasIndex(l => l.StockItemId);
        });

        modelBuilder.Entity<StockMovement>(movement =>
        {
            movement.HasIndex(m => new { m.StockItemId, m.OccurredUtc });

            // A product recall traces a batch to the patients it was used on.
            movement.HasIndex(m => m.BatchNumber);
            movement.HasIndex(m => m.PatientId);
        });

        modelBuilder.Entity<SterilisationCycle>(cycle =>
        {
            cycle.Ignore(c => c.IsReleased);
            cycle.Property(c => c.SterilisorName).IsRequired().HasMaxLength(150);

            // Read back off a pouch months later: machine plus cycle number.
            cycle.HasIndex(c => new { c.SterilisorName, c.CycleNumber });
            cycle.HasIndex(c => new { c.PracticeLocationId, c.StartedUtc });
            cycle.HasIndex(c => c.Result);
        });

        modelBuilder.Entity<SterilisationCycleUse>(use =>
        {
            // Both directions are asked: which patients from a failed cycle, and which
            // cycle for a given patient.
            use.HasIndex(u => u.SterilisationCycleId);
            use.HasIndex(u => u.PatientId);
        });

        modelBuilder.Entity<LabCase>(labCase =>
        {
            labCase.Property(c => c.Description).IsRequired().HasMaxLength(500);

            // The overdue-lab list reads "not yet received, due before today".
            labCase.HasIndex(c => new { c.Status, c.DueOn });
            labCase.HasIndex(c => c.PatientId);
            labCase.HasIndex(c => c.SupplierId);
        });
    }

    private static void ConfigureCommunications(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MessageTemplate>(template =>
        {
            template.Property(t => t.Name).IsRequired().HasMaxLength(200);
            template.Property(t => t.Body).IsRequired();
            template.Property(t => t.Description).HasMaxLength(400);
            template.HasIndex(t => new { t.Channel, t.Purpose });

            // The automated list reads "everything with a trigger", in trigger order.
            template.HasIndex(t => t.Trigger);
        });

        modelBuilder.Entity<CommunicationLog>(log =>
        {
            log.Property(l => l.Body).IsRequired();
            log.HasIndex(l => new { l.PatientId, l.CreatedUtc });

            // Everything queued while offline is read back by status.
            log.HasIndex(l => l.Status);
            log.HasIndex(l => l.AppointmentId);
        });

        modelBuilder.Entity<NotificationSettings>(settings =>
        {
            settings.Ignore(s => s.IsConfigured);
            settings.Property(s => s.SmtpHost).IsRequired().HasMaxLength(200);
            settings.Property(s => s.SenderAddress).HasMaxLength(320);
            settings.Property(s => s.ReplyToAddress).HasMaxLength(320);
            settings.Property(s => s.AlertsToAddress).HasMaxLength(320);
            settings.Property(s => s.DailySummaryRecipients).HasMaxLength(1000);

            // One row per clinic. Unique on the tenant so a second cannot be inserted —
            // two sending accounts for one practice is not a state worth supporting, and
            // the service reading "the first" would silently pick between them.
            settings.HasIndex(s => s.TenantId).IsUnique();
        });

        modelBuilder.Entity<PrinterSettings>(printer =>
        {
            printer.Ignore(p => p.ReceiptPrinterConfigured);
            printer.Ignore(p => p.ReceiptColumns);
            printer.Property(p => p.DeviceAddress).HasMaxLength(200);
            printer.Property(p => p.ReceiptPrinterName).HasMaxLength(120);
            printer.Property(p => p.DocumentPrinterName).HasMaxLength(200);
            printer.Property(p => p.ReceiptFooter).HasMaxLength(500);

            // One row per clinic, like the mail account beside it, and unique for the same
            // reason: the service reads "the first", and two rows would let it choose.
            printer.HasIndex(p => p.TenantId).IsUnique();
        });

        modelBuilder.Entity<AuditEntry>(audit =>
        {
            audit.Property(a => a.EntityName).IsRequired().HasMaxLength(100);

            // The audit screen's own query: this clinic's entries, newest first.
            //
            // Measured before adding it. At a million rows the plan was a scan of the
            // IsDeleted index followed by USE TEMP B-TREE FOR ORDER BY — a million-row sort
            // on every page load, costing 214ms for page one and 576ms for the last. With
            // this it becomes a plain index seek: 0ms and 64ms. The single-column TenantId
            // and IsDeleted indexes added to every table are no help here, because neither
            // can satisfy the ordering.
            //
            // Descending to match the read. An ascending index can be walked backwards by
            // SQLite, but saying it here keeps the index and the query obviously paired.
            audit.HasIndex(a => new { a.TenantId, a.IsDeleted, a.OccurredUtc })
                .IsDescending(false, false, true);

            // "Everything anyone did to this patient" is the question a privacy
            // complaint asks, and it is why PatientId is denormalised onto the entry.
            audit.HasIndex(a => new { a.PatientId, a.OccurredUtc });
            audit.HasIndex(a => new { a.EntityName, a.EntityId });
            audit.HasIndex(a => new { a.ProviderId, a.OccurredUtc });
        });
    }

    /// <summary>
    /// "PatientAlert" to "patient_alert". Handles the acronym case too, so "AbnLookup"
    /// becomes "abn_lookup" rather than "a_b_n_lookup".
    /// </summary>
    private static string ToSnakeCase(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];

            if (char.IsUpper(current) && i > 0)
            {
                var previous = name[i - 1];
                var startsNewWord = !char.IsUpper(previous) ||
                    (i + 1 < name.Length && !char.IsUpper(name[i + 1]));

                if (startsNewWord) builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }
}
