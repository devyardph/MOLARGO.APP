using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Repositories;
using DYS.Molargo.Shared.Services;

namespace DYS.Molargo.Shared.Features.Admin.Services;

/// <summary>One plan as the practice sees it, priced for their own size.</summary>
/// <param name="IsCurrent">The plan they are on.</param>
/// <param name="Serves">
/// False where the plan cannot cover the practice at all — it does not sell the extra
/// sites they run. Absent is not free, so such a plan is offered and refused rather than
/// quoted at a price it would not honour.
/// </param>
/// <param name="Difference">
/// What the monthly bill would change by. Negative is a saving. Null for the current plan.
/// </param>
public sealed record PlanChoice(
    Guid PlanId,
    string Name,
    string Code,
    PlanQuote Quote,
    bool IsCurrent,
    bool Serves,
    decimal? Difference);

/// <summary>What the practice is on, and what it costs them.</summary>
/// <param name="Quote">
/// Null where no plan is recorded — a practice mid-setup, or one the vendor has not yet
/// put on a plan.
/// </param>
public sealed record Subscription(
    string PracticeName,
    string CountryCode,
    Guid? PlanId,
    string? PlanName,
    PlanQuote? Quote,
    int Sites,
    int Clinicians,
    DateOnly? TrialEndsOn,
    int? TrialDaysLeft,
    DateOnly? SubscribedOn,
    IReadOnlyList<PlanChoice> Choices,

    /// <summary>Whether this practice has text messages switched on.</summary>
    bool SmsEnabled,

    /// <summary>False where the vendor has no gateway for this practice's country.</summary>
    bool SmsAvailable,

    decimal SmsPricePerMessage,
    string SmsCurrency,

    /// <summary>Messages that actually went, this calendar month.</summary>
    /// <summary>Texts the plan covers this month, across every site.</summary>
    int SmsIncludedThisMonth,

    int SmsSentThisMonth,

    /// <summary>And since the practice started.</summary>
    int SmsSentTotal)
{
    /// <summary>What this month's messages come to at the current rate.</summary>
    /// <remarks>
    /// Computed rather than stored. Nothing has been billed for these yet — the figure is
    /// what the practice would owe if the month closed now, which is the question somebody
    /// switching this on actually has.
    /// </remarks>
    /// <summary>Messages this month that will actually be charged.</summary>
    public int SmsBillableThisMonth => Math.Max(0, SmsSentThisMonth - SmsIncludedThisMonth);

    /// <summary>What is left of the allowance, never below zero.</summary>
    public int SmsRemainingThisMonth => Math.Max(0, SmsIncludedThisMonth - SmsSentThisMonth);

    /// <remarks>
    /// The chargeable messages only. Quoting the whole month's count at the rate would tell
    /// a practice on a plan with an allowance that it owes for messages the plan covered.
    /// </remarks>
    public decimal SmsCostThisMonth => SmsBillableThisMonth * SmsPricePerMessage;
}

/// <summary>
/// The practice's own view of its subscription.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the vendor's <c>IPlanService</c>, which every clinic must not be able to
/// reach: that one edits what the plans <em>are</em>, and this one only reads them and
/// records which one this practice is on. A clinic changing its own plan is a different
/// permission from a clinic rewriting the price list.
/// </para>
/// <para>
/// It records the change and nothing more. No payment is taken, no invoice is raised and
/// no card is held — subscription charges are raised on the vendor's side, from the plan
/// the tenant is on at the time. So this moves the practice onto a plan and the next
/// charge follows it, which is the honest half of what a real upgrade button does.
/// </para>
/// </remarks>
public interface ISubscriptionService
{
    /// <summary>The current plan, its price for this practice, and what else is on offer.</summary>
    Task<Subscription?> GetAsync(CancellationToken ct = default);

    /// <summary>Moves the practice onto another plan. Returns a refusal, or null.</summary>
    Task<string?> ChangePlanAsync(Guid planId, CancellationToken ct = default);

    /// <summary>
    /// Switches text messages on or off for this practice.
    /// </summary>
    /// <remarks>
    /// Refuses to switch on where the vendor has no gateway for the country. Allowing it
    /// would leave a practice believing it sends texts and quietly sending none.
    /// </remarks>
    Task<string?> SetSmsEnabledAsync(bool enabled, CancellationToken ct = default);
}

/// <inheritdoc cref="ISubscriptionService"/>
public sealed class SubscriptionService : ISubscriptionService
{
    private readonly IRepository<Tenant> _tenants;
    private readonly IRepository<Plan> _plans;
    private readonly IRepository<PracticeLocation> _locations;
    private readonly IRepository<Provider> _providers;
    private readonly IRepository<AuditEntry> _audit;
    private readonly IRepository<CommunicationLog> _messages;
    private readonly ISmsGatewayResolver _sms;
    private readonly ITenantContext _tenant;
    private readonly ISessionService _session;
    private readonly IPracticeGuard _guard;
    private readonly IClock _clock;

