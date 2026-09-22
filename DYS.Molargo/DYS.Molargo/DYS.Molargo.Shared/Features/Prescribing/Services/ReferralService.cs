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
    /// <param name="unfitFrom">
    /// The first day covered, or null for the day of attendance. Worth being able to set: a
    /// patient seen at five on Friday is often unfit from Monday, and a certificate that
    /// insisted on starting today would be wrong about the one fact an employer reads.
    /// </param>
    Task<MedicalCertificate> DraftCertificateAsync(
        Guid patientId, Guid providerId, int days, bool forStudy,
        DateOnly? unfitFrom = null, CancellationToken ct = default);

    /// <summary>
    /// Replaces the wording on a draft. Null on success, or the refusal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Editable because the generated sentence cannot cover every case — "unfit for duties
    /// involving heavy lifting", "may return to light duties" — and a clinician who cannot
    /// say what they mean writes the certificate on paper instead, which is the outcome
    /// this screen exists to avoid.
    /// </para>
    /// <para>
    /// Drafts only. An issued certificate is a document somebody has been given, and
    /// rewriting it afterwards would change what the practice said after it said it.
    /// </para>
    /// </remarks>
    Task<string?> SaveCertificateBodyAsync(
        Guid certificateId, string? body, CancellationToken ct = default);

    /// <summary>Records or clears the signing clinician's drawn signature.</summary>
    Task<string?> SignCertificateAsync(
        Guid certificateId, string? signature, CancellationToken ct = default);

    Task<MedicalCertificate?> GetCertificateAsync(
        Guid certificateId, CancellationToken ct = default);

    Task<string?> IssueCertificateAsync(Guid certificateId, CancellationToken ct = default);

    /// <summary>
    /// A patient's certificates, newest first, searched and paged.
    /// </summary>
    /// <remarks>
    /// Paged because it grows without bound: a patient of fifteen years has as many of
    /// these as they have had courses of treatment, and the ones worth finding are rarely
    /// the most recent.
    /// </remarks>
    Task<CertificatePage> GetCertificatesAsync(
        Guid patientId,
        string? search = null,
        int page = 0,
        int pageSize = 17,
        CancellationToken ct = default);

    /// <summary>
    /// Removes a certificate. Null on success, or the refusal.
    /// </summary>
    /// <remarks>
    /// Soft, like everything else here, and audited with what it said. An issued
    /// certificate is a document a patient was given and possibly handed to an employer, so
    /// the row survives with its wording and the audit entry names who removed it — a
    /// certificate that could vanish without trace is one the practice cannot answer for.
    /// </remarks>
    Task<string?> DeleteCertificateAsync(
        Guid certificateId, CancellationToken ct = default);
}

