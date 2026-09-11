using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A referral to or from another provider. One entity for both directions, because the
/// practice works a single list of everything in flight.
/// </summary>
public sealed class Referral : EntityBase
{
    public Guid PatientId { get; set; }

    public ReferralDirection Direction { get; set; }

    public ReferralStatus Status { get; set; } = ReferralStatus.Draft;

    /// <summary>The clinician here who wrote or received it.</summary>
    public Guid ProviderId { get; set; }

    /// <summary>The provider at the other end. Free text — most are outside the system.</summary>
    public string CounterpartyName { get; set; } = string.Empty;

    /// <summary>Their field — "Periodontics", "Oral surgery".</summary>
    public string? CounterpartySpecialty { get; set; }

    public string? CounterpartyPractice { get; set; }

    public string? CounterpartyPhone { get; set; }

    public string? CounterpartyEmail { get; set; }

    /// <summary>Why the patient is being referred, and what is asked of the specialist.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>The tooth or region concerned.</summary>
    public string? RelatesTo { get; set; }

    /// <summary>The letter itself, as sent.</summary>
    public string? LetterBody { get; set; }

    public DateTime? SentUtc { get; set; }

    public DateTime? AttendedUtc { get; set; }

    /// <summary>
    /// Set when the specialist's report comes back, which is what closes the loop. An
    /// outbound referral with no report is the thing this list exists to surface.
    /// </summary>
    public DateTime? ReportReceivedUtc { get; set; }

    /// <summary>The filed report.</summary>
    public Guid? ReportDocumentId { get; set; }

    /// <summary>True where the referral needs to be seen urgently — suspected pathology.</summary>
    public bool IsUrgent { get; set; }
}
