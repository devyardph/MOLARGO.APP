using DYS.Molargo.Domain.Dtos;
using DYS.Molargo.Shared.Features.Comms.Services;
using DYS.Molargo.Domain.Enums;
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
    Reminders = 4,
}

// Roster (3) and FailedToAttend (5) were here and are gone. The numbers are left as holes
// rather than closed up: the tab is part of the screen's state, and renumbering would make
// any stored or linked value point at a different tab than it did.
//
// Roster went because it duplicated Admin → Rosters & pay, and two places to see who is
// working is two places to disagree. The genuinely diary-shaped part of rostering is
// closures and leave blocking the grid so nobody books into a day the practice is shut —
// a rule the day and month views enforce, not a tab.
//
// FTA went because it is a report rather than a worklist. The no-shows are visible in the
// appointment list view, which is searchable by status.

/// <summary>How much of the calendar the diary tab is showing.</summary>
public enum DiaryScale
{
    Day = 0,
    Week = 1,
    Month = 2,

    /// <summary>
    /// Every appointment at the site, searchable, with no date window.
    /// </summary>
    /// <remarks>
    /// Sits with the other three because it is the same control on screen, but it is not a
    /// scale of the calendar: the date stepper means nothing here, and the screen hides it
    /// rather than leaving two arrows that move a date nothing is drawn against.
    /// </remarks>
    List = 3,
}

/// <summary>
/// The appointments diary: the day grid with its chairs, the week and month overviews, and
/// the recall and short-notice worklists.
/// </summary>
public sealed class DiaryViewModel : BaseViewModel, IDisposable
{
    private readonly IDiaryService _diary;

    /// <summary>
    /// Only for the patient search behind "add to the short-notice list".
    /// </summary>
    /// <remarks>
    /// Reused rather than a second search written here. The appointment form already picks
    /// a patient this way, and two searches over one table are two sets of results that can
    /// disagree about who exists.
    /// </remarks>
    private readonly IAppointmentService _appointments;

    /// <summary>The reminders tab: the cadence, what is due, and what came of it.</summary>
    private readonly IReminderRunner _reminders;
    private readonly ISessionService _session;
    private readonly IAppNavigator _navigator;
    private readonly IClock _clock;

    private DiaryTab _tab = DiaryTab.Diary;
    private DiaryScale _scale = DiaryScale.Day;
    private DateOnly _date;

    private DiaryDay? _day;
    private IReadOnlyList<DiaryWeekDay> _week = [];
    private IReadOnlyList<DiaryMonthCell> _month = [];
    private DiaryList _list = new([], 0, 0, ListPageSize);

    private IReadOnlyList<ReminderDue> _remindersDue = [];
    private IReadOnlyList<ReminderSent> _remindersRecent = [];
    private string? _reminderCadence;
    private bool _remindersEnabled;
    private string? _reminderBlocked;
    private string? _reminderNotice;

    private bool _addingToWaitlist;
    private string? _waitlistSearch;
    private IReadOnlyList<PatientListItemDto> _waitlistMatches = [];
    private Guid _waitlistPatientId;
    private string? _waitlistPatientName;
    private string? _waitlistWants;
    private string? _waitlistWhen;
    private WaitlistPriority _waitlistPriority = WaitlistPriority.Routine;
    private string? _listSearch;
    private IReadOnlyList<DiaryRecallRow> _recalls = [];
    private IReadOnlyList<DiaryWaitlistRow> _waitlist = [];

    private Guid? _selectedId;
    private bool _isMoving;
    private string? _moveRefusal;

