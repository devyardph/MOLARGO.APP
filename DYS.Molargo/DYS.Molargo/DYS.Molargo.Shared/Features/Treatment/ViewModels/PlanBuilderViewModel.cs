using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Components;
using DYS.Molargo.Shared.Features.Treatment.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Treatment.ViewModels;

/// <summary>
/// Which patient, and which plan — or <see cref="Guid.Empty"/> for a new one.
/// </summary>
/// <remarks>
/// A record struct rather than two separate parameters because
/// <c>MvvmComponentBase&lt;TViewModel, TParameter&gt;</c> takes one, and compares it with
/// the default equality comparer to decide whether to reload. A record gives that
/// comparison value semantics for free; a class would reload on every render.
/// </remarks>
public readonly record struct PlanBuilderArgs(Guid PatientId, Guid PlanId);

/// <summary>Where the builder is in the save sequence.</summary>
public enum PlanBuilderStage
{
    Editing = 0,
    Saved = 1,
}

/// <summary>
/// The design's plan builder: the charted findings turned into priced lines on the left,
/// staged into visits on the right.
/// </summary>
public sealed class PlanBuilderViewModel : BaseViewModel<PlanBuilderArgs>
{
    private readonly ITreatmentPlanService _plans;
    private readonly IAppNavigator _navigator;
    private readonly ISessionService _session;

    private PlanBuilderArgs _args;
    private PlanDraft? _draft;
    private PlanBuilderOptions _options = new();
    private PlanBuilderStage _stage = PlanBuilderStage.Editing;
    private IReadOnlyDictionary<string, string> _errors = new Dictionary<string, string>();
    private bool _notFound;

    /// <summary>The catalogue picker's filter, and whether it is open.</summary>
    private string _codeSearch = string.Empty;
    private bool _isAdding;

    // The delete confirm, inline rather than a second screen: the decision is made while
    // looking at what the plan contains, which is the thing being deleted.
    private bool _isDeleting;

    public PlanBuilderViewModel(
        ITreatmentPlanService plans,
        IAppNavigator navigator,
        ISessionService session)
    {
        _plans = plans;
        _navigator = navigator;
        _session = session;

        // Built once, in the constructor — never rebuilt per render.
        SaveCommand = new MvxAsyncCommand(SaveAsync);
        PresentCommand = new MvxAsyncCommand(PresentAsync);
        BackCommand = new MvxCommand(BackToRecord);
        SelectProviderCommand = new MvxCommand<Guid>(SelectProvider);
        SetRecommendedCommand = new MvxCommand<bool>(SetRecommended);
        CycleVisitCommand = new MvxCommand<int>(CycleVisit);
        RemoveItemCommand = new MvxCommand<int>(RemoveItem);
        StartAddingCommand = new MvxCommand(() => SetAdding(true));
        CancelAddingCommand = new MvxCommand(() => SetAdding(false));
        AddCodeCommand = new MvxCommand<Guid>(AddCode);
        StartDeleteCommand = new MvxCommand(() => SetDeleting(true));
        CancelDeleteCommand = new MvxCommand(() => SetDeleting(false));
        ConfirmDeleteCommand = new MvxAsyncCommand(ConfirmDeleteAsync);
    }

    public IMvxAsyncCommand SaveCommand { get; }

    /// <summary>Saves, marks the plan presented, and opens the chairside screen.</summary>
    /// <remarks>
    /// One button, three steps, because they are one intention. Making the clinician save
    /// and then present would leave a plan presented from a version they had edited since
    /// — which is the exact mismatch the frozen total exists to prevent.
    /// </remarks>
    public IMvxAsyncCommand PresentCommand { get; }

    public IMvxCommand BackCommand { get; }

    public IMvxCommand<Guid> SelectProviderCommand { get; }

    /// <summary>The design's PRIORITY control — recommended, or an alternative.</summary>
    public IMvxCommand<bool> SetRecommendedCommand { get; }

