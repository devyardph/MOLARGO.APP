using DYS.Molargo.Api.Contracts;
using DYS.Molargo.Data;
using DYS.Molargo.Domain.Entities;

namespace DYS.Molargo.Api.Endpoints;

/// <summary>
/// The rest of one patient's record: documents, consent, medical history, messages.
/// </summary>
/// <remarks>
/// Split from <see cref="PatientsEndpoint"/>, which is the patient list and the patient
/// themselves. These are the things hanging off a patient, and putting all of it in one
/// class would make the file that holds "patients" the file everybody edits — the same
/// reason the endpoints are one class per feature in the first place.
/// </remarks>
public sealed class PatientRecordEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/patients/{patientId:guid}").WithTags("Patient record");

        group.MapGet("/documents", DocumentsAsync)
            .WithSummary("Document metadata. The files themselves are not served — see the README.");

        group.MapGet("/consent", ConsentAsync)
            .WithSummary("Consent forms and whether they are signed.");

        group.MapGet("/medical-history", MedicalHistoryAsync)
            .WithSummary("Medical history forms, newest first.");

        group.MapGet("/messages", MessagesAsync)
            .WithSummary("What was sent to this patient, newest first.");

        group.MapGet("/invoices", InvoicesAsync)
            .WithSummary("This patient's invoices and what they still owe.");
    }

    /// <summary>GET /patients/{id}/documents</summary>
    /// <remarks>
    /// Metadata only. The file lives on the device's disk under a relative path, and the
    /// server has no copy of it — returning the path would hand a caller a location that
    /// means nothing to them, so it is deliberately left off the DTO.
    /// </remarks>
    private static async Task<IResult> DocumentsAsync(
        Guid patientId, IRepository<PatientDocument> documents, CancellationToken ct)
    {
        var rows = await documents.ListAsync(
            document => document.PatientId == patientId, ct);

        return Results.Ok(rows
            .OrderByDescending(document => document.DocumentDateUtc ?? document.CreatedUtc)
            .Select(document => new PatientDocumentDto(
                document.Id,
                document.Kind.ToString(),
                document.Name,
                document.Description,
                document.ContentType,
                document.SizeBytes,
                document.DocumentDateUtc,
                document.AppointmentId))
            .ToList());
    }

    /// <summary>GET /patients/{id}/consent</summary>
    /// <remarks>
    /// The signature image is left off. It is a PNG data URI several kilobytes long, and a
    /// list of ten forms would be mostly signatures — for something a caller reading a
    /// consent list does not need to see.
    /// </remarks>
    private static async Task<IResult> ConsentAsync(
        Guid patientId, IRepository<ConsentForm> consent, CancellationToken ct)
    {
        var rows = await consent.ListAsync(form => form.PatientId == patientId, ct);

        return Results.Ok(rows
            .OrderByDescending(form => form.SignedUtc ?? form.CreatedUtc)
            .Select(form => new ConsentFormDto(
                form.Id,
                form.Title,
                form.Status.ToString(),
                form.SignedUtc,
                form.SignedByName,
                form.SignedByRelationship,
                form.ExpiresOn,
                form.TreatmentPlanId))
            .ToList());
    }

    /// <summary>GET /patients/{id}/medical-history</summary>
    private static async Task<IResult> MedicalHistoryAsync(
        Guid patientId, IRepository<MedicalHistoryForm> forms, CancellationToken ct)
    {
        var rows = await forms.ListAsync(form => form.PatientId == patientId, ct);

        return Results.Ok(rows
            .OrderByDescending(form => form.CompletedUtc ?? form.CreatedUtc)
            .Select(form => new MedicalHistoryFormDto(
                form.Id,
                form.FormVersion,
                form.CompletedUtc,
                form.SignedUtc,
                form.SignedByName,
                form.AppointmentId))
            .ToList());
    }

    /// <summary>GET /patients/{id}/messages</summary>
    private static async Task<IResult> MessagesAsync(
        Guid patientId,
        int? page,
        IRepository<CommunicationLog> log,
        CancellationToken ct)
    {
        var rows = await log.ListAsync(entry => entry.PatientId == patientId, ct);

        var ordered = rows
            .OrderByDescending(entry => entry.SentUtc ?? entry.CreatedUtc)
            .ToList();

        return Results.Ok(ApiDefaults.Page(ordered, page, entry => new CommunicationLogDto(
            entry.Id,
            entry.PatientId,
            entry.Channel.ToString(),
            entry.Direction.ToString(),
            entry.Purpose.ToString(),
            entry.Status.ToString(),
            entry.Subject,
            entry.Body,
            entry.Recipient,
            entry.AppointmentId,
            entry.SentUtc,
            entry.DeliveredUtc,
            entry.FailureReason)));
    }

    /// <summary>GET /patients/{id}/invoices</summary>
    private static async Task<IResult> InvoicesAsync(
        Guid patientId, IRepository<Invoice> invoices, CancellationToken ct)
    {
        var rows = await invoices.ListAsync(invoice => invoice.PatientId == patientId, ct);

        return Results.Ok(rows
            .OrderByDescending(invoice => invoice.IssuedUtc ?? invoice.CreatedUtc)
            .Select(InvoiceDto.From)
            .ToList());
    }
}

public sealed record PatientDocumentDto(
    Guid Id,
    string Kind,
    string Name,
    string? Description,
    string? ContentType,
    long SizeBytes,
    DateTime? DocumentDateUtc,
    Guid? AppointmentId);

public sealed record ConsentFormDto(
    Guid Id,
    string Title,
    string Status,
    DateTime? SignedUtc,
    string? SignedByName,
    string? SignedByRelationship,
    DateOnly? ExpiresOn,
    Guid? TreatmentPlanId);

public sealed record MedicalHistoryFormDto(
    Guid Id,
    int FormVersion,
    DateTime? CompletedUtc,
    DateTime? SignedUtc,
    string? SignedByName,
    Guid? AppointmentId);
