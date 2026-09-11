using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Components;
using DYS.Molargo.Shared.Repositories;
using DYS.Molargo.Shared.Services;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Features.Billing.Services;

/// <summary>One row of the invoice list.</summary>
public sealed record InvoiceRow(
    Guid InvoiceId,
    Guid PatientId,
    string PatientName,
    string InvoiceNumber,
    decimal Total,
    decimal FundEstimate,
    decimal Outstanding,
    InvoiceStatus Status)
{
    /// <summary>
    /// What the patient is expected to pay once the fund has paid its share.
    /// </summary>
    /// <remarks>
    /// Floored at zero. Crediting an invoice without revising its claim leaves the fund
    /// being asked for more than the invoice is now worth, and the arithmetic then reports
    /// a patient gap of minus sixty dollars — which is not a thing. The mismatch itself is
    /// real and worth fixing, so <see cref="ClaimExceedsInvoice"/> says so rather than the
    /// figure quietly going negative.
    /// </remarks>
    public decimal PatientGap => Math.Max(Total - FundEstimate, 0m);

    /// <summary>The claim asks the fund for more than the invoice now totals.</summary>
    public bool ClaimExceedsInvoice => FundEstimate > Total;
}

/// <summary>
/// One invoice in full: its lines, its claim, and every payment against it.
/// </summary>
/// <param name="Status">
/// The status the ledger implies, worked out by the service. Passed in rather than
/// computed here because deciding it needs to know what today is, and a record reaching
/// for the ambient clock is how a screen starts disagreeing with the tests.
/// </param>
public sealed record InvoiceDetail(
    Invoice Invoice,
    string PatientName,
    IReadOnlyList<InvoiceLine> Lines,
    Claim? Claim,
    IReadOnlyList<Payment> Payments,
    InvoiceStatus Status)
{
    /// <summary>
    /// What the fund is expected to pay.
    /// </summary>
    /// <remarks>
    /// The approved amount once the fund has assessed, the claimed amount before that.
    /// Using the claim throughout showed a patient gap of nothing on an invoice the fund
    /// had already part-refused — the gap is exactly what the patient has to be told about.
    /// </remarks>
    public decimal FundEstimate => Claim is null
        ? 0m
        : Claim.AssessedUtc is null ? Claim.AmountClaimed : Claim.AmountApproved;

    /// <summary>
    /// Paid, computed from the payment rows rather than read off the invoice.
    /// </summary>
    /// <remarks>
    /// The ledger is the truth. <c>Invoice.AmountPaid</c> is a cached total and a cache
    /// that disagrees with the payments behind it is how a patient gets chased for money
    /// they have already handed over.
    /// </remarks>
    public decimal Paid => Payments.Sum(payment => payment.Amount);

    public decimal Outstanding => Invoice.Total - Paid;

    public bool IsSettled => Outstanding <= 0m;

    /// <summary>
    /// The claim asks the fund for more than the invoice now totals — normally because
    /// the invoice was credited after the claim went in.
    /// </summary>
    public bool ClaimExceedsInvoice => FundEstimate > Invoice.Total;

    /// <summary>Money handed back, as a positive figure for display.</summary>
    public decimal Refunded => -Payments
        .Where(payment => payment.Amount < 0m)
        .Sum(payment => payment.Amount);
}

/// <summary>
/// One patient's debt, split by how old it is.
/// </summary>
/// <param name="Current">Not yet 30 days overdue, including not yet due.</param>
public sealed record DebtorRow(
    Guid PatientId,
    string PatientName,
    decimal Current,
    decimal Days30,
    decimal Days60,
    decimal Days90Plus)
{
    public decimal Total => Current + Days30 + Days60 + Days90Plus;

    /// <summary>The oldest bucket carrying anything — what decides the next step.</summary>
    public string OldestBucket => Days90Plus > 0m
        ? "90 days and over"
        : Days60 > 0m ? "60 days" : Days30 > 0m ? "30 days" : "current";
}

/// <summary>One line of the banking sheet.</summary>
public sealed record TakingsLine(PaymentMethod Method, int Count, decimal Amount);

/// <summary>The day's takings, by method.</summary>
public sealed record BankingSheet(
    DateOnly Day,
    IReadOnlyList<TakingsLine> Lines,
    decimal Refunds)
{
    public int Count => Lines.Sum(line => line.Count);

    /// <summary>
    /// Net takings — receipts less refunds, which is what actually reaches the bank.
    /// </summary>
    public decimal Total => Lines.Sum(line => line.Amount);
}

/// <summary>One row of the claims worklist.</summary>
public sealed record ClaimRow(
    Guid ClaimId,
    Guid InvoiceId,
    Guid PatientId,
    string PatientName,
    string Items,
    ClaimType Type,
    string? PayerName,
    decimal AmountClaimed,
    decimal AmountApproved,
    ClaimStatus Status,
    string? AssessmentMessage)
{
    public bool IsRejected => Status is ClaimStatus.Rejected;

    public bool IsOpen => Status is ClaimStatus.Draft or ClaimStatus.Submitted
        or ClaimStatus.Pending;

    /// <summary>What the patient is left owing after the fund's decision.</summary>
    public decimal Gap => AmountClaimed - AmountApproved;
}

/// <summary>A visit that could be invoiced.</summary>
/// <param name="Reason">What the visit was for, to identify it in a list.</param>
public sealed record BillableVisit(
    Guid AppointmentId, DateTime StartLocal, string Reason, string ProviderName);

/// <summary>
/// One catalogue item at the price a given site charges for it.
/// </summary>
/// <param name="Fee">
/// The effective fee — this site's override where there is one, the practice fee where
/// there is not. The one number a quote or an invoice line should ever read.
/// </param>
/// <param name="IsSitePrice">
/// True where <paramref name="Fee"/> came from an override. Worth surfacing: a receptionist
/// quoting from this list needs to know the figure is local, because the colleague at the
/// other site reading the same item number will read a different number.
/// </param>
/// <param name="Variations">
/// Every site charging something other than the practice fee, named. Carried on the row
/// rather than fetched per item by the screen: the catalogue's whole job is to show the
/// practice's schedule and its exceptions together, and an item whose exceptions are one
/// lazy load away is an item that gets quoted wrong.
/// </param>
public sealed record CatalogueItem(
    ProcedureCode Code,
    decimal Fee,
    bool IsSitePrice,
    IReadOnlyList<SitePrice> Variations)
{
    public string ItemNumber => Code.ItemNumber;

    public string Description => Code.Description;

    /// <summary>True where at least one site does not charge the practice fee.</summary>
    public bool HasVariations => Variations.Count > 0;
}

/// <summary>One site's price for an item, for the exception list on the catalogue.</summary>
public sealed record SitePrice(Guid LocationId, string LocationName, decimal Fee);

/// <summary>
/// One catalogue item with every site's price beside the practice fee — what the item
/// editor binds to.
/// </summary>
/// <remarks>
/// Every site, including the ones charging the practice fee. A list of only the overrides
/// would answer "which sites differ" but not "what does Newtown charge", and the second
/// question is the one somebody has the screen open to settle.
/// </remarks>
public sealed record CatalogueEntry(
    ProcedureCode Code, IReadOnlyList<CatalogueSiteFee> Sites)
{
    /// <summary>How many sites price this item for themselves.</summary>
    public int OverrideCount => Sites.Count(site => site.SiteFee is not null);
}

/// <summary>
/// What one site charges for one item.
/// </summary>
/// <param name="SiteFee">
/// Null where the site has no override and simply charges the practice fee. Null rather
/// than a copy of the practice fee: a copy would stop tracking the practice fee the moment
/// it moved, and the site would be left on last year's price without anyone choosing that.
/// </param>
/// <param name="EffectiveFee">What this site actually charges, for display.</param>
public sealed record CatalogueSiteFee(
    Guid LocationId,
    string LocationName,
    bool IsActive,
    decimal? SiteFee,
    decimal EffectiveFee);

/// <summary>
/// Invoices, payments, claims, debtors and the day's banking.
/// </summary>
public interface IBillingService
{
    /// <summary>Invoices raised on one day at one location.</summary>
    Task<IReadOnlyList<InvoiceRow>> GetDayAsync(
        Guid locationId, DateOnly day, CancellationToken ct = default);

