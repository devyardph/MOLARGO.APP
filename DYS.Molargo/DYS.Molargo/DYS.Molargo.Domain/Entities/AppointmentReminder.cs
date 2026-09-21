using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One reminder, for one appointment, at one point in the cadence.
/// </summary>
/// <remarks>
/// <para>
/// A row per (appointment, offset) rather than a flag on the appointment, because a cadence
/// has more than one step: seven days out and again the day before are two reminders, and
/// an appointment that only knew "reminded" would send the second one never or the first
/// one twice.
/// </para>
/// <para>
/// This is what makes the run idempotent. The sender asks "is there a row for this
/// appointment at this offset" and skips if there is — so running it twice in a morning,
/// or running it after a crash halfway through, sends nothing a second time. Idempotence
/// has to come from stored facts rather than from the run being careful, because the run
/// is the thing that can be interrupted.
/// </para>
/// <para>
/// Written even when the send fails. A failed reminder is not retried by the next run: the
/// commonest failure is a patient with no mobile or no consent, and retrying that every
/// hour until the appointment is a log full of the same refusal. The failure is on the tab
/// for somebody to act on, which is what a reminder screen is actually for.
/// </para>
/// </remarks>
public sealed class AppointmentReminder : EntityBase
{
    public Guid AppointmentId { get; set; }

    public Guid PatientId { get; set; }

    /// <summary>Which step of the cadence this is — 7 for "seven days before".</summary>
    public int OffsetDays { get; set; }

    /// <summary>When the run attempted it.</summary>
    public DateTime SentUtc { get; set; }

    /// <summary>Whether anything actually left.</summary>
    public bool Succeeded { get; set; }

    /// <summary>
    /// How it went, in the words of whatever carried it.
    /// </summary>
    /// <remarks>
    /// Kept verbatim from the mail server or the carrier, like every other send outcome
    /// here. "No mobile on their record" and "authentication failed" need different people
    /// to do different things, and a single "failed" tells neither of them which.
    /// </remarks>
    public string? Detail { get; set; }

    /// <summary>Which way it went, or was meant to.</summary>
    public CommunicationChannel Channel { get; set; } = CommunicationChannel.Email;
}
