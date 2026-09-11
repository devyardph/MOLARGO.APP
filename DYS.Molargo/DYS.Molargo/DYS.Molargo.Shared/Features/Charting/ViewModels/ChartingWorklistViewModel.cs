using DYS.Molargo.Domain.Dtos;
using DYS.Molargo.Shared.Components;
using DYS.Molargo.Shared.Features.FrontDesk.Services;
using DYS.Molargo.Shared.Features.Patient.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Charting.ViewModels;

/// <summary>
/// The Charting section's landing screen: whose chart to open.
/// </summary>
/// <remarks>
/// The chart itself is per-patient, so the nav section needs somewhere to stand. Today's
/// list is that place — a clinician arriving at Charting is nearly always opening the chart
/// of someone in the building — with a search for the exception.
/// </remarks>
public sealed class ChartingWorklistViewModel : BaseViewModel, IDisposable
{
    private readonly IFrontDeskService _frontDesk;
    private readonly IPatientService _patients;
    private readonly ISessionService _session;
    private readonly IAppNavigator _navigator;
    private readonly IClock _clock;

    private FrontDeskDay? _day;
    private string _searchTerm = string.Empty;
    private IReadOnlyList<PatientListItemDto> _results = [];

    public ChartingWorklistViewModel(
        IFrontDeskService frontDesk,
        IPatientService patients,
        ISessionService session,
        IAppNavigator navigator,
        IClock clock)
    {
        _frontDesk = frontDesk;
        _patients = patients;
        _session = session;
        _navigator = navigator;
        _clock = clock;

        // Built once, in the constructor — never rebuilt per render.
        OpenChartCommand = new MvxCommand<Guid>(id => _navigator.ToChart(id));
        OpenRecordCommand = new MvxCommand<Guid>(id => _navigator.ToPatientRecord(id));
        SearchCommand = new MvxAsyncCommand(SearchAsync);

        _session.Changed += OnSessionChanged;
    }

    public IMvxCommand<Guid> OpenChartCommand { get; }

    public IMvxCommand<Guid> OpenRecordCommand { get; }

    public IMvxAsyncCommand SearchCommand { get; }

    public override Task Initialize() => LoadAsync();

    /// <summary>
    /// Today's list, reusing the front desk's own day.
    /// </summary>
    /// <remarks>
    /// Deliberately the same query as the front desk rather than a charting-specific one:
    /// "who is in today, in time order, and how far through their visit" is one fact about
    /// the day, and two implementations of it would eventually disagree about the day the
    /// practice is actually having.
    /// </remarks>
    public IReadOnlyList<FrontDeskArrival> Today => _day?.Arrivals ?? [];

    public string DayLabel => MolargoFormat.DayLabel(_clock.Today);

    public string SearchTerm
    {
        get => _searchTerm;
        set => SetProperty(ref _searchTerm, value);
    }

    public IReadOnlyList<PatientListItemDto> Results => _results;

    public bool HasSearch => !string.IsNullOrWhiteSpace(_searchTerm);

    private Task LoadAsync() => RunGuardedAsync(async () =>
    {
        _day = await _frontDesk
            .GetDayAsync(_session.LocationId, _clock.Today)
            .ConfigureAwait(false);

        await RaisePropertyChanged(nameof(Today)).ConfigureAwait(false);
        await RaisePropertyChanged(nameof(DayLabel)).ConfigureAwait(false);
    });

    private Task SearchAsync() => RunGuardedAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(_searchTerm))
        {
            _results = [];
        }
        else
        {
            var page = await _patients
                .SearchAsync(new PatientQuery { SearchTerm = _searchTerm.Trim(), PageSize = 10 })
                .ConfigureAwait(false);

            _results = page.Items;
        }

        await RaisePropertyChanged(nameof(Results)).ConfigureAwait(false);
        await RaisePropertyChanged(nameof(HasSearch)).ConfigureAwait(false);
    });

    private void OnSessionChanged(object? sender, EventArgs e) => _ = LoadAsync();

    public void Dispose() => _session.Changed -= OnSessionChanged;
}
