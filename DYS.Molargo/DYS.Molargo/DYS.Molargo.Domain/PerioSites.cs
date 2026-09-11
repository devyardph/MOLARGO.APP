using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain;

/// <summary>
/// One of the six blocks a mouth is probed in.
/// </summary>
/// <remarks>
/// Sextants, not quadrants. A perio chart is probed and reviewed a sextant at a time —
/// it is the unit the design's "UR sextant shown" refers to, and the unit that fits on a
/// tablet without scrolling.
/// </remarks>
public enum PerioSextant
{
    UpperRight = 0,
    UpperAnterior = 1,
    UpperLeft = 2,
    LowerLeft = 3,
    LowerAnterior = 4,
    LowerRight = 5,
}

/// <summary>
/// Which teeth are in which sextant, and which sites sit on which side of a tooth.
/// </summary>
public static class PerioSites
{
    /// <summary>
    /// The six sites in probing order: across the buccal, then across the lingual.
    /// </summary>
    /// <remarks>
    /// This order is the order a clinician calls the numbers out in, so entry follows the
    /// hand rather than the enum. Charting out of probing order is how a reading lands on
    /// the wrong site.
    /// </remarks>
    public static readonly PerioSite[] ProbingOrder =
    [
        PerioSite.MesioBuccal, PerioSite.Buccal, PerioSite.DistoBuccal,
        PerioSite.MesioLingual, PerioSite.Lingual, PerioSite.DistoLingual,
    ];

    public static readonly PerioSite[] BuccalSites =
        [PerioSite.MesioBuccal, PerioSite.Buccal, PerioSite.DistoBuccal];

    public static readonly PerioSite[] LingualSites =
        [PerioSite.MesioLingual, PerioSite.Lingual, PerioSite.DistoLingual];

    /// <summary>Whether the site is on the cheek side.</summary>
    public static bool IsBuccal(PerioSite site) => site is
        PerioSite.MesioBuccal or PerioSite.Buccal or PerioSite.DistoBuccal;

    /// <summary>The three sites on the same side as <paramref name="site"/>.</summary>
    public static PerioSite[] SidesOf(PerioSite site) =>
        IsBuccal(site) ? BuccalSites : LingualSites;

    /// <summary>"MB", "B", "DB" — the two-letter form written on a paper chart.</summary>
    public static string ShortLabel(PerioSite site) => site switch
    {
        PerioSite.MesioBuccal => "MB",
        PerioSite.Buccal => "B",
        PerioSite.DistoBuccal => "DB",
        PerioSite.MesioLingual => "ML",
        PerioSite.Lingual => "L",
        PerioSite.DistoLingual => "DL",
        _ => string.Empty,
    };

    /// <summary>
    /// Spelled out, for a tooltip and for a screen reader — "MB" is not a word.
    /// </summary>
    /// <param name="fdi">
    /// Decides whether the tongue side is called lingual or palatal, which is the term the
    /// clinician expects to see on an upper tooth.
    /// </param>
    public static string LongLabel(PerioSite site, string fdi)
    {
        var tongueSide = IsUpper(fdi) ? "palatal" : "lingual";

        return site switch
        {
            PerioSite.MesioBuccal => "mesio-buccal",
            PerioSite.Buccal => "buccal",
            PerioSite.DistoBuccal => "disto-buccal",
            PerioSite.MesioLingual => $"mesio-{tongueSide}",
            PerioSite.Lingual => tongueSide,
            PerioSite.DistoLingual => $"disto-{tongueSide}",
            _ => string.Empty,
        };
    }

    public static string SextantLabel(PerioSextant sextant) => sextant switch
    {
        PerioSextant.UpperRight => "Upper right",
        PerioSextant.UpperAnterior => "Upper front",
        PerioSextant.UpperLeft => "Upper left",
        PerioSextant.LowerLeft => "Lower left",
        PerioSextant.LowerAnterior => "Lower front",
        PerioSextant.LowerRight => "Lower right",
        _ => string.Empty,
    };

    /// <summary>The short form used on the sextant switcher — "UR", "LA".</summary>
    public static string SextantCode(PerioSextant sextant) => sextant switch
    {
        PerioSextant.UpperRight => "UR",
        PerioSextant.UpperAnterior => "UA",
        PerioSextant.UpperLeft => "UL",
        PerioSextant.LowerLeft => "LL",
        PerioSextant.LowerAnterior => "LA",
        PerioSextant.LowerRight => "LR",
        _ => string.Empty,
    };

    /// <summary>
    /// The permanent teeth of one sextant, in FDI, left to right as the chart draws them.
    /// </summary>
    /// <remarks>
    /// Molars and premolars in the posterior sextants, canine to canine in the anterior
    /// ones — the conventional 3-3 split, so the front sextants hold six teeth.
    /// </remarks>
    public static string[] TeethIn(PerioSextant sextant) => sextant switch
    {
        PerioSextant.UpperRight => ["18", "17", "16", "15", "14"],
        PerioSextant.UpperAnterior => ["13", "12", "11", "21", "22", "23"],
        PerioSextant.UpperLeft => ["24", "25", "26", "27", "28"],
        PerioSextant.LowerLeft => ["34", "35", "36", "37", "38"],
        PerioSextant.LowerAnterior => ["33", "32", "31", "41", "42", "43"],
        PerioSextant.LowerRight => ["44", "45", "46", "47", "48"],
        _ => [],
    };

    /// <summary>Which sextant a tooth belongs to, or null if the number is not permanent.</summary>
    public static PerioSextant? SextantOf(string fdi)
    {
        foreach (var sextant in Enum.GetValues<PerioSextant>())
        {
            if (TeethIn(sextant).Contains(fdi)) return sextant;
        }

        return null;
    }

    /// <summary>
    /// Whether the tooth is multi-rooted, and so whether furcation can be probed at all.
    /// </summary>
    /// <remarks>
    /// Molars only, by FDI position. Upper first premolars are commonly two-rooted, but
    /// the furcation sits too far apically to probe, so recording one there would be a
    /// number nobody could have measured.
    /// </remarks>
    public static bool HasFurcation(string fdi) =>
        fdi.Length == 2 && fdi[1] is '6' or '7' or '8';

    private static bool IsUpper(string fdi) =>
        fdi.Length == 2 && fdi[0] is '1' or '2' or '5' or '6';
}
