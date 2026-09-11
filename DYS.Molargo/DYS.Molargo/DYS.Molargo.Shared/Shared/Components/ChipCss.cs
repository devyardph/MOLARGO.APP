namespace DYS.Molargo.Shared.Components;

/// <summary>
/// A selectable chip: a small on/off button used for categories, segments, metrics,
/// consent sources and suppliers.
/// </summary>
/// <remarks>
/// <para>
/// Cross-cutting rather than per-feature. Four screens had grown their own copy of the
/// same two class strings, and the reports builder was about to reach into
/// <see cref="CommsCss"/> for it — a comms mapper styling a reports screen. One definition
/// means retuning the affordance is one edit rather than a grep.
/// </para>
/// <para>
/// Whole literal strings, both branches: Tailwind scans source text, so a class assembled
/// from a base plus a conditional suffix never reaches the stylesheet.
/// </para>
/// </remarks>
public static class ChipCss
{
    // White on the filled chip, matching btn-primary — see the note there on the
    // contrast trade this makes.
    public static string Chip(bool isOn) => isOn
        ? "cursor-pointer border-2 border-accent bg-accent px-2.5 py-1.5 font-[inherit] text-xs font-bold text-white"
        : "cursor-pointer border-2 border-divider bg-transparent px-2.5 py-1.5 font-[inherit] text-xs text-ink hover:border-accent";
}
