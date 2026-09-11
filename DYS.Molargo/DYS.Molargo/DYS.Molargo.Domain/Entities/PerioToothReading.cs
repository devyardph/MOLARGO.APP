using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// The findings that belong to a whole tooth rather than to one probed site.
/// </summary>
/// <remarks>
/// Separate from <see cref="PerioSiteReading"/> because mobility and furcation are
/// properties of the tooth, and hanging them off an arbitrary site — the buccal, say —
/// makes them disappear the moment that site is not probed.
/// </remarks>
public sealed class PerioToothReading : EntityBase
{
    public Guid PerioExamId { get; set; }

    public string ToothNumber { get; set; } = string.Empty;

    /// <summary>Null where mobility was not assessed, as opposed to assessed as none.</summary>
    public ToothMobility? Mobility { get; set; }

    /// <summary>Null on a single-rooted tooth, where the reading has no meaning.</summary>
    public FurcationGrade? Furcation { get; set; }

    /// <summary>Plaque present anywhere on the tooth, for the hygiene score.</summary>
    public bool Plaque { get; set; }

    public string? Notes { get; set; }
}
