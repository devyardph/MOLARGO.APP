namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A subscription plan the vendor sells, priced for one country.
/// </summary>
/// <remarks>
/// <para>
/// One row per plan <em>per country</em>, not one row per plan with prices hanging off it.
/// A practice in Auckland is not buying the Sydney plan in another currency — it is buying
/// a different product at a locally-decided price, and the two move independently. The pair
/// <see cref="Code"/> + <see cref="CountryCode"/> is what identifies a plan.
/// </para>
/// <para>
/// Deliberately excluded from tenant filtering in the same way <see cref="Tenant"/> is:
/// plans belong to the vendor, not to a clinic, and a clinic has to be able to read the
/// one it is on. Its <c>TenantId</c> is set to the platform tenant so the column is never
/// a surprising zero.
/// </para>
/// <para>
/// Prices are held as <c>decimal</c> and never as <c>double</c>. Money that has been
/// through binary floating point produces invoices that are a cent out, and the cent is
/// always noticed.
/// </para>
/// </remarks>
public sealed class Plan : EntityBase
{
    /// <summary>What the plan is called on a quote — "Practice".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// A short stable handle — "practice".
    /// </summary>
    /// <remarks>
    /// Separate from the name for the same reason a tenant's slug is: the name is
    /// marketing and will be rewritten, and this is what a subscription points at.
    /// </remarks>
    public string Code { get; set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2 — "AU", "NZ", "GB".</summary>
    public string CountryCode { get; set; } = "AU";

    /// <summary>ISO 4217 — "AUD". Carried on the plan, not the tenant.</summary>
    /// <remarks>
    /// The plan decides the currency because the plan decides the price. A tenant that
    /// changes plan changes currency with it, which is the only combination that can be
    /// invoiced coherently.
    /// </remarks>
    public string CurrencyCode { get; set; } = "AUD";

    /// <summary>What the plan costs a month before any extras, excluding tax.</summary>
    public decimal MonthlyBase { get; set; }

    /// <summary>Sites the base covers. At least one.</summary>
    public int IncludedSites { get; set; } = 1;

    /// <summary>
    /// Clinician seats included per site.
    /// </summary>
    /// <remarks>
    /// Per site rather than per subscription, so a two-surgery group gets twice the
    /// allowance without needing a second field. It is the difference between "4 seats"
    /// and "4 seats a site", and a group notices which one they were sold.
    /// </remarks>
    public int IncludedSeatsPerSite { get; set; } = 1;

    /// <summary>
    /// Text messages included each month, per site.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per site rather than per subscription, for the same reason seats are: a two-surgery
    /// practice sends roughly twice the reminders, and a flat allowance would be spent by
    /// the larger clinic before the month started. Scaling by clinicians would track the
    /// volume more closely still — a single-site practice with eight dentists sends more
    /// than one with two — but that is a second scaling rule for one field, and sites is
    /// the rule this model already has.
    /// </para>
    /// <para>
    /// Zero is the ordinary state and means every message is charged from the first. It is
    /// not "unset": a plan that includes nothing is a real plan, and the vendor pays the
    /// carrier for each message either way.
    /// </para>
    /// </remarks>
    public int IncludedSmsPerSite { get; set; }

    /// <summary>Charged for each site beyond <see cref="IncludedSites"/>.</summary>
    /// <remarks>
    /// Also how a per-site plan is expressed: set the base and this to the same figure and
    /// the plan costs that much per location, with no separate concept needed.
    /// </remarks>
    public decimal PricePerExtraSite { get; set; }

    /// <summary>Charged for each clinician beyond the included allowance.</summary>
    public decimal PricePerExtraSeat { get; set; }

    /// <summary>
    /// Months charged on an annual prepay, or null where the plan is monthly only.
    /// </summary>
    /// <remarks>
    /// Ten means "pay for ten, get twelve". Stored as months rather than a percentage
    /// because that is how it is sold and how it is invoiced; a percentage would have to be
    /// converted back and would not land on a round number.
    /// </remarks>
    public int? AnnualMonthsCharged { get; set; }

    /// <summary>Order on the pricing page, cheapest first.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Whether the plan can still be subscribed to.
    /// </summary>
    /// <remarks>
    /// Retired rather than deleted. A clinic already on a withdrawn plan keeps its price —
    /// deleting the row would leave their subscription pointing at nothing, and grandfathered
    /// pricing is the normal reason a plan gets withdrawn in the first place.
    /// </remarks>
    public bool IsActive { get; set; } = true;

    /// <summary>What a clinic on this plan is described as paying, before usage.</summary>
    public bool IsPerSite => PricePerExtraSite > 0 && MonthlyBase == PricePerExtraSite;
}
