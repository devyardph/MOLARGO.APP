using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Features.Patient.Services;

/// <summary>One line of the billing tab: what was charged, and how it was paid for.</summary>
/// <param name="FundBenefit">Approved by the fund or Medicare against this invoice.</param>
/// <param name="PatientPortion">The gap: fee less benefit.</param>
public sealed record BillingRow(
    DateOnly ServiceDate,
    string ItemNumber,
    string Description,
    string? ToothNumber,
    decimal Fee,
    decimal FundBenefit,
    decimal PatientPortion,
    InvoiceStatus InvoiceStatus,
    decimal InvoiceOutstanding,
    string? InvoiceNumber);

/// <summary>One line of the "treatment in progress" table.</summary>
/// <param name="NextVisitUtc">
/// When the item is booked for, or null where nothing is booked. Deliberately the raw
/// timestamp rather than a rendered string: formatting belongs to the view, which is
/// where the pinned culture lives — building it here put an unpinned "11 Sept" on screen.
/// </param>
public sealed record TreatmentProgressRow(
    string Description,
    string? ToothNumber,
    TreatmentItemStatus Status,
    DateTime? NextVisitUtc);

/// <summary>
/// Everything the patient record screen needs, fetched in one round trip.
/// </summary>
/// <remarks>
/// One aggregate rather than a dozen separate awaits from the view model. The record
/// screen has six tabs over ten tables; loading them independently means ten loading
/// states and ten ways for a clinical record to be partly wrong on screen — which, on a
/// screen someone makes a prescribing decision from, is worse than being slow.
/// </remarks>
public sealed class PatientRecord
{
    public required PatientEntity Patient { get; init; }

    public IReadOnlyList<PatientAlert> Alerts { get; init; } = [];

    public IReadOnlyList<PatientEntity> Household { get; init; } = [];

    public IReadOnlyList<TreatmentPlan> Plans { get; init; } = [];

    public IReadOnlyList<TreatmentPlanItem> PlanItems { get; init; } = [];

    public IReadOnlyList<Appointment> Appointments { get; init; } = [];

    public IReadOnlyList<PatientDocument> Documents { get; init; } = [];

    public IReadOnlyList<ConsentForm> Consents { get; init; } = [];

    public IReadOnlyList<CommunicationLog> Communications { get; init; } = [];

    public IReadOnlyList<BillingRow> Billing { get; init; } = [];

    public IReadOnlyList<Recall> Recalls { get; init; } = [];

    /// <summary>The most recent completed questionnaire, or null if there has never been one.</summary>
    public MedicalHistoryForm? MedicalHistory { get; init; }

    /// <summary>
    /// The answers to <see cref="MedicalHistory"/>, in the order the questionnaire asks
    /// them. Only that one form is loaded: earlier completions are kept in the database
    /// as clinical fact, but the record screen shows the current picture.
    /// </summary>
    public IReadOnlyList<MedicalHistoryAnswer> MedicalHistoryAnswers { get; init; } = [];

    /// <summary>Provider display names by id, for tables that name a clinician.</summary>
    public IReadOnlyDictionary<Guid, string> ProviderNames { get; init; } =
        new Dictionary<Guid, string>();

    // ---- derived ---------------------------------------------------------

    /// <summary>
    /// Alerts that must interrupt — the allergies and anticoagulants the header band
    /// shows. Ordered so the criticals lead.
    /// </summary>
    public IEnumerable<PatientAlert> CriticalAlerts =>
        Alerts.Where(alert => alert is { Severity: AlertSeverity.Critical, IsResolved: false });

    public IEnumerable<PatientAlert> Allergies =>
        Alerts.Where(alert => alert is { Kind: AlertKind.Allergy, IsResolved: false });

    public IEnumerable<PatientAlert> Medications =>
        Alerts.Where(alert => alert is { Kind: AlertKind.Medication, IsResolved: false });

    public IEnumerable<PatientAlert> Conditions =>
        Alerts.Where(alert => alert is { Kind: AlertKind.MedicalCondition, IsResolved: false });

    /// <summary>
    /// True when the medical history needs re-confirming. The prompt on the overview tab.
    /// </summary>
    /// <remarks>
    /// Twelve months is the usual practice interval, and "never completed" also counts —
    /// a patient with no history on file is exactly the one who should not be treated
    /// before it is taken.
    /// </remarks>
    public bool IsMedicalHistoryOverdue(DateOnly today) =>
        MedicalHistory?.CompletedUtc is not { } completed
        || DateOnly.FromDateTime(completed.ToLocalTime()).AddMonths(12) < today;

    /// <summary>Future visits, soonest first.</summary>
    public IEnumerable<Appointment> UpcomingAppointments(DateTime asOfUtc) =>
        Appointments
            .Where(appointment => appointment.StartUtc >= asOfUtc
                && appointment.Status is not (AppointmentStatus.Cancelled or AppointmentStatus.FailedToAttend))
            .OrderBy(appointment => appointment.StartUtc);

    /// <summary>Past visits, most recent first.</summary>
    public IEnumerable<Appointment> PastAppointments(DateTime asOfUtc) =>
        Appointments
            .Where(appointment => appointment.StartUtc < asOfUtc)
            .OrderByDescending(appointment => appointment.StartUtc);

    /// <summary>The next visit, for the header band's figure.</summary>
    public Appointment? NextAppointment(DateTime asOfUtc) => UpcomingAppointments(asOfUtc).FirstOrDefault();

    /// <summary>Plans the patient can still act on, recommended first.</summary>
    public IEnumerable<TreatmentPlan> OpenPlans =>
        Plans
            .Where(plan => plan.Status is not (TreatmentPlanStatus.Completed or TreatmentPlanStatus.Void))
            .OrderByDescending(plan => plan.IsRecommended)
            .ThenBy(plan => plan.DisplayOrder);

    /// <summary>Items on their way through — the "treatment in progress" table.</summary>
    /// <remarks>
    /// Planned items are excluded: a plan the patient has not accepted is not treatment in
    /// progress, and listing it as such is how a practice ends up believing work is under
    /// way that was never agreed.
    /// </remarks>
    public IEnumerable<TreatmentProgressRow> InProgress()
    {
        var byAppointment = Appointments.ToDictionary(appointment => appointment.Id);

        return PlanItems
            .Where(item => item.Status is TreatmentItemStatus.Scheduled or TreatmentItemStatus.Completed)
            .OrderBy(item => item.StageNumber)
            .ThenBy(item => item.DisplayOrder)
            .Select(item => new TreatmentProgressRow(
                item.Description,
                item.ToothNumber,
                item.Status,
                item.AppointmentId is { } id && byAppointment.TryGetValue(id, out var appointment)
                    ? appointment.StartUtc
                    : null));
    }

    /// <summary>Outstanding across every issued invoice. Should agree with the patient's balance.</summary>
    public decimal Outstanding => Billing.Sum(row => row.InvoiceOutstanding);
}
