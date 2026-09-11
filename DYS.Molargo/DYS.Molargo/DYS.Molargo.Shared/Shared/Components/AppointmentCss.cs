using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Shared.Components;

/// <summary>
/// Maps <see cref="AppointmentStatus"/> onto the design system's classes and labels.
/// </summary>
/// <remarks>
/// The status ramp is the prototype's: neutral for not-yet-arrived, warming through the
/// accent tints as the patient moves towards the chair, full accent in the chair, then
/// dark once done. It reads as progress across a glance down the day, which a set of
/// unrelated colours would not.
/// </remarks>
public static class AppointmentCss
{
    /// <summary>The tappable status chip on the arrivals list.</summary>
    /// <remarks>
    /// Whole literal class strings, never assembled from fragments: Tailwind discovers
    /// classes by scanning source text, so a name built by concatenation is invisible to
    /// the compiler and the utility is simply absent from the stylesheet.
    /// </remarks>
    public static string StatusChip(AppointmentStatus status)
    {
        const string shape =
            "cursor-pointer border-0 px-3 py-[5px] text-left text-[11px] font-semibold " +
            "tracking-[0.04em] min-w-[88px] font-[inherit]";

        return status switch
        {
            AppointmentStatus.CheckedIn => $"{shape} bg-accent-200 text-accent-800",
            AppointmentStatus.Seated => $"{shape} bg-accent-300 text-accent-900",
            AppointmentStatus.InProgress => $"{shape} bg-accent text-white",
            AppointmentStatus.Completed => $"{shape} bg-neutral-800 text-neutral-100",

            // Cancelled and no-show are outlined rather than filled: they are not points
            // on the arrival ramp and should not look like one.
            AppointmentStatus.Cancelled or AppointmentStatus.FailedToAttend =>
                $"{shape} border border-accent bg-transparent text-accent",

            _ => $"{shape} bg-neutral-200 text-neutral-800",
        };
    }

    /// <summary>
    /// The same status as a plain, non-interactive chip — for a table that reports an
    /// appointment rather than offering to advance it.
    /// </summary>
    public static string StatusTag(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Confirmed or AppointmentStatus.CheckedIn
            or AppointmentStatus.Seated or AppointmentStatus.InProgress => "tag tag-accent",
        AppointmentStatus.Cancelled or AppointmentStatus.FailedToAttend => "tag tag-outline",
        _ => "tag tag-neutral",
    };

    /// <summary>
    /// The front desk's words for a status, which are not the enum's. Staff say "booked"
    /// and "in chair"; the enum says <c>Scheduled</c> and <c>InProgress</c>.
    /// </summary>
    public static string StatusLabel(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Scheduled => "Booked",
        AppointmentStatus.Confirmed => "Confirmed",
        AppointmentStatus.CheckedIn => "Arrived",
        AppointmentStatus.Seated => "Seated",
        AppointmentStatus.InProgress => "In chair",
        AppointmentStatus.Completed => "Completed",
        AppointmentStatus.Cancelled => "Cancelled",
        AppointmentStatus.FailedToAttend => "Did not attend",
        _ => string.Empty,
    };
}
