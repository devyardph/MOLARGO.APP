namespace DYS.Molargo.Domain.Enums;

/// <summary>What a stored document is, so the record screen can group and filter it.</summary>
public enum DocumentKind
{
    Other = 0,

    /// <summary>Intraoral or extraoral photograph.</summary>
    Photograph = 1,

    /// <summary>Radiograph — OPG, bitewing, periapical, CBCT slice.</summary>
    Radiograph = 2,

    /// <summary>A signed consent form.</summary>
    Consent = 3,

    /// <summary>A referral letter, in either direction.</summary>
    Referral = 4,

    /// <summary>A specialist's or pathology report.</summary>
    Report = 5,

    /// <summary>A lab prescription or work docket.</summary>
    LabDocket = 6,

    /// <summary>An invoice, receipt or claim statement.</summary>
    Financial = 7,

    /// <summary>Scanned correspondence.</summary>
    Correspondence = 8,
}
