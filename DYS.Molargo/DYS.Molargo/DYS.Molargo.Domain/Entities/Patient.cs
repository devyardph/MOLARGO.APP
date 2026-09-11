using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A person on the practice's books. The spine of the record: the diary, charting,
/// billing, claiming and recall features all hang off this identity.
/// </summary>
/// <remarks>
/// No EF Core attributes here by design — the local SQLite schema is configured fluently
/// in <c>DYS.Molargo.Shared.Data.MolargoDbContext</c>, which keeps this type free of a
/// persistence dependency that every other project would otherwise inherit.
/// </remarks>
public sealed class Patient : EntityBase
{
    // ---- identity --------------------------------------------------------

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    /// <summary>What the patient is actually called, where it is not their first name.</summary>
    public string? PreferredName { get; set; }

    public string? Title { get; set; }

    /// <summary>
    /// Human-facing patient number, shown on correspondence and quoted over the phone.
    /// Separate from <see cref="EntityBase.Id"/>: staff cannot read a GUID aloud, and the
    /// GUID has to stay fixed while this can be renumbered on a practice merge.
    /// </summary>
    public string? PatientNumber { get; set; }

    /// <summary>
    /// Date only, with no time component and no time zone. A date of birth is a calendar
    /// fact rather than an instant: held as a <see cref="DateTime"/> it shifts by a day
    /// the first time a value written in local time is read as UTC, which quietly changes
    /// a child's CDBS eligibility.
    /// </summary>
    public DateOnly? DateOfBirth { get; set; }

    public Sex Sex { get; set; } = Sex.Unspecified;

    // ---- contact ---------------------------------------------------------

    public string? Mobile { get; set; }

    public string? HomePhone { get; set; }

    public string? Email { get; set; }

    public string? AddressLine { get; set; }

    public string? Suburb { get; set; }

    public string? State { get; set; }

    public string? Postcode { get; set; }

    /// <summary>
    /// Preferred spoken language. Paired with <see cref="PatientTags.InterpreterNeeded"/>
    /// rather than implying it: plenty of patients prefer another language at home and
    /// need no interpreter in the surgery.
    /// </summary>
    public string? PreferredLanguage { get; set; }

    public string? EmergencyContactName { get; set; }

    public string? EmergencyContactRelationship { get; set; }

    public string? EmergencyContactPhone { get; set; }

    // ---- funding ---------------------------------------------------------

    /// <summary>Private health fund name, as claimed against through HICAPS.</summary>
    public string? HealthFund { get; set; }

    public string? HealthFundMemberNumber { get; set; }

    public string? MedicareNumber { get; set; }

    /// <summary>
    /// Position on the Medicare card, 1-9. Required on a claim and separate from the
    /// number, since a family shares the number.
    /// </summary>
    public int? MedicareReferenceNumber { get; set; }

    /// <summary>Department of Veterans' Affairs card number, where the patient holds one.</summary>
    public string? DvaNumber { get; set; }

    /// <summary>Concession card number — pensioner or health care card.</summary>
    public string? ConcessionCardNumber { get; set; }

    // ---- practice --------------------------------------------------------

    /// <summary>The site the patient normally attends.</summary>
    public Guid? PracticeLocationId { get; set; }

    /// <summary>The clinician they normally see, used to default a booking.</summary>
    public Guid? PreferredProviderId { get; set; }

    /// <summary>
    /// Links members of one household, so a family can be booked and billed together.
    /// A shared id on each member rather than a separate household table: the only
    /// question ever asked of it is "who else lives here", and it must survive a member
    /// moving out without orphaning a row.
    /// </summary>
    public Guid? HouseholdId { get; set; }

    /// <summary>How the patient found the practice, for marketing attribution.</summary>
    public string? ReferralSource { get; set; }

    public PatientStatus Status { get; set; } = PatientStatus.Active;

    public PatientTags Tags { get; set; } = PatientTags.None;

    /// <summary>Last attended appointment. Null for a lead who has never been in.</summary>
    public DateTime? LastSeenUtc { get; set; }

    /// <summary>
    /// Outstanding patient-payable amount. Denormalised onto the patient because every
    /// list row and record header shows it, and recomputing it from the ledger per row
    /// turned the patients screen into 25 extra queries.
    /// </summary>
    public decimal Balance { get; set; }

    /// <summary>
    /// Appointments missed without notice. Kept as a running count rather than derived
    /// from the appointment history: the front desk needs it on a list row, and the
    /// practice's FTA policy counts from the point it started counting, not from the
    /// beginning of time.
    /// </summary>
    public int FailedToAttendCount { get; set; }

    // ---- consent ---------------------------------------------------------

    /// <summary>
    /// Consent to receive marketing. Separate from reminders on purpose: a patient may
    /// refuse promotions while still wanting to be told about their appointment, and
    /// treating one as the other breaches the Spam Act.
    /// </summary>
    public bool MarketingConsent { get; set; }

    /// <summary>Consent to appointment reminders and recalls, which are operational.</summary>
    public bool ReminderConsent { get; set; } = true;

    /// <summary>
    /// When each consent last changed.
    /// </summary>
    /// <remarks>
    /// Per consent, not one timestamp for both. The Spam Act question is "when did this
    /// patient agree to marketing, and how" — answering it with the date the reminder
    /// preference was touched is not an answer, and a single field makes that mistake
    /// unavoidable.
    /// </remarks>
    public DateTime? MarketingConsentUpdatedUtc { get; set; }

    public DateTime? ReminderConsentUpdatedUtc { get; set; }

    /// <summary>
    /// How the consent was obtained — "Signed form · tablet", "Portal settings", "Phone
    /// request · logged". Stored because consent without a provenance is not evidence.
    /// </summary>
    public string? ConsentSource { get; set; }

    /// <summary>Free-text note shown on the record. Not a substitute for a clinical alert.</summary>
    public string? Notes { get; set; }

    // ---- computed --------------------------------------------------------

    /// <summary>For list rows and headers. Not persisted.</summary>
    public string FullName => $"{FirstName} {LastName}".Trim();

    /// <summary>How the patient should be addressed. Not persisted.</summary>
    public string DisplayName => $"{PreferredName ?? FirstName} {LastName}".Trim();

    /// <summary>
    /// Age in whole years at <paramref name="today"/>, or null with no date of birth.
    /// Takes the date rather than reading a clock, so "today" can be pinned in a test and
    /// is resolved once in the practice's own time zone.
    /// </summary>
    public int? AgeAt(DateOnly today)
    {
        if (DateOfBirth is not { } dob) return null;

        var age = today.Year - dob.Year;

        // Comparing month and day separately gets 29 February wrong in a non-leap year;
        // adding the years to the birth date and comparing whole dates does not.
        if (dob.AddYears(age) > today) age--;

        return age;
    }

    /// <summary>
    /// True where the patient is under 18 at <paramref name="today"/> — the gate on CDBS
    /// eligibility and on needing a guardian's consent.
    /// </summary>
    public bool IsMinorAt(DateOnly today) => AgeAt(today) is { } age && age < 18;
}
