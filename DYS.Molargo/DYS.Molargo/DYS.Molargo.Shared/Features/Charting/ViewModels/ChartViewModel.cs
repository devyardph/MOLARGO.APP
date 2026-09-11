using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Components;
using DYS.Molargo.Shared.Features.Charting.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Features.Charting.ViewModels;

/// <summary>Which pane of the chart is showing.</summary>
/// <remarks>Explicit values: the tab goes in the URL, so renumbering would move it.</remarks>
public enum ChartTab
{
    Odontogram = 0,
    Perio = 1,
    Endo = 2,
    Ortho = 3,
    SoftTissue = 4,
    NotesTimeline = 5,
}

/// <summary>
/// One tooth as the odontogram renders it.
/// </summary>
/// <param name="Fdi">The stored identity, always FDI.</param>
/// <param name="Label">What to show, in the notation the clinician has selected.</param>
/// <param name="Mark">A two- or three-letter hint under the number — "MOD", "RCT", "MIS".</param>
public sealed record ToothCell(
    string Fdi,
    string Label,
    ToothCondition Condition,
    ToothSurface Surfaces,
    string? Mark,
    bool IsSelected);

/// <summary>
/// The clinical chart: the odontogram, the tooth detail panel and the visit note.
/// </summary>
public sealed partial class ChartViewModel : BaseViewModel<Guid>
{
    private readonly IChartingService _charting;
    private readonly IPerioService _perioService;
    private readonly ISessionService _session;
    private readonly IAppNavigator _navigator;
    private readonly IClock _clock;

    private Guid _id;
    private PatientChart? _chart;
    private ChartTab _tab = ChartTab.Odontogram;
    private ToothNotation _notation = ToothNotation.Fdi;
    private Dentition _dentition = Dentition.Permanent;
    private string _selectedTooth = "46";
    private ToothSurface _surfaces = ToothSurface.None;
    private string? _presenting;
    private string? _treatment;
    private string? _plan;

    public ChartViewModel(
        IChartingService charting,
        IPerioService perioService,
        ISessionService session,
        IAppNavigator navigator,
        IClock clock)
    {
        _charting = charting;
        _perioService = perioService;
        _session = session;
        _navigator = navigator;
        _clock = clock;

        // Built once, in the constructor — never rebuilt per render.
        SelectTabCommand = new MvxAsyncCommand<ChartTab>(SelectTabAsync);
        SelectNotationCommand = new MvxCommand<ToothNotation>(SelectNotation);
        SelectDentitionCommand = new MvxCommand<Dentition>(SelectDentition);
        SelectToothCommand = new MvxCommand<string>(SelectTooth);
        ToggleSurfaceCommand = new MvxCommand<ToothSurface>(ToggleSurface);
        ChartConditionCommand = new MvxAsyncCommand<ToothCondition>(ChartConditionAsync);
        ClearToSoundCommand = new MvxAsyncCommand(ClearToSoundAsync);
        SaveNoteCommand = new MvxAsyncCommand(SaveNoteAsync);
        LockNoteCommand = new MvxAsyncCommand(LockNoteAsync, () => CanLockNote);
        AmendNoteCommand = new MvxAsyncCommand(AmendNoteAsync, () => IsNoteLocked);
        InsertTemplateCommand = new MvxCommand(InsertTemplate);
        BackCommand = new MvxCommand(() => _navigator.ToPatientRecord(_id));

        BuildPerioCommands();
    }

    public IMvxAsyncCommand<ChartTab> SelectTabCommand { get; }

    public IMvxCommand<ToothNotation> SelectNotationCommand { get; }

    public IMvxCommand<Dentition> SelectDentitionCommand { get; }

    public IMvxCommand<string> SelectToothCommand { get; }

    public IMvxCommand<ToothSurface> ToggleSurfaceCommand { get; }

    public IMvxAsyncCommand<ToothCondition> ChartConditionCommand { get; }

    public IMvxAsyncCommand ClearToSoundCommand { get; }

