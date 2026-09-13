using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Dtos;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Features.Patient.Services;
using DYS.Molargo.Shared.Features.Treatment.Services;
using DYS.Molargo.Shared.Repositories;
using DYS.Molargo.Shared.Services;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Features.Diary.Services;

/// <summary>
/// Booking and amending one appointment: the pickers the form offers, the pre-booking
/// checks, the save and the cancellation.
/// </summary>
public interface IAppointmentService
{
    /// <summary>The appointment types and provider/chair pairings this location offers.</summary>
    Task<AppointmentOptions> GetOptionsAsync(Guid locationId, CancellationToken ct = default);

    /// <summary>An existing booking as a form, or null if there is no such appointment.</summary>
    Task<AppointmentForm?> GetFormAsync(Guid appointmentId, CancellationToken ct = default);

    /// <summary>
    /// Patients matching what has been typed, for the form's search box. Capped short.
    /// </summary>
    Task<IReadOnlyList<PatientListItemDto>> SearchPatientsAsync(
        string term, CancellationToken ct = default);

    /// <summary>How the chosen patient should be labelled once picked.</summary>
    Task<string?> GetPatientLabelAsync(Guid patientId, CancellationToken ct = default);

    /// <summary>
    /// The pre-booking checks for the form as it currently stands.
    /// </summary>
    /// <remarks>
    /// Takes the whole form rather than a patient id, because the clash check depends on
    /// the chair and the slot as well as the patient.
    /// </remarks>
    Task<PreBookingChecks> GetChecksAsync(AppointmentForm form, CancellationToken ct = default);

    /// <summary>
    /// Writes the booking, or returns the reasons it cannot be written.
    /// </summary>
    Task<AppointmentSaveResult> SaveAsync(AppointmentForm form, CancellationToken ct = default);

    /// <summary>
    /// Cancels a booking, keeping the row and the reason.
    /// </summary>
    /// <param name="failedToAttend">
    /// True where the patient simply did not arrive. Counted against them and reported
    /// separately, because it costs the practice the slot where a cancellation with notice
    /// does not.
    /// </param>
    Task<string?> CancelAsync(
        Guid appointmentId,
        string? reason,
        bool failedToAttend = false,
        CancellationToken ct = default);
}

/// <inheritdoc cref="IAppointmentService"/>
public sealed class AppointmentService : IAppointmentService
{
    /// <summary>
    /// How many patients the search box offers. Short on purpose: the box is for
    /// identifying one known person, and a list long enough to scroll means the front desk
    /// should be typing more of the name rather than reading.
    /// </summary>
    private const int SearchResultLimit = 8;

    private readonly IRepository<Appointment> _appointments;
    private readonly IRepository<PatientEntity> _patients;
    private readonly IRepository<Provider> _providers;
    private readonly IRepository<AppointmentType> _appointmentTypes;
    private readonly IRepository<Operatory> _operatories;
    private readonly IRepository<PatientAlert> _alerts;
    private readonly IRepository<LabCase> _labCases;
    private readonly IPatientService _patientService;

    /// <summary>Only to link and release the plan items a booking delivers.</summary>
    /// <remarks>
    /// The dependency points this way, not the other. A plan owns whether its items are
    /// booked; the diary owns the slot. Having the plan service reach into appointments
    /// instead would put two owners on the same question.
    /// </remarks>
    private readonly ITreatmentPlanService _plans;

    /// <summary>Only for the selected site's trading days and hours.</summary>
    private readonly ISessionService _session;

    public AppointmentService(
        IRepository<Appointment> appointments,
        IRepository<PatientEntity> patients,
        IRepository<Provider> providers,
        IRepository<AppointmentType> appointmentTypes,
        IRepository<Operatory> operatories,
        IRepository<PatientAlert> alerts,
        IRepository<LabCase> labCases,
        IPatientService patientService,
        ITreatmentPlanService plans,
        ISessionService session)
    {
        _appointments = appointments;
        _patients = patients;
        _providers = providers;
        _appointmentTypes = appointmentTypes;
        _operatories = operatories;
        _alerts = alerts;
        _labCases = labCases;
        _patientService = patientService;
        _plans = plans;
        _session = session;
    }

