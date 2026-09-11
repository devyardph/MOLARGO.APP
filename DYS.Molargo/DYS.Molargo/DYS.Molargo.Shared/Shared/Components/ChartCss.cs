using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Shared.Components;

/// <summary>
/// Maps charting enums onto the odontogram's appearance and the practice's words.
/// </summary>
/// <remarks>
/// The colour scheme is the design's legend, and it encodes time rather than kind: dark
/// for what is already in the mouth, an accent outline for what is planned, solid accent
/// for what was done this course, faded for what is gone. A clinician glancing at an arch
/// is asking "what still needs doing", and that is the question the legend answers.
/// </remarks>
public static class ChartCss
{
    /// <summary>
    /// One pocket-depth cell in the perio table.
    /// </summary>
    /// <remarks>
    /// Coloured by severity, because the number alone does not read at a glance across a
    /// sextant: healthy to 3mm, watch at 4mm, and accent from 5mm — the depth at which the
    /// patient can no longer clean the site themselves. Suppuration rings the cell rather
    /// than recolouring it, so pus does not disguise the depth.
    /// </remarks>
    public static string PocketDepth(int? depthMm, bool suppuration)
    {
        const string shape =
            "size-8 cursor-pointer border-0 p-0 text-center font-[inherit] text-[13px] " +
            "font-bold tabular-nums";

        var ring = suppuration
            ? " outline-2 outline-offset-[-2px] outline-accent-700"
            : string.Empty;

        var fill = depthMm switch
        {
            null => "bg-surface text-ink/35",
            <= 3 => "bg-neutral-200 text-ink",
            4 => "bg-accent-200 text-accent-800",
            <= 6 => "bg-accent text-white",

            // Beyond 6mm goes darker still rather than more accent: it is a different
            // conversation from a 5mm pocket, and the ramp has run out of accent.
            _ => "bg-accent-800 text-bg",
        };

        return $"{shape} {fill}{ring}";
    }

    /// <summary>A bleeding-on-probing dot. Filled when bleeding.</summary>
    public static string BleedingDot(bool bleeding)
    {
        const string shape = "size-4 cursor-pointer rounded-full p-0";

        return bleeding
            ? $"{shape} border-0 bg-accent"
            : $"{shape} border border-divider bg-transparent hover:border-accent";
    }

    /// <summary>A recession, mobility or furcation cell — tappable, quiet when zero.</summary>
    public static string PerioValue(bool notable)
    {
        const string shape =
            "min-w-8 cursor-pointer border-0 px-1.5 py-1 text-center font-[inherit] " +
            "text-[13px] tabular-nums";

        return notable
            ? $"{shape} bg-accent-200 font-bold text-accent-800"
            : $"{shape} bg-surface text-ink/55";
    }

    /// <summary>The tooth button. Sized for a finger on a tablet.</summary>
    public static string Tooth(ToothCondition condition, bool isSelected)
    {
        const string shape =
            "flex flex-1 min-w-[34px] cursor-pointer flex-col items-center justify-center gap-0.5 "
            + "border py-1.5 font-heading text-[13px] font-extrabold leading-none";

        var state = condition switch
        {
            // Already in the mouth: dark, because it is context rather than work.
            ToothCondition.Restoration or ToothCondition.RootCanalTreated or ToothCondition.Crown
                or ToothCondition.Bridge or ToothCondition.Implant or ToothCondition.Veneer =>
                "border-neutral-800 bg-neutral-800 text-neutral-100",

            // Needs doing: outlined accent, so it reads as an outstanding item.
            ToothCondition.Caries or ToothCondition.Fractured or ToothCondition.Periodontal
                or ToothCondition.Watch or ToothCondition.Wear =>
                "border-2 border-accent bg-accent-100 text-accent-800",

            // Gone. Faded rather than hidden: the gap is clinically meaningful, and a
            // missing button would silently reflow the arch and misalign the quadrants.
            ToothCondition.Missing => "border-divider bg-bg text-ink opacity-30",

            ToothCondition.Unerupted or ToothCondition.Impacted =>
                "border-dashed border-divider bg-bg text-ink/55",

            _ => "border-divider bg-bg text-ink",
        };

        // The selection ring is drawn outside the button so it does not fight whatever
        // fill the condition put on it.
        var selection = isSelected ? " outline outline-2 outline-offset-2 outline-accent" : string.Empty;

        return shape + " " + state + selection;
    }

