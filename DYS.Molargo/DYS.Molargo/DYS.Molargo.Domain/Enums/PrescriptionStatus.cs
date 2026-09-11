namespace DYS.Molargo.Domain.Enums;

/// <summary>Where a prescription sits.</summary>
public enum PrescriptionStatus
{
    Draft = 0,

    /// <summary>Signed by the prescriber and given to the patient.</summary>
    Issued = 1,

    /// <summary>Sent to a nominated pharmacy electronically.</summary>
    SentToPharmacy = 2,

    /// <summary>Confirmed dispensed.</summary>
    Dispensed = 3,

    /// <summary>Withdrawn before dispensing.</summary>
    Cancelled = 4,

    /// <summary>Past its validity period without being dispensed.</summary>
    Expired = 5,
}
