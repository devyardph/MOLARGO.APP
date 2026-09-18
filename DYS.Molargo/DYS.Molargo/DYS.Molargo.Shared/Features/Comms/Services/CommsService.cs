using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Repositories;
using DYS.Molargo.Shared.Services;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Features.Comms.Services;

/// <summary>One template as the list shows it.</summary>
public sealed record TemplateRow(
    Guid TemplateId,
    string Name,
    CommunicationChannel Channel,
    MessagePurpose Purpose,
    MessageTrigger Trigger,
    string? Description,
    int? HoursBefore,
    int? HoursAfter,
    bool IsActive,
    int UnknownTokenCount)
{
    /// <summary>Marketing needs opt-in; everything else is operational.</summary>
    public bool NeedsMarketingConsent => Purpose == MessagePurpose.Marketing;

    /// <summary>"48 hours before" — when this message would go, in words.</summary>
    public string Timing => Trigger switch
    {
        MessageTrigger.BeforeAppointment => HoursBefore is { } hours
            ? Hours(hours) + " before the appointment"
            : "before the appointment",

        MessageTrigger.AfterAppointment => HoursAfter is { } hours
            ? Hours(hours) + " after the appointment"
            : "after the appointment",

        MessageTrigger.RecallDue => "when the recall falls due",
        MessageTrigger.AccountOverdue => "when the account falls overdue",
        MessageTrigger.Birthday => "on the patient's birthday",
        MessageTrigger.ReviewRequest => HoursAfter is { } hours
            ? Hours(hours) + " after a completed visit"
            : "after a completed visit",

        MessageTrigger.PasswordReset => "when an administrator resets a password",
        MessageTrigger.PasswordResetRequested =>
            "when somebody asks for a reset link from the sign-in screen",

        MessageTrigger.SignInCode =>
            "when somebody with two-step sign-in enters their password",

        _ => "sent by hand",
    };

    private static string Hours(int hours) => hours switch
    {
        1 => "1 hour",
        < 24 => $"{hours} hours",
        24 => "1 day",
        _ when hours % 24 == 0 => $"{hours / 24} days",
        _ => $"{hours} hours",
    };
}

/// <summary>A template rendered against one patient, for the preview.</summary>
public sealed record TemplatePreview(
    string PatientName,
    string? Subject,
    string Body,
    IReadOnlyList<string> Unresolved,
    IReadOnlyList<string> Unknown)
{
    public bool IsComplete => Unresolved.Count == 0 && Unknown.Count == 0;
}

/// <summary>One audience a campaign can be aimed at.</summary>
public sealed record SegmentDefinition(string Key, string Label, string Description);

/// <summary>A patient a campaign would reach.</summary>
public sealed record AudienceMember(Guid PatientId, string Name, string? Reachable);

/// <summary>A patient it would not, and why not.</summary>
public sealed record AudienceExclusion(Guid PatientId, string Name, string Reason);

/// <summary>Who a segment resolves to, and who it leaves out.</summary>
public sealed record CampaignAudience(
    string SegmentKey,
    CommunicationChannel Channel,
    IReadOnlyList<AudienceMember> Included,
    IReadOnlyList<AudienceExclusion> Excluded)
{
    public int Size => Included.Count;

    /// <summary>
    /// What the send would cost.
    /// </summary>
    /// <remarks>
    /// A per-message rate, not a quote. There is no gateway account behind this and the
    /// real rate is whatever the practice's plan says, so the screen has to name it as an
    /// estimate.
    /// </remarks>
    public decimal EstimatedCost => Channel == CommunicationChannel.Sms
        ? Size * SmsRatePerMessage
        : 0m;

    /// <summary>Indicative Australian bulk-SMS rate, in dollars per message.</summary>
    public const decimal SmsRatePerMessage = 0.055m;
}