    public DiaryViewModel(
        IDiaryService diary,
        IAppointmentService appointments,
        IReminderRunner reminders,
        ISessionService session,
        IAppNavigator navigator,
        IClock clock)
    {
        _diary = diary;
        _appointments = appointments;
        _reminders = reminders;
        _session = session;
        _navigator = navigator;
        _clock = clock;

        _date = clock.Today;

        // Built once, in the constructor — never rebuilt per render.
        SelectTabCommand = new MvxAsyncCommand<DiaryTab>(SelectTabAsync);
        SelectScaleCommand = new MvxAsyncCommand<DiaryScale>(SelectScaleAsync);
        PreviousListPageCommand = new MvxAsyncCommand(() => ReloadListAsync(ListPage - 1));
        NextListPageCommand = new MvxAsyncCommand(() => ReloadListAsync(ListPage + 1));
        GoToListPageCommand = new MvxAsyncCommand<int>(ReloadListAsync);
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
        RunRemindersCommand = new MvxAsyncCommand(RunRemindersAsync);
        SaveCadenceCommand = new MvxAsyncCommand(SaveCadenceAsync);
        ToggleRemindersCommand = new MvxAsyncCommand(ToggleRemindersAsync);
        StartWaitlistAddCommand = new MvxCommand(StartWaitlistAdd);
        CancelWaitlistAddCommand = new MvxCommand(CancelWaitlistAdd);
        ChooseWaitlistPatientCommand = new MvxCommand<PatientListItemDto>(
            patient => ChooseWaitlistPatient(patient!));
        SetWaitlistPriorityCommand = new MvxCommand<WaitlistPriority>(
            priority => WaitlistPriority = priority);
        AddToWaitlistCommand = new MvxAsyncCommand(AddToWaitlistAsync);
        RemoveFromWaitlistCommand = new MvxAsyncCommand<Guid>(id => RemoveFromWaitlistAsync(id, false));
        WaitlistBookedCommand = new MvxAsyncCommand<Guid>(id => RemoveFromWaitlistAsync(id, true));
        NewAppointmentCommand = new MvxCommand(() => _navigator.ToNewAppointment());
        BookSlotCommand = new MvxCommand<DiarySlot>(slot => BookSlot(slot!));
        EditSelectedCommand = new MvxCommand(EditSelected);

        // Switching site in the app bar has to reload. Without this the grid keeps showing
        // the previous location's chairs under the new location's name.
        _session.Changed += OnSessionChanged;
    }

    /// <summary>The site's trading pattern — the day the grid and gutter are scaled to.</summary>
    public PracticeHours Hours => _session.Hours;

    public IMvxAsyncCommand<DiaryTab> SelectTabCommand { get; }

    public IMvxAsyncCommand<DiaryScale> SelectScaleCommand { get; }

    public IMvxAsyncCommand PreviousListPageCommand { get; }

    public IMvxAsyncCommand NextListPageCommand { get; }

    public IMvxAsyncCommand<int> GoToListPageCommand { get; }

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

    public IMvxAsyncCommand RunRemindersCommand { get; }

    public IMvxAsyncCommand SaveCadenceCommand { get; }

    public IMvxAsyncCommand ToggleRemindersCommand { get; }

    public IMvxCommand StartWaitlistAddCommand { get; }

    public IMvxCommand CancelWaitlistAddCommand { get; }

    public IMvxCommand<PatientListItemDto> ChooseWaitlistPatientCommand { get; }

    public IMvxCommand<WaitlistPriority> SetWaitlistPriorityCommand { get; }

    public IMvxAsyncCommand AddToWaitlistCommand { get; }

    /// <summary>Off the list because they no longer want a slot.</summary>
    public IMvxAsyncCommand<Guid> RemoveFromWaitlistCommand { get; }

    /// <summary>Off the list because one was found for them.</summary>
    public IMvxAsyncCommand<Guid> WaitlistBookedCommand { get; }

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

    public int GridHeight => (int)(_session.Hours.WorkingMinutes * PixelsPerMinute);

    /// <summary>The half-hour labels down the time gutter.</summary>
    /// <remarks>
    /// A property, not a field initialiser: the marks are spaced across this site's own
    /// trading day, which the session only knows once it has loaded.
    /// </remarks>
    public IReadOnlyList<DiaryGutterMark> GutterMarks => BuildGutter();

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

