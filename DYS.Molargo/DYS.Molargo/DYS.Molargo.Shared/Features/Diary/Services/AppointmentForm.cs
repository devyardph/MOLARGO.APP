using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Shared.Features.Diary.Services;

/// <summary>
/// What the appointment form holds while it is being filled in.
/// </summary>
/// <remarks>
/// <para>
/// A mutable form model, not the entity. The entity carries audit stamps, a soft-delete
/// flag and the arrival timestamps that no booking form should be able to set. This is the
/// half-finished state of a person typing, which is a different thing.
/// </para>
/// <para>
/// Date and time are typed rather than strings, unlike <c>PatientForm</c>'s date of birth.
/// That form is a free-text field where half a date has to survive between keystrokes;
/// these are native date and time pickers, which only ever hand back a whole value or
/// nothing — so there is no half-typed state to preserve, and holding strings would just
/// mean parsing the browser's own output back again.
/// </para>
/// </remarks>
public sealed class AppointmentForm
{
    /// <summary>
    /// The wire format a native date input uses.
    /// </summary>
    /// <remarks>
    /// ISO, and never seen by a clinician: the browser renders the picker in its own
    /// locale's order. This is only what crosses the boundary between the input element
    /// and the binder, which is why it does not follow the app's display convention.
    /// </remarks>
    public const string DateInputFormat = "yyyy-MM-dd";

    /// <summary>The wire format a native time input uses. Twenty-four hour.</summary>
    public const string TimeInputFormat = "HH:mm";

    /// <summary>Empty for a new booking; the existing id when editing.</summary>
    public Guid Id { get; set; }

    public Guid PracticeLocationId { get; set; }

    /// <summary>Empty until a patient is picked from the search results.</summary>
    public Guid PatientId { get; set; }

    /// <summary>The chosen patient as the form shows them — "Margaret Yuen · #10201".</summary>
    public string PatientLabel { get; set; } = string.Empty;

    public Guid? AppointmentTypeId { get; set; }

    public Guid ProviderId { get; set; }

    public Guid? OperatoryId { get; set; }

    /// <summary>Null until a date is chosen.</summary>
    public DateOnly? Date { get; set; }

    /// <summary>Null until a time is chosen.</summary>
    public TimeOnly? Time { get; set; }

    public int DurationMinutes { get; set; } = 30;

    /// <summary>
    /// What the visit is for — "Crown fit 46". Shown as the second line of the diary block.
    /// </summary>
    /// <remarks>
    /// Not a field the design's form has, but one its own diary depends on: every block in
    /// the prototype is labelled with something more specific than its appointment type,
    /// and without this the whole day reads "Exam &amp; clean" six times over. Optional —
    /// the type's name stands in when it is blank.
    /// </remarks>
    public string? Reason { get; set; }

    /// <summary>Prep instructions — "premed 1h before", "interpreter booked".</summary>
    public string? Notes { get; set; }

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Scheduled;

    public bool IsNew => Id == Guid.Empty;

    /// <summary>The date and time as one local value, or null while either is unset.</summary>
    public DateTime? StartLocal => Date is { } date && Time is { } time
        ? date.ToDateTime(time, DateTimeKind.Local)
        : null;

    public static AppointmentForm FromEntity(Appointment appointment, string patientLabel)
    {
        var startLocal = appointment.StartUtc.ToLocalTime();

        return new AppointmentForm
        {
            Id = appointment.Id,
            PracticeLocationId = appointment.PracticeLocationId,
            PatientId = appointment.PatientId,
            PatientLabel = patientLabel,
            AppointmentTypeId = appointment.AppointmentTypeId,
            ProviderId = appointment.ProviderId,
            OperatoryId = appointment.OperatoryId,
            Date = DateOnly.FromDateTime(startLocal),
            Time = TimeOnly.FromDateTime(startLocal),
            DurationMinutes = appointment.DurationMinutes,
            Reason = appointment.Reason,
            Notes = appointment.Notes,
            Status = appointment.Status,
        };
    }

