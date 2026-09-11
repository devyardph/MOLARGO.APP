using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain;

/// <summary>
/// The teeth in one arch, in the order they appear on screen.
/// </summary>
/// <param name="Upper">Upper arch, patient's right to left — viewer's left to right.</param>
/// <param name="Lower">Lower arch, in the same direction.</param>
public sealed record ToothArches(IReadOnlyList<string> Upper, IReadOnlyList<string> Lower);

/// <summary>
/// Which teeth exist, and what to call them in each numbering system.
/// </summary>
/// <remarks>
/// <para>
/// FDI is the stored form throughout — <c>ToothChartEntry.ToothNumber</c> holds "46" — and
/// the other systems are display conversions. Storing whatever the clinician happened to
/// have selected would mean a chart that cannot be read without also knowing which
/// notation was active when each row was written, and a referral arriving in Universal
/// would be indistinguishable from FDI for the teeth numbered 11 to 32.
/// </para>
/// <para>
/// The conversions are tables rather than arithmetic. The relationships look regular
/// enough to compute — Universal runs 1 to 32 across the arches — but the direction
/// reverses between upper and lower, and getting that wrong names a tooth in the opposite
/// quadrant. A table is checkable against a chart on the wall.
/// </para>
/// </remarks>
public static class ToothNumbering
{
    // Permanent, in screen order. Quadrant 1 (upper right) runs 18 down to 11, then
    // quadrant 2 (upper left) 21 up to 28 — so the midline sits in the middle of the row,
    // which is how an odontogram is drawn and how a clinician reads it.
    private static readonly string[] PermanentUpper =
        ["18", "17", "16", "15", "14", "13", "12", "11", "21", "22", "23", "24", "25", "26", "27", "28"];

    private static readonly string[] PermanentLower =
        ["48", "47", "46", "45", "44", "43", "42", "41", "31", "32", "33", "34", "35", "36", "37", "38"];

    // Deciduous: quadrants 5-8, same layout.
    private static readonly string[] DeciduousUpper =
        ["55", "54", "53", "52", "51", "61", "62", "63", "64", "65"];

    private static readonly string[] DeciduousLower =
        ["85", "84", "83", "82", "81", "71", "72", "73", "74", "75"];

    /// <summary>
    /// The transitional arch: permanent first molars and incisors, deciduous canines and
    /// molars.
    /// </summary>
    /// <remarks>
    /// An approximation of a typical seven-to-nine-year-old, and openly one — real mixed
    /// dentition varies tooth by tooth and premolars erupt across it. The clinician charts
    /// what is actually in the mouth; this decides which buttons are offered, and the
    /// permanent view is always available for anything this arch does not show.
    /// </remarks>
    private static readonly string[] MixedUpper =
        ["16", "55", "54", "53", "12", "11", "21", "22", "63", "64", "65", "26"];

    private static readonly string[] MixedLower =
        ["46", "85", "84", "83", "42", "41", "31", "32", "73", "74", "75", "36"];

    /// <summary>
    /// FDI to Universal for the permanent teeth. Universal numbers 1-16 across the upper
    /// arch from the patient's right, then 17-32 across the lower arch from the patient's
    /// LEFT — the reversal is the part that catches people out.
    /// </summary>
    private static readonly Dictionary<string, string> PermanentUniversal = new()
    {
        ["18"] = "1", ["17"] = "2", ["16"] = "3", ["15"] = "4",
        ["14"] = "5", ["13"] = "6", ["12"] = "7", ["11"] = "8",
        ["21"] = "9", ["22"] = "10", ["23"] = "11", ["24"] = "12",
        ["25"] = "13", ["26"] = "14", ["27"] = "15", ["28"] = "16",

        ["38"] = "17", ["37"] = "18", ["36"] = "19", ["35"] = "20",
        ["34"] = "21", ["33"] = "22", ["32"] = "23", ["31"] = "24",
        ["41"] = "25", ["42"] = "26", ["43"] = "27", ["44"] = "28",
        ["45"] = "29", ["46"] = "30", ["47"] = "31", ["48"] = "32",
    };

    /// <summary>FDI to Universal for the deciduous teeth, which Universal letters A-T.</summary>
    private static readonly Dictionary<string, string> DeciduousUniversal = new()
    {
        ["55"] = "A", ["54"] = "B", ["53"] = "C", ["52"] = "D", ["51"] = "E",
        ["61"] = "F", ["62"] = "G", ["63"] = "H", ["64"] = "I", ["65"] = "J",

        ["75"] = "K", ["74"] = "L", ["73"] = "M", ["72"] = "N", ["71"] = "O",
        ["81"] = "P", ["82"] = "Q", ["83"] = "R", ["84"] = "S", ["85"] = "T",
    };

    /// <summary>The teeth to render for a dentition.</summary>
    public static ToothArches ArchesFor(Dentition dentition) => dentition switch
    {
        Dentition.Deciduous => new ToothArches(DeciduousUpper, DeciduousLower),
        Dentition.Mixed => new ToothArches(MixedUpper, MixedLower),
        _ => new ToothArches(PermanentUpper, PermanentLower),
    };

    /// <summary>
    /// What to call an FDI tooth in the given notation.
    /// </summary>
    /// <remarks>
    /// Falls back to the FDI number for anything not in the tables. A chart is better off
    /// showing a number the clinician can recognise than an em dash, and an unmapped tooth
    /// means the tables need extending rather than that the tooth does not exist.
    /// </remarks>
    public static string Label(string fdi, ToothNotation notation) => notation switch
    {
        ToothNotation.Universal =>
            PermanentUniversal.GetValueOrDefault(fdi)
            ?? DeciduousUniversal.GetValueOrDefault(fdi)
            ?? fdi,

        ToothNotation.Palmer => PalmerLabel(fdi),

        _ => fdi,
    };

    /// <summary>
    /// Palmer notation, written as a quadrant prefix and the tooth's position.
    /// </summary>
    /// <remarks>
    /// True Palmer draws a bracket around the number to show the quadrant — ⌐, ¬, L, ⌐
    /// mirrored — and those glyphs render inconsistently across the platforms this app
    /// targets and are unreadable to a screen reader. "UR6" says the same thing and
    /// survives being read aloud, which on a clinical chart matters more than the
    /// typography.
    /// </remarks>
    private static string PalmerLabel(string fdi)
    {
        if (fdi.Length != 2 || !char.IsDigit(fdi[0]) || !char.IsDigit(fdi[1])) return fdi;

        var quadrant = fdi[0] switch
        {
            '1' or '5' => "UR",
            '2' or '6' => "UL",
            '3' or '7' => "LL",
            '4' or '8' => "LR",
            _ => null,
        };

        if (quadrant is null) return fdi;

        // Deciduous teeth are lettered A-E from the midline in Palmer, where permanent
        // teeth are numbered 1-8.
        var position = fdi[1];
        var isDeciduous = fdi[0] is '5' or '6' or '7' or '8';

        var suffix = isDeciduous
            ? ((char)('A' + (position - '1'))).ToString()
            : position.ToString();

        return quadrant + suffix;
    }

    /// <summary>True where this FDI number belongs to a deciduous tooth.</summary>
    public static bool IsDeciduous(string fdi) =>
        fdi.Length == 2 && fdi[0] is '5' or '6' or '7' or '8';
}
