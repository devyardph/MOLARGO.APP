using DYS.Molargo.Shared.Features.Auth.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Auth.ViewModels;

/// <summary>
/// Asking for a reset link.
/// </summary>
/// <remarks>
/// The screen has exactly one outcome, whatever happened underneath: the request was taken.
/// Anything else — "no such user", "that account has no email" — would let a stranger with
/// a clinic code work out which usernames exist, one guess at a time, and the clinic code
/// is derived from the practice name so it is not a secret either.
/// </remarks>
public sealed class ForgotPasswordViewModel : BaseViewModel
{
    private readonly IPasswordResetService _reset;

    private string? _clinicCode;
    private string? _username;
    private bool _sent;

    public ForgotPasswordViewModel(IPasswordResetService reset)
    {
        _reset = reset;

        RequestCommand = new MvxAsyncCommand(RequestAsync);
        AgainCommand = new MvxCommand(Again);
    }

    public IMvxAsyncCommand RequestCommand { get; }

    /// <summary>Back to the form, for somebody who mistyped the username.</summary>
    public IMvxCommand AgainCommand { get; }

    public string? ClinicCode
    {
        get => _clinicCode;
        set
        {
            if (SetProperty(ref _clinicCode, value)) RaisePropertyChanged(nameof(CanSubmit));
        }
    }

    public string? Username
    {
        get => _username;
        set
        {
            if (SetProperty(ref _username, value)) RaisePropertyChanged(nameof(CanSubmit));
        }
    }

    /// <summary>True once the request has been taken, whatever came of it.</summary>
    public bool IsSent => _sent;

    public bool CanSubmit =>
        !IsBusy
        && !string.IsNullOrWhiteSpace(_clinicCode)
        && !string.IsNullOrWhiteSpace(_username);

    private Task RequestAsync() => RunGuardedAsync(async () =>
    {
        await _reset
            .RequestAsync(_clinicCode ?? string.Empty, _username ?? string.Empty)
            .ConfigureAwait(false);

        // Nothing is read back, because the service returns nothing to read. The screen
        // says the same sentence whether a link went out or the username was invented.
        _sent = true;

        await RaisePropertyChanged(nameof(IsSent)).ConfigureAwait(false);
        await RaisePropertyChanged(nameof(CanSubmit)).ConfigureAwait(false);
    });

    private void Again()
    {
        _sent = false;
        _username = null;

        RaisePropertyChanged(nameof(IsSent));
        RaisePropertyChanged(nameof(Username));
        RaisePropertyChanged(nameof(CanSubmit));
    }
}
