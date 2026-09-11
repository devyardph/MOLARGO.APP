using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Shared.Components;

/// <summary>
/// How the comms screens name and colour messages, channels and consent.
/// </summary>
/// <remarks>
/// Whole literal class strings, never composed at runtime: Tailwind scans source text, so
/// a class built by concatenation is a class that never reaches the stylesheet.
/// </remarks>
public static class CommsCss
{
    public static string StatusLabel(CommunicationStatus status) => status switch
    {
        CommunicationStatus.Pending => "Queued",
        CommunicationStatus.Sent => "Sent",
        CommunicationStatus.Delivered => "Delivered",
        CommunicationStatus.Responded => "Replied",
        CommunicationStatus.Failed => "Failed",
        CommunicationStatus.Suppressed => "Suppressed — opted out",
        _ => status.ToString(),
    };

    /// <summary>
    /// A message status chip.
    /// </summary>
    /// <remarks>
    /// Failed takes the accent, because an unnoticed bounce is a reminder the patient never
    /// got and surfaces later as an FTA nobody can explain. Suppressed is quieter: it is
    /// the system correctly honouring an opt-out, not a fault.
    /// </remarks>
    public static string StatusTag(CommunicationStatus status) => status switch
    {
        CommunicationStatus.Failed => "tag tag-accent",
        CommunicationStatus.Pending => "tag tag-accent2",
        CommunicationStatus.Suppressed => "tag tag-outline",
        _ => "tag tag-neutral",
    };

    /// <summary>
    /// A consent cell.
    /// </summary>
    /// <remarks>
    /// Only the refusal is coloured, matching the design — an opted-out patient is the row
    /// a marketing send has to skip, and colouring the opted-in majority as well would
    /// bury it.
    /// </remarks>
    public static string ConsentCell(bool granted) => granted
        ? "body-cell text-xs"
        : "body-cell text-xs font-semibold text-accent-700";

    public static string ConsentLabel(bool granted) => granted ? "Opted in" : "Opted out";

    /// <summary>One row of a list, highlighted when selected.</summary>
    public static string PickRow(bool isSelected) => isSelected
        ? "flex cursor-pointer items-center gap-2 border-[1.5px] border-accent bg-accent-100 px-3 py-2.5 text-left"
        : "flex cursor-pointer items-center gap-2 border-[1.5px] border-divider bg-transparent px-3 py-2.5 text-left hover:border-accent";

    /// <summary>
    /// A selectable chip — a segment, a channel, a consent source.
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="ChipCss"/>, which every screen with chips shares. Kept here
    /// so the comms views read against their own mapper.
    /// </remarks>
    public static string Chip(bool isOn) => ChipCss.Chip(isOn);

    /// <summary>
    /// One message in a portal thread — the patient's on the left, the practice's right.
    /// </summary>
    public static string Bubble(bool fromPatient) => fromPatient
        ? "max-w-[80%] self-start bg-surface px-3 py-2.5 text-[13px]"
        : "max-w-[80%] self-end bg-neutral-900 px-3 py-2.5 text-[13px] text-neutral-100";

    public static string BubbleMeta(bool fromPatient) => fromPatient
        ? "mt-1 text-[10px] text-ink/55"
        : "mt-1 text-[10px] text-neutral-100/60";
}
