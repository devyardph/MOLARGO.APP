using DYS.Molargo.Api.Contracts;
using DYS.Molargo.Api.Infrastructure;
using DYS.Molargo.Data;
using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Api.Endpoints;

/// <summary>Staff, sites, chairs, appointment types and the audit log.</summary>
/// <inheritdoc cref="PatientsEndpoint" path="/remarks"/>
public sealed class AdminEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin").WithTags("Admin");

        group.MapGet("/staff", StaffAsync)
            .WithSummary("Staff at this practice. Never returns a password hash.");

        group.MapGet("/sites", SitesAsync)
            .WithSummary("Practice locations and their trading hours.");

        group.MapGet("/chairs", ChairsAsync)
            .WithSummary("Chairs, optionally for one site.");

        group.MapGet("/appointment-types", AppointmentTypesAsync)
            .WithSummary("The kinds of visit this practice books.");

        group.MapGet("/audit", AuditAsync)
            .WithSummary("The audit log, newest first. Requires the staff permission.");
    }

    /// <summary>GET /admin/staff</summary>
    /// <remarks>
    /// The DTO has no password hash, no failed-attempt count and no lockout time. A read
    /// model that carried them would put a practice's credential state one forgotten filter
    /// away from a caller — and nothing about this endpoint needs them.
    /// </remarks>
    private static async Task<IResult> StaffAsync(
        bool? includeInactive, IRepository<Provider> providers, CancellationToken ct)
    {
        var rows = await providers.ListAsync(
            provider => includeInactive == true || provider.IsActive, ct);

        return Results.Ok(rows
            .OrderBy(provider => provider.LastName)
            .ThenBy(provider => provider.FirstName)
            .Select(provider => new StaffDto(
                provider.Id,
                provider.FirstName,
                provider.LastName,
                provider.FullName,
                provider.Role.ToString(),
                provider.Permissions.ToString(),
                provider.IsOwner,
                provider.Email,
                provider.Mobile,
                provider.LicenceNumber,
                provider.PrimaryLocationId,
                provider.IsActive,

                // The two facts the role actually governs, given rather than left to the
                // caller to derive from the role name. One answer to "may they" across the
                // device and the server is the only way the two cannot drift.
                ProviderRoles.IsClinical(provider.Role),
                ProviderRoles.IsBookable(provider.Role)))
            .ToList());
    }

    /// <summary>GET /admin/sites</summary>
    private static async Task<IResult> SitesAsync(
        IRepository<PracticeLocation> locations, CancellationToken ct)
    {
        var rows = await locations.ListAsync(null, ct);

        return Results.Ok(rows
            .OrderBy(site => site.DisplayOrder)
            .Select(site =>
            {
                // Effective, not raw. A site with no days set or a close before its open is
                // read as "not configured" everywhere in the app, and an API that returned
                // the raw values would have every client reimplement that fallback.
                var hours = site.Hours.Effective;

                return new SiteDto(
                    site.Id,
                    site.Name,
                    site.ShortName,
                    site.AddressLine,
                    site.Suburb,
                    site.State,
                    site.Postcode,
                    site.Phone,
                    site.Email,
                    site.Abn,
                    site.TimeZoneId,
                    hours.Days.ToString(),
                    hours.Open,
                    hours.Close,
                    site.IsActive);
            })
            .ToList());
    }

    /// <summary>GET /admin/chairs</summary>
    private static async Task<IResult> ChairsAsync(
        Guid? siteId, IRepository<Operatory> chairs, CancellationToken ct)
    {
        var rows = await chairs.ListAsync(
            chair => siteId == null || chair.PracticeLocationId == siteId, ct);

        return Results.Ok(rows
            .OrderBy(chair => chair.DisplayOrder)
            .Select(chair => new ChairDto(
                chair.Id, chair.PracticeLocationId, chair.Name, chair.IsActive))
            .ToList());
    }

    /// <summary>GET /admin/appointment-types</summary>
    private static async Task<IResult> AppointmentTypesAsync(
        bool? includeRetired, IRepository<AppointmentType> types, CancellationToken ct)
    {
        var rows = await types.ListAsync(type => includeRetired == true || type.IsActive, ct);

        return Results.Ok(rows
            .OrderByDescending(type => type.IsActive)
            .ThenBy(type => type.Name)
            .Select(type => new AppointmentTypeDto(
                type.Id,
                type.Name,
                type.DefaultDurationMinutes,
                type.Colour,
                type.RecallIntervalMonths,
                type.IsActive))
            .ToList());
    }

    /// <summary>GET /admin/audit</summary>
    /// <remarks>
    /// Guarded, unlike every other read here. The audit log says who opened which patient's
    /// record and when — it is the most sensitive list in the practice, and the app puts it
    /// behind the staff permission for that reason.
    /// </remarks>
    private static async Task<IResult> AuditAsync(
        string? search,
        Guid? patientId,
        int? page,
        IApiCaller caller,
        IRepository<AuditEntry> audit,
        CancellationToken ct)
    {
        if (!caller.Can(PracticePermissions.ManageStaff))
        {
            return Results.Problem(
                "Reading the audit log needs the staff permission.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var rows = await audit.ListAsync(
            entry => patientId == null || entry.PatientId == patientId, ct);

        var term = search?.Trim();

        if (term is { Length: > 0 })
        {
            rows = rows
                .Where(entry =>
                    (entry.Detail ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (entry.ProviderName ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)
                    || entry.EntityName.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var ordered = rows.OrderByDescending(entry => entry.OccurredUtc).ToList();

        return Results.Ok(ApiDefaults.Page(ordered, page, entry => new AuditEntryDto(
            entry.Id,
            entry.Action.ToString(),
            entry.EntityName,
            entry.EntityId,
            entry.PatientId,
            entry.ProviderId,
            entry.ProviderName,
            entry.OccurredUtc,
            entry.DeviceId,
            entry.Detail)));
    }
}

public sealed record StaffDto(
    Guid Id,
    string FirstName,
    string LastName,
    string FullName,
    string Role,
    string Permissions,
    bool IsOwner,
    string? Email,
    string? Mobile,
    string? LicenceNumber,
    Guid? PrimaryLocationId,
    bool IsActive,
    bool IsClinical,
    bool IsBookable);

public sealed record SiteDto(
    Guid Id,
    string Name,
    string? ShortName,
    string? AddressLine,
    string? Suburb,
    string? State,
    string? Postcode,
    string? Phone,
    string? Email,
    string? Abn,
    string TimeZoneId,
    string OpeningDays,
    TimeOnly OpensAt,
    TimeOnly ClosesAt,
    bool IsActive);

public sealed record ChairDto(Guid Id, Guid PracticeLocationId, string Name, bool IsActive);

public sealed record AppointmentTypeDto(
    Guid Id,
    string Name,
    int DefaultDurationMinutes,
    string? Colour,
    int? RecallIntervalMonths,
    bool IsActive);

public sealed record AuditEntryDto(
    Guid Id,
    string Action,
    string EntityName,
    Guid? EntityId,
    Guid? PatientId,
    Guid? ProviderId,
    string? ProviderName,
    DateTime OccurredUtc,
    string? DeviceId,
    string? Detail);
