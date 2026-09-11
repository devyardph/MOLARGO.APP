namespace DYS.Molargo.Domain.Enums;

/// <summary>Which scheme a claim is made against.</summary>
public enum ClaimType
{
    /// <summary>Private health fund, claimed on the spot through HICAPS.</summary>
    PrivateHealthFund = 0,

    /// <summary>Child Dental Benefits Schedule.</summary>
    MedicareCdbs = 1,

    /// <summary>Department of Veterans' Affairs.</summary>
    Dva = 2,

    /// <summary>A state public dental scheme voucher.</summary>
    PublicScheme = 3,

    /// <summary>Workers' compensation or a third-party insurer.</summary>
    ThirdPartyInsurer = 4,
}
