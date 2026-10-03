using DYS.Molargo.Api.Contracts;
using DYS.Molargo.Api.Infrastructure;
using DYS.Molargo.Domain.Data;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Api.Endpoints;

/// <summary>
/// Patients.
/// </summary>
/// <remarks>
/// One endpoint class per feature, and one method per route inside it. <see cref="Map"/> is
/// the route table and nothing else — every line of it is a URL, a verb and the name of the
/// method that answers — so the shape of the feature is readable without scrolling through
/// the handlers, and a handler can be changed without touching the routing.
///
/// The alternative, lambdas inline in Map, puts three screens of logic between the first
/// route and the last and makes the table unreadable by the third one.
/// </remarks>
public sealed class PatientsEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/patients").WithTags("Patients");

        group.MapGet("/", ListAsync)
            .WithSummary("Patients at this clinic, searched and paged.");

        group.MapGet("/{id:guid}", GetAsync)
            .WithSummary("One patient.");

        group.MapPost("/", CreateAsync)
            .WithSummary("Creates a patient.");
    }

    /// <summary>GET /patients</summary>
    private static async Task<IResult> ListAsync(
        string? search,
        int? page,
        IRepository<PatientEntity> patients,
        CancellationToken ct)
    {
        // No tenant filter written here, and none needed: the context the repository opens
        // carries the request's clinic, and every query is filtered on it at the database.
        // An endpoint that had to remember would eventually be an endpoint that forgot.
        var term = search?.Trim();

        var result = await patients.GetPageAsync(
            page ?? 0,
            ApiDefaults.PageSize,
            patient => patient.LastName,
            predicate: term is { Length: > 0 }
                ? patient => patient.LastName.Contains(term)
                    || patient.FirstName.Contains(term)
                    || (patient.PatientNumber != null && patient.PatientNumber.Contains(term))
                : null,
            ct: ct);

        return Results.Ok(new PageDto<PatientDto>(
            result.Items.Select(PatientDto.From).ToList(),
            result.TotalCount,
            result.Page,
            result.PageSize));
    }

    /// <summary>GET /patients/{id}</summary>
    private static async Task<IResult> GetAsync(
        Guid id, IRepository<PatientEntity> patients, CancellationToken ct)
    {
        var patient = await patients.GetByIdAsync(id, ct);

        // 404 for a patient in another clinic as much as for one that does not exist — the
        // repository simply does not see them. Answering "forbidden" would confirm the
        // record exists somewhere, which is the one thing a tenant boundary withholds.
        return patient is null ? Results.NotFound() : Results.Ok(PatientDto.From(patient));
    }

    /// <summary>POST /patients</summary>
    private static async Task<IResult> CreateAsync(
        CreatePatientRequest body,
        IRepository<PatientEntity> patients,
        IApiCaller caller,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.FirstName)
            || string.IsNullOrWhiteSpace(body.LastName))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["A patient needs a first and last name."],
            });
        }

        var patient = new PatientEntity
        {
            FirstName = body.FirstName.Trim(),
            LastName = body.LastName.Trim(),
            DateOfBirth = body.DateOfBirth,
            Mobile = body.Mobile?.Trim(),
            Email = body.Email?.Trim(),

            // The caller's own site unless they name one. A record with no site belongs to
            // no worklist and appears on no front desk.
            PracticeLocationId = body.PracticeLocationId
                ?? (caller.LocationId == Guid.Empty ? null : caller.LocationId),
        };

        // The repository stamps the tenant, the id and the audit dates. Setting TenantId
        // here would be a second place deciding which clinic a row belongs to, and the two
        // would disagree the first time one changed.
        var id = await patients.SaveAsync(patient, ct);

        return Results.Created($"/patients/{id}", PatientDto.From(patient));
    }
}

/// <summary>
/// What a caller may set when creating a patient.
/// </summary>
/// <remarks>
/// Its own shape, not <see cref="PatientDto"/>. A write shape that doubled as a read shape
/// would accept an id, a patient number and a full name — none of which a caller gets to
/// choose — and silently ignoring them is worse than not offering them.
///
/// Beside the endpoint rather than in Contracts, because exactly one route accepts it.
/// </remarks>
public sealed record CreatePatientRequest(
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    string? Mobile,
    string? Email,
    Guid? PracticeLocationId);
