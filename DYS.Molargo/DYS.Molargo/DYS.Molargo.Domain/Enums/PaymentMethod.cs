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

/// <summary>How each payment method is named to a person.</summary>
/// <remarks>
/// Here rather than in the UI's CSS mappers, where these labels used to live. A label is
/// what the practice calls the thing — it is read out at the front desk and written into
/// audit entries — so it is domain vocabulary, and keeping it beside the enum means a new
/// method cannot be added with no name but its C# identifier.
///
/// The giveaway was <c>BillingService</c> reaching into a CSS mapper for a word to put in
/// an audit message. A service wanting a style class is wrong; a service wanting a label is
/// not, and the label was simply in the wrong place.
/// </remarks>
public static class PaymentMethods
{
    public static string Label(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Cash",
        PaymentMethod.EftposCard => "EFTPOS",
        PaymentMethod.CreditCard => "Credit card",
        PaymentMethod.BankTransfer => "Bank transfer",
        PaymentMethod.HicapsFundBenefit => "Fund benefit (HICAPS)",
        PaymentMethod.MedicareBenefit => "Medicare benefit",
        PaymentMethod.DvaBenefit => "DVA benefit",
        PaymentMethod.PaymentPlan => "Payment plan",
        PaymentMethod.AccountCredit => "Account credit",
        PaymentMethod.Refund => "Refund",
        _ => method.ToString(),
    };
}
