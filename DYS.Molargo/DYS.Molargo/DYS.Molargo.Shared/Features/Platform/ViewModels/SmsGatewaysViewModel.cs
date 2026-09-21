using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Enums;
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
    private bool _currencyChosen;
    private string? _credit;
    private IReadOnlyList<SmsCountryUsage> _usage = [];
    private string? _template;
    private string? _headers;
    private string? _contentType;
    private SmsAuthType _authType;
    private string? _authHeaderName;
    private string? _authUsername;
    private bool _base64Key;
    private string? _apiKey;
    private string? _testNumber;
    private SmsResult? _testResult;

    public SmsGatewaysViewModel(ISmsGatewayService gateways, ISessionService session)
    {
        _gateways = gateways;
        _session = session;

        NewGatewayCommand = new MvxCommand(NewGateway);
        SetAuthTypeCommand = new MvxCommand<SmsAuthType>(SetAuthType);
        EditGatewayCommand = new MvxCommand<Guid>(EditGateway);
        CloseCommand = new MvxCommand(Close);
        SaveCommand = new MvxAsyncCommand(SaveAsync);
        TestCommand = new MvxAsyncCommand(TestAsync);
        SetActiveCommand = new MvxAsyncCommand<Guid>(SetActiveAsync);
        DeleteCommand = new MvxAsyncCommand<Guid>(DeleteAsync);
    }

    public IMvxCommand NewGatewayCommand { get; }

    public IMvxCommand<SmsAuthType> SetAuthTypeCommand { get; }

    public IMvxCommand<Guid> EditGatewayCommand { get; }

    public IMvxCommand CloseCommand { get; }

    public IMvxAsyncCommand SaveCommand { get; }

    public IMvxAsyncCommand TestCommand { get; }

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

            // The country's own currency, until somebody picks one. A gateway for the
            // Philippines priced in AUD is a mistake nobody would make deliberately and one
            // the old free-text box made easy — and the country is chosen first, so this is
            // filled in before anybody looks at it.
            if (!_currencyChosen && value is { Length: 2 })
            {
                _currency = PracticeCurrency.ForCountry(value);
                RaisePropertyChanged(nameof(CurrencyCode));
                RaisePropertyChanged(nameof(CurrencyOptions));
            }

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

    /// <summary>What the currency select offers.</summary>
    /// <remarks>
    /// Built around the stored code, so a gateway priced in a currency this machine's ICU
    /// data does not carry keeps it — see <see cref="PracticeCurrency.OptionsFor"/>.
    /// </remarks>
    public IReadOnlyList<CurrencyOption> CurrencyOptions =>
        PracticeCurrency.OptionsFor(_currency);

    public string? CurrencyCode
    {
        get => _currency;
        set
        {
            if (!SetProperty(ref _currency, value)) return;

            // Chosen from the list from here on. The country stops filling it in, because
            // overwriting a deliberate pick the next time somebody corrects a typo in the
            // country is the kind of change nobody notices until a practice is invoiced in
            // the wrong money.
            _currencyChosen = true;

            RaisePropertyChanged(nameof(CurrencyOptions));
            RaisePropertyChanged(nameof(CanSave));
        }
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
        set
        {
            if (SetProperty(ref _headers, value)) RaisePropertyChanged(nameof(Preview));
        }
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

    /// <summary>Which authentication shape the provider wants.</summary>
    public SmsAuthType AuthType => _authType;

    public bool IsApiKeyAuth => _authType == SmsAuthType.ApiKey;

    public bool IsBasicAuth => _authType == SmsAuthType.BasicAuth;

    public IReadOnlyList<SmsAuthType> AuthTypeOptions { get; } =
        [SmsAuthType.ApiKey, SmsAuthType.BasicAuth];

    /// <summary>The label for one option, so the screen holds no copy of the list.</summary>
    public static string AuthTypeLabel(SmsAuthType type) => type switch
    {
        SmsAuthType.BasicAuth => "Basic auth",
        _ => "API key",
    };

    /// <summary>
    /// The header an API key is sent in.
    /// </summary>
    /// <remarks>
    /// Typed rather than picked. <c>X-API-Key</c> and <c>Authorization</c> cover most of
    /// them and neither covers the vendor whose header is its own name, which is the same
    /// reason the payload is a template.
    /// </remarks>
    public string? AuthHeaderName
    {
        get => _authHeaderName;
        set
        {
            if (!SetProperty(ref _authHeaderName, value)) return;

            RaisePropertyChanged(nameof(CanSave));
            RaisePropertyChanged(nameof(Preview));
        }
    }

    /// <summary>The username half of a Basic credential — not itself a secret.</summary>
    public string? AuthUsername
    {
        get => _authUsername;
        set
        {
            if (!SetProperty(ref _authUsername, value)) return;

            RaisePropertyChanged(nameof(CanSave));
            RaisePropertyChanged(nameof(Preview));
        }
    }

    /// <summary>
    /// Whether an API key's value is Base64-encoded on its way into its header.
    /// </summary>
    /// <remarks>
    /// Off by default: most carriers want the key as issued, and encoding one that expects
    /// it plain produces a 401 that reads like a wrong credential. Not offered under Basic,
    /// which always encodes.
    /// </remarks>
    public bool Base64EncodeApiKey
    {
        get => _base64Key;
        set
        {
            if (!SetProperty(ref _base64Key, value)) return;

            // The preview is the only place this is visible before a send, since the header
            // it changes is the one carrying the credential.
            RaisePropertyChanged(nameof(Preview));
        }
    }

    /// <summary>
    /// True where the scheme has changed and the stored secret no longer applies.
    /// </summary>
    /// <remarks>
    /// Changing the scheme changes what the credential means — an API key is not a Basic
    /// password — so the stored one never carries over. Under an API key that means the box
    /// has to be filled in, and the save refuses it empty. Under Basic it means the stored
    /// value is dropped rather than reused: leaving the box blank is a deliberate credential
    /// with no password, which is a thing providers genuinely want.
    /// </remarks>
    public bool AuthTypeChanged => Selected is { } row && row.AuthType != _authType;

    /// <summary>True where the box has to be filled in before this will save.</summary>
    public bool AuthTypeChangedNeedsSecret => AuthTypeChanged && IsApiKeyAuth;

    /// <summary>What the credential box is called under the chosen scheme.</summary>
    public string SecretLabel => IsBasicAuth ? "Password" : "API key value";

    /// <summary>
    /// The mobile a test send goes to.
    /// </summary>
    /// <remarks>
    /// Typed each time rather than remembered. It is somebody's own phone, it differs per
    /// person testing, and a stored one would sooner or later send a test to whoever set the
    /// gateway up two years ago.
    /// </remarks>
    public string? TestNumber
    {
        get => _testNumber;
        set { if (SetProperty(ref _testNumber, value)) RaisePropertyChanged(nameof(CanTest)); }
    }

    /// <summary>
    /// True where a test would actually reach a provider.
    /// </summary>
    /// <remarks>
    /// The gateway has to be saved first. The key lives only in the database — the screen
    /// never gets it back — so there is nothing to test until it has been written, and a
    /// test run against unsaved edits would be a test of a request no clinic will send.
    /// </remarks>
    public bool CanTest =>
        !IsBusy
        && !IsNew
        && Selected is not null
        && !string.IsNullOrWhiteSpace(_testNumber);

    /// <summary>The row the editor is open on, or null while adding.</summary>
    public SmsGatewayRow? Selected =>
        _isEditing && _editingId != Guid.Empty
            ? _rows.FirstOrDefault(row => row.GatewayId == _editingId)
            : null;

    /// <summary>What the last test said, while this editor has been open.</summary>
    /// <remarks>
    /// Held here rather than read back off the row, so the answer that appears is the one
    /// from the send that just happened. The row carries the stored result too — see
    /// <see cref="LastTestSummary"/> — which is what a gateway tested last week shows.
    /// </remarks>
    public SmsResult? TestResult => _testResult;

    public bool HasTestResult => _testResult is not null;

    /// <summary>The stored outcome of whenever this gateway was last tested.</summary>
    public string? LastTestSummary => Selected is { LastTestUtc: { } when } row
        ? $"{(row.LastTestSucceeded ? "Passed" : "Failed")} "
            + $"{when.ToLocalTime():d MMM yyyy, h:mm tt} — {row.LastTestResult}"
        : null;

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
        new SmsAuth(
            _authType,
            _authHeaderName ?? string.Empty,
            _authUsername ?? string.Empty,
            _apiKey ?? string.Empty,
            _base64Key),
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

    /// <summary>
    /// The cap on how many messages this country may send, blank for none.
    /// </summary>
    /// <remarks>
    /// Text, parsed on save, for the same reason the price is: bound to an int? the empty
    /// box and a typed 0 would both arrive as null, and they mean opposite things here —
    /// no limit at all, and a limit of none.
    /// </remarks>
    public string? CreditLimit
    {
        get => _credit;
        set { if (SetProperty(ref _credit, value)) RaisePropertyChanged(nameof(CanSave)); }
    }

    /// <summary>The parsed credit, and whether what was typed is a number at all.</summary>
    private (int? Value, bool Ok) ParsedCredit
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_credit)) return (null, true);

            return int.TryParse(_credit.Trim(), out var value) && value >= 0
                ? (value, true)
                : (null, false);
        }
    }

    /// <summary>What every subscriber has sent, by country.</summary>
    public IReadOnlyList<SmsCountryUsage> Usage => _usage;

    /// <summary>True where any country has spent its credit.</summary>
    public bool AnyExhausted => _usage.Any(row => row.IsExhausted);

    public int SubscriberCount => _usage.Sum(row => row.ClinicCount);

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
    public bool HasStoredKey => Selected?.HasKey == true;

    public bool CanSave =>
        !IsBusy
        && !string.IsNullOrWhiteSpace(_country)
        && !string.IsNullOrWhiteSpace(_provider)
        && !string.IsNullOrWhiteSpace(_apiUrl)
        && ParsedPrice is not null
        && !string.IsNullOrWhiteSpace(_currency)
        && ParsedCredit.Ok
        && !string.IsNullOrWhiteSpace(_template)

        // The field the chosen scheme needs beside the secret.
        && (IsBasicAuth
            ? !string.IsNullOrWhiteSpace(_authUsername)
            : !string.IsNullOrWhiteSpace(_authHeaderName))

        // Basic may have no password at all — a provider authenticating on the key alone
        // documents it as the username with nothing after the colon. An API key cannot: a
        // header with nothing in it is not a credential.
        && (IsBasicAuth
            || (HasStoredKey && !AuthTypeChangedNeedsSecret)
            || !string.IsNullOrWhiteSpace(_apiKey));

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
        _usage = await _gateways.GetUsageAsync().ConfigureAwait(false);
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

        // The vendor's own default until a country is picked, which is the moment it
        // becomes a real answer. Not null: a select bound to null shows whichever currency
        // sorts first and would save that.
        _currency = PracticeCurrency.Default;
        _currencyChosen = false;

        // Blank, which is no limit. A new gateway acquiring a cap nobody typed is a country
        // that stops sending on a number somebody picked as a placeholder.
        _credit = null;

        // Prefilled rather than left blank. An empty payload box on a new gateway is a
        // blank page in a format nobody remembers, and the commonest shape is a better
        // starting point than nothing — see SmsPayload.DefaultTemplate.
        _template = SmsPayload.DefaultTemplate;
        _headers = SmsPayload.DefaultHeaders;
        _contentType = SmsPayload.Json;

        // An API key in Authorization, which is the commonest of the two and the one whose
        // header name is worth prefilling — a provider wanting its own name is a word
        // somebody replaces, not a blank box they have to guess the shape of.
        _authType = SmsAuthType.ApiKey;
        _authHeaderName = SmsAuth.AuthorizationHeader;
        _authUsername = null;

        // Off. Most carriers want the key exactly as issued, and encoding one that does not
        // expect it answers 401 — which reads as a wrong key rather than a wrong switch.
        _base64Key = false;
        _apiKey = null;
        _testResult = null;

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

        // Already decided, so the country must not overwrite it — this row was saved with
        // whatever somebody meant.
        _currencyChosen = true;

        _credit = row.CreditLimit?.ToString();

        // Defaulted where a row predates the payload columns. Those rows were saved with an
        // empty template by the migration, and an empty box would read as "this provider
        // wants no body" rather than "nobody has filled this in yet".
        _template = row.PayloadTemplate is { Length: > 0 }
            ? row.PayloadTemplate
            : SmsPayload.DefaultTemplate;

        _headers = row.Headers is { Length: > 0 } ? row.Headers : SmsPayload.DefaultHeaders;
        _contentType = row.ContentType is { Length: > 0 } ? row.ContentType : SmsPayload.Json;

        _authType = row.AuthType;

        // Defaulted where a row predates the auth columns, the same way the template is.
        // A blank header name reads as "this provider wants no credential", which no
        // provider does.
        _authHeaderName = row.AuthHeaderName is { Length: > 0 }
            ? row.AuthHeaderName
            : SmsAuth.AuthorizationHeader;

        _authUsername = row.AuthUsername;
        _base64Key = row.Base64EncodeApiKey;

        // Cleared with the panel. A pass shown against the country now open, from a send
        // made against the one just closed, is the worst kind of wrong answer.
        _testResult = null;

        // Never prefilled. The service does not return it, and a box showing a credential
        // is a credential on screen for as long as the panel is open.
        _apiKey = null;

        ErrorMessage = null;
        _ = RaiseAll();
    }

    /// <summary>
    /// Switches the scheme, keeping whatever the other one had typed into it.
    /// </summary>
    /// <remarks>
    /// The secret is cleared, and deliberately. An API key is not a Basic password: carrying
    /// one over would send the wrong credential under a scheme that looks correctly filled
    /// in, and the provider's 401 would read as the scheme not working.
    /// </remarks>
    private void SetAuthType(SmsAuthType type)
    {
        if (_authType == type) return;

        _authType = type;
        _apiKey = null;

        if (type == SmsAuthType.BasicAuth)
        {
            // Basic is Authorization by definition — see SmsAuth. Held at that so the
            // preview does not print a header the provider will not read.
            _authHeaderName = SmsAuth.AuthorizationHeader;
        }
        else if (string.IsNullOrWhiteSpace(_authHeaderName))
        {
            _authHeaderName = SmsAuth.AuthorizationHeader;
        }

        ErrorMessage = null;
        _ = RaiseAll();
    }

    private void Close()
    {
        _isEditing = false;
        _editingId = Guid.Empty;
        _apiKey = null;
        _testResult = null;

        ErrorMessage = null;
        _ = RaiseAll();
    }

    private Task SaveAsync() => RunGuardedAsync(async () =>
    {
        var refusal = await _gateways
            .SaveAsync(_editingId, _country ?? string.Empty, _provider ?? string.Empty,
                _apiUrl ?? string.Empty, _senderId, ParsedPrice ?? 0m,
                _currency ?? string.Empty, ParsedCredit.Value, _template ?? string.Empty,
                _headers,
                _contentType ?? SmsPayload.Json, _authType, _authHeaderName, _authUsername,
                _base64Key, _apiKey)
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

    /// <summary>
    /// Sends one real text through the stored gateway.
    /// </summary>
    /// <remarks>
    /// The result is not an error even when it fails — a provider refusing the send is the
    /// answer the test was asking for, and putting it in the error banner would make a
    /// working screen look broken. Only the refusals that stop it being attempted go there.
    /// </remarks>
    private Task TestAsync() => RunGuardedAsync(async () =>
    {
        var row = Selected;
        if (row is null) return;

        _testResult = null;
        _lastAction = null;

        var result = await _gateways
            .TestAsync(row.GatewayId, _testNumber)
            .ConfigureAwait(false);

        _testResult = result;

        // Re-read, because the send stamped the row with what came back — the stored
        // "last tested" line underneath would otherwise still show the previous attempt.
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
            nameof(PricePerMessage), nameof(CurrencyCode), nameof(CurrencyOptions),
            nameof(CreditLimit), nameof(Usage), nameof(AnyExhausted), nameof(SubscriberCount), nameof(PayloadTemplate),
            nameof(Headers), nameof(ContentType), nameof(AuthType), nameof(IsApiKeyAuth),
            nameof(IsBasicAuth), nameof(AuthHeaderName), nameof(AuthUsername),
            nameof(Base64EncodeApiKey), nameof(AuthTypeChanged),
            nameof(AuthTypeChangedNeedsSecret),
            nameof(SecretLabel), nameof(Preview), nameof(Selected),
            nameof(TestNumber), nameof(CanTest), nameof(TestResult),
            nameof(HasTestResult), nameof(LastTestSummary),
            nameof(ApiKey), nameof(HasStoredKey), nameof(CanSave), nameof(HasError),
        })
        {
            await RaisePropertyChanged(name).ConfigureAwait(false);
        }
    }
}
