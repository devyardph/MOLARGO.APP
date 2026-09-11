namespace DYS.Molargo.Domain.Enums;

/// <summary>Where a consent form sits. Absence of consent is a treatment blocker.</summary>
public enum ConsentStatus
{
    /// <summary>Prepared and awaiting the patient.</summary>
    Pending = 0,

    /// <summary>Signed. Records who signed and when.</summary>
    Signed = 1,

    /// <summary>The patient refused. Treatment must not proceed.</summary>
    Refused = 2,

    /// <summary>Signed, then withdrawn before treatment.</summary>
    Withdrawn = 3,

    /// <summary>Past the validity period for the treatment it covered.</summary>
    Expired = 4,
}
