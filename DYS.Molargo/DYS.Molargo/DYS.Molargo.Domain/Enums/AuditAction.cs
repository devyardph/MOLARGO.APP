namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// What a user did to a record. Health records carry an access-logging obligation, so
/// <see cref="Viewed"/> is in here alongside the writes — "who looked at this patient" is
/// the question a privacy complaint actually asks.
/// </summary>
public enum AuditAction
{
    Created = 0,
    Updated = 1,

    /// <summary>Soft-deleted. Nothing in this domain is hard-deleted.</summary>
    Deleted = 2,

    /// <summary>Opened a record. Logged for privacy, not for debugging.</summary>
    Viewed = 3,

    /// <summary>Exported or printed, which takes data outside the system.</summary>
    Exported = 4,

    SignedIn = 5,
    SignedOut = 6,

    /// <summary>A failed sign-in. Repeated entries are the signal worth alerting on.</summary>
    SignInFailed = 7,

    /// <summary>
    /// An email the app meant to send and could not.
    /// </summary>
    /// <remarks>
    /// Its own action rather than an <see cref="Updated"/> entry with a sad detail line.
    /// The question this answers — "why did nothing arrive?" — is asked days later by
    /// somebody scanning the log, and an entry filed as "Changed" is invisible to them.
    ///
    /// Covers the silent stops as well as the rejections: no address on the staff record,
    /// no mail account configured, notifications switched off. From the recipient's side
    /// those are indistinguishable from a mail server refusing, and all four end with a
    /// person waiting for something that is never coming.
    /// </remarks>
    NotificationFailed = 8,
}
