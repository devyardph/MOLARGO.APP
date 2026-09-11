namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// Where a patient sits in the practice's lifecycle. Drives the list filters and the
/// status chip on the patients screen.
/// </summary>
public enum PatientStatus
{
    /// <summary>On the books and eligible for recalls, reminders and claiming.</summary>
    Active = 0,

    /// <summary>An enquiry that has not yet attended — no clinical record exists.</summary>
    Lead = 1,

    /// <summary>Active, but past their recall interval. Called out separately because the
    /// front desk works this cohort as a worklist rather than browsing it.</summary>
    RecallDue = 2,

    /// <summary>
    /// Retained for medico-legal record-keeping but excluded from all outbound
    /// automation. Archiving is the closest thing to deletion the practice has: clinical
    /// records carry a statutory retention period, so rows are never actually removed.
    /// </summary>
    Archived = 3,
}
