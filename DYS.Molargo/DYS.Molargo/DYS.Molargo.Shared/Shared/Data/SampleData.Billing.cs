using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Shared.Data;

/// <summary>
/// The practice's ledger beyond the example patient: today's invoices, and debt old enough
/// to age.
/// </summary>
/// <remarks>
/// <para>
/// The existing invoice seed belongs to Margaret alone and is all dated weeks back, so the
/// invoice list and the banking sheet — both of which are about one day — opened empty,
/// and every aged-debtor bucket but one was blank. A billing screen with nothing in it
/// cannot be judged.
/// </para>
/// <para>
/// The invoices here follow today's actual appointments: the completed exam is paid, the
/// hygiene visit is part-paid by a fund with a gap outstanding, and the extraction is
/// unpaid because the patient is still in the building.
/// </para>
/// </remarks>
internal static partial class SampleData
{
    private static (List<Invoice> Invoices, List<InvoiceLine> Lines, List<Payment> Payments,
        List<Claim> Claims)
        PracticeBilling(DateOnly today)
    {
        var invoices = new List<Invoice>();
        var lines = new List<InvoiceLine>();
        var payments = new List<Payment>();
        var claims = new List<Claim>();

        // ---- today ------------------------------------------------------

        // Liam's exam and clean: done, paid on the card as he left.
        Raise(invoices, lines, "today-okafor", "10451", "10204", today, 0,
            [("011", "Comprehensive oral examination", 95m),
             ("114", "Removal of calculus — first appointment", 150m)]);

        payments.Add(Receipt("today-okafor-card", "invoice:today-okafor", "10204",
            PaymentMethod.EftposCard, 245m, today, 9, 05, "EFTPOS 8841"));

        // Tom's hygiene visit: the fund paid its share on the terminal, the gap did not
        // get taken because he had to leave. This is the row the front desk chases.
        Raise(invoices, lines, "today-braddon", "10452", "10206", today, 0,
            [("114", "Removal of calculus — first appointment", 150m),
             ("121", "Topical application of remineralising agent", 45m)],
            providerKey: "ito");

        claims.Add(FundClaim("today-braddon", "invoice:today-braddon", "10206",
            claimed: 195m, approved: 130m, ClaimStatus.PartiallyApproved, today,
            "Preventive limit part-used. Benefit paid to remaining limit."));

        payments.Add(Receipt("today-braddon-fund", "invoice:today-braddon", "10206",
            PaymentMethod.HicapsFundBenefit, 130m, today, 10, 20, "HICAPS 902144",
            claimKey: "claim:today-braddon"));

        // Sofia's extraction: invoiced, nothing taken — she is still in the chair.
        Raise(invoices, lines, "today-reyes", "10453", "10205", today, 0,
            [("311", "Removal of a tooth", 260m)],
            providerKey: "ellery");

        // A claim sitting unassessed, so the claims pane has something to record an
        // outcome against.
        claims.Add(FundClaim("today-reyes", "invoice:today-reyes", "10205",
            claimed: 260m, approved: 0m, ClaimStatus.Pending, today, message: null));

        // ---- debt old enough to age -------------------------------------

        // Dated so the buckets actually populate. Ageing runs from the due date, so an
        // invoice has to be 44 days old on 30-day terms to reach the 30-day bucket —
        // dating these by age rather than by overdue days put three of them in "current"
        // and left the screen looking like it could only count one column.
        //
        // 50 days old, 36 overdue: the 30-day bucket.
        Raise(invoices, lines, "aged-papas", "10402", "10209", today.AddDays(-50), 14,
            [("015", "Limited oral examination", 75m),
             ("531", "Adhesive restoration — two surfaces, posterior", 235m)]);

        // 85 days old, 71 overdue: the 60-day bucket.
        Raise(invoices, lines, "aged-chen", "10371", "10210", today.AddDays(-85), 14,
            [("022", "Intraoral periapical radiograph", 55m),
             ("415", "Complete chemomechanical preparation of root canal", 480m)],
            providerKey: "ellery");

        payments.Add(Receipt("aged-chen-part", "invoice:aged-chen", "10210",
            PaymentMethod.BankTransfer, 200m, today.AddDays(-70), 11, 0, "TRF 55120"));

        // 118 days old, 104 overdue: the 90-plus bucket. A fund rejection is why it was
        // never settled — the practice thought the fund had it.
        Raise(invoices, lines, "aged-marsh", "10318", "10212", today.AddDays(-118), 14,
            [("597", "Crown — veneered, indirect", 1650m)]);

        claims.Add(FundClaim("aged-marsh", "invoice:aged-marsh", "10212",
            claimed: 1650m, approved: 0m, ClaimStatus.Rejected, today.AddDays(-112),
            "Item 597 requires a pre-treatment estimate. Resubmit with the estimate "
                + "reference and radiographs."));

        return (invoices, lines, payments, claims);
    }

