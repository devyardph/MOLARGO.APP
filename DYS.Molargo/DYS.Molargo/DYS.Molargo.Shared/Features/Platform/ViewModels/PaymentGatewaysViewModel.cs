using System.Globalization;
using DYS.Molargo.Domain;
using DYS.Molargo.Services.Features.Platform;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Platform.ViewModels;

/// <summary>
/// The vendor's payment providers, one per country.
/// </summary>
/// <remarks>
/// <para>
/// Beside the SMS gateways and for the same reason: who will take money in a country is a
/// per-country arrangement the vendor makes, not something a practice can set up for
/// itself. Stripe does not onboard Philippine businesses at all, and a provider that does
/// will not be the right one for Australia.
/// </para>
/// <para>
/// Neither secret is ever read back. The screen reports whether each is stored and nothing
/// more, so leaving both blank on an edit changes the fee without needing a key nobody can
/// see.
/// </para>
/// </remarks>
public sealed class PaymentGatewaysViewModel : BaseViewModel
{
    private readonly IPaymentGatewayService _gateways;
    private readonly ISessionService _session;

    private IReadOnlyList<PaymentGatewayRow> _rows = [];
    private IReadOnlyList<PaymentCountryGap> _gaps = [];
    private IReadOnlyList<string> _providers = [];
    private IReadOnlyList<string> _countries = [];
    private bool _currencyChosen;
    private string? _lastAction;

    private bool _isEditing;
    private Guid _editingId;
    private string? _country;
    private string? _provider;
    private string? _apiUrl;
    private string? _currency;
    private string? _percentageFee;
    private string? _fixedFee;
    private bool _isLive;
    private string? _secretKey;
    private string? _webhookSecret;
    private bool _hasSecretKey;
    private bool _hasWebhookSecret;

    public PaymentGatewaysViewModel(
        IPaymentGatewayService gateways, ISessionService session)
    {
        _gateways = gateways;
        _session = session;

        NewGatewayCommand = new MvxCommand(NewGateway);
        EditGatewayCommand = new MvxCommand<Guid>(EditGateway);
        CloseCommand = new MvxCommand(Close);
        SetProviderCommand = new MvxCommand<string>(SetProvider);
        ToggleLiveCommand = new MvxCommand(ToggleLive);
        SaveCommand = new MvxAsyncCommand(SaveAsync);
        SetActiveCommand = new MvxAsyncCommand<Guid>(SetActiveAsync);
        DeleteCommand = new MvxAsyncCommand<Guid>(DeleteAsync);
    }

    public IMvxCommand NewGatewayCommand { get; }

    public IMvxCommand<Guid> EditGatewayCommand { get; }

    public IMvxCommand CloseCommand { get; }

    public IMvxCommand<string> SetProviderCommand { get; }

    public IMvxCommand ToggleLiveCommand { get; }

    public IMvxAsyncCommand SaveCommand { get; }

    public IMvxAsyncCommand<Guid> SetActiveCommand { get; }

    public IMvxAsyncCommand<Guid> DeleteCommand { get; }

    public override Task Initialize() => LoadAsync();

    public IReadOnlyList<PaymentGatewayRow> Rows => _rows;

    /// <summary>Countries with practices on the platform and nowhere for them to pay.</summary>
    public IReadOnlyList<PaymentCountryGap> Gaps => _gaps;

    public IReadOnlyList<string> Providers => _providers;

    /// <summary>The currencies this machine knows, plus whichever is already stored.</summary>
    /// <remarks>
    /// OptionsFor rather than the plain list: the codes come from this machine's own ICU
    /// data, and a select with no matching option renders blank and saves whatever was
    /// showing. See PracticeCurrency.
    /// </remarks>
    public IReadOnlyList<CurrencyOption> Currencies => PracticeCurrency.OptionsFor(_currency);

    public string? LastAction => _lastAction;

    public bool IsEditing => _isEditing;

    public bool IsNew => _editingId == Guid.Empty;

    /// <summary>
    /// The row the open form belongs to.
    /// </summary>
    /// <remarks>
    /// Exposed rather than matched back from the country on the form. It was found by
    /// country once, which quietly broke the moment somebody opened a gateway and changed
    /// the country before pressing Remove: the lookup then matched nothing, and Remove
    /// did nothing with no explanation.
    /// </remarks>
    public Guid EditingId => _editingId;

    /// <summary>Whether the row being edited is switched on.</summary>
    public bool EditingIsActive =>
        _rows.FirstOrDefault(row => row.GatewayId == _editingId)?.IsActive ?? true;

    public string FormTitle => IsNew ? "New payment gateway" : $"{_country} gateway";

    /// <summary>Whether this account may see the screen at all.</summary>
    public bool IsPermitted => _session.IsSuperAdmin;

    public string ActingAs => _session.UserDisplayName ?? "nobody";

