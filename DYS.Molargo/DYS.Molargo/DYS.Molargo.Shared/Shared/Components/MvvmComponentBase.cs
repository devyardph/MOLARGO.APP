using System.ComponentModel;
using DYS.Molargo.Shared.ViewModels;
using Microsoft.AspNetCore.Components;

namespace DYS.Molargo.Shared.Components;

/// <summary>
/// Binds a Razor component to an MvvmCross view model: resolves it from DI, runs the
/// MvvmCross <c>Initialize</c> lifecycle, and re-renders whenever the view model raises
/// <see cref="INotifyPropertyChanged.PropertyChanged"/>. This is the whole View layer of
/// the MVVM triangle — the pages below it contain markup and nothing else.
/// </summary>
/// <typeparam name="TViewModel">The screen's view model.</typeparam>
public abstract class MvvmComponentBase<TViewModel> : ComponentBase, IAsyncDisposable
    where TViewModel : BaseViewModel
{
    [Inject] protected TViewModel ViewModel { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        await ViewModel.Initialize();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        _ = InvokeAsync(StateHasChanged);

    public virtual ValueTask DisposeAsync()
    {
        // Not optional. A view model can outlive its component — one holding a reference
        // to a singleton, or simply a render that raced a navigation — and a leaked
        // handler then calls StateHasChanged on a disposed component.
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        if (ViewModel is IDisposable disposable) disposable.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// The variant for a screen with a route parameter. Drives <c>Prepare</c> before
/// <c>Initialize</c>, and re-prepares when the parameter changes.
/// </summary>
/// <typeparam name="TViewModel">The screen's view model.</typeparam>
/// <typeparam name="TParameter">The navigation parameter's type.</typeparam>
public abstract class MvvmComponentBase<TViewModel, TParameter> : ComponentBase, IAsyncDisposable
    where TViewModel : BaseViewModel<TParameter>
{
    private TParameter? _lastParameter;
    private bool _subscribed;

    [Inject] protected TViewModel ViewModel { get; set; } = default!;

    /// <summary>The value handed to the view model's <c>Prepare</c> method.</summary>
    protected abstract TParameter NavigationParameter { get; }

    protected override async Task OnParametersSetAsync()
    {
        var parameter = NavigationParameter;

        // Re-prepare only when the parameter actually changed. Blazor reuses the
        // component instance across a route change within the same page, so without the
        // comparison navigating /patients/1 -> /patients/2 keeps showing patient 1;
        // without the short-circuit, every unrelated re-render reloads the record.
        if (_subscribed && EqualityComparer<TParameter>.Default.Equals(_lastParameter, parameter)) return;

        if (!_subscribed)
        {
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            _subscribed = true;
        }

        _lastParameter = parameter;
        ViewModel.Prepare(parameter);
        await ViewModel.Initialize();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        _ = InvokeAsync(StateHasChanged);

    public virtual ValueTask DisposeAsync()
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        if (ViewModel is IDisposable disposable) disposable.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
