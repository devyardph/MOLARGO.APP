using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Repositories;
using DYS.Molargo.Shared.Services;

namespace DYS.Molargo.Shared.Features.Diary.Services;

/// <summary>
/// Keeps a patient's recall in step with the visits they actually have.
/// </summary>
/// <remarks>
/// <para>
/// The piece that was missing. Recalls were seeded and never created, so the worklist was
/// permanently empty for every practice except the demo one — a screen that cannot fill is
/// worse than no screen, because people learn not to open it.
/// </para>
/// <para>
/// Its own service rather than lines inside the front desk, because two different events
/// move a recall — finishing a visit and booking the next one — and they happen in
/// different features. One place means the rule cannot drift between them.
/// </para>
/// </remarks>
public interface IRecallScheduler
{
    /// <summary>
    /// Sets the next recall after a visit is completed.
    /// </summary>
    /// <remarks>
    /// Does nothing where the appointment's type carries no interval, which is most of
    /// them — see <c>AppointmentType.RecallIntervalMonths</c>.
    /// </remarks>
    Task OnVisitCompletedAsync(Appointment appointment, CancellationToken ct = default);

    /// <summary>
    /// Marks a patient's due recall as booked, where this appointment satisfies it.
    /// </summary>
    /// <remarks>
    /// Called when a booking is saved. Without it a recall stays on the worklist after the
    /// patient has booked, and somebody rings them about an appointment they already have —
    /// which is the single most irritating thing a recall list can do.
    /// </remarks>
    Task OnAppointmentBookedAsync(Appointment appointment, CancellationToken ct = default);
}

/// <inheritdoc cref="IRecallScheduler"/>
public sealed class RecallScheduler : IRecallScheduler
{
    private readonly IRepository<Recall> _recalls;
    private readonly IRepository<AppointmentType> _types;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;

    public RecallScheduler(
        IRepository<Recall> recalls,
        IRepository<AppointmentType> types,
        IClock clock,
        IAuditLog audit)
    {
        _recalls = recalls;
        _types = types;
        _clock = clock;
        _audit = audit;
    }

    public async Task OnVisitCompletedAsync(
        Appointment appointment, CancellationToken ct = default)
    {
        if (appointment.AppointmentTypeId is not { } typeId) return;

        var type = await _types.GetByIdAsync(typeId, ct).ConfigureAwait(false);

        if (type?.RecallIntervalMonths is not { } typeInterval || typeInterval <= 0) return;

        var existing = await _recalls
            .ListAsync(r => r.PatientId == appointment.PatientId, ct)
            .ConfigureAwait(false);

        // The one still open, if there is one. Declined is left alone on purpose: a patient
        // who has said they do not want reminders should not be put back on the list by
        // turning up for a filling.
        var open = existing.FirstOrDefault(r => r.Status
            is RecallStatus.Pending or RecallStatus.Due
            or RecallStatus.Contacted or RecallStatus.Booked or RecallStatus.Overdue);

        // The patient's own interval wins over the type's. A periodontal patient on three
        // months does not revert to six because they came in for a routine clean, and the
        // interval on their recall is where somebody recorded that decision.
        var months = open?.IntervalMonths is { } theirs && theirs > 0 ? theirs : typeInterval;
        var due = DateOnly.FromDateTime(appointment.StartUtc.ToLocalTime()).AddMonths(months);

        if (open is null)
        {
            var created = new Recall
            {
                Id = Guid.NewGuid(),
                PatientId = appointment.PatientId,
                RecallType = type.Name,
                IntervalMonths = months,
                DueOn = due,
                Status = RecallStatus.Pending,
                LastAppointmentId = appointment.Id,
            };

            await _recalls.SaveAsync(created, ct).ConfigureAwait(false);

            await RecordAsync(appointment.PatientId,
                $"Recall set for {due:d MMM yyyy} — {type.Name}, {months}-monthly", ct)
                .ConfigureAwait(false);

            return;
        }

        // Rolled forward rather than a second row added. One patient has one recall; two
        // would be two people ringing them in the same week about the same check-up.
        open.DueOn = due;
        open.LastAppointmentId = appointment.Id;

        // Back to the start of its life. The visit just happened, so whatever chasing was
        // done before it is spent, and the booking it was waiting for has now been kept.
        open.Status = RecallStatus.Pending;
        open.BookedAppointmentId = null;
        open.ContactAttempts = 0;
        open.LastContactedUtc = null;

        await _recalls.SaveAsync(open, ct).ConfigureAwait(false);

        await RecordAsync(appointment.PatientId,
            $"Recall rolled forward to {due:d MMM yyyy} after {type.Name}", ct)
            .ConfigureAwait(false);
    }

    public async Task OnAppointmentBookedAsync(
        Appointment appointment, CancellationToken ct = default)
    {
        var existing = await _recalls
            .ListAsync(r => r.PatientId == appointment.PatientId, ct)
            .ConfigureAwait(false);

        var open = existing.FirstOrDefault(r => r.Status
            is RecallStatus.Pending or RecallStatus.Due
            or RecallStatus.Contacted or RecallStatus.Overdue);

        if (open is null) return;

        var day = DateOnly.FromDateTime(appointment.StartUtc.ToLocalTime());

        // Only a booking at or after the due date counts. Somebody booked in for a broken
        // tooth next week has not had their six-month check, and clearing the recall would
        // lose them until the following one.
        //
        // A month's grace before the date, because practices book recalls slightly early
        // rather than to the day, and a recall that stayed on the list until its exact due
        // date would be chased a fortnight after it was booked.
        if (day < open.DueOn.AddMonths(-1)) return;

        open.Status = RecallStatus.Booked;
        open.BookedAppointmentId = appointment.Id;

        await _recalls.SaveAsync(open, ct).ConfigureAwait(false);

        await RecordAsync(appointment.PatientId,
            $"Recall marked booked for {day:d MMM yyyy}", ct)
            .ConfigureAwait(false);
    }

    /// <remarks>
    /// Swallowed, like the other audit writes around sends. A recall that moved is not
    /// worth failing the save that moved it.
    /// </remarks>
    private async Task RecordAsync(Guid patientId, string detail, CancellationToken ct)
    {
        try
        {
            await _audit
                .RecordAsync(AuditAction.Updated, nameof(Recall), null, detail, patientId, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            // Nothing to escalate to.
        }
    }
}
