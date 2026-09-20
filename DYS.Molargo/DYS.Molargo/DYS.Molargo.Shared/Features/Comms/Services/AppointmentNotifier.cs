using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Components;
using DYS.Molargo.Shared.Data;
using DYS.Molargo.Shared.Services;
using Microsoft.EntityFrameworkCore;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Features.Comms.Services;

/// <summary>Whether a patient can be reached on a channel, and why not when they cannot.</summary>
public sealed record NotifyOptions(
    bool CanEmail,
    string? EmailBlockedReason,
    bool CanText,
    string? TextBlockedReason);

/// <summary>What came of trying to tell a patient about their appointment.</summary>
/// <remarks>
/// One sentence per channel rather than a bool. "Emailed" and "not emailed" are not the
/// only outcomes — the interesting one is "written down but not sent", and a caller with a
/// bool cannot say that.
/// </remarks>
public sealed record NotifyResult(string? EmailOutcome, string? TextOutcome)
{
    public bool DidAnything => EmailOutcome is not null || TextOutcome is not null;

    /// <summary>The two outcomes as one line, for a confirmation banner.</summary>
    public string Summary =>
        string.Join(" ", new[] { EmailOutcome, TextOutcome }.Where(part => part is not null));
}

/// <summary>
/// Tells a patient about an appointment, by email, by text, or both.
/// </summary>
/// <remarks>
/// <para>
/// Its own service rather than six more dependencies on <c>AppointmentService</c>. Booking
/// a slot and telling somebody about it are different jobs: one writes the diary and must
/// not fail because a mail server is down, and this one reaches the outside world.
/// </para>
/// <para>
/// Every send is written to the patient's communication log whatever happens to it — sent,
/// failed, or queued and never transmitted. The log is the practice's answer to "were they
/// told", and an attempt that left no trace is the question with no answer.
/// </para>
/// </remarks>
public interface IAppointmentNotifier
{
    /// <summary>Whether this patient can be reached, for the checkboxes on the form.</summary>
    Task<NotifyOptions> OptionsForAsync(Guid patientId, CancellationToken ct = default);

    /// <summary>
    /// Sends what was asked for. Never throws — a booking is already written by the time
    /// this runs, and losing it to a mail failure would be the worse outcome.
    /// </summary>
    Task<NotifyResult> NotifyAsync(
        Guid appointmentId, bool email, bool text, CancellationToken ct = default);
}

/// <inheritdoc cref="IAppointmentNotifier"/>
public sealed class AppointmentNotifier : IAppointmentNotifier
{
    private readonly MolargoDatabase _database;
    private readonly IEmailSender _email;
    private readonly ISmsGatewayResolver _sms;
    private readonly ISmsSender _texts;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public AppointmentNotifier(
        MolargoDatabase database,
        IEmailSender email,
        ISmsGatewayResolver sms,
        ISmsSender texts,
        ITenantContext tenant,
        IClock clock)
    {
        _database = database;
        _email = email;
        _sms = sms;
        _texts = texts;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<NotifyOptions> OptionsForAsync(
        Guid patientId, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var patient = await db.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == patientId && !row.IsDeleted, ct)
            .ConfigureAwait(false);

        if (patient is null)
        {
            return new NotifyOptions(false, "No patient chosen yet.", false, "No patient chosen yet.");
        }

        // Consent first, because it overrides both channels and is the one refusal that is
        // about the patient's wishes rather than about what the practice has set up.
        if (!patient.ReminderConsent)
        {
            const string refused =
                "This patient has asked not to be sent reminders — Comms → Preferences.";

            return new NotifyOptions(false, refused, false, refused);
        }

        var settings = await db.NotificationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(row => !row.IsDeleted, ct)
            .ConfigureAwait(false);

        var emailReason =
            string.IsNullOrWhiteSpace(patient.Email) ? "No email address on their record."
            : settings is null || !settings.IsConfigured
                ? "The practice has no mail account set up — Admin → Settings."
            : !settings.EmailEnabled ? "Email is switched off under Admin → Settings."
            : null;

        var practice = await db.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.Id == _tenant.TenantId && !row.IsDeleted, ct)
            .ConfigureAwait(false);

        var pricing = await _sms.PricingForCurrentTenantAsync(ct).ConfigureAwait(false);

