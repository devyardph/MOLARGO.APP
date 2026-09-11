using MvvmCross.ViewModels;

namespace DYS.Molargo.Shared.ViewModels;

/// <summary>
/// Base for every view model in the app. Builds on MvvmCross's <see cref="MvxViewModel"/>
/// so the <c>Prepare</c> / <c>Initialize</c> lifecycle, <c>SetProperty</c> change
/// notification and <c>MvxAsyncCommand</c> all behave exactly as they would under a
/// native MvvmCross head. The Razor views subscribe to <c>PropertyChanged</c> and
/// re-render; nothing here knows what a view is.
/// </summary>
public abstract class BaseViewModel : MvxViewModel
{
    private bool _isBusy;
    private string? _errorMessage;
    private string _errorHeadline = ErrorHeadlines.Refused;

    protected BaseViewModel()
    {
        // MvvmCross would otherwise marshal change notification through a native
        // main-thread dispatcher that no Razor host installs. Blazor does its own
        // marshalling in InvokeAsync(StateHasChanged), so raise events inline.
        ShouldAlwaysRaiseInpcOnUserInterfaceThread(false);
    }

    /// <summary>True while a long-running load or save is in flight.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        protected set => SetProperty(ref _isBusy, value);
    }

    /// <summary>Set when an operation fails, so the view can show it instead of a blank screen.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        protected set => SetProperty(ref _errorMessage, value);
    }

    /// <inheritdoc cref="ErrorHeadlines"/>
    public string ErrorHeadline
    {
        get => _errorHeadline;
        protected set => SetProperty(ref _errorHeadline, value);
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Runs <paramref name="work"/> with busy state and error capture applied, so no
    /// command has to repeat the same try/finally.
    /// </summary>
    protected async Task RunGuardedAsync(Func<Task> work)
    {
        if (IsBusy) return;

        IsBusy = true;
        ErrorMessage = null;
        ErrorHeadline = ErrorHeadlines.Refused;
        await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);

        try
        {
            await work().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request — not an error worth showing. This matters
            // most for search-as-you-type, where every keystroke cancels the last
            // request and a cancelled read would otherwise paint an error banner.
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ErrorHeadline = ErrorHeadlines.Unexpected;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>
/// The bold first words of the red notice a failure paints — see <c>NoticeComponent</c>.
/// </summary>
/// <remarks>
/// A headline rather than a bare sentence because the fill colour is not the message: it
/// is invisible to a colour-blind user and absent from the accessibility tree entirely,
/// so the outcome has to be stated in words that a screen reader will read out.
///
/// Which of the two applies is decided by how the error arrived, not by the view. Every
/// explicit <c>ErrorMessage = refusal</c> in this app is a write that a service declined
/// on purpose — a duplicate email, a name already taken, a last-active-account rule — so
/// <see cref="Refused"/> is the default. A caught exception is different: it may just as
/// easily have come from a read, and telling somebody their save failed when the screen
/// merely could not load is its own small lie.
/// </remarks>
public static class ErrorHeadlines
{
    /// <summary>A service declined the write, and said why.</summary>
    public const string Refused = "Not saved.";

    /// <summary>Something threw. It might not have been a save at all.</summary>
    public const string Unexpected = "Something went wrong.";
}

/// <summary>
/// Base for a view model that takes a navigation parameter — a record id from the route,
/// typically.
/// </summary>
/// <remarks>
/// A near-duplicate of <see cref="BaseViewModel"/> rather than a subclass of it, because
/// MvvmCross splits <c>MvxViewModel</c> and <c>MvxViewModel&lt;TParameter&gt;</c> and
/// offers no shared base carrying <c>Prepare</c>. Keep the two bodies in step.
/// </remarks>
/// <typeparam name="TParameter">The navigation parameter's type.</typeparam>
public abstract class BaseViewModel<TParameter> : MvxViewModel<TParameter>
{
    private bool _isBusy;
    private string? _errorMessage;
    private string _errorHeadline = ErrorHeadlines.Refused;

    protected BaseViewModel() => ShouldAlwaysRaiseInpcOnUserInterfaceThread(false);

    /// <inheritdoc cref="BaseViewModel.IsBusy"/>
    public bool IsBusy
    {
        get => _isBusy;
        protected set => SetProperty(ref _isBusy, value);
    }

    /// <inheritdoc cref="BaseViewModel.ErrorMessage"/>
    public string? ErrorMessage
    {
        get => _errorMessage;
        protected set => SetProperty(ref _errorMessage, value);
    }

    /// <inheritdoc cref="BaseViewModel.ErrorHeadline"/>
    public string ErrorHeadline
    {
        get => _errorHeadline;
        protected set => SetProperty(ref _errorHeadline, value);
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <inheritdoc cref="BaseViewModel.RunGuardedAsync"/>
    protected async Task RunGuardedAsync(Func<Task> work)
    {
        if (IsBusy) return;

        IsBusy = true;
        ErrorMessage = null;
        ErrorHeadline = ErrorHeadlines.Refused;
        await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);

        try
        {
            await work().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ErrorHeadline = ErrorHeadlines.Unexpected;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