    public SubscriptionService(
        IRepository<Tenant> tenants,
        IRepository<Plan> plans,
        IRepository<PracticeLocation> locations,
        IRepository<Provider> providers,
        IRepository<AuditEntry> audit,
        IRepository<CommunicationLog> messages,
        ISmsGatewayResolver sms,
        ITenantContext tenant,
        ISessionService session,
        IPracticeGuard guard,
        IClock clock)
    {
        _tenants = tenants;
        _plans = plans;
        _locations = locations;
        _providers = providers;
        _audit = audit;
        _messages = messages;
        _sms = sms;
        _tenant = tenant;
        _session = session;
        _guard = guard;
        _clock = clock;
    }

    public async Task<Subscription?> GetAsync(CancellationToken ct = default)
    {
        var practice = await _tenants
            .GetByIdAsync(_tenant.TenantId, ct)
            .ConfigureAwait(false);

        if (practice is null) return null;

        var (sites, clinicians) = await CountAsync(ct).ConfigureAwait(false);

        var current = practice.PlanId is { } planId
            ? await _plans.GetByIdAsync(planId, ct).ConfigureAwait(false)
            : null;

        var quote = current is null ? null : Pricing.Quote(current, sites, clinicians);

        var choices = await ChoicesAsync(practice, current, quote, sites, clinicians, ct)
            .ConfigureAwait(false);

        var pricing = await _sms.PricingForCurrentTenantAsync(ct).ConfigureAwait(false);

        var monthStart = SmsBilling.MonthStart(_clock.Today);

        var texts = await _messages
            .ListAsync(message => message.Channel == CommunicationChannel.Sms, ct)
            .ConfigureAwait(false);

        // The shared rule, not a copy of it. This number, the vendor's per-country usage
        // and the credit check that decides whether the next message may go all have to
        // agree — a practice charged for a text the vendor's own total left out is the
        // failure that follows from three versions of "sent".
        var billable = texts.Where(SmsBilling.IsBillable).ToList();

        return new Subscription(
            practice.Name,
            practice.CountryCode,
            current?.Id,
            current?.Name,
            quote,
            sites,
            clinicians,
            practice.TrialEndsOn,
            practice.TrialDaysLeft(_clock.Today),
            practice.SubscribedOn,
            choices,
            practice.SmsEnabled,
            pricing.Available,
            pricing.PricePerMessage,
            pricing.CurrencyCode,

            // The allowance across every site, matching how the charge run works it out.
            Math.Max(0, current?.IncludedSmsPerSite ?? 0) * Math.Max(1, sites),

            // Dated by when it was sent, not when the row was made: a message queued in one
            // month and sent in the next belongs to the month it went out, which is the
            // month the carrier billed the vendor for it.
            billable.Count(message => message.SentUtc >= monthStart),
            billable.Count);
    }

    public async Task<string?> SetSmsEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        // The same right that changes the plan: this is the other way the practice's bill
        // moves, and there is no separate billing permission to hold it behind.
        var refusal = await _guard
            .RefuseAsync(PracticePermissions.ManageSettings, ct)
            .ConfigureAwait(false);

        if (refusal is not null) return refusal;

        var practice = await _tenants.GetByIdAsync(_tenant.TenantId, ct).ConfigureAwait(false);

        if (practice is null) return "This practice no longer exists.";

        if (practice.SmsEnabled == enabled) return null;

        if (enabled)
        {
            var pricing = await _sms.PricingForCurrentTenantAsync(ct).ConfigureAwait(false);

            if (!pricing.Available)
            {
                return "Text messaging is not available in "
                    + $"{practice.CountryCode} yet. Nothing would send, so it cannot be "
                    + "switched on.";
            }
        }

        practice.SmsEnabled = enabled;

        await _tenants.SaveAsync(practice, ct).ConfigureAwait(false);

        await _audit
            .SaveAsync(
                new AuditEntry
                {
                    Action = AuditAction.Updated,
                    EntityName = nameof(Tenant),
                    EntityId = practice.Id,
                    ProviderId = _session.ProviderId,
                    ProviderName = _session.UserDisplayName,
                    OccurredUtc = _clock.UtcNow,
                    Detail = enabled
                        ? "Text messaging switched on. Messages are charged per message on "
                            + "top of the plan."
                        : "Text messaging switched off.",
                },
                ct)
            .ConfigureAwait(false);

