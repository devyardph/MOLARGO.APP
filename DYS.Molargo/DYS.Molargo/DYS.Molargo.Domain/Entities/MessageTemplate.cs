using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A reusable message body for reminders, recalls and campaigns.
/// </summary>
public sealed class MessageTemplate : EntityBase
{
    public string Name { get; set; } = string.Empty;

    public CommunicationChannel Channel { get; set; }

    public MessagePurpose Purpose { get; set; }

    /// <summary>Subject line. Unused for SMS.</summary>
    public string? Subject { get; set; }

    /// <summary>
    /// The body, with placeholders in <c>{{PatientFirstName}}</c> form. Substitution
    /// happens at send time, and the rendered text is stored on the
    /// <see cref="CommunicationLog"/> — so editing this template never rewrites history.
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>What occasion sends this template.</summary>
    public MessageTrigger Trigger { get; set; } = MessageTrigger.Manual;

    /// <summary>
    /// Hours before the appointment to send, for a reminder. Null for anything not
    /// triggered off an appointment.
    /// </summary>
    public int? SendHoursBeforeAppointment { get; set; }

    /// <summary>
    /// Hours after the appointment to send, for a post-op check.
    /// </summary>
    /// <remarks>
    /// A second field rather than a negative "hours before". A signed offset reads as a
    /// bug at every call site that has to remember which sign means which side of the
    /// visit, and one wrong sign sends a post-op check two days before the treatment.
    /// </remarks>
    public int? SendHoursAfterAppointment { get; set; }

    /// <summary>
    /// One line for the staff list: what this message is for and when it goes.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether the practice wants this message sent.
    /// </summary>
    /// <remarks>
    /// Intent, not activity. Nothing in the app dispatches on a trigger — there is no
    /// scheduler and no gateway — so a screen showing this must say so rather than let a
    /// switch reading "on" be taken as reminders going out.
    /// </remarks>
    public bool IsActive { get; set; } = true;
}
