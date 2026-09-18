using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Dtos;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Features.Patient.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Patient.ViewModels;

/// <summary>
/// The patients list: search, filter, page, and open a record.
/// </summary>
public sealed class PatientsViewModel : BaseViewModel
{
    /// <summary>
    /// How long to wait after the last keystroke before searching. Long enough that
    /// typing a surname is one query rather than seven; short enough that it still feels
    /// like the list is following along.
    /// </summary>
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);

    private readonly IPatientService _patients;
    private readonly IAppNavigator _navigator;
    private readonly IClock _clock;

    private readonly PatientQuery _query = new();

    /// <summary>
    /// Cancels the in-flight search when a newer keystroke supersedes it. Without this,
    /// two searches race and the slower one — for a shorter, less specific term — can
    /// land last and overwrite the results for what the user actually typed.
    /// </summary>
    private CancellationTokenSource? _searchCts;

    private PagedResult<PatientListItemDto> _page = PagedResult<PatientListItemDto>.Empty(10);
    private string _searchTerm = string.Empty;

    public PatientsViewModel(IPatientService patients, IAppNavigator navigator, IClock clock)
    {
        _patients = patients;
        _navigator = navigator;
        _clock = clock;

        // Built once, in the constructor — never rebuilt per render.
        OpenCommand = new MvxCommand<Guid>(id => _navigator.ToPatientRecord(id));
        NewPatientCommand = new MvxCommand(() => _navigator.ToNewPatient());
        RefreshCommand = new MvxAsyncCommand(() => LoadAsync(CancellationToken.None));
        NextPageCommand = new MvxAsyncCommand(() => GoToPageAsync(_query.Page + 1), () => HasNextPage);
        PreviousPageCommand = new MvxAsyncCommand(() => GoToPageAsync(_query.Page - 1), () => HasPreviousPage);
        GoToPageCommand = new MvxAsyncCommand<int>(GoToPageAsync);
        ApplyFilterCommand = new MvxAsyncCommand<PatientListFilter>(ApplyFilterAsync);
    }

    public IMvxCommand<Guid> OpenCommand { get; }

    public IMvxCommand NewPatientCommand { get; }

    public IMvxAsyncCommand RefreshCommand { get; }

    public IMvxAsyncCommand NextPageCommand { get; }

    public IMvxAsyncCommand PreviousPageCommand { get; }

    public IMvxAsyncCommand<int> GoToPageCommand { get; }

    public IMvxAsyncCommand<PatientListFilter> ApplyFilterCommand { get; }

    public override Task Initialize() => LoadAsync(CancellationToken.None);

    /// <summary>
    /// Seeds the search term before the first load, for a screen arrived at from the app
    /// bar's search with <c>?search=</c> in the URL.
    /// </summary>
    /// <remarks>
    /// Sets the field directly rather than going through <see cref="SearchTerm"/>, which
    /// would start a debounced load. Assigning the property instead meant the screen
    /// prerendered the whole unfiltered list, then replaced it 300ms later — two loads and
    /// a visible flash of the wrong patients.
    /// </remarks>
    public void SetInitialSearch(string term)
    {
        if (string.IsNullOrWhiteSpace(term)) return;

        _searchTerm = term;
        _query.SearchTerm = term;
        _query.Page = 0;

        RaisePropertyChanged(nameof(SearchTerm));
    }

    /// <summary>
    /// The search box. Setting it starts a debounced search rather than searching on every
    /// character.
    /// </summary>
    public string SearchTerm
    {
        get => _searchTerm;
        set
        {
            if (!SetProperty(ref _searchTerm, value)) return;

            // Fire and forget: the setter is called from a bound input and cannot await.
            // Every failure path inside is handled by RunGuardedAsync.
            _ = DebouncedSearchAsync(value);
        }
    }

    public IReadOnlyList<PatientListItemDto> Items => _page.Items;

    public int TotalCount => _page.TotalCount;

    public int Page => _page.Page;

    public int PageCount => _page.PageCount;

    public bool HasNextPage => _page.Page + 1 < _page.PageCount;

    public bool HasPreviousPage => _page.Page > 0;

    /// <summary>
    /// True when the search really found nothing, as opposed to not having loaded yet.
    /// Distinguished so the view can say "no matches" instead of flashing it during the
    /// first load.
    /// </summary>
    public bool IsEmpty => !IsBusy && _page.TotalCount == 0;

    public PatientStatus? StatusFilter => _query.Status;

    public bool OutstandingOnly => _query.OutstandingBalanceOnly;

    /// <summary>"1–10 of 24 patients", the prototype's own wording. "No patients" when empty.</summary>
    public string RangeLabel
    {
        get
        {
            if (_page.TotalCount == 0) return "No patients";

            var first = (_page.Page * _page.PageSize) + 1;
            var last = Math.Min(first + _page.Items.Count - 1, _page.TotalCount);

            // En dash, as the design has it — it is a range, not a subtraction.
            return $"{first}–{last} of {_page.TotalCount} patients";
        }
    }

    /// <summary>Today in the practice's local zone, for the age column.</summary>
    public DateOnly Today => _clock.Today;

    private async Task DebouncedSearchAsync(string term)
    {
        var cts = ResetSearchToken();

        try
        {
            await Task.Delay(SearchDebounce, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke arrived inside the debounce window. Nothing to do — that
            // keystroke has already started its own delay.
            return;
        }

        // A new search starts from the first page. Keeping the page index would ask for
        // page 3 of a result set that may only have one.
        _query.Page = 0;
        _query.SearchTerm = term;

        await LoadAsync(cts.Token).ConfigureAwait(false);
    }

    private CancellationTokenSource ResetSearchToken()
    {
        var previous = _searchCts;
        _searchCts = new CancellationTokenSource();

        previous?.Cancel();
        previous?.Dispose();

        return _searchCts;
    }

    private Task GoToPageAsync(int page)
    {
        if (page < 0 || page >= _page.PageCount) return Task.CompletedTask;

        _query.Page = page;
        return LoadAsync(CancellationToken.None);
    }

    /// <summary>
    /// Takes a nullable filter because <c>MvxAsyncCommand&lt;T&gt;</c>'s delegate does —
    /// the command can be executed with no argument. Null is read as the default
    /// segment, which is what the "All" button passes anyway.
    /// </summary>
    private Task ApplyFilterAsync(PatientListFilter? filter)
    {
        _query.Status = filter?.Status;
        _query.OutstandingBalanceOnly = filter?.OutstandingOnly ?? false;

        // A new filter starts from the first page. Keeping the index would ask for page 3
        // of a result set that may only have one.
        _query.Page = 0;

        return LoadAsync(CancellationToken.None);
    }

    private Task LoadAsync(CancellationToken ct) => RunGuardedAsync(async () =>
    {
        var result = await _patients.SearchAsync(_query, ct).ConfigureAwait(false);

        // The repository clamps a stale page index, so the query has to adopt whatever
        // page actually came back — otherwise "next" from a clamped page goes nowhere.
        _query.Page = result.Page;

        _page = result;
        RaiseAllDerived();
    });

    /// <summary>
    /// Everything on this screen is computed from the loaded page, so one load has to
    /// announce all of it. Listed explicitly rather than raising a blanket change, which
    /// would re-render every binding including the search box the user is typing in.
    /// </summary>
    private void RaiseAllDerived()
    {
        foreach (var name in new[]
        {
            nameof(Items), nameof(TotalCount), nameof(Page), nameof(PageCount),
            nameof(HasNextPage), nameof(HasPreviousPage), nameof(IsEmpty),
            nameof(StatusFilter), nameof(OutstandingOnly),
            nameof(RangeLabel),
        })
        {
            RaisePropertyChanged(name);
        }

        NextPageCommand.RaiseCanExecuteChanged();
        PreviousPageCommand.RaiseCanExecuteChanged();
    }
}
