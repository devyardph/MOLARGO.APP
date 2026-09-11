using System.Globalization;
using DYS.Molargo.Domain.Enums;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Features.Patient.Services;

/// <summary>
/// What the patient form holds while it is being filled in.
/// </summary>
/// <remarks>
/// <para>
/// A mutable form model, not the entity and not <c>PatientDto</c>. The entity carries
/// audit stamps and a soft-delete flag no form should be able to set; the DTO is the wire
/// shape. This is neither — it is the half-finished state of a person typing, which is a
/// third thing.
/// </para>
/// <para>
/// Dates and the fund line are strings for the same reason: a field holds whatever has
/// been typed so far, including "12/03" and "1968-0", and a nullable date cannot
/// represent half a date without throwing the keystrokes away.
/// </para>
/// <para>
/// No validation attributes. Validation lives in <c>PatientEditViewModel</c>, so it can
/// be tested without a form and so <c>DYS.Molargo.Domain</c> stays free of a validation
/// framework it would otherwise inherit through this type.
/// </para>
/// </remarks>
public sealed class PatientForm
{
    /// <summary>
    /// The date format the form accepts and shows. Australian order, and the one the
    /// prototype's placeholder promises.
    /// </summary>
    public const string DateFormat = "dd/MM/yyyy";

    /// <summary>Empty for a new patient; the existing id when editing.</summary>
    public Guid Id { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PreferredName { get; set; }

    /// <summary>Assigned on save for a new patient, and never editable after.</summary>
    public string? PatientNumber { get; set; }

    /// <summary><see cref="DateFormat"/>, or whatever the user has typed so far.</summary>
    public string DateOfBirth { get; set; } = string.Empty;

    public Sex Sex { get; set; } = Sex.Unspecified;

    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? AddressLine { get; set; }
    public string? Suburb { get; set; }
    public string? State { get; set; }
    public string? Postcode { get; set; }
    public string? PreferredLanguage { get; set; } = "English";

    public string? HealthFund { get; set; }
    public string? HealthFundMemberNumber { get; set; }
    public string? MedicareNumber { get; set; }

    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactRelationship { get; set; }
    public string? EmergencyContactPhone { get; set; }

    /// <summary>Null for "no household".</summary>
    public Guid? HouseholdId { get; set; }

    public PatientTags Tags { get; set; } = PatientTags.None;

    public bool ReminderConsent { get; set; } = true;
    public bool MarketingConsent { get; set; }

    public string? Notes { get; set; }

    // ---- medical summary -------------------------------------------------

    /// <summary>
    /// Quick-entry for the alert rows, one per line or separated by "·".
    /// </summary>
    /// <remarks>
    /// These three fields <em>create</em> <c>PatientAlert</c> rows and never rewrite
    /// existing ones. Parsing edited free text back onto rows that already exist would
    /// silently drop each one's severity, onset date and who recorded it — so on an edit
    /// the form shows the alerts already on file read-only, and anything typed here is
    /// added alongside them.
    /// </remarks>
    public string? Allergies { get; set; }

    public string? Medications { get; set; }

    public string? Conditions { get; set; }

    // ---- conversion ------------------------------------------------------

    /// <summary>The date of birth as a real date, or null if it is not one yet.</summary>
    public DateOnly? ParsedDateOfBirth =>
        DateOnly.TryParseExact(DateOfBirth, DateFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    /// <summary>True when the field holds something that is not a date in the expected form.</summary>
    public bool HasUnparseableDateOfBirth =>
        !string.IsNullOrWhiteSpace(DateOfBirth) && ParsedDateOfBirth is null;

    /// <summary>Builds a form from an existing patient, for editing.</summary>
    public static PatientForm From(PatientEntity patient) => new()
    {
        Id = patient.Id,
        FirstName = patient.FirstName,
        LastName = patient.LastName,
        PreferredName = patient.PreferredName,
        PatientNumber = patient.PatientNumber,
        DateOfBirth = patient.DateOfBirth?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? string.Empty,
        Sex = patient.Sex,
        Mobile = patient.Mobile,
        Email = patient.Email,
        AddressLine = patient.AddressLine,
        Suburb = patient.Suburb,
        State = patient.State,
        Postcode = patient.Postcode,
        PreferredLanguage = patient.PreferredLanguage,
        HealthFund = patient.HealthFund,
        HealthFundMemberNumber = patient.HealthFundMemberNumber,
        MedicareNumber = patient.MedicareNumber,
        EmergencyContactName = patient.EmergencyContactName,
        EmergencyContactRelationship = patient.EmergencyContactRelationship,
        EmergencyContactPhone = patient.EmergencyContactPhone,
        HouseholdId = patient.HouseholdId,
        Tags = patient.Tags,
        ReminderConsent = patient.ReminderConsent,
        MarketingConsent = patient.MarketingConsent,
        Notes = patient.Notes,
    };

    /// <summary>
    /// Applies the form onto a patient entity.
    /// </summary>
    /// <remarks>
    /// Applies onto an existing instance rather than constructing one, so an edit keeps
    /// every field the form does not carry — the balance, the last-seen date, the FTA
    /// count, the audit stamps. Building a fresh entity from the form would zero all of
    /// them on the first save.
    /// </remarks>
    public void ApplyTo(PatientEntity patient)
    {
        patient.FirstName = FirstName.Trim();
        patient.LastName = LastName.Trim();
        patient.PreferredName = Blank(PreferredName);
        patient.PatientNumber = Blank(PatientNumber);
        patient.DateOfBirth = ParsedDateOfBirth;
        patient.Sex = Sex;
        patient.Mobile = Blank(Mobile);
        patient.Email = Blank(Email);
        patient.AddressLine = Blank(AddressLine);
        patient.Suburb = Blank(Suburb);
        patient.State = Blank(State);
        patient.Postcode = Blank(Postcode);
        patient.PreferredLanguage = Blank(PreferredLanguage);
        patient.HealthFund = Blank(HealthFund);
        patient.HealthFundMemberNumber = Blank(HealthFundMemberNumber);
        patient.MedicareNumber = Blank(MedicareNumber);
        patient.EmergencyContactName = Blank(EmergencyContactName);
        patient.EmergencyContactRelationship = Blank(EmergencyContactRelationship);
        patient.EmergencyContactPhone = Blank(EmergencyContactPhone);
        patient.HouseholdId = HouseholdId;
        patient.Tags = Tags;
        patient.ReminderConsent = ReminderConsent;
        patient.MarketingConsent = MarketingConsent;
        patient.Notes = Blank(Notes);
    }

    /// <summary>
    /// Fills in what the existing patient is missing, and changes nothing it already has.
    /// </summary>
    /// <remarks>
    /// This is what "apply to the existing record" has to mean. <see cref="ApplyTo"/>
    /// writes every field including the blank ones, which is right for an edit — clearing
    /// a field is a real intention there. On a merge it is destructive: the new entry is
    /// typically just a name, a date of birth and a phone number, so applying it wholesale
    /// wiped the existing patient's email, address, fund and Medicare details.
    ///
    /// Flags and consents are unioned rather than replaced, for the same reason: a sparse
    /// new entry saying nothing about "nervous patient" is not the same as saying no.
    /// </remarks>
    public void MergeInto(PatientEntity patient)
    {
        patient.FirstName = Fill(patient.FirstName, FirstName) ?? patient.FirstName;
        patient.LastName = Fill(patient.LastName, LastName) ?? patient.LastName;
        patient.PreferredName = Fill(patient.PreferredName, PreferredName);
        patient.DateOfBirth ??= ParsedDateOfBirth;
        patient.Mobile = Fill(patient.Mobile, Mobile);
        patient.Email = Fill(patient.Email, Email);
        patient.AddressLine = Fill(patient.AddressLine, AddressLine);
        patient.Suburb = Fill(patient.Suburb, Suburb);
        patient.State = Fill(patient.State, State);
        patient.Postcode = Fill(patient.Postcode, Postcode);
        patient.PreferredLanguage = Fill(patient.PreferredLanguage, PreferredLanguage);
        patient.HealthFund = Fill(patient.HealthFund, HealthFund);
        patient.HealthFundMemberNumber = Fill(patient.HealthFundMemberNumber, HealthFundMemberNumber);
        patient.MedicareNumber = Fill(patient.MedicareNumber, MedicareNumber);
        patient.EmergencyContactName = Fill(patient.EmergencyContactName, EmergencyContactName);
        patient.EmergencyContactRelationship = Fill(patient.EmergencyContactRelationship, EmergencyContactRelationship);
        patient.EmergencyContactPhone = Fill(patient.EmergencyContactPhone, EmergencyContactPhone);
        patient.HouseholdId ??= HouseholdId;

        patient.Tags |= Tags;
        patient.ReminderConsent |= ReminderConsent;
        patient.MarketingConsent |= MarketingConsent;

        // Appended, not replaced: a front-desk note on the existing record is somebody's
        // observation, and the new entry's note is another one.
        var notes = new[] { patient.Notes, Blank(Notes) }
            .Where(note => !string.IsNullOrWhiteSpace(note))
            .ToList();

        patient.Notes = notes.Count == 0 ? null : string.Join(Environment.NewLine, notes);
    }

    /// <summary>Keeps what is already there; uses the form's value only to fill a blank.</summary>
    private static string? Fill(string? existing, string? incoming) =>
        string.IsNullOrWhiteSpace(existing) ? Blank(incoming) : existing;

    /// <summary>
    /// Turns "" into null, so an emptied field is absent rather than an empty string.
    /// The difference matters to every <c>is { Length: &gt; 0 }</c> check on the record
    /// screen, and to the chips that only render when a value is really there.
    /// </summary>
    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
