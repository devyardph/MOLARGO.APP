using DYS.Molargo.Api.Contracts;
using DYS.Molargo.Data;
using DYS.Molargo.Domain.Entities;

namespace DYS.Molargo.Api.Endpoints;

/// <summary>The clinical record: the chart, the notes, and perio.</summary>
/// <inheritdoc cref="PatientsEndpoint" path="/remarks"/>
public sealed class ChartingEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/patients/{patientId:guid}").WithTags("Charting");

        group.MapGet("/chart", ChartAsync)
            .WithSummary("The tooth chart as it currently stands.");

        group.MapGet("/notes", NotesAsync)
            .WithSummary("Clinical notes, newest first.");

        group.MapGet("/perio", PerioAsync)
            .WithSummary("Perio examinations, newest first.");

        group.MapGet("/alerts", AlertsAsync)
            .WithSummary("Medical alerts and allergies.");
    }

    /// <summary>GET /patients/{id}/chart</summary>
    /// <remarks>
    /// Current findings only unless <c>includeSuperseded</c> is asked for. A chart entry is
    /// superseded rather than deleted — the tooth that was decayed and is now filled has
    /// both rows — so returning everything by default would show every tooth in every state
    /// it has ever been in.
    /// </remarks>
    private static async Task<IResult> ChartAsync(
        Guid patientId,
        bool? includeSuperseded,
        IRepository<ToothChartEntry> chart,
        CancellationToken ct)
    {
        var entries = await chart.ListAsync(
            entry => entry.PatientId == patientId
                && (includeSuperseded == true || entry.SupersededOn == null), ct);

        return Results.Ok(entries
            .OrderBy(entry => entry.ToothNumber)
            .ThenByDescending(entry => entry.ObservedOn)
            .Select(entry => new ToothChartEntryDto(
                entry.Id,
                entry.ToothNumber,
                entry.Notation.ToString(),
                entry.Condition.ToString(),
                entry.Surfaces.ToString(),
                entry.Detail,
                entry.ObservedOn,
                entry.SupersededOn))
            .ToList());
    }

    /// <summary>GET /patients/{id}/notes</summary>
    private static async Task<IResult> NotesAsync(
        Guid patientId,
        int? page,
        IRepository<ClinicalNote> notes,
        CancellationToken ct)
    {
        var rows = await notes.ListAsync(note => note.PatientId == patientId, ct);

        var ordered = rows
            .OrderByDescending(note => note.TreatmentDateUtc)
            .ToList();

        return Results.Ok(ApiDefaults.Page(ordered, page, note => new ClinicalNoteDto(
            note.Id,
            note.ProviderId,
            note.AppointmentId,
            note.TreatmentDateUtc,
            note.Presenting,
            note.Examination,
            note.Diagnosis,
            note.TreatmentProvided,
            note.Plan,

            // Whether it is signed, said plainly. A signed note is the record — later
            // changes are additions, not edits — so a caller reading one needs to know
            // which of the two it is holding.
            note.LockedUtc is not null,
            note.LockedUtc,
            note.AmendsNoteId)));
    }

    /// <summary>GET /patients/{id}/perio</summary>
    private static async Task<IResult> PerioAsync(
        Guid patientId, IRepository<PerioExam> exams, CancellationToken ct)
    {
        var rows = await exams.ListAsync(exam => exam.PatientId == patientId, ct);

        return Results.Ok(rows
            .OrderByDescending(exam => exam.ExamDate)
            .Select(exam => new PerioExamDto(exam.Id, exam.ExamDate, exam.ProviderId, exam.CompletedUtc))
            .ToList());
    }

    /// <summary>GET /patients/{id}/alerts</summary>
    /// <remarks>
    /// Unresolved first, then by severity. An allergy list where the resolved entries sort
    /// among the live ones is a list somebody reads the wrong line of at the chair.
    /// </remarks>
    private static async Task<IResult> AlertsAsync(
        Guid patientId, IRepository<PatientAlert> alerts, CancellationToken ct)
    {
        var rows = await alerts.ListAsync(alert => alert.PatientId == patientId, ct);

        return Results.Ok(rows
            .OrderBy(alert => alert.ResolvedDate is null ? 0 : 1)
            .ThenByDescending(alert => alert.Severity)
            .Select(alert => new PatientAlertDto(
                alert.Id,
                alert.Kind.ToString(),
                alert.Severity.ToString(),
                alert.Summary,
                alert.Detail,
                alert.OnsetDate,
                alert.ResolvedDate))
            .ToList());
    }
}

public sealed record ToothChartEntryDto(
    Guid Id,
    string ToothNumber,
    string Notation,
    string Condition,
    string Surfaces,
    string? Detail,
    DateOnly ObservedOn,
    DateOnly? SupersededOn);

public sealed record ClinicalNoteDto(
    Guid Id,
    Guid ProviderId,
    Guid? AppointmentId,
    DateTime TreatmentDateUtc,
    string? Presenting,
    string? Examination,
    string? Diagnosis,
    string? TreatmentProvided,
    string? Plan,
    bool IsSigned,
    DateTime? SignedUtc,
    Guid? AmendsNoteId);

public sealed record PerioExamDto(Guid Id, DateOnly ExamDate, Guid? ProviderId, DateTime? CompletedUtc);

public sealed record PatientAlertDto(
    Guid Id,
    string Kind,
    string Severity,
    string Summary,
    string? Detail,
    DateOnly? OnsetDate,
    DateOnly? ResolvedDate);
