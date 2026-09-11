using DYS.Molargo.Domain;
using DYS.Molargo.Shared.Components;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Features.Charting.Services;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Charting.ViewModels;

/// <summary>
/// The perio pane's half of the chart view model.
/// </summary>
/// <remarks>
/// A partial rather than a view model of its own. The perio pane is one tab of one screen
/// and shares the patient, the notation and the selected tooth with the rest of it —
/// splitting it into a second view model would mean keeping that state in step across two
/// objects. Split across files only because one file holding both is unreadable.
/// </remarks>
public sealed partial class ChartViewModel
{
    private PerioChart _perio = new(null, [], []);
    private PerioSextant _sextant = PerioSextant.UpperRight;
    private bool _showLingual;
    private bool _perioLoaded;

    /// <summary>Which sextant the table shows.</summary>
    public PerioSextant Sextant => _sextant;

    /// <summary>
    /// Whether the tongue-side sites are showing instead of the cheek-side ones.
    /// </summary>
    /// <remarks>
    /// A flip rather than six columns. The design shows three sites and notes "lingual
    /// sites on the flip view": six numbers per row on a tablet leaves each too small to
    /// hit, and probing is done one face at a time anyway.
    /// </remarks>
    public bool ShowLingual => _showLingual;

    public PerioChart Perio => _perio;

    public bool PerioLoaded => _perioLoaded;

    /// <summary>False until the patient has ever been probed.</summary>
    public bool HasPerioExam => _perio.Current is not null;

    public bool IsPerioExamComplete => _perio.Current?.IsComplete ?? false;

    /// <summary>The exam being edited or reviewed.</summary>
    public IReadOnlyList<PerioSummary> PerioHistory => _perio.History;

    public PerioSummary? PerioSummaryNow => _perio.CurrentSummary;

    /// <summary>The teeth of the current sextant, in chart order.</summary>
    public IReadOnlyList<string> SextantTeeth => PerioSites.TeethIn(_sextant);

    /// <summary>The three sites the table's columns show, given the flip.</summary>
    public IReadOnlyList<PerioSite> VisibleSites =>
        _showLingual ? PerioSites.LingualSites : PerioSites.BuccalSites;

    /// <summary>"Upper right · cheek side" — the caption above the table.</summary>
    public string PerioCaption
    {
        get
        {
            var side = _showLingual ? "tongue side" : "cheek side";

            var exam = _perio.Current is { } current
                ? MolargoFormat.Date(current.ExamDate)
                : "no exam";

            return $"{PerioSites.SextantLabel(_sextant)} · {side} · {exam}";
        }
    }

    /// <summary>
    /// The worst deep-site count in the history, so the comparison bars share a scale.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bars measure deep sites, not mean pocket depth — a deliberate departure from
    /// the design's "Comparison — mean pocket depth". A whole-mouth mean is dominated by
    /// the healthy sites, which are most of them, so it barely moves even when treatment
    /// works: the example patient went from 40 sites at 5mm-plus down to 1, and her mean
    /// fell only 3.2mm to 3.0mm — bars of 80% against 75%, which reports a transformation
    /// as a rounding error.
    /// </para>
    /// <para>
    /// The mean is still shown, as the number beside each bar. It is the right figure for
    /// "how is this mouth overall"; it is the wrong one for "did the scaling work".
    /// </para>
    /// </remarks>
    public int PerioScaleDeepSites
    {
        get
        {
            var worst = _perio.History
                .Select(entry => entry.DeepSiteCount)
                .DefaultIfEmpty(0)
                .Max();

            // A floor, or one remaining deep site fills the bar and reads as untreated.
            return Math.Max(worst, 8);
        }
    }

    public IMvxAsyncCommand<PerioSextant> SelectSextantCommand { get; private set; } = default!;

    public IMvxAsyncCommand ToggleArchSideCommand { get; private set; } = default!;

    public IMvxAsyncCommand StartPerioExamCommand { get; private set; } = default!;

    public IMvxAsyncCommand CompletePerioExamCommand { get; private set; } = default!;

    public IMvxAsyncCommand<PerioSiteTap> CycleDepthCommand { get; private set; } = default!;

    public IMvxAsyncCommand<PerioSiteTap> ToggleBleedingCommand { get; private set; } = default!;

    public IMvxAsyncCommand<PerioSiteTap> ToggleSuppurationCommand { get; private set; } = default!;

    public IMvxAsyncCommand<PerioSiteTap> CycleRecessionCommand { get; private set; } = default!;

    public IMvxAsyncCommand<string> CycleMobilityCommand { get; private set; } = default!;

    public IMvxAsyncCommand<string> CycleFurcationCommand { get; private set; } = default!;

    public IMvxAsyncCommand<Guid> ReviewPerioExamCommand { get; private set; } = default!;