/// <summary>One message in a portal thread.</summary>
public sealed record ThreadMessage(
    Guid MessageId,
    bool FromPatient,
    string Body,
    DateTime WhenUtc,
    string? AuthorName,
    CommunicationStatus Status);

/// <summary>A patient's portal conversation.</summary>
public sealed record InboxThread(
    Guid PatientId,
    string PatientName,
    string LatestBody,
    DateTime LatestUtc,
    bool AwaitingReply,
    IReadOnlyList<ThreadMessage> Messages);

/// <summary>One patient's communication consent.</summary>
public sealed record ConsentRow(
    Guid PatientId,
    string PatientName,
    string PatientNumber,
    bool ReminderConsent,
    bool MarketingConsent,
    DateTime? ReminderUpdatedUtc,
    DateTime? MarketingUpdatedUtc,
    string? ConsentSource,
    bool HasMobile,
    bool HasEmail)
{
    /// <summary>
    /// Consent recorded with no provenance. Not a formatting problem: consent the practice
    /// cannot show the origin of is consent it cannot rely on in a complaint.
    /// </summary>
    public bool SourceMissing => MarketingConsent && string.IsNullOrWhiteSpace(ConsentSource);

    /// <summary>
    /// No way to reach them on any channel this app could use.
    /// </summary>
    public bool Unreachable => !HasMobile && !HasEmail;
}

/// <summary>The month's comms activity, for the compliance panel.</summary>
public sealed record CommsActivity(
    int Sent,
    int Delivered,
    int Failed,
    int Suppressed,
    int MarketingOptOuts,
    int MarketingOptIns,
    int TotalPatients)
{
    public decimal OptInRate => TotalPatients == 0
        ? 0m
        : Math.Round(100m * MarketingOptIns / TotalPatients, 1);
}

/// <summary>
/// Message templates, campaign audiences, the portal inbox and communication consent.
/// </summary>
/// <remarks>
/// Nothing here sends. There is no SMS or email gateway and no scheduler, which is online
/// work — so every method either records intent, or answers a question about who could be
/// reached and who could not. The screens say which is which.
/// </remarks>
public interface ICommsService
{
    // ---- templates -------------------------------------------------------

    Task<IReadOnlyList<TemplateRow>> GetTemplatesAsync(CancellationToken ct = default);

    Task<MessageTemplate?> GetTemplateAsync(Guid templateId, CancellationToken ct = default);

    /// <summary>Saves a template. Returns a refusal, or null on success.</summary>
    Task<string?> SaveTemplateAsync(MessageTemplate template, CancellationToken ct = default);

    Task<string?> SetTemplateActiveAsync(
        Guid templateId, bool isActive, CancellationToken ct = default);

    /// <summary>
    /// Renders a body against a real patient, so the preview shows what would actually go
    /// out rather than the token names.
    /// </summary>
    /// <param name="patientId">
    /// The patient to render against, or <see cref="Guid.Empty"/> to pick one with a
    /// booking — a preview against a patient with no appointment cannot fill the reminder
    /// tokens and looks broken when the template is fine.
    /// </param>
    Task<TemplatePreview?> PreviewAsync(
        Guid locationId,
        CommunicationChannel channel,
        string? subject,
        string body,
        Guid patientId = default,
        CancellationToken ct = default);

    // ---- campaigns -------------------------------------------------------

    /// <summary>Resolves a segment to the patients it would and would not reach.</summary>
    Task<CampaignAudience> GetAudienceAsync(
        Guid locationId,
        string segmentKey,
        CommunicationChannel channel,
        CancellationToken ct = default);

    // ---- inbox -----------------------------------------------------------

    Task<IReadOnlyList<InboxThread>> GetInboxAsync(CancellationToken ct = default);

