using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain;

/// <summary>
/// How a set of tooth surfaces is written down.
/// </summary>
/// <remarks>
/// In Domain, not in a view helper, because "MOD" is clinical vocabulary rather than
/// presentation: it goes on the chart, into a referral letter and onto an invoice line,
/// and those must not disagree. It lived in <c>ChartCss</c> until a referral letter needed
/// it too and the choice was between a service reaching into a CSS class or a second copy
/// of the ordering.
/// </remarks>
public static class ToothSurfaces
{
    /// <summary>
    /// The conventional order surfaces are written in — MOIDBLPR, not the order they were
    /// tapped in. A restoration is "MO", never "OM", and a clinician reading "OM" pauses.
    /// </summary>
    private static readonly (ToothSurface Flag, string Code)[] Order =
    [
        (ToothSurface.Mesial, "M"),
        (ToothSurface.Occlusal, "O"),
        (ToothSurface.Incisal, "I"),
        (ToothSurface.Distal, "D"),
        (ToothSurface.Buccal, "B"),
        (ToothSurface.Lingual, "L"),
        (ToothSurface.Palatal, "P"),
        (ToothSurface.Root, "R"),
    ];

    /// <summary>"MOD" for mesial, occlusal and distal. Empty for <c>None</c>.</summary>
    public static string Code(ToothSurface surfaces) =>
        string.Concat(Order
            .Where(entry => surfaces.HasFlag(entry.Flag))
            .Select(entry => entry.Code));
}