    /// <summary>Moves a line to the next visit, wrapping back to the first.</summary>
    public IMvxCommand<int> CycleVisitCommand { get; }

    public IMvxCommand<int> RemoveItemCommand { get; }

    public IMvxCommand StartAddingCommand { get; }

    public IMvxCommand CancelAddingCommand { get; }

    public IMvxCommand<Guid> AddCodeCommand { get; }

    /// <summary>Opens the confirm for deleting this draft.</summary>
    /// <remarks>
    /// Only a draft, and only one already saved. A plan the patient has seen is a clinical
    /// record and is withdrawn from the presentation screen instead, and a new plan that
    /// has never been saved is abandoned by navigating away.
    /// </remarks>
    public IMvxCommand StartDeleteCommand { get; }

    public IMvxCommand CancelDeleteCommand { get; }

    public IMvxAsyncCommand ConfirmDeleteCommand { get; }

    public override void Prepare(PlanBuilderArgs parameter) => _args = parameter;

    public override Task Initialize() => LoadAsync();

    // ---- what the view reads ---------------------------------------------

    public bool NotFound => _notFound;

    public PlanDraft? Draft => _draft;

    public PlanBuilderOptions Options => _options;

    public PlanBuilderStage Stage => _stage;

    public bool IsEditing => _stage == PlanBuilderStage.Editing;

    public bool IsNew => _draft?.IsNew ?? true;

    public string Title => IsNew ? "New treatment plan" : "Edit treatment plan";

    /// <summary>"Plan builder — Margaret Yuen", as the design's heading.</summary>
    public string Heading => _draft is { } draft && draft.PatientName.Length > 0
        ? $"Plan builder — {draft.PatientName}"
        : "Plan builder";

    public string SavedMessage => _draft is { } draft
        ? $"{draft.Title} — {draft.Items.Count} "
            + $"{(draft.Items.Count == 1 ? "item" : "items")} over "
            + $"{VisitCount} {(VisitCount == 1 ? "visit" : "visits")}, "
            + $"{MolargoFormat.Money(draft.Total)}."
        : string.Empty;

    public int VisitCount => _draft?.UsedStages.Count ?? 0;

    public IReadOnlyList<int> Stages => Enumerable.Range(1, PlanDraft.MaxStages).ToList();

    public bool IsAdding => _isAdding;

    public string CodeSearch
    {
        get => _codeSearch;
        set => SetProperty(ref _codeSearch, value);
    }

