using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Shared.Features.Platform.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Platform.ViewModels;

/// <summary>
/// What the panel beside the list is showing.
/// </summary>
/// <remarks>
/// One mode rather than two independent flags. Editing an operator and setting their
/// password used to be tracked separately — an entity for one, an id for the other — which
/// let both be open at once, one as a form on the right and one as a box wedged into the
/// row. Making it a mode means the two cannot fight over the same space, because there is
/// only one answer to what is showing.
/// </remarks>
public enum OperatorPanel
{
    /// <summary>Nothing selected: the panel explains how these accounts work.</summary>
    None = 0,

    Edit = 1,

    Password = 2,
}

/// <summary>
/// The vendor's own operator accounts — who may administer the platform.
/// </summary>
public sealed class PlatformUsersViewModel : BaseViewModel
{
    private readonly IPlatformUserService _users;
    private readonly ISessionService _session;

    private IReadOnlyList<OperatorRow> _operators = [];
    private OperatorPanel _panel;
    private Provider? _editing;
    private bool _editingIsNew;
    private string? _draftPassword;
    private bool _showPassword;
    private string? _lastAction;

    public PlatformUsersViewModel(IPlatformUserService users, ISessionService session)
    {
        _users = users;
        _session = session;

        NewOperatorCommand = new MvxCommand(StartNew);
        SelectOperatorCommand = new MvxAsyncCommand<Guid>(id => OpenAsync(id, OperatorPanel.Edit));
        StartResetCommand = new MvxAsyncCommand<Guid>(id => OpenAsync(id, OperatorPanel.Password));
        CancelEditCommand = new MvxCommand(ClosePanel);
        SaveOperatorCommand = new MvxAsyncCommand(SaveAsync);
        SetPasswordCommand = new MvxAsyncCommand(SetPasswordAsync);
        ToggleShowPasswordCommand = new MvxCommand(ToggleShowPassword);

        DeactivateCommand = new MvxAsyncCommand<Guid>(id => SetActiveAsync(id, false));
        ReactivateCommand = new MvxAsyncCommand<Guid>(id => SetActiveAsync(id, true));
        UnlockCommand = new MvxAsyncCommand<Guid>(UnlockAsync);
    }

    public IMvxCommand NewOperatorCommand { get; }

    public IMvxAsyncCommand<Guid> SelectOperatorCommand { get; }

    /// <summary>Opens the same panel in password mode: details read-only, password below.</summary>
    public IMvxAsyncCommand<Guid> StartResetCommand { get; }

    public IMvxCommand CancelEditCommand { get; }

    public IMvxAsyncCommand SaveOperatorCommand { get; }

    public IMvxAsyncCommand SetPasswordCommand { get; }

    public IMvxCommand ToggleShowPasswordCommand { get; }

    public IMvxAsyncCommand<Guid> DeactivateCommand { get; }

    public IMvxAsyncCommand<Guid> ReactivateCommand { get; }

    public IMvxAsyncCommand<Guid> UnlockCommand { get; }

    public override Task Initialize() => LoadAsync();

    public bool IsPermitted => _session.IsSuperAdmin;

    public string ActingAs => _session.UserDisplayName ?? "nobody";

    public string? LastAction => _lastAction;

    public IReadOnlyList<OperatorRow> Operators => _operators;

    public int ActiveCount => _operators.Count(row => row.IsActive);

    /// <summary>
    /// True where the vendor has one working account and no way back in if it is lost.
    /// </summary>
    /// <remarks>
    /// Surfaced prominently because there is no password reset in this app: a second
    /// operator is the whole of the recovery plan, and a vendor running on one does not
    /// find that out until the day it matters.
    /// </remarks>
    public bool IsSinglePointOfFailure =>
        _operators.Count(row => row.IsActive && row.CanSignIn) < 2;

    public int LockedOutCount => _operators.Count(row => row.IsLockedOut);

    public int AwaitingPasswordCount => _operators.Count(row => row.NeedsPassword);

    // ---- editor ----------------------------------------------------------

    public Provider? Editing => _editing;

    public OperatorPanel Panel => _panel;

    public bool HasEditor => _panel is OperatorPanel.Edit && _editing is not null;

    /// <summary>Password mode: the same panel, details shown but not editable.</summary>
    public bool IsSettingPassword =>
        _panel is OperatorPanel.Password && _editing is not null;

    public bool EditingIsNew => _editingIsNew;

    public string EditorTitle => _editing is null
        ? "Operator"
        : _editingIsNew ? "New operator" : _editing.FullName;

    /// <summary>
    /// A password field on the edit form only while the operator is new.
    /// </summary>
    /// <remarks>
    /// An existing operator's password is changed through the password panel instead, which
    /// makes that an action somebody chose rather than a field they could fill in by
    /// accident while correcting a surname.
    /// </remarks>
    public bool ShowsPasswordField => _editingIsNew;

    /// <summary>The details the password panel shows, read-only.</summary>
    public string? SubjectName => _editing?.FullName;

    public string? SubjectUsername => _editing?.Username;

    public string? SubjectEmail => _editing?.Email;

    public string? FirstName
    {
        get => _editing?.FirstName;
        set
        {
            if (_editing is null) return;

            _editing.FirstName = value ?? string.Empty;
            RaisePropertyChanged();
        }
    }

    public string? LastName
    {
        get => _editing?.LastName;
        set
        {
            if (_editing is null) return;

            _editing.LastName = value ?? string.Empty;
            RaisePropertyChanged();
        }
    }

    public string? Username
    {
        get => _editing?.Username;
        set
        {
            if (_editing is null) return;

            _editing.Username = value;
            RaisePropertyChanged();
        }
    }

