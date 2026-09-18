using System.Globalization;
using System.Text;

using DYS.Molargo.Shared.Components;

namespace DYS.Molargo.Shared.Services;

/// <summary>One priced line on a receipt.</summary>
public sealed record ReceiptLine(string Description, decimal Amount, string? Note = null);

/// <summary>
/// Everything that goes on one receipt, already decided by the caller.
/// </summary>
/// <remarks>
/// A plain record rather than an invoice, so a receipt can be laid out — and read on a
/// settings screen — without a payment existing. Billing will hand it a real invoice's
/// figures; the printer pane hands it a sample.
/// </remarks>
public sealed record ReceiptDocument(
    string PracticeName,
    string? AddressLine,
    string? Phone,
    string? Abn,
    string Title,
    string Reference,
    DateTime IssuedLocal,
    string PatientName,
    string? ProviderName,
    IReadOnlyList<ReceiptLine> Lines,
    decimal Total,
    decimal Paid,
    string? PaymentMethod,
    string? Footer);

/// <summary>
/// Lays a receipt out as fixed-width text at a given column count.
/// </summary>
/// <remarks>
/// <para>
/// Plain text, not ESC/POS. Every thermal printer prints text sent to it; only the paper
/// cut, the drawer kick and a stored logo need escape codes, and none of those change the
/// layout. Keeping this at text means one rendering serves three paths — what a person
/// reads in a preview, what a fallback page print produces, and what would go down a
/// socket. Three renderings drift, and only one of them ever gets looked at.
/// </para>
/// <para>
/// Width is a parameter rather than a constant because 58 mm paper is 32 columns and 80 mm
/// is 48. A layout that assumes 48 does not narrow gracefully — it wraps mid-figure, which
/// is how a receipt ends up showing a different total from the one it charged.
/// </para>
/// </remarks>
public static class ReceiptLayout
{
    /// <summary>The narrowest width this layout still holds together at.</summary>
    /// <remarks>
    /// Below this the money column and a two-character description cannot both fit, so the
    /// renderer would be producing something misleading rather than something narrow.
    /// </remarks>
    public const int MinimumColumns = 24;

    public static string Render(ReceiptDocument receipt, int columns)
    {
        var width = Math.Max(MinimumColumns, columns);
        var page = new StringBuilder();

        void Line(string text = "") => page.Append(text).Append('\n');

        void Centred(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            foreach (var part in Centre(text, width)) Line(part);
        }

        Centred(receipt.PracticeName.ToUpperInvariant());
        Centred(receipt.AddressLine);
        Centred(receipt.Phone);
        Centred(receipt.Abn is { Length: > 0 } abn ? $"ABN {abn}" : null);

        Line();
        Centred(receipt.Title.ToUpperInvariant());
        Line(Rule(width));

        Line(Pair("Receipt", receipt.Reference, width));
        Line(Pair("Date", receipt.IssuedLocal.ToString("d MMM yyyy h:mm tt"), width));
        Line(Pair("Patient", receipt.PatientName, width));

        if (receipt.ProviderName is { Length: > 0 } provider)
        {
            Line(Pair("Provider", provider, width));
        }

        Line(Rule(width));

        foreach (var line in receipt.Lines)
        {
            Line(Money(line.Description, line.Amount, width));

            if (line.Note is { Length: > 0 } note)
            {
                // Indented under its line, and wrapped: an item number or a tooth that ran
                // off the edge of the paper is exactly the part a patient queries.
                foreach (var part in Wrap(note, width - 2)) Line("  " + part);
            }
        }

        Line(Rule(width));
        Line(Money("TOTAL", receipt.Total, width));
        Line(Money("PAID", receipt.Paid, width));

        var owing = receipt.Total - receipt.Paid;

        // Printed only when there is one. A receipt reading "BALANCE DUE $0.00" invites
        // the question it was meant to have answered.
        if (owing != 0)
        {
            Line(Money(owing > 0 ? "BALANCE DUE" : "CREDIT", Math.Abs(owing), width));
        }

        if (receipt.PaymentMethod is { Length: > 0 } method)
        {
            Line();
            Line(Pair("Paid by", method, width));
        }

        if (receipt.Footer is { Length: > 0 } footer)
        {
            Line();

            // The author's own line breaks are kept, and each is then wrapped to the paper.
            foreach (var authored in footer.Replace("\r\n", "\n").Split('\n'))
            {
                Centred(authored.Trim());
            }
        }

        return page.ToString();
    }

