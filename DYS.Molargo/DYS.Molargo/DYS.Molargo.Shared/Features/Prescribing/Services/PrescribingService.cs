using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Repositories;
using DYS.Molargo.Shared.Services;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Features.Prescribing.Services;

/// <summary>
/// One finding from the pre-prescribing check.
/// </summary>
/// <param name="Because">
/// The patient record entry that triggered it, quoted. A warning that does not say what it
/// is reacting to cannot be judged — and the prescriber has to judge it, not obey it.
/// </param>
public sealed record PrescribingCheck(
    PrescribingRisk Risk,
    string Medicine,
    string Message,
    string Because);

/// <summary>
/// A medicine on the script being written, with its directions as they currently stand.
/// </summary>
/// <param name="IsEdited">
/// Whether the directions differ from the formulary template, so the screen can offer to
/// put them back.
/// </param>
public sealed record DraftItem(
    Guid Id,
    Guid? FormularyMedicineId,
    string MedicineName,
    string Strength,
    string? Form,
    string Directions,
    int Quantity,
    int Repeats,
    bool IsEdited);

/// <summary>
/// A prescription being written: its items and everything the checks found.
/// </summary>
public sealed record PrescriptionDraft(
    Prescription Prescription,
    IReadOnlyList<DraftItem> Items,
    IReadOnlyList<PrescribingCheck> Checks)
{
    public bool IsEmpty => Items.Count == 0;

    /// <summary>
    /// Whether anything found would stop the script.
    /// </summary>
    public bool IsBlocked =>
        Checks.Any(check => check.Risk == PrescribingRisk.Contraindicated);
}

/// <summary>One row of the prescription history.</summary>
public sealed record PrescriptionSummary(
    Guid PrescriptionId,
    DateOnly? IssuedOn,
    string Summary,
    int ItemCount,
    string PrescriberName,
    PrescriptionStatus Status);

/// <summary>
/// Writing prescriptions: the formulary, the safety checks, and issuing.
/// </summary>
public interface IPrescribingService
{
    /// <summary>The practice formulary, in list order.</summary>
    Task<IReadOnlyList<FormularyMedicine>> GetFormularyAsync(CancellationToken ct = default);

    /// <summary>
    /// The patient's open draft script, creating one if there is none.
    /// </summary>
    Task<PrescriptionDraft> GetOrStartDraftAsync(
        Guid patientId, Guid? providerId, CancellationToken ct = default);

    Task<PrescriptionDraft> GetDraftAsync(Guid prescriptionId, CancellationToken ct = default);

    /// <summary>Adds a formulary medicine to the draft with its template directions.</summary>
    Task AddItemAsync(
        Guid prescriptionId, Guid formularyMedicineId, CancellationToken ct = default);

    Task RemoveItemAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>Rewrites one item's directions, quantity and repeats.</summary>
    Task UpdateItemAsync(
        Guid itemId, string directions, int quantity, int repeats,
        CancellationToken ct = default);

    /// <summary>Puts an item's directions back to the formulary template.</summary>
    Task ResetItemAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>
    /// Issues the script.
    /// </summary>
    /// <param name="overrideReason">
    /// Required to issue past a contraindication, and stored on the prescription. A
    /// prescriber may have good reason — a documented mild reaction rather than a true
    /// allergy — but the reason has to be on the record, because the script is not the
    /// place to find out that nobody wrote one down.
    /// </param>
    /// <returns>Null on success, or why it was refused.</returns>
    Task<string?> IssueAsync(
        Guid prescriptionId, string? overrideReason = null, CancellationToken ct = default);

    /// <summary>The patient's issued scripts, newest first.</summary>
    Task<IReadOnlyList<PrescriptionSummary>> GetHistoryAsync(
        Guid patientId, CancellationToken ct = default);

    /// <summary>
    /// Copies an issued script into a new draft, then re-runs the checks against today's
    /// record.
    /// </summary>
    /// <remarks>
    /// Re-checked rather than trusted. The original was safe when it was written; a new
    /// allergy or a new medication recorded since is exactly what a re-issue must catch,
    /// and it is the case a "repeat last script" button gets wrong.
    /// </remarks>
    Task<PrescriptionDraft> ReissueAsync(
        Guid prescriptionId, Guid? providerId, CancellationToken ct = default);
}

/// <inheritdoc cref="IPrescribingService"/>
public sealed class PrescribingService : IPrescribingService
{
    /// <summary>
    /// How long a script stays dispensable. Twelve months is the Australian default for a
    /// standard prescription.
    /// </summary>
    private const int ValidMonths = 12;

