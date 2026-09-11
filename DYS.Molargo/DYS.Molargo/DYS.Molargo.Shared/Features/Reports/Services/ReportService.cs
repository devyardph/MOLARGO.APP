using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Repositories;
using DYS.Molargo.Shared.Services;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Features.Reports.Services;

/// <summary>How far back a report looks.</summary>
public enum ReportPeriod
{
    /// <summary>The current calendar month to date.</summary>
    Month = 0,

    Quarter = 1,
    Year = 2,
}

/// <summary>
/// The window a report covers.
/// </summary>
/// <remarks>
/// To date, not the whole calendar period. A practice reading "this month" at the 9th
/// means the nine days so far, and a figure quietly divided over thirty days would report
/// a third of the real production.
/// </remarks>
public sealed record ReportRange(DateOnly From, DateOnly To, string Label)
{
    public int Days => To.DayNumber - From.DayNumber + 1;

    /// <summary>Days the practice was open, which is what a per-day rate divides by.</summary>
    public int OpenDays
    {
        get
        {
            var count = 0;

            for (var day = From; day <= To; day = day.AddDays(1))
            {
                if (PracticeHours.IsOpenOn(day)) count++;
            }

            return count;
        }
    }

    public bool Contains(DateOnly day) => day >= From && day <= To;

    public bool Contains(DateTime utc)
    {
        var local = DateOnly.FromDateTime(utc.ToLocalTime());
        return Contains(local);
    }
}

/// <summary>The six headline figures the design puts across the top.</summary>
public sealed record KpiSet(
    decimal Production,
    decimal Collections,
    int NewPatients,
    int Appointments,
    int FailedToAttend,
    int PlansPresented,
    int PlansAccepted,
    decimal Outstanding)
{
    /// <summary>
    /// Collections as a share of production.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The number a practice actually watches. Production without it is a claim about work
    /// done, not money received.
    /// </para>
    /// <para>
    /// Null rather than zero on an empty denominator, and so are the two rates below. A
    /// tile reading "0% acceptance" over a period where nothing was presented says nobody
    /// accepts anything; "0% FTA" over a period with no appointments says perfect
    /// attendance. Both are the opposite of the truth, which is that there is nothing to
    /// report.
    /// </para>
    /// </remarks>
    public decimal? CollectionRate => Production <= 0m
        ? null
        : Math.Round(100m * Collections / Production, 1);

    public decimal? FtaRate => Appointments == 0
        ? null
        : Math.Round(100m * FailedToAttend / Appointments, 1);

    public decimal? AcceptanceRate => PlansPresented == 0
        ? null
        : Math.Round(100m * PlansAccepted / PlansPresented, 1);
}

/// <summary>Production earned by one provider.</summary>
public sealed record ProviderProduction(
    Guid ProviderId, string Name, ProviderRole Role, decimal Production, int Items);

/// <summary>Treatment a patient accepted or was shown and has not booked.</summary>
public sealed record UnscheduledRow(
    Guid PatientId,
    Guid TreatmentPlanId,
    string PatientName,
    string PlanTitle,
    decimal Value,
    int Items,
    int DaysSincePresented,
    bool IsAccepted)
{
    /// <summary>"62 days" — the column that decides which one to ring first.</summary>
    public string Aging => DaysSincePresented switch
    {
        < 0 => "not presented",
        0 => "today",
        1 => "1 day",
        < 60 => $"{DaysSincePresented} days",
        _ => $"{DaysSincePresented / 30} months",
    };

    /// <summary>
    /// Accepted and unbooked is worse than merely presented: the patient said yes and the
    /// practice did not book it.
    /// </summary>
    public bool IsUrgent => IsAccepted && DaysSincePresented >= 14;
}

/// <summary>How often one provider's plans are accepted.</summary>
public sealed record AcceptanceRow(
    Guid ProviderId, string Name, int Presented, int Accepted, int Declined)
{
    public int Decided => Accepted + Declined;

    /// <summary>
    /// Accepted over everything shown, including the still-undecided.
    /// </summary>
    /// <remarks>
    /// Over presented rather than over decided. Dividing by decided flatters the figure —
    /// a plan nobody ever answered is not a plan that has yet to be accepted, it is one
    /// the practice failed to follow up.
    /// </remarks>
    public decimal Rate => Presented == 0
        ? 0m
        : Math.Round(100m * Accepted / Presented, 1);
}

/// <summary>Where new patients came from.</summary>
public sealed record SourceRow(
    string Source, int NewPatients, decimal Revenue, int WithRevenue)
{
    public decimal AverageRevenue => WithRevenue == 0
        ? 0m
        : Math.Round(Revenue / WithRevenue, 0);
}

