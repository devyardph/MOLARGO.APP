using DYS.Molargo.Api.Contracts;
using DYS.Molargo.Data;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using FundClaim = DYS.Molargo.Domain.Entities.Claim;

namespace DYS.Molargo.Api.Endpoints;

/// <summary>Invoices, payments, claims and the fee catalogue.</summary>
/// <inheritdoc cref="PatientsEndpoint" path="/remarks"/>
public sealed class BillingEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/billing").WithTags("Billing");

        group.MapGet("/invoices", InvoicesAsync)
            .WithSummary("Invoices, newest first. Filter by patient or status.");

        group.MapGet("/invoices/{id:guid}", InvoiceAsync)
            .WithSummary("One invoice with its lines and payments.");

        group.MapGet("/payments", PaymentsAsync)
            .WithSummary("Payments received, newest first.");

        group.MapGet("/claims", ClaimsAsync)
            .WithSummary("Health-fund claims. Recorded here, never submitted — see the README.");

        group.MapGet("/catalogue", CatalogueAsync)
            .WithSummary("The fee catalogue.");
    }

    /// <summary>GET /billing/invoices</summary>
    private static async Task<IResult> InvoicesAsync(
        Guid? patientId,
        string? status,
        int? page,
        IRepository<Invoice> invoices,
        CancellationToken ct)
    {
        var rows = await invoices.ListAsync(
            invoice => patientId == null || invoice.PatientId == patientId, ct);

        // Filtered in memory, not in the query. Status is an enum the caller sends as text,
        // and pushing an unparsed string into the predicate would turn a typo into an empty
        // result rather than a refusal.
        if (status is { Length: > 0 })
        {
            if (!Enum.TryParse<InvoiceStatus>(status, ignoreCase: true, out var wanted))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = [$"\"{status}\" is not an invoice status."],
                });
            }

            rows = rows.Where(invoice => invoice.Status == wanted).ToList();
        }

        var ordered = rows
            .OrderByDescending(invoice => invoice.IssuedUtc ?? invoice.CreatedUtc)
            .ToList();

        return Results.Ok(ApiDefaults.Page(ordered, page, InvoiceDto.From));
    }

    /// <summary>GET /billing/invoices/{id}</summary>
    /// <remarks>
    /// Lines and payments come with it. An invoice without its lines is a total nobody can
    /// check, and the ledger is the truth here — the cached total on the invoice follows
    /// the payments, so returning one without the other invites a client to disagree with
    /// the app about what is owed.
    /// </remarks>
    private static async Task<IResult> InvoiceAsync(
        Guid id,
        IRepository<Invoice> invoices,
        IRepository<InvoiceLine> lines,
        IRepository<Payment> payments,
        CancellationToken ct)
    {
        var invoice = await invoices.GetByIdAsync(id, ct);

        if (invoice is null) return Results.NotFound();

        var invoiceLines = await lines.ListAsync(line => line.InvoiceId == id, ct);
        var received = await payments.ListAsync(payment => payment.InvoiceId == id, ct);

        return Results.Ok(new InvoiceDetailDto(
            InvoiceDto.From(invoice),
            invoiceLines
                .OrderBy(line => line.ServiceDate)
                .Select(line => new InvoiceLineDto(
                    line.Id,
                    line.ItemNumber,
                    line.Description,
                    line.ToothNumber,
                    line.Quantity,
                    line.UnitFee,
                    line.DiscountAmount,
                    line.LineTotal,
                    line.ServiceDate))
                .ToList(),
            received
                .OrderByDescending(payment => payment.ReceivedUtc)
                .Select(PaymentDto.From)
                .ToList()));
    }

    /// <summary>GET /billing/payments</summary>
    private static async Task<IResult> PaymentsAsync(
        Guid? patientId, int? page, IRepository<Payment> payments, CancellationToken ct)
    {
        var rows = await payments.ListAsync(
            payment => patientId == null || payment.PatientId == patientId, ct);

        var ordered = rows.OrderByDescending(payment => payment.ReceivedUtc).ToList();

        return Results.Ok(ApiDefaults.Page(ordered, page, PaymentDto.From));
    }

    /// <summary>GET /billing/claims</summary>
    private static async Task<IResult> ClaimsAsync(
        Guid? patientId, int? page, IRepository<FundClaim> claims, CancellationToken ct)
    {
        var rows = await claims.ListAsync(
            claim => patientId == null || claim.PatientId == patientId, ct);

        var ordered = rows
            .OrderByDescending(claim => claim.SubmittedUtc ?? claim.CreatedUtc)
            .ToList();

        return Results.Ok(ApiDefaults.Page(ordered, page, claim => new ClaimDto(
            claim.Id,
            claim.PatientId,
            claim.InvoiceId,
            claim.Type.ToString(),
            claim.Status.ToString(),
            claim.PayerName,
            claim.AmountClaimed,
            claim.AmountApproved,
            claim.SubmittedUtc,
            claim.AssessedUtc,
            claim.AssessmentMessage)));
    }

    /// <summary>GET /billing/catalogue</summary>
    private static async Task<IResult> CatalogueAsync(
        string? search,
        bool? includeWithdrawn,
        int? page,
        IRepository<ProcedureCode> codes,
        CancellationToken ct)
    {
        var rows = await codes.ListAsync(
            code => includeWithdrawn == true || code.IsActive, ct);

        var term = search?.Trim();

        if (term is { Length: > 0 })
        {
            rows = rows
                .Where(code => code.ItemNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || code.Description.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (code.Category ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var ordered = rows
            .OrderBy(code => code.Category)
            .ThenBy(code => code.ItemNumber)
            .ToList();

        return Results.Ok(ApiDefaults.Page(ordered, page, code => new ProcedureCodeDto(
            code.Id,
            code.ItemNumber,
            code.Description,
            code.PatientFriendlyName,
            code.Category,
            code.Fee,
            code.TypicalDurationMinutes,
            code.IsActive)));
    }
}

public sealed record InvoiceDto(
    Guid Id,
    Guid PatientId,
    string? InvoiceNumber,
    string Status,
    DateTime? IssuedUtc,
    DateOnly? DueOn,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal Total,
    decimal AmountPaid,
    decimal Outstanding)
{
    public static InvoiceDto From(Invoice invoice) => new(
        invoice.Id,
        invoice.PatientId,
        invoice.InvoiceNumber,
        invoice.Status.ToString(),
        invoice.IssuedUtc,
        invoice.DueOn,
        invoice.Subtotal,
        invoice.DiscountAmount,
        invoice.TaxAmount,
        invoice.Total,
        invoice.AmountPaid,

        // Given, not left to the caller. Every client would work it out the same way and
        // one of them would eventually get the sign wrong on a credit.
        invoice.Total - invoice.AmountPaid);
}

public sealed record InvoiceLineDto(
    Guid Id,
    string ItemNumber,
    string Description,
    string? ToothNumber,
    int Quantity,
    decimal UnitFee,
    decimal DiscountAmount,
    decimal LineTotal,
    DateOnly ServiceDate);

public sealed record InvoiceDetailDto(
    InvoiceDto Invoice,
    IReadOnlyList<InvoiceLineDto> Lines,
    IReadOnlyList<PaymentDto> Payments);

public sealed record PaymentDto(
    Guid Id,
    Guid PatientId,
    Guid? InvoiceId,
    string Method,
    decimal Amount,
    DateTime ReceivedUtc,
    string? Reference,
    Guid? ReversesPaymentId)
{
    public static PaymentDto From(Payment payment) => new(
        payment.Id,
        payment.PatientId,
        payment.InvoiceId,
        payment.Method.ToString(),
        payment.Amount,
        payment.ReceivedUtc,
        payment.Reference,
        payment.ReversesPaymentId);
}

public sealed record ClaimDto(
    Guid Id,
    Guid PatientId,
    Guid InvoiceId,
    string Type,
    string Status,
    string? PayerName,
    decimal AmountClaimed,
    decimal AmountApproved,
    DateTime? SubmittedUtc,
    DateTime? AssessedUtc,
    string? AssessmentMessage);

public sealed record ProcedureCodeDto(
    Guid Id,
    string ItemNumber,
    string Description,
    string? PatientFriendlyName,
    string? Category,
    decimal Fee,
    int? TypicalDurationMinutes,
    bool IsActive);