    public IMvxAsyncCommand SaveNoteCommand { get; }

    public IMvxAsyncCommand LockNoteCommand { get; }

    public IMvxAsyncCommand AmendNoteCommand { get; }

    public IMvxCommand InsertTemplateCommand { get; }

    public IMvxCommand BackCommand { get; }

    public override void Prepare(Guid parameter) => _id = parameter;

    public override Task Initialize() => LoadAsync();

    public bool NotFound { get; private set; }

    public PatientEntity? Patient => _chart?.Patient;

    public string PatientName => Patient?.FullName ?? string.Empty;

    public ChartTab Tab => _tab;

    public ToothNotation Notation => _notation;

    public Dentition Dentition => _dentition;

    /// <summary>"adult dentition" — the line beside the patient's name.</summary>
    public string DentitionLabel => _dentition switch
    {
        Dentition.Deciduous => "deciduous dentition",
        Dentition.Mixed => "mixed dentition",
        _ => "adult dentition",
    };

    // ---- odontogram ------------------------------------------------------

    public IReadOnlyList<ToothCell> UpperTeeth { get; private set; } = [];

    public IReadOnlyList<ToothCell> LowerTeeth { get; private set; } = [];

    public string SelectedTooth => _selectedTooth;

    public string SelectedToothLabel => ToothNumbering.Label(_selectedTooth, _notation);

    /// <summary>Every current finding on the selected tooth, for the detail panel.</summary>
    public IReadOnlyList<ToothChartEntry> SelectedFindings =>
        _chart?.For(_selectedTooth) ?? [];

    /// <summary>The one-line description under the tooth heading.</summary>
    public string SelectedDescription
    {
        get
        {
            var findings = SelectedFindings;

            if (findings.Count == 0) return "Sound — no charted findings";

            return string.Join(
                " · ",
                findings.Select(entry => ChartCss.ConditionLabel(entry.Condition)
                    + (entry.Surfaces == ToothSurface.None
                        ? string.Empty
                        : $" ({ChartCss.SurfaceCode(entry.Surfaces)})")));
        }
    }

    /// <summary>The surfaces armed for the next finding.</summary>
    public ToothSurface Surfaces => _surfaces;

    public bool IsSurfaceSet(ToothSurface surface) => _surfaces.HasFlag(surface);

    // ---- note ------------------------------------------------------------

    public ClinicalNote? Note => _chart?.Note;

    public bool IsNoteLocked => Note?.IsLocked ?? false;

    /// <summary>The locked note being corrected, when this note is an amendment.</summary>
    public ClinicalNote? AmendedNote => _chart?.Amends;

    public bool IsAmendment => AmendedNote is not null;

    /// <summary>
    /// Whether the note can be signed.
    /// </summary>
    /// <remarks>
    /// Deliberately does NOT require a saved note to exist. On a fresh visit there is no
    /// ClinicalNote row until a draft is saved, so requiring one left the button disabled
    /// for the whole of the first visit — the one case it is needed most. LockNoteAsync
    /// saves before locking, which is what creates the row.
    /// </remarks>
    public bool CanLockNote => !IsNoteLocked && HasNoteContent;

    private bool HasNoteContent =>
        !string.IsNullOrWhiteSpace(_presenting)
        || !string.IsNullOrWhiteSpace(_treatment)
        || !string.IsNullOrWhiteSpace(_plan);

    public string? Presenting
    {
        get => _presenting;
        set
        {
            if (SetProperty(ref _presenting, value)) RaiseNoteState();
        }
    }

    public string? TreatmentProvided
    {
        get => _treatment;
        set
        {
            if (SetProperty(ref _treatment, value)) RaiseNoteState();
        }
    }

    public string? Plan
    {
        get => _plan;
        set
        {
            if (SetProperty(ref _plan, value)) RaiseNoteState();
        }
    }

    public string LockLabel => IsNoteLocked
        ? "Locked — amendments audited"
        : IsAmendment ? "Sign & lock correction" : "Sign & lock note";

