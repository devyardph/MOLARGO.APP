namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// The mail account a clinic sends its notifications from.
/// </summary>
/// <remarks>
/// <para>
/// One row per clinic, not per location: the practice sends as itself, and a patient
/// receiving mail from two different addresses depending on which site booked them would
/// look like a phishing attempt.
/// </para>
/// <para>
/// Holds an SMTP credential, which makes it the most sensitive row in the database. See
/// <see cref="AppPassword"/> — it is stored in the same unencrypted file as everything
/// else, and the settings screen says so rather than implying otherwise.
/// </para>
/// </remarks>
public sealed class NotificationSettings : EntityBase
{
    /// <summary>
    /// Whether the practice wants email notifications sent at all.
    /// </summary>
    /// <remarks>
    /// Separate from whether an account is configured. Turning it off is how a practice
    /// stops mail going out without deleting the credential and having to find it again.
    /// </remarks>
    public bool EmailEnabled { get; set; }

    /// <summary>The address mail is sent from — the Gmail account itself.</summary>
    public string? SenderAddress { get; set; }

    /// <summary>
    /// The name recipients see, rather than the raw address.
    /// </summary>
    /// <remarks>
    /// Worth holding because a reminder from "Molargo Dental" is opened and one from
    /// "molargo.notifications@gmail.com" is not.
    /// </remarks>
    public string? SenderName { get; set; }

    /// <summary>
    /// Where a patient's reply goes, if it should not go to the sending account.
    /// </summary>
    /// <remarks>
    /// A sending account is usually unmonitored. Without this, a patient answering a
    /// reminder writes into a mailbox nobody opens.
    /// </remarks>
    public string? ReplyToAddress { get; set; }

    /// <summary>Where alerts addressed to the practice itself go.</summary>
    public string? AlertsToAddress { get; set; }

    // ---- what gets sent --------------------------------------------------

    /// <summary>Offer to email a receipt once a payment is recorded.</summary>
    public bool NotifyReceipts { get; set; }

    /// <summary>Mail when a stock item reaches its reorder level.</summary>
    public bool NotifyLowStock { get; set; }

    /// <summary>Send the day's takings each evening.</summary>
    public bool NotifyDailySummary { get; set; }

    /// <summary>
    /// Who the daily summary goes to — more than one address, comma-separated.
    /// </summary>
    /// <remarks>
    /// A list rather than one address, and separate from the alerts address: the takings go
    /// to the owner and the bookkeeper, who are not the people who need to know a box of
    /// gloves has run low.
    /// </remarks>
    public string? DailySummaryRecipients { get; set; }

    /// <summary>
    /// What time the summary would go out, in practice-local time.
    /// </summary>
    /// <remarks>
    /// Stored as a local time of day rather than UTC. It means "after we close", which is a
    /// wall-clock intention — converting it to UTC would shift it by an hour twice a year.
    /// </remarks>
    public TimeOnly DailySummaryAt { get; set; } = new(19, 0);

    /// <summary>
    /// The Gmail app password for <see cref="SenderAddress"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An app password rather than the account's own password: it is scoped to mail, can be
    /// revoked on its own without changing the sign-in everyone uses, and is the only thing
    /// Google will accept for SMTP on an account with two-factor enabled.
    /// </para>
    /// <para>
    /// Stored as given, in the clear. There is nowhere to hide a key in an offline app —
    /// whatever decrypted this would have to be readable by the same process, so encrypting
    /// it here would buy confidence rather than security. Never rendered back to the screen
    /// once saved, and the pane states the exposure.
    /// </para>
    /// </remarks>
    public string? AppPassword { get; set; }

    /// <summary>Defaults to Gmail's submission host.</summary>
    public string SmtpHost { get; set; } = "smtp.gmail.com";

    /// <summary>
    /// 587, the STARTTLS submission port.
    /// </summary>
    /// <remarks>
    /// Not 465. Both work with Gmail, but 465 is implicit TLS, which
    /// <c>System.Net.Mail</c> does not speak — pointing this at 465 fails with a timeout
    /// that says nothing about why.
    /// </remarks>
    public int SmtpPort { get; set; } = 587;

    /// <summary>When the credential was last proved to work.</summary>
    public DateTime? LastTestUtc { get; set; }

    /// <summary>
    /// What the last test said, verbatim — including the failure.
    /// </summary>
    /// <remarks>
    /// Kept because an SMTP rejection is the only thing that explains why nothing is
    /// arriving, and Google's messages name the cause precisely: a wrong app password, an
    /// account without two-factor, a blocked sign-in attempt.
    /// </remarks>
    public string? LastTestResult { get; set; }

    public bool LastTestSucceeded { get; set; }

    /// <summary>True where there is enough here to attempt a send.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SenderAddress)
        && !string.IsNullOrWhiteSpace(AppPassword)
        && !string.IsNullOrWhiteSpace(SmtpHost)
        && SmtpPort > 0;
}
