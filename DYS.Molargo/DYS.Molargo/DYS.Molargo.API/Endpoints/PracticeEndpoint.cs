using DYS.Molargo.Api.Contracts;
using DYS.Molargo.Api.Infrastructure;
using DYS.Molargo.Domain.Data;
using DYS.Molargo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DYS.Molargo.Api.Endpoints;

/// <summary>The practice itself: who it is, what it pays, and the manual.</summary>
/// <inheritdoc cref="PatientsEndpoint" path="/remarks"/>
public sealed class PracticeEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/practice").WithTags("Practice");

        group.MapGet("/", GetAsync)
            .WithSummary("The calling clinic — name, country, currency, trial and plan.");

        group.MapGet("/help", HelpAsync)
            .WithSummary("The knowledge base every practice reads.");
    }

    /// <summary>GET /practice</summary>
    /// <remarks>
    /// By the caller's own tenant id, not by one in the route. A practice endpoint that
    /// took an id would be a practice endpoint somebody could point at another clinic.
    /// </remarks>
    private static async Task<IResult> GetAsync(
        IApiCaller caller,
        IRepository<Tenant> tenants,
        IMolargoContextSource source,
        TimeProvider time,
        CancellationToken ct)
    {
        var practice = await tenants.GetByIdAsync(caller.TenantId, ct);

        if (practice is null) return Results.NotFound();

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

        // Straight to the context, past the tenant filter. A plan is the vendor's row —
        // it carries the platform tenant's id, not this clinic's — so reading it through
        // the repository returns nothing at all, and the practice's own plan name would
        // come back null for every clinic on the system.
        await using var db = await source.CreateContextAsync(ct);

        var plan = practice.PlanId is { } planId
            ? await db.Plans
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(row => row.Id == planId, ct)
            : null;

        return Results.Ok(new PracticeDto(
            practice.Id,
            practice.Name,
            practice.Slug,
            practice.Abn,
            practice.ContactEmail,
            practice.ContactPhone,
            practice.CountryCode,

            // What the practice charges patients, which is not what it pays Molargo. Two
            // currencies on one record, and conflating them silently redenominates a
            // subscription price into a clinic's own money.
            practice.CurrencyCode,
            plan?.Name,
            practice.SubscribedOn,
            practice.TrialEndsOn,
            practice.IsInTrial(today),
            practice.TrialDaysLeft(today),
            practice.SmsEnabled,
            practice.IsActive));
    }

    /// <summary>GET /practice/help</summary>
    /// <remarks>
    /// Published articles only. An unpublished one is a half-written draft the vendor has
    /// not put in front of customers, and the practice-facing side of the app does not show
    /// it either.
    /// </remarks>
    private static async Task<IResult> HelpAsync(
        string? category,
        IMolargoContextSource source,
        CancellationToken ct)
    {
        // Articles carry the vendor's tenant id, not the caller's — the same arrangement
        // plans and SMS gateways use — so the filter is stepped around here, or a clinic
        // finds an empty manual. Published only: an unpublished article is a half-written
        // draft the vendor has not put in front of customers.
        await using var db = await source.CreateContextAsync(ct);

        var rows = await db.HelpArticles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(article => article.IsPublished && !article.IsDeleted)
            .ToListAsync(ct);

        if (category is { Length: > 0 })
        {
            rows = rows
                .Where(article => string.Equals(
                    article.Category, category, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return Results.Ok(rows
            .OrderBy(article => article.DisplayOrder)
            .Select(article => new HelpArticleDto(
                article.Id,
                article.Slug,
                article.Category,
                article.Title,
                article.Summary,
                article.Body,
                article.Keywords))
            .ToList());
    }
}

public sealed record PracticeDto(
    Guid Id,
    string Name,
    string ClinicCode,
    string? Abn,
    string? ContactEmail,
    string? ContactPhone,
    string CountryCode,
    string CurrencyCode,
    string? PlanName,
    DateOnly? SubscribedOn,
    DateOnly? TrialEndsOn,
    bool IsInTrial,
    int? TrialDaysLeft,
    bool SmsEnabled,
    bool IsActive);

public sealed record HelpArticleDto(
    Guid Id,
    string Slug,
    string Category,
    string Title,
    string Summary,
    string Body,
    string Keywords);
