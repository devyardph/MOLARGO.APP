using DYS.Molargo.Api.Contracts;
using DYS.Molargo.Domain.Data;
using DYS.Molargo.Domain.Entities;

namespace DYS.Molargo.Api.Endpoints;

/// <summary>Message templates, and what was actually sent.</summary>
/// <inheritdoc cref="PatientsEndpoint" path="/remarks"/>
public sealed class CommsEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/comms").WithTags("Comms");

        group.MapGet("/templates", TemplatesAsync)
            .WithSummary("Message templates. The automated ones are those with a trigger.");

        group.MapGet("/log", LogAsync)
            .WithSummary("What was sent to patients, newest first. Includes failures.");
    }

    /// <summary>GET /comms/templates</summary>
    private static async Task<IResult> TemplatesAsync(
        IRepository<MessageTemplate> templates, CancellationToken ct)
    {
        var rows = await templates.ListAsync(null, ct);

        return Results.Ok(rows
            .OrderBy(template => template.Name)
            .Select(template => new MessageTemplateDto(
                template.Id,
                template.Name,
                template.Channel.ToString(),
                template.Purpose.ToString(),
                template.Subject,
                template.Body,
                template.IsActive))
            .ToList());
    }

    /// <summary>GET /comms/log</summary>
    /// <remarks>
    /// Failures included, and not as an option. This log is the answer to "were they
    /// told" — a version that quietly showed only what succeeded would answer that question
    /// wrongly in exactly the case somebody is asking it.
    /// </remarks>
    private static async Task<IResult> LogAsync(
        Guid? patientId,
        int? page,
        IRepository<CommunicationLog> log,
        CancellationToken ct)
    {
        var rows = await log.ListAsync(
            entry => patientId == null || entry.PatientId == patientId, ct);

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
}

public sealed record MessageTemplateDto(
    Guid Id,
    string Name,
    string Channel,
    string Purpose,
    string? Subject,
    string Body,
    bool IsActive);

public sealed record CommunicationLogDto(
    Guid Id,
    Guid PatientId,
    string Channel,
    string Direction,
    string Purpose,
    string Status,
    string? Subject,
    string Body,
    string? Recipient,
    Guid? AppointmentId,
    DateTime? SentUtc,
    DateTime? DeliveredUtc,
    string? FailureReason);
