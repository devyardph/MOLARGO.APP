namespace DYS.Molargo.Domain.Enums;

/// <summary>Where a referral sits.</summary>
public enum ReferralStatus
{
    Draft = 0,
    Sent = 1,

    /// <summary>Acknowledged by the recipient.</summary>
    Acknowledged = 2,

    /// <summary>The patient has been seen and a report is expected.</summary>
    Attended = 3,

    /// <summary>Report received and filed. Closes the referral.</summary>
    ReportReceived = 4,

    Declined = 5,
    Cancelled = 6,
}
