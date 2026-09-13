namespace DYS.Molargo.Shared.Components;

/// <summary>
/// The outcome a <c>NoticeComponent</c> is reporting.
/// </summary>
/// <remarks>
/// <para>
/// Three members, not four. An "info" tone is still left out for the reason both of the
/// others were argued down originally: the screens that genuinely need to explain
/// something — no print path, no payment gateway, no MFA — are not reporting an operation
/// at all, and read better as prose in the panel than as a banner competing with the one
/// that says whether the save worked.
/// </para>
/// <para>
/// <see cref="Warning"/> was in that rejected set and has since earned its place. The
/// argument against it was that an operation either did the thing or it did not — true,
/// and it is the reason there is no amber "partly saved". What it missed is the message
/// that is not about an operation's outcome but about its input: the booking form's
/// closed-day and outside-hours messages say the slot cannot be used, which the person
/// reading can fix in front of them. Painting that the same red as a refused save told
/// them something had gone wrong when nothing had yet.
/// </para>
/// </remarks>
public enum NoticeKind
{
    /// <summary>The operation completed. Accent-toned.</summary>
    Success = 0,

    /// <summary>The operation did not happen. Light red, and announced as an alert.</summary>
    Failure = 1,

    /// <summary>
    /// The input cannot be used as it stands. Amber, and announced as an alert.
    /// </summary>
    /// <remarks>
    /// Announced like a failure rather than politely like a success: it appears because a
    /// save was attempted and did not happen, and somebody who misses it walks away
    /// believing the appointment is booked.
    /// </remarks>
    Warning = 2,
}
