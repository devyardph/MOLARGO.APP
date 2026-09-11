using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Shared.Features.Platform.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Platform.ViewModels;

/// <summary>
/// The vendor's price list: the plans on sale, per country.
/// </summary>
public sealed class PlansViewModel : BaseViewModel
{
    /// <summary>
    /// The example the quote preview prices, unless the vendor changes it.
    /// </summary>
    /// <remarks>
    /// Two sites and five clinicians, because that is the shape where every line of the
    /// pricing rule contributes something: it exceeds a single-site plan's sites and a
    /// four-seat allowance at once. One site and one clinician would price every plan at
    /// its base and prove nothing.
    /// </remarks>
    private const int DefaultPreviewSites = 2;

    private const int DefaultPreviewClinicians = 5;

    private readonly IPlanService _plans;
    private readonly ISessionService _session;

    private IReadOnlyList<PlanRow> _rows = [];
    private IReadOnlyList<string> _countries = [];
    private string? _country;
    private string? _lastAction;

    private Plan? _editing;
    private bool _editingIsNew;

    private int _previewSites = DefaultPreviewSites;
    private int _previewClinicians = DefaultPreviewClinicians;

    public PlansViewModel(IPlanService plans, ISessionService session)
    {
        _plans = plans;
        _session = session;

        SelectCountryCommand = new MvxAsyncCommand<string>(SelectCountryAsync);
        NewPlanCommand = new MvxCommand(StartNew);
        EditPlanCommand = new MvxAsyncCommand<Guid>(OpenAsync);
        CancelEditCommand = new MvxCommand(ClosePanel);
        SavePlanCommand = new MvxAsyncCommand(SaveAsync);
        WithdrawPlanCommand = new MvxAsyncCommand<Guid>(id => SetActiveAsync(id, false));
        RestorePlanCommand = new MvxAsyncCommand<Guid>(id => SetActiveAsync(id, true));
    }

    public IMvxAsyncCommand<string> SelectCountryCommand { get; }

    public IMvxCommand NewPlanCommand { get; }

    public IMvxAsyncCommand<Guid> EditPlanCommand { get; }

    public IMvxCommand CancelEditCommand { get; }

    public IMvxAsyncCommand SavePlanCommand { get; }

    public IMvxAsyncCommand<Guid> WithdrawPlanCommand { get; }

    public IMvxAsyncCommand<Guid> RestorePlanCommand { get; }

    public override Task Initialize() => LoadAsync();

    public bool IsPermitted => _session.IsSuperAdmin;

    public string ActingAs => _session.UserDisplayName ?? "nobody";

    public string? LastAction => _lastAction;

    public IReadOnlyList<string> Countries => _countries;

    public string? Country => _country;

    public bool IsCountry(string code) =>
        string.Equals(_country, code, StringComparison.OrdinalIgnoreCase);

    /// <summary>The plans for the chosen country, on sale first.</summary>
    public IReadOnlyList<PlanRow> Plans => _rows
        .Where(row => string.Equals(row.CountryCode, _country, StringComparison.OrdinalIgnoreCase))
        .ToList();

    public int CountryCount => _countries.Count;

    public int OnSaleCount => Plans.Count(row => row.IsActive);

    public int SubscriberCount => Plans.Sum(row => row.Subscribers);

    /// <summary>The currency the chosen country's plans are priced in.</summary>
    /// <remarks>
    /// Read from the plans rather than from a country-to-currency table, because the plan
    /// is what decides it. A country whose plans disagreed would be a mistake worth seeing
    /// rather than one to paper over with a lookup.
    /// </remarks>
    public string? Currency => Plans.Select(row => row.CurrencyCode).FirstOrDefault();

    public bool IsSelected(Guid planId) => _editing?.Id == planId;

    // ---- the quote preview ------------------------------------------------

    public int PreviewSites
    {
        get => _previewSites;
        set
        {
            SetProperty(ref _previewSites, Math.Clamp(value, 1, 50));
            RaisePropertyChanged(nameof(Previews));
            RaisePropertyChanged(nameof(EditorPreview));
        }
    }

    public int PreviewClinicians
    {
        get => _previewClinicians;
        set
        {
            SetProperty(ref _previewClinicians, Math.Clamp(value, 0, 200));
            RaisePropertyChanged(nameof(Previews));
            RaisePropertyChanged(nameof(EditorPreview));
        }
    }

