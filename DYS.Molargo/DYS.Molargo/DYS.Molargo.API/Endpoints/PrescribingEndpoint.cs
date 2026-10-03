using DYS.Molargo.Api.Contracts;
using DYS.Molargo.Domain.Data;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Api.Endpoints;

/// <summary>Prescriptions, referrals, certificates and the formulary.</summary>
/// <inheritdoc cref="PatientsEndpoint" path="/remarks"/>
public sealed class PrescribingEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/rx").WithTags("Rx & referrals");

        group.MapGet("/prescriptions", PrescriptionsAsync)
            .WithSummary("Issued prescriptions, newest first. Drafts are excluded.");

        group.MapGet("/prescriptions/{id:guid}", PrescriptionAsync)
            .WithSummary("One prescription with its medicines.");

        group.MapGet("/referrals", ReferralsAsync)
            .WithSummary("Referrals in or out.");

        group.MapGet("/referrals/{id:guid}", ReferralAsync)
            .WithSummary("One referral, including the letter.");

        group.MapGet("/certificates", CertificatesAsync)
            .WithSummary("Medical certificates, newest first.");

        group.MapGet("/formulary", FormularyAsync)
            .WithSummary("The practice formulary.");
    }

    /// <summary>GET /rx/prescriptions</summary>
    /// <remarks>
    /// Drafts excluded unless asked for. A draft is a script somebody started and has not
    /// signed — it is not a thing the patient has, and a list that mixed the two would let
    /// a caller report a medicine as prescribed when nobody prescribed it.
    /// </remarks>
    private static async Task<IResult> PrescriptionsAsync(
        Guid? patientId,
        bool? includeDrafts,
        int? page,
        IRepository<Prescription> prescriptions,
        IRepository<PrescriptionItem> items,
        CancellationToken ct)
    {
        var rows = await prescriptions.ListAsync(
            script => (patientId == null || script.PatientId == patientId)
                && (includeDrafts == true || script.Status != PrescriptionStatus.Draft), ct);

        var ordered = rows
            .OrderByDescending(script => script.IssuedUtc ?? script.CreatedUtc)
            .ToList();

        var page1 = ApiDefaults.Page(ordered, page, script => script);

        // Medicines for the page only, in one read. The list is scanned to answer "what has
        // this patient had before", and a count of items answers that for nobody.
        var ids = page1.Items.Select(script => script.Id).ToHashSet();
        var lines = await items.ListAsync(item => ids.Contains(item.PrescriptionId), ct);

        return Results.Ok(new PageDto<PrescriptionDto>(
            page1.Items
                .Select(script => PrescriptionDto.From(
                    script, lines.Where(line => line.PrescriptionId == script.Id).ToList()))
                .ToList(),
            page1.Total,
            page1.Page,
            page1.PageSize));
    }

    /// <summary>GET /rx/prescriptions/{id}</summary>
    private static async Task<IResult> PrescriptionAsync(
        Guid id,
        IRepository<Prescription> prescriptions,
        IRepository<PrescriptionItem> items,
        CancellationToken ct)
    {
        var script = await prescriptions.GetByIdAsync(id, ct);

        if (script is null) return Results.NotFound();

        var lines = await items.ListAsync(item => item.PrescriptionId == id, ct);

        return Results.Ok(PrescriptionDto.From(script, lines));
    }

    /// <summary>GET /rx/referrals</summary>
    private static async Task<IResult> ReferralsAsync(
        Guid? patientId,
        string? direction,
        int? page,
        IRepository<Referral> referrals,
        CancellationToken ct)
    {
        ReferralDirection? wanted = null;

        if (direction is { Length: > 0 })
        {
            if (!Enum.TryParse<ReferralDirection>(direction, ignoreCase: true, out var parsed))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["direction"] = [$"\"{direction}\" is not Inbound or Outbound."],
                });
            }

            wanted = parsed;
        }

        var rows = await referrals.ListAsync(
            referral => (patientId == null || referral.PatientId == patientId)
                && (wanted == null || referral.Direction == wanted), ct);

        var ordered = rows
            .OrderByDescending(referral => referral.SentUtc ?? referral.CreatedUtc)
            .ToList();

        // The letter body is left off the list. It is several paragraphs per row, and a
        // page of seventeen would be most of the response for something nobody reads until
        // they open one — see the detail route.
        return Results.Ok(ApiDefaults.Page(ordered, page, ReferralDto.From));
    }

    /// <summary>GET /rx/referrals/{id}</summary>
    private static async Task<IResult> ReferralAsync(
        Guid id, IRepository<Referral> referrals, CancellationToken ct)
    {
        var referral = await referrals.GetByIdAsync(id, ct);

        return referral is null
            ? Results.NotFound()
            : Results.Ok(new ReferralDetailDto(ReferralDto.From(referral), referral.LetterBody));
    }

    /// <summary>GET /rx/certificates</summary>
    private static async Task<IResult> CertificatesAsync(
        Guid? patientId,
        int? page,
        IRepository<MedicalCertificate> certificates,
        CancellationToken ct)
    {
        var rows = await certificates.ListAsync(
            certificate => patientId == null || certificate.PatientId == patientId, ct);

        var ordered = rows
            .OrderByDescending(certificate => certificate.AttendedOn)
            .ThenByDescending(certificate => certificate.CreatedUtc)
            .ToList();

        return Results.Ok(ApiDefaults.Page(ordered, page, certificate => new CertificateDto(
            certificate.Id,
            certificate.PatientId,
            certificate.ProviderId,
            certificate.AttendedOn,
            certificate.UnfitFrom,
            certificate.UnfitTo,
            certificate.IsForStudy,
            certificate.Body,
            certificate.IssuedUtc,

            // Issued and signed are different facts. An issued certificate nobody signed is
            // one an employer has no reason to accept, and the app flags it for exactly
            // that reason — a caller needs the same distinction.
            certificate.IssuedUtc is not null,
            certificate.SignedUtc is not null)));
    }

    /// <summary>GET /rx/formulary</summary>
    private static async Task<IResult> FormularyAsync(
        bool? includeRetired,
        IRepository<FormularyMedicine> formulary,
        CancellationToken ct)
    {
        var rows = await formulary.ListAsync(
            medicine => includeRetired == true || medicine.IsActive, ct);

        return Results.Ok(rows
            .OrderByDescending(medicine => medicine.IsActive)
            .ThenBy(medicine => medicine.DisplayOrder)
            .ThenBy(medicine => medicine.GenericName)
            .Select(medicine => new FormularyMedicineDto(
                medicine.Id,
                medicine.GenericName,
                medicine.BrandName,
                medicine.Strength,
                medicine.Form,
                medicine.Class.ToString(),
                medicine.DefaultDirections,
                medicine.DefaultQuantity,
                medicine.DefaultRepeats,
                medicine.AllergyClasses,

                // Surfaced because the consequence is invisible from anywhere else: a
                // medicine with no allergy families is prescribed with no allergy screen at
                // all, and the script says "checked" either way.
                string.IsNullOrWhiteSpace(medicine.AllergyClasses),
                medicine.IsActive))
            .ToList());
    }
}

