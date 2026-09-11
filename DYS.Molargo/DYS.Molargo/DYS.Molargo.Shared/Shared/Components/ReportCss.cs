namespace DYS.Molargo.Shared.Components;

/// <summary>
/// How the reports screen draws bars and colours figures.
/// </summary>
/// <remarks>
/// Whole literal class strings, never composed at runtime: Tailwind scans source text, so
/// a class built by concatenation is a class that never reaches the stylesheet. The one
/// exception is a bar's width, which is an inline style rather than a class because it is
/// a continuous value — there is no utility for "63%".
/// </remarks>
public static class ReportCss
{
    /// <summary>The track a bar sits in.</summary>
    public const string BarTrack = "bg-neutral-200";

    /// <summary>
    /// A bar's fill.
    /// </summary>
    /// <remarks>
    /// The accent marks the outcome step of a funnel — booked, attended, accepted — and
    /// dark neutral marks the steps before it. The design does the same: colouring every
    /// bar would leave nothing to read as the result.
    /// </remarks>
    public static string BarFill(bool isOutcome) => isOutcome
        ? "h-4 bg-accent"
        : "h-4 bg-neutral-800";

    /// <summary>A bar's width, as an inline style. Clamped so a rounding error cannot overflow.</summary>
    public static string BarWidth(decimal percent) =>
        $"width:{Math.Clamp(percent, 0m, 100m).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}%";

    /// <summary>
    /// A KPI tile's label.
    /// </summary>
    /// <remarks>
    /// The FTA tile is accented in the design, and it is the only one: it is the figure a
    /// practice acts on this week, and the rest are the figures it reads.
    /// </remarks>
    public static string TileLabel(bool isWarning) => isWarning
        ? "text-[10px] tracking-[0.1em] text-accent-700 uppercase"
        : "text-[10px] tracking-[0.1em] text-neutral-600 uppercase";

    public static string TileValue(bool isWarning) => isWarning
        ? "font-heading text-2xl font-extrabold text-accent-700"
        : "font-heading text-2xl font-extrabold";

    /// <summary>
    /// A chair-utilisation figure.
    /// </summary>
    /// <remarks>
    /// Low utilisation is the finding, not high: an idle chair is unbooked capacity, which
    /// is what the gap-fill list exists to recover.
    /// </remarks>
    public static string UtilisationValue(decimal? percent) => percent < 65m
        ? "text-right font-bold text-accent-700"
        : "text-right font-bold";
}