    /// <summary>Built from the constructor, alongside the odontogram's commands.</summary>
    private void BuildPerioCommands()
    {
        SelectSextantCommand = new MvxAsyncCommand<PerioSextant>(SelectSextantAsync);
        ToggleArchSideCommand = new MvxAsyncCommand(ToggleArchSideAsync);
        StartPerioExamCommand = new MvxAsyncCommand(StartPerioExamAsync);
        CompletePerioExamCommand = new MvxAsyncCommand(CompletePerioExamAsync);
        CycleDepthCommand = new MvxAsyncCommand<PerioSiteTap>(tap => CycleDepthAsync(tap!));
        ToggleBleedingCommand = new MvxAsyncCommand<PerioSiteTap>(tap => ToggleBleedingAsync(tap!));
        ToggleSuppurationCommand =
            new MvxAsyncCommand<PerioSiteTap>(tap => ToggleSuppurationAsync(tap!));
        CycleRecessionCommand = new MvxAsyncCommand<PerioSiteTap>(tap => CycleRecessionAsync(tap!));
        CycleMobilityCommand = new MvxAsyncCommand<string>(fdi => CycleMobilityAsync(fdi!));
        CycleFurcationCommand = new MvxAsyncCommand<string>(fdi => CycleFurcationAsync(fdi!));
        ReviewPerioExamCommand = new MvxAsyncCommand<Guid>(ReviewPerioExamAsync);
    }

    // ---- reading ---------------------------------------------------------

    public int? DepthAt(string fdi, PerioSite site) => _perio.Tooth(fdi)?.DepthAt(site);

    public bool BleedingAt(string fdi, PerioSite site) =>
        _perio.Tooth(fdi)?.BleedingAt(site) ?? false;

    public bool SuppurationAt(string fdi, PerioSite site) =>
        _perio.Tooth(fdi)?.SuppurationAt(site) ?? false;

    /// <summary>Recession on the face currently showing.</summary>
    public int? RecessionOn(string fdi) =>
        _perio.Tooth(fdi)?.At(VisibleSites[0])?.RecessionMm;

    public ToothMobility? MobilityOf(string fdi) => _perio.Tooth(fdi)?.Tooth?.Mobility;

    public FurcationGrade? FurcationOf(string fdi) => _perio.Tooth(fdi)?.Tooth?.Furcation;

    public bool CanChartFurcation(string fdi) => PerioSites.HasFurcation(fdi);

    /// <summary>Whether the tooth is charted as missing, so its row can say so.</summary>
    public bool IsToothMissing(string fdi) =>
        _chart?.DominantFor(fdi)?.Condition == ToothCondition.Missing;

    // ---- loading and editing ---------------------------------------------

    private Task LoadPerioAsync() => RunGuardedAsync(async () =>
    {
        _perio = await _perioService.GetChartAsync(_id).ConfigureAwait(false);
        _perioLoaded = true;

        RaisePerio();
    });

    private async Task SelectSextantAsync(PerioSextant sextant)
    {
        _sextant = sextant;

        await RaisePropertyChanged(nameof(Sextant)).ConfigureAwait(false);
        await RaisePropertyChanged(nameof(SextantTeeth)).ConfigureAwait(false);
        await RaisePropertyChanged(nameof(PerioCaption)).ConfigureAwait(false);
    }

    private async Task ToggleArchSideAsync()
    {
        _showLingual = !_showLingual;

        await RaisePropertyChanged(nameof(ShowLingual)).ConfigureAwait(false);
        await RaisePropertyChanged(nameof(VisibleSites)).ConfigureAwait(false);
        await RaisePropertyChanged(nameof(PerioCaption)).ConfigureAwait(false);
    }

    private Task StartPerioExamAsync() => RunGuardedAsync(async () =>
    {
        await _perioService
            .StartExamAsync(_id, _session.ProviderId)
            .ConfigureAwait(false);

        _perio = await _perioService.GetChartAsync(_id).ConfigureAwait(false);
        RaisePerio();
    });

    private Task CompletePerioExamAsync() => RunGuardedAsync(async () =>
    {
        if (_perio.Current is not { } current) return;

        await _perioService.CompleteExamAsync(current.Id).ConfigureAwait(false);

        _perio = await _perioService.GetChartAsync(_id, current.Id).ConfigureAwait(false);
        RaisePerio();
    });

    private Task ReviewPerioExamAsync(Guid examId) => RunGuardedAsync(async () =>
    {
        _perio = await _perioService.GetChartAsync(_id, examId).ConfigureAwait(false);
        RaisePerio();
    });

