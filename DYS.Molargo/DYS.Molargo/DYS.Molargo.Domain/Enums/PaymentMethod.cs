namespace DYS.Molargo.Domain.Enums;

/// <summary>How a payment reached the practice.</summary>
public enum PaymentMethod
{
    Cash = 0,
    EftposCard = 1,
    CreditCard = 2,
    BankTransfer = 3,

    /// <summary>Paid on the spot by a health fund through HICAPS.</summary>
    HicapsFundBenefit = 4,

    /// <summary>Medicare benefit, including the child dental schedule.</summary>
    MedicareBenefit = 5,

    /// <summary>Department of Veterans' Affairs.</summary>
    DvaBenefit = 6,

    /// <summary>Third-party payment plan — Zip, Afterpay, a dental finance provider.</summary>
    PaymentPlan = 7,

    /// <summary>Applied from a credit already held on the account.</summary>
    AccountCredit = 8,

    /// <summary>Refunded to the patient. Stored as a negative amount.</summary>
    Refund = 9,
}
