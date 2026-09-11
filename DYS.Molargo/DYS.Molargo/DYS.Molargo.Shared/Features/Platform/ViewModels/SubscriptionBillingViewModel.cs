using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Features.Platform.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Platform.ViewModels;

/// <summary>What the panel beside the billing table is doing.</summary>
public enum BillingPanel
{
    None = 0,

    /// <summary>Recording that money arrived, with a reference.</summary>
    Paid = 1,

    /// <summary>Recording that collection failed, with a reason.</summary>
    Failed = 2,

    /// <summary>Writing a charge off, with a reason.</summary>
    Waived = 3,
}

/// <summary>
/// The vendor's billing table: every clinic, what it owes, and whether it paid.
/// </summary>
public sealed class SubscriptionBillingViewModel : BaseViewModel
{
    private readonly ISubscriptionBillingService _billing;
    private readonly ISessionService _session;

    private IReadOnlyList<BillingRow> _rows = [];
    private string? _search;
    private Guid? _selectedTenantId;
    private IReadOnlyList<SubscriptionCharge> _history = [];

    private BillingPanel _panel;
    private SubscriptionCharge? _subject;
    private string? _note;
    private string? _lastAction;

    public SubscriptionBillingViewModel(
        ISubscriptionBillingService billing, ISessionService session)
    {
        _billing = billing;
        _session = session;

        SelectClinicCommand = new MvxAsyncCommand<Guid>(SelectAsync);
        RaiseChargesCommand = new MvxAsyncCommand(RaiseAsync);
        StartPaidCommand = new MvxCommand<Guid>(id => Start(id, BillingPanel.Paid));
        StartFailedCommand = new MvxCommand<Guid>(id => Start(id, BillingPanel.Failed));
        StartWaivedCommand = new MvxCommand<Guid>(id => Start(id, BillingPanel.Waived));
        CancelPanelCommand = new MvxCommand(ClosePanel);
        ConfirmCommand = new MvxAsyncCommand(ConfirmAsync);
    }

    public IMvxAsyncCommand<Guid> SelectClinicCommand { get; }

    public IMvxAsyncCommand RaiseChargesCommand { get; }

    public IMvxCommand<Guid> StartPaidCommand { get; }

    public IMvxCommand<Guid> StartFailedCommand { get; }

    public IMvxCommand<Guid> StartWaivedCommand { get; }

    public IMvxCommand CancelPanelCommand { get; }

    public IMvxAsyncCommand ConfirmCommand { get; }

    public override Task Initialize() => LoadAsync();

    public bool IsPermitted => _session.IsSuperAdmin;

    public string ActingAs => _session.UserDisplayName ?? "nobody";

    public string? LastAction => _lastAction;

