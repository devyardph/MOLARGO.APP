using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// Where the practice's receipts and printed documents go.
/// </summary>
/// <remarks>
/// <para>
/// Two printers, not one, because the two artefacts are not the same object. A receipt is
/// a 48-character strip off a thermal roll at the counter. A prescription is an A4 page
/// that has to be signed, handed over and still legible in a pharmacy's files years later
/// — thermal paper fades, so a script coming off the receipt roll is a defect, not a
/// convenience. Holding one printer for both would force that.
/// </para>
/// <para>
/// One row per clinic. That is a real limitation where a practice has two counters with
/// different printers: this cannot describe both. It is recorded here rather than papered
/// over — the device head keeps its own database file, so per-install already means per
/// terminal there, and it is only the shared server head where two counters would collide.
/// Splitting the row per terminal needs a terminal identity the app does not have.
/// </para>
/// <para>
/// Nothing here opens a printer. The fields are what an operator has to have told us
/// before any print path could work, and the settings screen states plainly that no print
/// path exists yet.
/// </para>
/// </remarks>
public sealed class PrinterSettings : EntityBase
{
    // ---- receipt printer -------------------------------------------------

    /// <summary>How the receipt printer is attached.</summary>
    public PrinterConnection Connection { get; set; } = PrinterConnection.Network;

    /// <summary>
    /// What identifies the device — a host name, a USB device name, or a pairing name.
    /// </summary>
    /// <remarks>
    /// One field for all three connection kinds rather than three, because only one is ever
    /// in use and three fields would leave two stale values on screen to be misread.
    /// </remarks>
    public string? DeviceAddress { get; set; }

    /// <summary>
    /// The raw port a network printer listens on.
    /// </summary>
    /// <remarks>
    /// 9100 is the JetDirect convention nearly every network thermal printer follows.
    /// Ignored for USB and Bluetooth, and the screen hides it there.
    /// </remarks>
    public int NetworkPort { get; set; } = 9100;

    /// <summary>What staff call this printer, for messages about it.</summary>
    public string? ReceiptPrinterName { get; set; }

    /// <summary>The roll width, which sets the line length of every receipt.</summary>
    public ReceiptPaper Paper { get; set; } = ReceiptPaper.Roll80mm;

    /// <summary>Print a receipt as soon as a payment is recorded, without being asked.</summary>
    public bool PrintReceiptAutomatically { get; set; }

    /// <summary>Pulse the cash drawer's kick-out when a receipt prints.</summary>
    /// <remarks>
    /// Only meaningful for a drawer wired to the printer, which is how counter drawers are
    /// opened — the printer, not the software, holds the solenoid.
    /// </remarks>
    public bool OpenCashDrawer { get; set; }

    /// <summary>
    /// Print the practice name as a graphic rather than as text.
    /// </summary>
    /// <remarks>
    /// Off by default. A logo has to be uploaded to the printer's own flash memory first,
    /// so turning this on before that is done prints nothing where the header should be.
    /// </remarks>
    public bool PrintLogo { get; set; }

    /// <summary>Lines added to the bottom of every receipt — ABN, a thank you, a URL.</summary>
    public string? ReceiptFooter { get; set; }

    // ---- documents: prescriptions and referrals ---------------------------

    /// <summary>The printer scripts and referrals go to, by its name on this machine.</summary>
    public string? DocumentPrinterName { get; set; }

    /// <summary>A4 or Letter.</summary>
    public DocumentPaper DocumentPaper { get; set; } = DocumentPaper.A4;

    /// <summary>
    /// How many copies of a prescription to print.
    /// </summary>
    /// <remarks>
    /// Two is common: one to the patient, one for the record. Capped by the service rather
    /// than here, because an entity should hold what was chosen, not police it.
    /// </remarks>
    public int PrescriptionCopies { get; set; } = 1;

    /// <summary>Print the prescriber's name and licence number in the signature block.</summary>
    /// <remarks>
    /// A script is invalid without an identifiable prescriber, so this defaults on. It is a
    /// setting only because a practice using pre-printed letterhead already has it.
    /// </remarks>
    public bool PrintPrescriberDetails { get; set; } = true;

    /// <summary>True where there is enough recorded to attempt a receipt.</summary>
    public bool ReceiptPrinterConfigured =>
        !string.IsNullOrWhiteSpace(DeviceAddress)
        && (Connection != PrinterConnection.Network || NetworkPort is > 0 and < 65536);

    /// <summary>The line length of a receipt, in characters.</summary>
    /// <remarks>
    /// 48 and 32 are the two printers' default fonts at 80 mm and 58 mm. Held here so the
    /// layout and the screen's preview cannot disagree about it.
    /// </remarks>
    public int ReceiptColumns => Paper == ReceiptPaper.Roll58mm ? 32 : 48;
}
