namespace DYS.Molargo.Shared.Components;

/// <summary>
/// The outcome a <c>NoticeComponent</c> is reporting.
/// </summary>
/// <remarks>
/// Two members, not four. A "warning" and an "info" tone were considered and left out:
/// an operation either did the thing or it did not, and the screens that genuinely need
/// to explain something — no print path, no payment gateway, no MFA — are not reporting
/// an operation at all and read better as prose in the panel than as a banner competing
/// with the one that says whether the save worked.
/// </remarks>
public enum NoticeKind
{
    /// <summary>The operation completed. Accent-toned.</summary>
    Success = 0,

    /// <summary>The operation did not happen. Light red, and announced as an alert.</summary>
    Failure = 1,
}
