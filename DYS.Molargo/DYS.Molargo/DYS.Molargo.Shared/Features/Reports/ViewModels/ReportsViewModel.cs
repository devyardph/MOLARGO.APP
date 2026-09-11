using System.Globalization;
using System.Text;
using DYS.Molargo.Shared.Features.Reports.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Reports.ViewModels;

/// <summary>Which pane of the reports screen is showing.</summary>
public enum ReportTab
{
    Overview = 0,
    Patients = 1,
    Operations = 2,
    Mix = 3,
    Builder = 4,
}

/// <summary>
/// Practice reporting: production, collections, attendance, acceptance and the mix.
/// </summary>
public sealed class ReportsViewModel : BaseViewModel, IDisposable
{
    private readonly IReportService _reports;
    private readonly ISessionService _session;
    private readonly IAppNavigator _navigator;

    private ReportTab _tab = ReportTab.Overview;
    private ReportPeriod _period = ReportPeriod.Month;

    /// <summary>Null means every location — the design's "all sites".</summary>
    private Guid? _locationId;

    private bool _allSites;
    private ReportSet? _set;

    private readonly List<ReportMetric> _metrics =
        [ReportMetric.Production, ReportMetric.Appointments];

    private ReportGrouping _grouping = ReportGrouping.Provider;
    private BuilderResult? _built;

    public ReportsViewModel(
        IReportService reports,
        ISessionService session,
        IAppNavigator navigator)
    {
        _reports = reports;
        _session = session;
        _navigator = navigator;

        // Built once, in the constructor — never rebuilt per render.
        SelectTabCommand = new MvxAsyncCommand<ReportTab>(SelectTabAsync);
        SetPeriodCommand = new MvxAsyncCommand<ReportPeriod>(SetPeriodAsync);
        SetSiteCommand = new MvxAsyncCommand<Guid>(SetSiteAsync);
        AllSitesCommand = new MvxAsyncCommand(AllSitesAsync);

        ToggleMetricCommand = new MvxAsyncCommand<ReportMetric>(ToggleMetricAsync);
        SetGroupingCommand = new MvxAsyncCommand<ReportGrouping>(SetGroupingAsync);

        OpenPatientCommand = new MvxCommand<Guid>(id => _navigator.ToPatientRecord(id));
        OpenDiaryCommand = new MvxCommand(() => _navigator.ToDiary());
        OpenBillingCommand = new MvxCommand(() => _navigator.ToBilling());

        _session.Changed += OnSessionChanged;
    }

    public IMvxAsyncCommand<ReportTab> SelectTabCommand { get; }

    public IMvxAsyncCommand<ReportPeriod> SetPeriodCommand { get; }

    public IMvxAsyncCommand<Guid> SetSiteCommand { get; }

    public IMvxAsyncCommand AllSitesCommand { get; }

    public IMvxAsyncCommand<ReportMetric> ToggleMetricCommand { get; }

    public IMvxAsyncCommand<ReportGrouping> SetGroupingCommand { get; }

    public IMvxCommand<Guid> OpenPatientCommand { get; }

    public IMvxCommand OpenDiaryCommand { get; }

    public IMvxCommand OpenBillingCommand { get; }

    public override Task Initialize()
    {
        _locationId = _session.LocationId;

        return LoadAsync();
    }

    public ReportTab Tab => _tab;

    public ReportPeriod Period => _period;

    public ReportSet? Set => _set;

    public bool HasData => _set is not null;

    public string RangeLabel => _set?.Range.Label ?? string.Empty;

    // ---- period and site -------------------------------------------------

    public static readonly ReportPeriod[] Periods =
    [
        ReportPeriod.Month,
        ReportPeriod.Quarter,
        ReportPeriod.Year,
    ];

    public static string PeriodLabel(ReportPeriod period) => period switch
    {
        ReportPeriod.Month => "Month",
        ReportPeriod.Quarter => "Quarter",
        ReportPeriod.Year => "Year",
        _ => period.ToString(),
    };

    public IReadOnlyList<SessionLocation> Sites => _session.Locations;

