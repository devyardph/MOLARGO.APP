namespace DYS.Molargo.Domain.Enums;

/// <summary>Where one period's subscription charge got to.</summary>
/// <remarks>
/// Four states, and deliberately no "Pending" or "Processing": nothing in this app talks
/// to a payment gateway, so there is no in-flight attempt for a status to describe. Every
/// value here is something a person recorded.
/// </remarks>
public enum ChargeStatus
{
    /// <summary>Raised and owed. Nobody has tried to collect it yet.</summary>
    Due = 0,

    /// <summary>Money received, recorded by the vendor.</summary>
    Paid = 1,

    /// <summary>
    /// Collection was attempted and did not succeed.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Due"/> on purpose. "Nobody has billed them" and "their card
    /// was declined" call for completely different follow-up, and a single "unpaid" status
    /// would hide which one a clinic is in.
    /// </remarks>
    Failed = 2,

    /// <summary>Cancelled without payment — a credit, a goodwill write-off, a duplicate.</summary>
    Waived = 3,
}
