using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Shared.Components;

/// <summary>
/// How the diary grid draws an appointment block, a move target and a month cell.
/// </summary>
/// <remarks>
/// <para>
/// The block's <em>fill</em> comes from the status, not the appointment type, and the type's
/// colour is a stripe down the left edge instead. The prototype fills the whole block with
/// the type colour, which looks better in a mockup and fails in use for two reasons: the
/// colours are practice-configurable arbitrary hex, so no text colour is legible against
/// all of them, and a diary's most-read fact is how far through the day each patient is —
/// which the fill can only carry if the fill is the status.
/// </para>
/// <para>
/// Class strings are whole literals, never assembled from fragments: Tailwind finds classes
/// by scanning source text, so a name built by concatenation is simply absent from the
/// stylesheet.
/// </para>
/// </remarks>
public static class DiaryCss
{
    /// <summary>An appointment block in the day grid.</summary>
    public static string Block(AppointmentStatus status, bool isSelected)
    {
        const string shape =
            "absolute overflow-hidden border-0 border-l-4 px-1.5 py-1 text-left " +
            "font-[inherit] cursor-pointer leading-tight";

        // The selected block is ringed rather than recoloured, so selecting something does
        // not destroy the status reading that made it worth selecting.
        var ring = isSelected ? " outline-2 outline-offset-[-2px] outline-ink z-20" : " z-10";

        var fill = status switch
        {
            AppointmentStatus.Confirmed => "bg-neutral-300 text-ink",
            AppointmentStatus.CheckedIn => "bg-accent-200 text-accent-800",
            AppointmentStatus.Seated => "bg-accent-300 text-accent-900",
            AppointmentStatus.InProgress => "bg-accent text-white",
            AppointmentStatus.Completed => "bg-neutral-800 text-neutral-100",
            _ => "bg-neutral-200 text-ink",
        };

        return $"{shape} {fill}{ring}";
    }

    /// <summary>
    /// A slot the selected appointment can be moved into. Only rendered while a move is
    /// armed, so it can afford to be conspicuous.
    /// </summary>
    public static string MoveTarget() =>
        "absolute z-30 cursor-pointer border border-dashed border-accent/45 bg-accent/5 " +
        "p-0 hover:bg-accent/25";

    /// <summary>
    /// Empty chair time, offered as somewhere to book. Invisible until hovered — four
    /// hundred outlined cells would read as a grid of buttons rather than as a day's diary.
    /// </summary>
    public static string BookableSlot() =>
        "absolute z-0 cursor-pointer border-0 bg-transparent p-0 hover:bg-accent/10";

    /// <summary>One cell of the month grid.</summary>
    public static string MonthCell(bool inMonth, bool isOpen, bool isToday)
    {
        const string shape =
            "flex min-h-[74px] cursor-pointer flex-col gap-0.5 border-r border-b " +
            "border-divider p-2 text-left font-[inherit]";

        if (!isOpen) return $"{shape} cursor-default bg-surface text-ink/30";

        // Outside the month, but still rendered: blanking the padding days would break the
        // week alignment that makes a month grid readable at all.
        if (!inMonth) return $"{shape} bg-bg text-ink/35";

        return isToday
            ? $"{shape} bg-accent-100 text-ink outline-2 outline-offset-[-2px] outline-accent"
            : $"{shape} bg-bg text-ink hover:bg-surface";
    }
}