    /// <summary>
    /// The countries with no gateway, so the picker only offers what is missing.
    /// </summary>
    /// <remarks>
    /// On an edit the row's own country stays in the list, or the select it is bound to
    /// would have no matching option, render blank, and save the blank.
    /// </remarks>
    public IReadOnlyList<string> CountryOptions
    {
        get
        {
            var taken = _rows
                .Where(row => !string.Equals(row.CountryCode, _country, StringComparison.Ordinal))
                .Select(row => row.CountryCode)
                .ToHashSet(StringComparer.Ordinal);

            return _countries.Where(code => !taken.Contains(code)).ToList();
        }
    }

    public string? Country
    {
        get => _country;
        set
        {
            if (!SetProperty(ref _country, value?.ToUpperInvariant())) return;

            // The country's usual currency, offered until somebody picks one. Nearly
            // always right, and the one that is wrong is visible on the form rather than
            // found later by a charge refused for a currency mismatch.
            if (!_currencyChosen && _country is { Length: 2 })
            {
                _currency = PracticeCurrency.ForCountry(_country);
                RaisePropertyChanged(nameof(Currency));
                RaisePropertyChanged(nameof(Currencies));
            }

            // The list depends on it — see CountryOptions.
            RaisePropertyChanged(nameof(CountryOptions));
        }
    }

    public string? Provider => _provider;

    public string? ApiUrl
    {
        get => _apiUrl;
        set => SetProperty(ref _apiUrl, value);
    }

    public string? Currency
    {
        get => _currency;
        set
        {
            if (!SetProperty(ref _currency, value)) return;

            // Chosen by hand from here on, so changing the country no longer overwrites it.
            _currencyChosen = true;
        }
    }

    /// <summary>
    /// The provider's cut, typed as a percentage and stored as a fraction.
    /// </summary>
    /// <remarks>
    /// People say "3.5%", so that is what the box takes. Converting here rather than asking
    /// for 0.035 is the difference between a typo that looks wrong and one that silently
    /// records a provider keeping three hundred and fifty per cent.
    /// </remarks>
    public string? PercentageFee
    {
        get => _percentageFee;
        set => SetProperty(ref _percentageFee, value);
    }

    public string? FixedFee
    {
        get => _fixedFee;
        set => SetProperty(ref _fixedFee, value);
    }

    public bool IsLive => _isLive;

    public string? SecretKey
    {
        get => _secretKey;
        set => SetProperty(ref _secretKey, value);
    }

    public string? WebhookSecret
    {
        get => _webhookSecret;
        set => SetProperty(ref _webhookSecret, value);
    }

    public bool HasSecretKey => _hasSecretKey;

    public bool HasWebhookSecret => _hasWebhookSecret;

    /// <summary>
    /// The path to give the provider, for the country being edited.
    /// </summary>
    /// <remarks>
    /// A path and not a URL, because this app does not know its own public address — it is
    /// whatever the deployment put in front of it. Shown anyway: the country in the path is
    /// the part somebody gets wrong, and a webhook pointed at the wrong country fails every
    /// signature check and reads as nobody paying.
    /// </remarks>
    public string WebhookPath =>
        $"/webhooks/payments/{(string.IsNullOrWhiteSpace(_country) ? "{country}" : _country)}";

    public bool HasRows => _rows.Count > 0;

    /// <summary>
    /// How many gateways could actually take a payment.
    /// </summary>
    /// <remarks>
    /// Both secrets, not just the key. A gateway with an API key and no signing secret
    /// makes links and never hears that any of them were paid, which is the failure this
    /// count exists to keep off the "all fine" side of the ledger.
    /// </remarks>
    public int UsableCount =>
        _rows.Count(row => row.IsActive && row.HasSecretKey && row.HasWebhookSecret);

    public string EmptyMessage =>
        "No payment gateway is set up for any country, so no practice can pay online. "
        + "Charges can still be raised and recorded as paid by hand.";

    // ---- editing ---------------------------------------------------------

    private void NewGateway()
    {
        _isEditing = true;
        _editingId = Guid.Empty;
        _country = null;
        _currencyChosen = false;
        _provider = _providers.Count == 1 ? _providers[0] : null;

        // PayMongo's documented base, offered rather than blank. A URL typed from memory
        // is the field most likely to be wrong, and this one is the same for every country
        // the provider serves.
        _apiUrl = "https://api.paymongo.com/v1";
        _currency = null;
        _percentageFee = null;
        _fixedFee = null;
        _isLive = false;
        _secretKey = null;
        _webhookSecret = null;
        _hasSecretKey = false;
        _hasWebhookSecret = false;
        _lastAction = null;

        RaiseForm();
    }

