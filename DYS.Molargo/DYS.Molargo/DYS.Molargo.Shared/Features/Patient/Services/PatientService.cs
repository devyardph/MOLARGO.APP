using System.Globalization;
using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Dtos;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Documents;
using DYS.Molargo.Shared.Repositories;
using DYS.Molargo.Shared.Services;
using Microsoft.EntityFrameworkCore;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Features.Patient.Services;

/// <summary>
/// The patients feature's own vocabulary, over the generic repository.
/// </summary>
/// <remarks>
/// <para>
/// This is where <see cref="IRepository{TEntity}"/> stops being generic. A view model
/// asking for "the debtors worklist" should not be composing a LINQ expression, and the
/// two screens that want that list must not each write their own version of it — which is
/// exactly what a generic repository invites if nothing sits in front of it.
/// </para>
/// <para>
/// So: the repository owns paging, ordering, soft-delete and audit stamps; this owns what
/// a filter <em>means</em>. New query shapes for patients belong here as named methods.
/// </para>
/// </remarks>
/// <summary>Who already holds an email address, named so a refusal can say.</summary>
public sealed record EmailHolder(Guid PatientId, string Name, string? PatientNumber);

public interface IPatientService
{
    /// <summary>One page of the patients list, projected to just the columns it shows.</summary>
    Task<PagedResult<PatientListItemDto>> SearchAsync(
        PatientQuery query, CancellationToken ct = default);

    Task<PatientEntity?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// The patient plus every list the record screen shows, in one round trip. Null when
    /// there is no such patient.
    /// </summary>
    Task<PatientRecord?> GetRecordAsync(Guid id, CancellationToken ct = default);

    Task<Guid> SaveAsync(PatientEntity patient, CancellationToken ct = default);

    /// <summary>
    /// The patient already holding this email address, or null.
    /// </summary>
    /// <param name="excludingId">
    /// The record being edited, so saving a patient without changing their email does not
    /// report them as a conflict with themselves.
    /// </param>
    /// <remarks>
    /// Returns who holds it rather than a bare true or false. A refusal that just says
    /// "already used" leaves the front desk with no way to tell a duplicate record from a
    /// second family member on the same address, which is the case this check is most
    /// likely to be wrong about.
    /// </remarks>
    Task<EmailHolder?> FindEmailHolderAsync(
        string email, Guid? excludingId = null, CancellationToken ct = default);

    /// <summary>
    /// Existing patients that look like the details being entered, best match first.
    /// Empty when nothing scores highly enough to be worth interrupting for.
    /// </summary>
    Task<IReadOnlyList<DuplicateCandidate>> FindDuplicatesAsync(
        string firstName,
        string lastName,
        DateOnly? dateOfBirth,
        string? mobile,
        Guid? excludingId = null,
        CancellationToken ct = default);

    /// <summary>The next human-facing patient number to assign.</summary>
    Task<string> GetNextPatientNumberAsync(CancellationToken ct = default);

    /// <summary>Existing households, for linking a new patient to a family.</summary>
    Task<IReadOnlyList<HouseholdOption>> GetHouseholdsAsync(CancellationToken ct = default);

    /// <summary>
    /// Records a medical alert against a patient. Additive: it never rewrites an existing
    /// one, because an alert carries a severity, an onset date and who recorded it that a
    /// quick-entry box has no way to supply again.
    /// </summary>
    Task AddAlertAsync(
        Guid patientId,
        AlertKind kind,
        AlertSeverity severity,
        string summary,
        DateOnly onset,
        CancellationToken ct = default);

    /// <summary>
    /// Records a completed medical-history questionnaire: the form, its answers, and the
    /// alerts a "yes" implies. Never removes an alert — an answer that contradicts the
    /// record comes back in the result for a clinician to look at.
    /// </summary>
    Task<MedicalHistoryResult> SaveMedicalHistoryAsync(
        MedicalHistorySubmission submission, CancellationToken ct = default);

    /// <summary>
    /// Stores an uploaded file and records it against the patient. The bytes go to
    /// <c>IDocumentStore</c>, which is the seam the bucket replaces later.
    /// </summary>
    Task<PatientDocument> AddDocumentAsync(
        Guid patientId,
        DocumentKind kind,
        string fileName,
        string? contentType,
        Stream content,
        string? relatesTo = null,
        Guid? appointmentId = null,
        CancellationToken ct = default);

