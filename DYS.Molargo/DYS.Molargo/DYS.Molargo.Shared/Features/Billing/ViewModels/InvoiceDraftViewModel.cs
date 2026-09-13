using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Shared.Components;
using DYS.Molargo.Shared.Features.Billing.Services;
using DYS.Molargo.Shared.Features.Patient.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;
using DYS.Molargo.Domain.Dtos;

namespace DYS.Molargo.Shared.Features.Billing.ViewModels;

/// <summary>
/// Building one invoice, on its own screen.
/// </summary>
/// <remarks>
/// <para>
/// Split out of the invoices list because the two are different jobs sharing one pane. The
/// list is a day's takings, read at a glance; building an invoice is a search, a running
/// total and a decision to bill. Both were crammed into a 1fr column beside the table,
/// which left the catalogue picker about three hundred pixels wide on the screen where
/// somebody is hunting an item number.
/// </para>
/// <para>
/// It owns no rules. Every refusal — per-tooth items, issued invoices, double-billing —
/// comes back from <see cref="IBillingService"/>, so this and the list cannot drift into
/// disagreeing about what is allowed.
/// </para>
/// </remarks>
public sealed class InvoiceDraftViewModel : BaseViewModel<Guid>
{
    /// <summary>Payment terms the front desk actually offers.</summary>
    public static readonly int[] Terms = [0, 7, 14, 30];

    private readonly IBillingService _billing;
    private readonly IAppNavigator _navigator;
    private readonly ISessionService _session;
    private readonly IPatientService _patients;

    private Guid _id;

    private InvoiceDetail? _detail;
    private IReadOnlyList<CatalogueItem> _catalogue = [];
    private IReadOnlyList<SuggestedItem> _suggestions = [];
    private IReadOnlyList<BillableVisit> _visits = [];

    private bool _notFound;
    private string? _search;
    private string? _tooth;
    private int _termDays;
    private string? _refusal;

    private bool _isMoving;
    private string? _patientSearch;
    private IReadOnlyList<PatientListItemDto> _patientResults = [];

    public InvoiceDraftViewModel(
        IBillingService billing,
        IPatientService patients,
        IAppNavigator navigator,
        ISessionService session)
    {
        _billing = billing;
        _patients = patients;
        _navigator = navigator;
        _session = session;

        // Built once, in the constructor — never rebuilt per render.
        AddSuggestedCommand = new MvxAsyncCommand<Guid>(AddSuggestedAsync);
        AddLineCommand = new MvxAsyncCommand<Guid>(AddLineAsync);
        RemoveLineCommand = new MvxAsyncCommand<Guid>(RemoveLineAsync);
        UpdateLineCommand = new MvxAsyncCommand<LineEdit>(UpdateLineAsync);
        AttachVisitCommand = new MvxAsyncCommand<Guid?>(AttachVisitAsync);
        SetTermsCommand = new MvxCommand<int>(days => TermDays = days);
        IssueCommand = new MvxAsyncCommand(IssueAsync);
        DiscardCommand = new MvxAsyncCommand(DiscardAsync);
        StartMoveCommand = new MvxCommand(() => SetMoving(true));
        CancelMoveCommand = new MvxCommand(() => SetMoving(false));
        SearchPatientsCommand = new MvxAsyncCommand(SearchPatientsAsync);
        MoveToPatientCommand = new MvxAsyncCommand<Guid>(MoveToPatientAsync);
        OpenPatientCommand = new MvxCommand(OpenPatient);
        BackCommand = new MvxCommand(() => _navigator.ToBilling());
    }

    /// <summary>Adds treatment the patient has actually had, in one click.</summary>
    public IMvxAsyncCommand<Guid> AddSuggestedCommand { get; }

    public IMvxAsyncCommand<Guid> AddLineCommand { get; }

    public IMvxAsyncCommand<Guid> RemoveLineCommand { get; }

    public IMvxAsyncCommand<LineEdit> UpdateLineCommand { get; }

    public IMvxAsyncCommand<Guid?> AttachVisitCommand { get; }

    public IMvxCommand<int> SetTermsCommand { get; }

    public IMvxAsyncCommand IssueCommand { get; }

    public IMvxAsyncCommand DiscardCommand { get; }

    /// <summary>
    /// Starts moving the draft to a different patient.
    /// </summary>
    /// <remarks>
    /// Offered because the alternative is discarding the draft and retyping every line,
    /// which is what makes people bill the wrong person rather than fix it.
    /// </remarks>
    public IMvxCommand StartMoveCommand { get; }

    public IMvxCommand CancelMoveCommand { get; }

    public IMvxAsyncCommand SearchPatientsCommand { get; }

    public IMvxAsyncCommand<Guid> MoveToPatientCommand { get; }

    public IMvxCommand OpenPatientCommand { get; }

