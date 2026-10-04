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

    // ---- the outbox ------------------------------------------------------
    //
    // This table was already most of a queue — a Status that starts at Pending, a
    // Recipient, a Body and a SentUtc — it simply was not used as one. Messages were sent
    // first and the row written afterwards with the outcome already known, which meant a
    // slow mail server blocked whoever booked the appointment, and a crash mid-send lost
    // the message with nothing recording that it had ever been meant to go.
    //
    // Written Pending in the same transaction as the thing that caused it, and drained by
    // a background service. Either the appointment and its reminder both exist or neither
    // does, which is the property a separate queue cannot give you.

    /// <summary>How many times sending has been tried.</summary>
    /// <remarks>
    /// Capped rather than retried forever: a mistyped mobile number is not a transient
    /// fault, and a queue that keeps retrying one is a queue that never drains past it.
    /// </remarks>
    public int Attempts { get; set; }

    /// <summary>
    /// The earliest this may be tried again. Null means now.
    /// </summary>
    /// <remarks>
    /// Backoff, so a gateway that is briefly down is not hammered by every pending message
    /// at once — which is how a brief outage becomes a rate-limit ban.
    /// </remarks>
    public DateTime? NextAttemptUtc { get; set; }

    /// <summary>
    /// When a drainer took this row, or null when nobody holds it.
    /// </summary>
    /// <remarks>
    /// Two servers drain the same table. Claiming is an atomic UPDATE … WHERE Status =
    /// Pending AND claim is free, so only one of them can win a given row — without it the
    /// obvious implementation sends every reminder twice, once per server.
    ///
    /// A claim older than the timeout is treated as abandoned: the process that took it has
    /// died, and the alternative is a message stuck forever behind a server that no longer
    /// exists.
    /// </remarks>
    public DateTime? ClaimedUtc { get; set; }

    /// <summary>Which process holds the claim, for telling a stuck queue from a busy one.</summary>
    public string? ClaimedBy { get; set; }

    public DateTime? RespondedUtc { get; set; }

    /// <summary>Why it did not get through — a bounce or gateway rejection, verbatim.</summary>
    public string? FailureReason { get; set; }

    /// <summary>Who sent it by hand, or who logged the call. Null for an automated send.</summary>
    public Guid? SentByProviderId { get; set; }
}
