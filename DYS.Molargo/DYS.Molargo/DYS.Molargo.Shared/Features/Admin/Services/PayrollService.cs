using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Components;
using DYS.Molargo.Shared.Features.Reports.Services;
using DYS.Molargo.Shared.Repositories;
using DYS.Molargo.Shared.Services;

namespace DYS.Molargo.Shared.Features.Admin.Services;

/// <summary>One clinician's standing week, as the roster reads it.</summary>
/// <param name="Days">
/// Which days they work. The standing pattern, not a shift — see the note on the service.
/// </param>
public sealed record RosterRow(
    Guid ProviderId,
    string Name,
    ProviderRole Role,
    string SiteName,
    WorkingDays Days,
    TimeOnly? From,
    TimeOnly? To)
{
    public bool HasHours => From is not null && To is not null;

    /// <summary>"8:00–16:00", or a dash where no hours are set.</summary>
    public string HoursLabel => HasHours ? $"{From:HH:mm}–{To:HH:mm}" : "—";

    /// <summary>Whether this clinician's standing week covers a given weekday.</summary>
    public bool WorksOn(DayOfWeek day) => Days.HasFlag(ProviderAvailability.Of(day));
}

/// <summary>What one clinician earned over a period, and what the practice owes them.</summary>
/// <param name="Production">What they billed, dated by when the work was done.</param>
/// <param name="Collections">
/// What was actually collected against their work, dated by when the money arrived.
/// </param>
/// <param name="Pay">
/// What the arrangement produces, or null where it cannot be worked out — no arrangement
/// recorded, or an hourly one with no hours to apply it to.
/// </param>
public sealed record PayRow(
    Guid ProviderId,
    string Name,
    ProviderRole Role,
    PayBasis Basis,
    decimal? Rate,
    decimal? BonusTarget,
    decimal? BonusPercent,
    decimal Production,
    decimal Collections,
    decimal? Pay,
    decimal? Bonus,
    string Explanation);

/// <summary>
/// The roster and provider pay.
/// </summary>
/// <remarks>
/// <para>
/// Two halves of the design's HR pane, and only one of them is fully answerable today.
/// Pay is: the arrangement is recorded per clinician and applied to production and
/// collections the app already computes from real invoices and payments.
/// </para>
/// <para>
/// The roster is the practice's <em>standing week</em> — the working days and hours each
/// clinician is available on, which the diary already enforces. It is not shifts, leave or
/// timesheets: those need a record per day per person that nothing in the app writes, and
/// inventing hours to fill the design's timesheet column would put a number in front of a
/// payroll decision that nobody measured.
/// </para>
/// </remarks>
public interface IPayrollService
{
    /// <summary>The standing week, one row per working clinician.</summary>
    Task<IReadOnlyList<RosterRow>> GetRosterAsync(
        Guid? locationId = null, CancellationToken ct = default);

    /// <summary>What each clinician earned over a period.</summary>
    Task<IReadOnlyList<PayRow>> GetPayAsync(
        ReportPeriod period, Guid? locationId = null, CancellationToken ct = default);

    /// <summary>Records how a clinician is paid. Returns a refusal, or null.</summary>
    Task<string?> SaveArrangementAsync(
        Guid providerId,
        PayBasis basis,
        decimal? rate,
        decimal? bonusTarget,
        decimal? bonusPercent,
        CancellationToken ct = default);
}

/// <inheritdoc cref="IPayrollService"/>
public sealed class PayrollService : IPayrollService
{
    private readonly IRepository<Provider> _providers;
    private readonly IRepository<PracticeLocation> _locations;
    private readonly IRepository<Invoice> _invoices;
    private readonly IRepository<InvoiceLine> _lines;
    private readonly IRepository<Payment> _payments;
    private readonly IReportService _reports;
    private readonly IPracticeGuard _guard;

    public PayrollService(
        IRepository<Provider> providers,
        IRepository<PracticeLocation> locations,
        IRepository<Invoice> invoices,
        IRepository<InvoiceLine> lines,
        IRepository<Payment> payments,
        IReportService reports,
        IPracticeGuard guard)
    {
        _providers = providers;
        _locations = locations;
        _invoices = invoices;
        _lines = lines;
        _payments = payments;
        _reports = reports;
        _guard = guard;
    }

