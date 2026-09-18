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
    private string? _code;
    private string? _codeSentTo;

    public SignInViewModel(
        IAuthService auth, IAppNavigator navigator, ISessionHandoff handoff)
    {
        _auth = auth;
        _navigator = navigator;
        _handoff = handoff;

        // Built once, in the constructor — never rebuilt per render.
        SignInCommand = new MvxAsyncCommand(SignInAsync);
        VerifyCodeCommand = new MvxAsyncCommand(VerifyCodeAsync);
        StartOverCommand = new MvxCommand(StartOver);
    }

    public IMvxAsyncCommand SignInCommand { get; }

    /// <summary>Submits the emailed code and finishes the sign-in.</summary>
    public IMvxAsyncCommand VerifyCodeCommand { get; }

    /// <summary>
    /// Abandons the code step and goes back to the password.
    /// </summary>
    /// <remarks>
    /// There has to be a way back. Somebody whose code never arrives is otherwise stuck on
    /// a screen whose only control they cannot use, and the way to get a fresh code is to
    /// sign in again.
    /// </remarks>
    public IMvxCommand StartOverCommand { get; }

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

    /// <summary>The code the person is typing.</summary>
    public string? Code
    {
        get => _code;
        set => SetProperty(ref _code, value);
    }

    /// <summary>True while the password is accepted and the code is outstanding.</summary>
    public bool NeedsCode => _codeSentTo is not null;

    /// <summary>The masked address the code went to.</summary>
    public string? CodeSentTo => _codeSentTo;

    /// <summary>
    /// Six digits before the button does anything.
    /// </summary>
    /// <remarks>
    /// Length only. Whether the digits are right is the service's business, and a screen
    /// that decides a code looks wrong before asking is one that will one day be wrong
    /// about it.
    /// </remarks>
    public bool CanVerify =>
        !IsBusy && _code is { Length: 6 } typed && typed.All(char.IsAsciiDigit);

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

        // The password was right and a code is on its way. Not a success — no session
        // exists yet — so this is checked before the failure branch, which would otherwise
        // show "not signed in" to somebody whose password was perfectly correct.
        if (result.NeedsCode)
        {
            _codeSentTo = result.CodeSentTo;
            _code = null;

            await RaiseAll().ConfigureAwait(false);
            return;
        }

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

    private Task VerifyCodeAsync() => RunGuardedAsync(async () =>
    {
        _failure = null;
        _lockedOut = false;

        var result = await _auth
            .CompleteWithCodeAsync(
                _tenantCode ?? string.Empty, _username ?? string.Empty, _code ?? string.Empty)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            _failure = result.Failure;
            _lockedOut = result.IsLockedOut;

            // The box is cleared but the step is not abandoned. A wrong digit is the common
            // case and the code in the email is still the right one; sending them back to
            // the password would burn that code for nothing.
            _code = null;

            // A lockout is the exception — there is nothing left to type.
            if (_lockedOut) _codeSentTo = null;

            await RaiseAll().ConfigureAwait(false);
            return;
        }

        _code = null;
        _codeSentTo = null;

        await RaiseAll().ConfigureAwait(false);

        if (!_handoff.RedirectsAfterSignIn) _navigator.ToHome();
    });

    private void StartOver()
    {
        _codeSentTo = null;
        _code = null;
        _password = null;
        _failure = null;

        // Fire and forget is wrong here, but so is awaiting in a void command: this is a
        // synchronous reset of four fields and the raise is the only asynchronous part.
        _ = RaiseAll();
    }

    private async Task RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(TenantCode), nameof(Username), nameof(Password), nameof(Failure),
            nameof(HasFailure), nameof(IsLockedOut), nameof(CanSubmit),
            nameof(Code), nameof(NeedsCode), nameof(CodeSentTo), nameof(CanVerify),
        })
        {
            await RaisePropertyChanged(name).ConfigureAwait(false);
        }
    }
}
