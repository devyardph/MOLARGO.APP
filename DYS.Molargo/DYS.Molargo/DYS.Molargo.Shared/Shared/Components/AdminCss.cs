using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Features.Admin.Services;

namespace DYS.Molargo.Shared.Components;

/// <summary>
/// How the admin screens colour staff, registrations and audited actions.
/// </summary>
/// <remarks>
/// Whole literal class strings, never composed at runtime: Tailwind scans source text, so
/// a class built by concatenation is a class that never reaches the stylesheet.
/// </remarks>
public static class AdminCss
{
    /// <summary>
    /// The admin screens' check control: a filled square when on, an outline when off.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Here rather than private to one screen because two now draw it — the notification
    /// and printer switches on Settings, and the access boxes on Users. It was a private
    /// helper in SettingsComponent, and a second copy of it is a second place for the
    /// design to drift.
    /// </para>
    /// <para>
    /// Not the shared <c>.radio</c> pattern, which draws a round indicator. These are
    /// independent on/off switches rather than a choice between alternatives, and the
    /// square says so — the same distinction the rest of the design makes.
    /// </para>
    /// <para>
    /// Whole literal strings, every branch. Tailwind scans source text, so a class
    /// assembled from a shared base plus a conditional suffix never reaches the stylesheet.
    /// </para>
    /// </remarks>
    /// <param name="enabled">
    /// False where the switch cannot be moved — the last owner's own ownership box. Dimmed
    /// and cursor-default rather than merely inert, so it does not invite the click it will
    /// refuse.
    /// </param>
    public static string Check(bool on, bool enabled = true)
    {
        if (!enabled)
        {
            return on
                ? "mt-0.5 size-4 shrink-0 cursor-default border-[1.5px] border-accent/45 bg-accent/45 p-0"
                : "mt-0.5 size-4 shrink-0 cursor-default border-[1.5px] border-divider/50 bg-transparent p-0";
        }

        return on
            ? "mt-0.5 size-4 shrink-0 cursor-pointer border-[1.5px] border-accent bg-accent p-0"
            : "mt-0.5 size-4 shrink-0 cursor-pointer border-[1.5px] border-divider bg-transparent p-0";
    }

    /// <summary>The label beside a <see cref="Check"/>, which toggles it too.</summary>
    /// <remarks>
    /// A button, not a label element: the control it belongs to is a button rather than a
    /// real checkbox, so there is no input for a label's <c>for</c> to point at. Clicking
    /// the words has to work — a 16px square is a small target on a tablet.
    /// </remarks>
    public static string CheckLabel(bool enabled = true) => enabled
        ? "cursor-pointer border-0 bg-transparent p-0 text-left font-[inherit] font-bold text-ink hover:text-accent"
        : "cursor-default border-0 bg-transparent p-0 text-left font-[inherit] font-bold text-ink/55";

    /// <summary>
    /// A staff status chip.
    /// </summary>
    /// <remarks>
    /// Deactivated is outlined rather than accented. It is a deliberate state the practice
    /// chose, not a fault — and the accent on this screen is reserved for a registration
    /// that blocks someone from signing.
    /// </remarks>
    public static string StaffTag(bool isActive) => isActive
        ? "tag tag-neutral"
        : "tag tag-outline";

    /// <summary>
    /// A registration status chip.
    /// </summary>
    /// <remarks>
    /// Missing and expired both take the accent, because both mean the clinician cannot
    /// lawfully sign and anything they do sign is worthless. "Not checked" is a softer
    /// warning: the registration may well be current, but nobody has confirmed it.
    /// </remarks>
    public static string RegistrationTag(RegistrationRow row)
    {
        if (row.IsMissing || row.IsExpired) return "tag tag-accent";
        if (row.IsExpiringSoon) return "tag tag-accent2";
        if (row.ExpiryUnknown) return "tag tag-outline";

        return "tag tag-neutral";
    }

    public static string RegistrationLabel(RegistrationRow row)
    {
        if (row.IsMissing) return "No licence number";
        if (row.IsExpired) return "Expired";
        if (row.IsExpiringSoon) return "Expiring";
        if (row.ExpiryUnknown) return "Not checked";

        return "Current";
    }

    /// <summary>The expiry cell, marked once it blocks or is about to.</summary>
    public static string ExpiryCell(RegistrationRow row) =>
        row.IsExpired || row.IsMissing
            ? "body-cell text-xs font-bold text-accent-800"
            : row.IsExpiringSoon
                ? "body-cell text-xs font-semibold text-accent-700"
                : "body-cell text-xs";

    /// <summary>
    /// An audited action chip.
    /// </summary>
    /// <remarks>
    /// A removal takes the accent — it is the entry an investigation looks for. Viewing is
    /// the quietest, and will be the most numerous once access logging exists.
    /// </remarks>
    public static string ActionTag(AuditAction action) => action switch
    {
        AuditAction.Deleted => "tag tag-accent",
        AuditAction.Exported => "tag tag-accent2",

        // Red, like every other failure in the app. An email that did not go is not an
        // event somebody should have to read the detail column to notice.
        AuditAction.NotificationFailed => "tag tag-accent2",
        AuditAction.Viewed => "tag tag-outline",
        AuditAction.SignInFailed => "tag tag-accent",
        _ => "tag tag-neutral",
    };
}