    /// <summary>Soft-deletes the row and removes the stored file. False if there was no such row.</summary>
    Task<bool> DeleteDocumentAsync(Guid documentId, CancellationToken ct = default);

    Task<PatientDocument?> GetDocumentAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>
    /// Which of these documents still have bytes behind them, by id. A row whose file has
    /// gone is shown as such rather than offered as openable.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, bool>> GetDocumentAvailabilityAsync(
        IEnumerable<PatientDocument> documents, CancellationToken ct = default);

    /// <summary>
    /// Moves the patient through their lifecycle. Named for what it means to the practice
    /// rather than for the column it writes: setting <see cref="PatientStatus.Archived"/>
    /// stops recalls, reminders and marketing, which is why anyone calls this.
    /// </summary>
    Task SetStatusAsync(Guid id, PatientStatus status, CancellationToken ct = default);

    /// <summary>Other members of the same household, excluding the patient themselves.</summary>
    Task<IReadOnlyList<PatientEntity>> GetHouseholdAsync(Guid id, CancellationToken ct = default);
}

/// <inheritdoc cref="IPatientService"/>
public sealed class PatientService : IPatientService
{
    /// <summary>
    /// Where patient numbering starts on an empty database. Not 1: a number that reads
    /// like a sequence position tells anyone holding it how few patients the practice
    /// has, and staff misread "7" over the phone far more often than "10207".
    /// </summary>
    private const int FirstPatientNumber = 10_001;

    private readonly IRepository<PatientEntity> _patients;
    private readonly IRepository<PatientAlert> _alerts;
    private readonly IRepository<TreatmentPlan> _plans;
    private readonly IRepository<TreatmentPlanItem> _planItems;
    private readonly IRepository<Appointment> _appointments;
    private readonly IRepository<PatientDocument> _documents;
    private readonly IRepository<ConsentForm> _consents;
    private readonly IRepository<CommunicationLog> _communications;
    private readonly IRepository<Invoice> _invoices;
    private readonly IRepository<InvoiceLine> _invoiceLines;
    private readonly IRepository<Claim> _claims;
    private readonly IRepository<Recall> _recalls;
    private readonly IRepository<MedicalHistoryForm> _medicalHistory;
    private readonly IRepository<MedicalHistoryAnswer> _medicalHistoryAnswers;
    private readonly IRepository<Provider> _providers;
    private readonly IDocumentStore _documentStore;
    private readonly IClock _clock;

    public PatientService(
        IRepository<PatientEntity> patients,
        IRepository<PatientAlert> alerts,
        IRepository<TreatmentPlan> plans,
        IRepository<TreatmentPlanItem> planItems,
        IRepository<Appointment> appointments,
        IRepository<PatientDocument> documents,
        IRepository<ConsentForm> consents,
        IRepository<CommunicationLog> communications,
        IRepository<Invoice> invoices,
        IRepository<InvoiceLine> invoiceLines,
        IRepository<Claim> claims,
        IRepository<Recall> recalls,
        IRepository<MedicalHistoryForm> medicalHistory,
        IRepository<MedicalHistoryAnswer> medicalHistoryAnswers,
        IRepository<Provider> providers,
        IDocumentStore documentStore,
        IClock clock)
    {
        _patients = patients;
        _alerts = alerts;
        _plans = plans;
        _planItems = planItems;
        _appointments = appointments;
        _documents = documents;
        _consents = consents;
        _communications = communications;
        _invoices = invoices;
        _invoiceLines = invoiceLines;
        _claims = claims;
        _recalls = recalls;
        _medicalHistory = medicalHistory;
        _medicalHistoryAnswers = medicalHistoryAnswers;
        _providers = providers;
        _documentStore = documentStore;
        _clock = clock;
    }

