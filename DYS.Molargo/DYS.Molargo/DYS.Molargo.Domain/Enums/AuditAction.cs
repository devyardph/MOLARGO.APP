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
}