    public bool IsAllSites => _allSites;

    public bool IsSite(Guid locationId) => !_allSites && _locationId == locationId;

    /// <summary>The site the figures cover, for the header.</summary>
    public string SiteLabel => _allSites
        ? "All locations"
        : _session.Locations
            .FirstOrDefault(location => location.Id == _locationId)?.Name
            ?? "This location";

    // ---- builder ---------------------------------------------------------

    public static readonly ReportMetric[] AllMetrics =
    [
        ReportMetric.Production,
        ReportMetric.Collections,
        ReportMetric.Appointments,
        ReportMetric.FailedToAttend,
        ReportMetric.NewPatients,
        ReportMetric.PlanValuePresented,
    ];

    public static readonly ReportGrouping[] AllGroupings =
    [
        ReportGrouping.Provider,
        ReportGrouping.Day,
        ReportGrouping.Category,
        ReportGrouping.Location,
    ];

    public IReadOnlyList<ReportMetric> Metrics => _metrics;

    public bool IsMetricOn(ReportMetric metric) => _metrics.Contains(metric);

    public ReportGrouping Grouping => _grouping;

    public BuilderResult? Built => _built;

    /// <summary>
    /// Metrics the current grouping cannot answer, named so the empty column is explained.
    /// </summary>
    /// <remarks>
    /// Collections has no provider and no procedure category behind it; plan value has no
    /// day. Rather than let the table fill with em dashes and look broken, the pane says
    /// which combination is the problem.
    /// </remarks>
    public IReadOnlyList<string> UnanswerableMetrics => _metrics
        .Where(metric => !CanAnswer(metric, _grouping))
        .Select(ReportService.MetricLabel)
        .ToList();

    private static bool CanAnswer(ReportMetric metric, ReportGrouping grouping) => metric switch
    {
        ReportMetric.Collections => grouping is ReportGrouping.Day or ReportGrouping.Location,

        ReportMetric.PlanValuePresented => grouping is ReportGrouping.Provider
            or ReportGrouping.Location,

        ReportMetric.Production => true,

        _ => grouping is not ReportGrouping.Category,
    };

    // ---- export ----------------------------------------------------------

