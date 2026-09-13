using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Shared.Repositories;

namespace DYS.Molargo.Shared.Features.Patient.Services;

/// <summary>
/// Reading and editing the practice's medical-history question set.
/// </summary>
/// <remarks>
/// One place that owns the version arithmetic. Every write goes through here so that no
/// caller can change a question without the set's version moving, which is the whole
/// reason the set was safe to take out of code.
/// </remarks>
public interface IMedicalHistoryCatalogue
{
    /// <summary>The questions a patient is asked, in order.</summary>
    Task<IReadOnlyList<MedicalHistoryQuestion>> GetActiveAsync(CancellationToken ct = default);

    /// <summary>Every question, retired ones included, for the Admin screen.</summary>
    Task<IReadOnlyList<MedicalHistoryQuestion>> GetAllAsync(CancellationToken ct = default);

    Task<MedicalHistoryQuestion?> GetAsync(Guid questionId, CancellationToken ct = default);

    /// <summary>
    /// The set's version — the highest any question has been changed at.
    /// </summary>
    Task<int> CurrentVersionAsync(CancellationToken ct = default);

    /// <summary>One question by its code, for interpreting an answer.</summary>
    Task<MedicalHistoryQuestion?> FindAsync(string code, CancellationToken ct = default);

    /// <summary>Creates or updates a question. Returns a refusal, or null.</summary>
    Task<string?> SaveAsync(MedicalHistoryQuestion question, CancellationToken ct = default);

    /// <summary>Retires a question, or brings it back.</summary>
    Task<string?> SetActiveAsync(
        Guid questionId, bool isActive, CancellationToken ct = default);

    /// <summary>Moves a question up or down the form.</summary>
    Task<string?> MoveAsync(Guid questionId, int delta, CancellationToken ct = default);
}

/// <inheritdoc cref="IMedicalHistoryCatalogue"/>
public sealed class MedicalHistoryCatalogue : IMedicalHistoryCatalogue
{
    private readonly IRepository<MedicalHistoryQuestion> _questions;

    public MedicalHistoryCatalogue(IRepository<MedicalHistoryQuestion> questions) =>
        _questions = questions;

    public async Task<IReadOnlyList<MedicalHistoryQuestion>> GetActiveAsync(
        CancellationToken ct = default)
    {
        var all = await _questions
            .ListAsync(question => question.IsActive, ct)
            .ConfigureAwait(false);

        return all.OrderBy(question => question.DisplayOrder).ToList();
    }

    public async Task<IReadOnlyList<MedicalHistoryQuestion>> GetAllAsync(
        CancellationToken ct = default)
    {
        var all = await _questions.ListAsync(ct: ct).ConfigureAwait(false);

        return all
            .OrderBy(question => question.IsActive ? 0 : 1)
            .ThenBy(question => question.DisplayOrder)
            .ToList();
    }

    public Task<MedicalHistoryQuestion?> GetAsync(
        Guid questionId, CancellationToken ct = default) =>
        _questions.GetByIdAsync(questionId, ct);

    public async Task<int> CurrentVersionAsync(CancellationToken ct = default)
    {
        var all = await _questions.ListAsync(ct: ct).ConfigureAwait(false);

        // Retired questions count. Retiring one changes what is asked, so it has to move
        // the version like any other edit — and its own row is where that bump is recorded.
        return all.Count == 0
            ? MedicalHistoryQuestionnaire.FirstVersion
            : all.Max(question => question.Version);
    }

    public Task<MedicalHistoryQuestion?> FindAsync(
        string code, CancellationToken ct = default) =>
        _questions.FindAsync(question => question.Code == code, ct);