    public Task<PagedResult<PatientListItemDto>> SearchAsync(
        PatientQuery query, CancellationToken ct = default)
    {
        // A LIKE pattern rather than a bare term, because the predicate below uses
        // EF.Functions.Like and not string.Contains. Contains translates to SQLite's
        // instr(), which is CASE-SENSITIVE — so searching "yuen" found no Margaret Yuen,
        // while "Yuen" did. LIKE is case-insensitive over ASCII, which is what a front
        // desk typing a name into one box expects.
        //
        // The cost is that this expression can now only be run by an EF provider. That is
        // acceptable here: the alternative is comparing case-insensitively in memory,
        // which means reading every patient row to render ten of them.
        var pattern = string.IsNullOrWhiteSpace(query.SearchTerm)
            ? null
            : $"%{Escape(query.SearchTerm.Trim())}%";

        var status = query.Status;
        var requiredTags = (int)query.Tags;
        var outstandingOnly = query.OutstandingBalanceOnly;

        return _patients.GetPageAsync(
            query.Page,
            query.PageSize,
            orderBy: patient => patient.LastName,
            selector: patient => new PatientListItemDto
            {
                Id = patient.Id,

                // Concatenated in the projection rather than read from the entity's
                // FullName, which is a computed property and cannot be translated to SQL.
                FullName = patient.FirstName + " " + patient.LastName,
                PatientNumber = patient.PatientNumber,
                DateOfBirth = patient.DateOfBirth,
                Mobile = patient.Mobile,
                Status = patient.Status,
                Tags = patient.Tags,
                LastSeenUtc = patient.LastSeenUtc,
                Balance = patient.Balance,
                FailedToAttendCount = patient.FailedToAttendCount,
            },
            descending: false,
            predicate: patient =>
                // No explicit status filter hides the archived. Including them by default
                // is how a recall letter reaches someone who has died.
                (status != null ? patient.Status == status : patient.Status != PatientStatus.Archived) &&

                (pattern == null ||
                    EF.Functions.Like(patient.FirstName, pattern) ||
                    EF.Functions.Like(patient.LastName, pattern) ||
                    (patient.PatientNumber != null && EF.Functions.Like(patient.PatientNumber, pattern)) ||
                    (patient.Mobile != null && EF.Functions.Like(patient.Mobile, pattern))) &&

                // Bitwise AND on the stored integer, evaluated in SQL. HasFlag does not
                // translate, and the provider throws rather than falling back to memory.
                (requiredTags == 0 || ((int)patient.Tags & requiredTags) == requiredTags) &&

                // Inequality, not "> 0". Balance is stored as TEXT because SQLite has no
                // decimal, so EF compares the converted string — "is not the string 0" is
                // right, where a greater-than would rank "1000" below "9".
                (!outstandingOnly || patient.Balance != 0m),
            ct);
    }

    public Task<PatientEntity?> GetAsync(Guid id, CancellationToken ct = default) =>
        _patients.GetByIdAsync(id, ct);


    public async Task<PatientRecord?> GetRecordAsync(Guid id, CancellationToken ct = default)
    {
        var patient = await _patients.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (patient is null) return null;

        // Each list is one indexed query on PatientId. Issued in sequence rather than
        // with Task.WhenAll: every repository shares one MolargoDatabase, a DbContext is
        // not thread-safe, and running these concurrently against SQLite is how you get
        // an intermittent "database is locked" that only shows up under load.
        var alerts = await _alerts
            .ListAsync(alert => alert.PatientId == id, ct).ConfigureAwait(false);

        var plans = await _plans
            .ListAsync(plan => plan.PatientId == id, ct).ConfigureAwait(false);

        var planIds = plans.Select(plan => plan.Id).ToList();
        var planItems = await _planItems
            .ListAsync(item => planIds.Contains(item.TreatmentPlanId), ct).ConfigureAwait(false);

        var appointments = await _appointments
            .ListAsync(appointment => appointment.PatientId == id, ct).ConfigureAwait(false);

        var documents = await _documents
            .ListAsync(document => document.PatientId == id, ct).ConfigureAwait(false);

        var consents = await _consents
            .ListAsync(consent => consent.PatientId == id, ct).ConfigureAwait(false);

        var communications = await _communications
            .ListAsync(message => message.PatientId == id, ct).ConfigureAwait(false);

        var recalls = await _recalls
            .ListAsync(recall => recall.PatientId == id, ct).ConfigureAwait(false);

        var history = await _medicalHistory
            .ListAsync(form => form.PatientId == id, ct).ConfigureAwait(false);

        var latestHistory = history
            .Where(form => form.CompletedUtc is not null)
            .OrderByDescending(form => form.CompletedUtc)
            .FirstOrDefault();

        // Only the latest questionnaire's answers. Loading every completion's answers
        // would be most of the table for a long-standing patient, and the screen shows
        // the current picture.
        var answers = latestHistory is null
            ? []
            : await _medicalHistoryAnswers
                .ListAsync(answer => answer.MedicalHistoryFormId == latestHistory.Id, ct)
                .ConfigureAwait(false);

        var billing = await BuildBillingAsync(id, ct).ConfigureAwait(false);

        var household = await GetHouseholdAsync(id, ct).ConfigureAwait(false);

        var providers = await _providers.ListAsync(ct: ct).ConfigureAwait(false);

        return new PatientRecord
        {
            Patient = patient,

            // Sorted here rather than in the query: the order wanted is by severity
            // descending, and severity is a stored enum whose numeric order happens to
            // match — relying on that in SQL would break silently if a member were added.
            Alerts = alerts
                .OrderByDescending(alert => alert.Severity)
                .ThenBy(alert => alert.Kind)
                .ToList(),

            Household = household,
            Plans = plans,
            PlanItems = planItems,
            Appointments = appointments,

            Documents = documents
                .OrderByDescending(document => document.DocumentDateUtc ?? document.CreatedUtc)
                .ToList(),

            Consents = consents,

            Communications = communications
                .OrderByDescending(message => message.SentUtc ?? message.CreatedUtc)
                .ToList(),

            Billing = billing,
            Recalls = recalls,

            MedicalHistory = latestHistory,
            MedicalHistoryAnswers = answers,

            ProviderNames = providers.ToDictionary(provider => provider.Id, provider => provider.FullName),
        };
    }

