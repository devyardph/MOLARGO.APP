using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A patient wanting an earlier appointment than the one they hold — the short-notice
/// list a cancelled slot is offered to.
/// </summary>
public sealed class WaitlistEntry : EntityBase
{
    public Guid PatientId { get; set; }

    public Guid PracticeLocationId { get; set; }

    /// <summary>Set where the patient will only see one clinician.</summary>
    public Guid? PreferredProviderId { get; set; }

    public Guid? AppointmentTypeId { get; set; }

    public WaitlistPriority Priority { get; set; } = WaitlistPriority.Routine;

    /// <summary>What they are waiting for, in the patient's words.</summary>
    public string? Reason { get; set; }

    /// <summary>
    /// When they can come in, as free text — "mornings only", "not Thursdays". Free text
    /// rather than structured availability because the front desk reads it aloud on the
    /// phone, and every structured version of this has been more work to fill in than it
    /// saved.
    /// </summary>
    public string? Availability { get; set; }

    /// <summary>Earliest date worth calling them about.</summary>
    public DateOnly? AvailableFrom { get; set; }

    /// <summary>
    /// After this the entry is stale — the patient's regular appointment has come round.
    /// </summary>
    public DateOnly? AvailableUntil { get; set; }

    /// <summary>The appointment they already hold, which a short-notice slot would replace.</summary>
    public Guid? CurrentAppointmentId { get; set; }

    /// <summary>Set once they have been given a slot, which closes the entry.</summary>
    public DateTime? FulfilledUtc { get; set; }

    /// <summary>Last time someone rang and got no answer. Drives the call order.</summary>
    public DateTime? LastContactedUtc { get; set; }

    public bool IsOpen => FulfilledUtc is null && !IsDeleted;
}