    /// <summary>"Locked 9 Sep · 09:41 · Dr Vance" — the chip on a signed note.</summary>
    public string LockedDetail
    {
        get
        {
            if (Note is not { LockedUtc: { } locked }) return string.Empty;

            return $"Locked {MolargoFormat.DayTime(locked)} · {_session.UserDisplayName ?? "unknown"}";
        }
    }

    /// <summary>
    /// The patient's clinical history, newest day first. Empty until the timeline pane is
    /// opened — the history is a second query and most visits never leave the odontogram.
    /// </summary>
    public IReadOnlyList<VisitHistory> History { get; private set; } = [];

    public bool HistoryLoaded { get; private set; }

    private async Task SelectTabAsync(ChartTab tab)
    {
        _tab = tab;
        await RaisePropertyChanged(nameof(Tab)).ConfigureAwait(false);

        if (tab == ChartTab.Perio)
        {
            if (!_perioLoaded) await LoadPerioAsync().ConfigureAwait(false);
            return;
        }

        if (tab != ChartTab.NotesTimeline || HistoryLoaded) return;

        await LoadHistoryAsync().ConfigureAwait(false);
    }

    private Task LoadHistoryAsync() => RunGuardedAsync(async () =>
    {
        History = await _charting.GetHistoryAsync(_id).ConfigureAwait(false);
        HistoryLoaded = true;

        await RaisePropertyChanged(nameof(History)).ConfigureAwait(false);
        await RaisePropertyChanged(nameof(HistoryLoaded)).ConfigureAwait(false);
    });

    private Task LoadAsync() => RunGuardedAsync(async () =>
    {
        _chart = await _charting.GetChartAsync(_id).ConfigureAwait(false);

        if (_chart is null)
        {
            NotFound = true;
            RaiseAllDerived();
            return;
        }

        // The note's stored text populates the boxes. Only on load: doing it on every
        // refresh would overwrite what the clinician is part-way through typing.
        _presenting = _chart.Note?.Presenting;
        _treatment = _chart.Note?.TreatmentProvided;
        _plan = _chart.Note?.Plan;

        // A tooth the current arch does not show cannot be the selected one — switching
        // to the deciduous view with 46 selected left the detail panel editing a tooth
        // with no button on screen.
        EnsureSelectionIsVisible();

        RebuildArches();
        RaiseAllDerived();
    });

    private void SelectNotation(ToothNotation notation)
    {
        _notation = notation;
        RebuildArches();

        RaisePropertyChanged(nameof(Notation));
        RaisePropertyChanged(nameof(SelectedToothLabel));
        RaiseArches();
    }

    private void SelectDentition(Dentition dentition)
    {
        _dentition = dentition;
        EnsureSelectionIsVisible();
        RebuildArches();

        RaisePropertyChanged(nameof(Dentition));
        RaisePropertyChanged(nameof(DentitionLabel));
        RaiseSelection();
        RaiseArches();
    }

    private void SelectTooth(string? fdi)
    {
        if (string.IsNullOrWhiteSpace(fdi) || fdi == _selectedTooth) return;

        _selectedTooth = fdi;

        // The armed surfaces are cleared with the selection. Carrying them across meant
        // tapping a new tooth and charting it with the previous tooth's surfaces still
        // set, which is a wrong entry that looks deliberate.
        _surfaces = ToothSurface.None;

        RebuildArches();
        RaiseSelection();
        RaiseArches();
    }

    private void ToggleSurface(ToothSurface surface)
    {
        _surfaces = _surfaces.HasFlag(surface) ? _surfaces & ~surface : _surfaces | surface;

        RaisePropertyChanged(nameof(Surfaces));
    }

    private Task ChartConditionAsync(ToothCondition condition) => RunGuardedAsync(async () =>
    {
        await _charting
            .ChartAsync(_id, _selectedTooth, condition, _surfaces,
                providerId: _session.ProviderId)
            .ConfigureAwait(false);

        _surfaces = ToothSurface.None;
        await ReloadChartAsync().ConfigureAwait(false);
    });

