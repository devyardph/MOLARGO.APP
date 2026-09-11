namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// Tooth surfaces, as flags: a restoration routinely spans several, and the combination
/// is what the ADA item number and the fee depend on.
/// </summary>
/// <remarks>
/// Explicit powers of two, persisted as one integer. Add new members with the next unused
/// bit and never renumber an existing one.
/// </remarks>
[Flags]
public enum ToothSurface
{
    None = 0,

    /// <summary>Biting surface of a molar or premolar.</summary>
    Occlusal = 1 << 0,

    /// <summary>Towards the midline.</summary>
    Mesial = 1 << 1,

    /// <summary>Away from the midline.</summary>
    Distal = 1 << 2,

    /// <summary>Towards the cheek or lip.</summary>
    Buccal = 1 << 3,

    /// <summary>Towards the tongue, on a lower tooth.</summary>
    Lingual = 1 << 4,

    /// <summary>Towards the palate, on an upper tooth.</summary>
    Palatal = 1 << 5,

    /// <summary>Biting edge of an incisor or canine.</summary>
    Incisal = 1 << 6,

    /// <summary>Root surface, below the crown.</summary>
    Root = 1 << 7,
}