    // ---- putting somebody on the short-notice list -----------------------

    // ---- reminders -------------------------------------------------------

    public IReadOnlyList<ReminderDue> RemindersDue => _remindersDue;

    public IReadOnlyList<ReminderSent> RemindersRecent => _remindersRecent;

    /// <summary>The cadence as typed — "7, 1".</summary>
    public string? ReminderCadence
    {
        get => _reminderCadence;
        set => SetProperty(ref _reminderCadence, value);
    }

    public bool RemindersEnabled => _remindersEnabled;

    /// <summary>Why nothing would send, or null where it would.</summary>
    public string? ReminderBlockedReason => _reminderBlocked;

    /// <summary>What the last run did.</summary>
    public string? ReminderNotice => _reminderNotice;

    public bool CanRunReminders => !IsBusy && _reminderBlocked is null;

    public bool IsAddingToWaitlist => _addingToWaitlist;

    /// <summary>Who to add, searched by name or patient number.</summary>
    public string? WaitlistSearch
    {
        get => _waitlistSearch;
        set
        {
            if (!SetProperty(ref _waitlistSearch, value)) return;

            // Fire and forget: the setter is called from a bound input and cannot await.
            // The same shape the patients list uses.
            _ = SearchForWaitlistAsync(value);
        }
    }

    public IReadOnlyList<PatientListItemDto> WaitlistMatches => _waitlistMatches;

    public Guid WaitlistPatientId => _waitlistPatientId;

    public string? WaitlistPatientName => _waitlistPatientName;

    public bool WaitlistHasPatient => _waitlistPatientId != Guid.Empty;

    /// <summary>What they are waiting for — "any exam slot", "toothache".</summary>
    public string? WaitlistWants
    {
        get => _waitlistWants;
        set => SetProperty(ref _waitlistWants, value);
    }

    /// <summary>When they could come, in their own words.</summary>
    public string? WaitlistWhen
    {
        get => _waitlistWhen;
        set => SetProperty(ref _waitlistWhen, value);
    }

    public WaitlistPriority WaitlistPriority
    {
        get => _waitlistPriority;
        set => SetProperty(ref _waitlistPriority, value);
    }

    public static readonly WaitlistPriority[] WaitlistPriorities =
        Enum.GetValues<WaitlistPriority>();

    public bool CanAddToWaitlist => !IsBusy && WaitlistHasPatient;

    /// <summary>
    /// Rows a page, matching every other list in the app.
    /// </summary>
    /// <remarks>
    /// Seventeen, the same as Patients, the audit log and the stock list. One number across
    /// the app means a pager that looks and behaves the same everywhere; a diary that
    /// showed twenty would make the control read as a different control.
    /// </remarks>
    public const int ListPageSize = 17;

    public IReadOnlyList<DiaryListRow> List => _list.Rows;

    public int ListPage => _list.Page;

    public int ListPageCount =>
        Math.Max(1, (int)Math.Ceiling(_list.Total / (double)ListPageSize));

    public int ListTotal => _list.Total;

    public bool ListHasRows => _list.Rows.Count > 0;

    /// <summary>What is being searched for, applied as it is typed.</summary>
    public string? ListSearch
    {
        get => _listSearch;
        set
        {
            if (!SetProperty(ref _listSearch, value)) return;

            // Back to the first page. A search run while somebody is on page nine would
            // otherwise land them past the end of a shorter result, and the clamp in the
            // service would silently move them somewhere they did not ask to be.
            _ = ReloadListAsync(0);
        }
    }

    public bool ListIsFiltered => !string.IsNullOrWhiteSpace(_listSearch);

    /// <summary>"Nothing matched" reads differently from "nothing booked".</summary>
    public string ListEmptyMessage => ListIsFiltered
        ? $"Nothing matches \"{_listSearch?.Trim()}\"."
        : "No appointments at this site yet.";

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

            case DiaryTab.Reminders:
                await ReloadRemindersAsync().ConfigureAwait(false);
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

