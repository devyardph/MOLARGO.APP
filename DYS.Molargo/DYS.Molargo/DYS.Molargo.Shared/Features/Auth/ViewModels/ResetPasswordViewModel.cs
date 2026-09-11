using DYS.Molargo.Shared.Features.Auth.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Auth.ViewModels;

/// <summary>
/// Setting a new password from an emailed link.
/// </summary>
/// <remarks>
/// Takes the token as its navigation parameter. It is the only thing identifying the
/// account — there is nothing signed in — so the screen shows whose account it is before
/// asking for a password: somebody who followed a link from an inbox deserves to see it is
/// the right account before typing into it.
/// </remarks>
public sealed class ResetPasswordViewModel : BaseViewModel<string>
{
    private readonly IPasswordResetService _reset;
    private readonly IAppNavigator _navigator;

    private string? _token;
    private ResetSubject? _subject;
    private bool _checked;
    private string? _password;
    private string? _confirm;
    private bool _show;
    private bool _done;

    public ResetPasswordViewModel(IPasswordResetService reset, IAppNavigator navigator)
    {
        _reset = reset;
        _navigator = navigator;

        SaveCommand = new MvxAsyncCommand(SaveAsync);
        ToggleShowCommand = new MvxCommand(() =>
        {
            _show = !_show;
            RaisePropertyChanged(nameof(ShowPassword));
        });

        SignInCommand = new MvxCommand(() => _navigator.To("/sign-in"));
    }

    public IMvxAsyncCommand SaveCommand { get; }

    public IMvxCommand ToggleShowCommand { get; }

    public IMvxCommand SignInCommand { get; }

    public override void Prepare(string parameter) => _token = parameter;

    public override Task Initialize() => RunGuardedAsync(async () =>
    {
        _subject = await _reset
            .ValidateAsync(_token ?? string.Empty)
            .ConfigureAwait(false);

        // Recorded separately from the subject being null. "Not checked yet" and "checked,
        // and the link is dead" look the same on a nullable reference, and the screen has
        // to say different things about them — a spinner against a refusal.
        _checked = true;

        await RaisePropertyChanged(nameof(IsChecked)).ConfigureAwait(false);
        await RaisePropertyChanged(nameof(IsLinkGood)).ConfigureAwait(false);
        await RaisePropertyChanged(nameof(StaffName)).ConfigureAwait(false);
        await RaisePropertyChanged(nameof(Username)).ConfigureAwait(false);
        await RaisePropertyChanged(nameof(PracticeName)).ConfigureAwait(false);
    });

    public bool IsChecked => _checked;

    public bool IsLinkGood => _subject is not null;

    public string? StaffName => _subject?.Name;

    public string? Username => _subject?.Username;

    public string? ClinicCode => _subject?.ClinicCode;

    public string? PracticeName => _subject?.PracticeName;

    /// <summary>True once the password has been changed, so the form gives way to a note.</summary>
    public bool IsDone => _done;

    public bool ShowPassword => _show;

    public string? Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value)) RaiseChecks();
        }
    }

    /// <summary>
    /// Typed twice.
    /// </summary>
    /// <remarks>
    /// Asked for here where the administrative reset does not, and the difference is who is
    /// watching: an administrator setting a password reads it out and hears it repeated
    /// back, while somebody alone with an emailed link who mistypes it has locked
    /// themselves out again and has to start over.
    /// </remarks>
    public string? Confirm
    {
        get => _confirm;
        set
        {
            if (SetProperty(ref _confirm, value)) RaiseChecks();
        }
    }

    public const int MinimumLength = 10;

    public bool IsLongEnough => (_password?.Length ?? 0) >= MinimumLength;

    public bool Matches =>
        !string.IsNullOrEmpty(_password)
        && string.Equals(_password, _confirm, StringComparison.Ordinal);

    public bool CanSave => !IsBusy && IsLinkGood && IsLongEnough && Matches;

    /// <summary>The message under the second box, or null while it is fine.</summary>
    public string? ConfirmError =>
        string.IsNullOrEmpty(_confirm) || Matches ? null : "The two do not match.";

    private void RaiseChecks()
    {
        RaisePropertyChanged(nameof(IsLongEnough));
        RaisePropertyChanged(nameof(Matches));
        RaisePropertyChanged(nameof(ConfirmError));
        RaisePropertyChanged(nameof(CanSave));
    }

    private Task SaveAsync() => RunGuardedAsync(async () =>
    {
        var refusal = await _reset
            .CompleteAsync(_token ?? string.Empty, _password ?? string.Empty)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        // Both boxes cleared before the success renders, so the typed password does not
        // survive in a view model that outlives the keystroke.
        _password = null;
        _confirm = null;
        _show = false;
        _done = true;

        await RaisePropertyChanged(nameof(IsDone)).ConfigureAwait(false);
        await RaisePropertyChanged(nameof(ShowPassword)).ConfigureAwait(false);
    });
}
