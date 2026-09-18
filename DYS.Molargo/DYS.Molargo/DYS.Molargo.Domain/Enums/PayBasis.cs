namespace DYS.Molargo.Domain.Enums;

/// <summary>How a clinician is paid.</summary>
/// <remarks>
/// The three arrangements a dental practice actually uses. Which one applies changes what
/// the practice owes and when — a production share is earned the day the work is done, a
/// collections share only when the money arrives — so it is recorded per person rather
/// than assumed.
/// </remarks>
public enum PayBasis
{
    /// <summary>No arrangement recorded. Pay cannot be worked out.</summary>
    None = 0,

    /// <summary>A percentage of what they billed, earned when the work is done.</summary>
    ProductionShare = 1,

    /// <summary>
    /// A percentage of what was actually collected against their work.
    /// </summary>
    /// <remarks>
    /// Not the same as a production share and routinely confused with it. Work billed and
    /// never paid earns nothing here, which is the point of the arrangement.
    /// </remarks>
    CollectionShare = 2,

    /// <summary>An hourly rate, optionally with a bonus over a production target.</summary>
    Hourly = 3,
}
