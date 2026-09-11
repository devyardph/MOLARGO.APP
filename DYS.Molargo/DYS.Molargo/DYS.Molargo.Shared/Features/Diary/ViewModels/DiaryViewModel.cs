using DYS.Molargo.Domain;
using DYS.Molargo.Shared.Components;
using DYS.Molargo.Shared.Features.Diary.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Diary.ViewModels;

/// <summary>Which of the diary's worklists is showing.</summary>
/// <remarks>Explicit values: the tab is part of the screen's state, not its layout.</remarks>
public enum DiaryTab
{
    Diary = 0,
    Recalls = 1,
    Waitlist = 2,
    Roster = 3,
    Reminders = 4,
    FailedToAttend = 5,
}

/// <summary>How much of the calendar the diary tab is showing.</summary>
public enum DiaryScale
{
    Day = 0,
    Week = 1,
    Month = 2,
}

/// <summary>
/// The appointments diary: the day grid with its chairs, the week and month overviews, and
/// the recall and short-notice worklists.
/// </summary>
public sealed class DiaryViewModel : BaseViewModel, IDisposable
{
    private readonly IDiaryService _diary;
    private readonly ISessionService _session;
    private readonly IAppNavigator _navigator;
    private readonly IClock _clock;

    private DiaryTab _tab = DiaryTab.Diary;
    private DiaryScale _scale = DiaryScale.Day;
    private DateOnly _date;

    private DiaryDay? _day;
    private IReadOnlyList<DiaryWeekDay> _week = [];
    private IReadOnlyList<DiaryMonthCell> _month = [];
    private IReadOnlyList<DiaryRecallRow> _recalls = [];
    private IReadOnlyList<DiaryWaitlistRow> _waitlist = [];

    private Guid? _selectedId;
    private bool _isMoving;
    private string? _moveRefusal;

    public DiaryViewModel(
        IDiaryService diary,
        ISessionService session,
        IAppNavigator navigator,
        IClock clock)
    {
        _diary = diary;
        _session = session;
        _navigator = navigator;
        _clock = clock;

        _date = clock.Today;

        // Built once, in the constructor — never rebuilt per render.
        SelectTabCommand = new MvxAsyncCommand<DiaryTab>(SelectTabAsync);
        SelectScaleCommand = new MvxAsyncCommand<DiaryScale>(SelectScaleAsync);
        StepCommand = new MvxAsyncCommand<int>(StepAsync);
        TodayCommand = new MvxAsyncCommand(() => GoToAsync(_clock.Today));
        OpenDayCommand = new MvxAsyncCommand<DateOnly>(OpenDayAsync);
        SelectBlockCommand = new MvxCommand<Guid>(SelectBlock);
        ClearSelectionCommand = new MvxCommand(ClearSelection);
        StartMoveCommand = new MvxCommand(StartMove);
        MoveHereCommand = new MvxAsyncCommand<DiarySlot>(slot => MoveHereAsync(slot!));
        MarkArrivedCommand = new MvxAsyncCommand(MarkArrivedAsync);
        OpenPatientCommand = new MvxCommand<Guid>(id => _navigator.ToPatientRecord(id));
        OpenSelectedPatientCommand = new MvxCommand(OpenSelectedPatient);
        LogRecallContactCommand = new MvxAsyncCommand<Guid>(LogRecallContactAsync);
        LogWaitlistContactCommand = new MvxAsyncCommand<Guid>(LogWaitlistContactAsync);
        NewAppointmentCommand = new MvxCommand(() => _navigator.ToNewAppointment());
        BookSlotCommand = new MvxCommand<DiarySlot>(slot => BookSlot(slot!));
        EditSelectedCommand = new MvxCommand(EditSelected);

        // Switching site in the app bar has to reload. Without this the grid keeps showing
        // the previous location's chairs under the new location's name.
        _session.Changed += OnSessionChanged;
    }

    public IMvxAsyncCommand<DiaryTab> SelectTabCommand { get; }

    public IMvxAsyncCommand<DiaryScale> SelectScaleCommand { get; }

    /// <summary>Steps the date by the current scale — a day, a week or a month.</summary>
    public IMvxAsyncCommand<int> StepCommand { get; }