    private readonly IRepository<Prescription> _prescriptions;
    private readonly IRepository<PrescriptionItem> _items;
    private readonly IRepository<FormularyMedicine> _formulary;
    private readonly IRepository<PatientAlert> _alerts;
    private readonly IRepository<PatientEntity> _patients;
    private readonly IRepository<Provider> _providers;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;

    public PrescribingService(
        IRepository<Prescription> prescriptions,
        IRepository<PrescriptionItem> items,
        IRepository<FormularyMedicine> formulary,
        IRepository<PatientAlert> alerts,
        IRepository<PatientEntity> patients,
        IRepository<Provider> providers,
        IClock clock,
        IAuditLog audit)
    {
        _prescriptions = prescriptions;
        _items = items;
        _formulary = formulary;
        _alerts = alerts;
        _patients = patients;
        _providers = providers;
        _clock = clock;
        _audit = audit;
    }

    public async Task<IReadOnlyList<FormularyMedicine>> GetFormularyAsync(
        CancellationToken ct = default)
    {
        var medicines = await _formulary
            .ListAsync(medicine => medicine.IsActive, ct)
            .ConfigureAwait(false);

        return medicines
            .OrderBy(medicine => medicine.DisplayOrder)
            .ThenBy(medicine => medicine.GenericName)
            .ToList();
    }

    public async Task<PrescriptionDraft> GetOrStartDraftAsync(
        Guid patientId, Guid? providerId, CancellationToken ct = default)
    {
        var drafts = await _prescriptions
            .ListAsync(prescription => prescription.PatientId == patientId
                && prescription.Status == PrescriptionStatus.Draft, ct)
            .ConfigureAwait(false);

        // The newest open draft is reused. Starting a fresh one on every visit to the
        // screen would leave half-written scripts scattered behind the prescriber.
        var draft = drafts.OrderByDescending(prescription => prescription.CreatedUtc).FirstOrDefault();

        if (draft is null)
        {
            draft = new Prescription
            {
                PatientId = patientId,
                ProviderId = providerId ?? Guid.Empty,
                Status = PrescriptionStatus.Draft,
            };

            await _prescriptions.SaveAsync(draft, ct).ConfigureAwait(false);
        }

        return await BuildDraftAsync(draft, ct).ConfigureAwait(false);
    }