    Task<InvoiceDetail?> GetInvoiceAsync(Guid invoiceId, CancellationToken ct = default);

    /// <summary>
    /// Records a payment against an invoice.
    /// </summary>
    /// <param name="amount">
    /// Positive. Null takes whatever is still outstanding, which is the common case.
    /// </param>
    /// <returns>Null on success, or why it was refused.</returns>
    Task<string?> TakePaymentAsync(
        Guid invoiceId,
        PaymentMethod method,
        decimal? amount = null,
        Guid? receivedByProviderId = null,
        string? reference = null,
        CancellationToken ct = default);

    /// <summary>
    /// Reduces what the patient owes, because the practice should not have charged it.
    /// </summary>
    /// <remarks>
    /// A credit note is not a write-off and not a refund. It says the invoice was wrong —
    /// so the total comes down and the patient never owed the money. Conflating the three
    /// is how a practice loses track of what it actually billed.
    /// </remarks>
    Task<string?> CreditAsync(
        Guid invoiceId, decimal amount, string reason, CancellationToken ct = default);

    /// <summary>
    /// Gives up on collecting the balance. The invoice stands; the debt does not.
    /// </summary>
    Task<string?> WriteOffAsync(Guid invoiceId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Hands money back that was taken.
    /// </summary>
    /// <remarks>
    /// Recorded as a negative payment against the invoice rather than a status change, so
    /// the ledger still adds up: a refund that did not reduce the paid total would leave a
    /// refunded patient looking settled.
    /// </remarks>
    Task<string?> RefundAsync(
        Guid invoiceId, decimal amount, string reason, Guid? byProviderId = null,
        CancellationToken ct = default);

    /// <summary>Every patient with money outstanding, oldest debt first.</summary>
    Task<IReadOnlyList<DebtorRow>> GetDebtorsAsync(
        Guid locationId, CancellationToken ct = default);

    Task<BankingSheet> GetBankingSheetAsync(
        Guid locationId, DateOnly day, CancellationToken ct = default);

    Task<IReadOnlyList<ClaimRow>> GetClaimsAsync(
        Guid locationId, CancellationToken ct = default);

    /// <summary>
    /// Records a fund's decision on a claim.
    /// </summary>
    /// <param name="approved">
    /// What the fund actually paid. Less than claimed leaves the difference as the
    /// patient's gap, and a benefit payment is recorded for the approved part.
    /// </param>
    Task<string?> AssessClaimAsync(
        Guid claimId, decimal approved, string? message, CancellationToken ct = default);

    /// <summary>Puts a rejected claim back to draft so it can be corrected and sent again.</summary>
    Task<string?> ReopenClaimAsync(Guid claimId, CancellationToken ct = default);

    /// <summary>
    /// The practice's item catalogue, priced for one site.
    /// </summary>
    /// <param name="locationId">
    /// The site whose prices to apply. <see cref="Guid.Empty"/> prices everything at the
    /// practice fee — which is what the catalogue screen wants, since it shows the
    /// practice-wide list and names the exceptions separately.
    /// </param>
    /// <param name="includeWithdrawn">
    /// True to include items that can no longer be charged. The catalogue screen needs
    /// them — withdrawing one is reversible and there has to be a way back — while a
    /// picker adding a line to an invoice must not offer them.
    /// </param>
    Task<IReadOnlyList<CatalogueItem>> GetCatalogueAsync(
        Guid locationId = default,
        bool includeWithdrawn = false,
        CancellationToken ct = default);

    /// <summary>One catalogue item with every site's price, for the editor.</summary>
    Task<CatalogueEntry?> GetCatalogueEntryAsync(Guid codeId, CancellationToken ct = default);

    /// <summary>
    /// Adds a catalogue item, or saves changes to one.
    /// </summary>
    /// <param name="draft">
    /// A new item when <see cref="EntityBase.Id"/> is empty, otherwise the item to update.
    /// </param>
    /// <returns>Null on success, or why it was refused.</returns>
    Task<string?> SaveCatalogueItemAsync(ProcedureCode draft, CancellationToken ct = default);

    /// <summary>
    /// Sets what one site charges for one item, or clears the override.
    /// </summary>
    /// <param name="fee">
    /// The site's price, or null to put the site back on the practice fee. Null clears the
    /// row rather than storing the practice fee into it, so the site keeps following future
    /// changes to the practice schedule.
    /// </param>
    Task<string?> SetSiteFeeAsync(
        Guid codeId, Guid locationId, decimal? fee, CancellationToken ct = default);

    /// <summary>
    /// Withdraws an item from the catalogue, or puts it back.
    /// </summary>
    /// <remarks>
    /// Withdrawn, not deleted. Invoice lines name the item they were raised from, and
    /// removing the row would leave historical invoices citing an item nobody can look up.
    /// A withdrawn item stops being offered and stays readable.
    /// </remarks>
    Task<string?> SetCatalogueItemActiveAsync(
        Guid codeId, bool isActive, CancellationToken ct = default);

    /// <summary>
    /// Starts a draft invoice for a patient, optionally against a visit.
    /// </summary>
    /// <remarks>
    /// A draft, not an issued invoice. Nothing has been billed until it is issued, which
    /// is why a draft can be freely edited and thrown away and an issued one cannot.
    /// </remarks>
    Task<Guid> CreateDraftAsync(
        Guid locationId,
        Guid patientId,
        Guid? appointmentId = null,
        Guid? providerId = null,
        CancellationToken ct = default);

    /// <summary>The patient's open draft for this location, if there is one.</summary>
    Task<Guid?> FindOpenDraftAsync(
        Guid locationId, Guid patientId, CancellationToken ct = default);

    /// <summary>Adds a catalogue item to a draft at its listed fee.</summary>
    Task<string?> AddLineAsync(
        Guid invoiceId,
        Guid procedureCodeId,
        string? toothNumber = null,
        CancellationToken ct = default);

    Task<string?> RemoveLineAsync(Guid lineId, CancellationToken ct = default);

    /// <summary>Changes a draft line's quantity, fee or discount.</summary>
    Task<string?> UpdateLineAsync(
        Guid lineId, int quantity, decimal unitFee, decimal discount,
        CancellationToken ct = default);

    /// <summary>
    /// Issues the draft: assigns its number, dates it, and makes it billable.
    /// </summary>
    /// <param name="termDays">Days until payment is due. Zero means due today.</param>
    Task<string?> IssueInvoiceAsync(
        Guid invoiceId, int termDays = 0, CancellationToken ct = default);

    /// <summary>Discards a draft. Refused once issued — void or credit it instead.</summary>
    Task<string?> DiscardDraftAsync(Guid invoiceId, CancellationToken ct = default);

    /// <summary>
    /// Moves a draft to a different patient.
    /// </summary>
    /// <remarks>
    /// Draft only, and it clears any attached visit — the visit belonged to the patient
    /// being moved away from. Offered because the alternative was discarding the draft and
    /// retyping every line, which is what the prescribing screen's "Change patient" avoids.
    /// </remarks>
    Task<string?> MoveDraftToPatientAsync(
        Guid invoiceId, Guid patientId, CancellationToken ct = default);

    /// <summary>
    /// Ties a draft to the visit it bills, or unties it when null.
    /// </summary>
    /// <remarks>
    /// Draft only. Once issued, the visit an invoice bills is part of what was billed, and
    /// re-pointing it would make the claim and the clinical record disagree about which
    /// treatment was charged.
    /// </remarks>
    Task<string?> AttachVisitAsync(
        Guid invoiceId, Guid? appointmentId, CancellationToken ct = default);

    /// <summary>
    /// The patient's recent visits a new invoice could be attached to.
    /// </summary>
    /// <remarks>
    /// Completed and in-progress visits only, and only those not already invoiced — the
    /// point of attaching one is to bill work that has been done and not yet charged.
    /// </remarks>
    Task<IReadOnlyList<BillableVisit>> GetBillableVisitsAsync(
        Guid locationId, Guid patientId, CancellationToken ct = default);
}

/// <inheritdoc cref="IBillingService"/>
public sealed class BillingService : IBillingService
{
    private readonly IRepository<Invoice> _invoices;
    private readonly IRepository<InvoiceLine> _lines;
    private readonly IRepository<Payment> _payments;
    private readonly IRepository<Claim> _claims;
    private readonly IRepository<PatientEntity> _patients;
    private readonly IRepository<ProcedureCode> _codes;
    private readonly IRepository<ProcedureCodeFee> _siteFees;
    private readonly IRepository<PracticeLocation> _locations;
    private readonly IRepository<Appointment> _appointments;
    private readonly IRepository<Provider> _providers;
    private readonly IRepository<AuditEntry> _audit;
    private readonly ISessionService _session;
    private readonly IPracticeGuard _guard;
    private readonly IClock _clock;

