using DYS.Molargo.Api.Contracts;
using DYS.Molargo.Domain.Data;
using DYS.Molargo.Domain.Entities;

namespace DYS.Molargo.Api.Endpoints;

/// <summary>Treatment plans and the visits they are staged into.</summary>
/// <inheritdoc cref="PatientsEndpoint" path="/remarks"/>
public sealed class TreatmentPlansEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/treatment-plans").WithTags("Treatment plans");

        group.MapGet("/", ListAsync)
            .WithSummary("Treatment plans. Filter by patient.");

        group.MapGet("/{id:guid}", GetAsync)
            .WithSummary("One plan with its items, grouped into the visits they are staged as.");
    }

    /// <summary>GET /treatment-plans</summary>
    private static async Task<IResult> ListAsync(
        Guid? patientId, int? page, IRepository<TreatmentPlan> plans, CancellationToken ct)
    {
        var rows = await plans.ListAsync(
            plan => patientId == null || plan.PatientId == patientId, ct);

        var ordered = rows
            .OrderByDescending(plan => plan.PresentedUtc ?? plan.CreatedUtc)
            .ToList();

        return Results.Ok(ApiDefaults.Page(ordered, page, TreatmentPlanDto.From));
    }

    /// <summary>GET /treatment-plans/{id}</summary>
    /// <remarks>
    /// Items grouped by stage, because a stage is a visit. A flat list of items is the
    /// quote; the stages are what the patient is actually being asked to attend, and a
    /// client rebuilding that grouping from <c>StageNumber</c> would be reimplementing the
    /// one thing the plan is for.
    /// </remarks>
    private static async Task<IResult> GetAsync(
        Guid id,
        IRepository<TreatmentPlan> plans,
        IRepository<TreatmentPlanItem> items,
        CancellationToken ct)
    {
        var plan = await plans.GetByIdAsync(id, ct);

        if (plan is null) return Results.NotFound();

        var rows = await items.ListAsync(item => item.TreatmentPlanId == id, ct);

        var visits = rows
            .GroupBy(item => item.StageNumber)
            .OrderBy(stage => stage.Key)
            .Select(stage => new TreatmentPlanVisitDto(
                stage.Key,
                stage.Sum(item => item.Fee),
                stage.Sum(item => item.EstimatedBenefit),

                // One appointment per stage where the visit is booked. Taken from the items
                // rather than stored on the stage, because a stage is not a row — it is a
                // number the items carry.
                stage.Select(item => item.AppointmentId).FirstOrDefault(id => id is not null),
                stage
                    .OrderBy(item => item.DisplayOrder)
                    .Select(item => new TreatmentPlanItemDto(
                        item.Id,
                        item.ItemNumber,
                        item.Description,
                        item.ToothNumber,
                        item.Surfaces.ToString(),
                        item.Quantity,
                        item.Fee,
                        item.EstimatedBenefit,
                        item.Status.ToString(),
                        item.AppointmentId,
                        item.CompletedUtc))
                    .ToList()))
            .ToList();

        return Results.Ok(new TreatmentPlanDetailDto(TreatmentPlanDto.From(plan), visits));
    }
}

public sealed record TreatmentPlanDto(
    Guid Id,
    Guid PatientId,
    Guid ProviderId,
    string Title,
    string Status,
    string? Rationale,
    DateTime? PresentedUtc,
    DateTime? DecidedUtc,
    DateOnly? EstimateValidUntil,
    decimal QuotedTotal,
    decimal EstimatedBenefit,
    decimal EstimatedGap,
    bool IsRecommended)
{
    public static TreatmentPlanDto From(TreatmentPlan plan) => new(
        plan.Id,
        plan.PatientId,
        plan.ProviderId,
        plan.Title,
        plan.Status.ToString(),
        plan.Rationale,
        plan.PresentedUtc,
        plan.DecidedUtc,
        plan.EstimateValidUntil,
        plan.QuotedTotal,
        plan.EstimatedBenefit,

        // The number the patient actually asks about. Given rather than left to the caller,
        // for the same reason the invoice gives its outstanding amount.
        plan.QuotedTotal - plan.EstimatedBenefit,
        plan.IsRecommended);
}

public sealed record TreatmentPlanItemDto(
    Guid Id,
    string ItemNumber,
    string Description,
    string? ToothNumber,
    string Surfaces,
    int Quantity,
    decimal Fee,
    decimal EstimatedBenefit,
    string Status,
    Guid? AppointmentId,
    DateTime? CompletedUtc);

/// <summary>One stage of a plan — the work intended for a single visit.</summary>
public sealed record TreatmentPlanVisitDto(
    int StageNumber,
    decimal Fee,
    decimal EstimatedBenefit,
    Guid? AppointmentId,
    IReadOnlyList<TreatmentPlanItemDto> Items);

public sealed record TreatmentPlanDetailDto(
    TreatmentPlanDto Plan, IReadOnlyList<TreatmentPlanVisitDto> Visits);