    public string? Email
    {
        get => _editing?.Email;
        set
        {
            if (_editing is null) return;

            _editing.Email = value;
            RaisePropertyChanged();
        }
    }

    /// <summary>
    /// The password as typed. Never populated from what is stored.
    /// </summary>
    /// <remarks>
    /// Same rule as the mail account's app password: nothing stored is ever rendered back
    /// into the page, so there is nothing on screen that could leak it. Left empty on an
    /// existing operator means "keep the one you have".
    /// </remarks>
    public string? DraftPassword
    {
        get => _draftPassword;
        set => SetProperty(ref _draftPassword, value);
    }

    public bool ShowPassword => _showPassword;

    /// <summary>Which row is the panel about, so the list can mark it.</summary>
    public bool IsOpenFor(Guid providerId) =>
        _panel is not OperatorPanel.None && _editing?.Id == providerId;

    public bool CanSetPassword =>
        !IsBusy && _editing is not null && !string.IsNullOrWhiteSpace(_draftPassword);

    // ---- loading ---------------------------------------------------------

    private Task LoadAsync() => RunGuardedAsync(LoadListAsync);

    private async Task LoadListAsync()
    {
        _operators = await _users.GetOperatorsAsync().ConfigureAwait(false);

        RaiseAll();
    }

    private void StartNew()
    {
        _editing = new Provider { IsActive = true };
        _panel = OperatorPanel.Edit;
        _editingIsNew = true;
        _draftPassword = null;
        _showPassword = false;
        _lastAction = null;

        RaiseAll();
    }

    /// <summary>
    /// Opens one operator in the panel, in the mode asked for.
    /// </summary>
    /// <remarks>
    /// One method for both buttons. Editing and setting a password load the same record
    /// into the same place and differ only in what the panel then lets you change, so
    /// two loaders would be two chances for them to drift out of step.
    /// </remarks>
    private async Task OpenAsync(Guid providerId, OperatorPanel panel)
    {
        // Deliberately not RunGuardedAsync.
        //
        // That helper returns silently when another operation is already in flight, and
        // one always is just after the screen first renders — Initialize sets the busy
        // flag, but the buttons only learn they are disabled after a round trip to the
        // server. A click landing in that window did nothing whatsoever: no panel, no
        // error, no spinner. Opening a panel is a read and a state change; it is not the
        // kind of work that needs excluding, and it must not be the kind that vanishes.
        try
        {
            var subject = await _users.GetOperatorAsync(providerId).ConfigureAwait(false);

            _editing = subject;
            _panel = subject is null ? OperatorPanel.None : panel;
            _editingIsNew = false;
            _draftPassword = null;
            _showPassword = false;
            _lastAction = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        RaiseAll();

        await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
    }

    private void ClosePanel()
    {
        _editing = null;
        _panel = OperatorPanel.None;
        _editingIsNew = false;

        // Cleared on the way out. A secret sitting in a bound property outlives the render
        // that needed it for no reason.
        _draftPassword = null;
        _showPassword = false;

        RaiseAll();
    }

    private void ToggleShowPassword()
    {
        _showPassword = !_showPassword;

        RaisePropertyChanged(nameof(ShowPassword));
    }

    private Task SaveAsync() => RunGuardedAsync(async () =>
    {
        if (_editing is null) return;

        var refusal = await _users
            .SaveOperatorAsync(_editing, _draftPassword)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = _editingIsNew
            ? $"{_editing.FullName} added. They sign in with the vendor's clinic code."
            : $"{_editing.FullName} updated.";

        _editing = null;
        _panel = OperatorPanel.None;
        _editingIsNew = false;
        _draftPassword = null;
        _showPassword = false;

        await LoadListAsync().ConfigureAwait(false);
    });

    private Task SetPasswordAsync() => RunGuardedAsync(async () =>
    {
        if (_editing is not { } subject || string.IsNullOrWhiteSpace(_draftPassword))
        {
            return;
        }

        var refusal = await _users
            .SetPasswordAsync(subject.Id, _draftPassword)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = $"Password set for {subject.FullName}. Tell them out of band — "
            + "nothing here emails it.";

        _editing = null;
        _panel = OperatorPanel.None;
        _draftPassword = null;
        _showPassword = false;

        await LoadListAsync().ConfigureAwait(false);
    });

    private Task SetActiveAsync(Guid providerId, bool isActive) => RunGuardedAsync(async () =>
    {
        var refusal = await _users
            .SetOperatorActiveAsync(providerId, isActive)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = isActive
            ? "Operator reactivated."
            : "Operator deactivated. Their audit entries stay attributed to them.";

        await LoadListAsync().ConfigureAwait(false);
    });

    private Task UnlockAsync(Guid providerId) => RunGuardedAsync(async () =>
    {
        var refusal = await _users.UnlockOperatorAsync(providerId).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = "Lockout cleared. They can try again now.";

        await LoadListAsync().ConfigureAwait(false);
    });

    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(IsPermitted), nameof(ActingAs), nameof(LastAction), nameof(Operators),
            nameof(ActiveCount), nameof(IsSinglePointOfFailure), nameof(LockedOutCount),
            nameof(AwaitingPasswordCount), nameof(Editing), nameof(Panel),
            nameof(HasEditor), nameof(IsSettingPassword), nameof(EditingIsNew),
            nameof(EditorTitle), nameof(ShowsPasswordField), nameof(SubjectName),
            nameof(SubjectUsername), nameof(SubjectEmail), nameof(FirstName),
            nameof(LastName), nameof(Username), nameof(Email), nameof(DraftPassword),
            nameof(ShowPassword), nameof(CanSetPassword),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
