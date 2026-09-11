namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// Glickman furcation involvement. Only meaningful on multi-rooted teeth.
/// </summary>
public enum FurcationGrade
{
    None = 0,

    /// <summary>Probe enters the furcation but does not pass between the roots.</summary>
    Grade1 = 1,

    /// <summary>Probe enters but does not pass through.</summary>
    Grade2 = 2,

    /// <summary>Probe passes right through the furcation.</summary>
    Grade3 = 3,
}
