namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// What became of an outbound message. Failures matter: an unnoticed bounce is a reminder
/// the patient never got, which shows up as an FTA nobody can explain.
/// </summary>
public enum CommunicationStatus
{
    /// <summary>Queued, not yet sent. Also the state of everything sent while offline.</summary>
    Pending = 0,

    Sent = 1,

    /// <summary>The gateway confirmed delivery.</summary>
    Delivered = 2,

    /// <summary>The patient replied or clicked through.</summary>
    Responded = 3,

    /// <summary>Rejected by the gateway or bounced.</summary>
    Failed = 4,

    /// <summary>Not sent because the patient has opted out of this channel.</summary>
    Suppressed = 5,
}
