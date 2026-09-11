namespace DYS.Molargo.Domain.Enums;

/// <summary>Where a claim sits with the payer.</summary>
public enum ClaimStatus
{
    /// <summary>Assembled, not yet sent.</summary>
    Draft = 0,

    Submitted = 1,

    /// <summary>Sent and awaiting assessment. The usual state for a batched claim.</summary>
    Pending = 2,

    /// <summary>Assessed and paid in full.</summary>
    Approved = 3,

    /// <summary>Assessed and paid at less than claimed. The gap falls to the patient.</summary>
    PartiallyApproved = 4,

    /// <summary>Assessed and refused. See the payer's reason on the claim.</summary>
    Rejected = 5,

    /// <summary>Cancelled before assessment.</summary>
    Cancelled = 6,
}