    /// <summary>
    /// Records a reply on the patient's communication record.
    /// </summary>
    /// <remarks>
    /// Recorded, not sent — there is no portal to deliver into. Logged anyway because the
    /// reply is clinical advice given to a patient, and advice that was given but not
    /// written down is the gap a complaint falls into.
    /// </remarks>
    Task<string?> ReplyAsync(
        Guid patientId, string body, Guid? byProviderId = null, CancellationToken ct = default);

    // ---- consent ---------------------------------------------------------

    Task<IReadOnlyList<ConsentRow>> GetConsentAsync(
        Guid locationId, string? term = null, CancellationToken ct = default);

    /// <summary>
    /// Changes one patient's consent, stamping when and from what source.
    /// </summary>
    Task<string?> SetConsentAsync(
        Guid patientId,
        bool reminderConsent,
        bool marketingConsent,
        string? source,
        CancellationToken ct = default);

    Task<CommsActivity> GetActivityAsync(Guid locationId, CancellationToken ct = default);
}

/// <inheritdoc cref="ICommsService"/>
public sealed class CommsService : ICommsService
{
    /// <summary>
    /// The segments the campaign builder offers, in the design's order.
    /// </summary>
    /// <remarks>
    /// A fixed set rather than a query builder. Each of these is a question a practice
    /// actually asks, and each is expressible against the seeded data — an arbitrary
    /// filter UI would offer combinations nothing behind it can answer.
    /// </remarks>
    public static readonly SegmentDefinition[] Segments =
    [
        new("overdue-recall", "Overdue recall",
            "Recall due date has passed and nothing is booked."),
        new("no-visit-12m", "Not seen in 12 months",
            "Active patients whose last visit was more than a year ago."),
        // Chosen over "new patients this quarter", which the seed makes meaningless: every
        // sample patient is created in one pass, so that segment resolved to the whole
        // list and was indistinguishable from "all current". This one is a question with
        // a different answer — an unanswered estimate is treatment the patient has been
        // quoted for and not booked.
        new("plan-unaccepted", "Plan awaiting a decision",
            "Presented a treatment plan and not yet accepted or declined it."),
        // "Current" rather than "active": PatientStatus.Active is one status among
        // several a current patient can hold, and a chip reading "all active" alongside a
        // filter that also takes RecallDue and Lead would be describing itself wrongly.
        new("all-current", "All current patients",
            "Every patient at this location whose record is not archived."),
    ];

    /// <summary>How long "not seen recently" is, in months.</summary>
    private const int LapsedMonths = 12;


    private readonly IRepository<MessageTemplate> _templates;
    private readonly IRepository<CommunicationLog> _log;
    private readonly IRepository<PatientEntity> _patients;
    private readonly IRepository<Appointment> _appointments;
    private readonly IRepository<AppointmentType> _appointmentTypes;
    private readonly IRepository<Recall> _recalls;
    private readonly IRepository<TreatmentPlan> _plans;
    private readonly IRepository<Provider> _providers;
    private readonly IRepository<PracticeLocation> _locations;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;

    public CommsService(
        IRepository<MessageTemplate> templates,
        IRepository<CommunicationLog> log,
        IRepository<PatientEntity> patients,
        IRepository<Appointment> appointments,
        IRepository<AppointmentType> appointmentTypes,
        IRepository<Recall> recalls,
        IRepository<TreatmentPlan> plans,
        IRepository<Provider> providers,
        IRepository<PracticeLocation> locations,
        IClock clock,
        IAuditLog audit)
    {
        _templates = templates;
        _log = log;
        _patients = patients;
        _appointments = appointments;
        _appointmentTypes = appointmentTypes;
        _recalls = recalls;
        _plans = plans;
        _providers = providers;
        _locations = locations;
        _clock = clock;
        _audit = audit;
    }

    // ---- templates -------------------------------------------------------

    public async Task<IReadOnlyList<TemplateRow>> GetTemplatesAsync(CancellationToken ct = default)
    {
        var templates = await _templates.ListAsync(ct: ct).ConfigureAwait(false);

        return templates
            .OrderBy(template => template.Trigger == MessageTrigger.Manual ? 1 : 0)
            .ThenBy(template => template.Name)
            .Select(Row)
            .ToList();
    }

