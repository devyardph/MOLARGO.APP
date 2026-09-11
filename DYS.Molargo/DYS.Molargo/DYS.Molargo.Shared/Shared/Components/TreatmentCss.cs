using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Shared.Components;

/// <summary>
/// Maps the treatment-plan enums onto the design system's chips and the practice's own
/// words for them.
/// </summary>
/// <remarks>
/// The accent is spent on the states that need someone to act: a plan awaiting a decision,
/// an item booked but not yet delivered. Completed and draft are neutral — they are
/// resting states, and colouring them competes with the ones that are not.
/// </remarks>
public static class TreatmentCss
{
    public static string PlanTag(TreatmentPlanStatus status) => status switch
    {
        TreatmentPlanStatus.Presented => "tag tag-accent",
        TreatmentPlanStatus.Accepted or TreatmentPlanStatus.InProgress => "tag tag-accent2",
        TreatmentPlanStatus.Declined or TreatmentPlanStatus.Void => "tag tag-outline",
        _ => "tag tag-neutral",
    };

    public static string PlanLabel(TreatmentPlanStatus status) => status switch
    {
        TreatmentPlanStatus.Draft => "Draft",
        TreatmentPlanStatus.Presented => "Presented — awaiting acceptance",

        // "Accepted" alone reads as finished. What it means is that the practice owes the
        // patient a booking, which is the whole point of the unscheduled-plan worklist.
        TreatmentPlanStatus.Accepted => "Accepted — not booked",

        TreatmentPlanStatus.InProgress => "In progress",
        TreatmentPlanStatus.Completed => "Completed",
        TreatmentPlanStatus.Declined => "Declined",
        TreatmentPlanStatus.Void => "Superseded",
        _ => string.Empty,
    };

    public static string ItemTag(TreatmentItemStatus status) => status switch
    {
        TreatmentItemStatus.Scheduled => "tag tag-accent",
        TreatmentItemStatus.Declined or TreatmentItemStatus.Void => "tag tag-outline",
        _ => "tag tag-neutral",
    };

    public static string ItemLabel(TreatmentItemStatus status) => status switch
    {
        TreatmentItemStatus.Planned => "Planned",
        TreatmentItemStatus.Scheduled => "Booked",
        TreatmentItemStatus.Completed => "Done",
        TreatmentItemStatus.Declined => "Declined",
        TreatmentItemStatus.Void => "No longer needed",
        _ => string.Empty,
    };

    /// <summary>Consent is a treatment blocker, so an unsigned one is not neutral.</summary>
    public static string ConsentTag(ConsentStatus status) => status switch
    {
        ConsentStatus.Signed => "tag tag-neutral",
        ConsentStatus.Refused or ConsentStatus.Withdrawn => "tag tag-outline",
        _ => "tag tag-accent",
    };

    public static string ConsentLabel(ConsentStatus status) => status switch
    {
        ConsentStatus.Pending => "Awaiting signature",
        ConsentStatus.Signed => "Signed",
        ConsentStatus.Refused => "Refused",
        ConsentStatus.Withdrawn => "Withdrawn",
        ConsentStatus.Expired => "Expired",
        _ => string.Empty,
    };
}