    /// <summary>
    /// What each plan in this country would charge the example practice.
    /// </summary>
    /// <remarks>
    /// The whole reason the pricing rule lives in Domain: this is the same
    /// <c>Pricing.Quote</c> the clinic list bills with, so a plan cannot look one price
    /// here and charge another there.
    /// </remarks>
    public IReadOnlyList<(PlanRow Row, PlanQuote Quote)> Previews => Plans
        .Select(row => (row, Pricing.Quote(ToPlan(row), _previewSites, _previewClinicians)))
        .ToList();

    /// <summary>The same preview for the plan being edited, quoted as the vendor types.</summary>
    public PlanQuote? EditorPreview => _editing is null
        ? null
        : Pricing.Quote(_editing, _previewSites, _previewClinicians);

    // ---- editor ----------------------------------------------------------

    public Plan? Editing => _editing;

    public bool HasEditor => _editing is not null;

    public bool EditingIsNew => _editingIsNew;

    public string EditorTitle => _editing is null
        ? "Plan"
        : _editingIsNew ? "New plan" : $"{_editing.Name} — {_editing.CountryCode}";

    /// <summary>
    /// Whether clinics are already on this plan, so a price change is not free.
    /// </summary>
    /// <remarks>
    /// Shown while editing rather than refused. Changing a live price is a legitimate
    /// thing to do — it just needs to be a decision, and the count is what makes it one.
    /// </remarks>
    public int EditingSubscribers => _editing is null
        ? 0
        : _rows.FirstOrDefault(row => row.PlanId == _editing.Id)?.Subscribers ?? 0;

    public string? PlanName
    {
        get => _editing?.Name;
        set
        {
            if (_editing is null) return;

            _editing.Name = value ?? string.Empty;
            RaisePropertyChanged();
        }
    }

    public string? PlanCode
    {
        get => _editing?.Code;
        set
        {
            if (_editing is null) return;

            _editing.Code = value ?? string.Empty;
            RaisePropertyChanged();
        }
    }