    private Task ClearToSoundAsync() => RunGuardedAsync(async () =>
    {
        await _charting
            .ClearToSoundAsync(_id, _selectedTooth, _session.ProviderId)
            .ConfigureAwait(false);

        _surfaces = ToothSurface.None;
        await ReloadChartAsync().ConfigureAwait(false);
    });

    private Task SaveNoteAsync() => RunGuardedAsync(async () =>
    {
        await _charting
            .SaveNoteAsync(_id, _session.ProviderId ?? Guid.Empty, _presenting, _treatment, _plan)
            .ConfigureAwait(false);

        await ReloadChartAsync().ConfigureAwait(false);
    });

    private Task LockNoteAsync() => RunGuardedAsync(async () =>
    {
        // Saved before locking, so whatever is in the boxes is what gets signed. Locking
        // first would freeze the last saved version and silently discard the edits made
        // since — on the one document where that is least acceptable.
        var note = await _charting
            .SaveNoteAsync(_id, _session.ProviderId ?? Guid.Empty, _presenting, _treatment, _plan)
            .ConfigureAwait(false);

        await _charting.LockNoteAsync(note.Id).ConfigureAwait(false);
        await ReloadChartAsync().ConfigureAwait(false);

        RaiseNoteState();
    });

    /// <summary>
    /// Opens a correction to the signed note.
    /// </summary>
    /// <remarks>
    /// The boxes start from what the original says rather than empty. A correction is
    /// almost always a change to one line, and retyping the rest from a read-only panel is
    /// how the retyped parts end up differing from the note being corrected.
    /// </remarks>
    private Task AmendNoteAsync() => RunGuardedAsync(async () =>
    {
        if (Note is not { IsLocked: true } locked) return;

        await _charting
            .AmendNoteAsync(locked.Id, _session.ProviderId ?? Guid.Empty)
            .ConfigureAwait(false);

        _presenting = locked.Presenting;
        _treatment = locked.TreatmentProvided;
        _plan = locked.Plan;

        await ReloadChartAsync().ConfigureAwait(false);

        await RaisePropertyChanged(nameof(Presenting)).ConfigureAwait(false);
        await RaisePropertyChanged(nameof(TreatmentProvided)).ConfigureAwait(false);
        await RaisePropertyChanged(nameof(Plan)).ConfigureAwait(false);
    });

    /// <summary>
    /// Fills the note from what has been charted this visit.
    /// </summary>
    /// <remarks>
    /// Built from the chart rather than being a fixed block of text — the prototype's
    /// template is a hard-coded paragraph about tooth 46, and a template that has to be
    /// edited every time is one nobody uses. Anything already typed is left alone.
    /// </remarks>
    private void InsertTemplate()
    {
        var today = _clock.Today;

        var charted = _chart?.Current
            .Where(entry => entry.ObservedOn == today && entry.Condition != ToothCondition.Sound)
            .OrderBy(entry => entry.ToothNumber)
            .Select(entry => ToothNumbering.Label(entry.ToothNumber, _notation)
                + (entry.Surfaces == ToothSurface.None
                    ? string.Empty
                    : $" ({ChartCss.SurfaceCode(entry.Surfaces)})")
                + $": {ChartCss.ConditionLabel(entry.Condition)}")
            .ToList() ?? [];

        if (string.IsNullOrWhiteSpace(_treatment))
        {
            TreatmentProvided = charted.Count > 0
                ? string.Join(Environment.NewLine, charted)
                : "No findings charted this visit.";
        }

        if (string.IsNullOrWhiteSpace(_presenting)) Presenting = "Routine examination.";

        if (string.IsNullOrWhiteSpace(_plan))
        {
            Plan = "Oral hygiene advice given. Review at next recall.";
        }
    }

