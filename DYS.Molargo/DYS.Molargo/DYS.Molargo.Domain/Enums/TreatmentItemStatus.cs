namespace DYS.Molargo.Domain.Enums;

/// <summary>Where one line of a treatment plan sits.</summary>
public enum TreatmentItemStatus
{
    Planned = 0,

    /// <summary>An appointment exists for it.</summary>
    Scheduled = 1,

    Completed = 2,

    /// <summary>The patient declined this line while accepting others.</summary>
    Declined = 3,

    /// <summary>No longer needed — the tooth was extracted instead, say.</summary>
    Void = 4,
}