/// <summary>One page of a patient's certificates, with the total behind it.</summary>
/// <param name="Total">
/// Every match, not just this page. The pager needs it to say "1–17 of 40", and a screen
/// that only knew its own page could not tell somebody whether their search found one
/// certificate or forty.
/// </param>
public sealed record CertificatePage(
    IReadOnlyList<MedicalCertificate> Rows,
    int Total,
    int Page,
    int PageSize);

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
    private readonly IAuditLog _audit;

    public ReferralService(
        IRepository<Referral> referrals,
        IRepository<MedicalCertificate> certificates,
        IRepository<PatientEntity> patients,
        IRepository<PatientAlert> alerts,
        IRepository<ToothChartEntry> chart,
        IRepository<Provider> providers,
        IRepository<PracticeLocation> locations,
        IClock clock,
        IAuditLog audit)
    {
        _referrals = referrals;
        _certificates = certificates;
        _patients = patients;
        _alerts = alerts;
        _chart = chart;
        _providers = providers;
        _locations = locations;
        _clock = clock;
        _audit = audit;
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

        // Exported: a referral letter carries the clinical history to somebody outside the
        // practice, which is a disclosure, not an internal change.
        await _audit
            .RecordAsync(
                AuditAction.Exported,
                nameof(Referral),
                referralId,
                $"Referral sent to {referral.CounterpartyName}",
                referral.PatientId,
                ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task MarkReportReceivedAsync(Guid referralId, CancellationToken ct = default)
    {
        var referral = await _referrals.GetByIdAsync(referralId, ct).ConfigureAwait(false);
        if (referral is null || referral.ReportReceivedUtc is not null) return;

        referral.ReportReceivedUtc = _clock.UtcNow;
        referral.Status = ReferralStatus.ReportReceived;

        await _referrals.SaveAsync(referral, ct).ConfigureAwait(false);

        await _audit
            .RecordAsync(
                AuditAction.Updated,
                nameof(Referral),
                referralId,
                $"Report received back from {referral.CounterpartyName}",
                referral.PatientId,
                ct)
            .ConfigureAwait(false);
    }

    public async Task<MedicalCertificate> DraftCertificateAsync(
        Guid patientId, Guid providerId, int days, bool forStudy,
        DateOnly? unfitFrom = null, CancellationToken ct = default)
    {
        var today = _clock.Today;

        // At least today, and capped. A dental certificate covering a fortnight is not a
        // dental judgement, and the cap makes that a rule rather than a habit.
        var covered = Math.Clamp(days, 1, 5);

        // The day of attendance unless somebody said otherwise, and never before it: a
        // certificate cannot cover days that had already passed when the patient was seen.
        var from = unfitFrom is { } chosen && chosen >= today ? chosen : today;

        var certificate = new MedicalCertificate
        {
            PatientId = patientId,
            ProviderId = providerId,
            AttendedOn = today,
            UnfitFrom = from,
            UnfitTo = from.AddDays(covered - 1),
            IsForStudy = forStudy,
        };

        certificate.Body = await ComposeCertificateAsync(certificate, ct).ConfigureAwait(false);

        await _certificates.SaveAsync(certificate, ct).ConfigureAwait(false);
        return certificate;
    }

    public Task<MedicalCertificate?> GetCertificateAsync(
        Guid certificateId, CancellationToken ct = default) =>
        _certificates.GetByIdAsync(certificateId, ct);

    public async Task<string?> SaveCertificateBodyAsync(
        Guid certificateId, string? body, CancellationToken ct = default)
    {
        var certificate = await _certificates.GetByIdAsync(certificateId, ct)
            .ConfigureAwait(false);

        if (certificate is null) return "That certificate no longer exists.";

        if (certificate.IsIssued)
        {
            return "That certificate has been issued. Draft a new one rather than "
                + "rewriting what the patient was already given.";
        }

        var wording = (body ?? string.Empty).Trim();

        // Refused rather than regenerated. A blank certificate that silently filled itself
        // back in would overwrite wording somebody had just deleted on purpose.
        if (wording.Length == 0)
        {
            return "A certificate needs wording. Draft a new one to start from the "
                + "standard text again.";
        }

        certificate.Body = wording;

        await _certificates.SaveAsync(certificate, ct).ConfigureAwait(false);

        return null;
    }

    public async Task<string?> SignCertificateAsync(
        Guid certificateId, string? signature, CancellationToken ct = default)
    {
        var certificate = await _certificates.GetByIdAsync(certificateId, ct)
            .ConfigureAwait(false);

        if (certificate is null) return "That certificate no longer exists.";

        var drawn = string.IsNullOrWhiteSpace(signature) ? null : signature;

        certificate.Signature = drawn;
        certificate.SignedUtc = drawn is null ? null : _clock.UtcNow;

        await _certificates.SaveAsync(certificate, ct).ConfigureAwait(false);

        await _audit
            .RecordAsync(
                AuditAction.Updated,
                nameof(MedicalCertificate),
                certificateId,
                drawn is null ? "Certificate signature cleared" : "Certificate signed",
                certificate.PatientId,
                ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string?> IssueCertificateAsync(
        Guid certificateId, CancellationToken ct = default)
    {
        var certificate = await _certificates.GetByIdAsync(certificateId, ct).ConfigureAwait(false);
        if (certificate is null) return "That certificate no longer exists.";

        if (certificate.IsIssued) return "That certificate has already been issued.";

        var provider = await _providers.GetByIdAsync(certificate.ProviderId, ct)
            .ConfigureAwait(false);

        // Refused without one. A certificate signed by somebody with no licence number
        // on file is not a document an employer can rely on, and issuing one puts the
        // practice's name to a claim it cannot support.
        if (provider is null || string.IsNullOrWhiteSpace(provider.LicenceNumber))
        {
            return "The signing clinician has no licence number on file. "
                + "A certificate cannot be issued without one.";
        }

        certificate.IssuedUtc = _clock.UtcNow;

        await _certificates.SaveAsync(certificate, ct).ConfigureAwait(false);

        // A certificate is a claim made to an employer or a school under the practice's
        // name. The clinician who signed it is on the certificate; this says who issued it
        // and when, which is what answers a challenge to one.
        await _audit
            .RecordAsync(
                AuditAction.Exported,
                nameof(MedicalCertificate),
                certificateId,
                "Issued a medical certificate covering "
                    + $"{MolargoFormat.Date(certificate.UnfitFrom)} to "
                    + MolargoFormat.Date(certificate.UnfitTo),
                certificate.PatientId,
                ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<CertificatePage> GetCertificatesAsync(
        Guid patientId,
        string? search = null,
        int page = 0,
        int pageSize = 17,
        CancellationToken ct = default)
    {
        var size = Math.Max(1, pageSize);

        var certificates = await _certificates
            .ListAsync(certificate => certificate.PatientId == patientId, ct)
            .ConfigureAwait(false);

        var rows = certificates
            .OrderByDescending(certificate => certificate.AttendedOn)
            .ThenByDescending(certificate => certificate.CreatedUtc)
            .ToList();

        var terms = (search ?? string.Empty).Trim();

        if (terms.Length > 0)
        {
            // Every word has to match something, rather than the whole phrase matching one
            // field — "study march" finds a March certificate for study, which is how
            // somebody remembers one.
            var words = terms.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            rows = rows.Where(row => words.All(word => Matches(row, word))).ToList();
        }

        var total = rows.Count;
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)size));

        // Clamped, because a search that shrinks the results while somebody is on page
        // three would otherwise show an empty page with no way to tell it from no matches.
        var wanted = Math.Clamp(page, 0, pageCount - 1);

        return new CertificatePage(
            rows.Skip(wanted * size).Take(size).ToList(), total, wanted, size);
    }

    /// <summary>Whether one search word appears anywhere on a certificate.</summary>
    /// <remarks>
    /// The wording is searched too, which is the point of letting it be edited: "light
    /// duties" is how somebody finds the one they are thinking of.
    /// </remarks>
    private static bool Matches(MedicalCertificate row, string word) =>
        Has(row.Body, word)
        || Has(row.AttendedOn.ToString("d MMM yyyy"), word)
        || Has(row.UnfitFrom.ToString("d MMM yyyy"), word)
        || Has(row.UnfitTo.ToString("d MMM yyyy"), word)
        || Has(row.IsForStudy ? "study" : "work", word)
        || Has(row.IsIssued ? "issued" : "draft", word)
        || Has(row.IsSigned ? "signed" : "unsigned", word)
        || Has($"{row.Days}", word);

    private static bool Has(string? value, string word) =>
        value is { Length: > 0 }
        && value.Contains(word, StringComparison.CurrentCultureIgnoreCase);

    public async Task<string?> DeleteCertificateAsync(
        Guid certificateId, CancellationToken ct = default)
    {
        var certificate = await _certificates.GetByIdAsync(certificateId, ct)
            .ConfigureAwait(false);

        if (certificate is null) return null;

        // What it covered, captured before the row goes. An audit entry saying only that a
        // certificate was deleted answers none of the questions somebody would be asking by
        // the time they read it.
        var detail = certificate.IsIssued
            ? $"Removed an issued certificate covering "
                + $"{certificate.UnfitFrom:d MMM yyyy} to {certificate.UnfitTo:d MMM yyyy}"
            : "Removed a certificate draft";

        await _certificates.DeleteAsync(certificateId, ct).ConfigureAwait(false);

        await _audit
            .RecordAsync(
                AuditAction.Deleted,
                nameof(MedicalCertificate),
                certificateId,
                detail,
                certificate.PatientId,
                ct)
            .ConfigureAwait(false);

        return null;
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
                + (string.IsNullOrWhiteSpace(provider.LicenceNumber)
                    ? string.Empty
                    : $", licence {provider.LicenceNumber}");

        // No diagnosis. A certificate states attendance and unfitness; naming the
        // treatment discloses clinical detail to an employer who has no right to it.
        return $"{name} attended this practice for dental treatment on {attended} "
            + $"and is unfit for {purpose} {period}."
            + Environment.NewLine + Environment.NewLine
            + $"Signed: {signature}";
    }
}