/// <summary>Recalls from due through to attended.</summary>
public sealed record RecallFunnel(int Due, int Contacted, int Booked, int Attended)
{
    public decimal ContactedShare => Share(Contacted);

    public decimal BookedShare => Share(Booked);

    public decimal AttendedShare => Share(Attended);

    private decimal Share(int value) => Due == 0
        ? 0m
        : Math.Round(100m * value / Due, 0);
}

/// <summary>Who turned up and who did not.</summary>
public sealed record AttendanceStats(
    int Total, int FailedToAttend, int Cancelled, int LateCancelled, int Completed)
{
    public decimal? FtaRate => Rate(FailedToAttend);

    public decimal? CancellationRate => Rate(Cancelled);

    public decimal? LateRate => Rate(LateCancelled);

    /// <summary>Null on no appointments — see the note on <see cref="KpiSet"/>.</summary>
    private decimal? Rate(int value) => Total == 0
        ? null
        : Math.Round(100m * value / Total, 1);
}

/// <summary>How busy one chair was.</summary>
public sealed record ChairUse(
    Guid OperatoryId, string Name, int BookedMinutes, int AvailableMinutes)
{
    /// <summary>
    /// Booked minutes as a share of the chair's available minutes.
    /// </summary>
    /// <remarks>
    /// One decimal rather than whole percent. Over a year-to-date window the denominator
    /// is tens of thousands of minutes, and rounding to whole numbers reported every chair
    /// as a flat 0% with an empty bar — which reads as a broken panel rather than as a
    /// quiet one.
    /// </remarks>
    public decimal? Utilisation => AvailableMinutes == 0
        ? null
        : Math.Round(100m * BookedMinutes / AvailableMinutes, 1);

    public decimal BookedHours => Math.Round(BookedMinutes / 60m, 1);
}

/// <summary>One line of the procedure mix.</summary>
public sealed record ProcedureMixRow(
    string Category, int Count, decimal Revenue, decimal Share, decimal AverageFee);

/// <summary>The hygiene department, or any role's slice of the practice.</summary>
public sealed record DepartmentSummary(
    string Name,
    decimal Production,
    decimal Share,
    int Appointments,
    decimal BookedHours)
{
    /// <summary>Production per booked hour — the figure a hygiene column is judged on.</summary>
    public decimal PerHour => BookedHours <= 0m
        ? 0m
        : Math.Round(Production / BookedHours, 0);
}

/// <summary>What the custom builder can total up.</summary>
public enum ReportMetric
{
    Production = 0,
    Collections = 1,
    Appointments = 2,
    FailedToAttend = 3,
    NewPatients = 4,
    PlanValuePresented = 5,
}

/// <summary>What it can total up by.</summary>
public enum ReportGrouping
{
    Provider = 0,
    Day = 1,
    Category = 2,
    Location = 3,
}

/// <summary>
/// One row of a built report.
/// </summary>
/// <param name="Cells">Formatted for the screen — "$700", "6", or "—".</param>
/// <param name="Values">
/// The same figures unformatted, null where the grouping cannot answer the metric.
/// </param>
/// <remarks>
/// Both, rather than formatting at the point of use. The screen wants "$700" and a
/// spreadsheet wants 700 — the export was reusing the display strings, so every money
/// column imported as text and a thousands separator sat inside a comma-separated file.
/// </remarks>
public sealed record BuilderRow(
    string Group, IReadOnlyList<string> Cells, IReadOnlyList<decimal?> Values);

/// <summary>A built report: the chosen metrics as columns, the grouping as rows.</summary>
public sealed record BuilderResult(
    IReadOnlyList<string> Columns, IReadOnlyList<BuilderRow> Rows);

/// <summary>
/// Everything the whole report screen needs for one period and site, fetched together.
/// </summary>
/// <remarks>
/// One object rather than a call per panel. Every figure on the screen is derived from the
/// same invoices, payments and appointments, and fetching them per panel let the tiles and
/// the tables below disagree — a production total that did not match the sum of the
/// provider bars beneath it.
/// </remarks>
public sealed record ReportSet(
    ReportRange Range,
    KpiSet Kpis,
    IReadOnlyList<ProviderProduction> ByProvider,
    IReadOnlyList<UnscheduledRow> Unscheduled,
    IReadOnlyList<AcceptanceRow> Acceptance,
    IReadOnlyList<SourceRow> Sources,
    RecallFunnel Recalls,
    AttendanceStats Attendance,
    IReadOnlyList<ChairUse> Chairs,
    IReadOnlyList<ProcedureMixRow> Mix,
    IReadOnlyList<DepartmentSummary> Departments,
    int BilledVisits)
{
    /// <summary>
    /// Average fee per billed visit.
    /// </summary>
    /// <remarks>
    /// Divided by the invoices that produced the figure, not by appointments marked
    /// complete. Only one of three visits billed today had been marked off — the others
    /// were still in the chair — so dividing by completed reported the whole day's
    /// production as the average fee for one patient.
    /// </remarks>
    public decimal? AveragePerVisit => BilledVisits == 0
        ? null
        : Math.Round(Kpis.Production / BilledVisits, 0);

    /// <summary>Chair utilisation across every chair in scope.</summary>
    public decimal? ChairUtilisation
    {
        get
        {
            var available = Chairs.Sum(chair => chair.AvailableMinutes);

            return available == 0
                ? null
                : Math.Round(100m * Chairs.Sum(chair => chair.BookedMinutes) / available, 1);
        }
    }

    public decimal UnscheduledValue => Unscheduled.Sum(row => row.Value);
}

