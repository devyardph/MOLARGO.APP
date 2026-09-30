namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A course of treatment several appointments belong to — "Root canal 16, 1–3".
/// </summary>
/// <remarks>
/// <para>
/// The thing the booking form's series toggle was waiting for. Three appointments that
/// merely share a patient and a reason are not a course: nothing can say which is first,
/// nothing notices when the second is cancelled and the third still stands, and the diary
/// has no way to label a block "visit 2 of 3".
/// </para>
/// <para>
/// Deliberately thin. It carries no fees, no consent and no clinical content, because a
/// <see cref="TreatmentPlan"/> already owns all of that and a course booked at the front
/// desk — RCT 1–3, implant phases — must not have to be a presented, accepted plan first.
/// Where a series <em>does</em> come from a plan, <see cref="TreatmentPlanId"/> says so and
/// the plan remains the clinical record; this stays the scheduling side of it.
/// </para>
/// </remarks>
public sealed class AppointmentSeries : EntityBase
{
    public Guid PatientId { get; set; }

    /// <summary>
    /// What the course is called — "Root canal 16".
    /// </summary>
    /// <remarks>
    /// Its own name rather than the appointment type's. A course of three visits is one
    /// piece of work with one name, and the visits inside it are often of different types:
    /// an RCT series is an emergency, then two long appointments.
    /// </remarks>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// How many visits the course was booked as.
    /// </summary>
    /// <remarks>
    /// Stored rather than counted from the appointments, so "visit 2 of 3" keeps saying
    /// "of 3" after one is cancelled. A course that silently became "2 of 2" would hide
    /// exactly the fact worth seeing — that a visit is missing.
    /// </remarks>
    public int PlannedVisits { get; set; }

    /// <summary>The plan this course delivers, where it came from one.</summary>
    public Guid? TreatmentPlanId { get; set; }

    /// <summary>Why the course was booked, if the front desk said.</summary>
    public string? Notes { get; set; }
}