    public IMvxCommand BackCommand { get; }

    public override void Prepare(Guid parameter) => _id = parameter;

    public override Task Initialize() => LoadAsync();

    // ---- what the view binds --------------------------------------------

    public bool NotFound => _notFound;

    public InvoiceDetail? Detail => _detail;

    public IReadOnlyList<InvoiceLine> Lines => _detail?.Lines ?? [];

    public string PatientName => _detail?.PatientName ?? "Patient";

    /// <summary>
    /// True only while the invoice can still be built. An issued one opens read-only.
    /// </summary>
    public bool IsDraft => _detail is { Invoice.Status: Domain.Enums.InvoiceStatus.Draft };

    public decimal Total => _detail?.Invoice.Total ?? 0m;

    public bool HasLines => Lines.Count > 0;

    public string? Refusal => _refusal;

    public bool IsMovingPatient => _isMoving;

    public string? PatientSearch
    {
        get => _patientSearch;
        set => SetProperty(ref _patientSearch, value);
    }

    public IReadOnlyList<PatientListItemDto> PatientResults => _patientResults;

    /// <summary>
    /// One line's editable figures, as text.
    /// </summary>
    /// <remarks>
    /// Held as text and applied on change rather than bound to the line: a decimal-bound
    /// input rejects a half-typed "12." while somebody is still typing it, which on a
    /// front desk reads as the field fighting them.
    /// </remarks>
    public static string Money(decimal value) =>
        value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    // ---- the catalogue, on the left --------------------------------------

    public string? Search
    {
        get => _search;
        set
        {
            if (!SetProperty(ref _search, value)) return;

            RaisePropertyChanged(nameof(Results));
        }
    }

    /// <summary>
    /// The tooth a per-tooth item is charged against.
    /// </summary>
    /// <remarks>
    /// Beside the catalogue rather than beside the lines: it has to be set before the item
    /// is clicked, because a per-tooth item with no tooth is refused outright.
    /// </remarks>
    public string? Tooth
    {
        get => _tooth;
        set => SetProperty(ref _tooth, value);
    }

