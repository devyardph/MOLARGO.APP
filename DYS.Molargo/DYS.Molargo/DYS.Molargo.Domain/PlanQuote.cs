using DYS.Molargo.Domain.Entities;

namespace DYS.Molargo.Domain;

/// <summary>
/// What one clinic pays on one plan, broken into the lines it is made of.
/// </summary>
/// <param name="Sites">Sites counted, never below one.</param>
/// <param name="Clinicians">
/// Billable seats. Only the roles <see cref="ProviderRoles.IsClinical"/> recognises —
/// assistants and reception are free, and the price has to be computed from the same
/// definition the app uses everywhere else or a quote will not match the seat count on the
/// screen beside it.
/// </param>
public sealed record PlanQuote(
    string PlanName,
    string CurrencyCode,
    int Sites,
    int Clinicians,
    int ExtraSites,
    int IncludedSeats,
    int ExtraSeats,
    decimal Base,
    decimal SiteCharge,
    decimal SeatCharge,
    int? AnnualMonthsCharged,

    /// <summary>
    /// The practice needs more sites than the plan covers, and the plan does not sell
    /// extras.
    /// </summary>
    /// <remarks>
    /// A separate answer from a price, because the alternative was worse: a plan with no
    /// extra-site price quoted the second site at zero, so Solo advertised "extra site —
    /// not offered" and then billed two sites for the price of one. Absent is not free.
    /// </remarks>
    bool SitesNotOffered)
{
    /// <summary>
    /// What the practice pays a month — meaningless where the plan cannot serve them.
    /// </summary>
    /// <remarks>
    /// Callers must check <see cref="SitesNotOffered"/> before showing this. It is left as
    /// a figure rather than made nullable so the breakdown lines still add up on screen;
    /// what changes is whether a total is shown at all.
    /// </remarks>
    public decimal Monthly => Base + SiteCharge + SeatCharge;

    /// <summary>Twelve months at the monthly rate.</summary>
    public decimal AnnualAtMonthlyRate => Monthly * 12;

    /// <summary>What a year costs prepaid, or null where the plan is monthly only.</summary>
    public decimal? Annual => AnnualMonthsCharged is { } months ? Monthly * months : null;

    /// <summary>What the prepay saves over paying monthly.</summary>
    public decimal? AnnualSaving =>
        Annual is { } annual ? AnnualAtMonthlyRate - annual : null;

    /// <summary>
    /// The monthly figure divided by billable seats, or null where there are none.
    /// </summary>
    /// <remarks>
    /// Null rather than zero for a clinic with no clinicians yet. A brand new tenant would
    /// otherwise be quoted "$0 per clinician", which reads as free rather than as a
    /// practice that has not entered its staff.
    /// </remarks>
    public decimal? PerClinician =>
        Clinicians > 0 ? decimal.Round(Monthly / Clinicians, 2) : null;
}

/// <summary>
/// The pricing rule: a base per subscription, sites beyond what it covers, and clinician
/// seats beyond the allowance.
/// </summary>
/// <remarks>
/// <para>
/// In Domain so exactly one implementation exists. The vendor's plan editor quotes as
/// somebody types, the clinic list shows what each practice pays, and an invoice would use
/// it too — three readers, and any of them computing it separately is how a customer ends
/// up quoted one figure and billed another.
/// </para>
/// <para>
/// It charges for clinicians only, which is a deliberate commercial decision and not an
/// oversight: charging per front-desk login is what makes a practice share one reception
/// account, and a shared account destroys the audit trail the compliance screens exist to
/// keep.
/// </para>
/// </remarks>
public static class Pricing
{
    /// <summary>Prices one clinic against one plan.</summary>
    /// <param name="sites">Open locations. Treated as one where none are recorded yet.</param>
    /// <param name="clinicians">Active staff in a clinical role.</param>
    public static PlanQuote Quote(Plan plan, int sites, int clinicians)
    {
        // At least one site, because a subscription with none is a practice mid-setup
        // rather than a practice that should be billed nothing.
        var countedSites = Math.Max(1, sites);
        var countedClinicians = Math.Max(0, clinicians);

        var extraSites = Math.Max(0, countedSites - Math.Max(1, plan.IncludedSites));

        // The allowance scales with sites, so a group's second surgery brings its own
        // seats rather than eating the first one's.
        var includedSeats = Math.Max(0, plan.IncludedSeatsPerSite) * countedSites;
        var extraSeats = Math.Max(0, countedClinicians - includedSeats);

        return new PlanQuote(
            plan.Name,
            plan.CurrencyCode,
            countedSites,
            countedClinicians,
            extraSites,
            includedSeats,
            extraSeats,
            plan.MonthlyBase,
            extraSites * plan.PricePerExtraSite,
            extraSeats * plan.PricePerExtraSeat,
            plan.AnnualMonthsCharged,

            // A plan with no extra-site price does not sell extra sites. Multiplying by
            // that zero would quote a second surgery for nothing, which is how a
            // single-site plan ends up serving a group.
            SitesNotOffered: extraSites > 0 && plan.PricePerExtraSite <= 0);
    }

    /// <summary>
    /// A plan's headline, for a pricing page — "$329 a month, 1 site, 4 clinicians".
    /// </summary>
    /// <remarks>
    /// Built from the same fields the quote uses rather than typed into the plan as a
    /// separate marketing string, so a price change cannot leave the headline behind.
    /// </remarks>
    public static string Headline(Plan plan)
    {
        var money = Money(plan.MonthlyBase, plan.CurrencyCode);
        var per = plan.IsPerSite ? " a site" : string.Empty;
        var sites = plan.IncludedSites == 1 ? "1 site" : $"{plan.IncludedSites} sites";

        var seats = plan.IncludedSeatsPerSite == 1
            ? "1 clinician a site"
            : $"{plan.IncludedSeatsPerSite} clinicians a site";

        return $"{money} a month{per} — {sites}, {seats}";
    }

    /// <summary>
    /// Money with the plan's currency, for a screen the vendor reads across countries.
    /// </summary>
    /// <remarks>
    /// The code is shown rather than only a symbol, because "$329" beside "$369" gives no
    /// clue that one is Australian and the other is a New Zealand price. The symbol alone
    /// is the ambiguity this whole feature exists to remove.
    /// </remarks>
    public static string Money(decimal amount, string currencyCode)
    {
        var symbol = currencyCode switch
        {
            "AUD" or "NZD" or "USD" or "CAD" or "SGD" => "$",
            "GBP" => "£",
            "EUR" => "€",
            "PHP" => "₱",
            "JPY" => "¥",
            "INR" => "₹",

            // No symbol rather than a guessed one. The code is printed either way, so an
            // unknown currency reads as "1490 PHP" — correct and unambiguous — instead of
            // borrowing a dollar sign it has no right to.
            _ => string.Empty,
        };

        // Grouped thousands, because the figures are no longer all two or three digits:
        // "₱35880" is a number somebody has to count the digits of, and an annual prepay
        // in pesos is five of them.
        return $"{symbol}{amount:#,##0.##} {currencyCode}";
    }
}
