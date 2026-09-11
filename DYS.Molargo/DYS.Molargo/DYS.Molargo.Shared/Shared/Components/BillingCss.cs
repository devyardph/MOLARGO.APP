using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Shared.Components;

/// <summary>
/// How the billing screens name and colour invoices, payments and claims.
/// </summary>
public static class BillingCss
{
    /// <summary>The front desk's words for a payment method, not the enum's.</summary>
    public static string MethodLabel(PaymentMethod method) => method switch
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

    public static string InvoiceStatusLabel(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Draft => "Draft",
        InvoiceStatus.Issued => "Unpaid",
        InvoiceStatus.PartiallyPaid => "Part paid",
        InvoiceStatus.Paid => "Paid",
        InvoiceStatus.Overdue => "Overdue",
        InvoiceStatus.Voided => "Voided",
        InvoiceStatus.WrittenOff => "Written off",
        _ => status.ToString(),
    };

    /// <summary>
    /// An invoice status chip.
    /// </summary>
    /// <remarks>
    /// Only overdue takes the accent. Paid is the normal, quiet outcome, and a screen where
    /// every row shouts is a screen where the one overdue row does not.
    /// </remarks>
    public static string InvoiceStatusTag(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Overdue => "tag tag-accent",
        InvoiceStatus.Voided or InvoiceStatus.WrittenOff => "tag tag-outline",
        _ => "tag tag-neutral",
    };

    public static string ClaimStatusLabel(ClaimStatus status) => status switch
    {
        ClaimStatus.Draft => "Draft",
        ClaimStatus.Submitted => "Submitted",
        ClaimStatus.Pending => "Awaiting assessment",
        ClaimStatus.Approved => "Approved",
        ClaimStatus.PartiallyApproved => "Part approved",
        ClaimStatus.Rejected => "Rejected",
        ClaimStatus.Cancelled => "Cancelled",
        _ => status.ToString(),
    };

    public static string ClaimStatusTag(ClaimStatus status) => status switch
    {
        ClaimStatus.Rejected => "tag tag-accent",
        ClaimStatus.PartiallyApproved => "tag tag-accent2",
        ClaimStatus.Cancelled => "tag tag-outline",
        _ => "tag tag-neutral",
    };

    public static string ClaimTypeLabel(ClaimType type) => type switch
    {
        ClaimType.PrivateHealthFund => "Health fund",
        ClaimType.MedicareCdbs => "Medicare CDBS",
        ClaimType.Dva => "DVA",
        ClaimType.PublicScheme => "Public scheme",
        ClaimType.ThirdPartyInsurer => "Insurer",
        _ => type.ToString(),
    };

    /// <summary>
    /// A money cell in the aged-debtor table, darkening with age.
    /// </summary>
    /// <remarks>
    /// Zero is rendered faint rather than hidden, so the columns stay readable as columns —
    /// a table of blanks and figures is harder to scan than a table of figures.
    /// </remarks>
    public static string AgedCell(decimal amount, int bucketDays)
    {
        if (amount <= 0m) return "body-cell text-ink/30";

        return bucketDays switch
        {
            >= 90 => "body-cell font-bold text-accent-800",
            >= 60 => "body-cell font-semibold text-accent-700",
            _ => "body-cell",
        };
    }

    /// <summary>One row of the invoice list, highlighted when selected.</summary>
    public static string InvoiceRow(bool isSelected) => isSelected
        ? "cursor-pointer bg-accent-100"
        : "cursor-pointer hover:bg-surface";
}
