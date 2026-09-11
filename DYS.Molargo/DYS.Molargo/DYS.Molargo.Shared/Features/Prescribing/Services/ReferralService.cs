using System.Globalization;
using System.Text;
using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Components;
using DYS.Molargo.Shared.Repositories;
using DYS.Molargo.Shared.Services;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Features.Prescribing.Services;

/// <summary>
/// A referral template — the specialty, and what is normally asked of them.
/// </summary>
/// <remarks>
/// Hard-coded rather than a table. The list is the specialties a general practice refers
/// to, it changes about never, and a settings screen to maintain five rows is machinery
/// nobody would use. It becomes data the day a practice wants its own.
/// </remarks>
public sealed record ReferralTemplate(
    string Key, string Specialty, string DefaultCounterparty, string Ask);

/// <summary>One row of the referral lists.</summary>
public sealed record ReferralRow(
    Guid ReferralId,
    Guid PatientId,
    string PatientName,
    string Counterparty,
    string? Specialty,
    string Reason,
    ReferralStatus Status,
    DateTime? SentUtc,
    DateTime? ReportReceivedUtc,
    bool IsUrgent)
{
    /// <summary>
    /// An outbound referral sent with nothing back. What the list exists to surface: the
    /// practice has handed a patient on and does not know what happened.
    /// </summary>
    public bool IsAwaitingReport =>
        SentUtc is not null && ReportReceivedUtc is null
        && Status is not (ReferralStatus.Declined or ReferralStatus.Cancelled);
}

/// <summary>
/// Referral letters in both directions, and medical certificates.
/// </summary>
public interface IReferralService
{
    IReadOnlyList<ReferralTemplate> Templates { get; }

    Task<IReadOnlyList<ReferralRow>> GetOutboundAsync(
        Guid? patientId = null, CancellationToken ct = default);

    Task<IReadOnlyList<ReferralRow>> GetInboundAsync(CancellationToken ct = default);

    Task<Referral?> GetAsync(Guid referralId, CancellationToken ct = default);

    /// <summary>
    /// Drafts an outbound referral with a letter written from the patient's record.
    /// </summary>
    Task<Referral> DraftAsync(
        Guid patientId, Guid providerId, string templateKey, CancellationToken ct = default);

    Task SaveLetterAsync(
        Guid referralId, string counterparty, string reason, string letterBody, bool isUrgent,
        CancellationToken ct = default);

    /// <summary>
    /// Marks the referral sent.
    /// </summary>
    /// <remarks>
    /// Records that it went; it does not transmit anything. Secure messaging to a
    /// specialist is online work and not built, so the letter is printed or emailed by
    /// hand and this is the note that it happened.
    /// </remarks>
    Task<string?> MarkSentAsync(Guid referralId, CancellationToken ct = default);

    /// <summary>Records the specialist's report coming back, which closes the loop.</summary>
    Task MarkReportReceivedAsync(Guid referralId, CancellationToken ct = default);

    /// <summary>Drafts a certificate of attendance for a patient, with its wording.</summary>
    Task<MedicalCertificate> DraftCertificateAsync(
        Guid patientId, Guid providerId, int days, bool forStudy,
        CancellationToken ct = default);

    Task<string?> IssueCertificateAsync(Guid certificateId, CancellationToken ct = default);

    Task<IReadOnlyList<MedicalCertificate>> GetCertificatesAsync(
        Guid patientId, CancellationToken ct = default);
}

/// <inheritdoc cref="IReferralService"/>
public sealed class ReferralService : IReferralService
{
    private readonly IRepository<Referral> _referrals;
    private readonly IRepository<MedicalCertificate> _certificates;
    private readonly IRepository<PatientEntity> _patients;
    private readonly IRepository<PatientAlert> _alerts;
    private readonly IRepository<ToothChartEntry> _chart;
    private readonly IRepository<Provider> _providers;
    private readonly IRepository<PracticeLocation> _locations;
    private readonly IClock _clock;

    public ReferralService(
        IRepository<Referral> referrals,
        IRepository<MedicalCertificate> certificates,
        IRepository<PatientEntity> patients,
        IRepository<PatientAlert> alerts,
        IRepository<ToothChartEntry> chart,
        IRepository<Provider> providers,
        IRepository<PracticeLocation> locations,
        IClock clock)
    {
        _referrals = referrals;
        _certificates = certificates;
        _patients = patients;
        _alerts = alerts;
        _chart = chart;
        _providers = providers;
        _locations = locations;
        _clock = clock;
    }

