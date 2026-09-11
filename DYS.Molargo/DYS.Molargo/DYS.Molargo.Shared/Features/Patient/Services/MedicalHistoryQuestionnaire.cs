using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Shared.Features.Patient.Services;

/// <summary>
/// One question on the medical-history form.
/// </summary>
/// <param name="Code">
/// The stable identifier stored against the answer. The wording is free to be reworded;
/// this is what a query matches on, so it must never change for a given question.
/// </param>
/// <param name="Text">The question as the patient reads it, stored with the answer.</param>
/// <param name="AlertKind">
/// What a "yes" means clinically, or null where the answer is context rather than an
/// alert. This is the mapping that turns a questionnaire into the record's alert banner.
/// </param>
/// <param name="Severity">The severity a "yes" is recorded at.</param>
/// <param name="PromptsForDetail">
/// True where a "yes" is useless without specifics — "allergies: yes" tells a prescriber
/// nothing, so the form asks which.
/// </param>
public sealed record MedicalHistoryQuestion(
    string Code,
    string Text,
    AlertKind? AlertKind,
    AlertSeverity Severity,
    bool PromptsForDetail = false)
{
    /// <summary>The label on the detail box, where one is asked for.</summary>
    public string DetailPrompt => Code switch
    {
        MedicalHistoryQuestionnaire.Allergies => "If yes, please list them",
        MedicalHistoryQuestionnaire.Medications => "If yes, please list them and the dose",
        _ => "If yes, please give details",
    };
}

/// <summary>
/// The practice's medical-history question set.
/// </summary>
/// <remarks>
/// <para>
/// Versioned, and the version is stored on every completed form. Without it an answer to
/// a question that has since been reworded or removed cannot be interpreted at all — and
/// clinical records are kept for a statutory period, so old answers will be read.
/// </para>
/// <para>
/// Held in code rather than in the database. The set changes rarely, every change needs a
/// version bump and a clinician's sign-off, and a question set that can be edited at
/// runtime is one that gets edited without either.
/// </para>
/// </remarks>
public static class MedicalHistoryQuestionnaire
{
    /// <summary>
    /// Bump when the question set changes in any way that alters meaning — a question
    /// added, removed, or reworded enough that a past "yes" would mean something else.
    /// Never reuse a version number.
    /// </summary>
    public const int CurrentVersion = 1;

    // Question codes. Referenced from the alert reconciliation and from any report that
    // asks "which patients are on anticoagulants", so they are constants, not literals.
    public const string HeartCondition = "heart";
    public const string Anticoagulants = "anticoagulants";
    public const string ChronicConditions = "chronic";
    public const string Allergies = "allergies";
    public const string Medications = "medications";
    public const string Pregnancy = "pregnant";
    public const string Smoking = "smoking";

    /// <summary>
    /// The questions, in the order the patient answers them.
    /// </summary>
    /// <remarks>
    /// The order is the design's: the things that stop treatment come first, so a patient
    /// who abandons the form half-way has still answered what matters most.
    /// </remarks>
    public static IReadOnlyList<MedicalHistoryQuestion> Questions { get; } =
    [
        new(HeartCondition,
            "Do you have a heart condition or pacemaker?",
            Domain.Enums.AlertKind.MedicalCondition,
            AlertSeverity.Critical,
            PromptsForDetail: true),

        new(Anticoagulants,
            "Are you taking blood thinners (e.g. Warfarin)?",
            Domain.Enums.AlertKind.Medication,
            AlertSeverity.Critical,
            PromptsForDetail: true),

        new(ChronicConditions,
            "Diabetes, high blood pressure, or respiratory conditions?",
            Domain.Enums.AlertKind.MedicalCondition,
            AlertSeverity.Warning,
            PromptsForDetail: true),

        new(Allergies,
            "Any allergies to medications or materials?",
            Domain.Enums.AlertKind.Allergy,
            AlertSeverity.Critical,
            PromptsForDetail: true),

        new(Medications,
            "Are you taking any other medications?",
            Domain.Enums.AlertKind.Medication,
            AlertSeverity.Warning,
            PromptsForDetail: true),

        new(Pregnancy,
            "Are you pregnant or breastfeeding?",
            Domain.Enums.AlertKind.Pregnancy,
            AlertSeverity.Warning),

        // No alert kind: smoking changes healing and periodontal risk, which the
        // clinician reads off the history. It is not something that should interrupt
        // prescribing, and putting it on the alert banner would dilute the banner.
        new(Smoking, "Do you smoke?", AlertKind: null, AlertSeverity.Information),
    ];

    /// <summary>How often the practice asks for the history to be re-confirmed.</summary>
    public const int ReconfirmationIntervalMonths = 12;

    public static MedicalHistoryQuestion? Find(string code) =>
        Questions.FirstOrDefault(question => question.Code == code);
}
