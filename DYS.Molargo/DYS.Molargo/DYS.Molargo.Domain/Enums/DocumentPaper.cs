namespace DYS.Molargo.Domain.Enums;

/// <summary>The page a prescription or referral is printed on.</summary>
/// <remarks>
/// Separate from <see cref="ReceiptPaper"/>, and deliberately so: a script is a document
/// that has to be legible, signed and filed for years, and thermal paper fades. Practices
/// here use A4; Letter is offered because the app should not assume a country.
/// </remarks>
public enum DocumentPaper
{
    A4 = 0,
    Letter = 1,
}