    public async Task<IReadOnlyList<RosterRow>> GetRosterAsync(
        Guid? locationId = null, CancellationToken ct = default)
    {
        var staff = await _providers
            .ListAsync(provider => provider.IsActive, ct)
            .ConfigureAwait(false);

        var sites = await _locations.ListAsync(ct: ct).ConfigureAwait(false);
        var names = sites.ToDictionary(site => site.Id, site => site.Name);

        return staff
            .Where(provider => locationId is null || provider.PrimaryLocationId == locationId)

            // Administration staff are not on the clinical roster. They have no diary
            // column, so a row for them would show a week of blanks.
            .Where(provider => provider.Role != ProviderRole.Administration)
            .OrderBy(provider => provider.Role)
            .ThenBy(provider => provider.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(provider => new RosterRow(
                provider.Id,
                provider.FullName,
                provider.Role,
                provider.PrimaryLocationId is { } id && names.TryGetValue(id, out var site)
                    ? site
                    : "No site",
                provider.WorkingDays,
                provider.WorkingFrom,
                provider.WorkingTo))
            .ToList();
    }

    public async Task<IReadOnlyList<PayRow>> GetPayAsync(
        ReportPeriod period, Guid? locationId = null, CancellationToken ct = default)
    {
        var range = _reports.RangeFor(period);

        var staff = await _providers
            .ListAsync(provider => provider.IsActive, ct)
            .ConfigureAwait(false);

        var clinicians = staff
            .Where(provider => locationId is null || provider.PrimaryLocationId == locationId)
            .Where(provider => provider.Role != ProviderRole.Administration)
            .ToList();

        if (clinicians.Count == 0) return [];

        var production = await ProductionAsync(range, locationId, ct).ConfigureAwait(false);
        var collections = await CollectionsAsync(range, locationId, ct).ConfigureAwait(false);

        return clinicians
            .Select(provider => Build(
                provider,
                production.GetValueOrDefault(provider.Id),
                collections.GetValueOrDefault(provider.Id)))
            .OrderByDescending(row => row.Pay ?? 0m)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// What each clinician billed inside the window.
    /// </summary>
    /// <remarks>
    /// Dated by the line's service date, not the invoice's. A visit worked on the 30th and
    /// invoiced on the 2nd belongs to the month it was worked — which is the month the
    /// clinician is paid for it.
    /// </remarks>
    private async Task<Dictionary<Guid, decimal>> ProductionAsync(
        ReportRange range, Guid? locationId, CancellationToken ct)
    {
        var invoices = await BillableInvoicesAsync(locationId, ct).ConfigureAwait(false);

        if (invoices.Count == 0) return [];

        var ids = invoices.Keys.ToList();

        var lines = await _lines
            .ListAsync(line => ids.Contains(line.InvoiceId), ct)
            .ConfigureAwait(false);

        var totals = new Dictionary<Guid, decimal>();

        foreach (var line in lines)
        {
            if (line.ProviderId is not { } providerId) continue;
            if (line.ServiceDate < range.From || line.ServiceDate > range.To) continue;

            totals[providerId] = totals.GetValueOrDefault(providerId) + line.LineTotal;
        }

        return totals;
    }

    /// <summary>
    /// What was collected against each clinician's work inside the window.
    /// </summary>
    /// <remarks>
    /// Allocated by each clinician's share of the invoice the payment was made against,
    /// not by who took the money. The person at the desk who keyed the payment is the one
    /// <c>Payment.ReceivedByProviderId</c> names, and paying a receptionist a share of a
    /// dentist's crown because they processed the card is the wrong answer to the wrong
    /// question.
    ///
    /// A part payment is split in the same proportions, so an invoice half paid earns each
    /// clinician half of their share rather than paying the first one in full.
    /// </remarks>
    private async Task<Dictionary<Guid, decimal>> CollectionsAsync(
        ReportRange range, Guid? locationId, CancellationToken ct)
    {
        var invoices = await BillableInvoicesAsync(locationId, ct).ConfigureAwait(false);

        if (invoices.Count == 0) return [];

        var ids = invoices.Keys.ToList();

        var lines = await _lines
            .ListAsync(line => ids.Contains(line.InvoiceId), ct)
            .ConfigureAwait(false);

        var shares = lines
            .Where(line => line.ProviderId is not null)
            .GroupBy(line => line.InvoiceId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(line => line.ProviderId!.Value)
                    .ToDictionary(
                        byProvider => byProvider.Key,
                        byProvider => byProvider.Sum(line => line.LineTotal)));

        var payments = await _payments
            .ListAsync(payment => payment.InvoiceId != null, ct)
            .ConfigureAwait(false);

        var totals = new Dictionary<Guid, decimal>();

        foreach (var payment in payments)
        {
            if (payment.InvoiceId is not { } invoiceId) continue;
            if (!shares.TryGetValue(invoiceId, out var byProvider)) continue;

            var received = DateOnly.FromDateTime(payment.ReceivedUtc.ToLocalTime());

            if (received < range.From || received > range.To) continue;

            var billed = byProvider.Values.Sum();

            // Nothing attributable to a clinician on this invoice. Skipped rather than
            // split evenly — an even split would invent an attribution the lines do not
            // support.
            if (billed <= 0m) continue;

            foreach (var (providerId, amount) in byProvider)
            {
                var portion = payment.Amount * (amount / billed);

                totals[providerId] = totals.GetValueOrDefault(providerId)
                    + Math.Round(portion, 2, MidpointRounding.AwayFromZero);
            }
        }

        return totals;
    }

    /// <summary>
    /// The invoices that count, keyed by id.
    /// </summary>
    /// <remarks>
    /// Drafts, voided and written-off invoices are out. A draft was never billed, a void
    /// should never have been, and a write-off is money the practice decided not to chase —
    /// paying a clinician a share of any of the three pays them for work the practice was
    /// never paid for.
    /// </remarks>
    private async Task<Dictionary<Guid, Invoice>> BillableInvoicesAsync(
        Guid? locationId, CancellationToken ct)
    {
        var invoices = await _invoices
            .ListAsync(
                invoice => invoice.Status != InvoiceStatus.Draft
                    && invoice.Status != InvoiceStatus.Voided
                    && invoice.Status != InvoiceStatus.WrittenOff,
                ct)
            .ConfigureAwait(false);

        return invoices
            .Where(invoice => locationId is null || invoice.PracticeLocationId == locationId)
            .ToDictionary(invoice => invoice.Id);
    }

    private static PayRow Build(Provider provider, decimal production, decimal collections)
    {
        decimal? pay = null;
        decimal? bonus = null;
        string explanation;

        switch (provider.PayBasis)
        {
            case PayBasis.ProductionShare when provider.PayRate is { } rate:
                pay = Round(production * rate / 100m);
                explanation = $"{Percent(rate)} of {Money(production)} produced";
                break;

            case PayBasis.CollectionShare when provider.PayRate is { } rate:
                pay = Round(collections * rate / 100m);
                explanation = $"{Percent(rate)} of {Money(collections)} collected";
                break;

            case PayBasis.Hourly:
                // The bonus half can be worked out; the hourly half cannot, because no
                // hours are recorded anywhere. Said rather than guessed — an hourly figure
                // invented from the standing roster would look like a measurement.
                if (provider.PayBonusTarget is { } target
                    && provider.PayBonusPercent is { } share
                    && production > target)
                {
                    bonus = Round((production - target) * share / 100m);
                }

                explanation = provider.PayRate is { } hourly
                    ? $"{Money(hourly)}/hour — hours are not recorded, so only the bonus is shown"
                    : "Hourly, but no rate recorded";
                break;

            case PayBasis.None:
            default:
                explanation = "No pay arrangement recorded";
                break;
        }

        return new PayRow(
            provider.Id,
            provider.FullName,
            provider.Role,
            provider.PayBasis,
            provider.PayRate,
            provider.PayBonusTarget,
            provider.PayBonusPercent,
            production,
            collections,
            pay,
            bonus,
            explanation);
    }

    public async Task<string?> SaveArrangementAsync(
        Guid providerId,
        PayBasis basis,
        decimal? rate,
        decimal? bonusTarget,
        decimal? bonusPercent,
        CancellationToken ct = default)
    {
        var refusal = await _guard
            .RefuseAsync(PracticePermissions.ManageStaff, ct)
            .ConfigureAwait(false);

        if (refusal is not null) return refusal;

        var provider = await _providers.GetByIdAsync(providerId, ct).ConfigureAwait(false);

        if (provider is null) return "That staff member no longer exists.";

        if (basis is PayBasis.ProductionShare or PayBasis.CollectionShare)
        {
            // A share over 100 pays out more than the work earned. Refused rather than
            // stored, because the figure it produces looks like a real number.
            if (rate is not { } share || share <= 0m || share > 100m)
            {
                return "A share is a percentage between 0 and 100.";
            }
        }

        if (basis == PayBasis.Hourly && rate is { } hourly && hourly < 0m)
        {
            return "An hourly rate cannot be negative.";
        }

        if (bonusPercent is { } bonus && (bonus < 0m || bonus > 100m))
        {
            return "A bonus is a percentage between 0 and 100.";
        }

        if (bonusTarget is { } target && target < 0m)
        {
            return "A bonus target cannot be negative.";
        }

        provider.PayBasis = basis;

        // Cleared with the basis. A percentage left behind from a share arrangement would
        // be read as an hourly rate the day somebody switched them over.
        provider.PayRate = basis == PayBasis.None ? null : rate;
        provider.PayBonusTarget = basis == PayBasis.Hourly ? bonusTarget : null;
        provider.PayBonusPercent = basis == PayBasis.Hourly ? bonusPercent : null;

        await _providers.SaveAsync(provider, ct).ConfigureAwait(false);

        return null;
    }

    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <remarks>
    /// Formatted here rather than in the view because the explanation is one sentence the
    /// screen prints whole — and it goes through the same formatter as every other figure,
    /// so it follows the practice's currency without this service knowing what that is.
    /// </remarks>
    private static string Money(decimal value) => MolargoFormat.MoneyExact(value);

    private static string Percent(decimal value) => $"{value:0.#}%";
}