    public string? PlanCountry
    {
        get => _editing?.CountryCode;
        set
        {
            if (_editing is null) return;

            _editing.CountryCode = value ?? string.Empty;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(EditorTitle));
        }
    }

    public string? PlanCurrency
    {
        get => _editing?.CurrencyCode;
        set
        {
            if (_editing is null) return;

            _editing.CurrencyCode = value ?? string.Empty;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(EditorPreview));
        }
    }

    public decimal PlanMonthlyBase
    {
        get => _editing?.MonthlyBase ?? 0m;
        set
        {
            if (_editing is null) return;

            _editing.MonthlyBase = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(EditorPreview));
        }
    }

    public int PlanIncludedSites
    {
        get => _editing?.IncludedSites ?? 1;
        set
        {
            if (_editing is null) return;

            _editing.IncludedSites = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(EditorPreview));
        }
    }

    public int PlanIncludedSeatsPerSite
    {
        get => _editing?.IncludedSeatsPerSite ?? 0;
        set
        {
            if (_editing is null) return;

            _editing.IncludedSeatsPerSite = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(EditorPreview));
        }
    }

    public decimal PlanPricePerExtraSite
    {
        get => _editing?.PricePerExtraSite ?? 0m;
        set
        {
            if (_editing is null) return;

            _editing.PricePerExtraSite = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(EditorPreview));
        }
    }

    public decimal PlanPricePerExtraSeat
    {
        get => _editing?.PricePerExtraSeat ?? 0m;
        set
        {
            if (_editing is null) return;

            _editing.PricePerExtraSeat = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(EditorPreview));
        }
    }

    public int? PlanAnnualMonths
    {
        get => _editing?.AnnualMonthsCharged;
        set
        {
            if (_editing is null) return;

            _editing.AnnualMonthsCharged = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(EditorPreview));
        }
    }

    // ---- loading ---------------------------------------------------------

    private Task LoadAsync() => RunGuardedAsync(LoadListAsync);

    private async Task LoadListAsync()
    {
        _rows = await _plans.GetPlansAsync().ConfigureAwait(false);
        _countries = await _plans.GetCountriesAsync().ConfigureAwait(false);

        // The vendor's own country first where it has plans, otherwise whatever exists.
        _country ??= _countries.FirstOrDefault();

        RaiseAll();
    }

    /// <inheritdoc cref="OpenAsync"/>
    private async Task SelectCountryAsync(string? code)
    {
        _country = code;
        _editing = null;
        _lastAction = null;

        RaiseAll();

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private void StartNew()
    {
        _editing = new Plan
        {
            CountryCode = _country ?? "AU",
            CurrencyCode = Currency ?? "AUD",
            IncludedSites = 1,
            IncludedSeatsPerSite = 1,
            AnnualMonthsCharged = 10,
            IsActive = true,
        };

        _editingIsNew = true;
        _lastAction = null;

        RaiseAll();
    }

    /// <summary>
    /// Opens a plan in the editor.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>RunGuardedAsync</c>: that returns silently while another
    /// operation is in flight, and one always is just after the screen first renders — so
    /// an early click did nothing at all, with no error to explain it.
    /// </remarks>
    private async Task OpenAsync(Guid planId)
    {
        try
        {
            _editing = await _plans.GetPlanAsync(planId).ConfigureAwait(false);
            _editingIsNew = false;
            _lastAction = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        RaiseAll();

        await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
    }

    private void ClosePanel()
    {
        _editing = null;
        _editingIsNew = false;

        RaiseAll();
    }

    private Task SaveAsync() => RunGuardedAsync(async () =>
    {
        if (_editing is null) return;

        var refusal = await _plans.SavePlanAsync(_editing).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = _editingIsNew
            ? $"\"{_editing.Name}\" added for {_editing.CountryCode}."
            : $"\"{_editing.Name}\" updated.";

        // Follow the plan to its country, so saving a GB plan while looking at AU does not
        // appear to have done nothing.
        _country = _editing.CountryCode;
        _editing = null;
        _editingIsNew = false;

        await LoadListAsync().ConfigureAwait(false);
    });

    private Task SetActiveAsync(Guid planId, bool isActive) => RunGuardedAsync(async () =>
    {
        var refusal = await _plans.SetPlanActiveAsync(planId, isActive).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = isActive
            ? "Plan back on sale."
            : "Plan withdrawn. Clinics already on it keep it, and keep its price.";

        _editing = null;

        await LoadListAsync().ConfigureAwait(false);
    });

    /// <summary>
    /// A row back into the entity the pricing rule takes.
    /// </summary>
    /// <remarks>
    /// The row carries every field the quote needs, so this is a shape change rather than
    /// a second read. Keeps <c>Pricing.Quote</c> taking a <c>Plan</c> — one signature,
    /// used by this screen, the clinic list and eventually an invoice.
    /// </remarks>
    private static Plan ToPlan(PlanRow row) => new()
    {
        Id = row.PlanId,
        Name = row.Name,
        Code = row.Code,
        CountryCode = row.CountryCode,
        CurrencyCode = row.CurrencyCode,
        MonthlyBase = row.MonthlyBase,
        IncludedSites = row.IncludedSites,
        IncludedSeatsPerSite = row.IncludedSeatsPerSite,
        PricePerExtraSite = row.PricePerExtraSite,
        PricePerExtraSeat = row.PricePerExtraSeat,
        AnnualMonthsCharged = row.AnnualMonthsCharged,
        IsActive = row.IsActive,
    };

    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(IsPermitted), nameof(ActingAs), nameof(LastAction), nameof(Countries),
            nameof(Country), nameof(Plans), nameof(CountryCount), nameof(OnSaleCount),
            nameof(SubscriberCount), nameof(Currency), nameof(Previews),
            nameof(Editing), nameof(HasEditor), nameof(EditingIsNew),
            nameof(EditorTitle), nameof(EditingSubscribers), nameof(EditorPreview),
            nameof(PlanName), nameof(PlanCode), nameof(PlanCountry), nameof(PlanCurrency),
            nameof(PlanMonthlyBase), nameof(PlanIncludedSites),
            nameof(PlanIncludedSeatsPerSite), nameof(PlanPricePerExtraSite),
            nameof(PlanPricePerExtraSeat), nameof(PlanAnnualMonths),
            nameof(PreviewSites), nameof(PreviewClinicians),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