    public BillingService(
        IRepository<Invoice> invoices,
        IRepository<InvoiceLine> lines,
        IRepository<Payment> payments,
        IRepository<Claim> claims,
        IRepository<PatientEntity> patients,
        IRepository<ProcedureCode> codes,
        IRepository<ProcedureCodeFee> siteFees,
        IRepository<PracticeLocation> locations,
        IRepository<Appointment> appointments,
        IRepository<Provider> providers,
        IRepository<AuditEntry> audit,
        ISessionService session,
        IPracticeGuard guard,
        IClock clock)
    {
        _invoices = invoices;
        _lines = lines;
        _payments = payments;
        _claims = claims;
        _patients = patients;
        _codes = codes;
        _siteFees = siteFees;
        _locations = locations;
        _appointments = appointments;
        _providers = providers;
        _audit = audit;
        _session = session;
        _guard = guard;
        _clock = clock;
    }

    public async Task<IReadOnlyList<InvoiceRow>> GetDayAsync(
        Guid locationId, DateOnly day, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = LocalDayToUtc(day);

        var invoices = await _invoices
            .ListAsync(invoice => invoice.PracticeLocationId == locationId, ct)
            .ConfigureAwait(false);

        // Issued today, or still a draft raised today. Filtered in memory because the
        // date to compare is the issue timestamp when there is one and the created
        // timestamp when there is not, which SQL cannot express as one index-friendly
        // predicate.
        var today = invoices
            .Where(invoice => Between(invoice.IssuedUtc ?? invoice.CreatedUtc, fromUtc, toUtc))
            .ToList();

        return await DescribeAsync(today, ct).ConfigureAwait(false);
    }

    public async Task<InvoiceDetail?> GetInvoiceAsync(
        Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await _invoices.GetByIdAsync(invoiceId, ct).ConfigureAwait(false);
        if (invoice is null) return null;

        var lines = await _lines
            .ListAsync(line => line.InvoiceId == invoiceId, ct)
            .ConfigureAwait(false);

        var claims = await _claims
            .ListAsync(claim => claim.InvoiceId == invoiceId, ct)
            .ConfigureAwait(false);

        var payments = await _payments
            .ListAsync(payment => payment.InvoiceId == invoiceId, ct)
            .ConfigureAwait(false);

        var patient = await _patients.GetByIdAsync(invoice.PatientId, ct).ConfigureAwait(false);

        var ordered = payments.OrderBy(payment => payment.ReceivedUtc).ToList();

        return new InvoiceDetail(
            invoice,
            patient?.FullName ?? "Unknown patient",
            lines.OrderBy(line => line.ItemNumber).ToList(),
            claims.OrderByDescending(claim => claim.CreatedUtc).FirstOrDefault(),
            ordered,
            EffectiveStatus(invoice, Round(ordered.Sum(payment => payment.Amount))));
    }

