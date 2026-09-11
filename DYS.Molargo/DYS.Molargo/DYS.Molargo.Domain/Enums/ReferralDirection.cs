namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// Which way a referral points. The same entity serves both, because the practice needs
/// one list of everything in flight — a referral sent out and one received are both
/// waiting on someone else.
/// </summary>
public enum ReferralDirection
{
    /// <summary>Sent from this practice to a specialist.</summary>
    Outbound = 0,

    /// <summary>Received by this practice from another provider.</summary>
    Inbound = 1,
}
