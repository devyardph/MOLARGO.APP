using DYS.Molargo.Shared.Features.Auth.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Auth.ViewModels;

/// <summary>
/// Signing in with a clinic code, a username and a password.
/// </summary>
public sealed class SignInViewModel : BaseViewModel
{
    private readonly IAuthService _auth;
    private readonly IAppNavigator _navigator;
    private readonly ISessionHandoff _handoff;

    private string? _tenantCode;
    private string? _username;
    private string? _password;
    private string? _failure;
    private bool _lockedOut;

    public SignInViewModel(
        IAuthService auth, IAppNavigator navigator, ISessionHandoff handoff)
    {
        _auth = auth;
        _navigator = navigator;
        _handoff = handoff;

        // Built once, in the constructor — never rebuilt per render.
        SignInCommand = new MvxAsyncCommand(SignInAsync);
    }

    public IMvxAsyncCommand SignInCommand { get; }

    public override Task Initialize() => LoadAsync();

    public string? TenantCode
    {
        get => _tenantCode;
        set => SetProperty(ref _tenantCode, value);
    }

    public string? Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public string? Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    /// <summary>Why the last attempt was refused, in the words the screen shows.</summary>
    public string? Failure => _failure;

    public bool HasFailure => !string.IsNullOrEmpty(_failure);

    /// <summary>
    /// True where the refusal was a lockout rather than a wrong password.
    /// </summary>
    /// <remarks>
    /// Separated so the screen can stop inviting another attempt. Every other refusal is
    /// the same deliberately vague message, and re-trying is the right response to it.
    /// </remarks>
    public bool IsLockedOut => _lockedOut;

    public bool CanSubmit =>
        !IsBusy
        && !string.IsNullOrWhiteSpace(_tenantCode)
        && !string.IsNullOrWhiteSpace(_username)
        && !string.IsNullOrWhiteSpace(_password);

    private Task LoadAsync() => RunGuardedAsync(async () =>
    {
        // Prefilled on a device that already belongs to a clinic. The code is not a secret
        // and retyping it at every sign-in on a surgery tablet is friction with no
        // security value — the password is the credential.
        _tenantCode = await _auth.GetInstalledTenantCodeAsync().ConfigureAwait(false);

        await RaisePropertyChanged(nameof(TenantCode)).ConfigureAwait(false);
        await RaisePropertyChanged(nameof(CanSubmit)).ConfigureAwait(false);
    });

    private Task SignInAsync() => RunGuardedAsync(async () =>
    {
        _failure = null;
        _lockedOut = false;

        var result = await _auth
            .SignInAsync(_tenantCode ?? string.Empty, _username ?? string.Empty,
                _password ?? string.Empty)
            .ConfigureAwait(false);

        // Cleared whatever the outcome. A refused password left in the box gets retried by
        // a second click, and a successful one has no reason to stay in memory.
        _password = null;

        if (!result.Succeeded)
        {
            _failure = result.Failure;
            _lockedOut = result.IsLockedOut;

            await RaiseAll().ConfigureAwait(false);
            return;
        }

        await RaiseAll().ConfigureAwait(false);

        // On a host that persists the session by redirecting through an endpoint — the web
        // head, setting its cookie — the navigation has already been started. Sending them
        // home as well would race it, and the second navigation would win and lose the
        // cookie.
        if (!_handoff.RedirectsAfterSignIn) _navigator.ToHome();
    });

    private async Task RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(TenantCode), nameof(Username), nameof(Password), nameof(Failure),
            nameof(HasFailure), nameof(IsLockedOut), nameof(CanSubmit),
        })
        {
            await RaisePropertyChanged(name).ConfigureAwait(false);
        }
    }
}
