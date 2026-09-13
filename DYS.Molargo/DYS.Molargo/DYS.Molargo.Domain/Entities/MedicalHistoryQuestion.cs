using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One question on the practice's medical-history questionnaire.
/// </summary>
/// <remarks>
/// <para>
/// A row, not a constant. This set used to live in code, on the reasoning that every
/// change needs a version bump and a clinician's sign-off, and that a set editable at
/// runtime is one that gets edited without either. The objection was right about the risk
/// and wrong about the remedy: the bump was a developer's habit, and a habit is exactly
/// what gets skipped. Here it is arithmetic — see <see cref="Version"/> — so the record
/// stays interpretable whether or not anybody remembers.
/// </para>
/// <para>
/// What the change buys is the reason a practice needs it at all. A questionnaire that
/// cannot ask about a new drug class, or about the medication a local prescriber has just
/// started using, until somebody ships a release is a questionnaire that gets worked
/// around on paper.
/// </para>
/// </remarks>
public sealed class MedicalHistoryQuestion : EntityBase
{
    /// <summary>
    /// The stable identifier stored against every answer — "anticoagulants".
    /// </summary>
    /// <remarks>
    /// Set once and never edited afterwards. The wording is free to change; this is what
    /// "which patients are on blood thinners" matches on, and rewriting it would silently
    /// detach every answer already given.
    /// </remarks>
    public string Code { get; set; } = string.Empty;

    /// <summary>The question as the patient reads it. Copied onto each answer as given.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The label on the detail box, where one is asked for.</summary>
    public string? DetailPrompt { get; set; }

    /// <summary>
    /// What a "yes" means clinically, or null where the answer is context rather than an
    /// alert. This is the mapping that turns a questionnaire into the record's alert banner.
    /// </summary>
    public AlertKind? AlertKind { get; set; }

    /// <summary>The severity a "yes" is recorded at.</summary>
    public AlertSeverity Severity { get; set; } = AlertSeverity.Information;

    /// <summary>
    /// True where a "yes" is useless without specifics — "allergies: yes" tells a
    /// prescriber nothing, so the form asks which.
    /// </summary>
    public bool PromptsForDetail { get; set; }

    /// <summary>
    /// Where it sits on the form. The things that stop treatment come first, so a patient
    /// who abandons the form half-way has still answered what matters most.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Retired questions stay for the answers already given but are no longer asked.
    /// </summary>
    /// <remarks>
    /// Never deleted. Clinical records are kept for a statutory period and will be read;
    /// an answer whose question no longer exists cannot be interpreted at all.
    /// </remarks>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The question-set version at which this question was last changed.
    /// </summary>
    /// <remarks>
    /// The set's version is the highest of these, so adding, rewording or retiring a
    /// question raises it by arithmetic rather than by anybody remembering to. Every
    /// completed form stores the version it answered, which is what keeps a past "yes"
    /// readable after the wording moves on.
    /// </remarks>
    public int Version { get; set; } = 1;
}