            case DiaryScale.List:
                _list = await _diary
                    .GetListAsync(_session.LocationId, _listSearch, _list.Page, ListPageSize)
                    .ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// Re-reads one page of the list.
    /// </summary>
    /// <remarks>
    /// Not routed through the screen's guarded loader, which does not nest — typing in the
    /// search box while a page load is in flight would otherwise drop the keystroke and
    /// leave the box showing a term the list was never filtered by.
    /// </remarks>
    private async Task ReloadListAsync(int page)
    {
        _list = await _diary
            .GetListAsync(_session.LocationId, _listSearch, page, ListPageSize)
            .ConfigureAwait(false);

        foreach (var name in new[]
        {
            nameof(List), nameof(ListPage), nameof(ListPageCount), nameof(ListTotal),
            nameof(ListHasRows), nameof(ListIsFiltered), nameof(ListEmptyMessage),
        })
        {
            await RaisePropertyChanged(name).ConfigureAwait(false);
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
        // The list has no date window to step through. Left as a no-op rather than
        // disabled, because the screen hides the arrows in this mode and a shortcut key
        // could still reach this.
        DiaryScale.List => _date,
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
            new TimeOnly(0, 0).AddMinutes(_session.Hours.OpenMinutes + slot.OffsetMinutes),
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
        var time = new TimeOnly(0, 0).AddMinutes(_session.Hours.OpenMinutes + slot.OffsetMinutes);

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
                     offset < _session.Hours.WorkingMinutes;
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

    // ---- reminders -------------------------------------------------------

    /// <remarks>
    /// Not routed through the guarded loader, which does not nest — every command below is
    /// already inside one, and calling the guarded version would skip the reload and leave
    /// the screen showing what it held before the run.
    /// </remarks>
    private async Task ReloadRemindersAsync()
    {
        var settings = await _reminders.GetCadenceAsync().ConfigureAwait(false);

        _reminderCadence = settings.Count > 0 ? string.Join(", ", settings) : null;
        _reminderBlocked = await _reminders.GetBlockedReasonAsync().ConfigureAwait(false);
        _remindersEnabled = await _diary.AreRemindersOnAsync().ConfigureAwait(false);
        _remindersDue = await _reminders.GetDueAsync().ConfigureAwait(false);
        _remindersRecent = await _reminders.GetRecentAsync().ConfigureAwait(false);

        RaiseReminders();
    }

    private Task RunRemindersAsync() => RunGuardedAsync(async () =>
    {
        var run = await _reminders.RunAsync().ConfigureAwait(false);

        if (run.Refusal is { Length: > 0 } refusal)
        {
            _reminderNotice = refusal;
        }
        else
        {
            _reminderNotice = run.Sent + run.Failed + run.Skipped == 0
                ? "Nothing was due."
                : $"{run.Sent} sent"
                    + (run.Failed > 0 ? $", {run.Failed} failed" : string.Empty)
                    + (run.Skipped > 0 ? $", {run.Skipped} skipped" : string.Empty)
                    + ".";
        }

        await ReloadRemindersAsync().ConfigureAwait(false);
    });

    private Task SaveCadenceAsync() => RunGuardedAsync(async () =>
    {
        var refusal = await _diary
            .SaveReminderCadenceAsync(_reminderCadence, _remindersEnabled)
            .ConfigureAwait(false);

        _reminderNotice = refusal ?? "Cadence saved.";

        await ReloadRemindersAsync().ConfigureAwait(false);
    });

    private Task ToggleRemindersAsync() => RunGuardedAsync(async () =>
    {
        var refusal = await _diary
            .SaveReminderCadenceAsync(_reminderCadence, !_remindersEnabled)
            .ConfigureAwait(false);

        _reminderNotice = refusal;

        await ReloadRemindersAsync().ConfigureAwait(false);
    });

    private void RaiseReminders()
    {
        foreach (var name in new[]
        {
            nameof(RemindersDue), nameof(RemindersRecent), nameof(ReminderCadence),
            nameof(RemindersEnabled), nameof(ReminderBlockedReason), nameof(ReminderNotice),
            nameof(CanRunReminders),
        })
        {
            _ = RaisePropertyChanged(name);
        }
    }

    // ---- putting somebody on the short-notice list -----------------------

    private void StartWaitlistAdd()
    {
        _addingToWaitlist = true;
        ClearWaitlistForm();

        ErrorMessage = null;
        RaiseWaitlist();
    }

    private void CancelWaitlistAdd()
    {
        _addingToWaitlist = false;
        ClearWaitlistForm();

        ErrorMessage = null;
        RaiseWaitlist();
    }

    private void ChooseWaitlistPatient(PatientListItemDto patient)
    {
        _waitlistPatientId = patient.Id;
        _waitlistPatientName = patient.FullName;

        // The matches go once one is chosen. Leaving them under the chosen name invites a
        // second click that silently replaces the first.
        _waitlistMatches = [];
        _waitlistSearch = null;

        RaiseWaitlist();
    }

    private async Task SearchForWaitlistAsync(string? term)
    {
        _waitlistMatches = string.IsNullOrWhiteSpace(term)
            ? []
            : await _appointments.SearchPatientsAsync(term).ConfigureAwait(false);

        await RaisePropertyChanged(nameof(WaitlistMatches)).ConfigureAwait(false);
    }

    private Task AddToWaitlistAsync() => RunGuardedAsync(async () =>
    {
        var refusal = await _diary
            .AddToWaitlistAsync(
                _waitlistPatientId, _waitlistWants, _waitlistWhen, _waitlistPriority)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _addingToWaitlist = false;
        ClearWaitlistForm();

        _waitlist = await _diary.GetWaitlistAsync(_session.LocationId).ConfigureAwait(false);
        RaiseWaitlist();
    });

    private Task RemoveFromWaitlistAsync(Guid entryId, bool booked) => RunGuardedAsync(async () =>
    {
        await _diary.RemoveFromWaitlistAsync(entryId, booked).ConfigureAwait(false);

        _waitlist = await _diary.GetWaitlistAsync(_session.LocationId).ConfigureAwait(false);
        RaiseWaitlist();
    });

    private void ClearWaitlistForm()
    {
        _waitlistSearch = null;
        _waitlistMatches = [];
        _waitlistPatientId = Guid.Empty;
        _waitlistPatientName = null;
        _waitlistWants = null;
        _waitlistWhen = null;
        _waitlistPriority = WaitlistPriority.Routine;
    }

    private void RaiseWaitlist()
    {
        foreach (var name in new[]
        {
            nameof(Waitlist), nameof(IsAddingToWaitlist), nameof(WaitlistSearch),
            nameof(WaitlistMatches), nameof(WaitlistPatientId), nameof(WaitlistPatientName),
            nameof(WaitlistHasPatient), nameof(WaitlistWants), nameof(WaitlistWhen),
            nameof(WaitlistPriority), nameof(CanAddToWaitlist), nameof(HasError),
        })
        {
            _ = RaisePropertyChanged(name);
        }
    }

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

        var last = _session.Hours.WorkingMinutes - block.Minutes;

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

    /// <summary>Not static: the gutter is scaled to this site's own trading day.</summary>
    private IReadOnlyList<DiaryGutterMark> BuildGutter()
    {
        var marks = new List<DiaryGutterMark>();

        // Every half hour. Every fifteen would match the slot granularity but crowds the
        // gutter to the point of being unreadable at this row height.
        for (var minutes = 0; minutes <= _session.Hours.WorkingMinutes; minutes += 30)
        {
            var time = new TimeOnly(0, 0).AddMinutes(_session.Hours.OpenMinutes + minutes);

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
            nameof(List), nameof(ListPage), nameof(ListPageCount), nameof(ListTotal),
            nameof(ListHasRows), nameof(ListSearch), nameof(ListIsFiltered),
            nameof(ListEmptyMessage),
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