    /// <summary>
    /// The current pane as CSV, ready for a download link.
    /// </summary>
    /// <remarks>
    /// A <c>data:</c> URI on a plain anchor rather than JS interop. The app has no
    /// interop anywhere else, and adding it for one button means a second code path that
    /// behaves differently under prerender on the web head and inside the MAUI WebView.
    /// An anchor with a download attribute works identically in both.
    /// </remarks>
    public string CsvDataUri
    {
        get
        {
            var csv = BuildCsv();

            // Base64 rather than percent-encoding: a report contains commas, quotes and
            // newlines in every row, and percent-encoding all of them is both longer and
            // easier to get wrong.
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(csv));

            return "data:text/csv;charset=utf-8;base64," + encoded;
        }
    }

    public string CsvFileName
    {
        get
        {
            var pane = _tab switch
            {
                ReportTab.Patients => "new-patients",
                ReportTab.Operations => "operations",
                ReportTab.Mix => "procedure-mix",
                ReportTab.Builder => "custom",
                _ => "overview",
            };

            var from = _set?.Range.From.ToString("yyyy-MM-dd") ?? "range";

            return $"molargo-{pane}-{from}.csv";
        }
    }

    /// <summary>
    /// The active pane's table, as CSV.
    /// </summary>
    /// <remarks>
    /// Per pane, matching the design's note that exports honour the current selection. One
    /// combined export would put six unrelated tables in one file and none of them would
    /// open as a spreadsheet.
    /// </remarks>
    private string BuildCsv()
    {
        var csv = new StringBuilder();

        void Row(params string[] cells) =>
            csv.AppendLine(string.Join(",", cells.Select(Escape)));

        Row("Molargo report", _tab.ToString(), SiteLabel, RangeLabel);
        csv.AppendLine();

        if (_set is not { } set)
        {
            Row("No data");
            return csv.ToString();
        }

        switch (_tab)
        {
            case ReportTab.Patients:
                Row("Source", "New patients", "Lifetime revenue", "Average revenue");

                foreach (var row in set.Sources)
                {
                    Row(row.Source, Number(row.NewPatients), Money(row.Revenue),
                        Money(row.AverageRevenue));
                }

                break;

            case ReportTab.Operations:
                Row("Recall funnel", "Count", "Share of due");
                Row("Due", Number(set.Recalls.Due), "100%");
                Row("Contacted", Number(set.Recalls.Contacted), Percent(set.Recalls.ContactedShare));
                Row("Booked", Number(set.Recalls.Booked), Percent(set.Recalls.BookedShare));
                Row("Attended", Number(set.Recalls.Attended), Percent(set.Recalls.AttendedShare));
                csv.AppendLine();

                Row("Attendance", "Count", "Rate");
                Row("Appointments", Number(set.Attendance.Total), string.Empty);
                Row("Failed to attend", Number(set.Attendance.FailedToAttend),
                    Percent(set.Attendance.FtaRate));
                Row("Cancelled", Number(set.Attendance.Cancelled),
                    Percent(set.Attendance.CancellationRate));
                Row("Cancelled inside 24h", Number(set.Attendance.LateCancelled),
                    Percent(set.Attendance.LateRate));
                csv.AppendLine();

                Row("Chair", "Booked hours", "Utilisation");

                foreach (var chair in set.Chairs)
                {
                    Row(chair.Name, chair.BookedHours.ToString("0.#", Invariant),
                        Percent(chair.Utilisation));
                }

                break;

            case ReportTab.Mix:
                Row("Category", "Count", "Revenue", "Share of production", "Average fee");

                foreach (var row in set.Mix)
                {
                    Row(row.Category, Number(row.Count), Money(row.Revenue),
                        Percent(row.Share), Money(row.AverageFee));
                }

                break;

            case ReportTab.Builder:
                if (_built is { } built)
                {
                    Row([ReportService.GroupingLabel(_grouping), .. built.Columns]);

                    foreach (var row in built.Rows)
                    {
                        // The raw values, not the display cells: "$700" imports as text
                        // and its thousands separator sits inside a comma-separated file.
                        Row([row.Group, .. row.Values.Select(Figure)]);
                    }
                }

                break;

            default:
                Row("Measure", "Value");
                Row("Production", Money(set.Kpis.Production));
                Row("Collections", Money(set.Kpis.Collections));
                Row("Collection rate", Percent(set.Kpis.CollectionRate));
                Row("New patients", Number(set.Kpis.NewPatients));
                Row("Appointments", Number(set.Kpis.Appointments));
                Row("FTA rate", Percent(set.Kpis.FtaRate));
                Row("Plan acceptance", Percent(set.Kpis.AcceptanceRate));
                Row("Outstanding", Money(set.Kpis.Outstanding));
                csv.AppendLine();

                Row("Provider", "Production", "Items");

                foreach (var row in set.ByProvider)
                {
                    Row(row.Name, Money(row.Production), Number(row.Items));
                }

                csv.AppendLine();
                Row("Patient", "Plan", "Unscheduled value", "Aging");

                foreach (var row in set.Unscheduled)
                {
                    Row(row.PatientName, row.PlanTitle, Money(row.Value), row.Aging);
                }

                break;
        }

        return csv.ToString();
    }

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>
    /// Figures written for a spreadsheet, not for a person.
    /// </summary>
    /// <remarks>
    /// No currency symbol, no thousands separator, invariant decimal point. "$1,234.00"
    /// imports as text in every spreadsheet, and a comma inside a number breaks the column
    /// alignment of a comma-separated file even when quoted.
    /// </remarks>
    private static string Money(decimal value) => value.ToString("0.00", Invariant);

    private static string Number(int value) => value.ToString(Invariant);

    /// <summary>
    /// A builder cell for a spreadsheet: the bare figure, or blank where unanswerable.
    /// </summary>
    /// <remarks>
    /// Two decimals only where there are cents to keep. An appointment count written
    /// "6.00" is technically right and reads as a mistake in every column beside it.
    /// </remarks>
    private static string Figure(decimal? value) => value switch
    {
        null => string.Empty,
        { } figure when figure == decimal.Truncate(figure) => figure.ToString("0", Invariant),
        { } figure => figure.ToString("0.00", Invariant),
    };

    /// <summary>
    /// A rate for a spreadsheet, blank where there was nothing to divide by.
    /// </summary>
    /// <remarks>
    /// Blank rather than "0" or "—". A zero would import as a real rate and average into
    /// whatever the reader builds on top of it, which is the same misreading the screen
    /// avoids by showing a dash.
    /// </remarks>
    private static string Percent(decimal? value) =>
        value is { } rate ? rate.ToString("0.#", Invariant) : string.Empty;

    /// <summary>Quotes a cell if it carries a comma, a quote or a newline.</summary>
    private static string Escape(string cell)
    {
        if (cell.Length == 0) return cell;

        var needsQuotes = cell.Contains(',', StringComparison.Ordinal)
            || cell.Contains('"', StringComparison.Ordinal)
            || cell.Contains('\n', StringComparison.Ordinal)
            || cell.Contains('\r', StringComparison.Ordinal);

        if (!needsQuotes) return cell;

        return "\"" + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    // ---- loading ---------------------------------------------------------

    private Task LoadAsync() => RunGuardedAsync(LoadSetAsync);

    private async Task LoadSetAsync()
    {
        _set = await _reports
            .GetAsync(_period, _allSites ? null : _locationId)
            .ConfigureAwait(false);

        if (_tab == ReportTab.Builder)
        {
            _built = await _reports
                .BuildAsync(_period, _allSites ? null : _locationId, _metrics, _grouping)
                .ConfigureAwait(false);
        }

        RaiseAll();
    }

    private async Task SelectTabAsync(ReportTab tab)
    {
        _tab = tab;

        await RaisePropertyChanged(nameof(Tab)).ConfigureAwait(false);
        await LoadAsync().ConfigureAwait(false);
    }

    private async Task SetPeriodAsync(ReportPeriod period)
    {
        _period = period;

        await RaisePropertyChanged(nameof(Period)).ConfigureAwait(false);
        await LoadAsync().ConfigureAwait(false);
    }

    private async Task SetSiteAsync(Guid locationId)
    {
        _locationId = locationId;
        _allSites = false;

        await LoadAsync().ConfigureAwait(false);
    }

    private async Task AllSitesAsync()
    {
        _allSites = true;

        await LoadAsync().ConfigureAwait(false);
    }

    private Task ToggleMetricAsync(ReportMetric metric) => RunGuardedAsync(async () =>
    {
        if (!_metrics.Remove(metric)) _metrics.Add(metric);

        // Kept in the catalogue's order rather than the order they were clicked, so the
        // columns do not shuffle as the selection changes.
        _metrics.Sort();

        await LoadSetAsync().ConfigureAwait(false);
    });

    private Task SetGroupingAsync(ReportGrouping grouping) => RunGuardedAsync(async () =>
    {
        _grouping = grouping;

        await LoadSetAsync().ConfigureAwait(false);
    });

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        // The app bar's own location switcher moved. A report pinned to a location the
        // user has just left would keep reporting the old site's figures under the new
        // site's name in the header.
        if (!_allSites) _locationId = _session.LocationId;

        _ = LoadAsync();
    }

    public void Dispose() => _session.Changed -= OnSessionChanged;

    /// <summary>
    /// Everything the screen reads. Listed explicitly rather than raising a blanket change,
    /// which would re-render the whole grid on every keystroke elsewhere.
    /// </summary>
    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(Tab), nameof(Period), nameof(Set), nameof(HasData), nameof(RangeLabel),
            nameof(IsAllSites), nameof(SiteLabel), nameof(Sites), nameof(Metrics),
            nameof(Grouping), nameof(Built), nameof(UnanswerableMetrics),
            nameof(CsvDataUri), nameof(CsvFileName),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
