namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A billable procedure — the ADA item number, its description and the practice's fee.
///
/// The fee lives here as the current schedule; the fee actually charged is copied onto the
/// invoice line at the time. Reading the price back through this row would silently
/// restate every historical invoice the next time the schedule went up.
/// </summary>
public sealed class ProcedureCode : EntityBase
{
    /// <summary>ADA item number — "011", "613". A string, not an int: leading zeros matter.</summary>
    public string ItemNumber { get; set; } = string.Empty;

    /// <summary>The schedule's own wording, which is what has to appear on a claim.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>The practice's plain-English label for a treatment plan the patient reads.</summary>
    public string? PatientFriendlyName { get; set; }

    /// <summary>Schedule grouping — "Diagnostic", "Restorative", "Endodontics".</summary>
    public string? Category { get; set; }

    public decimal Fee { get; set; }

    /// <summary>
    /// True where the item is charged per tooth, so a plan covering three teeth is three
    /// lines rather than one with a quantity — the claim has to name each tooth.
    /// </summary>
    public bool IsPerTooth { get; set; }

    /// <summary>True where the item requires a surface to be nominated on the claim.</summary>
    public bool RequiresSurface { get; set; }

    /// <summary>Typical chair time, used to size an appointment booked from a plan.</summary>
    public int? TypicalDurationMinutes { get; set; }

    /// <summary>Claimable under the Child Dental Benefits Schedule.</summary>
    public bool IsCdbsEligible { get; set; }

    /// <summary>Superseded items stay for historical invoices but cannot be newly charged.</summary>
    public bool IsActive { get; set; } = true;
}