    /// <summary>The catalogue, narrowed by what has been typed into the picker.</summary>
    public IReadOnlyList<PlanCodeOption> CodeResults
    {
        get
        {
            var term = _codeSearch.Trim();

            if (term.Length == 0) return _options.Codes;

            return _options.Codes
                .Where(code =>
                    code.ItemNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || code.Label.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || code.Description.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (code.Category?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }
    }

    // ---- plan fields the view binds --------------------------------------

    public string PlanTitle
    {
        get => _draft?.Title ?? string.Empty;
        set
        {
            if (_draft is null) return;

            _draft.Title = value;
            RaisePropertyChanged();
        }
    }

    public string? Rationale
    {
        get => _draft?.Rationale;
        set
        {
            if (_draft is null) return;

            _draft.Rationale = value;
            RaisePropertyChanged();
        }
    }

    /// <summary>
    /// The fund estimate, bound as a number the clinician types.
    /// </summary>
    /// <remarks>
    /// Nullable, and null renders as an empty box rather than "0". A zero typed on purpose
    /// and a field nobody filled in mean different things to a patient reading a gap, and
    /// a box pre-filled with 0 makes the second look like the first.
    /// </remarks>
    public decimal? EstimatedBenefit
    {
        get => _draft is { EstimatedBenefit: > 0m } draft ? draft.EstimatedBenefit : null;
        set
        {
            if (_draft is null) return;

            _draft.EstimatedBenefit = value ?? 0m;
            RaiseTotals();
        }
    }

    public DateOnly? EstimateValidUntil
    {
        get => _draft?.EstimateValidUntil;
        set
        {
            if (_draft is null) return;

            _draft.EstimateValidUntil = value;
            RaisePropertyChanged();
        }
    }

    public bool IsRecommended => _draft?.IsRecommended ?? true;

    public Guid ProviderId => _draft?.ProviderId ?? Guid.Empty;

    public bool IsProviderSelected(Guid providerId) => ProviderId == providerId;

    public decimal Total => _draft?.Total ?? 0m;

    public decimal Benefit => _draft?.EstimatedBenefit ?? 0m;

    public decimal Gap => _draft?.Gap ?? 0m;

    public bool HasBenefitEstimate => _draft?.HasBenefitEstimate ?? false;

    /// <summary>
    /// Why the site, and not a fee schedule the design shows a picker for.
    /// </summary>
    public string PricingNote => _session.LocationName is { Length: > 0 } site
        ? $"Fees are {site}'s prices from the item catalogue."
        : "Fees come from the item catalogue.";

    // ---- validation -------------------------------------------------------

    public IReadOnlyDictionary<string, string> Errors => _errors;

    public string? ErrorFor(string field) => _errors.GetValueOrDefault(field);

    /// <summary>The error that belongs to no particular field.</summary>
    public string? FormError => _errors.GetValueOrDefault(string.Empty);

    /// <summary>Whether this draft can be deleted at all.</summary>
    public bool CanDelete =>
        _draft is { IsNew: false, Status: TreatmentPlanStatus.Draft };

    public bool IsDeleting => _isDeleting;

    // ---- loading ----------------------------------------------------------

    private Task LoadAsync() => RunGuardedAsync(async () =>
    {
        _notFound = false;
        _stage = PlanBuilderStage.Editing;
        _errors = new Dictionary<string, string>();

        _draft = _args.PlanId == Guid.Empty
            ? await _plans.StartFromChartAsync(_args.PatientId).ConfigureAwait(false)
            : await _plans.GetDraftAsync(_args.PlanId).ConfigureAwait(false);

        if (_draft is null)
        {
            _notFound = true;
            RaiseAll();
            return;
        }

        _options = await _plans
            .GetOptionsAsync(_draft.PatientId, _draft.PracticeLocationId)
            .ConfigureAwait(false);

        RaiseAll();
    });

    // ---- editing ----------------------------------------------------------

    private void SelectProvider(Guid providerId)
    {
        if (_draft is null) return;

        _draft.ProviderId = providerId;

        RaisePropertyChanged(nameof(ProviderId));
    }

    private void SetRecommended(bool recommended)
    {
        if (_draft is null) return;

        _draft.IsRecommended = recommended;

        RaisePropertyChanged(nameof(IsRecommended));
    }

    private void CycleVisit(int index)
    {
        if (_draft is null || index < 0 || index >= _draft.Items.Count) return;

        var item = _draft.Items[index];

        // A booked or delivered line is not the builder's to move: an appointment already
        // exists for it, and the two would then disagree about which visit it is in.
        if (item.IsLocked) return;

        item.StageNumber = item.StageNumber >= PlanDraft.MaxStages
            ? 1
            : item.StageNumber + 1;

        RaiseAll();
    }

    private void RemoveItem(int index)
    {
        if (_draft is null || index < 0 || index >= _draft.Items.Count) return;

        if (_draft.Items[index].IsLocked) return;

        _draft.Items.RemoveAt(index);

        RaiseAll();
    }

    private void SetAdding(bool adding)
    {
        _isAdding = adding;

        if (!adding) _codeSearch = string.Empty;

        RaisePropertyChanged(nameof(IsAdding));
        RaisePropertyChanged(nameof(CodeSearch));
        RaisePropertyChanged(nameof(CodeResults));
    }

    private void AddCode(Guid codeId)
    {
        if (_draft is null) return;

        if (_options.Codes.FirstOrDefault(code => code.Id == codeId) is not { } code) return;

        _draft.Items.Add(new PlanDraftItem
        {
            ProcedureCodeId = code.Id,
            ItemNumber = code.ItemNumber,
            Description = code.Description,
            FriendlyName = code.FriendlyName,

            // Left blank for the clinician to fill in. Guessing a tooth is worse than
            // asking: a per-tooth item on the wrong tooth is a rejected claim, and on a
            // plan it is a patient quoted for work on a tooth that is fine.
            ToothNumber = null,
            Fee = code.Fee,
            DurationMinutes = code.DurationMinutes,

            // Into the last visit that has anything in it, which is where a line added
            // while thinking through a course almost always belongs.
            StageNumber = _draft.UsedStages.Count > 0 ? _draft.UsedStages[^1] : 1,
            DisplayOrder = _draft.Items.Count,
        });

        SetAdding(false);
        RaiseAll();
    }

    // ---- saving -----------------------------------------------------------

    private Task SaveAsync() => RunGuardedAsync(async () =>
    {
        if (_draft is null) return;

        var result = await _plans.SaveAsync(_draft).ConfigureAwait(false);

        _errors = result.Errors;

        if (!result.Succeeded)
        {
            RaiseAll();
            return;
        }

        _stage = PlanBuilderStage.Saved;

        RaiseAll();
    });

    private Task PresentAsync() => RunGuardedAsync(async () =>
    {
        if (_draft is null) return;

        var result = await _plans.SaveAsync(_draft).ConfigureAwait(false);

        _errors = result.Errors;

        if (!result.Succeeded)
        {
            RaiseAll();
            return;
        }

        var refusal = await _plans.PresentAsync(result.PlanId).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            _errors = new Dictionary<string, string> { [string.Empty] = refusal };
            RaiseAll();
            return;
        }

        _navigator.ToPlanPresentation(result.PlanId);
    });

    private void SetDeleting(bool value)
    {
        _isDeleting = value;
        _errors = new Dictionary<string, string>();

        RaisePropertyChanged(nameof(IsDeleting));
        RaisePropertyChanged(nameof(FormError));
    }

    private Task ConfirmDeleteAsync() => RunGuardedAsync(async () =>
    {
        if (_draft is not { } draft || draft.IsNew) return;

        var refusal = await _plans.DeleteDraftAsync(draft.Id).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            _errors = new Dictionary<string, string> { [string.Empty] = refusal };
            _isDeleting = false;
            RaiseAll();
            return;
        }

        // Straight back to the record. There is nothing left on this screen to look at,
        // and leaving an emptied builder open invites a save against a deleted plan.
        _navigator.ToPatientRecord(draft.PatientId);
    });