    public async Task<AppointmentOptions> GetOptionsAsync(
        Guid locationId, CancellationToken ct = default)
    {
        var types = await _appointmentTypes
            .ListAsync(type => type.IsActive, ct)
            .ConfigureAwait(false);

        var operatories = await _operatories
            .ListAsync(chair => chair.PracticeLocationId == locationId && chair.IsActive, ct)
            .ConfigureAwait(false);

        var providers = await _providers.ListAsync(ct: ct).ConfigureAwait(false);

        // Clinical roles only. A receptionist cannot be booked into a chair, and offering
        // one is how a slot ends up owned by nobody who can treat.
        var clinical = providers
            .Where(provider => ProviderRoles.IsBookable(provider.Role))
            .OrderBy(provider => provider.LastName)
            .ToList();

        return new AppointmentOptions
        {
            Types = types
                .OrderBy(type => type.Name)
                .Select(type => new AppointmentTypeOption(
                    type.Id,
                    type.Name,
                    $"{type.DefaultDurationMinutes} min",
                    type.DefaultDurationMinutes,
                    type.Colour))
                .ToList(),

            Providers = clinical
                .Select(provider => new ProviderOption(
                    provider.Id,
                    provider.DisplayName is { Length: > 0 } display
                        ? display
                        : provider.FullName,
                    RoleLabel(provider.Role),
                    provider.DiaryColour,

                    // Carried so the picker can mark who is not in on the chosen day.
                    // Everyone is still offered — see the note on the form's availability
                    // warning for why this is not a filter.
                    provider.WorkingDays,
                    provider.WorkingFrom,
                    provider.WorkingTo))
                .ToList(),

            // In diary order, so the buttons read left to right in the same order as the
            // columns the booking will appear in.
            Chairs = operatories
                .OrderBy(chair => chair.DisplayOrder)
                .ThenBy(chair => chair.Name)
                .Select(chair => new ChairOption(chair.Id, chair.Name, chair.IsSurgical))
                .ToList(),
        };
    }

    public async Task<AppointmentForm?> GetFormAsync(
        Guid appointmentId, CancellationToken ct = default)
    {
        var appointment = await _appointments.GetByIdAsync(appointmentId, ct).ConfigureAwait(false);
        if (appointment is null) return null;

        var label = await GetPatientLabelAsync(appointment.PatientId, ct).ConfigureAwait(false);

        return AppointmentForm.FromEntity(appointment, label ?? "Unknown patient");
    }

    public async Task<IReadOnlyList<PatientListItemDto>> SearchPatientsAsync(
        string term, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(term)) return [];

        var page = await _patientService
            .SearchAsync(new PatientQuery { SearchTerm = term.Trim(), PageSize = SearchResultLimit }, ct)
            .ConfigureAwait(false);