/// <summary>
/// Practice reporting: production, collections, attendance, acceptance and the mix.
/// </summary>
/// <remarks>
/// Every figure is derived from the ledger at read time. Nothing is stored as a report
/// total, because a stored total is a cache that drifts from the invoices behind it — the
/// same mistake that had the front desk and the billing screen quoting different debt.
/// </remarks>
public interface IReportService
{
    /// <summary>The window a period resolves to, given today.</summary>
    ReportRange RangeFor(ReportPeriod period);

    /// <summary>
    /// Everything the screen shows for one period and site.
    /// </summary>
    /// <param name="locationId">
    /// One location, or null for every location — the design's "all sites" button.
    /// </param>
    Task<ReportSet> GetAsync(
        ReportPeriod period, Guid? locationId = null, CancellationToken ct = default);

    /// <summary>Runs the custom builder over the same period and site.</summary>
    Task<BuilderResult> BuildAsync(
        ReportPeriod period,
        Guid? locationId,
        IReadOnlyList<ReportMetric> metrics,
        ReportGrouping grouping,
        CancellationToken ct = default);
}

/// <inheritdoc cref="IReportService"/>
public sealed class ReportService : IReportService
{
    /// <summary>
    /// How late a cancellation has to be to count as short notice.
    /// </summary>
    /// <remarks>
    /// Twenty-four hours, which is the window a practice can realistically refill from the
    /// short-notice list. Anything inside it is a lost hour rather than a rescheduling.
    /// </remarks>
    private const int LateCancellationHours = 24;

    /// <summary>Rows a grouped report shows before it stops being readable.</summary>
    private const int MaxGroupRows = 40;

    private readonly IRepository<Invoice> _invoices;
    private readonly IRepository<InvoiceLine> _lines;
    private readonly IRepository<Payment> _payments;
    private readonly IRepository<Appointment> _appointments;
    private readonly IRepository<PatientEntity> _patients;
    private readonly IRepository<Provider> _providers;
    private readonly IRepository<Operatory> _operatories;
    private readonly IRepository<PracticeLocation> _locations;
    private readonly IRepository<ProcedureCode> _codes;
    private readonly IRepository<TreatmentPlan> _plans;
    private readonly IRepository<TreatmentPlanItem> _planItems;
    private readonly IRepository<Recall> _recalls;
    private readonly IClock _clock;

    public ReportService(
        IRepository<Invoice> invoices,
        IRepository<InvoiceLine> lines,
        IRepository<Payment> payments,
        IRepository<Appointment> appointments,
        IRepository<PatientEntity> patients,
        IRepository<Provider> providers,
        IRepository<Operatory> operatories,
        IRepository<PracticeLocation> locations,
        IRepository<ProcedureCode> codes,
        IRepository<TreatmentPlan> plans,
        IRepository<TreatmentPlanItem> planItems,
        IRepository<Recall> recalls,
        IClock clock)
    {
        _invoices = invoices;
        _lines = lines;
        _payments = payments;
        _appointments = appointments;
        _patients = patients;
        _providers = providers;
        _operatories = operatories;
        _locations = locations;
        _codes = codes;
        _plans = plans;
        _planItems = planItems;
        _recalls = recalls;
        _clock = clock;
    }

    public ReportRange RangeFor(ReportPeriod period)
    {
        var today = _clock.Today;

        return period switch
        {
            ReportPeriod.Quarter => new ReportRange(
                new DateOnly(today.Year, ((today.Month - 1) / 3 * 3) + 1, 1),
                today,
                $"Q{((today.Month - 1) / 3) + 1} {today.Year} to date"),

            ReportPeriod.Year => new ReportRange(
                new DateOnly(today.Year, 1, 1),
                today,
                $"{today.Year} to date"),

            _ => new ReportRange(
                new DateOnly(today.Year, today.Month, 1),
                today,
                $"{today:MMMM yyyy} to date"),
        };
    }

