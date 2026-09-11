namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One answer on a <see cref="MedicalHistoryForm"/>.
/// </summary>
/// <remarks>
/// A row per answer rather than a JSON blob on the form. The clinical questions asked of
/// this data are per-question — "which patients are on anticoagulants" — and a blob makes
/// every one of them a full table scan and a parse.
/// </remarks>
public sealed class MedicalHistoryAnswer : EntityBase
{
    public Guid MedicalHistoryFormId { get; set; }

    /// <summary>
    /// Stable identifier for the question, not its wording — "anticoagulants". The
    /// wording is free to be reworded; the code is what a query matches on.
    /// </summary>
    public string QuestionCode { get; set; } = string.Empty;

    /// <summary>The question as the patient saw it, so an old answer stays interpretable.</summary>
    public string QuestionText { get; set; } = string.Empty;

    /// <summary>
    /// The yes/no answer. Nullable for a question the patient skipped: "no" and "did not
    /// answer" are clinically different, and defaulting a skipped question to false is
    /// how a real allergy goes unrecorded.
    /// </summary>
    public bool? YesNo { get; set; }

    /// <summary>The elaboration a "yes" usually asks for.</summary>
    public string? Detail { get; set; }
}
