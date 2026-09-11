namespace DYS.Molargo.Domain.Enums;

/// <summary>Miller mobility, recorded per tooth rather than per site.</summary>
public enum ToothMobility
{
    /// <summary>Physiological. Recorded explicitly so "not mobile" is distinguishable
    /// from "not assessed", which is null.</summary>
    None = 0,

    /// <summary>Up to 1mm horizontal.</summary>
    Grade1 = 1,

    /// <summary>More than 1mm horizontal.</summary>
    Grade2 = 2,

    /// <summary>Horizontal and vertical — the tooth depresses in its socket.</summary>
    Grade3 = 3,
}
