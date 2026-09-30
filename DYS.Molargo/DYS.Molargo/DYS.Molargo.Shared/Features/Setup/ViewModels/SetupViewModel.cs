using DYS.Molargo.Shared.Features.Setup.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Setup.ViewModels;

/// <summary>
/// The setup checklist a practice lands on after signing up.
/// </summary>
/// <remarks>
/// Holds no state of its own beyond the snapshot. Every step is derived from the database
/// each time the screen is opened, so coming back from Admin having filled something in
/// shows it ticked without anything having to be told.
/// </remarks>
public sealed class SetupViewModel : BaseViewModel
{
    private readonly ISetupService _setup;
    private readonly ISessionService _session;

    private SetupProgress _progress = SetupProgress.Empty;

    public SetupViewModel(ISetupService setup, ISessionService session)
    {
        _setup = setup;
        _session = session;

        RefreshCommand = new MvxAsyncCommand(() => RunGuardedAsync(LoadAsync));
    }

    public IMvxAsyncCommand RefreshCommand { get; }

    public override Task Initialize() => RunGuardedAsync(LoadAsync);

    /// <summary>
    /// Whether this account may see the checklist.
    /// </summary>
    /// <remarks>
    /// The same right the Admin section takes, because every step leads there. A nurse
    /// who cannot open Admin would be handed a list of nine links that refuse her.
    /// </remarks>
    public bool IsPermitted => _session.CanReachAdmin && !_session.IsSuperAdmin;

    public string PracticeName => _progress.PracticeName;

    public string? SiteName => _progress.SiteName;

    public int Done => _progress.Done;

    public int Total => _progress.Total;

    public int Percent => _progress.Percent;

    public int Outstanding => _progress.Outstanding;

    public bool IsComplete => _progress.IsComplete;

    /// <summary>"3 of 9 done" — the figure, built here rather than in markup.</summary>
    /// <remarks>
    /// Razor drops the whitespace between an expression and the block after it, so a
    /// sentence assembled from three <c>@</c> expressions arrives with its words run
    /// together. Every counted line in this app is built as a string for that reason.
    /// </remarks>
    public string Tally => $"{Done} of {Total} done";

    /// <summary>What still has to happen, in one line.</summary>
    public string Headline => Outstanding switch
    {
        0 => "Everything essential is set up.",
        1 => "One thing left before this practice is ready to run.",
        _ => $"{Outstanding} things left before this practice is ready to run.",
    };

    public string? TrialLine => _progress.TrialDaysLeft switch
    {
        null => null,
        <= 0 => "The trial has ended.",
        1 => "One day left of the trial.",
        var days => $"{days} days left of the trial.",
    };

    public IReadOnlyList<SetupStep> Practice => Group(SetupGroup.Practice);

    public IReadOnlyList<SetupStep> People => Group(SetupGroup.People);

    public IReadOnlyList<SetupStep> Live => Group(SetupGroup.Live);

    private IReadOnlyList<SetupStep> Group(SetupGroup group) =>
        _progress.Steps.Where(step => step.Group == group).ToList();

    private async Task LoadAsync()
    {
        if (!IsPermitted) return;

        _progress = await _setup.GetAsync().ConfigureAwait(false);

        foreach (var name in new[]
        {
            nameof(IsPermitted), nameof(PracticeName), nameof(SiteName), nameof(Done),
            nameof(Total), nameof(Percent), nameof(Outstanding), nameof(IsComplete),
            nameof(Tally), nameof(Headline), nameof(TrialLine), nameof(Practice),
            nameof(People), nameof(Live),
        })
        {
            await RaisePropertyChanged(name).ConfigureAwait(false);
        }
    }
}