    /// <summary>
    /// Flattens invoices, their lines and their claims into the billing tab's rows.
    /// </summary>
    /// <remarks>
    /// The fund benefit is held per claim, which is per invoice, while the tab shows it
    /// per line. It is apportioned across an invoice's lines in proportion to their fee —
    /// which is an approximation, and the right one to make here: the fund assesses the
    /// invoice as a whole and does not tell us which item its benefit was against.
    /// </remarks>
    private async Task<IReadOnlyList<BillingRow>> BuildBillingAsync(Guid id, CancellationToken ct)
    {
        var invoices = await _invoices
            .ListAsync(invoice => invoice.PatientId == id, ct).ConfigureAwait(false);

        if (invoices.Count == 0) return [];

        var invoiceIds = invoices.Select(invoice => invoice.Id).ToList();

        var lines = await _invoiceLines
            .ListAsync(line => invoiceIds.Contains(line.InvoiceId), ct).ConfigureAwait(false);

        var claims = await _claims
            .ListAsync(claim => invoiceIds.Contains(claim.InvoiceId), ct).ConfigureAwait(false);

        var benefitByInvoice = claims
            .GroupBy(claim => claim.InvoiceId)
            .ToDictionary(group => group.Key, group => group.Sum(claim => claim.AmountApproved));

        var rows = new List<BillingRow>();

        foreach (var invoice in invoices)
        {
            var invoiceLines = lines.Where(line => line.InvoiceId == invoice.Id).ToList();
            if (invoiceLines.Count == 0) continue;

            var benefit = benefitByInvoice.GetValueOrDefault(invoice.Id);
            var lineTotal = invoiceLines.Sum(line => line.LineTotal);

            foreach (var line in invoiceLines)
            {
                // Guard the division: an invoice whose lines total zero would otherwise
                // divide by zero rather than simply carrying no benefit.
                var share = lineTotal == 0m ? 0m : line.LineTotal / lineTotal;
                var lineBenefit = decimal.Round(benefit * share, 2);

                rows.Add(new BillingRow(
                    line.ServiceDate,
                    line.ItemNumber,
                    line.Description,
                    line.ToothNumber,
                    line.LineTotal,
                    lineBenefit,
                    line.LineTotal - lineBenefit,
                    invoice.Status,

                    // The outstanding amount belongs to the invoice, not the line, so it
                    // is attributed to the first line only — otherwise a three-line
                    // invoice reports its balance three times and the total triples.
                    line.Id == invoiceLines[0].Id ? invoice.AmountOutstanding : 0m,
                    invoice.InvoiceNumber));
            }
        }

        return rows.OrderByDescending(row => row.ServiceDate).ToList();
    }

    public Task<Guid> SaveAsync(PatientEntity patient, CancellationToken ct = default) =>
        _patients.SaveAsync(patient, ct);