        return null;
    }

    /// <summary>
    /// The plans this practice could move to, each priced for their own size.
    /// </summary>
    /// <remarks>
    /// Narrowed to their own country. A plan is one country's product at that country's
    /// price in that country's currency, so offering a practice in Sydney the New Zealand
    /// plan would quote them a number they can never be billed.
    ///
    /// Withdrawn plans are excluded unless the practice is already on one — grandfathering
    /// is the usual reason a plan is withdrawn, and a practice on a retired plan has to be
    /// able to see what they are paying for.
    /// </remarks>
    private async Task<IReadOnlyList<PlanChoice>> ChoicesAsync(
        Tenant practice,
        Plan? current,
        PlanQuote? currentQuote,
        int sites,
        int clinicians,
        CancellationToken ct)
    {
        var all = await _plans.ListAsync(ct: ct).ConfigureAwait(false);

        return all
            .Where(plan => string.Equals(
                plan.CountryCode, practice.CountryCode, StringComparison.OrdinalIgnoreCase))
            .Where(plan => plan.IsActive || plan.Id == current?.Id)
            .OrderBy(plan => plan.DisplayOrder)
            .ThenBy(plan => plan.MonthlyBase)
            .Select(plan =>
            {
                var quote = Pricing.Quote(plan, sites, clinicians);
                var isCurrent = plan.Id == current?.Id;

                return new PlanChoice(
                    plan.Id,
                    plan.Name,
                    plan.Code,
                    quote,
                    isCurrent,
                    !quote.SitesNotOffered,

                    // Compared on what each would actually cost this practice, not on the
                    // headline. A cheaper base that charges for every extra seat is not a
                    // cheaper plan for a practice with six clinicians, and the headline is
                    // exactly what would say otherwise.
                    isCurrent || currentQuote is null || quote.SitesNotOffered
                        ? null
                        : quote.Monthly - currentQuote.Monthly);
            })
            .ToList();
    }

    public async Task<string?> ChangePlanAsync(Guid planId, CancellationToken ct = default)
    {
        // The practice's own owner or a manager with settings rights — not the vendor
        // permission, which is what edits the price list.
        var refusal = await _guard
            .RefuseAsync(PracticePermissions.ManageSettings, ct)
            .ConfigureAwait(false);

        if (refusal is not null) return refusal;

        var practice = await _tenants
            .GetByIdAsync(_tenant.TenantId, ct)
            .ConfigureAwait(false);

        if (practice is null) return "The practice record could not be read.";

        var plan = await _plans.GetByIdAsync(planId, ct).ConfigureAwait(false);

        if (plan is null) return "That plan no longer exists.";

        if (plan.Id == practice.PlanId) return null;

        if (!plan.IsActive)
        {
            return $"{plan.Name} is no longer sold. A practice already on it keeps it, but "
                + "it cannot be moved onto.";
        }

        if (!string.Equals(plan.CountryCode, practice.CountryCode, StringComparison.OrdinalIgnoreCase))
        {
            return $"{plan.Name} is sold in {plan.CountryCode}, and this practice is in "
                + $"{practice.CountryCode}. Plans are priced per country.";
        }

        var (sites, clinicians) = await CountAsync(ct).ConfigureAwait(false);
        var quote = Pricing.Quote(plan, sites, clinicians);

        // Refused rather than quoted at a price the plan would not honour. A plan with no
        // extra-site price does not sell extra sites, and moving a two-surgery practice
        // onto one would bill them for one site and serve them two.
        if (quote.SitesNotOffered)
        {
            return $"{plan.Name} does not cover {sites} sites. It would have to be sold "
                + "with extra sites, and this one is not.";
        }

        var was = practice.PlanId is { } previous
            ? (await _plans.GetByIdAsync(previous, ct).ConfigureAwait(false))?.Name ?? "no plan"
            : "no plan";

        practice.PlanId = plan.Id;

        await _tenants.SaveAsync(practice, ct).ConfigureAwait(false);

        await _audit
            .SaveAsync(
                new AuditEntry
                {
                    Action = AuditAction.Updated,
                    EntityName = nameof(Tenant),
                    EntityId = practice.Id,
                    ProviderId = _session.ProviderId,
                    ProviderName = _session.UserDisplayName,
                    OccurredUtc = _clock.UtcNow,
                    Detail = $"Plan changed from {was} to {plan.Name}. "
                        + "No payment was taken — the next subscription charge follows the "
                        + "new plan.",
                },
                ct)
            .ConfigureAwait(false);

        return null;
    }

    /// <summary>
    /// What the practice is billed on: open sites, and clinical seats.
    /// </summary>
    /// <remarks>
    /// Counted the same way the vendor's own billing counts them, through
    /// <see cref="ProviderRoles.IsClinical"/>. A quote the practice reads here and the
    /// charge the vendor raises have to come from one definition, or the screen argues with
    /// the invoice.
    /// </remarks>
    private async Task<(int Sites, int Clinicians)> CountAsync(CancellationToken ct)
    {
        var sites = await _locations
            .ListAsync(location => location.IsActive, ct)
            .ConfigureAwait(false);

        var staff = await _providers
            .ListAsync(provider => provider.IsActive, ct)
            .ConfigureAwait(false);

        return (sites.Count, staff.Count(provider => ProviderRoles.IsClinical(provider.Role)));
    }
}
