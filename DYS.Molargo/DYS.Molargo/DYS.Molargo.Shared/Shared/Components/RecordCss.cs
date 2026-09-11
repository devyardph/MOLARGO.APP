using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Shared.Components;

/// <summary>
/// Chips, labels and formatting for the patient record's comms, documents, billing and
/// medical-history tabs.
/// </summary>
/// <remarks>
/// One type for four tabs rather than four near-empty ones: these are all "turn a stored
/// enum into the word the design shows", and splitting them by tab would mean four files
/// to open every time the record's vocabulary shifts.
/// </remarks>
public static class RecordCss
{
    // ---- communications --------------------------------------------------

    /// <summary>
    /// A reply gets the accent because it needs reading; a failure gets the outline
    /// because it needs fixing. A delivered reminder is not news, so it stays neutral.
    /// </summary>
    public static string CommsTag(CommunicationStatus status) => status switch
    {
        CommunicationStatus.Responded => "tag tag-accent",
        CommunicationStatus.Failed or CommunicationStatus.Suppressed => "tag tag-outline",
        _ => "tag tag-neutral",
    };

    public static string CommsLabel(CommunicationStatus status) => status switch
    {
        CommunicationStatus.Pending => "Queued",
        CommunicationStatus.Sent => "Sent",
        CommunicationStatus.Delivered => "Delivered",
        CommunicationStatus.Responded => "Replied",
        CommunicationStatus.Failed => "Failed",

        // Not a failure: the patient opted out and the practice correctly did not send.
        CommunicationStatus.Suppressed => "Not sent — opted out",
        _ => string.Empty,
    };

    public static string ChannelLabel(CommunicationChannel channel) => channel switch
    {
        CommunicationChannel.Sms => "SMS",
        CommunicationChannel.Email => "Email",
        CommunicationChannel.Phone => "Call",
        CommunicationChannel.Letter => "Letter",
        CommunicationChannel.PatientPortal => "Portal",
        CommunicationChannel.InPerson => "In person",
        _ => string.Empty,
    };

    public static string PurposeLabel(MessagePurpose purpose) => purpose switch
    {
        MessagePurpose.AppointmentReminder => "Reminder",
        MessagePurpose.Recall => "Recall",
        MessagePurpose.Aftercare => "Aftercare",
        MessagePurpose.AccountNotice => "Account",
        MessagePurpose.TreatmentFollowUp => "Plan follow-up",
        MessagePurpose.Marketing => "Marketing",
        MessagePurpose.SecurityNotice => "Security",
        MessagePurpose.General => "General",
        _ => string.Empty,
    };

    // ---- billing ---------------------------------------------------------

    /// <summary>
    /// The accent marks money still owed. Paid, voided and written-off are resting states
    /// from the front desk's point of view — nobody is going to chase them.
    /// </summary>
    public static string InvoiceTag(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Overdue or InvoiceStatus.PartiallyPaid => "tag tag-accent",
        InvoiceStatus.Issued => "tag tag-accent2",
        InvoiceStatus.Voided or InvoiceStatus.WrittenOff => "tag tag-outline",
        _ => "tag tag-neutral",
    };

    public static string InvoiceLabel(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Draft => "Draft",
        InvoiceStatus.Issued => "Issued",
        InvoiceStatus.PartiallyPaid => "Part paid",
        InvoiceStatus.Paid => "Paid",
        InvoiceStatus.Overdue => "Overdue",
        InvoiceStatus.Voided => "Voided",
        InvoiceStatus.WrittenOff => "Written off",
        _ => string.Empty,
    };

    // ---- documents -------------------------------------------------------

    /// <summary>The design's "Type" column wording.</summary>
    public static string DocumentKindLabel(DocumentKind kind) => kind switch
    {
        DocumentKind.Photograph => "Image",
        DocumentKind.Radiograph => "Image · DICOM",
        DocumentKind.Consent => "Form",
        DocumentKind.Referral => "Referral",
        DocumentKind.Report => "Report",
        DocumentKind.LabDocket => "Lab docket",
        DocumentKind.Financial => "Financial",
        DocumentKind.Correspondence => "Correspondence",
        _ => "Other",
    };

    public static string FileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB",
    };

    // ---- medical history -------------------------------------------------

    /// <summary>The medical-history tab's "Item" column.</summary>
    public static string AlertKindLabel(AlertKind kind) => kind switch
    {
        AlertKind.Allergy => "Allergy",
        AlertKind.MedicalCondition => "Condition",
        AlertKind.Medication => "Medication",
        AlertKind.Pregnancy => "Pregnancy",
        AlertKind.CareNote => "Care note",
        AlertKind.Financial => "Account",
        _ => string.Empty,
    };

    /// <summary>
    /// A critical alert's item label is accent-coloured, as the design has it — the eye
    /// should find the allergies before it reads the column.
    /// </summary>
    public static string AlertKindCss(AlertSeverity severity) =>
        severity == AlertSeverity.Critical ? "font-bold text-accent-700" : string.Empty;

    public static string SeverityLabel(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Critical => "Critical",
        AlertSeverity.Warning => "Warning",
        _ => "Information",
    };

    /// <summary>
    /// A questionnaire answer. Null is "not answered", which is deliberately not shown as
    /// "No" — a skipped question about anticoagulants is not a negative answer.
    /// </summary>
    public static string AnswerLabel(bool? yesNo) => yesNo switch
    {
        true => "Yes",
        false => "No",
        null => "Not answered",
    };

    public static string AnswerCss(bool? yesNo) =>
        yesNo == true ? "font-semibold text-accent-700" : "text-ink/55";
}