    /// <summary>
    /// Raises an invoice with its lines, totalled from the lines.
    /// </summary>
    /// <remarks>
    /// The total is summed rather than passed in. Two numbers that must agree eventually
    /// disagree, and an invoice whose total does not match its lines is the one thing a
    /// billing screen must never show.
    /// </remarks>
    private static void Raise(
        List<Invoice> invoices,
        List<InvoiceLine> lines,
        string key,
        string number,
        string patientNumber,
        DateOnly issued,
        int dueInDays,
        (string Code, string Description, decimal Fee)[] items,
        string providerKey = "vance")
    {
        var total = items.Sum(item => item.Fee);

        invoices.Add(new Invoice
        {
            Id = Id($"invoice:{key}"),
            PatientId = Id($"patient:{patientNumber}"),
            PracticeLocationId = SydneyCbd,

            // Whoever did the work, not always Dr Vance. Everything was billed to one
            // provider, which reported the whole practice's production as hers and left
            // the reports screen's provider comparison a single bar.
            ProviderId = Id($"provider:{providerKey}"),
            InvoiceNumber = number,

            // Issued, not paid. The service recomputes the status and the paid total from
            // the payment rows the moment anything touches the invoice, so seeding a
            // status here would only be right until the first click.
            Status = InvoiceStatus.Issued,
            IssuedUtc = issued.ToDateTime(new TimeOnly(17, 0), DateTimeKind.Local)
                .ToUniversalTime(),
            DueOn = issued.AddDays(dueInDays),
            Subtotal = total,
            Total = total,
            AmountPaid = 0m,
        });

        foreach (var item in items)
        {
            lines.Add(new InvoiceLine
            {
                Id = Id($"invoice:{key}:line:{item.Code}"),
                InvoiceId = Id($"invoice:{key}"),

                // Linked to the code, not just to its number. Without this every line
                // reported as "not categorised", which collapsed the procedure-mix report
                // to a single row — the item number alone does not carry the category.
                ProcedureCodeId = Id($"procedure:{item.Code}"),
                ItemNumber = item.Code,
                Description = item.Description,
                Quantity = 1,
                UnitFee = item.Fee,
                LineTotal = item.Fee,
                ServiceDate = issued,
                ProviderId = Id($"provider:{providerKey}"),
            });
        }
    }

    private static Payment Receipt(
        string key,
        string invoiceKey,
        string patientNumber,
        PaymentMethod method,
        decimal amount,
        DateOnly received,
        int hour,
        int minute,
        string reference,
        string? claimKey = null) =>
        new()
        {
            Id = Id($"payment:{key}"),
            PatientId = Id($"patient:{patientNumber}"),
            PracticeLocationId = SydneyCbd,
            InvoiceId = Id(invoiceKey),
            Method = method,
            Amount = amount,
            ReceivedUtc = received.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Local)
                .ToUniversalTime(),
            Reference = reference,
            ClaimId = claimKey is null ? null : Id(claimKey),
            ReceivedByProviderId = Id("provider:brennan"),
        };

    private static Claim FundClaim(
        string key,
        string invoiceKey,
        string patientNumber,
        decimal claimed,
        decimal approved,
        ClaimStatus status,
        DateOnly submitted,
        string? message) =>
        new()
        {
            Id = Id($"claim:{key}"),
            PatientId = Id($"patient:{patientNumber}"),
            InvoiceId = Id(invoiceKey),
            Type = ClaimType.PrivateHealthFund,
            Status = status,
            PayerName = "HCF",
            AmountClaimed = claimed,
            AmountApproved = approved,
            SubmittedUtc = submitted.ToDateTime(new TimeOnly(17, 10), DateTimeKind.Local)
                .ToUniversalTime(),
            AssessedUtc = status is ClaimStatus.Pending or ClaimStatus.Submitted
                ? null
                : submitted.ToDateTime(new TimeOnly(17, 30), DateTimeKind.Local).ToUniversalTime(),
            AssessmentMessage = message,
        };
}
