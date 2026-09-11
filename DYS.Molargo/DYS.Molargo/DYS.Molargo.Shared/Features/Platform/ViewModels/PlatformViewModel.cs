using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Shared.Features.Platform.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Platform.ViewModels;

/// <summary>
/// The vendor's tenant list: who subscribes, how much they hold, and who is suspended.
/// </summary>
public sealed class PlatformViewModel : BaseViewModel
{
    private readonly IPlatformService _platform;
    private readonly IPlanService _plans;
    private readonly ISessionService _session;

    private IReadOnlyList<TenantRow> _tenants = [];
    private Guid? _selectedId;
    private IReadOnlyList<string> _activity = [];
    private IReadOnlyList<Plan> _sellable = [];

    private Tenant? _editing;
    private bool _editingIsNew;
    private string? _lastAction;

    public PlatformViewModel(
        IPlatformService platform, IPlanService plans, ISessionService session)
    {
        _platform = platform;
        _plans = plans;
        _session = session;

        SelectTenantCommand = new MvxAsyncCommand<Guid>(SelectTenantAsync);
        NewTenantCommand = new MvxCommand(StartNewTenant);
        EditTenantCommand = new MvxAsyncCommand(EditSelectedAsync);
        CancelEditCommand = new MvxCommand(ClearEditor);
        SaveTenantCommand = new MvxAsyncCommand(SaveTenantAsync);
        SuspendTenantCommand = new MvxAsyncCommand(() => SetActiveAsync(false));
        RestoreTenantCommand = new MvxAsyncCommand(() => SetActiveAsync(true));
    }

    public IMvxAsyncCommand<Guid> SelectTenantCommand { get; }

    public IMvxCommand NewTenantCommand { get; }

    public IMvxAsyncCommand EditTenantCommand { get; }

    public IMvxCommand CancelEditCommand { get; }

    public IMvxAsyncCommand SaveTenantCommand { get; }

    public IMvxAsyncCommand SuspendTenantCommand { get; }

    public IMvxAsyncCommand RestoreTenantCommand { get; }

    public override Task Initialize() => LoadAsync();

    /// <summary>
    /// Whether the signed-in account holds the platform role.
    /// </summary>
    /// <remarks>
    /// For the screen's own message only. The refusal that matters is in the service,
    /// which re-reads the staff record and hands back nothing regardless of what this
    /// says.
    /// </remarks>
    public bool IsPermitted => _session.IsSuperAdmin;

    public string ActingAs => _session.UserDisplayName ?? "nobody";

    public string? LastAction => _lastAction;

    public IReadOnlyList<TenantRow> Tenants => _tenants;

    public int ActiveCount => _tenants.Count(row => row.IsActive);

    public int SuspendedCount => _tenants.Count(row => !row.IsActive);

    /// <summary>Clinics that signed up and put nothing in — trials to chase.</summary>
    public int EmptyCount => _tenants.Count(row => row.IsEmpty);

    public int OverSeatsCount => _tenants.Count(row => row.IsOverSeats);

    public int PatientsAcrossPlatform => _tenants.Sum(row => row.Patients);

    public bool IsSelected(Guid tenantId) => _selectedId == tenantId;

    public TenantRow? Selected =>
        _tenants.FirstOrDefault(row => row.TenantId == _selectedId);

    public IReadOnlyList<string> Activity => _activity;

    // ---- editor ----------------------------------------------------------

    public Tenant? Editing => _editing;

    public bool HasEditor => _editing is not null;

    public bool EditingIsNew => _editingIsNew;

    public string EditorTitle => _editing is null
        ? "Clinic"
        : _editingIsNew ? "New clinic" : _editing.Name;

    public string? TenantName
    {
        get => _editing?.Name;
        set
        {
            if (_editing is null) return;

            _editing.Name = value ?? string.Empty;
            RaisePropertyChanged();
        }
    }

    public string? TenantSlug
    {
        get => _editing?.Slug;
        set
        {
            if (_editing is null) return;

            _editing.Slug = value ?? string.Empty;
            RaisePropertyChanged();
        }
    }

