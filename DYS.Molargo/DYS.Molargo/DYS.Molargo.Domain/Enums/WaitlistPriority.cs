namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// How eagerly a waitlist entry should be offered a cancelled slot.
/// </summary>
public enum WaitlistPriority
{
    /// <summary>Happy to wait for a routine opening.</summary>
    Routine = 0,

    /// <summary>Wants an earlier slot if one appears.</summary>
    Preferred = 1,

    /// <summary>In pain. Offered first, ahead of any routine entry.</summary>
    Urgent = 2,
}
