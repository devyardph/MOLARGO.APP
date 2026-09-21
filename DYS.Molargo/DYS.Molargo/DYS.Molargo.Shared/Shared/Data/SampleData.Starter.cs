using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Shared.Entities;
using DYS.Molargo.Shared.Features.Patient.Services;

namespace DYS.Molargo.Shared.Data;

internal static partial class SampleData
{
    /// <summary>
    /// The reference data a practice needs before it can do anything, for a clinic that
    /// has just signed up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reference data only — the questionnaire, consent wording, message templates, stock
    /// categories, the fee catalogue and the formulary. Deliberately no patients, appointments, invoices
    /// or staff beyond the owner: those are the practice's own records, and inventing them
    /// would put fictional people in a real clinic's list on day one, where the only way to
    /// find out which are fake is to open each of them.
    /// </para>
    /// <para>
    /// The same wording the demo clinic gets, from the same generators, so there is one
    /// source for each set rather than a copy that drifts. What differs is the ids: fresh
    /// GUIDs rather than the deterministic ones, because those are derived from the row's
    /// key alone and the second practice on an install would collide with the first on
    /// every primary key.
    /// </para>
    /// <para>
    /// Not campaigns. A campaign is not a stored thing here — the Campaigns tab builds its
    /// audience from the patients and recalls that exist when it is opened, so a new
    /// practice sees an empty audience until it has patients, and there is nothing that
    /// could be seeded to change that.
    /// </para>
    /// </remarks>
    internal static void AddStarterData(MolargoDbContext db, Guid tenantId, DateTime now)
    {
        var rows = new List<EntityBase>();

        // Admin → Medical history. Without these the questionnaire has no questions, and
        // the screen that takes a history has nothing to ask.
        rows.AddRange(MedicalHistoryQuestionnaire.Builtin());

        // Admin → Consent forms.
        rows.AddRange(ConsentTemplates());

        // Comms → Templates, which is also what fills Comms → Automated: the automated tab
        // is the same rows filtered to those with a trigger.
        rows.AddRange(Templates());

        // Inventory → Categories.
        rows.AddRange(StockCategories());

        // Billing → Item catalogue. The one set a practice cannot work without: with no
        // codes there is nothing to put on an invoice or a treatment plan.
        rows.AddRange(ProcedureCodes());

        // Rx & referrals → Formulary. The dental shortlist — the antibiotics, the
        // analgesics and the rinses a practice actually writes — with the interaction and
        // allergy flags already on them.
        //
        // Seeded rather than left empty because an empty formulary is a prescribing screen
        // that cannot prescribe, and the alternative is every new practice typing out
        // amoxicillin from memory, including the warfarin caution that is the whole reason
        // the flags exist. A practice can withdraw anything it does not use, which is a
        // smaller job than entering ten medicines correctly.
        rows.AddRange(Formulary());

        foreach (var row in rows)
        {
            // New ids, overwriting the deterministic ones the generators mint. Those are
            // stable on purpose for the demo clinic, and stability is exactly the problem
            // here — two practices would be handed the same primary keys.
            row.Id = Guid.NewGuid();
            row.TenantId = tenantId;
            row.CreatedUtc = now;
            row.UpdatedUtc = now;
        }

        db.AddRange(rows);
    }

    /// <summary>How many rows <see cref="AddStarterData"/> writes, for the audit entry.</summary>
    /// <remarks>
    /// Counted from the same generators rather than written down beside them, so the
    /// number in the log cannot drift from what was actually seeded.
    /// </remarks>
    internal static string StarterSummary() =>
        $"{MedicalHistoryQuestionnaire.Builtin().Count} medical history questions, "
        + $"{ConsentTemplates().Count()} consent templates, "
        + $"{Templates().Count()} message templates, "
        + $"{StockCategories().Count()} stock categories, "
        + $"{ProcedureCodes().Count()} catalogue items and "
        + $"{Formulary().Count()} formulary medicines";
}
