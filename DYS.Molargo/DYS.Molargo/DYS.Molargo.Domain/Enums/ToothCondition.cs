namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// What is charted on a tooth or surface. One entry per finding, so a tooth can carry a
/// restoration and a separate area of decay at the same time.
/// </summary>
public enum ToothCondition
{
    /// <summary>Charted and found sound.</summary>
    Sound = 0,

    Caries = 1,

    /// <summary>An existing filling.</summary>
    Restoration = 2,

    Crown = 3,
    Bridge = 4,
    Veneer = 5,
    Implant = 6,

    /// <summary>Root canal treated.</summary>
    RootCanalTreated = 7,

    /// <summary>Extracted, and the space left.</summary>
    Missing = 8,

    /// <summary>Never developed.</summary>
    Unerupted = 9,

    /// <summary>Present but not through the gum, or partly through.</summary>
    Impacted = 10,

    Fractured = 11,

    /// <summary>Wear from grinding, acid or abrasion.</summary>
    Wear = 12,

    /// <summary>Periodontal finding — recession, pocketing, mobility.</summary>
    Periodontal = 13,

    /// <summary>Charted for review at the next visit rather than treated now.</summary>
    Watch = 14,
}
