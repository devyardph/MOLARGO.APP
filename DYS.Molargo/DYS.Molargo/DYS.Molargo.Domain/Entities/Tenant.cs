namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A clinic: the business that subscribes to the app, and the owner of every other row.
/// </summary>
/// <remarks>
/// <para>
/// The tenant boundary. One clinic may run several <see cref="PracticeLocation"/>s — a
/// patient, their chart and their balance follow them between sites — so the subscribing
/// business is a level above location, not the same thing as one.
/// </para>
/// <para>
/// This entity is deliberately the one exception to tenant filtering: it cannot be scoped
/// by the tenant it defines without becoming unreadable to the code that has to resolve
/// which tenant is current. Its own <c>TenantId</c> is set to its <c>Id</c> so the column
/// is never a surprising null, and the filter skips this table.
/// </para>
/// </remarks>
public sealed class Tenant : EntityBase
{
    /// <summary>The trading name — what appears on a patient's invoice.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// A short, stable, URL-safe handle — "apex-dental".
    /// </summary>
    /// <remarks>
    /// Separate from the name because the name changes when a practice rebrands and this
    /// must not: it is what a future sign-in, subdomain or sync endpoint identifies the
    /// clinic by.
    /// </remarks>
    public string Slug { get; set; } = string.Empty;

    public string? Abn { get; set; }

    public string? ContactEmail { get; set; }

    public string? ContactPhone { get; set; }

    /// <summary>
    /// The plan this clinic subscribes to, or null before one is chosen.
    /// </summary>
    /// <remarks>
    /// A reference, not a name. This used to be a free-text <c>PlanName</c>, which meant
    /// the price a clinic was on lived nowhere at all — the plan was a label and the
    /// figures were in somebody's spreadsheet. Pointing at a <see cref="Plan"/> makes the
    /// subscription and its price the same fact.
    /// </remarks>
    public Guid? PlanId { get; set; }

    /// <summary>
    /// Which country's prices apply — ISO 3166-1 alpha-2.
    /// </summary>
    /// <remarks>
    /// The billing country, which is not always where a surgery is: a group registered in
    /// one country running a clinic across a border is billed once, in one currency. Held
    /// on the tenant rather than derived from a location for exactly that case, and it is
    /// what narrows the plan picker to the prices this clinic can actually be sold.
    /// </remarks>
    public string CountryCode { get; set; } = "AU";

    /// <summary>
    /// What the practice charges patients in — "AUD", "NZD".
    /// </summary>
    /// <remarks>
    /// Separate from the plan's currency, which is what the practice pays Molargo. The two
    /// are different money and a practice in New Zealand billing its patients in NZD may
    /// still be invoiced by the vendor in AUD.
    /// </remarks>
    public string CurrencyCode { get; set; } = PracticeCurrency.Default;

    /// <summary>
    /// Whether this practice sends text messages, which it is charged for per message.
    /// </summary>
    /// <remarks>
    /// Off by default, and deliberately: SMS is the one thing here that costs the practice
    /// money every time it happens rather than a flat monthly fee. A default of on would be
    /// a bill somebody did not agree to, arriving a month later.
    ///
    /// Only meaningful where the vendor has a gateway for this practice's country — see
    /// <see cref="SmsGateway"/>. The screen refuses to switch it on otherwise, because
    /// there would be nothing to send through.
    /// </remarks>
    public bool SmsEnabled { get; set; }

    public DateOnly? SubscribedOn { get; set; }

    /// <summary>
    /// The last day of the free trial, or null for a clinic that never had one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An end date rather than a "IsInTrial" flag, because a flag has to be flipped by
    /// something and there is no scheduler to flip it. A date answers the question at the
    /// moment it is asked and cannot go stale.
    /// </para>
    /// <para>
    /// Nothing is switched off when it passes. Billing stops skipping the clinic, and the
    /// vendor's screens start showing the trial as expired — that is all. Locking a
    /// practice out of its own patient records over a lapsed trial is not a decision this
    /// app should make on its own.
    /// </para>
    /// </remarks>
    public DateOnly? TrialEndsOn { get; set; }

    /// <summary>When the person creating the practice accepted the terms.</summary>
    /// <remarks>
    /// Recorded because consent with no timestamp is not evidence of anything. What it
    /// cannot record is <em>which</em> terms — there is no versioned document to point at
    /// yet, and the signup screen says so rather than implying a legal artefact exists.
    /// </remarks>
    public DateTime? TermsAcceptedUtc { get; set; }

    /// <summary>Whether the clinic is inside its free trial on the given day.</summary>
    public bool IsInTrial(DateOnly today) =>
        TrialEndsOn is { } ends && today <= ends;

    /// <summary>Days of trial left, or null where there is no trial running.</summary>
    public int? TrialDaysLeft(DateOnly today) =>
        TrialEndsOn is { } ends && today <= ends ? ends.DayNumber - today.DayNumber : null;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// True for the vendor's own tenant, which is not a subscribing clinic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Super admins are staff records like any other, and staff records are tenant-scoped.
    /// Rather than inventing a second kind of user that lives outside the tenant model,
    /// the vendor gets a tenant of its own and its operators are the providers in it. Sign
    /// in works unchanged, and — because the tenant filter applies to the platform tenant
    /// exactly as it does to a clinic — no practice can see or edit a super admin's
    /// account, and a super admin sees no clinic's patients.
    /// </para>
    /// <para>
    /// A flag rather than a well-known slug. The slug is the vendor's to change; what
    /// must not change is which row the app refuses to let anybody suspend.
    /// </para>
    /// </remarks>
    public bool IsPlatform { get; set; }
}
