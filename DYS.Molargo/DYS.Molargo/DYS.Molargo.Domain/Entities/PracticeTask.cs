namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A job on the front desk's list — chase an account, rebook a no-show, ring the lab.
/// </summary>
/// <remarks>
/// A real entity rather than a note on a whiteboard because the whole point is that it
/// outlives the shift that created it and can be picked up by whoever is on next. Kept
/// deliberately thin: no assignment, no recurrence, no sub-tasks. Those are all plausible
/// and none of them is needed to work a day's list.
/// </remarks>
public sealed class PracticeTask : EntityBase
{
    public Guid PracticeLocationId { get; set; }

    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// When it needs doing. Date rather than timestamp: front-desk work is scheduled by
    /// day, and a time of day would be false precision nobody maintains.
    /// </summary>
    public DateOnly? DueOn { get; set; }

    /// <summary>
    /// Set when ticked off. A timestamp rather than a bool so the list can show what was
    /// cleared today, and so a task ticked by mistake can be told from one never done.
    /// </summary>
    public DateTime? CompletedUtc { get; set; }

    public bool IsDone => CompletedUtc is not null;

    /// <summary>The patient it concerns, where it has one. Opens their record from the list.</summary>
    public Guid? PatientId { get; set; }

    public Guid? AssignedToProviderId { get; set; }

    /// <summary>Ordering within a day, so the list is stable rather than reshuffling.</summary>
    public int DisplayOrder { get; set; }
}
