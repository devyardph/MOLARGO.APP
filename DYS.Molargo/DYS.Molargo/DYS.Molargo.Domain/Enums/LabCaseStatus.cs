namespace DYS.Molargo.Domain.Enums;

/// <summary>Where a case sits with the dental laboratory.</summary>
public enum LabCaseStatus
{
    /// <summary>Impression or scan taken, docket not yet sent.</summary>
    Prepared = 0,

    SentToLab = 1,

    /// <summary>The lab has confirmed it is being made.</summary>
    InProduction = 2,

    /// <summary>Back at the practice and checked in.</summary>
    Received = 3,

    /// <summary>Fitted to the patient. Closes the case.</summary>
    Fitted = 4,

    /// <summary>Returned to the lab for adjustment or remake.</summary>
    Remake = 5,

    Cancelled = 6,
}