    public async Task<ReportSet> GetAsync(
        ReportPeriod period, Guid? locationId = null, CancellationToken ct = default)
    {
        var range = RangeFor(period);
        var data = await LoadAsync(range, locationId, ct).ConfigureAwait(false);

        var production = data.Lines.Sum(line => line.LineTotal);

        var kpis = new KpiSet(
            production,
            data.Payments.Sum(payment => payment.Amount),
            data.NewPatientIds.Count,
            data.Appointments.Count,
            data.Appointments.Count(a => a.Status == AppointmentStatus.FailedToAttend),
            data.PlansPresented.Count,
            data.PlansAccepted.Count,
            data.Outstanding);

        return new ReportSet(
            range,
            kpis,
            ByProvider(data),
            Unscheduled(data, range),
            Acceptance(data),
            Sources(data),
            Recalls(data, range),
            Attendance(data),
            Chairs(data, range),
            Mix(data, production),
            Departments(data, production),
            data.Lines.Select(line => line.InvoiceId).Distinct().Count());
    }

    public async Task<BuilderResult> BuildAsync(
        ReportPeriod period,
        Guid? locationId,
        IReadOnlyList<ReportMetric> metrics,
        ReportGrouping grouping,
        CancellationToken ct = default)
    {
        if (metrics.Count == 0) return new BuilderResult([], []);

        var range = RangeFor(period);
        var data = await LoadAsync(range, locationId, ct).ConfigureAwait(false);

        var groups = GroupKeys(data, range, grouping);

        var rows = groups
            .Select(group =>
            {
                var values = metrics
                    .Select(metric => Measure(metric, data, group))
                    .ToList();

                return new BuilderRow(
                    group.Label,
                    values
                        .Select((value, index) => Format(metrics[index], value))
                        .ToList(),
                    values);
            })

            // A row every one of whose metrics is unanswerable carries no information.
            // Rows that are merely zero are kept: a quiet day is a fact.
            .Where(row => row.Values.Any(value => value is not null))
            .Take(MaxGroupRows)
            .ToList();

        return new BuilderResult(
            metrics.Select(MetricLabel).ToList(),
            rows);
    }

    /// <summary>The words staff use for a metric.</summary>
    public static string MetricLabel(ReportMetric metric) => metric switch
    {
        ReportMetric.Production => "Production",
        ReportMetric.Collections => "Collections",
        ReportMetric.Appointments => "Appointments",
        ReportMetric.FailedToAttend => "FTAs",
        ReportMetric.NewPatients => "New patients",
        ReportMetric.PlanValuePresented => "Plan value presented",
        _ => metric.ToString(),
    };

    public static string GroupingLabel(ReportGrouping grouping) => grouping switch
    {
        ReportGrouping.Provider => "Provider",
        ReportGrouping.Day => "Day",
        ReportGrouping.Category => "Procedure category",
        ReportGrouping.Location => "Location",
        _ => grouping.ToString(),
    };

    // ---- the one fetch ---------------------------------------------------

