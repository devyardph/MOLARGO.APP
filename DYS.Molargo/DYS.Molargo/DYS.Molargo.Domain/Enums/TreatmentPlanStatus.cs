namespace DYS.Molargo.Domain.Enums;

/// <summary>Where a treatment plan sits between proposal and completion.</summary>
public enum TreatmentPlanStatus
{
    /// <summary>Being built. Not yet shown to the patient.</summary>
    Draft = 0,

    /// <summary>Presented, with the estimate. Awaiting a decision.</summary>
    Presented = 1,

    /// <summary>Accepted but with nothing booked. The practice's unscheduled-plan worklist.</summary>
    Accepted = 2,

    /// <summary>Accepted and at least partly booked or delivered.</summary>
    InProgress = 3,

    Completed = 4,

    /// <summary>The patient declined. Kept for the clinical record.</summary>
    Declined = 5,

    /// <summary>Superseded by a newer plan, or no longer clinically appropriate.</summary>
    Void = 6,
}
