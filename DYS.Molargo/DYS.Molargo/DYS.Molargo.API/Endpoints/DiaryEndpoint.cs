using DYS.Molargo.Api.Contracts;
using DYS.Molargo.Data;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Api.Endpoints;

/// <summary>The diary: appointments, and the courses they belong to.</summary>
/// <inheritdoc cref="PatientsEndpoint" path="/remarks"/>
public sealed class DiaryEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/appointments").WithTags("Diary");

        group.MapGet("/", ListAsync)
            .WithSummary("Appointments in a date range. Defaults to the coming month.");

        group.MapGet("/{id:guid}", GetAsync)
            .WithSummary("One appointment.");

        group.MapGet("/series/{seriesId:guid}", GetSeriesAsync)
            .WithSummary("A course of treatment and its visits, in treatment order.");

        group.MapGet("/recalls", RecallsAsync)
            .WithSummary("Patients due back. Pending recalls only.");

        group.MapGet("/waitlist", WaitlistAsync)
            .WithSummary("The short-notice list, most urgent first.");
    }

    /// <summary>GET /appointments</summary>
    private static async Task<IResult> ListAsync(
        DateOnly? from,
        DateOnly? to,
        Guid? patientId,
        int? page,
        IRepository<Appointment> appointments,
        IRepository<PatientEntity> patients,
        CancellationToken ct)
    {
        // A bounded window by default. "Every appointment this clinic has ever taken" is a
        // query somebody writes once by accident and then wonders why the API fell over —
        // so an absent range means the coming month, and history has to be asked for.
        var start = (from ?? DateOnly.FromDateTime(DateTime.UtcNow))
            .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var end = (to ?? DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1))
            .ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var result = await appointments.GetPageAsync(
            page ?? 0,
            ApiDefaults.PageSize,
            appointment => appointment.StartUtc,
            predicate: appointment => appointment.StartUtc >= start
                && appointment.StartUtc <= end
                && (patientId == null || appointment.PatientId == patientId),
            ct: ct);

        var names = await NamesAsync(result.Items, patients, ct);

        return Results.Ok(new PageDto<AppointmentDto>(
            result.Items.Select(row => AppointmentDto.From(row, names)).ToList(),
            result.TotalCount,
            result.Page,
            result.PageSize));
    }

    /// <summary>GET /appointments/{id}</summary>
    private static async Task<IResult> GetAsync(
        Guid id,
        IRepository<Appointment> appointments,
        IRepository<PatientEntity> patients,
        CancellationToken ct)
    {
        var appointment = await appointments.GetByIdAsync(id, ct);

        if (appointment is null) return Results.NotFound();

        var names = await NamesAsync([appointment], patients, ct);

        return Results.Ok(AppointmentDto.From(appointment, names));
    }

    /// <summary>GET /appointments/series/{seriesId}</summary>
    private static async Task<IResult> GetSeriesAsync(
        Guid seriesId,
        IRepository<AppointmentSeries> series,
        IRepository<Appointment> appointments,
        IRepository<PatientEntity> patients,
        CancellationToken ct)
    {
        var course = await series.GetByIdAsync(seriesId, ct);

        if (course is null) return Results.NotFound();

        var visits = await appointments.ListAsync(
            appointment => appointment.AppointmentSeriesId == seriesId, ct);

        var names = await NamesAsync(visits, patients, ct);

        return Results.Ok(new AppointmentSeriesDto(
            course.Id,
            course.Name,
            course.PlannedVisits,

            // Ordered by position, not by date. The number is a place in the treatment, and
            // a visit moved later in the calendar is still the one it always was — see
            // Appointment.SeriesPosition.
            visits
                .OrderBy(visit => visit.SeriesPosition)
                .Select(visit => AppointmentDto.From(visit, names))
                .ToList()));
    }

    /// <summary>GET /appointments/recalls</summary>
    /// <remarks>
    /// Pending only, and due within a horizon. A recall that has been booked or written off
    /// is not work, and "every recall ever" is a list nobody can act on — the app uses the
    /// same 30-day window for the same reason.
    /// </remarks>
    private static async Task<IResult> RecallsAsync(
        int? withinDays,
        int? page,
        IRepository<Recall> recalls,
        IRepository<PatientEntity> patients,
        CancellationToken ct)
    {
        var horizon = DateOnly.FromDateTime(DateTime.UtcNow)
            .AddDays(Math.Clamp(withinDays ?? 30, 1, 365));

        var rows = await recalls.ListAsync(
            recall => recall.Status == RecallStatus.Pending && recall.DueOn <= horizon, ct);

        var ids = rows.Select(recall => recall.PatientId).ToHashSet();

        var people = await patients.ListAsync(patient => ids.Contains(patient.Id), ct);

        var names = people.ToDictionary(patient => patient.Id, patient => patient.FullName);

        var ordered = rows.OrderBy(recall => recall.DueOn).ToList();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return Results.Ok(ApiDefaults.Page(ordered, page, recall => new RecallDto(
            recall.Id,
            recall.PatientId,
            names.GetValueOrDefault(recall.PatientId, "Unknown patient"),
            recall.RecallType,
            recall.IntervalMonths,
            recall.DueOn,
            recall.DueOn < today,
            recall.LastContactedUtc,
            recall.ContactAttempts)));
    }

    /// <summary>GET /appointments/waitlist</summary>
    private static async Task<IResult> WaitlistAsync(
        int? page,
        IRepository<WaitlistEntry> waitlist,
        IRepository<PatientEntity> patients,
        CancellationToken ct)
    {
        var rows = await waitlist.ListAsync(entry => entry.FulfilledUtc == null, ct);

        var ids = rows.Select(entry => entry.PatientId).ToHashSet();

        var people = await patients.ListAsync(patient => ids.Contains(patient.Id), ct);

        var names = people.ToDictionary(patient => patient.Id, patient => patient.FullName);
        var mobiles = people.ToDictionary(patient => patient.Id, patient => patient.Mobile);

        var ordered = rows
            .OrderByDescending(entry => entry.Priority)

            // Never contacted first within a priority: somebody already rung and not
            // reached is a worse bet for a slot that expires in an hour.
            .ThenBy(entry => entry.LastContactedUtc ?? DateTime.MinValue)
            .ToList();

        return Results.Ok(ApiDefaults.Page(ordered, page, entry => new WaitlistEntryDto(
            entry.Id,
            entry.PatientId,
            names.GetValueOrDefault(entry.PatientId, "Unknown patient"),
            mobiles.GetValueOrDefault(entry.PatientId),
            entry.Priority.ToString(),
            entry.Reason,
            entry.Availability,
            entry.AvailableFrom,
            entry.AvailableUntil,
            entry.LastContactedUtc)));
    }

    /// <summary>Patient names for a set of appointments, in one read.</summary>
    /// <remarks>
    /// Looked up together rather than per row. A page of seventeen appointments would
    /// otherwise be seventeen round trips for a string — the shape of slow that only shows
    /// up once the database is across a network, which, unlike the device's file, this one
    /// is.
    /// </remarks>
    private static async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(
        IReadOnlyList<Appointment> appointments,
        IRepository<PatientEntity> patients,
        CancellationToken ct)
    {
        if (appointments.Count == 0) return new Dictionary<Guid, string>();

        var ids = appointments.Select(appointment => appointment.PatientId).ToHashSet();

        var rows = await patients.ListAsync(patient => ids.Contains(patient.Id), ct);

        return rows.ToDictionary(patient => patient.Id, patient => patient.FullName);
    }
}

/// <summary>A course of treatment and the visits that make it up.</summary>
public sealed record AppointmentSeriesDto(
    Guid Id,
    string Name,
    int PlannedVisits,
    IReadOnlyList<AppointmentDto> Visits);

public sealed record RecallDto(
    Guid Id,
    Guid PatientId,
    string PatientName,
    string RecallType,
    int IntervalMonths,
    DateOnly DueOn,
    bool IsOverdue,
    DateTime? LastContactedUtc,
    int ContactAttempts);

public sealed record WaitlistEntryDto(
    Guid Id,
    Guid PatientId,
    string PatientName,
    string? Mobile,
    string Priority,
    string? Reason,
    string? Availability,
    DateOnly? AvailableFrom,
    DateOnly? AvailableUntil,
    DateTime? LastContactedUtc);
