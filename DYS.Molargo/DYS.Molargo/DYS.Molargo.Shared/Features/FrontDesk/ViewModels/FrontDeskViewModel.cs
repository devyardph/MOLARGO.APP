using DYS.Molargo.Shared.Components;
using DYS.Molargo.Shared.Features.FrontDesk.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.FrontDesk.ViewModels;

/// <summary>
/// The front-desk dashboard, matching the prototype's screen: four figures across the
/// top, the day's arrivals on the left, tasks and the short-notice list on the right.
/// </summary>
public sealed class FrontDeskViewModel : BaseViewModel, IDisposable
{
    private readonly IFrontDeskService _frontDesk;
    private readonly ISessionService _session;
    private readonly IAppNavigator _navigator;
    private readonly IClock _clock;

    private FrontDeskDay? _day;
    private bool _gapOffered;

    public FrontDeskViewModel(
        IFrontDeskService frontDesk,
        ISessionService session,
        IAppNavigator navigator,
        IClock clock)
    {
        _frontDesk = frontDesk;
        _session = session;
        _navigator = navigator;
        _clock = clock;

        // Built once, in the constructor — never rebuilt per render.
        RefreshCommand = new MvxAsyncCommand(LoadAsync);
        AdvanceStatusCommand = new MvxAsyncCommand<Guid>(AdvanceStatusAsync);
        ToggleTaskCommand = new MvxAsyncCommand<Guid>(ToggleTaskAsync);
        OpenPatientCommand = new MvxCommand<Guid>(id => _navigator.ToPatientRecord(id));
        OfferGapCommand = new MvxCommand(OfferGap);

        // Switching site in the app bar has to reload the day. The session raises this
        // rather than the view model polling it, and without the subscription the screen
        // keeps showing the previous location's diary under the new location's name.
        _session.Changed += OnSessionChanged;
    }

    public IMvxAsyncCommand RefreshCommand { get; }

    public IMvxAsyncCommand<Guid> AdvanceStatusCommand { get; }

    public IMvxAsyncCommand<Guid> ToggleTaskCommand { get; }

    public IMvxCommand<Guid> OpenPatientCommand { get; }

    public IMvxCommand OfferGapCommand { get; }

    public override Task Initialize() => LoadAsync();

    public FrontDeskDay? Day => _day;

    public bool HasDay => _day is not null;

    // ---- the four figures ------------------------------------------------

    public int BookedCount => _day?.BookedCount ?? 0;

    /// <summary>"Mon 8 Sep · 3 providers" — the line under the booked figure.</summary>
    public string BookedCaption
    {
        get
        {
            if (_day is null) return string.Empty;

            var date = MolargoFormat.DayLabel(_day.Day);
            var providers = _day.ProviderCount == 1 ? "1 provider" : $"{_day.ProviderCount} providers";
            return $"{date} · {providers}";
        }
    }

    /// <summary>"1h 45m", or "None" where the day is full.</summary>
    public string GapLabel
    {
        get
        {
            var minutes = _day?.GapMinutes ?? 0;
            if (minutes == 0) return "None";

            var hours = minutes / 60;
            var rest = minutes % 60;

            return hours == 0 ? $"{rest}m" : rest == 0 ? $"{hours}h" : $"{hours}h {rest}m";
        }
    }

    public string GapCaption
    {
        get
        {
            var count = _day?.Gaps.Count ?? 0;
            if (count == 0) return "Day is fully booked";

            var gaps = count == 1 ? "1 gap" : $"{count} gaps";
            return $"{gaps} · short-notice list ready";
        }
    }

    /// <summary>True where there is a gap worth an accent-coloured figure.</summary>
    public bool HasGaps => (_day?.Gaps.Count ?? 0) > 0;

    public decimal UnpaidTotal => _day?.UnpaidTotal ?? 0m;

    public string UnpaidCaption
    {
        get
        {
            var count = _day?.UnpaidAccountCount ?? 0;
            return count == 1 ? "1 account" : $"{count} accounts";
        }
    }

    public int FailedToAttendThisWeek => _day?.FailedToAttendThisWeek ?? 0;

    public string FailedToAttendCaption =>
        FailedToAttendThisWeek == 0 ? "None this week" : "Fee rule applied";

    // ---- panels ----------------------------------------------------------

    public IReadOnlyList<FrontDeskArrival> Arrivals => _day?.Arrivals ?? [];