    /// <summary>
    /// The code is fixed once a clinic is using it.
    /// </summary>
    /// <remarks>
    /// The field is shown read-only rather than hidden, because it is what their staff
    /// type at sign-in and the vendor is asked for it constantly. The service refuses a
    /// change to it too — this is only what stops somebody trying.
    /// </remarks>
    public bool SlugIsFixed => !_editingIsNew;

    public string? TenantAbn
    {
        get => _editing?.Abn;
        set
        {
            if (_editing is null) return;

            _editing.Abn = value;
            RaisePropertyChanged();
        }
    }

    public string? TenantContactEmail
    {
        get => _editing?.ContactEmail;
        set
        {
            if (_editing is null) return;

            _editing.ContactEmail = value;
            RaisePropertyChanged();
        }
    }

    public string? TenantContactPhone
    {
        get => _editing?.ContactPhone;
        set
        {
            if (_editing is null) return;

            _editing.ContactPhone = value;
            RaisePropertyChanged();
        }
    }

    /// <summary>
    /// The billing country, which decides which plans this clinic can be sold.
    /// </summary>
    /// <remarks>
    /// Changing it clears the plan. A clinic left on another country's plan would be
    /// invoiced in the wrong currency at the wrong price, and the service refuses that —
    /// clearing it here is what stops the refusal being the way the vendor finds out.
    /// </remarks>
    public string? TenantCountry
    {
        get => _editing?.CountryCode;
        set
        {
            if (_editing is null) return;

            var country = (value ?? string.Empty).ToUpperInvariant();

            if (!string.Equals(_editing.CountryCode, country, StringComparison.Ordinal))
            {
                _editing.PlanId = null;
            }

            _editing.CountryCode = country;

            RaisePropertyChanged();
            RaisePropertyChanged(nameof(TenantPlanId));
            RaisePropertyChanged(nameof(SellablePlans));
            RaisePropertyChanged(nameof(EditorQuote));

            _ = LoadSellablePlansAsync();
        }
    }

    public Guid? TenantPlanId
    {
        get => _editing?.PlanId;
        set
        {
            if (_editing is null) return;

            _editing.PlanId = value;

            RaisePropertyChanged();
            RaisePropertyChanged(nameof(EditorQuote));
        }
    }

    /// <summary>The plans on sale in the clinic's billing country.</summary>
    public IReadOnlyList<Plan> SellablePlans => _sellable;

    /// <summary>
    /// What the clinic would pay on the plan currently chosen in the form.
    /// </summary>
    /// <remarks>
    /// Priced from the clinic's real site and clinician counts, so the figure the vendor
    /// sees while choosing is the figure the list will show afterwards.
    /// </remarks>
    public PlanQuote? EditorQuote
    {
        get
        {
            if (_editing?.PlanId is not { } planId) return null;

            var plan = _sellable.FirstOrDefault(entry => entry.Id == planId);
            if (plan is null) return null;

            var row = _tenants.FirstOrDefault(entry => entry.TenantId == _editing.Id);

            return Pricing.Quote(plan, row?.Sites ?? 1, row?.Clinicians ?? 0);
        }
    }

    public DateOnly? TenantSubscribedOn
    {
        get => _editing?.SubscribedOn;
        set
        {
            if (_editing is null) return;

            _editing.SubscribedOn = value;
            RaisePropertyChanged();
        }
    }

    // ---- loading ---------------------------------------------------------

    private Task LoadAsync() => RunGuardedAsync(LoadListAsync);

    private async Task LoadListAsync()
    {
        _tenants = await _platform.GetTenantsAsync().ConfigureAwait(false);

        // Settles on one so the detail panel has something in it, and so the counts
        // beside it are not a panel of dashes on first open.
        _selectedId ??= _tenants.FirstOrDefault()?.TenantId;

        _activity = _selectedId is { } id
            ? await _platform.GetRecentActivityAsync(id).ConfigureAwait(false)
            : [];

        RaiseAll();
    }

    /// <summary>
    /// Selects a clinic and reloads the panel beside the list.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>RunGuardedAsync</c>. That returns silently while another
    /// operation is in flight, and one always is just after the screen first renders — so
    /// a click landing in that window selected nothing, with no error to explain it.
    /// </remarks>
    private async Task SelectTenantAsync(Guid tenantId)
    {
        _selectedId = tenantId;
        _editing = null;
        _lastAction = null;

        try
        {
            await LoadListAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;

            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
        }
    }

