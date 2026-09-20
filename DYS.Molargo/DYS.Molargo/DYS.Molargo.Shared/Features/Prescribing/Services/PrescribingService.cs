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
/// One formulary medicine as the manage screen holds it, before it is saved.
/// </summary>
/// <remarks>
/// A record rather than the entity itself. The screen edits a copy and the service decides
/// what becomes of it — passing the tracked row straight out of the repository would mean a
/// half-typed generic name was already in the object the safety screen reads, and an
/// abandoned edit would leave it there.
/// </remarks>
/// <param name="Id">
/// Empty for a new medicine. The id of the row being replaced otherwise — it is also what
/// every issued script points at, so it must survive an edit.
/// </param>
public sealed record FormularyMedicineEdit(
    Guid Id,
    string GenericName,
    string? BrandName,
    string Strength,
    string? Form,
    MedicineClass Class,
    string DefaultDirections,
    int DefaultQuantity,
    int DefaultRepeats,
    string? AllergyClasses,
    string? InteractsWith,
    string? InteractionCaution,
    string? ConditionCautions,
    string? ConditionCaution);

/// <summary>
/// Writing prescriptions: the formulary, the safety checks, and issuing.
/// </summary>
public interface IPrescribingService
{
    /// <summary>The practice formulary, in list order.</summary>
    Task<IReadOnlyList<FormularyMedicine>> GetFormularyAsync(CancellationToken ct = default);

    /// <summary>
    /// Every medicine on the list, retired ones included.
    /// </summary>
    /// <remarks>
    /// The manage screen's view, unlike <see cref="GetFormularyAsync"/>, which is the
    /// prescriber's. A retired medicine has to stay visible to whoever manages the list —
    /// it is how somebody sees that amoxicillin was taken off rather than never added, and
    /// it is the only way to put it back.
    /// </remarks>
    Task<IReadOnlyList<FormularyMedicine>> GetAllFormularyAsync(CancellationToken ct = default);

    /// <summary>
    /// Adds or replaces one medicine. Null on success, or the refusal.
    /// </summary>
    /// <remarks>
    /// Refusals rather than exceptions, like the rest of the practice-configured lists. A
    /// missing generic name is something the person typing can fix, and a screen that says
    /// so is better than one that throws.
    /// </remarks>
    Task<string?> SaveFormularyMedicineAsync(
        FormularyMedicineEdit edit, CancellationToken ct = default);

    /// <summary>
    /// Takes a medicine off the prescribing list, or puts it back. Null on success.
    /// </summary>
    /// <remarks>
    /// Retired, never deleted. Every issued script points at the row it was written from —
    /// that is what <see cref="GetHistoryAsync"/> re-reads to tell an in-formulary item
    /// from an off-formulary one — so deleting a medicine would make old scripts
    /// unreadable to the one screen that has to interpret them.
    /// </remarks>
    Task<string?> SetFormularyActiveAsync(
        Guid medicineId, bool isActive, CancellationToken ct = default);

    /// <summary>
    /// Moves a medicine earlier or later in the prescriber's list.
    /// </summary>
    /// <remarks>
    /// Order is not decoration here. The picker shows the list in this order and the first
    /// few entries are what a prescriber reaches for without searching, so a practice that
    /// prescribes amoxiclav first should be able to put it first.
    /// </remarks>
    /// <param name="later">True to move it down the list, false to move it up.</param>
    Task MoveFormularyMedicineAsync(
        Guid medicineId, bool later, CancellationToken ct = default);

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

    /// <summary>
    /// Puts an issued script back into draft so it can be corrected. Null on success.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For the minute after issuing, when the prescriber reads it back and sees the wrong
    /// quantity. Nothing in this app transmits a script — it is printed or handed over — so
    /// until that happens the issued row is a record of an intention and correcting it is
    /// honest. After it has been handed over it is not, which is why this is on the screen
    /// that has just issued rather than on the history list.
    /// </para>
    /// <para>
    /// The issue stamps go with it: the date, the validity period and the allergy-check
    /// timestamp all described the version that has just been withdrawn. Re-issuing re-runs
    /// the checks and stamps them again, which is the point — a script edited after its
    /// allergy check and still carrying the old timestamp would claim to have been screened
    /// in a state it was never in.
    /// </para>
    /// <para>
    /// Refused once anything has been dispensed against it. At that point the script is not
    /// this app's to withdraw.
    /// </para>
    /// </remarks>
    Task<string?> ReopenAsync(Guid prescriptionId, CancellationToken ct = default);

