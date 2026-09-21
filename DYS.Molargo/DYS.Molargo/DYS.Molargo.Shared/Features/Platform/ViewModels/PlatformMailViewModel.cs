using DYS.Molargo.Shared.Features.Platform.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Platform.ViewModels;

/// <summary>
/// The mail account the platform sends from.
/// </summary>
/// <remarks>
/// Its own screen rather than a section on the SMS one, though the two are siblings: both
/// are the vendor's own way of reaching the outside world. They are configured at different
/// times by different people — the SMS gateway when a country is opened up, this the day
/// the product is first installed — and a screen that asked for both would be half-blank
/// either way.
/// </remarks>
public sealed class PlatformMailViewModel : BaseViewModel
{
    private readonly IPlatformMailService _mail;
    private readonly ISessionService _session;

    private PlatformMailRow? _row;
    private string? _lastAction;

    private string? _sender;
    private string? _senderName;
    private string? _host;
    private int _port = 587;
    private bool _enabled = true;
    private string? _password;
    private string? _testTo;

    public PlatformMailViewModel(IPlatformMailService mail, ISessionService session)
    {
        _mail = mail;
        _session = session;

        SaveCommand = new MvxAsyncCommand(SaveAsync);
        TestCommand = new MvxAsyncCommand(TestAsync);
    }

    public IMvxAsyncCommand SaveCommand { get; }

    public IMvxAsyncCommand TestCommand { get; }

    public override Task Initialize() => RunGuardedAsync(ReloadAsync);

    /// <summary>Vendor only, like every other screen under Platform.</summary>
    public bool IsPermitted => _session.IsSuperAdmin;

    public string ActingAs => _session.UserDisplayName ?? "nobody";

    public PlatformMailRow? Account => _row;

    public string? LastAction => _lastAction;

    public bool IsSetUp => _row is not null;

    public string? SenderAddress
    {
        get => _sender;
        set { if (SetProperty(ref _sender, value)) RaisePropertyChanged(nameof(CanSave)); }
    }

    public string? SenderName
    {
        get => _senderName;
        set => SetProperty(ref _senderName, value);
    }

    public string? SmtpHost
    {
        get => _host;
        set { if (SetProperty(ref _host, value)) RaisePropertyChanged(nameof(CanSave)); }
    }

    public int SmtpPort
    {
        get => _port;
        set { if (SetProperty(ref _port, value)) RaisePropertyChanged(nameof(CanSave)); }
    }

    public bool IsEnabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    /// <summary>
    /// The password being typed, never the one stored.
    /// </summary>
    /// <remarks>
    /// Starts empty and stays empty unless somebody types: the service reads blank as
    /// "leave the stored one alone". The screen says so, because a blank box over a working
    /// account otherwise reads as "there is no password".
    /// </remarks>
    public string? AppPassword
    {
        get => _password;
        set { if (SetProperty(ref _password, value)) RaisePropertyChanged(nameof(CanSave)); }
    }

    /// <summary>Where a test message goes.</summary>
    public string? TestTo
    {
        get => _testTo;
        set { if (SetProperty(ref _testTo, value)) RaisePropertyChanged(nameof(CanTest)); }
    }

    public bool HasStoredPassword => _row?.HasPassword == true;

    public bool CanSave =>
        !IsBusy
        && !string.IsNullOrWhiteSpace(_sender)
        && !string.IsNullOrWhiteSpace(_host)
        && _port is > 0 and < 65536
        && (HasStoredPassword || !string.IsNullOrWhiteSpace(_password));

    public bool CanTest => !IsBusy && IsSetUp && !string.IsNullOrWhiteSpace(_testTo);

    /// <summary>
    /// Re-reads the account, without the busy guard.
    /// </summary>
    /// <remarks>
    /// Split from the guarded loader because <c>RunGuardedAsync</c> does not nest — it
    /// returns silently while another operation is in flight, so calling the guarded
    /// version from inside a save would skip the reload and leave the screen showing what
    /// it held before.
    /// </remarks>
    private async Task ReloadAsync()
    {
        _row = await _mail.GetAsync().ConfigureAwait(false);

        if (_row is not null)
        {
            _sender = _row.SenderAddress;
            _senderName = _row.SenderName;
            _host = _row.SmtpHost;
            _port = _row.SmtpPort;
            _enabled = _row.IsEnabled;
        }

        // Never prefilled. The service does not return it, and a box showing a credential
        // is a credential on screen for as long as the page is open.
        _password = null;

        await RaiseAll().ConfigureAwait(false);
    }

    private Task SaveAsync() => RunGuardedAsync(async () =>
    {
        var refusal = await _mail
            .SaveAsync(_sender ?? string.Empty, _senderName, _host ?? string.Empty,
                _port, _enabled, _password)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = "Sending account saved.";
        ErrorMessage = null;

        await ReloadAsync().ConfigureAwait(false);
    });

    private Task TestAsync() => RunGuardedAsync(async () =>
    {
        _lastAction = await _mail.TestAsync(_testTo ?? string.Empty).ConfigureAwait(false);

        ErrorMessage = null;

        await ReloadAsync().ConfigureAwait(false);
    });

    private async Task RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(IsPermitted), nameof(ActingAs), nameof(Account), nameof(LastAction),
            nameof(IsSetUp), nameof(SenderAddress), nameof(SenderName), nameof(SmtpHost),
            nameof(SmtpPort), nameof(IsEnabled), nameof(AppPassword), nameof(TestTo),
            nameof(HasStoredPassword), nameof(CanSave), nameof(CanTest), nameof(HasError),
        })
        {
            await RaisePropertyChanged(name).ConfigureAwait(false);
        }
    }
}