    public IMvxAsyncCommand TodayCommand { get; }

    public IMvxAsyncCommand<DateOnly> OpenDayCommand { get; }

    public IMvxCommand<Guid> SelectBlockCommand { get; }

    public IMvxCommand ClearSelectionCommand { get; }

    public IMvxCommand StartMoveCommand { get; }

    public IMvxAsyncCommand<DiarySlot> MoveHereCommand { get; }

    public IMvxAsyncCommand MarkArrivedCommand { get; }

    public IMvxCommand<Guid> OpenPatientCommand { get; }

    public IMvxCommand OpenSelectedPatientCommand { get; }

    public IMvxAsyncCommand<Guid> LogRecallContactCommand { get; }

    public IMvxAsyncCommand<Guid> LogWaitlistContactCommand { get; }

    /// <summary>Book with nothing pre-filled — the header's button.</summary>
    public IMvxCommand NewAppointmentCommand { get; }

    /// <summary>Book into a specific empty slot — a click on bare chair time.</summary>
    public IMvxCommand<DiarySlot> BookSlotCommand { get; }

    public IMvxCommand EditSelectedCommand { get; }

    public override Task Initialize() => LoadAsync();

    public DiaryTab Tab => _tab;

    public DiaryScale Scale => _scale;

    public DateOnly Date => _date;

    // ---- the day grid ----------------------------------------------------

    public DiaryDay? Day => _day;

    public IReadOnlyList<DiaryColumn> Columns => _day?.Columns ?? [];

    public IReadOnlyList<DiaryBlock> Blocks => _day?.Blocks ?? [];

    public IReadOnlyList<DiaryBlock> Unplaced => _day?.Unplaced ?? [];

    public bool IsClosedDay => _day is { IsOpen: false };

    /// <summary>
    /// Vertical scale: pixels per minute. A 30-minute booking is 33px, close to the
    /// design's 30px row, and every other duration lands proportionally — which a
    /// fixed row height cannot do for a 45- or 20-minute appointment.
    /// </summary>
    public const decimal PixelsPerMinute = 1.1m;

    public int GridHeight => (int)(PracticeHours.WorkingMinutes * PixelsPerMinute);

    /// <summary>The half-hour labels down the time gutter.</summary>
    public IReadOnlyList<DiaryGutterMark> GutterMarks { get; } = BuildGutter();

    /// <summary>
    /// Every slot a selected appointment could be moved to, as drop targets.
    /// </summary>
    /// <remarks>
    /// Built only while a move is in progress. Rendering four hundred tappable cells
    /// behind the blocks at all times made the grid impossible to click through — every
    /// click on empty chair time was a move.
    /// </remarks>
    public IReadOnlyList<DiarySlot> MoveTargets { get; private set; } = [];

    public string DateLabel => MolargoFormat.DayLabel(_date);