    private void StartNewTenant()
    {
        _editing = new Tenant { IsActive = true, CountryCode = "AU" };
        _editingIsNew = true;
        _lastAction = null;

        RaiseAll();

        _ = LoadSellablePlansAsync();
    }

    /// <summary>
    /// Loads the plans the clinic's billing country offers.
    /// </summary>
    /// <remarks>
    /// Its own read rather than part of the list load: the country can change inside the
    /// form, and the picker beside it has to follow without a round trip through the whole
    /// screen.
    /// </remarks>
    private async Task LoadSellablePlansAsync()
    {
        if (_editing?.CountryCode is not { Length: 2 } country)
        {
            _sellable = [];
            await RaisePropertyChanged(nameof(SellablePlans)).ConfigureAwait(false);
            return;
        }

        try
        {
            var sellable = await _plans
                .GetSellablePlansAsync(country)
                .ConfigureAwait(false);

            // The clinic's own plan is added back where it has been withdrawn from sale.
            //
            // Without this, editing a grandfathered clinic showed a picker with its plan
            // missing — the select rendered as nothing chosen, and saving would have
            // silently dropped the subscription. Withdrawing a plan is supposed to stop
            // new sales and change nothing for the clinics already on it; this is what
            // makes that true of the editor as well as the price.
            if (_editing?.PlanId is { } current
                && sellable.All(plan => plan.Id != current)
                && await _plans.GetPlanAsync(current).ConfigureAwait(false) is { } kept)
            {
                sellable = [.. sellable, kept];
            }

            _sellable = sellable;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;

            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
        }

        await RaisePropertyChanged(nameof(SellablePlans)).ConfigureAwait(false);
        await RaisePropertyChanged(nameof(EditorQuote)).ConfigureAwait(false);
    }

    /// <inheritdoc cref="SelectTenantAsync"/>
    private async Task EditSelectedAsync()
    {
        if (_selectedId is not { } tenantId) return;

        try
        {
            _editing = await _platform.GetTenantAsync(tenantId).ConfigureAwait(false);
            _editingIsNew = false;
            _lastAction = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        RaiseAll();

        await LoadSellablePlansAsync().ConfigureAwait(false);
        await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
    }

    private void ClearEditor()
    {
        _editing = null;
        _editingIsNew = false;

        RaiseAll();
    }

    private Task SaveTenantAsync() => RunGuardedAsync(async () =>
    {
        if (_editing is null) return;

        var refusal = await _platform.SaveTenantAsync(_editing).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = _editingIsNew
            ? $"{_editing.Name} added. Their staff sign in with the code \"{_editing.Slug}\"."
            : $"{_editing.Name} updated.";

        _selectedId = _editing.Id;
        _editing = null;
        _editingIsNew = false;

        await LoadListAsync().ConfigureAwait(false);
    });

    private Task SetActiveAsync(bool isActive) => RunGuardedAsync(async () =>
    {
        if (_selectedId is not { } tenantId) return;

        var refusal = await _platform
            .SetTenantActiveAsync(tenantId, isActive)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = isActive
            ? "Subscription restored. Their staff can sign in again."
            : "Subscription suspended. Every sign-in for that clinic is refused, and "
                + "nothing has been deleted.";

        _editing = null;

        await LoadListAsync().ConfigureAwait(false);
    });

    /// <summary>Everything the screen reads, named rather than raised as a blanket change.</summary>
    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(IsPermitted), nameof(ActingAs), nameof(LastAction), nameof(Tenants),
            nameof(ActiveCount), nameof(SuspendedCount), nameof(EmptyCount),
            nameof(OverSeatsCount), nameof(PatientsAcrossPlatform), nameof(Selected),
            nameof(Activity), nameof(Editing), nameof(HasEditor), nameof(EditingIsNew),
            nameof(EditorTitle), nameof(TenantName), nameof(TenantSlug),
            nameof(SlugIsFixed), nameof(TenantAbn), nameof(TenantContactEmail),
            nameof(TenantContactPhone), nameof(TenantCountry), nameof(TenantPlanId),
            nameof(SellablePlans), nameof(EditorQuote),
            nameof(TenantSubscribedOn),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