    /// <summary>
    /// Steps a pocket depth on each tap, wrapping past the deepest back to unrecorded.
    /// </summary>
    /// <remarks>
    /// Cycling rather than typing, which is what the design does and what a gloved hand on
    /// a tablet needs. Starts at 1mm and wraps through blank so a mis-tap can be cleared
    /// without a separate control — and blank means "not probed", not zero.
    /// </remarks>
    private Task CycleDepthAsync(PerioSiteTap tap) => RunGuardedAsync(async () =>
    {
        if (_perio.Current is not { } current) return;

        var next = DepthAt(tap.Fdi, tap.Site) switch
        {
            null => 1,
            >= PerioCycleCeilingMm => (int?)null,
            var depth => depth + 1,
        };

        await _perioService
            .SetDepthAsync(current.Id, tap.Fdi, tap.Site, next)
            .ConfigureAwait(false);

        await ReloadPerioAsync().ConfigureAwait(false);
    });

    /// <summary>
    /// Where the depth cycle wraps.
    /// </summary>
    /// <remarks>
    /// 12mm, short of the probe's 15mm. Tapping to a genuinely rare 13mm pocket costs
    /// thirteen taps either way, and stopping the cycle sooner keeps the common range
    /// reachable. Deeper readings will need typed entry.
    /// </remarks>
    private const int PerioCycleCeilingMm = 12;

    private Task ToggleBleedingAsync(PerioSiteTap tap) => RunGuardedAsync(async () =>
    {
        if (_perio.Current is not { } current) return;

        await _perioService
            .ToggleBleedingAsync(current.Id, tap.Fdi, tap.Site)
            .ConfigureAwait(false);

        await ReloadPerioAsync().ConfigureAwait(false);
    });

    private Task ToggleSuppurationAsync(PerioSiteTap tap) => RunGuardedAsync(async () =>
    {
        if (_perio.Current is not { } current) return;

        await _perioService
            .ToggleSuppurationAsync(current.Id, tap.Fdi, tap.Site)
            .ConfigureAwait(false);

        await ReloadPerioAsync().ConfigureAwait(false);
    });

    private Task CycleRecessionAsync(PerioSiteTap tap) => RunGuardedAsync(async () =>
    {
        if (_perio.Current is not { } current) return;

        // 0mm is a real reading here — the margin sitting at the CEJ — so the cycle runs
        // 0,1,2… and wraps to blank rather than starting at 1.
        var next = RecessionOn(tap.Fdi) switch
        {
            null => 0,
            >= PerioRecessionCeilingMm => (int?)null,
            var recession => recession + 1,
        };

        await _perioService
            .SetRecessionAsync(current.Id, tap.Fdi, tap.Site, next)
            .ConfigureAwait(false);

        await ReloadPerioAsync().ConfigureAwait(false);
    });

    private const int PerioRecessionCeilingMm = 9;

    private Task CycleMobilityAsync(string fdi) => RunGuardedAsync(async () =>
    {
        if (_perio.Current is not { } current) return;

        var next = MobilityOf(fdi) switch
        {
            null => ToothMobility.None,
            ToothMobility.None => ToothMobility.Grade1,
            ToothMobility.Grade1 => ToothMobility.Grade2,
            ToothMobility.Grade2 => ToothMobility.Grade3,
            _ => (ToothMobility?)null,
        };

        await _perioService.SetMobilityAsync(current.Id, fdi, next).ConfigureAwait(false);

        await ReloadPerioAsync().ConfigureAwait(false);
    });

    private Task CycleFurcationAsync(string fdi) => RunGuardedAsync(async () =>
    {
        if (_perio.Current is not { } current) return;

        // Refused on a single-rooted tooth: there is no furcation to probe, so a grade
        // there is a number nobody could have measured.
        if (!PerioSites.HasFurcation(fdi)) return;

        var next = FurcationOf(fdi) switch
        {
            null => FurcationGrade.None,
            FurcationGrade.None => FurcationGrade.Grade1,
            FurcationGrade.Grade1 => FurcationGrade.Grade2,
            FurcationGrade.Grade2 => FurcationGrade.Grade3,
            _ => (FurcationGrade?)null,
        };

        await _perioService.SetFurcationAsync(current.Id, fdi, next).ConfigureAwait(false);

        await ReloadPerioAsync().ConfigureAwait(false);
    });

    private async Task ReloadPerioAsync()
    {
        var examId = _perio.Current?.Id;

        _perio = examId is { } id
            ? await _perioService.GetChartAsync(_id, id).ConfigureAwait(false)
            : await _perioService.GetChartAsync(_id).ConfigureAwait(false);

        RaisePerio();
    }

    private void RaisePerio()
    {
        foreach (var name in new[]
        {
            nameof(Perio), nameof(PerioLoaded), nameof(HasPerioExam),
            nameof(IsPerioExamComplete), nameof(PerioHistory), nameof(PerioSummaryNow),
            nameof(PerioCaption), nameof(PerioScaleDeepSites), nameof(SextantTeeth),
            nameof(VisibleSites), nameof(Sextant), nameof(ShowLingual),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}

/// <summary>One tap on the perio table: which tooth, which site.</summary>
public sealed record PerioSiteTap(string Fdi, PerioSite Site);
