using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One probing reading: one site of one tooth in one exam.
/// </summary>
/// <remarks>
/// <para>
/// A row per site rather than a wide row per tooth. Two reasons, and the second is the one
/// that decided it: "which sites are 5mm or deeper" is the question every perio review
/// asks, and it is a <c>WHERE</c> clause here versus six column comparisons there; and a
/// partial exam stores only the sites actually probed, instead of a full-mouth row padded
/// with nulls that reads as a complete examination.
/// </para>
/// <para>
/// No reading means no row. Null and absent both mean "not assessed" — which is a
/// different clinical statement from a recorded 0mm.
/// </para>
/// </remarks>
public sealed class PerioSiteReading : EntityBase
{
    public Guid PerioExamId { get; set; }

    /// <summary>FDI, matching <c>ToothChartEntry</c>. See <c>ToothNumbering</c>.</summary>
    public string ToothNumber { get; set; } = string.Empty;

    public PerioSite Site { get; set; }

    /// <summary>Pocket depth in millimetres, gingival margin to base of pocket.</summary>
    public int? ProbingDepthMm { get; set; }

    /// <summary>
    /// Gingival recession in millimetres. Negative where the margin sits above the
    /// cemento-enamel junction, which happens with swelling and overgrowth.
    /// </summary>
    public int? RecessionMm { get; set; }

    /// <summary>Bleeding on probing — the activity marker the whole exam turns on.</summary>
    public bool Bleeding { get; set; }

    public bool Suppuration { get; set; }

    /// <summary>
    /// Clinical attachment loss: depth plus recession.
    /// </summary>
    /// <remarks>
    /// The figure that actually tracks disease. Pocket depth alone moves when the gum
    /// swells or shrinks, so a pocket reading better after treatment can hide continuing
    /// attachment loss underneath.
    /// </remarks>
    public int? AttachmentLossMm => ProbingDepthMm is { } depth
        ? depth + (RecessionMm ?? 0)
        : null;
}
