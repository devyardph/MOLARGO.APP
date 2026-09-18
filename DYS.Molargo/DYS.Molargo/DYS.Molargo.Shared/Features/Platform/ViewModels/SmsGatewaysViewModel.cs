using DYS.Molargo.Domain;
using DYS.Molargo.Shared.Features.Platform.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Platform.ViewModels;

/// <summary>
/// The vendor's SMS providers, one per country.
/// </summary>
/// <remarks>
/// Its own screen rather than a tab on the clinic's notification settings, because the two
/// are owned by different people: a clinic sets up its own mail account, and only the
/// vendor can register a sender id with a carrier.
/// </remarks>
public sealed class SmsGatewaysViewModel : BaseViewModel
{
    private readonly ISmsGatewayService _gateways;
    private readonly ISessionService _session;

    private IReadOnlyList<SmsGatewayRow> _rows = [];
    private IReadOnlyList<string> _countries = [];
    private string? _lastAction;

    private Guid _editingId;
    private bool _isEditing;
    private string? _country;
    private string? _provider;
    private string? _apiUrl;
    private string? _senderId;
    private string? _price;
    private string? _currency;
    private string? _template;
    private string? _headers;
    private string? _contentType;
    private string? _apiKey;

    public SmsGatewaysViewModel(ISmsGatewayService gateways, ISessionService session)
    {
        _gateways = gateways;
        _session = session;

        NewGatewayCommand = new MvxCommand(NewGateway);
        EditGatewayCommand = new MvxCommand<Guid>(EditGateway);
        CloseCommand = new MvxCommand(Close);
        SaveCommand = new MvxAsyncCommand(SaveAsync);
        SetActiveCommand = new MvxAsyncCommand<Guid>(SetActiveAsync);
        DeleteCommand = new MvxAsyncCommand<Guid>(DeleteAsync);
    }

    public IMvxCommand NewGatewayCommand { get; }

    public IMvxCommand<Guid> EditGatewayCommand { get; }

    public IMvxCommand CloseCommand { get; }

    public IMvxAsyncCommand SaveCommand { get; }

    public IMvxAsyncCommand<Guid> SetActiveCommand { get; }

    public IMvxAsyncCommand<Guid> DeleteCommand { get; }

    public override Task Initialize() => LoadAsync();

    /// <summary>Vendor only, like every other screen under Platform.</summary>
    public bool IsPermitted => _session.IsSuperAdmin;

    public string ActingAs => _session.UserDisplayName ?? "nobody";

    public IReadOnlyList<SmsGatewayRow> Gateways => _rows;

    public string? LastAction => _lastAction;

    public int UsableCount => _rows.Count(row => row.IsUsable);

    /// <summary>The countries with no gateway, so the picker only offers what is missing.</summary>
    /// <remarks>
    /// Offered rather than typed, because a mistyped country is a gateway that never
    /// matches a clinic and gives no sign of why — the practice simply never receives a
    /// text. On an edit the row's own country stays in the list, or the box it is bound to
    /// would have no matching option and render as blank.
    /// </remarks>
    public IReadOnlyList<string> CountryOptions
    {
        get
        {
            var taken = _rows
                .Where(row => !string.Equals(row.CountryCode, _country, StringComparison.Ordinal))
                .Select(row => row.CountryCode)
                .ToHashSet(StringComparer.Ordinal);

            return _countries
                .Where(code => !taken.Contains(code))
                .ToList();
        }
    }

    public bool IsEditing => _isEditing;

    public bool IsNew => _isEditing && _editingId == Guid.Empty;

    public string? Country
    {
        get => _country;
        set
        {
            if (!SetProperty(ref _country, value)) return;

            // The list depends on it — see CountryOptions.
            RaisePropertyChanged(nameof(CountryOptions));
            RaisePropertyChanged(nameof(CanSave));
        }
    }

    public string? ProviderName
    {
        get => _provider;
        set { if (SetProperty(ref _provider, value)) RaisePropertyChanged(nameof(CanSave)); }
    }

    public string? ApiUrl
    {
        get => _apiUrl;
        set
        {
            if (!SetProperty(ref _apiUrl, value)) return;

            RaisePropertyChanged(nameof(CanSave));

            // The preview prints the URL it would post to, so it is stale the moment this
            // changes — and a preview showing the previous endpoint is worse than none.
            RaisePropertyChanged(nameof(Preview));
        }
    }

    public string? SenderId
    {
        get => _senderId;
        set { if (SetProperty(ref _senderId, value)) RaisePropertyChanged(nameof(Preview)); }
    }