    /// <summary>The little code under the tooth number.</summary>
    public static string? Mark(ToothCondition condition, ToothSurface surfaces) => condition switch
    {
        ToothCondition.Sound => null,
        ToothCondition.Missing => "MIS",
        ToothCondition.Unerupted => "UNE",
        ToothCondition.Impacted => "IMP",
        ToothCondition.RootCanalTreated => "RCT",
        ToothCondition.Crown => "CR",
        ToothCondition.Bridge => "BR",
        ToothCondition.Implant => "IMPL",
        ToothCondition.Veneer => "VEN",
        ToothCondition.Periodontal => "PER",
        ToothCondition.Watch => "WCH",
        ToothCondition.Wear => "WR",
        ToothCondition.Fractured => "FX",

        // Decay and restorations are the ones where the surfaces matter more than the
        // condition — "MOD" tells a clinician more than "caries" does.
        _ => surfaces == ToothSurface.None ? "RES" : SurfaceCode(surfaces),
    };

    /// <summary>
    /// The surface combination in the conventional order — "MOD", not "DOM".
    /// </summary>
    /// <remarks>
    /// Mesial, occlusal/incisal, distal, buccal, lingual/palatal. Charting notation has a
    /// fixed order and a clinician reads it as one token; emitting the flags in enum order
    /// produced "OMD", which reads as a different lesion.
    /// </remarks>
    public static string SurfaceCode(ToothSurface surfaces) => ToothSurfaces.Code(surfaces);

    /// <summary>What a clinician calls the condition.</summary>
    public static string ConditionLabel(ToothCondition condition) => condition switch
    {
        ToothCondition.Sound => "Sound",
        ToothCondition.Caries => "Caries",
        ToothCondition.Restoration => "Restoration",
        ToothCondition.Crown => "Crown",
        ToothCondition.Bridge => "Bridge",
        ToothCondition.Veneer => "Veneer",
        ToothCondition.Implant => "Implant",
        ToothCondition.RootCanalTreated => "Root canal treated",
        ToothCondition.Missing => "Missing",
        ToothCondition.Unerupted => "Unerupted",
        ToothCondition.Impacted => "Impacted",
        ToothCondition.Fractured => "Fractured",
        ToothCondition.Wear => "Wear",
        ToothCondition.Periodontal => "Periodontal",
        ToothCondition.Watch => "Watch",
        _ => string.Empty,
    };

    /// <summary>A surface button in the detail panel. 46px, as the design has it.</summary>
    public static string Surface(bool selected) =>
        selected
            ? "size-[46px] cursor-pointer border-[1.5px] border-accent bg-accent font-[inherit] text-[15px] font-extrabold text-white"
            : "size-[46px] cursor-pointer border-[1.5px] border-divider bg-bg font-[inherit] text-[15px] font-extrabold text-ink hover:border-accent";

    /// <summary>A condition button in the palette. 44px minimum for a tablet.</summary>
    public static string Palette(bool selected) =>
        selected
            ? "min-h-11 cursor-pointer whitespace-nowrap border-[1.5px] border-accent bg-accent-100 px-3 py-2.5 font-[inherit] text-xs font-semibold text-accent-800"
            : "min-h-11 cursor-pointer whitespace-nowrap border-[1.5px] border-divider bg-bg px-3 py-2.5 font-[inherit] text-xs font-semibold text-ink hover:bg-ink/7";
}