    /// <summary>
    /// What the search matches, or the whole catalogue while nothing is typed.
    /// </summary>
    /// <remarks>
    /// Matched on item number as well as wording. A front desk billing a crown types
    /// "613" as often as "crown", and a search that only read descriptions made the
    /// faster of the two habits the one that failed.
    /// </remarks>
    public IReadOnlyList<CatalogueItem> Results
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_search)) return _catalogue;

            var term = _search.Trim();

            return _catalogue
                .Where(item =>
                    item.ItemNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || item.Description.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (item.Code.PatientFriendlyName ?? string.Empty)
                        .Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    // ---- what was actually done ------------------------------------------

    public IReadOnlyList<SuggestedItem> Suggestions => _suggestions;

    public bool HasSuggestions => _suggestions.Count > 0;

    /// <summary>The visits this draft could be billing, for the picker.</summary>
    public IReadOnlyList<BillableVisit> Visits => _visits;

    public Guid? AttachedVisitId => _detail?.Invoice.AppointmentId;

    public bool IsVisitAttached(Guid appointmentId) => AttachedVisitId == appointmentId;

    // ---- issuing ---------------------------------------------------------

    public int TermDays
    {
        get => _termDays;
        set
        {
            if (!SetProperty(ref _termDays, value)) return;

            RaisePropertyChanged(nameof(TermsLabel));
        }
    }

    public bool IsTerm(int days) => _termDays == days;

    public static string TermLabel(int days) => days == 0 ? "Due now" : $"{days}d";

    public string TermsLabel => _termDays == 0
        ? "Payable today."
        : $"Payable within {_termDays} days.";

    private async Task LoadAsync() => await RunGuardedAsync(async () =>
    {
        await ReloadAsync().ConfigureAwait(false);

        if (_detail is null)
        {
            _notFound = true;
            RaiseAll();
            return;
        }

        _catalogue = await _billing
            .GetCatalogueAsync(_detail.Invoice.PracticeLocationId)
            .ConfigureAwait(false);

        _visits = await _billing
            .GetBillableVisitsAsync(_session.LocationId, _detail.Invoice.PatientId)
            .ConfigureAwait(false);

        RaiseAll();
    }).ConfigureAwait(false);

    /// <summary>
    /// Re-reads the invoice and what is still left to suggest.
    /// </summary>
    /// <remarks>
    /// Unguarded, and called from inside the guarded commands. RunGuardedAsync returns
    /// silently while IsBusy, so a guarded reload nested in a guarded caller does nothing
    /// at all — the database is right and the screen is not.
    /// </remarks>
    private async Task ReloadAsync()
    {
        _detail = await _billing.GetInvoiceAsync(_id).ConfigureAwait(false);

        _suggestions = _detail is null
            ? []
            : await _billing.GetSuggestedItemsAsync(_id).ConfigureAwait(false);
    }

    private Task AddSuggestedAsync(Guid planItemId) => RunGuardedAsync(async () =>
    {
        _refusal = await _billing.AddSuggestedLineAsync(_id, planItemId).ConfigureAwait(false);

        await ReloadAsync().ConfigureAwait(false);

        RaiseAll();
    });

    private Task AddLineAsync(Guid codeId) => RunGuardedAsync(async () =>
    {
        _refusal = await _billing.AddLineAsync(_id, codeId, _tooth).ConfigureAwait(false);

        // Cleared on success only. A refusal is usually "that item needs a tooth number",
        // and wiping the box they are about to type into would be the wrong answer to it.
        if (_refusal is null) _tooth = null;

        await ReloadAsync().ConfigureAwait(false);

        RaiseAll();
    });

    private Task RemoveLineAsync(Guid lineId) => RunGuardedAsync(async () =>
    {
        _refusal = await _billing.RemoveLineAsync(lineId).ConfigureAwait(false);

        await ReloadAsync().ConfigureAwait(false);

        RaiseAll();
    });

    private Task UpdateLineAsync(LineEdit? edit) => RunGuardedAsync(async () =>
    {
        // The command hands this in nullable. Nothing to apply without an edit, and a
        // guard here beats a null-forgiving operator that would throw on the day the
        // command is invoked from somewhere new.
        if (edit is null) return;

        _refusal = await _billing
            .UpdateLineAsync(edit.LineId, edit.Quantity, edit.UnitFee, edit.Discount)
            .ConfigureAwait(false);

        await ReloadAsync().ConfigureAwait(false);

        RaiseAll();
    });

    private Task AttachVisitAsync(Guid? appointmentId) => RunGuardedAsync(async () =>
    {
        // The same visit again detaches it. A picker that could only ever attach left no
        // way back from a misclick except discarding the draft.
        var next = AttachedVisitId == appointmentId ? null : appointmentId;

        _refusal = await _billing.AttachVisitAsync(_id, next).ConfigureAwait(false);

        await ReloadAsync().ConfigureAwait(false);

        RaiseAll();
    });

    private Task IssueAsync() => RunGuardedAsync(async () =>
    {
        _refusal = await _billing.IssueInvoiceAsync(_id, _termDays).ConfigureAwait(false);

        if (_refusal is not null)
        {
            RaiseAll();
            return;
        }

        // Back to the list, where an issued invoice is taken payment against. Staying here
        // would leave a read-only builder on screen with nothing left to do on it.
        _navigator.ToBilling();
    });

    private Task DiscardAsync() => RunGuardedAsync(async () =>
    {
        _refusal = await _billing.DiscardDraftAsync(_id).ConfigureAwait(false);

        if (_refusal is not null)
        {
            RaiseAll();
            return;
        }

        _navigator.ToBilling();
    });

    private void SetMoving(bool moving)
    {
        _isMoving = moving;
        _patientSearch = null;
        _patientResults = [];
        _refusal = null;

        RaiseAll();
    }

    private Task SearchPatientsAsync() => RunGuardedAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(_patientSearch))
        {
            _patientResults = [];
        }
        else
        {
            var page = await _patients
                .SearchAsync(new PatientQuery { SearchTerm = _patientSearch.Trim(), PageSize = 8 })
                .ConfigureAwait(false);

            _patientResults = page.Items;
        }

        RaiseAll();
    });

    private Task MoveToPatientAsync(Guid patientId) => RunGuardedAsync(async () =>
    {
        _refusal = await _billing
            .MoveDraftToPatientAsync(_id, patientId)
            .ConfigureAwait(false);

        if (_refusal is not null)
        {
            RaiseAll();
            return;
        }

        _isMoving = false;
        _patientSearch = null;
        _patientResults = [];

        // Moving clears the attached visit — it belonged to the patient being moved away
        // from — so what is suggested changes with it.
        await ReloadAsync().ConfigureAwait(false);

        _visits = await _billing
            .GetBillableVisitsAsync(_session.LocationId, patientId)
            .ConfigureAwait(false);

        RaiseAll();
    });

    private void OpenPatient()
    {
        if (_detail is { } detail) _navigator.ToPatientRecord(detail.Invoice.PatientId);
    }

    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(NotFound), nameof(Detail), nameof(Lines), nameof(PatientName),
            nameof(IsDraft), nameof(Total), nameof(HasLines), nameof(Refusal),
            nameof(Search), nameof(Tooth), nameof(Results), nameof(Suggestions),
            nameof(HasSuggestions), nameof(Visits), nameof(AttachedVisitId),
            nameof(TermDays), nameof(TermsLabel), nameof(IsMovingPatient),
            nameof(PatientSearch), nameof(PatientResults),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
