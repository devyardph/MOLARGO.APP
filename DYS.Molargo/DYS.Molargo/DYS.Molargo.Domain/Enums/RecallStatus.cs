namespace DYS.Molargo.Domain.Enums;

/// <summary>Where a recall sits in the reminder cycle.</summary>
public enum RecallStatus
{
    /// <summary>Scheduled for a future date. Nothing to do yet.</summary>
    Pending = 0,

    /// <summary>Due now and not yet contacted.</summary>
    Due = 1,

    /// <summary>Contacted and awaiting a reply.</summary>
    Contacted = 2,

    /// <summary>An appointment was made. Closes the recall.</summary>
    Booked = 3,

    /// <summary>Past due with no reply after the configured attempts.</summary>
    Overdue = 4,

    /// <summary>The patient asked not to be recalled, or moved away.</summary>
    Declined = 5,
}
