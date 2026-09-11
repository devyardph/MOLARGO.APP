namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A dental supplier or laboratory the practice orders from.
/// </summary>
public sealed class Supplier : EntityBase
{
    public string Name { get; set; } = string.Empty;

    public string? AccountNumber { get; set; }

    public string? ContactName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }

    /// <summary>
    /// Usual lead time in days, used to work out when to reorder rather than when stock
    /// runs out.
    /// </summary>
    public int? LeadTimeDays { get; set; }

    /// <summary>True where this supplier is a dental laboratory rather than a consumables merchant.</summary>
    public bool IsLaboratory { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }
}
