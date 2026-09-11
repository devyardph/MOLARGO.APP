namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A chair, and one column of the diary. Called an operatory rather than a room because a
/// room can hold two.
/// </summary>
public sealed class Operatory : EntityBase
{
    public Guid PracticeLocationId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Left-to-right position in the diary. Staff learn the layout by position.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// True where the chair is equipped for surgical work, so the diary can keep a
    /// routine clean out of the only room an emergency extraction can use.
    /// </summary>
    public bool IsSurgical { get; set; }

    /// <summary>A chair out of service takes no bookings but keeps its history.</summary>
    public bool IsActive { get; set; } = true;
}