    /// <summary>
    /// Records the prescriber's drawn signature on an issued script. Null on success.
    /// </summary>
    /// <remarks>
    /// Separate from issuing, because they are separate acts: issuing records the decision,
    /// signing happens on the sheet the patient walks out with. A script can be issued and
    /// printed unsigned — which is a state worth being able to see — so this does not
    /// happen automatically.
    /// </remarks>
    /// <param name="signatureImage">
    /// A PNG data URI from the signature pad, or null to clear one already drawn.
    /// </param>
    Task<string?> SignAsync(
        Guid prescriptionId, string? signatureImage, CancellationToken ct = default);

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

    public async Task<IReadOnlyList<FormularyMedicine>> GetAllFormularyAsync(
        CancellationToken ct = default)
    {
        var medicines = await _formulary.ListAsync(null, ct).ConfigureAwait(false);

        // Active first, then retired. A manage list sorted purely by order puts a retired
        // medicine between two live ones, which reads as a gap in the prescriber's list
        // rather than as something taken off it.
        return medicines
            .OrderByDescending(medicine => medicine.IsActive)
            .ThenBy(medicine => medicine.DisplayOrder)
            .ThenBy(medicine => medicine.GenericName)
            .ToList();
    }

    public async Task<string?> SaveFormularyMedicineAsync(
        FormularyMedicineEdit edit, CancellationToken ct = default)
    {
        var generic = (edit.GenericName ?? string.Empty).Trim();

        if (generic.Length == 0)
        {
            return "The generic name is what gets prescribed, so it cannot be blank.";
        }

        var strength = (edit.Strength ?? string.Empty).Trim();

        if (strength.Length == 0)
        {
            return "The strength has to be written out — 500mg, 0.2%. It goes onto the "
                + "script exactly as it is typed here.";
        }

        var directions = (edit.DefaultDirections ?? string.Empty).Trim();

        if (directions.Length == 0)
        {
            return "The default directions are the point of the list. Spell them out in "
                + "the words the label will carry — a prescriber can still change them on "
                + "any one script.";
        }

        if (edit.DefaultQuantity < 1) return "The quantity has to be at least one.";

        if (edit.DefaultRepeats < 0) return "Repeats cannot be negative.";

        var all = await _formulary.ListAsync(null, ct).ConfigureAwait(false);

        // Generic name and strength together, because that pair is the medicine identity
        // everywhere else: AddItemAsync refuses a second line matching it, and
        // MatchFormulary re-reads history by it. A second active row with the same pair
        // could never be put onto a script, and would leave an old script ambiguous about
        // which row wrote it.
        //
        // Against the active rows only. Retiring a medicine and writing a corrected one in
        // its place is a reasonable thing to do, and the retired row is unreachable.
        var clash = all.FirstOrDefault(medicine =>
            medicine.Id != edit.Id
            && medicine.IsActive
            && string.Equals(medicine.GenericName, generic, StringComparison.OrdinalIgnoreCase)
            && string.Equals(medicine.Strength, strength, StringComparison.OrdinalIgnoreCase));

        if (clash is not null)
        {
            return $"{clash.Label} is already on the list. Edit that one, or retire it "
                + "first if this is meant to replace it.";
        }

        var isNew = edit.Id == Guid.Empty;

        var row = isNew
            ? new FormularyMedicine
            {
                // Onto the end of the list. A new medicine at DisplayOrder 0 would sort
                // above everything the practice already prescribes, which is not what
                // adding one to the list means.
                DisplayOrder = all.Count == 0
                    ? 1
                    : all.Max(medicine => medicine.DisplayOrder) + 1,
            }
            : await _formulary.GetByIdAsync(edit.Id, ct).ConfigureAwait(false);

        if (row is null) return "That medicine is no longer on the list.";

        row.GenericName = generic;
        row.BrandName = Blank(edit.BrandName);
        row.Strength = strength;
        row.Form = Blank(edit.Form);
        row.Class = edit.Class;
        row.DefaultDirections = directions;
        row.DefaultQuantity = edit.DefaultQuantity;
        row.DefaultRepeats = edit.DefaultRepeats;
        row.AllergyClasses = Tokens(edit.AllergyClasses);
        row.InteractsWith = Tokens(edit.InteractsWith);
        row.InteractionCaution = Blank(edit.InteractionCaution);
        row.ConditionCautions = Tokens(edit.ConditionCautions);
        row.ConditionCaution = Blank(edit.ConditionCaution);

        var id = await _formulary.SaveAsync(row, ct).ConfigureAwait(false);

        await _audit
            .RecordAsync(
                isNew ? AuditAction.Created : AuditAction.Updated,
                nameof(FormularyMedicine),
                id,
                (isNew ? "Added " : "Changed ") + row.Label + " on the practice formulary"

                    // Named in the entry, because this is the field the safety screen reads
                    // and an empty one means the medicine is prescribed unscreened.
                    + (string.IsNullOrWhiteSpace(row.AllergyClasses)
                        ? " — no allergy families recorded, so it screens nothing"
                        : string.Empty),
                null,
                ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string?> SetFormularyActiveAsync(
        Guid medicineId, bool isActive, CancellationToken ct = default)
    {
        var row = await _formulary.GetByIdAsync(medicineId, ct).ConfigureAwait(false);

        if (row is null) return "That medicine is no longer on the list.";

        if (row.IsActive == isActive) return null;

        if (isActive)
        {
            // Putting one back can collide where the save could not: something else may
            // have taken its name and strength while it was off the list.
            var all = await _formulary.ListAsync(null, ct).ConfigureAwait(false);

            var clash = all.FirstOrDefault(medicine =>
                medicine.Id != row.Id
                && medicine.IsActive
                && string.Equals(
                    medicine.GenericName, row.GenericName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    medicine.Strength, row.Strength, StringComparison.OrdinalIgnoreCase));

            if (clash is not null)
            {
                return $"{clash.Label} is on the list already, so this one cannot go back "
                    + "beside it. Retire that one first.";
            }
        }

        row.IsActive = isActive;

        await _formulary.SaveAsync(row, ct).ConfigureAwait(false);

        await _audit
            .RecordAsync(
                AuditAction.Updated,
                nameof(FormularyMedicine),
                medicineId,
                (isActive ? "Put " : "Retired ") + row.Label
                    + (isActive
                        ? " back onto the practice formulary"
                        : " from the practice formulary — it can no longer be prescribed "
                            + "from the list"),
                null,
                ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task MoveFormularyMedicineAsync(
        Guid medicineId, bool later, CancellationToken ct = default)
    {
        var active = await GetFormularyAsync(ct).ConfigureAwait(false);

        var index = active.ToList().FindIndex(medicine => medicine.Id == medicineId);

        if (index < 0) return;

        var target = later ? index + 1 : index - 1;

        // Off either end is a no-op rather than a wrap. The buttons are on the first and
        // last rows too, and moving the top medicine to the bottom is not what anybody
        // pressing "up" meant by it.
        if (target < 0 || target >= active.Count) return;

        var moving = active[index];
        var neighbour = active[target];

        // Swapped rather than renumbered. The seeded rows are 1 to 10, and renumbering
        // would rewrite every one of them to move a single medicine one place.
        (moving.DisplayOrder, neighbour.DisplayOrder) =
            (neighbour.DisplayOrder, moving.DisplayOrder);

        // One transaction, so the list cannot be left with two medicines claiming the same
        // place — which reads as an arbitrary order the next time it is sorted.
        await _formulary.SaveRangeAsync([moving, neighbour], ct).ConfigureAwait(false);
    }

    /// <summary>Null for a box left empty, so a blank is stored as absent rather than "".</summary>
    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// A semicolon list, tidied.
    /// </summary>
    /// <remarks>
    /// Lower-cased and de-duplicated, with the spaces around the separators taken out. The
    /// matching in <see cref="Check"/> is case-insensitive already, so this is for whoever
    /// reads the field next rather than for the screen: a list typed as
    /// "Penicillin; penicillin ;beta-lactam" is three tokens, one of them a duplicate and
    /// one with a leading space, and none of that survives being saved.
    /// </remarks>
    private static string? Tokens(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var tokens = value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return tokens.Count == 0 ? null : string.Join(";", tokens);
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

    public async Task<string?> ReopenAsync(
        Guid prescriptionId, CancellationToken ct = default)
    {
        var prescription = await _prescriptions
            .GetByIdAsync(prescriptionId, ct)
            .ConfigureAwait(false);

        if (prescription is null) return "That prescription no longer exists.";

        if (prescription.Status == PrescriptionStatus.Draft) return null;

        if (prescription.Status != PrescriptionStatus.Issued)
        {
            // Named, rather than a flat refusal. Dispensed and cancelled are different
            // situations with different next steps, and "cannot be edited" tells the
            // prescriber neither of them.
            return prescription.Status switch
            {
                PrescriptionStatus.Dispensed =>
                    "This script has been dispensed. Write a new one rather than changing "
                        + "what the pharmacy filled.",
                PrescriptionStatus.SentToPharmacy =>
                    "This script has gone to a pharmacy. Write a new one — the copy they "
                        + "hold will not change.",
                PrescriptionStatus.Cancelled =>
                    "This script was cancelled. Write a new one.",
                _ => "This script has expired. Write a new one.",
            };
        }

        if (prescription.DispensedUtc is not null)
        {
            return "This script has been dispensed. Write a new one rather than changing "
                + "what the pharmacy filled.";
        }

        var issuedOn = prescription.IssuedUtc;

        prescription.Status = PrescriptionStatus.Draft;

        // All of it, because all of it described the version being withdrawn. The allergy
        // stamp is the one that matters most: a script edited after its check and still
        // carrying the old timestamp would claim to have been screened in a state it was
        // never in.
        prescription.IssuedUtc = null;
        prescription.ValidUntil = null;
        prescription.AllergyCheckedUtc = null;

        // The signature goes with them, and for the same reason read one step further on.
        // It was put on a sheet listing particular medicines at particular doses; leaving
        // it would carry it onto whatever the script becomes, and the printed page would
        // show the prescriber signing for a version they never saw.
        var wasSigned = prescription.IsSigned;

        prescription.PrescriberSignature = null;
        prescription.SignedUtc = null;

        await _prescriptions.SaveAsync(prescription, ct).ConfigureAwait(false);

        // Audited, and not quietly. Withdrawing a script that was signed is exactly the
        // event an investigation looks for, and the original issue entry stays beside this
        // one — so the pair reads as "issued, then taken back", which is what happened.
        await _audit
            .RecordAsync(
                AuditAction.Updated,
                nameof(Prescription),
                prescriptionId,
                "Reopened an issued prescription for editing"
                    + (issuedOn is { } when
                        ? $" — it had been issued at {when:yyyy-MM-dd HH:mm} UTC"
                        : string.Empty)
                    + ". The allergy check was cleared and must run again on re-issue."
                    + (wasSigned ? " The prescriber signature was cleared with it." : string.Empty),
                prescription.PatientId,
                ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string?> SignAsync(
        Guid prescriptionId, string? signatureImage, CancellationToken ct = default)
    {
        var prescription = await _prescriptions
            .GetByIdAsync(prescriptionId, ct)
            .ConfigureAwait(false);

        if (prescription is null) return "That prescription no longer exists.";

        if (prescription.Status == PrescriptionStatus.Draft)
        {
            // Signing a draft would put a signature on a script whose allergy check has not
            // run. The order is the safeguard, not a formality.
            return "Issue the script before signing it — the allergy check runs on issue.";
        }

        var signature = string.IsNullOrWhiteSpace(signatureImage) ? null : signatureImage;

        // Guarded rather than trusted. The pad hands back a PNG data URI; anything else
        // reaching here is a bug or a caller passing raw text, and storing it would put
        // something that is not an image where a signature is displayed.
        if (signature is not null
            && !signature.StartsWith("data:image/", StringComparison.Ordinal))
        {
            return "That is not a signature image.";
        }

        if (signature is null && prescription.PrescriberSignature is null) return null;

        var clearing = signature is null;

        prescription.PrescriberSignature = signature;
        prescription.SignedUtc = clearing ? null : _clock.UtcNow;

        await _prescriptions.SaveAsync(prescription, ct).ConfigureAwait(false);

        await _audit
            .RecordAsync(
                AuditAction.Updated,
                nameof(Prescription),
                prescriptionId,
                clearing
                    ? "Cleared the prescriber signature from an issued prescription"
                    : "Signed an issued prescription",
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