    /// <summary>A sample receipt, for proving the layout without a payment.</summary>
    /// <remarks>
    /// Deliberately awkward: a description longer than 58 mm paper can hold, notes beneath
    /// the lines, and a part payment. A sample of one short line and a round number proves
    /// nothing about the case that actually breaks.
    /// </remarks>
    public static ReceiptDocument Sample(
        string practiceName,
        string? addressLine,
        string? phone,
        string? abn,
        DateTime issuedLocal,
        string? providerName,
        string? footer) =>
        new(
            practiceName,
            addressLine,
            phone,
            abn,
            "Test print",
            "TEST-0000",
            issuedLocal,
            "Sample, Patient",
            providerName,
            [
                new ReceiptLine("Comprehensive oral examination", 78.00m, "Item 011"),
                new ReceiptLine("Removal of plaque and calculus", 145.00m, "Item 111"),
                new ReceiptLine(
                    "Adhesive restoration, posterior, two surfaces",
                    210.00m,
                    "Item 532, tooth 36"),
            ],
            433.00m,
            300.00m,
            "EFTPOS",
            footer);

    // ---- fixed-width helpers ---------------------------------------------

    private static string Rule(int width) => new('-', width);

    /// <summary>Label left, value right, on one line where both fit.</summary>
    /// <remarks>
    /// Where they do not, the value drops to its own line rather than being truncated. A
    /// clipped receipt number is worse than a receipt one line longer.
    /// </remarks>
    private static string Pair(string label, string value, int width)
    {
        var gap = width - label.Length - value.Length;

        return gap >= 1
            ? label + new string(' ', gap) + value
            : label + "\n" + Clip(value, width).PadLeft(width);
    }

    /// <summary>
    /// A description with its amount right-aligned in a fixed money column.
    /// </summary>
    /// <remarks>
    /// The column is reserved first and the description wrapped into what is left, so the
    /// figures line up down the page and no amount can be pushed off the paper by a long
    /// description.
    /// </remarks>
    private static string Money(string description, decimal amount, int width)
    {
        // The practice's currency, not the machine's locale. A receipt printed from a
        // host set to en-US was writing US dollar signs on Australian invoices.
        var figure = amount.ToString("C2", MolargoFormat.Currency);
        var column = Math.Max(figure.Length + 1, 10);
        var room = width - column;

        if (room < 2)
        {
            // Nothing sane left for the description. Separate lines beat overlapping text.
            return description + "\n" + figure.PadLeft(width);
        }

        var parts = Wrap(description, room).ToArray();
        var page = new StringBuilder();

        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0) page.Append('\n');

            // The figure goes on the last line of the wrapped description, so the money
            // column stays a column even where descriptions run to three lines.
            page.Append(i == parts.Length - 1
                ? parts[i].PadRight(room) + figure.PadLeft(column)
                : parts[i]);
        }

        return page.ToString();
    }

    private static IEnumerable<string> Centre(string text, int width)
    {
        foreach (var part in Wrap(text, width))
        {
            var pad = (width - part.Length) / 2;

            yield return pad > 0 ? new string(' ', pad) + part : part;
        }
    }

    private static string Clip(string value, int width) =>
        value.Length <= width ? value : value[..width];

    /// <summary>
    /// Breaks text on spaces to fit a width, splitting a word only where it cannot fit.
    /// </summary>
    /// <remarks>
    /// Hand-rolled rather than a regex: this runs for every line of every printed page, and
    /// the browser-hosted head pays for regex construction at startup.
    /// </remarks>
    private static IEnumerable<string> Wrap(string text, int width)
    {
        if (width < 1)
        {
            yield return text;
            yield break;
        }

        var value = text.Trim();

        if (value.Length == 0)
        {
            yield return string.Empty;
            yield break;
        }

        var start = 0;

        while (start < value.Length)
        {
            if (value.Length - start <= width)
            {
                yield return value[start..];
                yield break;
            }

            var breakAt = value.LastIndexOf(' ', start + width, width + 1);

            if (breakAt <= start)
            {
                // One unbroken run — a product code, a URL. Cut it rather than let it run
                // off the edge of the paper, which loses the tail with no sign it did.
                yield return value.Substring(start, width);
                start += width;
                continue;
            }

            yield return value[start..breakAt];
            start = breakAt + 1;
        }
    }
}