    /// <summary>
    /// Everything in scope, read once.
    /// </summary>
    /// <remarks>
    /// Deliberately a whole-table read filtered in memory rather than a query per panel.
    /// The dataset is one practice's records, the screen needs almost all of it, and the
    /// alternative — twelve queries with twelve slightly different date predicates — is
    /// how two panels on one screen end up disagreeing.
    /// </remarks>
    private async Task<ReportData> LoadAsync(
        ReportRange range, Guid? locationId, CancellationToken ct)
    {
        var invoices = await _invoices.ListAsync(ct: ct).ConfigureAwait(false);
        var allLines = await _lines.ListAsync(ct: ct).ConfigureAwait(false);
        var payments = await _payments.ListAsync(ct: ct).ConfigureAwait(false);
        var appointments = await _appointments.ListAsync(ct: ct).ConfigureAwait(false);
        var patients = await _patients.ListAsync(ct: ct).ConfigureAwait(false);
        var providers = await _providers.ListAsync(ct: ct).ConfigureAwait(false);
        var operatories = await _operatories.ListAsync(o => o.IsActive, ct).ConfigureAwait(false);
        var locations = await _locations.ListAsync(l => l.IsActive, ct).ConfigureAwait(false);
        var codes = await _codes.ListAsync(ct: ct).ConfigureAwait(false);
        var plans = await _plans.ListAsync(ct: ct).ConfigureAwait(false);
        var planItems = await _planItems.ListAsync(ct: ct).ConfigureAwait(false);
        var recalls = await _recalls.ListAsync(ct: ct).ConfigureAwait(false);

        bool AtSite(Guid site) => locationId is null || site == locationId;

        // Voided invoices are not production: the work was reversed. Drafts are excluded
        // too — the day list accumulates empty $0 drafts, and counting them would report
        // production for invoices nobody has issued.
        var countedInvoices = invoices
            .Where(invoice => AtSite(invoice.PracticeLocationId)
                && invoice.Status is not (InvoiceStatus.Voided or InvoiceStatus.Draft))
            .ToList();

        var countedInvoiceIds = countedInvoices.Select(invoice => invoice.Id).ToHashSet();

        // Production is dated by when the work was done, collections by when the money
        // arrived. The two deliberately differ — that gap is the collection rate.
        var lines = allLines
            .Where(line => countedInvoiceIds.Contains(line.InvoiceId)
                && range.Contains(line.ServiceDate))
            .ToList();

        var scopedAppointments = appointments
            .Where(appointment => AtSite(appointment.PracticeLocationId))
            .ToList();

        var inRangeAppointments = scopedAppointments
            .Where(appointment => range.Contains(appointment.StartUtc))
            .ToList();

        // A patient is new when their first-ever appointment falls in the window.
        //
        // Not Patient.CreatedUtc: the seed stamps every sample record in one pass, so that
        // definition reports the whole list as new today and nothing in any other period.
        // First visit is also what a practice means by the phrase.
        var firstVisit = scopedAppointments
            .Where(appointment => appointment.Status != AppointmentStatus.Cancelled)
            .GroupBy(appointment => appointment.PatientId)
            .ToDictionary(
                group => group.Key,
                group => group.Min(appointment => appointment.StartUtc));

        var newPatientIds = firstVisit
            .Where(entry => range.Contains(entry.Value))
            .Select(entry => entry.Key)
            .ToHashSet();

        var scopedPlans = plans
            .Where(plan => AtSite(plan.PracticeLocationId))
            .ToList();

        return new ReportData(
            countedInvoices,
            lines,
            payments
                .Where(payment => AtSite(payment.PracticeLocationId)
                    && range.Contains(payment.ReceivedUtc))
                .ToList(),
            inRangeAppointments,
            scopedAppointments,
            patients.ToDictionary(patient => patient.Id),
            providers.ToDictionary(provider => provider.Id),
            operatories.Where(operatory => AtSite(operatory.PracticeLocationId)).ToList(),
            locations,
            codes.ToDictionary(code => code.Id),
            scopedPlans,
            planItems,
            recalls,
            newPatientIds,
            scopedPlans
                .Where(plan => plan.PresentedUtc is { } presented
                    && range.Contains(presented))
                .ToList(),
            scopedPlans
                .Where(plan => plan.Status is TreatmentPlanStatus.Accepted
                        or TreatmentPlanStatus.InProgress or TreatmentPlanStatus.Completed
                    && plan.DecidedUtc is { } decided && range.Contains(decided))
                .ToList(),
            countedInvoices.Sum(invoice => Math.Max(invoice.AmountOutstanding, 0m)));
    }

    /// <summary>Everything in scope for one period, read once.</summary>
    private sealed record ReportData(
        IReadOnlyList<Invoice> Invoices,
        IReadOnlyList<InvoiceLine> Lines,
        IReadOnlyList<Payment> Payments,
        IReadOnlyList<Appointment> Appointments,
        IReadOnlyList<Appointment> AllAppointments,
        IReadOnlyDictionary<Guid, PatientEntity> Patients,
        IReadOnlyDictionary<Guid, Provider> Providers,
        IReadOnlyList<Operatory> Operatories,
        IReadOnlyList<PracticeLocation> Locations,
        IReadOnlyDictionary<Guid, ProcedureCode> Codes,
        IReadOnlyList<TreatmentPlan> Plans,
        IReadOnlyList<TreatmentPlanItem> PlanItems,
        IReadOnlyList<Recall> Recalls,
        IReadOnlySet<Guid> NewPatientIds,
        IReadOnlyList<TreatmentPlan> PlansPresented,
        IReadOnlyList<TreatmentPlan> PlansAccepted,
        decimal Outstanding);

    // ---- panels ----------------------------------------------------------

    private static IReadOnlyList<ProviderProduction> ByProvider(ReportData data) => data.Lines
        .Where(line => line.ProviderId is not null)
        .GroupBy(line => line.ProviderId!.Value)
        .Select(group => new ProviderProduction(
            group.Key,
            data.Providers.TryGetValue(group.Key, out var provider)
                ? provider.FullName
                : "Unknown provider",
            data.Providers.TryGetValue(group.Key, out var role)
                ? role.Role
                : ProviderRole.Dentist,
            group.Sum(line => line.LineTotal),
            group.Count()))
        .OrderByDescending(row => row.Production)
        .ToList();