    public async Task<EmailHolder?> FindEmailHolderAsync(
        string email, Guid? excludingId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;

        var term = email.Trim();

        // LIKE with no wildcards, which is how the duplicate finder above matches a
        // surname: SQLite's LIKE is case-insensitive for ASCII, so "Jane@x.com" and
        // "jane@x.com" collide as they should. A plain equality test would not — SQLite
        // compares TEXT case-sensitively — and two records differing only in the case of
        // an address is exactly the duplicate this is meant to catch.
        //
        // Scoped to this clinic by the repository's tenant filter, and that is the right
        // scope: the same person can be a patient at two practices.
        var holders = await _patients
            .ListAsync(
                patient => patient.Email != null && EF.Functions.Like(patient.Email, term),
                ct)
            .ConfigureAwait(false);

        var holder = holders.FirstOrDefault(patient => patient.Id != excludingId);

        return holder is null
            ? null
            : new EmailHolder(holder.Id, holder.FullName, holder.PatientNumber);
    }

    public async Task<IReadOnlyList<DuplicateCandidate>> FindDuplicatesAsync(
        string firstName,
        string lastName,
        DateOnly? dateOfBirth,
        string? mobile,
        Guid? excludingId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(lastName)) return [];

        // Narrowed by surname in SQL before scoring in memory. Scoring every patient
        // would work on a seeded database and not on a real one — a practice with 20,000
        // records would read all of them on every keystroke of a new patient's name.
        //
        // The cost is that a mistyped surname escapes the check. That is the right trade:
        // the alternative narrowing key is date of birth, which is entered after the name
        // and is blank for a lead.
        var surname = lastName.Trim();

        var candidates = await _patients
            .ListAsync(patient => EF.Functions.Like(patient.LastName, surname), ct)
            .ConfigureAwait(false);