    public async Task<PrescriptionDraft> GetDraftAsync(
        Guid prescriptionId, CancellationToken ct = default)
    {
        var prescription = await _prescriptions
            .GetByIdAsync(prescriptionId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No prescription with id {prescriptionId}.");

        return await BuildDraftAsync(prescription, ct).ConfigureAwait(false);
    }

    public async Task AddItemAsync(
        Guid prescriptionId, Guid formularyMedicineId, CancellationToken ct = default)
    {
        var medicine = await _formulary
            .GetByIdAsync(formularyMedicineId, ct)
            .ConfigureAwait(false);

        if (medicine is null) return;

        var existing = await _items
            .ListAsync(item => item.PrescriptionId == prescriptionId, ct)
            .ConfigureAwait(false);

        // Already on the script is a no-op rather than a duplicate line. Two identical
        // lines dispense twice the quantity, which is the sort of doubling nobody notices
        // until the patient has taken it.
        if (existing.Any(item => item.MedicineName == medicine.GenericName
            && item.Strength == medicine.Strength))
        {
            return;
        }

        await _items
            .SaveAsync(
                new PrescriptionItem
                {
                    PrescriptionId = prescriptionId,
                    MedicineName = medicine.GenericName,
                    BrandName = medicine.BrandName,
                    Strength = medicine.Strength,
                    Form = medicine.Form,
                    Directions = medicine.DefaultDirections,
                    Quantity = medicine.DefaultQuantity,
                    Repeats = medicine.DefaultRepeats,
                },
                ct)
            .ConfigureAwait(false);
    }

    public async Task RemoveItemAsync(Guid itemId, CancellationToken ct = default) =>
        await _items.DeleteAsync(itemId, ct).ConfigureAwait(false);

    public async Task UpdateItemAsync(
        Guid itemId, string directions, int quantity, int repeats, CancellationToken ct = default)
    {
        var item = await _items.GetByIdAsync(itemId, ct).ConfigureAwait(false);
        if (item is null) return;

        // Directions are what the label carries, so an empty box would print a script
        // telling the patient nothing. The template stands until something replaces it.
        if (!string.IsNullOrWhiteSpace(directions)) item.Directions = directions.Trim();

        item.Quantity = Math.Max(quantity, 1);
        item.Repeats = Math.Max(repeats, 0);

        await _items.SaveAsync(item, ct).ConfigureAwait(false);
    }

    public async Task ResetItemAsync(Guid itemId, CancellationToken ct = default)
    {
        var item = await _items.GetByIdAsync(itemId, ct).ConfigureAwait(false);
        if (item is null) return;

        var medicine = await FindFormularyForAsync(item, ct).ConfigureAwait(false);
        if (medicine is null) return;

        item.Directions = medicine.DefaultDirections;
        item.Quantity = medicine.DefaultQuantity;
        item.Repeats = medicine.DefaultRepeats;

        await _items.SaveAsync(item, ct).ConfigureAwait(false);
    }

    public async Task<string?> IssueAsync(
        Guid prescriptionId, string? overrideReason = null, CancellationToken ct = default)
    {
        var prescription = await _prescriptions
            .GetByIdAsync(prescriptionId, ct)
            .ConfigureAwait(false);

        if (prescription is null) return "That prescription no longer exists.";

        if (prescription.Status != PrescriptionStatus.Draft)
        {
            return "That prescription has already been issued.";
        }

        var draft = await BuildDraftAsync(prescription, ct).ConfigureAwait(false);

        if (draft.IsEmpty) return "Add at least one medicine before issuing.";

        // The checks are re-run here, at the moment of issue, rather than trusted from
        // whenever the screen last rendered. A patient's allergies can be recorded by
        // someone else while the script sits half-written on this screen.
        if (draft.IsBlocked && string.IsNullOrWhiteSpace(overrideReason))
        {
            return "This script is contraindicated for this patient. "
                + "Remove the medicine, or record a reason for prescribing it anyway.";
        }

        var now = _clock.UtcNow;

        prescription.Status = PrescriptionStatus.Issued;
        prescription.IssuedUtc = now;
        prescription.ValidUntil = DateOnly.FromDateTime(now.ToLocalTime()).AddMonths(ValidMonths);

        // Stamped because the checks ran and were shown, not because anyone ticked a box.
        prescription.AllergyCheckedUtc = now;

        if (!string.IsNullOrWhiteSpace(overrideReason))
        {
            var note = $"Issued against a contraindication. Reason: {overrideReason.Trim()}";

            prescription.Notes = string.IsNullOrWhiteSpace(prescription.Notes)
                ? note
                : $"{prescription.Notes}{Environment.NewLine}{note}";
        }

        await _prescriptions.SaveAsync(prescription, ct).ConfigureAwait(false);

        // A prescription is the app's only output that a pharmacist acts on, and an
        // override is it being issued against a recorded contraindication. Both go in the
        // detail, because the second is the line an investigation is looking for.
        var written = await _items
            .ListAsync(item => item.PrescriptionId == prescriptionId, ct)
            .ConfigureAwait(false);

        await _audit
            .RecordAsync(
                AuditAction.Created,
                nameof(Prescription),
                prescriptionId,
                $"Issued a prescription for {written.Count} medicine(s)"
                    + (string.IsNullOrWhiteSpace(overrideReason)
                        ? string.Empty
                        : $" against a contraindication: {overrideReason.Trim()}"),
                prescription.PatientId,
                ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<IReadOnlyList<PrescriptionSummary>> GetHistoryAsync(
        Guid patientId, CancellationToken ct = default)
    {
        var prescriptions = await _prescriptions
            .ListAsync(prescription => prescription.PatientId == patientId
                && prescription.Status != PrescriptionStatus.Draft, ct)
            .ConfigureAwait(false);

        if (prescriptions.Count == 0) return [];

        var ids = prescriptions.Select(prescription => prescription.Id).ToHashSet();

        var items = await _items
            .ListAsync(item => ids.Contains(item.PrescriptionId), ct)
            .ConfigureAwait(false);

        var providers = await _providers.ListAsync(ct: ct).ConfigureAwait(false);
        var names = providers.ToDictionary(provider => provider.Id, provider => provider.FullName);

        return prescriptions
            .OrderByDescending(prescription => prescription.IssuedUtc ?? prescription.CreatedUtc)
            .Select(prescription =>
            {
                var lines = items
                    .Where(item => item.PrescriptionId == prescription.Id)
                    .OrderBy(item => item.MedicineName)
                    .ToList();

                return new PrescriptionSummary(
                    prescription.Id,
                    prescription.IssuedUtc is { } issued
                        ? DateOnly.FromDateTime(issued.ToLocalTime())
                        : null,

                    // Named medicines rather than "3 items": the history is scanned to
                    // answer "what has this patient had before", and a count answers it
                    // for nobody.
                    lines.Count == 0
                        ? "No medicines"
                        : string.Join(", ", lines.Select(item => item.MedicineName)),
                    lines.Count,
                    names.GetValueOrDefault(prescription.ProviderId, "Unknown prescriber"),
                    prescription.Status);
            })
            .ToList();
    }

    public async Task<PrescriptionDraft> ReissueAsync(
        Guid prescriptionId, Guid? providerId, CancellationToken ct = default)
    {
        var original = await _prescriptions
            .GetByIdAsync(prescriptionId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No prescription with id {prescriptionId}.");

        var draft = await GetOrStartDraftAsync(original.PatientId, providerId, ct)
            .ConfigureAwait(false);

        var originalItems = await _items
            .ListAsync(item => item.PrescriptionId == prescriptionId, ct)
            .ConfigureAwait(false);

        var existing = await _items
            .ListAsync(item => item.PrescriptionId == draft.Prescription.Id, ct)
            .ConfigureAwait(false);

        foreach (var source in originalItems)
        {
            var alreadyThere = existing.Any(item => item.MedicineName == source.MedicineName
                && item.Strength == source.Strength);

            if (alreadyThere) continue;

            await _items
                .SaveAsync(
                    new PrescriptionItem
                    {
                        PrescriptionId = draft.Prescription.Id,
                        MedicineName = source.MedicineName,
                        BrandName = source.BrandName,
                        Strength = source.Strength,
                        Form = source.Form,

                        // The directions as they were actually written, not the template.
                        // A prescriber who shortened a course last time meant it.
                        Directions = source.Directions,
                        Quantity = source.Quantity,
                        Repeats = source.Repeats,
                        BrandSubstitutionNotPermitted = source.BrandSubstitutionNotPermitted,
                    },
                    ct)
                .ConfigureAwait(false);
        }

        return await GetDraftAsync(draft.Prescription.Id, ct).ConfigureAwait(false);
    }

    // ---- helpers ---------------------------------------------------------

    private async Task<PrescriptionDraft> BuildDraftAsync(
        Prescription prescription, CancellationToken ct)
    {
        var items = await _items
            .ListAsync(item => item.PrescriptionId == prescription.Id, ct)
            .ConfigureAwait(false);

        var formulary = await GetFormularyAsync(ct).ConfigureAwait(false);

        var alerts = await _alerts
            .ListAsync(alert => alert.PatientId == prescription.PatientId
                && alert.ResolvedDate == null, ct)
            .ConfigureAwait(false);

        var drafted = items
            .OrderBy(item => item.MedicineName)
            .Select(item =>
            {
                var medicine = MatchFormulary(formulary, item);

                var isEdited = medicine is not null
                    && (item.Directions != medicine.DefaultDirections
                        || item.Quantity != medicine.DefaultQuantity
                        || item.Repeats != medicine.DefaultRepeats);

                return new DraftItem(
                    item.Id,
                    medicine?.Id,
                    item.MedicineName,
                    item.Strength,
                    item.Form,
                    item.Directions,
                    item.Quantity,
                    item.Repeats,
                    isEdited);
            })
            .ToList();

        var checks = Check(drafted, formulary, alerts);

        return new PrescriptionDraft(prescription, drafted, checks);
    }

    /// <summary>
    /// Screens the draft against the patient's recorded alerts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keyword matching against the practice's own formulary entries, not a drug-interaction
    /// database. It catches the cases that matter in dentistry — a penicillin allergy
    /// against amoxicillin, warfarin against an NSAID — and it is honest about what it is:
    /// a screen that reads the record so the prescriber does not have to remember to. It
    /// will not catch an interaction nobody has written into the formulary, and the screen
    /// says so rather than implying completeness.
    /// </para>
    /// <para>
    /// Matched on the alert text rather than on a coded field, because that is where the
    /// information actually is: alerts are written by clinicians as "Penicillin — rash,
    /// confirmed 2019", and demanding a coded allergy first would mean checking nothing at
    /// all for every patient already on the books.
    /// </para>
    /// </remarks>
    private static List<PrescribingCheck> Check(
        IReadOnlyList<DraftItem> items,
        IReadOnlyList<FormularyMedicine> formulary,
        IReadOnlyList<PatientAlert> alerts)
    {
        var checks = new List<PrescribingCheck>();

        foreach (var item in items)
        {
            var medicine = formulary.FirstOrDefault(entry => entry.Id == item.FormularyMedicineId);

            if (medicine is null)
            {
                // Off-formulary: nothing is known about it, so nothing can be screened.
                // Said out loud rather than passing quietly as though it were checked.
                checks.Add(new PrescribingCheck(
                    PrescribingRisk.Information,
                    item.MedicineName,
                    "Not in the practice formulary, so no allergy or interaction screen ran "
                        + "for it. Check by hand.",
                    "off-formulary item"));

                continue;
            }

            foreach (var alert in alerts)
            {
                // The summary only — never the detail. The detail is advice, history and
                // very often the name of the alternative: this patient's penicillin alert
                // reads "Rash, confirmed 2019. Use clindamycin for prophylaxis", and
                // searching it concluded she was allergic to clindamycin — the one
                // antibiotic she can safely have. A screen that pushes the prescriber off
                // the safe drug and towards the allergen is worse than no screen.
                var text = alert.Summary;

                if (alert.Kind == AlertKind.Allergy
                    && FirstMatch(medicine.AllergyClasses, text) is { } allergen)
                {
                    checks.Add(new PrescribingCheck(
                        PrescribingRisk.Contraindicated,
                        medicine.GenericName,
                        Allergen(medicine.GenericName, allergen),
                        alert.Summary));
                }

                if (alert.Kind == AlertKind.Medication
                    && FirstMatch(medicine.InteractsWith, text) is { } interacting)
                {
                    checks.Add(new PrescribingCheck(
                        PrescribingRisk.Caution,
                        medicine.GenericName,
                        medicine.InteractionCaution
                            ?? $"Interacts with {interacting}.",
                        alert.Summary));
                }

                if (alert.Kind is AlertKind.MedicalCondition or AlertKind.Pregnancy
                    && FirstMatch(medicine.ConditionCautions, text) is not null)
                {
                    checks.Add(new PrescribingCheck(
                        PrescribingRisk.Caution,
                        medicine.GenericName,
                        medicine.ConditionCaution ?? "Care needed with this condition.",
                        alert.Summary));
                }
            }
        }

        // Worst first, so the thing that stops the script is not below the thing that does
        // not — and de-duplicated, because two alerts naming the same allergy produce the
        // same finding twice.
        return checks
            .DistinctBy(check => (check.Risk, check.Medicine, check.Message))
            .OrderByDescending(check => check.Risk)
            .ThenBy(check => check.Medicine)
            .ToList();
    }

    /// <summary>
    /// How to word a blocked medicine.
    /// </summary>
    /// <remarks>
    /// Two wordings, because one produced "Clindamycin is a clindamycin". Where the match
    /// is the drug's own name the allergy is to the drug; where it is a family name — a
    /// penicillin allergy against amoxicillin — naming the family is the whole point,
    /// since that connection is what a prescriber might not make.
    /// </remarks>
    private static string Allergen(string medicine, string allergen) =>
        medicine.Contains(allergen, StringComparison.OrdinalIgnoreCase)
            ? $"{medicine} is recorded as an allergy for this patient."
            : $"{medicine} is a {allergen} and this patient has a recorded "
                + $"{allergen} allergy.";

    /// <summary>
    /// The first token of <paramref name="tokens"/> that appears in <paramref name="text"/>.
    /// </summary>
    /// <remarks>
    /// Case-insensitive substring, so "Penicillin" in a hand-typed alert matches the
    /// "penicillin" token. Substring rather than whole-word on purpose: alerts are written
    /// as "penicillins", "penicillin-allergic" and "Pen V", and a whole-word match would
    /// miss the first two.
    /// </remarks>
    private static string? FirstMatch(string? tokens, string text)
    {
        if (string.IsNullOrWhiteSpace(tokens)) return null;

        foreach (var token in tokens.Split(';', StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries))
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase)) return token;
        }

        return null;
    }

    private static FormularyMedicine? MatchFormulary(
        IReadOnlyList<FormularyMedicine> formulary, PrescriptionItem item) =>
        formulary.FirstOrDefault(entry => entry.GenericName == item.MedicineName
            && entry.Strength == item.Strength);

    private async Task<FormularyMedicine?> FindFormularyForAsync(
        PrescriptionItem item, CancellationToken ct)
    {
        var formulary = await GetFormularyAsync(ct).ConfigureAwait(false);

        return MatchFormulary(formulary, item);
    }
}