        return page.Items;
    }

    public async Task<string?> GetPatientLabelAsync(
        Guid patientId, CancellationToken ct = default)
    {
        var patient = await _patients.GetByIdAsync(patientId, ct).ConfigureAwait(false);

        if (patient is null) return null;

        return patient.PatientNumber is { Length: > 0 } number
            ? $"{patient.FullName} · #{number}"
            : patient.FullName;
    }

    public async Task<PreBookingChecks> GetChecksAsync(
        AppointmentForm form, CancellationToken ct = default)
    {
        if (form.PatientId == Guid.Empty) return new PreBookingChecks();

        var patient = await _patients.GetByIdAsync(form.PatientId, ct).ConfigureAwait(false);
        if (patient is null) return new PreBookingChecks();

        var alerts = await _alerts
            .ListAsync(alert => alert.PatientId == form.PatientId
                && alert.ResolvedDate == null, ct)
            .ConfigureAwait(false);

        // Not yet fitted, not cancelled. A case already in the patient's mouth is not a
        // reason to hesitate over the next booking.
        var labCases = await _labCases
            .ListAsync(labCase => labCase.PatientId == form.PatientId
                && labCase.Status != LabCaseStatus.Fitted
                && labCase.Status != LabCaseStatus.Cancelled, ct)
            .ConfigureAwait(false);

        return new PreBookingChecks
        {
            Alerts = alerts
                .OrderByDescending(alert => alert.Severity)
                .ThenBy(alert => alert.Summary)
                .ToList(),
            LabCases = labCases.OrderBy(labCase => labCase.DueOn).ToList(),
            Balance = patient.Balance,
            FailedToAttendCount = patient.FailedToAttendCount,
            Clashes = await FindClashesAsync(form, ct).ConfigureAwait(false),
        };
    }

    public async Task<AppointmentSaveResult> SaveAsync(
        AppointmentForm form, CancellationToken ct = default)
    {
        var errors = Validate(form);

        if (errors.Count > 0) return new AppointmentSaveResult(form.Id, errors);

        var appointment = form.IsNew
            ? new Appointment()
            : await _appointments.GetByIdAsync(form.Id, ct).ConfigureAwait(false);

        if (appointment is null)
        {
            return new AppointmentSaveResult(form.Id, new Dictionary<string, string>
            {
                [string.Empty] = "That appointment no longer exists. It may have been cancelled.",
            });
        }

        // A cancelled booking is not edited back to life. Its cancellation reason and the
        // patient's FTA count are already recorded against it, and reviving the row would
        // leave both describing a booking that went ahead.
        if (!form.IsNew && AppointmentProgress.IsLostSlot(appointment.Status))
        {
            return new AppointmentSaveResult(form.Id, new Dictionary<string, string>
            {
                [string.Empty] =
                    "That appointment was cancelled or missed and cannot be edited. Book a new one.",
            });
        }

        form.ApplyTo(appointment);

        await _appointments.SaveAsync(appointment, ct).ConfigureAwait(false);

        // The plan link, once the appointment has an id. After the save rather than before
        // it, because a plan item pointing at an appointment that was never written is a
        // dangling reference that nothing would ever notice.
        if (form.IsPlanVisit)
        {
            var refusal = await _plans
                .LinkVisitAsync(
                    form.TreatmentPlanId!.Value, form.PlanStageNumber!.Value, appointment.Id, ct)
                .ConfigureAwait(false);

            // Reported as a field error rather than thrown away. The booking itself stands
            // — the slot is genuinely taken — so the refusal has to say that the plan was
            // not updated, instead of a screen claiming a plan visit is booked when the
            // plan does not think so.
            if (refusal is { Length: > 0 })
            {
                return new AppointmentSaveResult(
                    appointment.Id,
                    new Dictionary<string, string>
                    {
                        [string.Empty] = $"The appointment is booked, but the plan was not "
                            + $"updated: {refusal}",
                    });
            }
        }

        return new AppointmentSaveResult(appointment.Id, new Dictionary<string, string>());
    }

    public async Task<string?> CancelAsync(
        Guid appointmentId,
        string? reason,
        bool failedToAttend = false,
        CancellationToken ct = default)
    {
        var appointment = await _appointments.GetByIdAsync(appointmentId, ct).ConfigureAwait(false);
        if (appointment is null) return "That appointment no longer exists.";

        if (AppointmentProgress.IsLostSlot(appointment.Status))
        {
            return "That appointment is already cancelled.";
        }

        appointment.Status = failedToAttend
            ? AppointmentStatus.FailedToAttend
            : AppointmentStatus.Cancelled;

        appointment.CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        await _appointments.SaveAsync(appointment, ct).ConfigureAwait(false);

        // Whatever plan treatment was booked into this slot goes back to needing a booking.
        // Without this the unscheduled-treatment worklist breaks the other way round —
        // work counted as booked against a slot that no longer exists, which nothing would
        // ever surface again. Completed items are left alone: cancelling a later
        // appointment does not un-do a filling.
        await _plans.UnlinkAppointmentAsync(appointmentId, ct).ConfigureAwait(false);

        // The count lives on the patient because it is the patient's history: it decides
        // whether the next booking needs a deposit, and it has to survive the appointment
        // row being archived.
        if (failedToAttend)
        {
            var patient = await _patients
                .GetByIdAsync(appointment.PatientId, ct)
                .ConfigureAwait(false);

            if (patient is not null)
            {
                patient.FailedToAttendCount += 1;
                await _patients.SaveAsync(patient, ct).ConfigureAwait(false);
            }
        }

        return null;
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// What is wrong with the form, keyed by field.
    /// </summary>
    /// <remarks>
    /// In the service rather than the view model, which is where <c>PatientForm</c>'s
    /// validation lives. The difference is that these rules are shared: the slot rules
    /// also govern a drag-reschedule, which never goes near this form. Keeping them here
    /// means one definition rather than a copy per entry point.
    /// </remarks>
    /// <summary>Not static any more: the opening hours it checks against are the
    /// selected site's, which only the session knows.</summary>
    private Dictionary<string, string> Validate(AppointmentForm form)
    {
        var errors = new Dictionary<string, string>();

        if (form.PatientId == Guid.Empty)
        {
            errors[nameof(AppointmentForm.PatientId)] = "Choose a patient.";
        }

        if (form.ProviderId == Guid.Empty)
        {
            errors[nameof(AppointmentForm.ProviderId)] = "Choose a provider.";
        }

        // Required, though the column allows null. An appointment with no chair does not
        // appear in the day grid at all — it lands in the "not on the grid" band — so
        // making that the default outcome of the booking form would be perverse.
        if (form.OperatoryId is null)
        {
            errors[nameof(AppointmentForm.OperatoryId)] = "Choose a chair.";
        }

        if (form.Date is null) errors[nameof(AppointmentForm.Date)] = "Choose a date.";

        if (form.Time is null) errors[nameof(AppointmentForm.Time)] = "Choose a time.";

        // Keyed to the slot, not to the time. "The practice is closed that day" is about
        // the date, "that runs outside opening hours" is about all three of date, time and
        // length — so neither belongs under the 140px time box, where it wrapped onto two
        // lines and blamed the one field that was not at fault.
        if (form.StartLocal is { } startLocal
            && _session.Hours.RefuseSlot(startLocal, form.DurationMinutes) is { } refusal)
        {
            errors[nameof(AppointmentForm.StartLocal)] = refusal;
        }

        return errors;
    }

    /// <summary>
    /// Bookings that overlap this one — in the chosen chair, or with the chosen clinician.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A warning, never a refusal — see the note on <c>DiaryService.MoveAsync</c> for why
    /// double-booking has to remain possible. The appointment being edited is excluded, or
    /// every save would report the booking clashing with itself.
    /// </para>
    /// <para>
    /// The clinician half was missing. The query filtered on the chair alone, so a dentist
    /// could be booked into Chair 1 and Chair 2 for the same hour and the checks panel said
    /// nothing at all — while the comment on <c>SelectChairCommand</c> claimed the provider
    /// was being checked for exactly that. One day's appointments are read once and both
    /// collisions are found in memory, rather than issuing a second query for a set that
    /// overlaps the first almost entirely.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<BookingClash>> FindClashesAsync(
        AppointmentForm form, CancellationToken ct)
    {
        if (form.StartLocal is not { } startLocal) return [];

        var chairId = form.OperatoryId;
        var providerId = form.ProviderId;

        if (chairId is null && providerId == Guid.Empty) return [];

        var dayStartUtc = DateOnly.FromDateTime(startLocal)
            .ToDateTime(TimeOnly.MinValue, DateTimeKind.Local)
            .ToUniversalTime();

        var dayEndUtc = dayStartUtc.AddDays(1);

        var sameDay = await _appointments
            .ListAsync(appointment => appointment.StartUtc >= dayStartUtc
                && appointment.StartUtc < dayEndUtc
                && (appointment.OperatoryId == chairId
                    || appointment.ProviderId == providerId), ct)
            .ConfigureAwait(false);

        var startUtc = DateTime.SpecifyKind(startLocal, DateTimeKind.Local).ToUniversalTime();
        var endUtc = startUtc.AddMinutes(form.DurationMinutes);

        var overlapping = sameDay
            .Where(appointment => appointment.Id != form.Id
                && !AppointmentProgress.IsLostSlot(appointment.Status)
                && appointment.StartUtc < endUtc
                && appointment.EndUtc > startUtc)
            .OrderBy(appointment => appointment.StartUtc)
            .ToList();

        if (overlapping.Count == 0) return [];

        var patientIds = overlapping.Select(appointment => appointment.PatientId).ToHashSet();

        var patients = await _patients
            .ListAsync(patient => patientIds.Contains(patient.Id), ct)
            .ConfigureAwait(false);

        var names = patients.ToDictionary(patient => patient.Id, patient => patient.FullName);

        return overlapping
            .Select(appointment => new BookingClash(
                appointment.Id,
                names.GetValueOrDefault(appointment.PatientId, "Unknown patient"),
                appointment.StartUtc.ToLocalTime(),
                appointment.DurationMinutes,

                // Chair wins where a booking collides on both, because it is the one the
                // person can fix by moving chairs. Reporting it twice would list the same
                // appointment under two headings and make one conflict look like two.
                chairId is not null && appointment.OperatoryId == chairId
                    ? ClashKind.Chair
                    : ClashKind.Provider))
            .ToList();
    }

    private static string RoleLabel(ProviderRole role) => role switch
    {
        ProviderRole.Dentist => "Dentist",
        ProviderRole.Hygienist => "Hygienist",
        ProviderRole.OralHealthTherapist => "Therapist",
        ProviderRole.DentalTherapist => "Therapist",
        ProviderRole.Prosthetist => "Prosthetist",
        ProviderRole.Specialist => "Specialist",
        _ => role.ToString(),
    };
}