    public IReadOnlyList<ReferralTemplate> Templates { get; } =
    [
        new("perio", "Periodontics", "Dr Osman",
            "periodontal assessment and management"),
        new("oral-surgery", "Oral surgery", "Dr Whitcombe",
            "surgical removal and review"),
        new("endo", "Endodontics", "Dr Bhatia",
            "endodontic assessment and treatment"),
        new("ortho", "Orthodontics", "Dr Lindqvist",
            "orthodontic assessment"),
        new("omfs", "Oral medicine / pathology", "Oral Medicine Unit",
            "urgent assessment of a soft-tissue lesion"),
    ];

    public async Task<IReadOnlyList<ReferralRow>> GetOutboundAsync(
        Guid? patientId = null, CancellationToken ct = default) =>
        await ListAsync(ReferralDirection.Outbound, patientId, ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<ReferralRow>> GetInboundAsync(CancellationToken ct = default) =>
        await ListAsync(ReferralDirection.Inbound, null, ct).ConfigureAwait(false);

    public async Task<Referral?> GetAsync(Guid referralId, CancellationToken ct = default) =>
        await _referrals.GetByIdAsync(referralId, ct).ConfigureAwait(false);

    public async Task<Referral> DraftAsync(
        Guid patientId, Guid providerId, string templateKey, CancellationToken ct = default)
    {
        var template = Templates.FirstOrDefault(entry => entry.Key == templateKey)
            ?? Templates[0];

        var referral = new Referral
        {
            PatientId = patientId,
            ProviderId = providerId,
            Direction = ReferralDirection.Outbound,
            Status = ReferralStatus.Draft,
            CounterpartyName = template.DefaultCounterparty,
            CounterpartySpecialty = template.Specialty,
            Reason = $"For {template.Ask}.",
            IsUrgent = template.Key == "omfs",
        };

        referral.LetterBody = await ComposeLetterAsync(referral, template, ct)
            .ConfigureAwait(false);

        await _referrals.SaveAsync(referral, ct).ConfigureAwait(false);
        return referral;
    }

    public async Task SaveLetterAsync(
        Guid referralId, string counterparty, string reason, string letterBody, bool isUrgent,
        CancellationToken ct = default)
    {
        var referral = await _referrals.GetByIdAsync(referralId, ct).ConfigureAwait(false);
        if (referral is null) return;

        // A sent letter is not edited. It is a statement already in someone else's hands,
        // and quietly rewriting the copy on file would leave the record disagreeing with
        // what the specialist actually received.
        if (referral.SentUtc is not null) return;

        if (!string.IsNullOrWhiteSpace(counterparty)) referral.CounterpartyName = counterparty.Trim();
        if (!string.IsNullOrWhiteSpace(reason)) referral.Reason = reason.Trim();

        referral.LetterBody = letterBody;
        referral.IsUrgent = isUrgent;

        await _referrals.SaveAsync(referral, ct).ConfigureAwait(false);
    }

    public async Task<string?> MarkSentAsync(Guid referralId, CancellationToken ct = default)
    {
        var referral = await _referrals.GetByIdAsync(referralId, ct).ConfigureAwait(false);
        if (referral is null) return "That referral no longer exists.";

        if (referral.SentUtc is not null) return "That referral has already been sent.";

        if (string.IsNullOrWhiteSpace(referral.LetterBody))
        {
            return "Write the letter before marking it sent.";
        }

        referral.SentUtc = _clock.UtcNow;
        referral.Status = ReferralStatus.Sent;

        await _referrals.SaveAsync(referral, ct).ConfigureAwait(false);
        return null;
    }

    public async Task MarkReportReceivedAsync(Guid referralId, CancellationToken ct = default)
    {
        var referral = await _referrals.GetByIdAsync(referralId, ct).ConfigureAwait(false);
        if (referral is null || referral.ReportReceivedUtc is not null) return;

        referral.ReportReceivedUtc = _clock.UtcNow;
        referral.Status = ReferralStatus.ReportReceived;

        await _referrals.SaveAsync(referral, ct).ConfigureAwait(false);
    }

    public async Task<MedicalCertificate> DraftCertificateAsync(
        Guid patientId, Guid providerId, int days, bool forStudy, CancellationToken ct = default)
    {
        var today = _clock.Today;

        // At least today, and capped. A dental certificate covering a fortnight is not a
        // dental judgement, and the cap makes that a rule rather than a habit.
        var covered = Math.Clamp(days, 1, 5);

        var certificate = new MedicalCertificate
        {
            PatientId = patientId,
            ProviderId = providerId,
            AttendedOn = today,
            UnfitFrom = today,
            UnfitTo = today.AddDays(covered - 1),
            IsForStudy = forStudy,
        };

        certificate.Body = await ComposeCertificateAsync(certificate, ct).ConfigureAwait(false);

        await _certificates.SaveAsync(certificate, ct).ConfigureAwait(false);
        return certificate;
    }

    public async Task<string?> IssueCertificateAsync(
        Guid certificateId, CancellationToken ct = default)
    {
        var certificate = await _certificates.GetByIdAsync(certificateId, ct).ConfigureAwait(false);
        if (certificate is null) return "That certificate no longer exists.";

        if (certificate.IsIssued) return "That certificate has already been issued.";

        var provider = await _providers.GetByIdAsync(certificate.ProviderId, ct)
            .ConfigureAwait(false);

        // Refused without a registration number. A certificate signed by someone with no
        // AHPRA number on file is not a document an employer can rely on, and issuing one
        // puts the practice's name to a claim it cannot support.
        if (provider is null || string.IsNullOrWhiteSpace(provider.AhpraNumber))
        {
            return "The signing clinician has no AHPRA registration number on file. "
                + "A certificate cannot be issued without one.";
        }

        certificate.IssuedUtc = _clock.UtcNow;

        await _certificates.SaveAsync(certificate, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<IReadOnlyList<MedicalCertificate>> GetCertificatesAsync(
        Guid patientId, CancellationToken ct = default)
    {
        var certificates = await _certificates
            .ListAsync(certificate => certificate.PatientId == patientId, ct)
            .ConfigureAwait(false);

        return certificates
            .OrderByDescending(certificate => certificate.AttendedOn)
            .ToList();
    }

    // ---- helpers ---------------------------------------------------------

    private async Task<IReadOnlyList<ReferralRow>> ListAsync(
        ReferralDirection direction, Guid? patientId, CancellationToken ct)
    {
        var referrals = await _referrals
            .ListAsync(referral => referral.Direction == direction, ct)
            .ConfigureAwait(false);

        if (patientId is { } wanted)
        {
            referrals = referrals.Where(referral => referral.PatientId == wanted).ToList();
        }

        if (referrals.Count == 0) return [];

        var patientIds = referrals.Select(referral => referral.PatientId).ToHashSet();

        var patients = await _patients
            .ListAsync(patient => patientIds.Contains(patient.Id), ct)
            .ConfigureAwait(false);

        var names = patients.ToDictionary(patient => patient.Id, patient => patient.FullName);

        return referrals
            .OrderByDescending(referral => referral.SentUtc ?? referral.CreatedUtc)
            .Select(referral => new ReferralRow(
                referral.Id,
                referral.PatientId,
                names.GetValueOrDefault(referral.PatientId, "Unknown patient"),
                referral.CounterpartyName,
                referral.CounterpartySpecialty,
                referral.Reason,
                referral.Status,
                referral.SentUtc,
                referral.ReportReceivedUtc,
                referral.IsUrgent))
            .ToList();
    }

    /// <summary>
    /// Writes the referral letter from the patient's actual record.
    /// </summary>
    /// <remarks>
    /// Assembled from the record rather than left as a blank box or a lorem-ipsum
    /// paragraph. The design shows a letter naming the patient's pocketing, their
    /// diabetes, their warfarin and their allergies — and every one of those is already on
    /// file, so the clinician's job here should be editing a draft rather than retyping
    /// the medical history they have just read on the previous screen. Editable
    /// afterwards, because a generated letter is a starting point and no more.
    /// </remarks>
    private async Task<string> ComposeLetterAsync(
        Referral referral, ReferralTemplate template, CancellationToken ct)
    {
        var patient = await _patients.GetByIdAsync(referral.PatientId, ct).ConfigureAwait(false);
        var provider = await _providers.GetByIdAsync(referral.ProviderId, ct).ConfigureAwait(false);

        var locations = await _locations.ListAsync(ct: ct).ConfigureAwait(false);
        var practice = locations.OrderBy(location => location.DisplayOrder).FirstOrDefault();

        var alerts = await _alerts
            .ListAsync(alert => alert.PatientId == referral.PatientId
                && alert.ResolvedDate == null, ct)
            .ConfigureAwait(false);

        var findings = await _chart
            .ListAsync(entry => entry.PatientId == referral.PatientId
                && entry.SupersededOn == null, ct)
            .ConfigureAwait(false);

        var name = patient?.FullName ?? "the patient";
        var dob = patient?.DateOfBirth is { } birth ? MolargoFormat.Date(birth) : "unknown";

        var letter = new StringBuilder();

        letter.AppendLine($"Dear {referral.CounterpartyName},");
        letter.AppendLine();
        letter.AppendLine($"Re: {name}, DOB {dob}");
        letter.AppendLine();
        letter.AppendLine(
            $"Thank you for seeing {patient?.FirstName ?? "this patient"} for {template.Ask}.");
        letter.AppendLine();

        // The current chart, summarised. Only what is actually charted — an invented
        // clinical picture in a referral letter is worse than an empty one.
        var notable = findings
            .Where(entry => entry.Condition is not (ToothCondition.Sound or ToothCondition.Missing))
            .OrderBy(entry => entry.ToothNumber)
            .Take(6)
            .Select(entry => $"{entry.ToothNumber} {ChartWording(entry)}")
            .ToList();

        letter.AppendLine(notable.Count > 0
            ? $"Current charted findings: {Sentence(string.Join("; ", notable))}"
            : "No current charted findings of note.");

        letter.AppendLine();

        var medical = alerts
            .Where(alert => alert.Kind is AlertKind.Allergy or AlertKind.MedicalCondition
                or AlertKind.Medication or AlertKind.Pregnancy)
            .Select(alert => alert.Summary)
            .ToList();

        letter.AppendLine(medical.Count > 0
            ? $"Medical history: {Sentence(string.Join("; ", medical))}"
            : "Medical history: nothing recorded.");

        letter.AppendLine();
        letter.AppendLine("Kind regards,");
        letter.AppendLine(provider?.DisplayName ?? provider?.FullName ?? "the practice");

        if (practice is not null) letter.AppendLine(practice.Name);

        return letter.ToString().TrimEnd();
    }

    /// <summary>
    /// Ends a sentence with exactly one full stop.
    /// </summary>
    /// <remarks>
    /// Clinicians write chart details as whole sentences — "Composite placed this
    /// course." — so appending a stop unconditionally produced "stable since Feb.." in a
    /// letter going to another practitioner.
    /// </remarks>
    private static string Sentence(string text)
    {
        var trimmed = text.TrimEnd();

        return trimmed.EndsWith('.') ? trimmed : trimmed + ".";
    }

    private static string ChartWording(ToothChartEntry entry)
    {
        var condition = entry.Condition.ToString().ToLowerInvariant();

        var surfaces = entry.Surfaces == ToothSurface.None
            ? string.Empty
            : $" ({ToothSurfaces.Code(entry.Surfaces)})";

        var detail = string.IsNullOrWhiteSpace(entry.Detail)
            ? string.Empty
            : $" — {entry.Detail}";

        return $"{condition}{surfaces}{detail}";
    }


    private async Task<string> ComposeCertificateAsync(
        MedicalCertificate certificate, CancellationToken ct)
    {
        var patient = await _patients.GetByIdAsync(certificate.PatientId, ct).ConfigureAwait(false);
        var provider = await _providers.GetByIdAsync(certificate.ProviderId, ct)
            .ConfigureAwait(false);

        var name = patient?.FullName ?? "The patient";

        var attended = certificate.AttendedOn.ToString("dddd d MMMM yyyy",
            CultureInfo.InvariantCulture);

        var purpose = certificate.IsForStudy ? "study" : "work";

        var period = certificate.Days == 1
            ? $"on {MolargoFormat.Date(certificate.UnfitFrom)}"
            : $"from {MolargoFormat.Date(certificate.UnfitFrom)} "
                + $"to {MolargoFormat.Date(certificate.UnfitTo)} inclusive";

        var signature = provider is null
            ? "the treating clinician"
            : $"{provider.DisplayName ?? provider.FullName}"
                + (string.IsNullOrWhiteSpace(provider.AhpraNumber)
                    ? string.Empty
                    : $", AHPRA {provider.AhpraNumber}");

        // No diagnosis. A certificate states attendance and unfitness; naming the
        // treatment discloses clinical detail to an employer who has no right to it.
        return $"{name} attended this practice for dental treatment on {attended} "
            + $"and is unfit for {purpose} {period}."
            + Environment.NewLine + Environment.NewLine
            + $"Signed: {signature}";
    }
}