        var textReason =
            string.IsNullOrWhiteSpace(patient.Mobile) ? "No mobile number on their record."
            : practice?.SmsEnabled != true
                ? "Text messages are switched off for this practice — Admin → Plan."
            : !pricing.Available
                ? "No SMS provider is set up for this country yet."
            : null;

        return new NotifyOptions(
            emailReason is null, emailReason,
            textReason is null, textReason);
    }

    public async Task<NotifyResult> NotifyAsync(
        Guid appointmentId, bool email, bool text, CancellationToken ct = default)
    {
        if (!email && !text) return new NotifyResult(null, null);

        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var appointment = await db.Appointments
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == appointmentId && !row.IsDeleted, ct)
            .ConfigureAwait(false);

        if (appointment is null) return new NotifyResult(null, null);

        var patient = await db.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == appointment.PatientId, ct)
            .ConfigureAwait(false);

        if (patient is null) return new NotifyResult(null, null);

        var practice = await db.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.Id == _tenant.TenantId, ct)
            .ConfigureAwait(false);

        var emailOutcome = email
            ? await SendEmailAsync(db, appointment, patient, practice, ct).ConfigureAwait(false)
            : null;

        var textOutcome = text
            ? await SendTextAsync(db, appointment, patient, practice, ct).ConfigureAwait(false)
            : null;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new NotifyResult(emailOutcome, textOutcome);
    }

    private async Task<string> SendEmailAsync(
        MolargoDbContext db,
        Appointment appointment,
        PatientEntity patient,
        Tenant? practice,
        CancellationToken ct)
    {
        var settings = await db.NotificationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(row => !row.IsDeleted, ct)
            .ConfigureAwait(false);

        var (subject, body) = await ComposeAsync(
                db, appointment, patient, practice, CommunicationChannel.Email, ct)
            .ConfigureAwait(false);

        var log = NewLog(appointment, patient, CommunicationChannel.Email, subject, body,
            patient.Email);

        if (settings is null || !settings.IsConfigured || string.IsNullOrWhiteSpace(patient.Email))
        {
            log.Status = CommunicationStatus.Failed;
            log.FailureReason = "No mail account configured, or no address on the record.";
            db.CommunicationLogs.Add(log);

            return "Email not sent — no mail account or address.";
        }

        try
        {
            var sent = await _email
                .SendAsync(settings, patient.Email!, subject, body, ct,
                    purpose: "Appointment", patientId: patient.Id)
                .ConfigureAwait(false);

            if (sent.Succeeded)
            {
                log.Status = CommunicationStatus.Sent;
                log.SentUtc = _clock.UtcNow;
                db.CommunicationLogs.Add(log);

                return "Emailed.";
            }

            log.Status = CommunicationStatus.Failed;
            log.FailureReason = sent.Detail;
            db.CommunicationLogs.Add(log);

            return "Email failed — see the patient's comms log.";
        }
        catch (Exception ex)
        {
            // Caught, because the booking is already written. A mail server refusing must
            // not turn a saved appointment into an error the front desk reads as "not
            // booked" and books again.
            log.Status = CommunicationStatus.Failed;
            log.FailureReason = ex.Message;
            db.CommunicationLogs.Add(log);

            return "Email failed — see the patient's comms log.";
        }
    }

    /// <summary>
    /// Sends the text, and writes what came of it to the log.
    /// </summary>
    /// <remarks>
    /// The log row is written either way. A message that failed is the one somebody needs
    /// to find — "we texted you" against a carrier that refused the number is the argument
    /// this log exists to settle — and only a Sent row is counted onto the practice's bill.
    /// </remarks>
    private async Task<string> SendTextAsync(
        MolargoDbContext db,
        Appointment appointment,
        PatientEntity patient,
        Tenant? practice,
        CancellationToken ct)
    {
        var (_, body) = await ComposeAsync(
                db, appointment, patient, practice, CommunicationChannel.Sms, ct)
            .ConfigureAwait(false);

        var log = NewLog(appointment, patient, CommunicationChannel.Sms, null, body,
            patient.Mobile);

        var sent = await _texts
            .SendAsync(patient.Mobile, body, ct,
                purpose: "Appointment", patientId: patient.Id)
            .ConfigureAwait(false);

        // The number the carrier was actually given, where there was one. The record holds
        // "0400 123 456" and the gateway was handed "+61400123456", and the log is where
        // somebody checks that those are the same phone.
        if (sent.Number is { Length: > 0 }) log.Recipient = sent.Number;

        if (sent.Succeeded)
        {
            log.Status = CommunicationStatus.Sent;
            log.SentUtc = _clock.UtcNow;
            db.CommunicationLogs.Add(log);

            return "Texted.";
        }

        log.Status = CommunicationStatus.Failed;
        log.FailureReason = sent.Detail;
        db.CommunicationLogs.Add(log);

        return "Text failed — see the patient's comms log.";
    }

    private CommunicationLog NewLog(
        Appointment appointment,
        PatientEntity patient,
        CommunicationChannel channel,
        string? subject,
        string body,
        string? recipient)
    {
        var now = _clock.UtcNow;

        return new CommunicationLog
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            PatientId = patient.Id,
            AppointmentId = appointment.Id,
            Channel = channel,
            Direction = CommunicationDirection.Outbound,
            // The occasion, which is what the comms log groups by — not General, which
            // would file a booking confirmation with the ad-hoc replies.
            Purpose = MessagePurpose.AppointmentReminder,
            Subject = subject,
            Body = body,
            Recipient = recipient,
            CreatedUtc = now,
            UpdatedUtc = now,
        };
    }

    /// <summary>
    /// The wording, from the practice's own template where there is one.
    /// </summary>
    /// <remarks>
    /// The appointment-reminder template, per channel — an SMS and an email for the same
    /// occasion are different lengths and different tones, and the practice has both. Falls
    /// back to a plain sentence rather than refusing: somebody ticked a box to tell a
    /// patient, and a missing template is not a reason to leave them untold.
    /// </remarks>
    private async Task<(string Subject, string Body)> ComposeAsync(
        MolargoDbContext db,
        Appointment appointment,
        PatientEntity patient,
        Tenant? practice,
        CommunicationChannel channel,
        CancellationToken ct)
    {
        var template = await db.MessageTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(
                row => row.Trigger == MessageTrigger.BeforeAppointment
                    && row.Channel == channel
                    && row.IsActive
                    && !row.IsDeleted,
                ct)
            .ConfigureAwait(false);

        var local = appointment.StartUtc.ToLocalTime();
        var practiceName = practice?.Name ?? "your dentist";

        var provider = await db.Providers
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == appointment.ProviderId, ct)
            .ConfigureAwait(false);

        var providerName = provider?.FullName ?? "your dentist";

        // The site the appointment is at, for its phone number. Left null this reached a
        // patient as "call {{PracticePhone}}" — the seeded reminder ends with it, so the
        // one token nobody noticed was the one in every message.
        var site = await db.PracticeLocations
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == appointment.PracticeLocationId, ct)
            .ConfigureAwait(false);

        // The names in MergeFields.All, not ones invented here. The first version used
        // FirstName and FullName, which no template uses — so the seeded reminder rendered
        // as "Hi {{PatientFirstName}}" and went into the log with its braces showing.
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PatientFirstName"] = patient.FirstName,
            ["AppointmentDate"] = MolargoFormat.Date(DateOnly.FromDateTime(local)),
            ["AppointmentTime"] = local.ToString("h:mm tt"),
            ["ProviderName"] = providerName,
            ["Procedure"] = appointment.Reason,
            ["PracticeName"] = practiceName,

            ["PracticePhone"] = site?.Phone,

            // Left null rather than guessed: there is no booking portal and no balance is
            // being chased here. MergeFields reports them unresolved, which is the truth —
            // a link invented here would be a dead one in a patient's hand.
            ["BookingLink"] = null,
            ["AmountOwing"] = null,
        };

        var fallback =
            $"Hello {patient.FirstName}, your appointment at {practiceName} is on "
            + $"{MolargoFormat.Date(DateOnly.FromDateTime(local))} at {local:h:mm tt}.";

        var subject = MergeFields
            .Render(template?.Subject ?? $"Your appointment at {practiceName}", values)
            .Text;

        return (subject, MergeFields.Render(template?.Body ?? fallback, values).Text);
    }
}
