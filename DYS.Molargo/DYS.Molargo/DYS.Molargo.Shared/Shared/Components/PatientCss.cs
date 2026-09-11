using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Shared.Components;

/// <summary>
/// Maps the patient domain enums onto the design system's classes and labels.
/// </summary>
/// <remarks>
/// <para>
/// Here rather than in the views because a status chip appears on the patients list, the
/// record header, the diary and the check-in kiosk — four places that have to agree, and
/// did not while each carried its own switch.
/// </para>
/// <para>
/// The class names returned are whole literal strings, never assembled from fragments.
/// Tailwind discovers classes by scanning source text, so a name built by concatenation
/// (<c>"tag-" + tone</c>) is invisible to the compiler and the utility is simply absent
/// from the stylesheet — with no error anywhere to say so.
/// </para>
/// </remarks>
public static class PatientCss
{
    /// <summary>The chip classes for a lifecycle status.</summary>
    public static string StatusTag(PatientStatus status) => status switch
    {
        PatientStatus.Lead => "tag tag-accent2",
        PatientStatus.RecallDue => "tag tag-accent",
        PatientStatus.Archived => "tag tag-outline",
        _ => "tag tag-neutral",
    };

    /// <summary>The label for a lifecycle status. Enum member names are not user-facing text.</summary>
    public static string StatusLabel(PatientStatus status) => status switch
    {
        PatientStatus.Active => "Active",
        PatientStatus.Lead => "Lead",
        PatientStatus.RecallDue => "Recall due",
        PatientStatus.Archived => "Archived",
        _ => string.Empty,
    };

    /// <summary>
    /// The flags set on a patient, as a list, for rendering chips. Ordered by the enum's
    /// declaration so a patient's chips do not reshuffle between renders.
    /// </summary>
    public static IReadOnlyList<PatientTags> TagList(PatientTags tags) =>
        Enum.GetValues<PatientTags>()
            .Where(tag => tag != PatientTags.None && tags.HasFlag(tag))
            .ToList();

    /// <summary>
    /// The label for one operational flag. Spelled out rather than derived from the enum
    /// member name, so "Vip" reads as "VIP" and "InterpreterNeeded" as something a
    /// receptionist would actually say.
    /// </summary>
    public static string TagLabel(PatientTags tag) => tag switch
    {
        PatientTags.Vip => "VIP",
        PatientTags.NervousPatient => "Nervous patient",
        PatientTags.HighRisk => "High-risk",
        PatientTags.InterpreterNeeded => "Interpreter needed",
        PatientTags.Debtor => "Debtor",
        _ => string.Empty,
    };

    /// <summary>
    /// A balance is emphasised only when something is owed. Every row showing a bold
    /// coloured "$0" trains people to ignore the column that matters.
    /// </summary>
    public static string BalanceCss(decimal balance) =>
        balance > 0m ? "font-bold text-accent-700" : string.Empty;
}