    private async Task ReloadChartAsync()
    {
        _chart = await _charting.GetChartAsync(_id).ConfigureAwait(false);
        RebuildArches();
        RaiseAllDerived();

        // The timeline is a separate query, so a note signed on the odontogram pane would
        // otherwise still be missing from a history loaded earlier in the visit.
        if (HistoryLoaded)
        {
            History = await _charting.GetHistoryAsync(_id).ConfigureAwait(false);
            await RaisePropertyChanged(nameof(History)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Moves the selection onto a tooth the current arch actually shows.
    /// </summary>
    private void EnsureSelectionIsVisible()
    {
        var arches = ToothNumbering.ArchesFor(_dentition);

        if (arches.Upper.Contains(_selectedTooth) || arches.Lower.Contains(_selectedTooth)) return;

        // The lower first molar where the arch has one, since that is where charting
        // usually starts; otherwise whatever the arch begins with.
        _selectedTooth = arches.Lower.FirstOrDefault(tooth => tooth is "46" or "85")
            ?? arches.Lower.FirstOrDefault()
            ?? arches.Upper[0];

        _surfaces = ToothSurface.None;
    }

    private void RebuildArches()
    {
        var arches = ToothNumbering.ArchesFor(_dentition);

        UpperTeeth = arches.Upper.Select(BuildCell).ToList();
        LowerTeeth = arches.Lower.Select(BuildCell).ToList();
    }

    private ToothCell BuildCell(string fdi)
    {
        var dominant = _chart?.DominantFor(fdi);

        return new ToothCell(
            fdi,
            ToothNumbering.Label(fdi, _notation),
            dominant?.Condition ?? ToothCondition.Sound,
            dominant?.Surfaces ?? ToothSurface.None,
            dominant is null ? null : ChartCss.Mark(dominant.Condition, dominant.Surfaces),
            fdi == _selectedTooth);
    }

    private void Set<T>(ref T field, T value, string name)
    {
        field = value;
        RaisePropertyChanged(name);
    }

    /// <summary>
    /// Re-evaluates the lock button. Called from the note setters, so typing the first
    /// character enables signing rather than leaving it dead until something else
    /// happens to re-render.
    /// </summary>
    private void RaiseNoteState()
    {
        RaisePropertyChanged(nameof(CanLockNote));
        LockNoteCommand.RaiseCanExecuteChanged();
        AmendNoteCommand.RaiseCanExecuteChanged();
    }

    private void RaiseArches()
    {
        RaisePropertyChanged(nameof(UpperTeeth));
        RaisePropertyChanged(nameof(LowerTeeth));
    }

    private void RaiseSelection()
    {
        RaisePropertyChanged(nameof(SelectedTooth));
        RaisePropertyChanged(nameof(SelectedToothLabel));
        RaisePropertyChanged(nameof(SelectedFindings));
        RaisePropertyChanged(nameof(SelectedDescription));
        RaisePropertyChanged(nameof(Surfaces));
    }

    /// <summary>
    /// Everything the screen reads. Listed explicitly rather than raising a blanket
    /// change, which would re-render the note box being typed in.
    /// </summary>
    private void RaiseAllDerived()
    {
        foreach (var name in new[]
        {
            nameof(NotFound), nameof(Patient), nameof(PatientName), nameof(Tab),
            nameof(Notation), nameof(Dentition), nameof(DentitionLabel),
            nameof(UpperTeeth), nameof(LowerTeeth), nameof(SelectedTooth),
            nameof(SelectedToothLabel), nameof(SelectedFindings), nameof(SelectedDescription),
            nameof(Surfaces), nameof(Note), nameof(IsNoteLocked), nameof(CanLockNote),
            nameof(LockLabel), nameof(LockedDetail), nameof(AmendedNote), nameof(IsAmendment),
        })
        {
            RaisePropertyChanged(name);
        }

        LockNoteCommand.RaiseCanExecuteChanged();
        AmendNoteCommand.RaiseCanExecuteChanged();
    }
}