        return DuplicateMatcher.Match(firstName, lastName, dateOfBirth, mobile, candidates, excludingId);
    }

    public async Task<string> GetNextPatientNumberAsync(CancellationToken ct = default)
    {
        var patients = await _patients.ListAsync(ct: ct).ConfigureAwait(false);

        // Highest existing number plus one, parsed rather than counted: counting rows
        // reuses a number as soon as one is archived, and two patients sharing #10214 is
        // exactly the confusion the human-facing number exists to avoid.
        var highest = patients
            .Select(patient => patient.PatientNumber)
            .Where(number => number is { Length: > 0 })
            .Select(number => int.TryParse(number, out var parsed) ? parsed : 0)
            .DefaultIfEmpty(FirstPatientNumber - 1)
            .Max();

        return Math.Max(highest + 1, FirstPatientNumber).ToString(CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<HouseholdOption>> GetHouseholdsAsync(CancellationToken ct = default)
    {
        var patients = await _patients
            .ListAsync(patient => patient.HouseholdId != null, ct)
            .ConfigureAwait(false);

        return patients
            .GroupBy(patient => patient.HouseholdId!.Value)
            .Select(group => new HouseholdOption(
                group.Key,

                // Named by the surname its members share, or the commonest one where they
                // differ — a blended family is still "the Byrne household" to the front
                // desk, and there is no household name field to read instead.
                group
                    .GroupBy(patient => patient.LastName)
                    .OrderByDescending(names => names.Count())
                    .ThenBy(names => names.Key)
                    .First().Key,
                group.Count()))
            .OrderBy(option => option.Surname)
            .ToList();
    }


    public async Task<MedicalHistoryResult> SaveMedicalHistoryAsync(
        MedicalHistorySubmission submission, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var today = _clock.Today;

        var form = new MedicalHistoryForm
        {
            PatientId = submission.PatientId,
            AppointmentId = submission.AppointmentId,

            // Stamped from the questionnaire, not taken as a parameter. An answer
            // recorded against the wrong version cannot be interpreted at all, and
            // letting a caller choose the version is how that happens.
            FormVersion = MedicalHistoryQuestionnaire.CurrentVersion,

            CompletedUtc = now,
            SignedUtc = now,
            SignedByName = submission.SignedByName.Trim(),
            AdditionalNotes = string.IsNullOrWhiteSpace(submission.AdditionalNotes)
                ? null
                : submission.AdditionalNotes.Trim(),
        };

        var formId = await _medicalHistory.SaveAsync(form, ct).ConfigureAwait(false);

        foreach (var answer in submission.Answers)
        {
            var question = MedicalHistoryQuestionnaire.Find(answer.Code);
            if (question is null) continue;

            await _medicalHistoryAnswers
                .SaveAsync(
                    new MedicalHistoryAnswer
                    {
                        MedicalHistoryFormId = formId,
                        QuestionCode = question.Code,

                        // The wording is stored with the answer, not only the code. A
                        // question reworded in a later version would otherwise leave this
                        // answer unreadable, and the record outlives the question set.
                        QuestionText = question.Text,
                        YesNo = answer.YesNo,
                        Detail = string.IsNullOrWhiteSpace(answer.Detail) ? null : answer.Detail.Trim(),
                    },
                    ct)
                .ConfigureAwait(false);
        }

        var reconciled = await ReconcileAlertsAsync(submission, today, ct).ConfigureAwait(false);

        if (reconciled.NeedsReview.Count > 0)
        {
            // Written onto the form as well as returned, so a discrepancy survives the
            // patient walking away from the tablet and is visible on the record instead
            // of only in the moment.
            form.AdditionalNotes = string.Join(
                Environment.NewLine,
                new[] { form.AdditionalNotes, "Needs clinician review:" }
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Concat(reconciled.NeedsReview.Select(item =>
                        $"- answered no to '{item.Question}' but the record still holds: {item.ExistingAlert}")));

            await _medicalHistory.SaveAsync(form, ct).ConfigureAwait(false);
        }

        return new MedicalHistoryResult(
            formId,
            reconciled.Added,
            reconciled.NeedsReview,
            today.AddMonths(MedicalHistoryQuestionnaire.ReconfirmationIntervalMonths));
    }

    /// <summary>
    /// Turns the answers into alerts, and reports the ones that contradict the record.
    /// </summary>
    /// <remarks>
    /// Additive only. A "yes" creates an alert where there is not already an equivalent
    /// one; a "no" against an existing alert produces a discrepancy and changes nothing.
    ///
    /// Never deleting on a "no" is the whole point. A patient who says they are not on
    /// warfarin when the record says they are may have stopped, or may have forgotten —
    /// and the cost of confusing those two is not symmetrical. Acting on their word would
    /// take an anticoagulant warning off the record of someone about to have a tooth out.
    /// </remarks>
    private async Task<(int Added, IReadOnlyList<MedicalHistoryDiscrepancy> NeedsReview)>
        ReconcileAlertsAsync(MedicalHistorySubmission submission, DateOnly today, CancellationToken ct)
    {
        var existing = await _alerts
            .ListAsync(alert => alert.PatientId == submission.PatientId, ct)
            .ConfigureAwait(false);

        var added = 0;
        var needsReview = new List<MedicalHistoryDiscrepancy>();

        foreach (var answer in submission.Answers)
        {
            var question = MedicalHistoryQuestionnaire.Find(answer.Code);
            if (question?.AlertKind is not { } kind) continue;

            var live = existing
                .Where(alert => alert.Kind == kind && !alert.IsResolved)
                .ToList();

            if (answer.YesNo == true)
            {
                // The listed items become the alerts. With nothing listed, the question
                // itself is recorded — "heart condition, no details given" is still a
                // warning a clinician needs, and dropping it because the box was empty
                // would lose the answer entirely.
                var entries = SplitEntries(answer.Detail);
                if (entries.Count == 0) entries = [question.Text.TrimEnd('?')];

                foreach (var entry in entries)
                {
                    await AddAlertAsync(submission.PatientId, kind, question.Severity, entry, today, ct)
                        .ConfigureAwait(false);
                    added++;
                }

                continue;
            }

            if (answer.YesNo == false && live.Count > 0)
            {
                needsReview.AddRange(live.Select(alert =>
                    new MedicalHistoryDiscrepancy(question.Text, alert.Summary)));
            }
        }

        return (added, needsReview);
    }

    /// <summary>
    /// Splits what the patient typed into one entry per item.
    /// </summary>
    /// <remarks>
    /// The same separators the patient form accepts, so "Penicillin (rash) · Latex"
    /// becomes two alerts rather than one with an interpunct in the middle. Commas are
    /// deliberately not separators: "Warfarin 5mg, daily" is one medication, and
    /// splitting on the comma produced a second alert reading "daily".
    /// </remarks>
    private static List<string> SplitEntries(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text
                .Split(['\n', '\r', '·', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();


    public async Task<PatientDocument> AddDocumentAsync(
        Guid patientId,
        DocumentKind kind,
        string fileName,
        string? contentType,
        Stream content,
        string? relatesTo = null,
        Guid? appointmentId = null,
        CancellationToken ct = default)
    {
        // The file is written first. A row pointing at nothing is worse than a file with
        // no row: the row shows on the record as a document that cannot be opened, while
        // an orphaned file is invisible and reclaimable.
        var stored = await _documentStore
            .SaveAsync(patientId, fileName, content, ct)
            .ConfigureAwait(false);

        var document = new PatientDocument
        {
            PatientId = patientId,
            Kind = kind,
            Name = Path.GetFileNameWithoutExtension(fileName) is { Length: > 0 } stem
                ? stem
                : fileName,
            RelativePath = stored.Key,
            ContentType = contentType,
            SizeBytes = stored.SizeBytes,
            DocumentDateUtc = _clock.UtcNow,
            RelatesTo = relatesTo,
            AppointmentId = appointmentId,
        };

        try
        {
            await _documents.SaveAsync(document, ct).ConfigureAwait(false);
        }
        catch
        {
            // The row failed, so the file it would have pointed at is orphaned. Removed
            // here rather than left for a cleanup job that does not exist.
            await _documentStore.DeleteAsync(stored.Key, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        return document;
    }

    public async Task<bool> DeleteDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        var document = await _documents.GetByIdAsync(documentId, ct).ConfigureAwait(false);
        if (document is null) return false;

        // The row is soft-deleted, and the file goes with it.
        //
        // A deliberate exception to this domain's keep-everything rule: a soft-deleted
        // row whose bytes are still on disk is a document the practice believes it has
        // deleted and has not. For a scanned ID or a radiograph that is a privacy
        // problem, not a retention one — the clinical fact is the row, and the row
        // survives.
        await _documents.DeleteAsync(documentId, ct).ConfigureAwait(false);
        await _documentStore.DeleteAsync(document.RelativePath, ct).ConfigureAwait(false);

        return true;
    }

    public async Task<PatientDocument?> GetDocumentAsync(Guid documentId, CancellationToken ct = default) =>
        await _documents.GetByIdAsync(documentId, ct).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<Guid, bool>> GetDocumentAvailabilityAsync(
        IEnumerable<PatientDocument> documents, CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, bool>();

        foreach (var document in documents)
        {
            result[document.Id] = await _documentStore
                .ExistsAsync(document.RelativePath, ct)
                .ConfigureAwait(false);
        }

        return result;
    }

    public async Task AddAlertAsync(
        Guid patientId,
        AlertKind kind,
        AlertSeverity severity,
        string summary,
        DateOnly onset,
        CancellationToken ct = default)
    {
        var trimmed = summary.Trim();
        if (trimmed.Length == 0) return;

        // Skip one that is already on file, so re-saving the form does not stack up
        // duplicate allergies. Matched on kind and text, case-insensitively, because
        // "Penicillin" and "penicillin" are the same warning.
        var existing = await _alerts
            .ListAsync(alert => alert.PatientId == patientId && alert.Kind == kind, ct)
            .ConfigureAwait(false);

        if (existing.Any(alert =>
                string.Equals(alert.Summary, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        await _alerts
            .SaveAsync(
                new PatientAlert
                {
                    PatientId = patientId,
                    Kind = kind,
                    Severity = severity,
                    Summary = trimmed,
                    OnsetDate = onset,
                },
                ct)
            .ConfigureAwait(false);
    }

    public async Task SetStatusAsync(Guid id, PatientStatus status, CancellationToken ct = default)
    {
        var patient = await _patients.GetByIdAsync(id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No patient with id {id}.");

        patient.Status = status;
        await _patients.SaveAsync(patient, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PatientEntity>> GetHouseholdAsync(
        Guid id, CancellationToken ct = default)
    {
        var patient = await _patients.GetByIdAsync(id, ct).ConfigureAwait(false);

        // A patient with no household id is a household of one, not everyone whose
        // household id is also null — which is what a naive equality filter would return.
        if (patient?.HouseholdId is not { } householdId) return Array.Empty<PatientEntity>();

        return await _patients
            .ListAsync(other => other.HouseholdId == householdId && other.Id != id, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Strips the LIKE wildcards from a user's search term.
    /// </summary>
    /// <remarks>
    /// Stripped rather than escaped. Escaping needs an ESCAPE clause, and EF only emits
    /// one for the three-argument <c>Like</c> overload, which SQLite does not support. A
    /// term left unescaped is worse than useless: a single "%" typed into the box matches
    /// every patient, which reads as the filter being broken rather than as a bad search.
    /// </remarks>
    private static string Escape(string term) =>
        term.Replace("%", string.Empty).Replace("_", string.Empty);
}