    public async Task<string?> SaveAsync(
        MedicalHistoryQuestion question, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question.Code))
        {
            return "Give the question a code. It is what every answer is stored against.";
        }

        if (string.IsNullOrWhiteSpace(question.Text))
        {
            return "Write the question as the patient will read it.";
        }

        var code = question.Code.Trim().ToLowerInvariant();
        var all = await _questions.ListAsync(ct: ct).ConfigureAwait(false);
        var existing = all.FirstOrDefault(other => other.Id == question.Id);

        // The code is the join between a question and every answer ever given to it.
        // Editing one would leave those answers pointing at a question that no longer
        // exists, and the reports that match on it would quietly return nothing.
        if (existing is not null && !string.Equals(existing.Code, code, StringComparison.Ordinal))
        {
            return $"A question's code cannot change once it exists. \"{existing.Code}\" is "
                + "stored against every answer already given to it. Retire this one and add "
                + "a new question instead.";
        }

        if (all.Any(other => other.Id != question.Id
            && string.Equals(other.Code, code, StringComparison.OrdinalIgnoreCase)))
        {
            return $"There is already a question with the code \"{code}\".";
        }

        // A "yes" that asks for details but never says what details is a box nobody fills
        // in usefully.
        if (question.PromptsForDetail && string.IsNullOrWhiteSpace(question.DetailPrompt))
        {
            question.DetailPrompt = "If yes, please give details";
        }

        var text = question.Text.Trim();

        // Nothing changed that a patient or a clinician would see, so the version stays
        // put. A version raised by opening a question and pressing save would make the
        // number meaningless within a week, and the number is what makes an old answer
        // readable.
        var changed = existing is null
            || existing.Text != text
            || existing.DetailPrompt != question.DetailPrompt
            || existing.AlertKind != question.AlertKind
            || existing.Severity != question.Severity
            || existing.PromptsForDetail != question.PromptsForDetail;

        if (changed)
        {
            question.Version = await NextVersionAsync(all, ct).ConfigureAwait(false);
        }

        question.Code = code;
        question.Text = text;

        if (existing is null && question.DisplayOrder == 0)
        {
            question.DisplayOrder = all.Count == 0
                ? 0
                : all.Max(other => other.DisplayOrder) + 1;
        }

        await _questions.SaveAsync(question, ct).ConfigureAwait(false);

        return null;
    }

    public async Task<string?> SetActiveAsync(
        Guid questionId, bool isActive, CancellationToken ct = default)
    {
        var question = await _questions.GetByIdAsync(questionId, ct).ConfigureAwait(false);

        if (question is null) return "That question no longer exists.";

        if (question.IsActive == isActive) return null;

        var all = await _questions.ListAsync(ct: ct).ConfigureAwait(false);

        // A questionnaire that asks nothing is not a questionnaire. Retiring the last
        // active question would leave the check-in tablet showing an empty form that
        // submits successfully, which reads as "nothing to declare".
        if (!isActive && all.Count(other => other.IsActive) <= 1)
        {
            return "This is the only question left on the form. A questionnaire that asks "
                + "nothing records a patient as having declared nothing.";
        }

        question.IsActive = isActive;
        question.Version = await NextVersionAsync(all, ct).ConfigureAwait(false);

        await _questions.SaveAsync(question, ct).ConfigureAwait(false);

        return null;
    }

    public async Task<string?> MoveAsync(
        Guid questionId, int delta, CancellationToken ct = default)
    {
        var ordered = await GetActiveAsync(ct).ConfigureAwait(false);

        var index = -1;

        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Id == questionId) index = i;
        }

        if (index < 0) return "That question is not on the form.";

        var target = index + delta;

        if (target < 0 || target >= ordered.Count) return null;

        // Renumbered from scratch rather than swapping two values. Seeded rows, hand-added
        // ones and anything retired and restored do not reliably hold a contiguous
        // sequence, and swapping inside a broken sequence moves nothing.
        var moved = ordered.ToList();
        (moved[index], moved[target]) = (moved[target], moved[index]);

        for (var i = 0; i < moved.Count; i++)
        {
            if (moved[i].DisplayOrder == i) continue;

            moved[i].DisplayOrder = i;

            await _questions.SaveAsync(moved[i], ct).ConfigureAwait(false);
        }

        // Order is not meaning. Moving a question changes nothing about what a past answer
        // to it meant, so the set's version stays where it is.
        return null;
    }

    private async Task<int> NextVersionAsync(
        IReadOnlyList<MedicalHistoryQuestion> all, CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        return all.Count == 0
            ? MedicalHistoryQuestionnaire.FirstVersion
            : all.Max(question => question.Version) + 1;
    }
}