    private void BackToRecord()
    {
        if (_draft is { } draft && draft.PatientId != Guid.Empty)
        {
            _navigator.ToPatientRecord(draft.PatientId);
            return;
        }

        _navigator.ToPatientRecord(_args.PatientId);
    }

    /// <summary>The figures, which every edit to a line moves.</summary>
    private void RaiseTotals()
    {
        foreach (var name in new[]
        {
            nameof(Total), nameof(Benefit), nameof(Gap), nameof(HasBenefitEstimate),
            nameof(EstimatedBenefit),
        })
        {
            RaisePropertyChanged(name);
        }
    }

    /// <summary>
    /// Everything the screen reads. Listed explicitly rather than raising a blanket
    /// change, which would re-render the rationale box being typed in.
    /// </summary>
    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(NotFound), nameof(Draft), nameof(Options), nameof(Stage),
            nameof(IsEditing), nameof(IsNew), nameof(Title), nameof(Heading),
            nameof(SavedMessage), nameof(VisitCount), nameof(IsAdding),
            nameof(CodeSearch), nameof(CodeResults), nameof(PlanTitle),
            nameof(Rationale), nameof(EstimateValidUntil), nameof(IsRecommended),
            nameof(ProviderId), nameof(Errors), nameof(FormError), nameof(PricingNote),
            nameof(CanDelete), nameof(IsDeleting),
        })
        {
            RaisePropertyChanged(name);
        }

        RaiseTotals();
    }
}