    private static IReadOnlyList<UnscheduledRow> Unscheduled(
        ReportData data, ReportRange range)
    {
        // Plans still open, whenever they were presented — not just those presented inside
        // the window. A plan from four months ago that nobody booked is precisely what this
        // list is for, and filtering it to the period would hide the oldest ones.
        var open = data.Plans
            .Where(plan => plan.Status is TreatmentPlanStatus.Presented
                or TreatmentPlanStatus.Accepted or TreatmentPlanStatus.InProgress)
            .ToList();

        var today = range.To;

        return open
            .Select(plan =>
            {
                var items = data.PlanItems
                    .Where(item => item.TreatmentPlanId == plan.Id
                        && item.Status is TreatmentItemStatus.Planned
                            or TreatmentItemStatus.Scheduled)
                    .ToList();

                // Items already on the books are scheduled treatment, not unscheduled.
                var unbooked = items.Where(item => item.AppointmentId is null).ToList();

                return new UnscheduledRow(
                    plan.PatientId,
                    plan.Id,
                    data.Patients.TryGetValue(plan.PatientId, out var patient)
                        ? patient.FullName
                        : "Unknown patient",
                    plan.Title,
                    unbooked.Sum(item => item.Fee * item.Quantity),
                    unbooked.Count,
                    plan.PresentedUtc is { } presented
                        ? today.DayNumber - DateOnly.FromDateTime(presented.ToLocalTime()).DayNumber
                        : -1,
                    plan.Status is not TreatmentPlanStatus.Presented);
            })
            .Where(row => row.Items > 0)
            .OrderByDescending(row => row.IsUrgent)
            .ThenByDescending(row => row.Value)
            .ToList();
    }

    private static IReadOnlyList<AcceptanceRow> Acceptance(ReportData data)
    {
        // Every plan ever presented by each provider, not only those inside the window.
        // Acceptance over a nine-day month is two or three plans, and a rate built on that
        // swings thirty points on one decision.
        var presented = data.Plans
            .Where(plan => plan.Status is not (TreatmentPlanStatus.Draft
                or TreatmentPlanStatus.Void))
            .ToList();

        return presented
            .GroupBy(plan => plan.ProviderId)
            .Select(group => new AcceptanceRow(
                group.Key,
                data.Providers.TryGetValue(group.Key, out var provider)
                    ? provider.FullName
                    : "Unknown provider",
                group.Count(),
                group.Count(plan => plan.Status is TreatmentPlanStatus.Accepted
                    or TreatmentPlanStatus.InProgress or TreatmentPlanStatus.Completed),
                group.Count(plan => plan.Status == TreatmentPlanStatus.Declined)))
            .OrderByDescending(row => row.Rate)
            .ToList();
    }

    private static IReadOnlyList<SourceRow> Sources(ReportData data)
    {
        var newPatients = data.NewPatientIds
            .Select(id => data.Patients.GetValueOrDefault(id))
            .Where(patient => patient is not null)
            .Select(patient => patient!)
            .ToList();

        // Revenue attributed to the patient, not to the period: the question is what a
        // patient from this source is worth, and a month-to-date slice of that is noise.
        var revenue = data.Invoices
            .GroupBy(invoice => invoice.PatientId)
            .ToDictionary(group => group.Key, group => group.Sum(invoice => invoice.Total));

        return newPatients
            .GroupBy(patient => string.IsNullOrWhiteSpace(patient.ReferralSource)
                ? "Not recorded"
                : patient.ReferralSource!)
            .Select(group => new SourceRow(
                group.Key,
                group.Count(),
                group.Sum(patient => revenue.GetValueOrDefault(patient.Id)),
                group.Count(patient => revenue.GetValueOrDefault(patient.Id) > 0m)))
            .OrderByDescending(row => row.NewPatients)
            .ThenBy(row => row.Source)
            .ToList();
    }

    private static RecallFunnel Recalls(ReportData data, ReportRange range)
    {
        // Recalls that fell due on or before the end of the window. A funnel over only
        // those due inside it would report almost nothing: a recall due in March is still
        // the practice's problem in September.
        var due = data.Recalls
            .Where(recall => recall.DueOn <= range.To
                && recall.Status != RecallStatus.Declined)
            .ToList();

        var attended = data.AllAppointments
            .Where(appointment => appointment.Status == AppointmentStatus.Completed)
            .Select(appointment => appointment.Id)
            .ToHashSet();

        return new RecallFunnel(
            due.Count,
            due.Count(recall => recall.LastContactedUtc is not null
                || recall.Status is RecallStatus.Contacted or RecallStatus.Booked),
            due.Count(recall => recall.BookedAppointmentId is not null
                || recall.Status == RecallStatus.Booked),
            due.Count(recall => recall.BookedAppointmentId is { } booked
                && attended.Contains(booked)));
    }

