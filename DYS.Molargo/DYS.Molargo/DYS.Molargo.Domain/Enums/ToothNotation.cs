namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// Which numbering system a tooth reference uses. Stored per chart entry rather than
/// assumed practice-wide: a referral or a set of scanned notes arrives in whatever system
/// its author used, and silently reinterpreting "36" between systems names a different
/// tooth.
/// </summary>
public enum ToothNotation
{
    /// <summary>FDI two-digit — quadrant then tooth. The Australian norm.</summary>
    Fdi = 0,

    /// <summary>Universal numbering, 1-32. Common on US material.</summary>
    Universal = 1,

    /// <summary>Palmer notation, quadrant symbol plus number.</summary>
    Palmer = 2,
}