public sealed record PrescriptionItemDto(
    Guid Id,
    string MedicineName,
    string? BrandName,
    string Strength,
    string? Form,
    string Directions,
    int Quantity,
    int Repeats,
    bool BrandSubstitutionNotPermitted);

public sealed record PrescriptionDto(
    Guid Id,
    Guid PatientId,
    Guid ProviderId,
    string Status,
    DateTime? IssuedUtc,
    DateOnly? ValidUntil,
    bool IsSigned,
    DateTime? AllergyCheckedUtc,
    IReadOnlyList<PrescriptionItemDto> Items)
{
    public static PrescriptionDto From(
        Prescription script, IReadOnlyList<PrescriptionItem> items) => new(
        script.Id,
        script.PatientId,
        script.ProviderId,
        script.Status.ToString(),
        script.IssuedUtc,
        script.ValidUntil,
        script.SignedUtc is not null,
        script.AllergyCheckedUtc,
        items
            .OrderBy(item => item.MedicineName)
            .Select(item => new PrescriptionItemDto(
                item.Id,
                item.MedicineName,
                item.BrandName,
                item.Strength,
                item.Form,
                item.Directions,
                item.Quantity,
                item.Repeats,
                item.BrandSubstitutionNotPermitted))
            .ToList());
}

public sealed record ReferralDto(
    Guid Id,
    Guid PatientId,
    string Direction,
    string Status,
    string Counterparty,
    string? Specialty,
    string Reason,
    bool IsUrgent,
    DateTime? SentUtc,
    DateTime? ReportReceivedUtc,
    bool IsAwaitingReport)
{
    public static ReferralDto From(Referral referral) => new(
        referral.Id,
        referral.PatientId,
        referral.Direction.ToString(),
        referral.Status.ToString(),
        referral.CounterpartyName,
        referral.CounterpartySpecialty,
        referral.Reason,
        referral.IsUrgent,
        referral.SentUtc,
        referral.ReportReceivedUtc,

        // The thing the outbound list exists to surface: a patient handed on with nothing
        // back is a loop the practice has left open.
        referral.Direction == ReferralDirection.Outbound
            && referral.SentUtc is not null
            && referral.ReportReceivedUtc is null);
}

public sealed record ReferralDetailDto(ReferralDto Referral, string? LetterBody);

public sealed record CertificateDto(
    Guid Id,
    Guid PatientId,
    Guid ProviderId,
    DateOnly AttendedOn,
    DateOnly UnfitFrom,
    DateOnly UnfitTo,
    bool IsForStudy,
    string? Body,
    DateTime? IssuedUtc,
    bool IsIssued,
    bool IsSigned);

public sealed record FormularyMedicineDto(
    Guid Id,
    string GenericName,
    string? BrandName,
    string Strength,
    string? Form,
    string Class,
    string DefaultDirections,
    int DefaultQuantity,
    int DefaultRepeats,
    string? AllergyClasses,
    bool HasNoAllergyScreen,
    bool IsActive);