    private static AttendanceStats Attendance(ReportData data) => new(
        data.Appointments.Count,
        data.Appointments.Count(a => a.Status == AppointmentStatus.FailedToAttend),
        data.Appointments.Count(a => a.Status == AppointmentStatus.Cancelled),

        // Short notice, measured from when the cancellation was recorded against when the
        // appointment was due to start.
        data.Appointments.Count(a => a.Status == AppointmentStatus.Cancelled
            && (a.StartUtc - a.UpdatedUtc).TotalHours < LateCancellationHours),
        data.Appointments.Count(a => a.Status == AppointmentStatus.Completed));

    private static IReadOnlyList<ChairUse> Chairs(ReportData data, ReportRange range)
    {
        var openDays = range.OpenDays;

        // What a chair could have been used for: the practice's working day, on every day
        // it was open. Shared with the diary through PracticeHours, so the two screens
        // cannot disagree about how long a day is.
        var available = openDays * PracticeHours.WorkingMinutes;

        return data.Operatories
            .OrderBy(operatory => operatory.DisplayOrder)
            .Select(operatory => new ChairUse(
                operatory.Id,
                operatory.Name,
                data.Appointments
                    .Where(appointment => appointment.OperatoryId == operatory.Id
                        && appointment.Status is not (AppointmentStatus.Cancelled
                            or AppointmentStatus.FailedToAttend))
                    .Sum(appointment => appointment.DurationMinutes),
                available))
            .ToList();
    }

    private static IReadOnlyList<ProcedureMixRow> Mix(ReportData data, decimal production)
    {
        return data.Lines
            .GroupBy(line => line.ProcedureCodeId is { } codeId
                && data.Codes.TryGetValue(codeId, out var code)
                && !string.IsNullOrWhiteSpace(code.Category)
                    ? code.Category!
                    : "Not categorised")
            .Select(group =>
            {
                var revenue = group.Sum(line => line.LineTotal);
                var count = group.Sum(line => line.Quantity);

                return new ProcedureMixRow(
                    group.Key,
                    count,
                    revenue,
                    production <= 0m ? 0m : Math.Round(100m * revenue / production, 0),
                    count == 0 ? 0m : Math.Round(revenue / count, 0));
            })
            .OrderByDescending(row => row.Revenue)
            .ToList();
    }

    private static IReadOnlyList<DepartmentSummary> Departments(
        ReportData data, decimal production)
    {
        var byProvider = ByProvider(data);

        return byProvider
            .GroupBy(row => row.Role)
            .Select(group =>
            {
                var ids = group.Select(row => row.ProviderId).ToHashSet();

                var minutes = data.Appointments
                    .Where(appointment => ids.Contains(appointment.ProviderId)
                        && appointment.Status is not (AppointmentStatus.Cancelled
                            or AppointmentStatus.FailedToAttend))
                    .Sum(appointment => appointment.DurationMinutes);

                var own = group.Sum(row => row.Production);

                return new DepartmentSummary(
                    RoleLabel(group.Key),
                    own,
                    production <= 0m ? 0m : Math.Round(100m * own / production, 0),
                    data.Appointments.Count(a => ids.Contains(a.ProviderId)),
                    Math.Round(minutes / 60m, 1));
            })
            .OrderByDescending(row => row.Production)
            .ToList();
    }

    /// <summary>The department a role belongs to, as a practice would name it.</summary>
    private static string RoleLabel(ProviderRole role) => role switch
    {
        ProviderRole.Dentist => "Dentistry",
        ProviderRole.Hygienist => "Hygiene",
        ProviderRole.OralHealthTherapist => "Oral health therapy",
        ProviderRole.DentalTherapist => "Dental therapy",
        ProviderRole.Prosthetist => "Prosthetics",
        ProviderRole.Specialist => "Specialist",
        ProviderRole.Assistant => "Assisting",
        ProviderRole.Administration => "Administration",
        _ => role.ToString(),
    };

    // ---- the builder -----------------------------------------------------

    /// <summary>
    /// One row of a built report: its label, and exactly one dimension set.
    /// </summary>
    /// <remarks>
    /// Explicit nullable keys rather than a match predicate. A predicate cannot be asked
    /// which dimension it is, and the collections metric needs to know — a payment has no
    /// provider, so grouping by provider has no answer rather than a zero.
    /// </remarks>
    private sealed record GroupKey(string Label)
    {
        public Guid? ProviderId { get; init; }

        public DateOnly? Day { get; init; }

        public string? Category { get; init; }

        public Guid? LocationId { get; init; }
    }

