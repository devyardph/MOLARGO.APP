using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Dtos;

/// <summary>
/// A patient as the UI exchanges one — the shape an edit form binds to and, later, the
/// shape that goes on the wire.
/// </summary>
/// <remarks>
/// <para>
/// A separate type from <see cref="Entities.Patient"/> even though most fields line up.
/// The entity carries audit and soft-delete bookkeeping that no form should be able to
/// set, and this contract has to be free to diverge from the local schema when the API
/// arrives.
/// </para>
/// <para>
/// The date of birth is a string here and a <c>DateOnly</c> on the entity. A form field
/// holds whatever the user has typed so far, including something not yet a valid date, and
/// a nullable date cannot represent "half-entered" without discarding the keystrokes.
/// </para>
/// </remarks>
public sealed class PatientDto
{
    public Guid Id { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PreferredName { get; set; }
    public string? Title { get; set; }
    public string? PatientNumber { get; set; }

    /// <summary>ISO <c>yyyy-MM-dd</c>. See the remarks on why this is not a date type.</summary>
    public string? DateOfBirth { get; set; }

    public Sex Sex { get; set; } = Sex.Unspecified;

    public string? Mobile { get; set; }
    public string? HomePhone { get; set; }
    public string? Email { get; set; }
    public string? AddressLine { get; set; }
    public string? Suburb { get; set; }
    public string? State { get; set; }
    public string? Postcode { get; set; }
    public string? PreferredLanguage { get; set; }

    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactRelationship { get; set; }
    public string? EmergencyContactPhone { get; set; }

    public string? HealthFund { get; set; }
    public string? HealthFundMemberNumber { get; set; }
    public string? MedicareNumber { get; set; }
    public int? MedicareReferenceNumber { get; set; }
    public string? DvaNumber { get; set; }
    public string? ConcessionCardNumber { get; set; }

    public Guid? PracticeLocationId { get; set; }
    public Guid? PreferredProviderId { get; set; }
    public Guid? HouseholdId { get; set; }
    public string? ReferralSource { get; set; }

    public PatientStatus Status { get; set; } = PatientStatus.Active;
    public PatientTags Tags { get; set; } = PatientTags.None;

    public DateTime? LastSeenUtc { get; set; }
    public decimal Balance { get; set; }
    public int FailedToAttendCount { get; set; }

    public bool MarketingConsent { get; set; }
    public bool ReminderConsent { get; set; } = true;

    public string? Notes { get; set; }
}