    public async Task<MessageTemplate?> GetTemplateAsync(
        Guid templateId, CancellationToken ct = default) =>
        await _templates.GetByIdAsync(templateId, ct).ConfigureAwait(false);

    public async Task<string?> SaveTemplateAsync(
        MessageTemplate template, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(template.Name)) return "Give the template a name.";

        if (string.IsNullOrWhiteSpace(template.Body)) return "The message body is empty.";

        var unknown = MergeFields.UnknownTokensIn(template.Body);

        // Refused, not warned. A token nothing can resolve reaches the patient as literal
        // braces, and the template is saved once and sent thousands of times.
        if (unknown.Count > 0)
        {
            return $"Unknown merge field{(unknown.Count == 1 ? string.Empty : "s")}: "
                + string.Join(", ", unknown.Select(MergeFields.Placeholder))
                + ". Use the chips below the body.";
        }

        if (template.Channel == CommunicationChannel.Sms
            && !string.IsNullOrWhiteSpace(template.Subject))
        {
            // Silently dropped rather than refused: a subject on an SMS is a leftover from
            // switching the channel, not something the author is asking for.
            template.Subject = null;
        }

        if (template.Channel == CommunicationChannel.Email
            && string.IsNullOrWhiteSpace(template.Subject))
        {
            return "An email needs a subject line.";
        }

        template.Name = template.Name.Trim();