    /// <summary>
    /// What a practice in this country is charged per message.
    /// </summary>
    /// <remarks>
    /// Text, parsed on save. Bound as a decimal it would show 0 in an empty box on a new
    /// gateway, and zero is a real price here — messages included in the plan — so the two
    /// have to be told apart. Blank is refused; typing 0 is how "included" is said.
    /// </remarks>
    public string? PricePerMessage
    {
        get => _price;
        set { if (SetProperty(ref _price, value)) RaisePropertyChanged(nameof(CanSave)); }
    }

    public string? CurrencyCode
    {
        get => _currency;
        set { if (SetProperty(ref _currency, value)) RaisePropertyChanged(nameof(CanSave)); }
    }

    /// <summary>The request body the provider expects, with tokens for what changes.</summary>
    public string? PayloadTemplate
    {
        get => _template;
        set
        {
            if (!SetProperty(ref _template, value)) return;

            RaisePropertyChanged(nameof(CanSave));
            RaisePropertyChanged(nameof(Preview));
        }
    }

    public string? Headers
    {
        get => _headers;
        set { if (SetProperty(ref _headers, value)) RaisePropertyChanged(nameof(Preview)); }
    }

    public string? ContentType
    {
        get => _contentType;
        set
        {
            if (!SetProperty(ref _contentType, value)) return;

            // The preview escapes by content type — quotes for JSON, percent-encoding for
            // a form — so switching this changes what the same template would post.
            RaisePropertyChanged(nameof(Preview));
        }
    }

    public IReadOnlyList<string> ContentTypeOptions { get; } = [SmsPayload.Json, SmsPayload.Form];

    /// <summary>
    /// Exactly what a send would post, rendered from the boxes above.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole point of letting the payload be typed: a template is only correct against
    /// a provider's documentation, and nobody can check it against the documentation while
    /// it is still full of tokens. Rendered with a sample number and message, so the
    /// escaping is visible too — a message with a quote in it is where a hand-written
    /// template usually breaks.
    /// </para>
    /// <para>
    /// The key is masked and is never even fetched. It is not returned by the service at
    /// all, so there is nothing here to leak — and a preview that showed the real
    /// credential would put it on screen every time somebody opened the panel to check a
    /// comma.
    /// </para>
    /// </remarks>
    public SmsRequest Preview => SmsPayload.Render(
        _apiUrl ?? string.Empty,
        _contentType ?? SmsPayload.Json,
        _headers,
        _template ?? string.Empty,
        SampleNumber,
        SampleMessage,
        _senderId,
        _apiKey ?? string.Empty,
        maskKey: true);

    /// <summary>
    /// The sample the preview is rendered against.
    /// </summary>
    /// <remarks>
    /// Carries an apostrophe and a quotation mark on purpose. They are what breaks a
    /// hand-written JSON template, and a sample of "Test message" would render cleanly for
    /// a template that falls apart on the first real reminder from a practice with a
    /// possessive in its name.
    /// </remarks>
    private const string SampleMessage =
        "Reminder: your appointment at O'Brien's is at 9:30 AM. Reply \"YES\" to confirm.";

    private const string SampleNumber = "+61400123456";

    /// <summary>The parsed price, or null while it is not a number.</summary>
    private decimal? ParsedPrice =>
        decimal.TryParse(_price, out var value) && value >= 0m ? value : null;

    /// <summary>
    /// The key being typed, never the one stored.
    /// </summary>
    /// <remarks>
    /// Starts empty on an edit and stays empty unless somebody types: the service reads
    /// blank as "leave the stored key alone". The screen says so, because a blank box over
    /// a configured gateway otherwise reads as "there is no key".
    /// </remarks>
    public string? ApiKey
    {
        get => _apiKey;
        set { if (SetProperty(ref _apiKey, value)) RaisePropertyChanged(nameof(CanSave)); }
    }

    /// <summary>True where the row being edited already holds a key.</summary>
    public bool HasStoredKey =>
        _editingId != Guid.Empty
        && _rows.FirstOrDefault(row => row.GatewayId == _editingId)?.HasKey == true;

    public bool CanSave =>
        !IsBusy
        && !string.IsNullOrWhiteSpace(_country)
        && !string.IsNullOrWhiteSpace(_provider)
        && !string.IsNullOrWhiteSpace(_apiUrl)
        && ParsedPrice is not null
        && !string.IsNullOrWhiteSpace(_currency)
        && !string.IsNullOrWhiteSpace(_template)
        && (HasStoredKey || !string.IsNullOrWhiteSpace(_apiKey));

    private Task LoadAsync() => RunGuardedAsync(ReloadAsync);