    /// <summary>"Mon 8 Sep · Sydney CBD · 14 booked" — the line under the view switcher.</summary>
    public string DiaryCaption
    {
        get
        {
            var parts = new List<string> { DateLabel };

            if (_session.LocationName is { Length: > 0 } location) parts.Add(location);

            if (_day is { } day)
            {
                parts.Add(day.BookedCount == 1 ? "1 booked" : $"{day.BookedCount} booked");

                if (day.LostCount > 0)
                {
                    parts.Add(day.LostCount == 1
                        ? "1 cancelled or missed"
                        : $"{day.LostCount} cancelled or missed");
                }
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>The heading for the week and month views, which are not one date.</summary>
    public string ScaleLabel => _scale switch
    {
        DiaryScale.Week => _week.Count > 0
            ? $"Week of {MolargoFormat.DayLabel(_week[0].Date)}"
            : "Week",
        DiaryScale.Month => _date.ToString("MMMM yyyy",
            System.Globalization.CultureInfo.InvariantCulture),
        _ => DateLabel,
    };

    public IReadOnlyList<DiaryWeekDay> Week => _week;

    public IReadOnlyList<DiaryMonthCell> Month => _month;

    // ---- selection -------------------------------------------------------

    public DiaryBlock? Selected =>
        _selectedId is { } id
            ? Blocks.FirstOrDefault(block => block.AppointmentId == id)
                ?? Unplaced.FirstOrDefault(block => block.AppointmentId == id)
            : null;

    public bool HasSelection => Selected is not null;

    public bool IsSelected(Guid appointmentId) => _selectedId == appointmentId;

    public bool IsMoving => _isMoving;

    /// <summary>Why the last attempted move was refused, or null.</summary>
    public string? MoveRefusal => _moveRefusal;

    /// <summary>"Margaret Yuen · 08:45–09:45" — the detail bar's heading.</summary>
    public string SelectedTitle => Selected is { } block
        ? $"{block.PatientName} · {block.StartLocal:HH\\:mm}–{block.EndLocal:HH\\:mm}"
        : string.Empty;

    /// <summary>"Crown fit · Chair 1 · Booked" — the detail bar's second line.</summary>
    public string SelectedSubtitle
    {
        get
        {
            if (Selected is not { } block) return string.Empty;

            var chair = Columns.FirstOrDefault(column => column.OperatoryId == block.OperatoryId);

            var parts = new List<string> { block.Reason };

            if (chair is not null) parts.Add(chair.OperatoryName);
            if (chair?.ProviderName is { Length: > 0 } provider) parts.Add(provider);

            parts.Add(AppointmentCss.StatusLabel(block.Status));
            parts.Add($"{block.Minutes} min");

            return string.Join(" · ", parts);
        }
    }

    public bool CanMarkArrived =>
        Selected is { } block && AppointmentProgress.IsAwaitingArrival(block.Status);

    // ---- worklists -------------------------------------------------------

    public IReadOnlyList<DiaryRecallRow> Recalls => _recalls;

    public IReadOnlyList<DiaryWaitlistRow> Waitlist => _waitlist;

    /// <summary>Recalls already past their due date, for the tab's caption.</summary>
    public int OverdueRecallCount => _recalls.Count(row => row.DueOn < _clock.Today);

    public DateOnly Today => _clock.Today;

    // ---- loading ---------------------------------------------------------

    private Task LoadAsync() => RunGuardedAsync(async () =>
    {
        switch (_tab)
        {
            case DiaryTab.Recalls:
                _recalls = await _diary
                    .GetRecallsAsync(_session.LocationId)
                    .ConfigureAwait(false);
                break;

            case DiaryTab.Waitlist:
                _waitlist = await _diary
                    .GetWaitlistAsync(_session.LocationId)
                    .ConfigureAwait(false);
                break;

            case DiaryTab.Diary:
                await LoadCalendarAsync().ConfigureAwait(false);
                break;

            // Roster, reminders and FTA are placeholders and load nothing. Falling through
            // to a calendar load would spend the query on a screen that cannot show it.
        }

        RaiseAll();
    });

    private async Task LoadCalendarAsync()
    {
        switch (_scale)
        {
            case DiaryScale.Day:
                _day = await _diary.GetDayAsync(_session.LocationId, _date).ConfigureAwait(false);

                // A selection that is no longer on the day has to go, or the detail bar
                // keeps offering actions on an appointment the grid stopped showing.
                if (_selectedId is not null && Selected is null) ClearSelection();

                break;

            case DiaryScale.Week:
                _week = await _diary.GetWeekAsync(_session.LocationId, _date).ConfigureAwait(false);
                break;

            case DiaryScale.Month:
                _month = await _diary.GetMonthAsync(_session.LocationId, _date).ConfigureAwait(false);
                break;
        }
    }

    private async Task SelectTabAsync(DiaryTab tab)
    {
        _tab = tab;
        ClearSelection();

        await RaisePropertyChanged(nameof(Tab)).ConfigureAwait(false);
        await LoadAsync().ConfigureAwait(false);
    }

    private async Task SelectScaleAsync(DiaryScale scale)
    {
        _scale = scale;
        ClearSelection();

        await RaisePropertyChanged(nameof(Scale)).ConfigureAwait(false);
        await LoadAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Steps by whatever the current view shows, so the same two arrows serve all three
    /// scales — a month view whose arrows moved by one day would take thirty taps to
    /// reach the next month.
    /// </summary>
    private Task StepAsync(int direction) => GoToAsync(_scale switch
    {
        DiaryScale.Week => _date.AddDays(7 * direction),
        DiaryScale.Month => _date.AddMonths(direction),
        _ => _date.AddDays(direction),
    });

    private async Task GoToAsync(DateOnly date)
    {
        _date = date;
        ClearSelection();

        await RaisePropertyChanged(nameof(Date)).ConfigureAwait(false);
        await LoadAsync().ConfigureAwait(false);
    }

    /// <summary>Opening a day from the week or month view drops back to the day grid.</summary>
    private async Task OpenDayAsync(DateOnly date)
    {
        _scale = DiaryScale.Day;
        await RaisePropertyChanged(nameof(Scale)).ConfigureAwait(false);
        await GoToAsync(date).ConfigureAwait(false);
    }

    // ---- selection and moving --------------------------------------------

    private void SelectBlock(Guid appointmentId)
    {
        // Tapping the selected block again clears it, which is how a stray tap is undone
        // without hunting for a close button.
        _selectedId = _selectedId == appointmentId ? null : appointmentId;

        _isMoving = false;
        _moveRefusal = null;

        RaiseSelection();
    }

    private void ClearSelection()
    {
        _selectedId = null;
        _isMoving = false;
        _moveRefusal = null;
        MoveTargets = [];

        RaiseSelection();
    }

    private void StartMove()
    {
        if (Selected is null) return;

        _isMoving = true;
        _moveRefusal = null;
        MoveTargets = BuildMoveTargets();

        RaiseSelection();
    }

    private Task MoveHereAsync(DiarySlot slot) => RunGuardedAsync(async () =>
    {
        if (Selected is not { } block) return;

        var startLocal = _date.ToDateTime(
            new TimeOnly(0, 0).AddMinutes(PracticeHours.OpenMinutes + slot.OffsetMinutes),
            DateTimeKind.Local);

        _moveRefusal = await _diary
            .MoveAsync(block.AppointmentId, slot.OperatoryId, startLocal)
            .ConfigureAwait(false);

        // The move stays armed on refusal, so the next tap is another attempt rather than
        // a fresh selection — the usual response to "that runs past closing" is to try a
        // slightly earlier slot.
        if (_moveRefusal is null)
        {
            _isMoving = false;
            MoveTargets = [];
        }

        await LoadCalendarAsync().ConfigureAwait(false);
        RaiseAll();
    });

    private Task MarkArrivedAsync() => RunGuardedAsync(async () =>
    {
        if (Selected is not { } block) return;

        await _diary.MarkArrivedAsync(block.AppointmentId).ConfigureAwait(false);

        await LoadCalendarAsync().ConfigureAwait(false);
        RaiseAll();
    });

    private void OpenSelectedPatient()
    {
        if (Selected is { } block) _navigator.ToPatientRecord(block.PatientId);
    }

    private void EditSelected()
    {
        if (Selected is { } block) _navigator.ToAppointment(block.AppointmentId);
    }

    private void BookSlot(DiarySlot slot)
    {
        var time = new TimeOnly(0, 0).AddMinutes(PracticeHours.OpenMinutes + slot.OffsetMinutes);

        _navigator.ToNewAppointment(_date, time, slot.OperatoryId);
    }

    /// <summary>
    /// Every slot in the day, as somewhere a new booking can start.
    /// </summary>
    /// <remarks>
    /// Rendered behind the appointment blocks and always present, unlike
    /// <see cref="MoveTargets"/>: clicking bare chair time to book into it is the
    /// obvious default, where clicking it to complete a move only makes sense while a
    /// move is actually armed.
    /// </remarks>
    public IReadOnlyList<DiarySlot> BookableSlots
    {
        get
        {
            if (_day is null || _isMoving) return [];

            var slots = new List<DiarySlot>();

            foreach (var column in Columns)
            {
                for (var offset = 0;
                     offset < PracticeHours.WorkingMinutes;
                     offset += PracticeHours.SlotMinutes)
                {
                    slots.Add(new DiarySlot(column.OperatoryId, offset));
                }
            }

            return slots;
        }
    }

    private Task LogRecallContactAsync(Guid recallId) => RunGuardedAsync(async () =>
    {
        await _diary.LogRecallContactAsync(recallId).ConfigureAwait(false);

        _recalls = await _diary.GetRecallsAsync(_session.LocationId).ConfigureAwait(false);
        await RaisePropertyChanged(nameof(Recalls)).ConfigureAwait(false);
        await RaisePropertyChanged(nameof(OverdueRecallCount)).ConfigureAwait(false);
    });

    private Task LogWaitlistContactAsync(Guid entryId) => RunGuardedAsync(async () =>
    {
        await _diary.LogWaitlistContactAsync(entryId).ConfigureAwait(false);

        _waitlist = await _diary.GetWaitlistAsync(_session.LocationId).ConfigureAwait(false);
        await RaisePropertyChanged(nameof(Waitlist)).ConfigureAwait(false);
    });

    private void OnSessionChanged(object? sender, EventArgs e) => _ = LoadAsync();

    public void Dispose() => _session.Changed -= OnSessionChanged;

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// One target per chair per slot, minus the slots the appointment already occupies.
    /// </summary>
    /// <remarks>
    /// Its own start is excluded so "move" cannot be completed by putting the appointment
    /// back where it was — a no-op that looks like a successful reschedule and leaves the
    /// front desk believing they changed something.
    /// </remarks>
    private IReadOnlyList<DiarySlot> BuildMoveTargets()
    {
        if (Selected is not { } block) return [];

        var slots = new List<DiarySlot>();

        var last = PracticeHours.WorkingMinutes - block.Minutes;

        foreach (var column in Columns)
        {
            for (var offset = 0; offset <= last; offset += PracticeHours.SlotMinutes)
            {
                var isWhereItIs = column.OperatoryId == block.OperatoryId
                    && offset == block.OffsetMinutes;

                if (isWhereItIs) continue;

                slots.Add(new DiarySlot(column.OperatoryId, offset));
            }
        }

        return slots;
    }

    private static IReadOnlyList<DiaryGutterMark> BuildGutter()
    {
        var marks = new List<DiaryGutterMark>();

        // Every half hour. Every fifteen would match the slot granularity but crowds the
        // gutter to the point of being unreadable at this row height.
        for (var minutes = 0; minutes <= PracticeHours.WorkingMinutes; minutes += 30)
        {
            var time = new TimeOnly(0, 0).AddMinutes(PracticeHours.OpenMinutes + minutes);

            marks.Add(new DiaryGutterMark(minutes, time.ToString("HH\\:mm")));
        }

        return marks;
    }

    private void RaiseSelection()
    {
        foreach (var name in new[]
        {
            nameof(Selected), nameof(HasSelection), nameof(SelectedTitle),
            nameof(SelectedSubtitle), nameof(CanMarkArrived), nameof(IsMoving),
            nameof(MoveTargets), nameof(MoveRefusal), nameof(BookableSlots),
        })
        {
            RaisePropertyChanged(name);
        }
    }

    /// <summary>
    /// Everything the screen reads. Listed explicitly rather than raising a blanket
    /// change, which re-renders inputs the user may be typing in.
    /// </summary>
    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(Tab), nameof(Scale), nameof(Date), nameof(DateLabel), nameof(ScaleLabel),
            nameof(Day), nameof(Columns), nameof(Blocks), nameof(Unplaced),
            nameof(IsClosedDay), nameof(DiaryCaption), nameof(Week), nameof(Month),
            nameof(Recalls), nameof(Waitlist), nameof(OverdueRecallCount),
            nameof(BookableSlots),
        })
        {
            RaisePropertyChanged(name);
        }

        RaiseSelection();
    }
}

/// <summary>A slot an appointment can be moved to: a chair, and an offset from opening.</summary>
public sealed record DiarySlot(Guid OperatoryId, int OffsetMinutes);

/// <summary>One label down the time gutter.</summary>
public sealed record DiaryGutterMark(int OffsetMinutes, string Label);
