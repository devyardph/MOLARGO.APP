using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A message sent to, or received from, a patient — including phone calls logged by hand.
/// </summary>
/// <remarks>
/// Append-only, and it stores the rendered text rather than a template reference. "What
/// exactly did we tell this patient" is the question a complaint asks, and a template
/// edited since would answer it wrongly.
/// </remarks>
public sealed class CommunicationLog : EntityBase
{
    public Guid PatientId { get; set; }

    public CommunicationChannel Channel { get; set; }

    public CommunicationDirection Direction { get; set; }

    public MessagePurpose Purpose { get; set; }

    public CommunicationStatus Status { get; set; } = CommunicationStatus.Pending;

    /// <summary>The template used, for reporting. The text below is what was actually sent.</summary>
    public Guid? MessageTemplateId { get; set; }

    public string? Subject { get; set; }

    /// <summary>The message as sent or received, fully rendered.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>The number or address it went to, as at the time of sending.</summary>
    public string? Recipient { get; set; }

    public Guid? AppointmentId { get; set; }

    public Guid? RecallId { get; set; }

    public DateTime? SentUtc { get; set; }

    public DateTime? DeliveredUtc { get; set; }

    /// <summary>The patient's reply, where there was one.</summary>
    public string? ResponseBody { get; set; }

    public DateTime? RespondedUtc { get; set; }

    /// <summary>Why it did not get through — a bounce or gateway rejection, verbatim.</summary>
    public string? FailureReason { get; set; }

    /// <summary>Who sent it by hand, or who logged the call. Null for an automated send.</summary>
    public Guid? SentByProviderId { get; set; }
}