    public async Task<string?> TakePaymentAsync(
        Guid invoiceId,
        PaymentMethod method,
        decimal? amount = null,
        Guid? receivedByProviderId = null,
        string? reference = null,
        CancellationToken ct = default)
    {
        var detail = await GetInvoiceAsync(invoiceId, ct).ConfigureAwait(false);
        if (detail is null) return "That invoice no longer exists.";

        if (detail.Invoice.Status == InvoiceStatus.Voided)
        {
            return "That invoice is voided. Nothing can be taken against it.";
        }

        var due = detail.Outstanding;

        if (due <= 0m) return "That invoice is already settled.";

        var taking = Round(amount ?? due);

        if (taking <= 0m) return "Enter an amount greater than zero.";

        // Overpayment is refused rather than absorbed. The excess would have to become
        // account credit, and there is nowhere to hold it yet — taking the money with no
        // record of owing it back is the worse of the two failures.
        if (taking > due)
        {
            return $"That is more than the {Money(due)} outstanding. "
                + "Account credit is not built yet, so take the exact amount.";
        }

        await _payments
            .SaveAsync(
                new Payment
                {
                    PatientId = detail.Invoice.PatientId,
                    PracticeLocationId = detail.Invoice.PracticeLocationId,
                    InvoiceId = invoiceId,
                    Method = method,
                    Amount = taking,
                    ReceivedUtc = _clock.UtcNow,
                    Reference = reference,
                    ReceivedByProviderId = receivedByProviderId,
                },
                ct)
            .ConfigureAwait(false);

        await SettleAsync(invoiceId, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> CreditAsync(
        Guid invoiceId, decimal amount, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "A credit note needs a reason — it changes what the practice billed.";
        }

        var detail = await GetInvoiceAsync(invoiceId, ct).ConfigureAwait(false);
        if (detail is null) return "That invoice no longer exists.";

        var credit = Round(amount);

        if (credit <= 0m) return "Enter an amount greater than zero.";

        if (credit > detail.Outstanding)
        {
            // Crediting below what has been paid would leave the patient owed money, which
            // is a refund and not a credit note. Kept separate so the two do not blur.
            return $"A credit note cannot exceed the {Money(detail.Outstanding)} still "
                + "outstanding. To hand money back, use a refund.";
        }

        var invoice = detail.Invoice;

        invoice.Subtotal = Round(invoice.Subtotal - credit);
        invoice.Total = Round(invoice.Total - credit);
        invoice.AdjustmentReason = Append(invoice.AdjustmentReason,
            $"Credit note {Money(credit)}: {reason.Trim()}");

        await _invoices.SaveAsync(invoice, ct).ConfigureAwait(false);
        await SettleAsync(invoiceId, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> WriteOffAsync(
        Guid invoiceId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "A write-off needs a reason — it gives up money the practice is owed.";
        }

        var detail = await GetInvoiceAsync(invoiceId, ct).ConfigureAwait(false);
        if (detail is null) return "That invoice no longer exists.";

        if (detail.IsSettled) return "That invoice is settled. There is nothing to write off.";

        var invoice = detail.Invoice;

        // The total is left alone on purpose. A write-off is not a correction: the practice
        // did the work and billed it correctly, and reducing the invoice would erase that
        // from the production figures along with the debt.
        invoice.Status = InvoiceStatus.WrittenOff;
        invoice.AdjustmentReason = Append(invoice.AdjustmentReason,
            $"Written off {Money(detail.Outstanding)}: {reason.Trim()}");

        await _invoices.SaveAsync(invoice, ct).ConfigureAwait(false);
        await RefreshPatientBalanceAsync(invoice.PatientId, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> RefundAsync(
        Guid invoiceId, decimal amount, string reason, Guid? byProviderId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "A refund needs a reason.";

        var detail = await GetInvoiceAsync(invoiceId, ct).ConfigureAwait(false);
        if (detail is null) return "That invoice no longer exists.";

        var refund = Round(amount);

        if (refund <= 0m) return "Enter an amount greater than zero.";

        // Only money actually taken can be handed back. Refunding more than was paid would
        // be inventing a payment to reverse.
        if (refund > detail.Paid)
        {
            return $"Only {Money(detail.Paid)} has been paid on this invoice.";
        }

        // The most recent receipt this refund reverses, so the two can be tied together on
        // the ledger. Best effort: a refund may span several receipts.
        var reversing = detail.Payments
            .Where(payment => payment.Amount > 0m)
            .OrderByDescending(payment => payment.ReceivedUtc)
            .FirstOrDefault();

        await _payments
            .SaveAsync(
                new Payment
                {
                    PatientId = detail.Invoice.PatientId,
                    PracticeLocationId = detail.Invoice.PracticeLocationId,
                    InvoiceId = invoiceId,
                    Method = PaymentMethod.Refund,

                    // Negative, so every total that sums payments stays correct without
                    // knowing what a refund is.
                    Amount = -refund,
                    ReceivedUtc = _clock.UtcNow,
                    ReceivedByProviderId = byProviderId,
                    ReversesPaymentId = reversing?.Id,
                    Notes = reason.Trim(),
                },
                ct)
            .ConfigureAwait(false);

        await SettleAsync(invoiceId, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<IReadOnlyList<DebtorRow>> GetDebtorsAsync(
        Guid locationId, CancellationToken ct = default)
    {
        var invoices = await _invoices
            .ListAsync(invoice => invoice.PracticeLocationId == locationId, ct)
            .ConfigureAwait(false);

        // Written-off and voided debt is not chased. It is still on the record; it is just
        // not money the practice is trying to collect.
        var chasing = invoices
            .Where(invoice => invoice.Status is not
                (InvoiceStatus.Voided or InvoiceStatus.WrittenOff or InvoiceStatus.Draft))
            .ToList();

        if (chasing.Count == 0) return [];

        var ids = chasing.Select(invoice => invoice.Id).ToHashSet();

        var payments = await _payments
            .ListAsync(payment => payment.InvoiceId != null
                && ids.Contains(payment.InvoiceId.Value), ct)
            .ConfigureAwait(false);

        var paidByInvoice = payments
            .GroupBy(payment => payment.InvoiceId!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(payment => payment.Amount));

        var patientIds = chasing.Select(invoice => invoice.PatientId).ToHashSet();

        var patients = await _patients
            .ListAsync(patient => patientIds.Contains(patient.Id), ct)
            .ConfigureAwait(false);

        var names = patients.ToDictionary(patient => patient.Id, patient => patient.FullName);

        var today = _clock.Today;

        var rows = chasing
            .Select(invoice => new
            {
                invoice.PatientId,
                Outstanding = Round(invoice.Total - paidByInvoice.GetValueOrDefault(invoice.Id)),

                // Aged from the due date, not the invoice date. A 30-day account is not
                // overdue on day one, and ageing from issue would put every current
                // account into a chasing bucket.
                DaysOverdue = invoice.DueOn is { } due ? today.DayNumber - due.DayNumber : 0,
            })
            .Where(entry => entry.Outstanding > 0m)
            .GroupBy(entry => entry.PatientId)
            .Select(group => new DebtorRow(
                group.Key,
                names.GetValueOrDefault(group.Key, "Unknown patient"),
                group.Where(entry => entry.DaysOverdue < 30).Sum(entry => entry.Outstanding),
                group.Where(entry => entry.DaysOverdue is >= 30 and < 60)
                    .Sum(entry => entry.Outstanding),
                group.Where(entry => entry.DaysOverdue is >= 60 and < 90)
                    .Sum(entry => entry.Outstanding),
                group.Where(entry => entry.DaysOverdue >= 90).Sum(entry => entry.Outstanding)))
            .ToList();

        // Oldest money first — that is the order the phone calls get made in.
        return rows
            .OrderByDescending(row => row.Days90Plus)
            .ThenByDescending(row => row.Days60)
            .ThenByDescending(row => row.Total)
            .ToList();
    }

    public async Task<BankingSheet> GetBankingSheetAsync(
        Guid locationId, DateOnly day, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = LocalDayToUtc(day);

        var payments = await _payments
            .ListAsync(payment => payment.PracticeLocationId == locationId
                && payment.ReceivedUtc >= fromUtc && payment.ReceivedUtc < toUtc, ct)
            .ConfigureAwait(false);

        var lines = payments
            .GroupBy(payment => payment.Method)
            .Select(group => new TakingsLine(
                group.Key,
                group.Count(),
                Round(group.Sum(payment => payment.Amount))))
            .OrderBy(line => line.Method)
            .ToList();

        return new BankingSheet(
            day,
            lines,
            Round(-payments.Where(payment => payment.Amount < 0m)
                .Sum(payment => payment.Amount)));
    }

    public async Task<IReadOnlyList<ClaimRow>> GetClaimsAsync(
        Guid locationId, CancellationToken ct = default)
    {
        var invoices = await _invoices
            .ListAsync(invoice => invoice.PracticeLocationId == locationId, ct)
            .ConfigureAwait(false);

        if (invoices.Count == 0) return [];

        var invoiceIds = invoices.Select(invoice => invoice.Id).ToHashSet();

        var claims = await _claims
            .ListAsync(claim => invoiceIds.Contains(claim.InvoiceId), ct)
            .ConfigureAwait(false);

        if (claims.Count == 0) return [];

        var lines = await _lines
            .ListAsync(line => invoiceIds.Contains(line.InvoiceId), ct)
            .ConfigureAwait(false);

        var patientIds = claims.Select(claim => claim.PatientId).ToHashSet();

        var patients = await _patients
            .ListAsync(patient => patientIds.Contains(patient.Id), ct)
            .ConfigureAwait(false);

        var names = patients.ToDictionary(patient => patient.Id, patient => patient.FullName);

        return claims
            .OrderBy(claim => claim.Status == ClaimStatus.Rejected ? 0 : 1)
            .ThenByDescending(claim => claim.SubmittedUtc ?? claim.CreatedUtc)
            .Select(claim => new ClaimRow(
                claim.Id,
                claim.InvoiceId,
                claim.PatientId,
                names.GetValueOrDefault(claim.PatientId, "Unknown patient"),

                // Item numbers, which is what a fund's rejection quotes back.
                string.Join(", ", lines
                    .Where(line => line.InvoiceId == claim.InvoiceId)
                    .Select(line => line.ItemNumber)
                    .Distinct()),
                claim.Type,
                claim.PayerName,
                claim.AmountClaimed,
                claim.AmountApproved,
                claim.Status,
                claim.AssessmentMessage))
            .ToList();
    }

    public async Task<string?> AssessClaimAsync(
        Guid claimId, decimal approved, string? message, CancellationToken ct = default)
    {
        var claim = await _claims.GetByIdAsync(claimId, ct).ConfigureAwait(false);
        if (claim is null) return "That claim no longer exists.";

        var amount = Round(approved);

        if (amount < 0m) return "An approved amount cannot be negative.";

        if (amount > claim.AmountClaimed)
        {
            return $"A fund cannot approve more than the {Money(claim.AmountClaimed)} claimed.";
        }

        claim.AmountApproved = amount;
        claim.AssessedUtc = _clock.UtcNow;
        claim.AssessmentMessage = message;

        claim.Status = amount == 0m
            ? ClaimStatus.Rejected
            : amount < claim.AmountClaimed
                ? ClaimStatus.PartiallyApproved
                : ClaimStatus.Approved;

        await _claims.SaveAsync(claim, ct).ConfigureAwait(false);

        // The approved part is money the fund pays, so it goes on the ledger as a benefit
        // payment. Without this the patient shows as owing the fund's share as well as
        // their own gap.
        if (amount > 0m)
        {
            var existing = await _payments
                .ListAsync(payment => payment.ClaimId == claimId, ct)
                .ConfigureAwait(false);

            var alreadyPaid = existing.Sum(payment => payment.Amount);
            var toPost = Round(amount - alreadyPaid);

            if (toPost > 0m)
            {
                await _payments
                    .SaveAsync(
                        new Payment
                        {
                            PatientId = claim.PatientId,
                            PracticeLocationId = await LocationOfAsync(claim.InvoiceId, ct)
                                .ConfigureAwait(false),
                            InvoiceId = claim.InvoiceId,
                            ClaimId = claimId,
                            Method = BenefitMethod(claim.Type),
                            Amount = toPost,
                            ReceivedUtc = _clock.UtcNow,
                            Reference = claim.PayerReference,
                        },
                        ct)
                    .ConfigureAwait(false);
            }
        }

        await SettleAsync(claim.InvoiceId, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> ReopenClaimAsync(Guid claimId, CancellationToken ct = default)
    {
        var claim = await _claims.GetByIdAsync(claimId, ct).ConfigureAwait(false);
        if (claim is null) return "That claim no longer exists.";

        if (claim.Status != ClaimStatus.Rejected)
        {
            return "Only a rejected claim needs correcting.";
        }

        claim.Status = ClaimStatus.Draft;
        claim.SubmittedUtc = null;
        claim.AssessedUtc = null;

        // The rejection message is kept. It says why the fund refused, which is the one
        // thing needed to fix the claim, and clearing it would leave the corrector guessing.
        await _claims.SaveAsync(claim, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<IReadOnlyList<CatalogueItem>> GetCatalogueAsync(
        Guid locationId = default,
        bool includeWithdrawn = false,
        CancellationToken ct = default)
    {
        var codes = includeWithdrawn
            ? await _codes.ListAsync(ct: ct).ConfigureAwait(false)
            : await _codes.ListAsync(code => code.IsActive, ct).ConfigureAwait(false);

        // Every override in the practice, read once. Not one read per item: the ADA
        // schedule runs to several hundred items, and a per-item lookup would be several
        // hundred round trips to price one screen. The override table is small by
        // construction — it holds only the deliberate exceptions — so reading all of it is
        // cheaper than reading a slice of it per row.
        var overrides = await _siteFees.ListAsync(ct: ct).ConfigureAwait(false);

        // Short names — "Newtown", not "Molargo Dental — Newtown". The full name repeats
        // the practice's own name on every row of a column that is already narrow, and
        // three of those stacked in one cell pushed the fee off the side of the table.
        var siteNames = overrides.Count == 0
            ? []
            : (await _locations.ListAsync(ct: ct).ConfigureAwait(false))
                .ToDictionary(site => site.Id, SiteLabel);

        var byItem = overrides
            .GroupBy(fee => fee.ProcedureCodeId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var priced = new List<CatalogueItem>(codes.Count);

        // Ordinal, not culture-aware: item numbers are strings so leading zeros survive,
        // and a culture sort orders "011" against "11" differently from machine to machine.
        foreach (var code in codes.OrderBy(code => code.ItemNumber, StringComparer.Ordinal))
        {
            var rows = byItem.TryGetValue(code.Id, out var found)
                ? found
                : new List<ProcedureCodeFee>();

            // Only the rows that actually differ. An override left equal to the practice
            // fee is not an exception worth listing, and showing it as one would put
            // "Newtown $65" beside a practice fee of $65 on the exception column.
            var variations = rows
                .Where(row => row.Fee != code.Fee)
                .Select(row => new SitePrice(
                    row.PracticeLocationId,
                    siteNames.TryGetValue(row.PracticeLocationId, out var name)
                        ? name
                        : "A closed site",
                    row.Fee))
                .OrderBy(row => row.LocationName, StringComparer.CurrentCulture)
                .ToList();

            var mine = locationId == Guid.Empty
                ? null
                : rows.FirstOrDefault(row => row.PracticeLocationId == locationId);

            priced.Add(mine is { } siteFee
                ? new CatalogueItem(code, siteFee.Fee, IsSitePrice: true, variations)
                : new CatalogueItem(code, code.Fee, IsSitePrice: false, variations));
        }

        return priced;
    }

    public async Task<CatalogueEntry?> GetCatalogueEntryAsync(
        Guid codeId, CancellationToken ct = default)
    {
        var code = await _codes.GetByIdAsync(codeId, ct).ConfigureAwait(false);
        if (code is null) return null;

        var sites = await _locations.ListAsync(ct: ct).ConfigureAwait(false);

        var overrides = await _siteFees
            .ListAsync(fee => fee.ProcedureCodeId == codeId, ct)
            .ConfigureAwait(false);

        var byLocation = overrides.ToDictionary(
            fee => fee.PracticeLocationId, fee => fee.Fee);

        // Closed sites are listed too, and flagged. A site that closed still has invoices
        // against it, and dropping its row would make a price somebody is asked about
        // unfindable — while showing it unflagged would invite editing a price nothing can
        // charge any more.
        var rows = sites
            .OrderBy(site => site.DisplayOrder)
            .ThenBy(site => site.Name, StringComparer.CurrentCulture)
            .Select(site => byLocation.TryGetValue(site.Id, out var fee)
                ? new CatalogueSiteFee(site.Id, SiteLabel(site), site.IsActive, fee, fee)
                : new CatalogueSiteFee(
                    site.Id, SiteLabel(site), site.IsActive, null, code.Fee))
            .ToList();

        return new CatalogueEntry(code, rows);
    }

    public async Task<string?> SaveCatalogueItemAsync(
        ProcedureCode draft, CancellationToken ct = default)
    {
        var denied = await _guard
            .RefuseAsync(PracticePermissions.ManagePricing, ct)
            .ConfigureAwait(false);

        if (denied is not null) return denied;

        var itemNumber = draft.ItemNumber?.Trim() ?? string.Empty;
        var description = draft.Description?.Trim() ?? string.Empty;

        if (itemNumber.Length == 0) return "An item needs a number.";
        if (description.Length == 0) return "An item needs the schedule's description.";

        // Negative is refused; zero is not. A no-charge item is a real thing — a courtesy
        // review, a warranty re-cement — and refusing zero would push the practice into
        // entering a cent to get the line onto the invoice.
        if (draft.Fee < 0m) return "A fee cannot be negative.";

        // Trimmed before comparing, so " 011" cannot slip past a check that the untrimmed
        // value would have passed and then land as a second 011 nobody can tell apart.
        var clash = await _codes
            .FindAsync(code => code.ItemNumber == itemNumber, ct)
            .ConfigureAwait(false);

        if (clash is not null && clash.Id != draft.Id)
        {
            return clash.IsActive
                ? $"Item {itemNumber} is already in the catalogue — {clash.Description}."
                : $"Item {itemNumber} exists but is withdrawn — {clash.Description}. "
                    + "Restore it rather than adding a second one.";
        }

        var isNew = draft.Id == Guid.Empty;

        var code = isNew
            ? new ProcedureCode()
            : await _codes.GetByIdAsync(draft.Id, ct).ConfigureAwait(false);

        if (code is null) return "That item no longer exists.";

        var wasFee = code.Fee;
        var wasNumber = code.ItemNumber;

        code.ItemNumber = itemNumber;
        code.Description = description;
        code.PatientFriendlyName = Blank(draft.PatientFriendlyName);
        code.Category = Blank(draft.Category);
        code.Fee = draft.Fee;
        code.IsPerTooth = draft.IsPerTooth;
        code.RequiresSurface = draft.RequiresSurface;
        code.IsCdbsEligible = draft.IsCdbsEligible;
        code.TypicalDurationMinutes = draft.TypicalDurationMinutes is > 0
            ? draft.TypicalDurationMinutes
            : null;

        if (isNew) code.IsActive = true;

        var id = await _codes.SaveAsync(code, ct).ConfigureAwait(false);

        // Stamped back onto the caller's object. On the update path draft and code are the
        // same row, but a new item is built here as a fresh entity — so without this the
        // caller was left holding Guid.Empty for a row that now exists, and the very next
        // call, setting that item's per-site fees, was refused with "that item no longer
        // exists" against an item created a millisecond earlier.
        draft.Id = id;

        // The old fee is named in the entry, not just the new one. "Changed 011 to $75" is
        // half an answer when the argument is about what it used to be.
        var detail = isNew
            ? $"Added item {itemNumber} — {description}, "
                + $"{MolargoFormat.MoneyExact(code.Fee)} practice fee"
            : wasFee == code.Fee
                ? $"Changed item {wasNumber} — {description}"
                : $"Changed item {wasNumber} fee from {MolargoFormat.MoneyExact(wasFee)} "
                    + $"to {MolargoFormat.MoneyExact(code.Fee)}";

        await RecordAsync(
                isNew ? AuditAction.Created : AuditAction.Updated,
                nameof(ProcedureCode),
                id,
                detail,
                ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string?> SetSiteFeeAsync(
        Guid codeId, Guid locationId, decimal? fee, CancellationToken ct = default)
    {
        var denied = await _guard
            .RefuseAsync(PracticePermissions.ManagePricing, ct)
            .ConfigureAwait(false);

        if (denied is not null) return denied;

        if (fee is < 0m) return "A fee cannot be negative.";

        var code = await _codes.GetByIdAsync(codeId, ct).ConfigureAwait(false);
        if (code is null) return "That item no longer exists.";

        var site = await _locations.GetByIdAsync(locationId, ct).ConfigureAwait(false);
        if (site is null) return "That site no longer exists.";

        var existing = await _siteFees
            .FindAsync(
                row => row.ProcedureCodeId == codeId
                    && row.PracticeLocationId == locationId,
                ct)
            .ConfigureAwait(false);

        if (fee is not { } amount)
        {
            // Nothing to clear is a success, not a refusal: the site already charges the
            // practice fee, which is what the caller asked for.
            if (existing is null) return null;

            await _siteFees.DeleteAsync(existing.Id, ct).ConfigureAwait(false);

            await RecordAsync(
                    AuditAction.Deleted,
                    nameof(ProcedureCodeFee),
                    existing.Id,
                    $"{site.Name} back on the practice fee for item {code.ItemNumber} "
                        + $"({MolargoFormat.MoneyExact(code.Fee)}), was "
                        + MolargoFormat.MoneyExact(existing.Fee),
                    ct)
                .ConfigureAwait(false);

            return null;
        }

        var was = existing?.Fee;

        var row = existing ?? new ProcedureCodeFee
        {
            ProcedureCodeId = codeId,
            PracticeLocationId = locationId,
        };

        row.Fee = amount;

        var id = await _siteFees.SaveAsync(row, ct).ConfigureAwait(false);

        await RecordAsync(
                existing is null ? AuditAction.Created : AuditAction.Updated,
                nameof(ProcedureCodeFee),
                id,
                was is { } previous
                    ? $"{site.Name} fee for item {code.ItemNumber} changed from "
                        + $"{MolargoFormat.MoneyExact(previous)} to "
                        + MolargoFormat.MoneyExact(amount)
                    : $"{site.Name} now charges {MolargoFormat.MoneyExact(amount)} for item "
                        + $"{code.ItemNumber}, against a practice fee of "
                        + MolargoFormat.MoneyExact(code.Fee),
                ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string?> SetCatalogueItemActiveAsync(
        Guid codeId, bool isActive, CancellationToken ct = default)
    {
        var denied = await _guard
            .RefuseAsync(PracticePermissions.ManagePricing, ct)
            .ConfigureAwait(false);

        if (denied is not null) return denied;

        var code = await _codes.GetByIdAsync(codeId, ct).ConfigureAwait(false);
        if (code is null) return "That item no longer exists.";

        if (code.IsActive == isActive) return null;

        code.IsActive = isActive;
        await _codes.SaveAsync(code, ct).ConfigureAwait(false);

        // The site overrides are left in place. Withdrawing is reversible, and deleting
        // them would quietly discard pricing the practice would have to re-enter on the
        // day somebody restored the item.
        await RecordAsync(
                AuditAction.Updated,
                nameof(ProcedureCode),
                code.Id,
                isActive
                    ? $"Restored item {code.ItemNumber} to the catalogue"
                    : $"Withdrew item {code.ItemNumber} — it stays on historical invoices",
                ct)
            .ConfigureAwait(false);

        return null;
    }

    /// <summary>
    /// What one site charges for one item: its override, or the practice fee.
    /// </summary>
    /// <remarks>
    /// The single place the question is answered, so a quote, a picker and an invoice line
    /// cannot disagree about the price. Everything that needs a fee goes through here or
    /// through <see cref="GetCatalogueAsync"/>, which applies the same rule in bulk.
    /// </remarks>
    private async Task<decimal> EffectiveFeeAsync(
        ProcedureCode code, Guid locationId, CancellationToken ct)
    {
        if (locationId == Guid.Empty) return code.Fee;

        var siteFee = await _siteFees
            .FindAsync(
                row => row.ProcedureCodeId == code.Id
                    && row.PracticeLocationId == locationId,
                ct)
            .ConfigureAwait(false);

        return siteFee?.Fee ?? code.Fee;
    }

    /// <summary>
    /// How a site is named on this screen — its short name where it has one.
    /// </summary>
    /// <remarks>
    /// Display only. The audit entries below name the site in full, because a trail read
    /// months later should not depend on a short name that has since been changed.
    /// </remarks>
    private static string SiteLabel(PracticeLocation site) =>
        string.IsNullOrWhiteSpace(site.ShortName) ? site.Name : site.ShortName;

    /// <summary>Null for a blank, so an empty box is stored as absent rather than "".</summary>
    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Writes one audit entry for a catalogue change.
    /// </summary>
    /// <remarks>
    /// A price change is audited for the same reason a staff change is: "who put 011 up to
    /// $75, and when" is a question a practice asks months later, and the invoices alone
    /// cannot answer it — they record what was charged, not who decided it.
    ///
    /// The acting person's name is stamped beside their id, because a provider can be
    /// renamed and the trail has to say who it was at the time.
    /// </remarks>
    private async Task RecordAsync(
        AuditAction action,
        string entityName,
        Guid? entityId,
        string detail,
        CancellationToken ct)
    {
        var providerId = _session.ProviderId;

        var actor = providerId is { } id
            ? await _providers.GetByIdAsync(id, ct).ConfigureAwait(false)
            : null;

        await _audit
            .SaveAsync(
                new AuditEntry
                {
                    Action = action,
                    EntityName = entityName,
                    EntityId = entityId,
                    ProviderId = providerId,
                    ProviderName = actor?.FullName ?? _session.UserDisplayName,
                    OccurredUtc = _clock.UtcNow,
                    Detail = detail,
                },
                ct)
            .ConfigureAwait(false);
    }

    public async Task<Guid> CreateDraftAsync(
        Guid locationId,
        Guid patientId,
        Guid? appointmentId = null,
        Guid? providerId = null,
        CancellationToken ct = default)
    {
        // An existing open draft is reused. Starting a second one leaves the front desk
        // with two half-built invoices for one patient and no way to tell which is real.
        if (await FindOpenDraftAsync(locationId, patientId, ct).ConfigureAwait(false)
            is { } existing)
        {
            return existing;
        }

        var invoice = new Invoice
        {
            PatientId = patientId,
            PracticeLocationId = locationId,
            ProviderId = providerId ?? Guid.Empty,
            AppointmentId = appointmentId,
            Status = InvoiceStatus.Draft,

            // No number yet. Numbers are assigned at issue, so a draft that gets abandoned
            // does not burn one and leave a hole in the sequence.
            InvoiceNumber = null,
            Subtotal = 0m,
            Total = 0m,
            AmountPaid = 0m,
        };

        await _invoices.SaveAsync(invoice, ct).ConfigureAwait(false);
        return invoice.Id;
    }

    public async Task<Guid?> FindOpenDraftAsync(
        Guid locationId, Guid patientId, CancellationToken ct = default)
    {
        var drafts = await _invoices
            .ListAsync(invoice => invoice.PracticeLocationId == locationId
                && invoice.PatientId == patientId
                && invoice.Status == InvoiceStatus.Draft, ct)
            .ConfigureAwait(false);

        return drafts
            .OrderByDescending(invoice => invoice.CreatedUtc)
            .FirstOrDefault()
            ?.Id;
    }

    public async Task<string?> AddLineAsync(
        Guid invoiceId,
        Guid procedureCodeId,
        string? toothNumber = null,
        CancellationToken ct = default)
    {
        var invoice = await _invoices.GetByIdAsync(invoiceId, ct).ConfigureAwait(false);
        if (invoice is null) return "That invoice no longer exists.";

        if (invoice.Status != InvoiceStatus.Draft) return IssuedRefusal;

        var code = await _codes.GetByIdAsync(procedureCodeId, ct).ConfigureAwait(false);
        if (code is null) return "That item is not in the catalogue.";

        // A per-tooth item with no tooth is a line nobody can audit and a claim the fund
        // will reject, so it is refused rather than saved half-filled.
        if (code.IsPerTooth && string.IsNullOrWhiteSpace(toothNumber))
        {
            return $"{code.ItemNumber} is charged per tooth. Give the tooth number.";
        }

        // Priced at the invoice's own site, not the site the person raising it happens to
        // be signed in to. A manager covering Newtown from the CBD desk would otherwise
        // put CBD prices on a Newtown invoice, and the invoice is the only record of which
        // site the work was done at.
        var fee = await EffectiveFeeAsync(code, invoice.PracticeLocationId, ct)
            .ConfigureAwait(false);

        await _lines
            .SaveAsync(
                new InvoiceLine
                {
                    InvoiceId = invoiceId,
                    ProcedureCodeId = code.Id,
                    ItemNumber = code.ItemNumber,
                    Description = code.Description,
                    ToothNumber = string.IsNullOrWhiteSpace(toothNumber)
                        ? null
                        : toothNumber.Trim(),
                    Quantity = 1,
                    UnitFee = fee,
                    LineTotal = fee,
                    ServiceDate = _clock.Today,
                    ProviderId = invoice.ProviderId == Guid.Empty ? null : invoice.ProviderId,
                },
                ct)
            .ConfigureAwait(false);

        await RetotalAsync(invoiceId, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> RemoveLineAsync(Guid lineId, CancellationToken ct = default)
    {
        var line = await _lines.GetByIdAsync(lineId, ct).ConfigureAwait(false);
        if (line is null) return null;

        var invoice = await _invoices.GetByIdAsync(line.InvoiceId, ct).ConfigureAwait(false);

        if (invoice is null || invoice.Status != InvoiceStatus.Draft) return IssuedRefusal;

        await _lines.DeleteAsync(lineId, ct).ConfigureAwait(false);
        await RetotalAsync(line.InvoiceId, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> UpdateLineAsync(
        Guid lineId, int quantity, decimal unitFee, decimal discount,
        CancellationToken ct = default)
    {
        var line = await _lines.GetByIdAsync(lineId, ct).ConfigureAwait(false);
        if (line is null) return "That line no longer exists.";

        var invoice = await _invoices.GetByIdAsync(line.InvoiceId, ct).ConfigureAwait(false);

        if (invoice is null || invoice.Status != InvoiceStatus.Draft) return IssuedRefusal;

        if (unitFee < 0m) return "A fee cannot be negative.";

        line.Quantity = Math.Max(quantity, 1);
        line.UnitFee = Round(unitFee);

        var gross = Round(line.UnitFee * line.Quantity);

        // A discount larger than the line would make it a negative charge — a credit
        // dressed up as a fee, reducing the rest of the invoice with no record of why.
        line.DiscountAmount = Round(Math.Clamp(discount, 0m, gross));
        line.LineTotal = Round(gross - line.DiscountAmount);

        await _lines.SaveAsync(line, ct).ConfigureAwait(false);
        await RetotalAsync(line.InvoiceId, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> IssueInvoiceAsync(
        Guid invoiceId, int termDays = 0, CancellationToken ct = default)
    {
        var invoice = await _invoices.GetByIdAsync(invoiceId, ct).ConfigureAwait(false);
        if (invoice is null) return "That invoice no longer exists.";

        if (invoice.Status != InvoiceStatus.Draft)
        {
            return "That invoice has already been issued.";
        }

        var lines = await _lines
            .ListAsync(line => line.InvoiceId == invoiceId, ct)
            .ConfigureAwait(false);

        if (lines.Count == 0) return "Add at least one item before issuing.";

        invoice.InvoiceNumber = await NextInvoiceNumberAsync(ct).ConfigureAwait(false);
        invoice.IssuedUtc = _clock.UtcNow;
        invoice.DueOn = _clock.Today.AddDays(Math.Max(termDays, 0));
        invoice.Status = InvoiceStatus.Issued;

        await _invoices.SaveAsync(invoice, ct).ConfigureAwait(false);

        // Re-totalled and re-settled from the ledger, so the status lands correctly even
        // if something had already been paid against the draft.
        await RetotalAsync(invoiceId, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> DiscardDraftAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await _invoices.GetByIdAsync(invoiceId, ct).ConfigureAwait(false);
        if (invoice is null) return null;

        if (invoice.Status != InvoiceStatus.Draft)
        {
            return "An issued invoice cannot be discarded. Credit it or void it instead.";
        }

        var lines = await _lines
            .ListAsync(line => line.InvoiceId == invoiceId, ct)
            .ConfigureAwait(false);

        foreach (var line in lines)
        {
            await _lines.DeleteAsync(line.Id, ct).ConfigureAwait(false);
        }

        await _invoices.DeleteAsync(invoiceId, ct).ConfigureAwait(false);
        await RefreshPatientBalanceAsync(invoice.PatientId, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> MoveDraftToPatientAsync(
        Guid invoiceId, Guid patientId, CancellationToken ct = default)
    {
        var invoice = await _invoices.GetByIdAsync(invoiceId, ct).ConfigureAwait(false);
        if (invoice is null) return "That invoice no longer exists.";

        if (invoice.Status != InvoiceStatus.Draft) return IssuedRefusal;

        if (invoice.PatientId == patientId) return null;

        var previous = invoice.PatientId;

        invoice.PatientId = patientId;

        // The visit went with the old patient. Carrying it across would bill one person
        // for another's treatment, which is the worst outcome this screen can produce.
        invoice.AppointmentId = null;

        await _invoices.SaveAsync(invoice, ct).ConfigureAwait(false);

        // Both balances, since the draft's total moves from one to the other.
        await RefreshPatientBalanceAsync(previous, ct).ConfigureAwait(false);
        await RefreshPatientBalanceAsync(patientId, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> AttachVisitAsync(
        Guid invoiceId, Guid? appointmentId, CancellationToken ct = default)
    {
        var invoice = await _invoices.GetByIdAsync(invoiceId, ct).ConfigureAwait(false);
        if (invoice is null) return "That invoice no longer exists.";

        if (invoice.Status != InvoiceStatus.Draft) return IssuedRefusal;

        if (appointmentId is { } wanted)
        {
            var already = await _invoices
                .ListAsync(other => other.AppointmentId == wanted && other.Id != invoiceId, ct)
                .ConfigureAwait(false);

            // One invoice per visit. Two would bill the same treatment twice, and the
            // second is the one nobody notices until the patient queries it.
            if (already.Count > 0)
            {
                return "That visit is already on another invoice.";
            }
        }

        invoice.AppointmentId = appointmentId;

        await _invoices.SaveAsync(invoice, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<IReadOnlyList<BillableVisit>> GetBillableVisitsAsync(
        Guid locationId, Guid patientId, CancellationToken ct = default)
    {
        var appointments = await _appointments
            .ListAsync(appointment => appointment.PracticeLocationId == locationId
                && appointment.PatientId == patientId, ct)
            .ConfigureAwait(false);

        // Treatment that has actually happened. A booking nobody has sat in cannot be
        // billed, and offering it is how a patient is charged for a visit they never had.
        var treated = appointments
            .Where(appointment => appointment.Status is AppointmentStatus.Completed
                or AppointmentStatus.InProgress or AppointmentStatus.Seated)
            .ToList();

        if (treated.Count == 0) return [];

        var invoices = await _invoices
            .ListAsync(invoice => invoice.PatientId == patientId, ct)
            .ConfigureAwait(false);

        // Already-invoiced visits drop out. Billing one twice is the mistake this list
        // exists to prevent, not one it should offer.
        var invoiced = invoices
            .Where(invoice => invoice.AppointmentId is not null
                && invoice.Status != InvoiceStatus.Draft)
            .Select(invoice => invoice.AppointmentId!.Value)
            .ToHashSet();

        var providers = await _providers.ListAsync(ct: ct).ConfigureAwait(false);
        var names = providers.ToDictionary(provider => provider.Id, provider => provider.FullName);

        return treated
            .Where(appointment => !invoiced.Contains(appointment.Id))
            .OrderByDescending(appointment => appointment.StartUtc)
            .Take(10)
            .Select(appointment => new BillableVisit(
                appointment.Id,
                appointment.StartUtc.ToLocalTime(),
                appointment.Reason ?? "Appointment",
                names.GetValueOrDefault(appointment.ProviderId, "Unassigned")))
            .ToList();
    }

    // ---- helpers ---------------------------------------------------------

    private const string IssuedRefusal =
        "That invoice has been issued and cannot be edited. Raise a credit note and a " +
        "corrected invoice instead.";

    /// <summary>
    /// Re-sums the invoice from its lines, then re-settles it.
    /// </summary>
    /// <remarks>
    /// The total is never typed. An invoice whose header disagrees with the lines under it
    /// is the one thing a billing screen must not show, and the only way to guarantee that
    /// is to derive the header every time a line moves.
    /// </remarks>
    private async Task RetotalAsync(Guid invoiceId, CancellationToken ct)
    {
        var invoice = await _invoices.GetByIdAsync(invoiceId, ct).ConfigureAwait(false);
        if (invoice is null) return;

        var lines = await _lines
            .ListAsync(line => line.InvoiceId == invoiceId, ct)
            .ConfigureAwait(false);

        invoice.Subtotal = Round(lines.Sum(line => Round(line.UnitFee * line.Quantity)));
        invoice.DiscountAmount = Round(lines.Sum(line => line.DiscountAmount));
        invoice.Total = Round(lines.Sum(line => line.LineTotal));

        await _invoices.SaveAsync(invoice, ct).ConfigureAwait(false);
        await SettleAsync(invoiceId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The next invoice number.
    /// </summary>
    /// <remarks>
    /// One past the highest in use, read at issue time. Non-numeric numbers are ignored
    /// rather than crashing the issue: an imported ledger may carry prefixed numbers this
    /// practice never used, and refusing to invoice because of one is worse than skipping it.
    /// </remarks>
    private async Task<string> NextInvoiceNumberAsync(CancellationToken ct)
    {
        var invoices = await _invoices.ListAsync(ct: ct).ConfigureAwait(false);

        var highest = invoices
            .Select(invoice => int.TryParse(invoice.InvoiceNumber, out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();

        return (Math.Max(highest, 10000) + 1).ToString();
    }


    private async Task<IReadOnlyList<InvoiceRow>> DescribeAsync(
        IReadOnlyList<Invoice> invoices, CancellationToken ct)
    {
        if (invoices.Count == 0) return [];

        var ids = invoices.Select(invoice => invoice.Id).ToHashSet();

        var claims = await _claims
            .ListAsync(claim => ids.Contains(claim.InvoiceId), ct)
            .ConfigureAwait(false);

        var payments = await _payments
            .ListAsync(payment => payment.InvoiceId != null
                && ids.Contains(payment.InvoiceId.Value), ct)
            .ConfigureAwait(false);

        var patientIds = invoices.Select(invoice => invoice.PatientId).ToHashSet();

        var patients = await _patients
            .ListAsync(patient => patientIds.Contains(patient.Id), ct)
            .ConfigureAwait(false);

        var names = patients.ToDictionary(patient => patient.Id, patient => patient.FullName);

        // Approved once assessed, claimed before that — see the remarks on
        // InvoiceDetail.FundEstimate.
        var claimByInvoice = claims
            .GroupBy(claim => claim.InvoiceId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(claim =>
                    claim.AssessedUtc is null ? claim.AmountClaimed : claim.AmountApproved));

        var paidByInvoice = payments
            .GroupBy(payment => payment.InvoiceId!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(payment => payment.Amount));

        return invoices
            .OrderByDescending(invoice => invoice.IssuedUtc ?? invoice.CreatedUtc)
            .Select(invoice => new InvoiceRow(
                invoice.Id,
                invoice.PatientId,
                names.GetValueOrDefault(invoice.PatientId, "Unknown patient"),
                invoice.InvoiceNumber ?? "—",
                invoice.Total,
                claimByInvoice.GetValueOrDefault(invoice.Id),
                Round(invoice.Total - paidByInvoice.GetValueOrDefault(invoice.Id)),
                EffectiveStatus(invoice, paidByInvoice.GetValueOrDefault(invoice.Id))))
            .ToList();
    }

    /// <summary>
    /// The status the payments imply, without writing anything.
    /// </summary>
    /// <remarks>
    /// Shared by the list and by <c>SettleAsync</c>, so what the screen shows and what
    /// gets stored cannot drift apart.
    /// </remarks>
    private InvoiceStatus EffectiveStatus(Invoice invoice, decimal paid)
    {
        if (invoice.Status is InvoiceStatus.WrittenOff or InvoiceStatus.Voided
            or InvoiceStatus.Draft)
        {
            return invoice.Status;
        }

        if (paid >= invoice.Total) return InvoiceStatus.Paid;

        return paid > 0m ? InvoiceStatus.PartiallyPaid : OverdueOrIssued(invoice);
    }

    /// <summary>
    /// Re-derives the invoice's paid total and status from its payments.
    /// </summary>
    /// <remarks>
    /// Called after every money movement. The status is a conclusion drawn from the
    /// ledger, not something a caller sets: leaving it to each call site is how an invoice
    /// ends up marked Paid with a balance still on it.
    /// </remarks>
    private async Task SettleAsync(Guid invoiceId, CancellationToken ct)
    {
        var invoice = await _invoices.GetByIdAsync(invoiceId, ct).ConfigureAwait(false);
        if (invoice is null) return;

        var payments = await _payments
            .ListAsync(payment => payment.InvoiceId == invoiceId, ct)
            .ConfigureAwait(false);

        var paid = Round(payments.Sum(payment => payment.Amount));

        invoice.AmountPaid = paid;

        // A written-off or voided invoice keeps its status. Recomputing it from the balance
        // would quietly un-write-off a debt the practice has already given up on.
        if (invoice.Status is not (InvoiceStatus.WrittenOff or InvoiceStatus.Voided))
        {
            invoice.Status = EffectiveStatus(invoice, paid);
        }

        await _invoices.SaveAsync(invoice, ct).ConfigureAwait(false);
        await RefreshPatientBalanceAsync(invoice.PatientId, ct).ConfigureAwait(false);
    }

    private InvoiceStatus OverdueOrIssued(Invoice invoice) =>
        invoice.DueOn is { } due && due < _clock.Today
            ? InvoiceStatus.Overdue
            : InvoiceStatus.Issued;

    /// <summary>
    /// Recomputes the patient's balance from their invoices.
    /// </summary>
    /// <remarks>
    /// <c>Patient.Balance</c> drives the front desk's debtors figure and the pre-booking
    /// checks, and it is a cache. Refreshed here rather than adjusted by deltas: a delta
    /// applied twice, or missed once, leaves a balance nobody can reconcile and no way to
    /// tell which.
    /// </remarks>
    private async Task RefreshPatientBalanceAsync(Guid patientId, CancellationToken ct)
    {
        var patient = await _patients.GetByIdAsync(patientId, ct).ConfigureAwait(false);
        if (patient is null) return;

        var invoices = await _invoices
            .ListAsync(invoice => invoice.PatientId == patientId, ct)
            .ConfigureAwait(false);

        var owing = invoices
            .Where(invoice => invoice.Status is not (InvoiceStatus.Voided
                or InvoiceStatus.WrittenOff or InvoiceStatus.Draft))
            .Sum(invoice => invoice.Total - invoice.AmountPaid);

        patient.Balance = Round(owing);

        await _patients.SaveAsync(patient, ct).ConfigureAwait(false);
    }

    private async Task<Guid> LocationOfAsync(Guid invoiceId, CancellationToken ct)
    {
        var invoice = await _invoices.GetByIdAsync(invoiceId, ct).ConfigureAwait(false);

        return invoice?.PracticeLocationId ?? Guid.Empty;
    }

    /// <summary>Which payment method a fund's benefit lands as.</summary>
    private static PaymentMethod BenefitMethod(ClaimType type) => type switch
    {
        ClaimType.MedicareCdbs => PaymentMethod.MedicareBenefit,
        ClaimType.Dva => PaymentMethod.DvaBenefit,
        _ => PaymentMethod.HicapsFundBenefit,
    };

    /// <summary>
    /// Money, to the cent.
    /// </summary>
    /// <remarks>
    /// Every arithmetic result goes through here. Half of a $340.005 rounding error is the
    /// kind of cent that turns up as a one-cent balance nobody can clear, and the ledger
    /// has to agree with the terminal to the cent.
    /// </remarks>
    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string Money(decimal value) => value.ToString("C2",
        System.Globalization.CultureInfo.GetCultureInfo("en-AU"));

    private static string Append(string? existing, string line) =>
        string.IsNullOrWhiteSpace(existing) ? line : $"{existing}{Environment.NewLine}{line}";

    private static bool Between(DateTime value, DateTime fromUtc, DateTime toUtc) =>
        value >= fromUtc && value < toUtc;

    private static (DateTime Start, DateTime End) LocalDayToUtc(DateOnly day)
    {
        var start = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
        return (start.ToUniversalTime(), start.AddDays(1).ToUniversalTime());
    }
}