    /// <summary>
    /// Re-reads the list, without the busy guard.
    /// </summary>
    /// <remarks>
    /// Split from <see cref="LoadAsync"/> because <c>RunGuardedAsync</c> does not nest —
    /// it returns silently while another operation is in flight. Every command below is
    /// already inside one, so calling the guarded loader from them would skip the reload
    /// and leave the screen showing what it held before the save.
    /// </remarks>
    private async Task ReloadAsync()
    {
        _rows = await _gateways.GetAllAsync().ConfigureAwait(false);
        _countries = await _gateways.GetCountriesAsync().ConfigureAwait(false);

        await RaiseAll().ConfigureAwait(false);
    }

    private void NewGateway()
    {
        _isEditing = true;
        _editingId = Guid.Empty;
        _country = null;
        _provider = null;
        _apiUrl = null;
        _senderId = null;
        _price = null;
        _currency = null;

        // Prefilled rather than left blank. An empty payload box on a new gateway is a
        // blank page in a format nobody remembers, and the commonest shape is a better
        // starting point than nothing — see SmsPayload.DefaultTemplate.
        _template = SmsPayload.DefaultTemplate;
        _headers = SmsPayload.DefaultHeaders;
        _contentType = SmsPayload.Json;
        _apiKey = null;

        ErrorMessage = null;
        _ = RaiseAll();
    }

    private void EditGateway(Guid gatewayId)
    {
        var row = _rows.FirstOrDefault(entry => entry.GatewayId == gatewayId);
        if (row is null) return;

        _isEditing = true;
        _editingId = gatewayId;
        _country = row.CountryCode;
        _provider = row.ProviderName;
        _apiUrl = row.ApiUrl;
        _senderId = row.SenderId;

        // Four places, trimmed of trailing zeros, so a rate of 0.0450 reads as 0.045 rather
        // than being re-typed by anybody who thinks the zeros are significant.
        _price = row.PricePerMessage.ToString("0.####");
        _currency = row.CurrencyCode;

        // Defaulted where a row predates the payload columns. Those rows were saved with an
        // empty template by the migration, and an empty box would read as "this provider
        // wants no body" rather than "nobody has filled this in yet".
        _template = row.PayloadTemplate is { Length: > 0 }
            ? row.PayloadTemplate
            : SmsPayload.DefaultTemplate;

        _headers = row.Headers is { Length: > 0 } ? row.Headers : SmsPayload.DefaultHeaders;
        _contentType = row.ContentType is { Length: > 0 } ? row.ContentType : SmsPayload.Json;

        // Never prefilled. The service does not return it, and a box showing a credential
        // is a credential on screen for as long as the panel is open.
        _apiKey = null;

        ErrorMessage = null;
        _ = RaiseAll();
    }

    private void Close()
    {
        _isEditing = false;
        _editingId = Guid.Empty;
        _apiKey = null;

        ErrorMessage = null;
        _ = RaiseAll();
    }

    private Task SaveAsync() => RunGuardedAsync(async () =>
    {
        var refusal = await _gateways
            .SaveAsync(_editingId, _country ?? string.Empty, _provider ?? string.Empty,
                _apiUrl ?? string.Empty, _senderId, ParsedPrice ?? 0m,
                _currency ?? string.Empty, _template ?? string.Empty, _headers,
                _contentType ?? SmsPayload.Json, _apiKey)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = $"{_country} gateway saved.";
        _isEditing = false;
        _editingId = Guid.Empty;
        _apiKey = null;

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
            ? $"{row.CountryCode} switched off — no practice in that country can send."
            : $"{row.CountryCode} switched on.";

        await ReloadAsync().ConfigureAwait(false);
    });

    private Task DeleteAsync(Guid gatewayId) => RunGuardedAsync(async () =>
    {
        var row = _rows.FirstOrDefault(entry => entry.GatewayId == gatewayId);
        if (row is null) return;

        var refusal = await _gateways.DeleteAsync(gatewayId).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = $"{row.CountryCode} gateway removed, key and all.";

        if (_editingId == gatewayId) Close();

        await ReloadAsync().ConfigureAwait(false);
    });

    private async Task RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(IsPermitted), nameof(ActingAs), nameof(Gateways), nameof(LastAction),
            nameof(UsableCount), nameof(CountryOptions), nameof(IsEditing), nameof(IsNew),
            nameof(Country), nameof(ProviderName), nameof(ApiUrl), nameof(SenderId),
            nameof(PricePerMessage), nameof(CurrencyCode), nameof(PayloadTemplate),
            nameof(Headers), nameof(ContentType), nameof(Preview),
            nameof(ApiKey), nameof(HasStoredKey), nameof(CanSave), nameof(HasError),
        })
        {
            await RaisePropertyChanged(name).ConfigureAwait(false);
        }
    }
}
