using DYS.Molargo.Shared.Features.Auth.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Auth.ViewModels;

/// <summary>
/// Resetting a password with an emailed code.
/// </summary>
/// <remarks>
/// <para>
/// One screen, two steps: ask for a code, then type it back with the new password. One
/// screen rather than two pages because the clinic code and username have to be in hand for
/// both, and a second page would either carry them in a URL or ask for them twice.
/// </para>
/// <para>
/// The request step has exactly one outcome whatever happened underneath: the request was
/// taken. Anything else — "no such user", "that account has no email" — would let a stranger
/// with a clinic code work out which usernames exist, one guess at a time, and the clinic
/// code is derived from the practice name so it is not a secret that gates this.
/// </para>
/// </remarks>
public sealed class ForgotPasswordViewModel : BaseViewModel
{
    /// <summary>Mirrors the service's minimum, so the button disables rather than refuses.</summary>
    public const int MinimumLength = 10;

    private readonly IPasswordResetService _reset;

    private string? _clinicCode;
    private string? _username;
    private string? _code;
    private string? _password;
    private string? _confirm;
    private bool _show;
    private bool _entering;
    private bool _done;

    public ForgotPasswordViewModel(IPasswordResetService reset)
    {
        _reset = reset;

        RequestCommand = new MvxAsyncCommand(RequestAsync);
        ResetCommand = new MvxAsyncCommand(ResetAsync);
        HaveCodeCommand = new MvxCommand(() => SetEntering(true));
        BackCommand = new MvxCommand(() => SetEntering(false));

        ToggleShowCommand = new MvxCommand(() =>
        {
            _show = !_show;
            RaisePropertyChanged(nameof(ShowPassword));
        });
    }

    /// <summary>Sends a code, then moves to the second step regardless of what happened.</summary>
    public IMvxAsyncCommand RequestCommand { get; }

    public IMvxAsyncCommand ResetCommand { get; }

    /// <summary>Skips straight to the second step, for somebody returning with a code.</summary>
    public IMvxCommand HaveCodeCommand { get; }

    public IMvxCommand BackCommand { get; }

    public IMvxCommand ToggleShowCommand { get; }

    /// <summary>True once the code and password boxes are showing.</summary>
    public bool IsEnteringCode => _entering;

    public bool IsDone => _done;

    public bool ShowPassword => _show;

    public string? ClinicCode
    {
        get => _clinicCode;
        set { if (SetProperty(ref _clinicCode, value)) RaiseChecks(); }
    }

    public string? Username
    {
        get => _username;
        set { if (SetProperty(ref _username, value)) RaiseChecks(); }
    }

    /// <summary>
    /// The six digits, as typed.
    /// </summary>
    /// <remarks>
    /// A string, never an int. "004821" is a perfectly good code and becomes 4821 the moment
    /// anything treats it as a number — which then never matches.
    /// </remarks>
    public string? Code
    {
        get => _code;
        set { if (SetProperty(ref _code, value)) RaiseChecks(); }
    }

    public string? Password
    {
        get => _password;
        set { if (SetProperty(ref _password, value)) RaiseChecks(); }
    }

    /// <summary>
    /// Typed twice.
    /// </summary>
    /// <remarks>
    /// Asked for here where the administrative reset does not, and the difference is who is
    /// watching: an administrator setting a password reads it out and hears it repeated
    /// back, while somebody alone with a code who mistypes it has locked themselves out
    /// again and has to start over.
    /// </remarks>
    public string? Confirm
    {
        get => _confirm;
        set { if (SetProperty(ref _confirm, value)) RaiseChecks(); }
    }

    public bool CanRequest =>
        !IsBusy
        && !string.IsNullOrWhiteSpace(_clinicCode)
        && !string.IsNullOrWhiteSpace(_username);

    public bool CodeLooksComplete =>
        _code is { Length: 6 } typed && typed.All(char.IsAsciiDigit);

    public bool Matches =>
        !string.IsNullOrEmpty(_password)
        && string.Equals(_password, _confirm, StringComparison.Ordinal);

    public bool CanReset =>
        !IsBusy
        && CanRequest
        && CodeLooksComplete
        && (_password?.Length ?? 0) >= MinimumLength
        && Matches;

    /// <summary>The message under the second password box, or null while it is fine.</summary>
    public string? ConfirmError =>
        string.IsNullOrEmpty(_confirm) || Matches ? null : "The two do not match.";

    private Task RequestAsync() => RunGuardedAsync(async () =>
    {
        await _reset
            .RequestAsync(_clinicCode ?? string.Empty, _username ?? string.Empty)
            .ConfigureAwait(false);

        // Nothing is read back, because the service returns nothing to read. The screen
        // moves on identically whether a code went out or the username was invented.
        _entering = true;

        await RaisePropertyChanged(nameof(IsEnteringCode)).ConfigureAwait(false);
        RaiseChecks();
    });

    private Task ResetAsync() => RunGuardedAsync(async () =>
    {
        var refusal = await _reset
            .ResetAsync(
                _clinicCode ?? string.Empty,
                _username ?? string.Empty,
                _code ?? string.Empty,
                _password ?? string.Empty)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);

            // The code is cleared on a miss, the password is not. Retyping a passphrase
            // because six digits were fat-fingered is how somebody ends up setting one they
            // did not intend.
            _code = null;

            await RaisePropertyChanged(nameof(Code)).ConfigureAwait(false);
            RaiseChecks();
            return;
        }

        // Cleared before the success renders, so nothing typed survives in a view model
        // that outlives the keystroke.
        _code = null;
        _password = null;
        _confirm = null;
        _show = false;
        _done = true;

        await RaisePropertyChanged(nameof(IsDone)).ConfigureAwait(false);
        await RaisePropertyChanged(nameof(ShowPassword)).ConfigureAwait(false);
    });

    private void SetEntering(bool entering)
    {
        _entering = entering;

        RaisePropertyChanged(nameof(IsEnteringCode));
        RaiseChecks();
    }

    private void RaiseChecks()
    {
        RaisePropertyChanged(nameof(CanRequest));
        RaisePropertyChanged(nameof(CanReset));
        RaisePropertyChanged(nameof(CodeLooksComplete));
        RaisePropertyChanged(nameof(Matches));
        RaisePropertyChanged(nameof(ConfirmError));
    }
}