    private void EditGateway(Guid gatewayId)
    {
        if (_rows.FirstOrDefault(row => row.GatewayId == gatewayId) is not { } row) return;

        _isEditing = true;
        _editingId = row.GatewayId;
        _country = row.CountryCode;

        // Already chosen, by whoever saved the row. Without this, opening a gateway would
        // reset its currency to the country's usual one the moment anything touched the
        // country field.
        _currencyChosen = true;
        _provider = row.ProviderName;
        _apiUrl = row.ApiUrl;
        _currency = row.CurrencyCode;

        // Back out to a percentage, because that is how it went in.
        _percentageFee = (row.PercentageFee * 100m).ToString("0.####", CultureInfo.InvariantCulture);
        _fixedFee = row.FixedFee.ToString("0.00", CultureInfo.InvariantCulture);
        _isLive = row.IsLive;

        // Both blank. They are write-only, and prefilling them with anything — even a mask
        // — invites somebody to save the mask as the key.
        _secretKey = null;
        _webhookSecret = null;
        _hasSecretKey = row.HasSecretKey;
        _hasWebhookSecret = row.HasWebhookSecret;
        _lastAction = null;

        RaiseForm();
    }

    private void Close()
    {
        _isEditing = false;
        _secretKey = null;
        _webhookSecret = null;

        RaiseForm();
    }

    private void SetProvider(string? provider)
    {
        _provider = provider;

        RaisePropertyChanged(nameof(Provider));
    }

    private void ToggleLive()
    {
        _isLive = !_isLive;

        RaisePropertyChanged(nameof(IsLive));
    }

    private Task SaveAsync() => RunGuardedAsync(async () =>
    {
        if (!decimal.TryParse(
            _percentageFee, NumberStyles.Number, CultureInfo.InvariantCulture, out var percent))
        {
            ErrorMessage = "The percentage fee has to be a number — 3.5 for 3.5%.";
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        if (!decimal.TryParse(
            _fixedFee, NumberStyles.Number, CultureInfo.InvariantCulture, out var fixedFee))
        {
            // Blank means none, which is a real answer — some providers charge a
            // percentage only.
            if (!string.IsNullOrWhiteSpace(_fixedFee))
            {
                ErrorMessage = "The fixed fee has to be a number, or blank for none.";
                await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
                return;
            }

            fixedFee = 0m;
        }

        var refusal = await _gateways
            .SaveAsync(
                _editingId,
                _country ?? string.Empty,
                _provider ?? string.Empty,
                _apiUrl ?? string.Empty,
                _currency ?? string.Empty,
                percent / 100m,
                fixedFee,
                _isLive,
                _secretKey,
                _webhookSecret)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = $"{_country} saved. Point {_provider}'s webhook at {WebhookPath} on "
            + "this server's public address, or nothing will ever be marked paid.";

        _isEditing = false;
        _secretKey = null;
        _webhookSecret = null;

        await ReloadAsync().ConfigureAwait(false);
    });

    private Task SetActiveAsync(Guid gatewayId) => RunGuardedAsync(async () =>
    {
        var row = _rows.FirstOrDefault(entry => entry.GatewayId == gatewayId);

        if (row is null) return;

        var refusal = await _gateways
            .SetActiveAsync(gatewayId, !row.IsActive)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = row.IsActive
            ? $"{row.CountryCode} switched off. Its practices cannot pay online until it "
                + "is switched back on."
            : $"{row.CountryCode} switched on.";

        await ReloadAsync().ConfigureAwait(false);
    });

    private Task DeleteAsync(Guid gatewayId) => RunGuardedAsync(async () =>
    {
        var refusal = await _gateways.DeleteAsync(gatewayId).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = "Gateway removed.";

        await ReloadAsync().ConfigureAwait(false);
    });

    // ---- loading ---------------------------------------------------------

    private Task LoadAsync() => RunGuardedAsync(ReloadAsync);

    private async Task ReloadAsync()
    {
        _rows = await _gateways.GetAllAsync().ConfigureAwait(false);
        _gaps = await _gateways.GetGapsAsync().ConfigureAwait(false);
        _providers = await _gateways.GetProvidersAsync().ConfigureAwait(false);
        _countries = await _gateways.GetCountriesAsync().ConfigureAwait(false);

        foreach (var name in new[]
        {
            nameof(Rows), nameof(Gaps), nameof(Providers), nameof(HasRows),
            nameof(LastAction), nameof(IsEditing), nameof(IsPermitted),
            nameof(ActingAs), nameof(CountryOptions), nameof(UsableCount),
        })
        {
            await RaisePropertyChanged(name).ConfigureAwait(false);
        }
    }

    private void RaiseForm()
    {
        foreach (var name in new[]
        {
            nameof(IsEditing), nameof(IsNew), nameof(FormTitle), nameof(Country),
            nameof(Provider), nameof(ApiUrl), nameof(Currency), nameof(PercentageFee),
            nameof(FixedFee), nameof(IsLive), nameof(SecretKey), nameof(WebhookSecret),
            nameof(HasSecretKey), nameof(HasWebhookSecret), nameof(WebhookPath),
            nameof(LastAction), nameof(EditingId), nameof(EditingIsActive),
            nameof(CountryOptions), nameof(Currencies),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