    /// <summary>
    /// Copies the form onto the entity.
    /// </summary>
    /// <remarks>
    /// Only the fields the form owns. The arrival and completion timestamps, the
    /// cancellation reason and the waitlist flag are deliberately untouched: editing the
    /// note on a patient who is already in the chair must not un-arrive them.
    /// </remarks>
    public void ApplyTo(Appointment appointment)
    {
        appointment.PracticeLocationId = PracticeLocationId;
        appointment.PatientId = PatientId;
        appointment.AppointmentTypeId = AppointmentTypeId;
        appointment.ProviderId = ProviderId;
        appointment.OperatoryId = OperatoryId;
        appointment.DurationMinutes = DurationMinutes;
        appointment.Reason = Blank(Reason);
        appointment.Notes = Blank(Notes);

        if (StartLocal is { } startLocal)
        {
            appointment.StartUtc = DateTime
                .SpecifyKind(startLocal, DateTimeKind.Local)
                .ToUniversalTime();
        }
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>An appointment type as the form's picker shows it.</summary>
/// <param name="Sub">"45 min · $340" style caption — the default duration.</param>
public sealed record AppointmentTypeOption(
    Guid Id, string Name, string Sub, int DefaultDurationMinutes, string? Colour);

/// <summary>A clinician the booking can be given to.</summary>
/// <param name="Colour">Their diary colour, for the swatch on the button.</param>
public sealed record ProviderOption(Guid Id, string Name, string? Role, string? Colour);

/// <summary>A chair the booking can be placed in.</summary>
public sealed record ChairOption(Guid Id, string Name, bool IsSurgical);

/// <summary>
/// Everything the booking form's pickers offer, for one location.
/// </summary>
/// <remarks>
/// Providers and chairs are two lists, not one list of pairings. Pairing them produced a
/// button per combination — three clinicians and three chairs came out as nine boxes
/// saying "Dr Ellery · Chair 1", "Dr Ellery · Chair 2" and so on, and a six-chair practice
/// with five clinicians would have thirty. The two choices are independent: who is
/// treating, and where. Asking them separately is two clicks instead of one and scales by
/// addition rather than multiplication.
/// </remarks>
public sealed class AppointmentOptions
{
    public IReadOnlyList<AppointmentTypeOption> Types { get; init; } = [];

    public IReadOnlyList<ProviderOption> Providers { get; init; } = [];

    public IReadOnlyList<ChairOption> Chairs { get; init; } = [];
}

/// <summary>One appointment already in the chosen chair that overlaps this one.</summary>
public sealed record BookingClash(
    Guid AppointmentId, string PatientName, DateTime StartLocal, int Minutes);

/// <summary>
/// The design's pre-booking checks — what the front desk should know before committing
/// the slot.
/// </summary>
/// <remarks>
/// Every item here is read from the record. The design's panel also promises a reminder
/// cadence, which needs a messaging gateway that does not exist; the screen says so rather
/// than showing a cadence nothing would honour.
/// </remarks>
public sealed class PreBookingChecks
{
    /// <summary>Unresolved alerts — allergies, anticoagulants, infection risk.</summary>
    public IReadOnlyList<PatientAlert> Alerts { get; init; } = [];

    /// <summary>Lab work outstanding for this patient, so a fit is not booked too early.</summary>
    public IReadOnlyList<LabCase> LabCases { get; init; } = [];

    public decimal Balance { get; init; }

    public int FailedToAttendCount { get; init; }

    /// <summary>Bookings already in the chosen chair at this time.</summary>
    public IReadOnlyList<BookingClash> Clashes { get; init; } = [];

    public bool HasCriticalAlert =>
        Alerts.Any(alert => alert.Severity == AlertSeverity.Critical);
}

/// <summary>
/// The outcome of a save.
/// </summary>
/// <param name="Errors">
/// Field name to message. Empty on success — a save either wrote or it did not.
/// </param>
public sealed record AppointmentSaveResult(
    Guid AppointmentId,
    IReadOnlyDictionary<string, string> Errors)
{
    public bool Succeeded => Errors.Count == 0;
}