    public IReadOnlyList<FrontDeskTask> Tasks => _day?.Tasks ?? [];

    public IReadOnlyList<FrontDeskWaiting> Waiting => _day?.Waiting ?? [];

    public FrontDeskGap? PrimaryGap => _day?.PrimaryGap;

    /// <summary>
    /// The gap banner's button label. Flips once offered, so the front desk can see the
    /// SMS went out without the row disappearing from under them.
    /// </summary>
    public string OfferGapLabel => _gapOffered
        ? $"SMS sent to {Waiting.Count} on list ✓"
        : "Fill from waitlist";

    public bool IsGapOffered => _gapOffered;

    /// <summary>
    /// The site this screen's figures are for.
    /// </summary>
    /// <remarks>
    /// Shown as the page's heading now rather than as a chip in the app bar. Since a staff
    /// member is scoped to their own site, that chip was the same word on every screen; it
    /// belongs where the day it describes is.
    /// </remarks>
    public string? LocationName => _session.LocationName;

    /// <summary>
    /// "Welcome, Dr Vance".
    /// </summary>
    /// <remarks>
    /// Null when nobody is signed in, so the screen omits the line rather than printing
    /// "Welcome," with nothing after it. The layout's own guard should mean this never
    /// happens, but the greeting is not the place to find out it did.
    /// </remarks>
    public string? Welcome => _session.UserDisplayName is { Length: > 0 } name
        ? $"Welcome, {name}"
        : null;

    private void OnSessionChanged(object? sender, EventArgs e) => _ = LoadAsync();

    private Task LoadAsync() => RunGuardedAsync(async () =>
    {
        _day = await _frontDesk
            .GetDayAsync(_session.LocationId, _clock.Today)
            .ConfigureAwait(false);

        RaiseAllDerived();
    });

    private Task AdvanceStatusAsync(Guid appointmentId) => RunGuardedAsync(async () =>
    {
        await _frontDesk.AdvanceStatusAsync(appointmentId).ConfigureAwait(false);

        // Re-read rather than mutating the loaded aggregate: the status change also moves
        // the check-in timestamps, and the screen should show what was actually written.
        _day = await _frontDesk
            .GetDayAsync(_session.LocationId, _clock.Today)
            .ConfigureAwait(false);

        RaiseAllDerived();
    });

    private Task ToggleTaskAsync(Guid taskId) => RunGuardedAsync(async () =>
    {
        await _frontDesk.ToggleTaskAsync(taskId).ConfigureAwait(false);

        _day = await _frontDesk
            .GetDayAsync(_session.LocationId, _clock.Today)
            .ConfigureAwait(false);

        RaiseAllDerived();
    });

    /// <summary>
    /// Marks the gap as offered. Nothing is actually sent: outbound messaging needs the
    /// comms feature and a gateway, neither of which exists offline. Kept as visible
    /// local state rather than silently doing nothing, so the button is honest about
    /// having been pressed and it is obvious where the real send has to go.
    /// </summary>
    private void OfferGap()
    {
        if (_gapOffered) return;

        _gapOffered = true;
        RaisePropertyChanged(nameof(OfferGapLabel));
        RaisePropertyChanged(nameof(IsGapOffered));
    }

    /// <summary>
    /// Unsubscribes from the session. MvvmComponentBase disposes the view model with the
    /// component, and without this the handler outlives the screen — a scoped session
    /// left holding a reference to a view model whose component is gone.
    /// </summary>
    public void Dispose() => _session.Changed -= OnSessionChanged;

    /// <summary>
    /// Everything on this screen is computed from <see cref="Day"/>, so one load has to
    /// announce all of it. Listed explicitly rather than raising a blanket change, which
    /// would re-render every binding on the page.
    /// </summary>
    private void RaiseAllDerived()
    {
        foreach (var name in new[]
        {
            nameof(Day), nameof(HasDay), nameof(BookedCount), nameof(BookedCaption),
            nameof(GapLabel), nameof(GapCaption), nameof(HasGaps), nameof(UnpaidTotal),
            nameof(UnpaidCaption), nameof(FailedToAttendThisWeek), nameof(FailedToAttendCaption),
            nameof(Arrivals), nameof(Tasks), nameof(Waiting), nameof(PrimaryGap),
            nameof(OfferGapLabel), nameof(LocationName),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