    private static IReadOnlyList<GroupKey> GroupKeys(
        ReportData data, ReportRange range, ReportGrouping grouping)
    {
        switch (grouping)
        {
            case ReportGrouping.Day:
            {
                var days = new List<GroupKey>();

                for (var day = range.From; day <= range.To; day = day.AddDays(1))
                {
                    if (!PracticeHours.IsOpenOn(day)) continue;

                    days.Add(new GroupKey(day.ToString("ddd d MMM")) { Day = day });
                }

                // Newest first: a to-date report is read from the most recent day back.
                days.Reverse();
                return days;
            }

            case ReportGrouping.Category:
                return data.Codes.Values
                    .Select(code => code.Category)
                    .Where(category => !string.IsNullOrWhiteSpace(category))
                    .Select(category => category!)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(category => category, StringComparer.Ordinal)
                    .Select(category => new GroupKey(category) { Category = category })
                    .ToList();

            case ReportGrouping.Location:
                return data.Locations
                    .OrderBy(location => location.DisplayOrder)
                    .Select(location => new GroupKey(location.ShortName ?? location.Name)
                    {
                        LocationId = location.Id,
                    })
                    .ToList();

            default:
                return data.Providers.Values
                    .Where(provider => provider.IsActive && ProviderRoles.IsClinical(provider.Role))
                    .OrderBy(provider => provider.LastName)
                    .Select(provider => new GroupKey(provider.FullName)
                    {
                        ProviderId = provider.Id,
                    })
                    .ToList();
        }
    }

    private static decimal? Measure(ReportMetric metric, ReportData data, GroupKey group)
    {
        var lines = data.Lines.Where(line => Matches(line, data, group)).ToList();
        var appointments = data.Appointments.Where(a => Matches(a, data, group)).ToList();

        switch (metric)
        {
            case ReportMetric.Production:
                return lines.Sum(line => line.LineTotal);

            case ReportMetric.Collections:
                // A payment settles an invoice; it carries no provider and no procedure
                // category. Those groupings have no answer rather than a zero — a zero in
                // a money column reads as "collected nothing", which is a different and
                // alarming claim.
                if (group.Category is not null || group.ProviderId is not null) return null;

                return data.Payments
                    .Where(payment => (group.Day is null
                            || DateOnly.FromDateTime(payment.ReceivedUtc.ToLocalTime()) == group.Day)
                        && (group.LocationId is null
                            || payment.PracticeLocationId == group.LocationId))
                    .Sum(payment => payment.Amount);

            case ReportMetric.Appointments:
                return group.Category is not null ? null : appointments.Count;

            case ReportMetric.FailedToAttend:
                return group.Category is not null
                    ? null
                    : appointments.Count(a => a.Status == AppointmentStatus.FailedToAttend);

            case ReportMetric.NewPatients:
                if (group.Category is not null) return null;

                return appointments
                    .Where(a => data.NewPatientIds.Contains(a.PatientId))
                    .Select(a => a.PatientId)
                    .Distinct()
                    .Count();

            case ReportMetric.PlanValuePresented:
            {
                if (group.Category is not null || group.Day is not null) return null;

                var plans = data.PlansPresented
                    .Where(plan => group.LocationId is { } locationId
                        ? plan.PracticeLocationId == locationId
                        : plan.ProviderId == group.ProviderId)
                    .ToList();

                return plans.Sum(plan => plan.QuotedTotal);
            }

            default:
                return null;
        }
    }

    private static bool Matches(InvoiceLine line, ReportData data, GroupKey group)
    {
        if (group.Day is { } day) return line.ServiceDate == day;

        if (group.Category is { } category)
        {
            return line.ProcedureCodeId is { } codeId
                && data.Codes.TryGetValue(codeId, out var code)
                && string.Equals(code.Category, category, StringComparison.Ordinal);
        }

        if (group.LocationId is { } locationId)
        {
            var invoice = data.Invoices.FirstOrDefault(entry => entry.Id == line.InvoiceId);
            return invoice?.PracticeLocationId == locationId;
        }

        return line.ProviderId == group.ProviderId;
    }

    private static bool Matches(Appointment appointment, ReportData data, GroupKey group)
    {
        if (group.Day is { } day)
        {
            return DateOnly.FromDateTime(appointment.StartUtc.ToLocalTime()) == day;
        }

        if (group.LocationId is { } locationId)
        {
            return appointment.PracticeLocationId == locationId;
        }

        return appointment.ProviderId == group.ProviderId;
    }

    /// <summary>
    /// A measured cell, or an em dash where the grouping cannot answer the metric.
    /// </summary>
    /// <remarks>
    /// Absent and zero must look different. "Collections by procedure category" has no
    /// answer — a payment settles an invoice, not a filling — and printing $0 there would
    /// read as a category that collected nothing.
    /// </remarks>
    private static string Format(ReportMetric metric, decimal? value) => value switch
    {
        null => "—",

        { } money when metric is ReportMetric.Production or ReportMetric.Collections
            or ReportMetric.PlanValuePresented => money.ToString("C0"),

        { } count => count.ToString("0"),
    };
}