        await _templates.SaveAsync(template, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> SetTemplateActiveAsync(
        Guid templateId, bool isActive, CancellationToken ct = default)
    {
        var template = await _templates.GetByIdAsync(templateId, ct).ConfigureAwait(false);
        if (template is null) return "That template no longer exists.";

        template.IsActive = isActive;

        await _templates.SaveAsync(template, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<TemplatePreview?> PreviewAsync(
        Guid locationId,
        CommunicationChannel channel,
        string? subject,
        string body,
        Guid patientId = default,
        CancellationToken ct = default)
    {
        var patient = patientId == default
            ? await SamplePatientAsync(locationId, ct).ConfigureAwait(false)
            : await _patients.GetByIdAsync(patientId, ct).ConfigureAwait(false);

        if (patient is null) return null;

        var values = await MergeValuesAsync(patient, locationId, ct).ConfigureAwait(false);

        var renderedBody = MergeFields.Render(body, values);
        var renderedSubject = MergeFields.Render(subject, values);

        var unresolved = renderedBody.Unresolved
            .Concat(renderedSubject.Unresolved)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new TemplatePreview(
            patient.FullName,
            channel == CommunicationChannel.Sms ? null : renderedSubject.Text,
            renderedBody.Text,
            unresolved,
            MergeFields.UnknownTokensIn(body));
    }

    // ---- campaigns -------------------------------------------------------

    public async Task<CampaignAudience> GetAudienceAsync(
        Guid locationId,
        string segmentKey,
        CommunicationChannel channel,
        CancellationToken ct = default)
    {
        var candidates = await SegmentAsync(locationId, segmentKey, ct).ConfigureAwait(false);

        var included = new List<AudienceMember>();
        var excluded = new List<AudienceExclusion>();

        foreach (var patient in candidates.OrderBy(entry => entry.LastName)
            .ThenBy(entry => entry.FirstName))
        {
            // Consent first, and before reachability. A patient who has opted out is
            // excluded whether or not the practice holds a mobile for them, and reporting
            // "no mobile" for someone who said no is the wrong reason on a compliance
            // screen.
            if (!patient.MarketingConsent)
            {
                excluded.Add(new AudienceExclusion(
                    patient.Id, patient.FullName, "No marketing consent"));
                continue;
            }

            var address = channel == CommunicationChannel.Sms ? patient.Mobile : patient.Email;

            if (string.IsNullOrWhiteSpace(address))
            {
                excluded.Add(new AudienceExclusion(
                    patient.Id,
                    patient.FullName,
                    channel == CommunicationChannel.Sms ? "No mobile number" : "No email address"));
                continue;
            }

            // Archived only.
            //
            // Filtering to Active alone threw out every patient whose status is RecallDue,
            // which is precisely who the overdue-recall segment exists to find — the
            // segment located them and the consent filter then dropped them, so the
            // audience came back empty for the one campaign a practice runs most. A Lead
            // who has opted in is a prospective patient, and marketing is what a lead is
            // for.
            if (patient.Status == PatientStatus.Archived)
            {
                excluded.Add(new AudienceExclusion(
                    patient.Id, patient.FullName, "Patient record archived"));
                continue;
            }

            included.Add(new AudienceMember(patient.Id, patient.FullName, address));
        }

        return new CampaignAudience(segmentKey, channel, included, excluded);
    }

    // ---- inbox -----------------------------------------------------------

    public async Task<IReadOnlyList<InboxThread>> GetInboxAsync(CancellationToken ct = default)
    {
        var messages = await _log
            .ListAsync(entry => entry.Channel == CommunicationChannel.PatientPortal, ct)
            .ConfigureAwait(false);

        if (messages.Count == 0) return [];

        var patientIds = messages.Select(entry => entry.PatientId).ToHashSet();

        var patients = await _patients
            .ListAsync(patient => patientIds.Contains(patient.Id), ct)
            .ConfigureAwait(false);

        var names = patients.ToDictionary(patient => patient.Id, patient => patient.FullName);

        var providers = await _providers.ListAsync(ct: ct).ConfigureAwait(false);
        var staff = providers.ToDictionary(provider => provider.Id, provider => provider.DisplayName);

        return messages
            .GroupBy(entry => entry.PatientId)
            .Select(group =>
            {
                var ordered = group
                    .OrderBy(entry => When(entry))
                    .Select(entry => new ThreadMessage(
                        entry.Id,
                        entry.Direction == CommunicationDirection.Inbound,
                        entry.Body,
                        When(entry),
                        entry.SentByProviderId is { } author
                            ? staff.GetValueOrDefault(author)
                            : null,
                        entry.Status))
                    .ToList();

                var latest = ordered[^1];

                return new InboxThread(
                    group.Key,
                    names.GetValueOrDefault(group.Key, "Unknown patient"),
                    latest.Body,
                    latest.WhenUtc,

                    // The last word was the patient's. That is the whole sort order of an
                    // inbox: a thread the practice has already answered is not waiting on
                    // anyone.
                    latest.FromPatient,
                    ordered);
            })
            .OrderByDescending(thread => thread.AwaitingReply)
            .ThenByDescending(thread => thread.LatestUtc)
            .ToList();
    }

    public async Task<string?> ReplyAsync(
        Guid patientId, string body, Guid? byProviderId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body)) return "Type a reply first.";

        var patient = await _patients.GetByIdAsync(patientId, ct).ConfigureAwait(false);
        if (patient is null) return "That patient no longer exists.";

        await _log
            .SaveAsync(
                new CommunicationLog
                {
                    PatientId = patientId,
                    Channel = CommunicationChannel.PatientPortal,
                    Direction = CommunicationDirection.Outbound,
                    Purpose = MessagePurpose.General,

                    // Pending, not Sent. Nothing carried it to the patient, and a status
                    // of Sent would make the record claim a delivery that never happened.
                    Status = CommunicationStatus.Pending,
                    Body = body.Trim(),
                    SentByProviderId = byProviderId,
                },
                ct)
            .ConfigureAwait(false);

        // The message body is not copied into the entry. It is already stored in full on
        // the communication row, and duplicating clinical text into a log that a wider set
        // of staff can read is a disclosure the log itself would be responsible for.
        await _audit
            .RecordAsync(
                AuditAction.Created,
                nameof(CommunicationLog),
                patientId,
                $"Replied to the patient ({body.Trim().Length} characters)",
                patientId,
                ct)
            .ConfigureAwait(false);

        return null;
    }

    // ---- consent ---------------------------------------------------------

    public async Task<IReadOnlyList<ConsentRow>> GetConsentAsync(
        Guid locationId, string? term = null, CancellationToken ct = default)
    {
        var patients = await _patients
            .ListAsync(patient => patient.PracticeLocationId == locationId, ct)
            .ConfigureAwait(false);

        var query = patients.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(term))
        {
            var needle = term.Trim();

            query = query.Where(patient =>
                patient.FullName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || (patient.PatientNumber?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return query
            // Opted out first, then consent with no recorded source. Both are the rows a
            // consent audit is looking for, and an alphabetical list buries them.
            .OrderBy(patient => patient.MarketingConsent ? 1 : 0)
            .ThenBy(patient => patient.LastName)
            .ThenBy(patient => patient.FirstName)
            .Take(60)
            .Select(patient => new ConsentRow(
                patient.Id,
                patient.FullName,
                patient.PatientNumber ?? "—",
                patient.ReminderConsent,
                patient.MarketingConsent,
                patient.ReminderConsentUpdatedUtc,
                patient.MarketingConsentUpdatedUtc,
                patient.ConsentSource,
                !string.IsNullOrWhiteSpace(patient.Mobile),
                !string.IsNullOrWhiteSpace(patient.Email)))
            .ToList();
    }

    public async Task<string?> SetConsentAsync(
        Guid patientId,
        bool reminderConsent,
        bool marketingConsent,
        string? source,
        CancellationToken ct = default)
    {
        var patient = await _patients.GetByIdAsync(patientId, ct).ConfigureAwait(false);
        if (patient is null) return "That patient no longer exists.";

        // Granting marketing consent without saying where it came from is refused.
        // Withdrawing it is not: a patient asking to be taken off the list is honoured
        // immediately, and blocking that on a form field is how an opt-out gets delayed.
        if (marketingConsent && !patient.MarketingConsent
            && string.IsNullOrWhiteSpace(source))
        {
            return "Record how consent was obtained — a signed form, the portal, or a "
                + "logged phone request.";
        }

        var now = _clock.UtcNow;

        if (patient.ReminderConsent != reminderConsent)
        {
            patient.ReminderConsent = reminderConsent;
            patient.ReminderConsentUpdatedUtc = now;
        }

        if (patient.MarketingConsent != marketingConsent)
        {
            patient.MarketingConsent = marketingConsent;
            patient.MarketingConsentUpdatedUtc = now;
        }

        if (!string.IsNullOrWhiteSpace(source)) patient.ConsentSource = source.Trim();

        await _patients.SaveAsync(patient, ct).ConfigureAwait(false);

        // Marketing consent is the one a regulator asks to see evidence for, and the
        // evidence is who set it, when, and on what basis — not the flag's current value.
        await _audit
            .RecordAsync(
                AuditAction.Updated,
                "Contact consent",
                patientId,
                $"Reminders {(reminderConsent ? "on" : "off")}, "
                    + $"marketing {(marketingConsent ? "on" : "off")}"
                    + (string.IsNullOrWhiteSpace(source) ? string.Empty : $" ({source.Trim()})"),
                patientId,
                ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<CommsActivity> GetActivityAsync(
        Guid locationId, CancellationToken ct = default)
    {
        var patients = await _patients
            .ListAsync(patient => patient.PracticeLocationId == locationId, ct)
            .ConfigureAwait(false);

        var ids = patients.Select(patient => patient.Id).ToHashSet();

        var monthStart = new DateOnly(_clock.Today.Year, _clock.Today.Month, 1)
            .ToDateTime(TimeOnly.MinValue, DateTimeKind.Local)
            .ToUniversalTime();

        var messages = await _log
            .ListAsync(entry => ids.Contains(entry.PatientId), ct)
            .ConfigureAwait(false);

        var thisMonth = messages
            .Where(entry => entry.Direction == CommunicationDirection.Outbound
                && When(entry) >= monthStart)
            .ToList();

        return new CommsActivity(
            thisMonth.Count(entry => entry.Status is CommunicationStatus.Sent
                or CommunicationStatus.Delivered or CommunicationStatus.Responded),
            thisMonth.Count(entry => entry.Status is CommunicationStatus.Delivered
                or CommunicationStatus.Responded),
            thisMonth.Count(entry => entry.Status == CommunicationStatus.Failed),
            thisMonth.Count(entry => entry.Status == CommunicationStatus.Suppressed),

            // Opt-outs recorded this month, which is only as complete as the consent
            // timestamps — a patient opted out before the field existed counts as neither.
            patients.Count(patient => !patient.MarketingConsent
                && patient.MarketingConsentUpdatedUtc >= monthStart),
            patients.Count(patient => patient.MarketingConsent),
            patients.Count);
    }

    // ---- helpers ---------------------------------------------------------

    private static TemplateRow Row(MessageTemplate template) => new(
        template.Id,
        template.Name,
        template.Channel,
        template.Purpose,
        template.Trigger,
        template.Description,
        template.SendHoursBeforeAppointment,
        template.SendHoursAfterAppointment,
        template.IsActive,
        MergeFields.UnknownTokensIn(template.Body).Count);

    /// <summary>
    /// When a logged message happened.
    /// </summary>
    /// <remarks>
    /// <c>SentUtc</c> where there is one, falling back to when the row was written. An
    /// inbound message and a queued reply have no send time, and ordering a thread by a
    /// null puts the patient's question after the answer to it.
    /// </remarks>
    private static DateTime When(CommunicationLog entry) =>
        entry.SentUtc ?? entry.CreatedUtc;

    /// <summary>
    /// A patient to render a preview against — one with a booking, for preference.
    /// </summary>
    /// <remarks>
    /// Reminder templates carry appointment tokens, and rendering them against whoever
    /// happens to sort first leaves every one of them unresolved. The preview then reports
    /// a broken template that is not broken.
    /// </remarks>
    private async Task<PatientEntity?> SamplePatientAsync(Guid locationId, CancellationToken ct)
    {
        var upcoming = await _appointments
            .ListAsync(appointment => appointment.PracticeLocationId == locationId
                && appointment.StartUtc >= _clock.UtcNow, ct)
            .ConfigureAwait(false);

        var next = upcoming.OrderBy(appointment => appointment.StartUtc).FirstOrDefault();

        if (next is not null)
        {
            var booked = await _patients.GetByIdAsync(next.PatientId, ct).ConfigureAwait(false);
            if (booked is not null) return booked;
        }

        var patients = await _patients
            .ListAsync(patient => patient.PracticeLocationId == locationId, ct)
            .ConfigureAwait(false);

        return patients.OrderBy(patient => patient.LastName).FirstOrDefault();
    }

    private async Task<Dictionary<string, string?>> MergeValuesAsync(
        PatientEntity patient, Guid locationId, CancellationToken ct)
    {
        var location = await _locations.GetByIdAsync(locationId, ct).ConfigureAwait(false);

        var appointments = await _appointments
            .ListAsync(appointment => appointment.PatientId == patient.Id
                && appointment.StartUtc >= _clock.UtcNow, ct)
            .ConfigureAwait(false);

        var next = appointments.OrderBy(appointment => appointment.StartUtc).FirstOrDefault();

        string? providerName = null;
        string? procedure = null;

        if (next is not null)
        {
            var provider = await _providers
                .GetByIdAsync(next.ProviderId, ct)
                .ConfigureAwait(false);

            providerName = provider?.DisplayName;

            // The booking's own reason first, falling back to the appointment type. The
            // reason is what the patient was told they are coming in for.
            if (!string.IsNullOrWhiteSpace(next.Reason))
            {
                procedure = next.Reason;
            }
            else if (next.AppointmentTypeId is { } typeId)
            {
                var type = await _appointmentTypes.GetByIdAsync(typeId, ct).ConfigureAwait(false);
                procedure = type?.Name;
            }
        }

        var local = next?.StartUtc.ToLocalTime();

        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PatientFirstName"] = patient.PreferredName ?? patient.FirstName,
            ["AppointmentDate"] = local?.ToString("ddd d MMM"),
            ["AppointmentTime"] = local?.ToString("h:mm tt"),
            ["ProviderName"] = providerName,
            ["Procedure"] = procedure,

            // A real booking link needs the online-booking widget behind a public URL,
            // which is online work. The token resolves to the practice's own page so a
            // preview is not misread as a broken template — the pane says it is not live.
            ["BookingLink"] = "molargo.example/book",
            ["PracticeName"] = location?.Name,
            ["PracticePhone"] = location?.Phone,
            ["AmountOwing"] = patient.Balance > 0m
                ? patient.Balance.ToString("C")
                : null,
        };
    }

    private async Task<IReadOnlyList<PatientEntity>> SegmentAsync(
        Guid locationId, string segmentKey, CancellationToken ct)
    {
        var patients = await _patients
            .ListAsync(patient => patient.PracticeLocationId == locationId, ct)
            .ConfigureAwait(false);

        switch (segmentKey)
        {
            case "overdue-recall":
            {
                // Booked and declined are both settled: one is coming in, the other has
                // said no. Chasing either is what makes a recall list stop being read.
                var recalls = await _recalls
                    .ListAsync(recall => recall.Status != RecallStatus.Booked
                        && recall.Status != RecallStatus.Declined, ct)
                    .ConfigureAwait(false);

                var today = _clock.Today;

                var overdue = recalls
                    .Where(recall => recall.DueOn < today)
                    .Select(recall => recall.PatientId)
                    .ToHashSet();

                // A patient already coming in does not need chasing, and a campaign that
                // texts them anyway is the reason practices stop trusting the recall list.
                var booked = await _appointments
                    .ListAsync(appointment => appointment.StartUtc >= _clock.UtcNow, ct)
                    .ConfigureAwait(false);

                var comingIn = booked
                    .Where(appointment => appointment.Status
                        is not (AppointmentStatus.Cancelled or AppointmentStatus.FailedToAttend))
                    .Select(appointment => appointment.PatientId)
                    .ToHashSet();

                return patients
                    .Where(patient => overdue.Contains(patient.Id)
                        && !comingIn.Contains(patient.Id))
                    .ToList();
            }

            case "no-visit-12m":
            {
                var cutoff = _clock.UtcNow.AddMonths(-LapsedMonths);

                // Not-archived rather than Active, for the same reason the consent filter
                // is: RecallDue is a state of being a current patient, and a lapsed
                // patient flagged as due is the one this segment most wants.
                return patients
                    .Where(patient => patient.Status != PatientStatus.Archived
                        && (patient.LastSeenUtc is null || patient.LastSeenUtc < cutoff))
                    .ToList();
            }

            case "plan-unaccepted":
            {
                var plans = await _plans
                    .ListAsync(plan => plan.Status == TreatmentPlanStatus.Presented, ct)
                    .ConfigureAwait(false);

                var waiting = plans.Select(plan => plan.PatientId).ToHashSet();

                return patients
                    .Where(patient => waiting.Contains(patient.Id))
                    .ToList();
            }

            default:
                return patients
                    .Where(patient => patient.Status != PatientStatus.Archived)
                    .ToList();
        }
    }
}
