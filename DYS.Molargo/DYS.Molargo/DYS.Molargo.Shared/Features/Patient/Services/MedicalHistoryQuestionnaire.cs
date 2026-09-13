using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Shared.Features.Patient.Services;

/// <summary>
/// The practice's medical-history question set.
/// </summary>
/// <remarks>
/// <para>
/// The questions themselves are rows now, editable under Admin, and are read through
/// <see cref="IMedicalHistoryCatalogue"/>. What stays here is what must not be editable:
/// the question codes, which every alert reconciliation and clinical report matches on,
/// and the starting set a new database is seeded from.
/// </para>
/// <para>
/// Versioning did not go away with the move — it got stricter. It used to be a constant a
/// developer had to remember to bump; it is now the highest
/// <see cref="MedicalHistoryQuestion.Version"/> in the set, raised automatically by any
/// edit. Every completed form stores the version it answered, which is what keeps an old
/// "yes" interpretable after the wording changes.
/// </para>
/// </remarks>
public static class MedicalHistoryQuestionnaire
{
    // Question codes. Referenced from the alert reconciliation and from any report that
    // asks "which patients are on anticoagulants", so they are constants, not literals —
    // and the reason Admin will not let a code be edited once it exists.
    public const string HeartCondition = "heart";
    public const string Anticoagulants = "anticoagulants";
    public const string ChronicConditions = "chronic";
    public const string Allergies = "allergies";
    public const string Medications = "medications";
    public const string Pregnancy = "pregnant";
    public const string Smoking = "smoking";

    /// <summary>How often the practice asks for the history to be re-confirmed.</summary>
    public const int ReconfirmationIntervalMonths = 12;

    /// <summary>The version a set starts at, and the floor it can never fall below.</summary>
    public const int FirstVersion = 1;

    /// <summary>
    /// The set a new database starts with.
    /// </summary>
    /// <remarks>
    /// Seeded, not enforced. A practice is expected to change this — it is a reasonable
    /// general questionnaire, not theirs — and nothing here overwrites what they have
    /// edited afterwards.
    ///
    /// The order is the design's: the things that stop treatment come first, so a patient
    /// who abandons the form half-way has still answered what matters most.
    /// </remarks>
    public static IReadOnlyList<MedicalHistoryQuestion> Builtin() =>
    [
        Question(HeartCondition,
            "Do you have a heart condition or pacemaker?",
            AlertKind.MedicalCondition, AlertSeverity.Critical, order: 0,
            detail: "If yes, please give details"),

        Question(Anticoagulants,
            "Are you taking blood thinners (e.g. Warfarin)?",
            AlertKind.Medication, AlertSeverity.Critical, order: 1,
            detail: "If yes, please list them and the dose"),

        Question(ChronicConditions,
            "Diabetes, high blood pressure, or respiratory conditions?",
            AlertKind.MedicalCondition, AlertSeverity.Warning, order: 2,
            detail: "If yes, please give details"),

        Question(Allergies,
            "Any allergies to medications or materials?",
            AlertKind.Allergy, AlertSeverity.Critical, order: 3,
            detail: "If yes, please list them"),

        Question(Medications,
            "Are you taking any other medications?",
            AlertKind.Medication, AlertSeverity.Warning, order: 4,
            detail: "If yes, please list them and the dose"),

        Question(Pregnancy,
            "Are you pregnant or breastfeeding?",
            AlertKind.Pregnancy, AlertSeverity.Warning, order: 5),

        // No alert kind: smoking changes healing and periodontal risk, which the clinician
        // reads off the history. It is not something that should interrupt prescribing,
        // and putting it on the alert banner would dilute the banner.
        Question(Smoking, "Do you smoke?", kind: null, AlertSeverity.Information, order: 6),
    ];

    private static MedicalHistoryQuestion Question(
        string code,
        string text,
        AlertKind? kind,
        AlertSeverity severity,
        int order,
        string? detail = null) =>
        new()
        {
            Code = code,
            Text = text,
            DetailPrompt = detail,
            AlertKind = kind,
            Severity = severity,
            PromptsForDetail = detail is { Length: > 0 },
            DisplayOrder = order,
            Version = FirstVersion,
        };

    /// <summary>The label on the detail box, where the practice has not written one.</summary>
    public static string DetailPromptFor(MedicalHistoryQuestion question) =>
        question.DetailPrompt is { Length: > 0 } prompt
            ? prompt
            : "If yes, please give details";
}