    /// <summary>
    /// What the table shows: every clinic, or those matching the search.
    /// </summary>
    /// <remarks>
    /// Filtered here rather than in the service. The whole list is a handful of rows the
    /// vendor already holds, and re-querying the database on every keystroke would be a
    /// round trip per character to narrow something already in memory.
    /// </remarks>
    public IReadOnlyList<BillingRow> Rows
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_search)) return _rows;

            var term = _search.Trim();

            return _rows
                .Where(row => Contains(row.Clinic, term)
                    || Contains(row.Slug, term)
                    || Contains(row.CountryCode, term)
                    || Contains(row.PlanName, term)
                    || Contains(StatusWord(row), term))
                .ToList();
        }
    }

    /// <summary>
    /// The search term. Matches the name, code, country, plan or status.
    /// </summary>
    /// <remarks>
    /// Status is searchable too, which is the one non-obvious inclusion: typing "failed"
    /// is how somebody actually asks the question this screen exists to answer, and it
    /// saves adding a filter control beside the box.
    /// </remarks>
    public string? Search
    {
        get => _search;
        set
        {
            SetProperty(ref _search, value);

            RaisePropertyChanged(nameof(Rows));
            RaisePropertyChanged(nameof(IsFiltered));
            RaisePropertyChanged(nameof(MatchCount));
        }
    }

    public bool IsFiltered => !string.IsNullOrWhiteSpace(_search);

    public int MatchCount => Rows.Count;

    private static bool Contains(string? value, string term) =>
        value is not null
        && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    /// <summary>The status as a word somebody would type.</summary>
    private static string StatusWord(BillingRow row) => row.NeverBilled
        ? "never billed"
        : row.Status switch
        {
            ChargeStatus.Due => "due",
            ChargeStatus.Paid => "paid",
            ChargeStatus.Failed => "failed",
            ChargeStatus.Waived => "written off waived",
            _ => string.Empty,
        };

    // ---- the figures ------------------------------------------------------
    //
    // Counted from every clinic, never from the filtered view. They describe the health of
    // the business, and a "Failed: 0" that only means "none in this search" would be the
    // most dangerous number on the screen.

    public int ClinicCount => _rows.Count;

    public int PaidCount => _rows.Count(row => row.Status == ChargeStatus.Paid);

    public int FailedCount => _rows.Count(row => row.Status == ChargeStatus.Failed);

    public int DueCount => _rows.Count(row => row.Status == ChargeStatus.Due);

    public int NeverBilledCount => _rows.Count(row => row.NeverBilled);

    /// <summary>
    /// Clinics still able to sign in while a payment has failed.
    /// </summary>
    /// <remarks>
    /// The figure worth acting on. Nothing suspends them automatically, so this is the
    /// count of conversations somebody owes.
    /// </remarks>
    public int FailedAndActiveCount => _rows.Count(row => row.FailedAndActive);

    /// <summary>
    /// Outstanding money, grouped by currency.
    /// </summary>
    /// <remarks>
    /// Never summed into one total. Adding pesos to pounds produces a number that is
    /// wrong in every currency, and this app has no exchange rates — nor should it invent
    /// one to make a headline figure.
    /// </remarks>
    public IReadOnlyList<(string Currency, decimal Amount)> Outstanding => _rows
        .Where(row => row.HasOutstanding)
        .GroupBy(row => row.CurrencyCode)
        .Select(group => (group.Key, group.Sum(row => row.Outstanding)))
        .OrderBy(entry => entry.Key)
        .ToList();

    // ---- the selected clinic ---------------------------------------------

    public bool IsSelected(Guid tenantId) => _selectedTenantId == tenantId;

    public BillingRow? Selected =>
        _rows.FirstOrDefault(row => row.TenantId == _selectedTenantId);

    public IReadOnlyList<SubscriptionCharge> History => _history;

    // ---- the panel -------------------------------------------------------

    public BillingPanel Panel => _panel;

    public bool HasPanel => _panel is not BillingPanel.None && _subject is not null;

    public SubscriptionCharge? Subject => _subject;

    public string PanelTitle => _panel switch
    {
        BillingPanel.Paid => "Record a payment",
        BillingPanel.Failed => "Record a failed payment",
        BillingPanel.Waived => "Write this charge off",
        _ => "Charge",
    };

    /// <summary>The reference on a payment, or the reason on a failure or write-off.</summary>
    public string? Note
    {
        get => _note;
        set => SetProperty(ref _note, value);
    }

    public string NoteLabel => _panel switch
    {
        BillingPanel.Paid => "Reference",
        BillingPanel.Failed => "What went wrong",
        _ => "Why it is being written off",
    };

    /// <summary>A reference is optional; a reason is not.</summary>
    /// <remarks>
    /// Deliberately asymmetric. A payment that arrived is self-explanatory, and the
    /// reference is a convenience. A failure or a write-off with no reason is the entry
    /// that generates a phone call next quarter to ask what it meant.
    /// </remarks>
    public bool NoteIsRequired => _panel is not BillingPanel.Paid;

    public bool CanConfirm =>
        !IsBusy
        && _subject is not null
        && (!NoteIsRequired || !string.IsNullOrWhiteSpace(_note));

    // ---- loading ---------------------------------------------------------

    private Task LoadAsync() => RunGuardedAsync(LoadTableAsync);

    private async Task LoadTableAsync()
    {
        _rows = await _billing.GetBillingAsync().ConfigureAwait(false);

        // Settles on the clinic that needs attention rather than the first alphabetically,
        // since the table is already ordered worst-first.
        _selectedTenantId ??= _rows.FirstOrDefault()?.TenantId;

        _history = _selectedTenantId is { } tenantId
            ? await _billing.GetHistoryAsync(tenantId).ConfigureAwait(false)
            : [];

        RaiseAll();
    }

    /// <summary>
    /// Selects a clinic and loads its charge history.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>RunGuardedAsync</c>: that returns silently while another
    /// operation is in flight, and one always is just after the screen first renders — so
    /// an early click did nothing at all, with no error to explain it.
    /// </remarks>
    private async Task SelectAsync(Guid tenantId)
    {
        _selectedTenantId = tenantId;
        _panel = BillingPanel.None;
        _subject = null;
        _note = null;
        _lastAction = null;

        try
        {
            _history = await _billing.GetHistoryAsync(tenantId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        RaiseAll();

        await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
    }

    private void Start(Guid chargeId, BillingPanel panel)
    {
        _subject = _history.FirstOrDefault(charge => charge.Id == chargeId);
        _panel = _subject is null ? BillingPanel.None : panel;
        _note = _panel is BillingPanel.Paid ? _subject?.Reference : null;
        _lastAction = null;

        RaiseAll();
    }

    private void ClosePanel()
    {
        _panel = BillingPanel.None;
        _subject = null;
        _note = null;

        RaiseAll();
    }

    private Task ConfirmAsync() => RunGuardedAsync(async () =>
    {
        if (_subject is not { } charge) return;

        var refusal = _panel switch
        {
            BillingPanel.Paid => await _billing
                .MarkPaidAsync(charge.Id, _note)
                .ConfigureAwait(false),

            BillingPanel.Failed => await _billing
                .MarkFailedAsync(charge.Id, _note ?? string.Empty)
                .ConfigureAwait(false),

            BillingPanel.Waived => await _billing
                .WaiveAsync(charge.Id, _note ?? string.Empty)
                .ConfigureAwait(false),

            _ => null,
        };

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = _panel switch
        {
            BillingPanel.Paid => $"Payment recorded for {charge.PeriodLabel}.",
            BillingPanel.Failed => $"Failure recorded for {charge.PeriodLabel}. Nothing "
                + "suspends the clinic automatically.",
            _ => $"{charge.PeriodLabel} written off.",
        };

        _panel = BillingPanel.None;
        _subject = null;
        _note = null;

        await LoadTableAsync().ConfigureAwait(false);
    });

    private Task RaiseAsync() => RunGuardedAsync(async () =>
    {
        var run = await _billing.RaiseMonthlyChargesAsync().ConfigureAwait(false);

        if (run.Refusal is { Length: > 0 })
        {
            ErrorMessage = run.Refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = run.Raised == 0
            ? $"Nothing to raise — this month is already billed"
                + (run.Skipped > 0 ? $" ({run.Skipped} skipped)." : ".")
            : $"Raised {run.Raised} "
                + (run.Raised == 1 ? "charge" : "charges")
                + (run.Skipped > 0 ? $", skipped {run.Skipped}" : string.Empty)
                + ". Nothing has been collected — record each outcome as it happens.";

        await LoadTableAsync().ConfigureAwait(false);
    });

    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(IsPermitted), nameof(ActingAs), nameof(LastAction), nameof(Rows),
            nameof(Search), nameof(IsFiltered), nameof(MatchCount),
            nameof(ClinicCount), nameof(PaidCount), nameof(FailedCount), nameof(DueCount),
            nameof(NeverBilledCount), nameof(FailedAndActiveCount), nameof(Outstanding),
            nameof(Selected), nameof(History), nameof(Panel), nameof(HasPanel),
            nameof(Subject), nameof(PanelTitle), nameof(Note), nameof(NoteLabel),
            nameof(NoteIsRequired), nameof(CanConfirm),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
