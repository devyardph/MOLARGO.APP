namespace DYS.Molargo.Domain.Enums;

/// <summary>What a receipt is printed on.</summary>
/// <remarks>
/// Only the two thermal roll widths a practice counter actually has. The width decides how
/// many characters fit on a line, which is very nearly the whole of receipt layout — see
/// <c>ReceiptLayout</c>.
/// </remarks>
public enum ReceiptPaper
{
    /// <summary>80 mm roll — 48 characters a line in the printer's default font.</summary>
    Roll80mm = 0,

    /// <summary>58 mm roll — 32 characters, which forces a narrower layout.</summary>
    Roll58mm = 1,
}
