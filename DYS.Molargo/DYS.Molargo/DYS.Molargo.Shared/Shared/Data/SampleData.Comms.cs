using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Services;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Data;

/// <summary>
/// Message templates, the portal inbox, and the consent behind them.
/// </summary>
/// <remarks>
/// <para>
/// Seeded to exercise the screen's own judgement rather than to look tidy: a template
/// whose merge fields all resolve and one whose reminder tokens cannot, an SMS long enough
/// to bill as two parts, a marketing template that needs an opt-in, a thread waiting on a
/// reply and one already answered, and patients whose consent is opted out, opted in with
/// a provenance, and opted in with none.
/// </para>
/// <para>
/// Nothing here has been sent. Every template is a draft the practice would use, and the
/// portal messages are inbound — written by patients, which is the one direction that does
/// not require the app to have a gateway.
/// </para>
/// </remarks>
internal static partial class SampleData
{
    private static IEnumerable<MessageTemplate> Templates()
    {
        // Two reminders at different distances, which is how practices actually run them:
        // one far enough out to rebook, one close enough to stop a no-show.
        yield return Template("reminder-48", "Appointment reminder — 2 days",
            CommunicationChannel.Sms, MessagePurpose.AppointmentReminder,
            MessageTrigger.BeforeAppointment,
            "Hi {{PatientFirstName}}, a reminder of your appointment with "
                + "{{ProviderName}} on {{AppointmentDate}} at {{AppointmentTime}}. "
                + "Reply YES to confirm or call {{PracticePhone}} to change it.",
            description: "Goes out two days ahead so there is time to rebook the slot.",
            hoursBefore: 48);

        yield return Template("reminder-2", "Appointment reminder — same day",
            CommunicationChannel.Sms, MessagePurpose.AppointmentReminder,
            MessageTrigger.BeforeAppointment,
            "{{PatientFirstName}}, see you at {{AppointmentTime}} today for "
                + "{{Procedure}}. {{PracticeName}}",
            description: "A short nudge on the morning of the visit.",
            hoursBefore: 2);

        // Recall, on the channel a recall actually goes out on.
        yield return Template("recall-due", "Recall due",
            CommunicationChannel.Sms, MessagePurpose.Recall,
            MessageTrigger.RecallDue,
            "Hi {{PatientFirstName}}, your check-up with {{PracticeName}} is due. "
                + "Book at {{BookingLink}} or call {{PracticePhone}}.",
            description: "Sent the day a recall falls due.");

        // Post-op. Long on purpose: rendered it runs past 160 characters, so the editor's
        // segment count has something to warn about.
        yield return Template("postop", "Post-op check — day after",
            CommunicationChannel.Sms, MessagePurpose.Aftercare,
            MessageTrigger.AfterAppointment,
            "Hi {{PatientFirstName}}, hope you are comfortable after yesterday's "
                + "{{Procedure}} with {{ProviderName}}. Some tenderness is normal for a "
                + "few days. If you have swelling, bleeding that will not settle, or pain "
                + "that is getting worse, call us on {{PracticePhone}} straight away.",
            description: "Day-one check after operative treatment.",
            hoursAfter: 24);

        yield return Template("account-overdue", "Account reminder",
            CommunicationChannel.Email, MessagePurpose.AccountNotice,
            MessageTrigger.AccountOverdue,
            "Hi {{PatientFirstName}},\n\nOur records show {{AmountOwing}} outstanding on "
                + "your account at {{PracticeName}}. If you have already paid, please "
                + "ignore this note — otherwise call us on {{PracticePhone}} and we will "
                + "sort out a time to settle it.\n\nThank you,\n{{PracticeName}}",
            description: "Goes out once an invoice passes its due date.",
            subject: "Your account at {{PracticeName}}");

        // Marketing, which needs an opt-in — so the "Needs opt-in" chip and the campaign
        // audience's consent filter both have something to act on.
        yield return Template("whitening", "Whitening offer",
            CommunicationChannel.Email, MessagePurpose.Marketing,
            MessageTrigger.Manual,
            "Hi {{PatientFirstName}},\n\nTake-home whitening trays are 20% off at "
                + "{{PracticeName}} until the end of the month. Reply to this email or "
                + "call {{PracticePhone}} if you would like to know whether whitening "
                + "suits your teeth.\n\n{{PracticeName}}",
            description: "Promotional. Requires marketing consent.",
            subject: "Whitening offer — 20% off take-home trays");

        // Review request. Sits in the automated list and is the only template pointing at
        // a capability — the review funnel — that has nothing behind it at all.
        yield return Template("review", "Review request",
            CommunicationChannel.Sms, MessagePurpose.Marketing,
            MessageTrigger.ReviewRequest,
            "Thanks for coming in, {{PatientFirstName}}. If you have a moment, we would "
                + "appreciate a review — it helps other patients find us.",
            description: "After a completed visit. No tracked link exists, so nothing "
                + "can measure whether it worked.",
            hoursAfter: 6,
            isActive: false);

        yield return Template("birthday", "Birthday greeting",
            CommunicationChannel.Sms, MessagePurpose.Marketing,
            MessageTrigger.Birthday,
            "Happy birthday, {{PatientFirstName}} — from everyone at {{PracticeName}}.",
            description: "Goodwill rather than promotion, but it still needs marketing "
                + "consent.",
            isActive: false);

        // The one template in this list the app actually sends. Admin → Users fires it the
        // moment an administrator resets a password, so it needs no scheduler — which is
        // what every other trigger here is still waiting for.
        //
        // It does NOT carry the new password, and there is no token for one. Email is not a
        // channel to put a credential on: it sits in a mailbox, in a sent folder, and on
        // whatever indexed it in between. The person who reset it reads it out; this tells
        // the owner of the account that it happened, which is the half that has to arrive
        // even when nobody is expecting it.
        yield return Template("password-reset", "Password reset",
            CommunicationChannel.Email, MessagePurpose.SecurityNotice,
            MessageTrigger.PasswordReset,
            "Hello {{StaffName}},\n\n"
                + "Your Molargo password was reset by {{ResetBy}} on {{ResetAt}}.\n\n"
                + "Ask them for the new one — it is not in this email, and nobody can "
                + "send it to you. You will need it with your username "
                + "{{StaffUsername}} and the clinic code {{ClinicCode}}.\n\n"
                + "If you were not expecting this, tell {{ResetBy}} straight away: "
                + "whoever set the password can sign in as you until it is changed.\n\n"
                + "{{PracticeName}}",
            description: "Sent automatically when an administrator resets a password "
                + "under Admin → Users. Turning this off leaves a password change with "
                + "nobody but the administrator knowing it happened.",
            subject: "Your Molargo password was reset");

        // The other half of the pair, and the other direction: this one hands somebody a
        // link to set their own password, where the one above tells them an administrator
        // already did. Two templates rather than one, because "your password was reset" is
        // alarming when it has not been, and a single wording covering both occasions has
        // to be vague about which happened.
        //
        // Carries a link and no password, and always will — {{ResetLink}} is a token that
        // dies in thirty minutes and after one use, which a password is not.
        yield return Template("password-reset-link", "Password reset link",
            CommunicationChannel.Email, MessagePurpose.SecurityNotice,
            MessageTrigger.PasswordResetRequested,
            "Hello {{StaffName}},\n\n"
                + "Somebody asked to reset the password for {{StaffUsername}} at "
                + "{{PracticeName}}.\n\n"
                + "Open this link to set a new one. It works once and expires in "
                + "{{LinkExpiry}}:\n\n{{ResetLink}}\n\n"
                + "If that was not you, ignore this email — your password has not changed "
                + "and the link will lapse on its own.\n\n"
                + "{{PracticeName}}",
            description: "Sent when somebody uses Forgot password? on the sign-in screen. "
                + "Turning this off leaves a practice with no self-service reset, which "
                + "matters most to a sole owner — nobody else can reset an owner.",
            subject: "Reset your Molargo password");
    }

    private static MessageTemplate Template(
        string key,
        string name,
        CommunicationChannel channel,
        MessagePurpose purpose,
        MessageTrigger trigger,
        string body,
        string? description = null,
        string? subject = null,
        int? hoursBefore = null,
        int? hoursAfter = null,
        bool isActive = true) =>
        new()
        {
            Id = Id($"template:{key}"),
            Name = name,
            Channel = channel,
            Purpose = purpose,
            Trigger = trigger,
            Body = body,
            Description = description,
            Subject = subject,
            SendHoursBeforeAppointment = hoursBefore,
            SendHoursAfterAppointment = hoursAfter,
            IsActive = isActive,
        };

    /// <summary>
    /// The design's three portal threads.
    /// </summary>
    /// <remarks>
    /// Inbound, and only the first is unanswered — a list where every row needs a reply
    /// hides which one has been waiting longest, and the sort that puts unanswered threads
    /// first has nothing to prove.
    /// </remarks>
    private static IEnumerable<CommunicationLog> PortalMessages(DateOnly today)
    {
        // Margaret Yuen, the design's open thread. Clinical, and unanswered — which is the
        // row the "awaiting reply" count is for.
        yield return Portal("yuen-1", "10201", inbound: true, today, new TimeOnly(8, 20),
            "Hi, my tooth is a bit sensitive to cold since the crown prep. Is that normal?");

        // Lucy Tran is not a seeded patient, so the design's second thread goes to one who
        // is. Answered yesterday, so the thread sorts below Margaret's.
        yield return Portal("reyes-1", "10205", inbound: true, today.AddDays(-1),
            new TimeOnly(15, 42),
            "I have lost the splint you made me — can I get a replacement?");

        yield return Portal("reyes-2", "10205", inbound: false, today.AddDays(-1),
            new TimeOnly(16, 10),
            "No problem — we can take a new impression at your next visit. "
                + "I will add it to the appointment so we allow time.",
            byProvider: "ellery",
            status: CommunicationStatus.Delivered);

        // Kevin Yuen's billing question, answered on Friday.
        yield return Portal("kevin-1", "10202", inbound: true, today.AddDays(-4),
            new TimeOnly(11, 5),
            "Can you tell me what the $88 line on my last invoice was for?");

        yield return Portal("kevin-2", "10202", inbound: false, today.AddDays(-4),
            new TimeOnly(11, 40),
            "That is the OPG — the full-mouth x-ray taken at your exam. "
                + "Your fund paid $88 of the $110 fee.",
            byProvider: "brennan",
            status: CommunicationStatus.Delivered);
    }

    private static CommunicationLog Portal(
        string key,
        string patientNumber,
        bool inbound,
        DateOnly day,
        TimeOnly time,
        string body,
        string? byProvider = null,
        CommunicationStatus status = CommunicationStatus.Delivered)
    {
        var at = day.ToDateTime(time, DateTimeKind.Local).ToUniversalTime();

        return new CommunicationLog
        {
            Id = Id($"portal:{key}"),
            PatientId = Id($"patient:{patientNumber}"),
            Channel = CommunicationChannel.PatientPortal,
            Direction = inbound
                ? CommunicationDirection.Inbound
                : CommunicationDirection.Outbound,
            Purpose = MessagePurpose.General,
            Status = status,
            Body = body,

            // Set on both directions so a thread orders correctly. An inbound message has
            // no "sent by us" time, but it does have a time it arrived, and ordering a
            // conversation by a null puts the question after its answer.
            SentUtc = at,
            DeliveredUtc = status == CommunicationStatus.Delivered ? at : null,
            SentByProviderId = byProvider is null ? null : Id($"provider:{byProvider}"),
        };
    }

    /// <summary>
    /// Records where each patient's consent came from.
    /// </summary>
    /// <remarks>
    /// Applied to the already-tracked patients rather than widening the Patient factory,
    /// which every one of the twenty-five calls. The consent bools are seeded there; what
    /// is added here is the provenance and the timestamp that make them evidence.
    /// </remarks>
    private static void AlignConsent(MolargoDbContext db, DateOnly today)
    {
        var patients = db.ChangeTracker.Entries<PatientEntity>()
            .Select(entry => entry.Entity)
            .ToList();

        // The design's three named rows, so the pane's own examples are the ones on screen.
        var named = new Dictionary<string, (bool Reminders, bool Marketing, string? Source, int OptedDaysAgo)>
        {
            // Opted out of marketing, on a signed form. Reminders still on: a patient
            // refusing promotions while wanting to be told about their appointment is the
            // normal case, and the whole reason the two consents are separate.
            ["10201"] = (true, false, "Signed form · tablet", 422),
            ["10208"] = (true, true, "Portal settings", 190),
            ["10209"] = (false, false, "Phone request · logged", 96),
        };

        var index = 0;

        foreach (var patient in patients.OrderBy(entry => entry.PatientNumber))
        {
            if (patient.PatientNumber is { } number && named.TryGetValue(number, out var set))
            {
                patient.ReminderConsent = set.Reminders;
                patient.MarketingConsent = set.Marketing;
                patient.ConsentSource = set.Source;
                patient.ReminderConsentUpdatedUtc = Stamp(today, set.OptedDaysAgo);
                patient.MarketingConsentUpdatedUtc = Stamp(today, set.OptedDaysAgo);
                index++;
                continue;
            }

            // Everyone else gets a provenance except every fourth patient, who keeps
            // marketing consent with none. That is the row the "without a source" count
            // exists to find, and a list where every row is complete never shows it.
            if (patient.MarketingConsent)
            {
                patient.ConsentSource = index % 4 == 0
                    ? null
                    : ConsentSourceFor(index);

                patient.MarketingConsentUpdatedUtc = Stamp(today, 30 + index * 11);
            }
            else
            {
                // An opt-out inside this month, so the compliance panel's monthly figure is
                // not always zero.
                patient.MarketingConsentUpdatedUtc = index % 3 == 0
                    ? Stamp(today, Math.Min(today.Day - 1, 5))
                    : Stamp(today, 60 + index * 7);

                // How the withdrawal was requested, and varied. A source is worth keeping
                // on an opt-out — it is the evidence the practice acted on a real request
                // — but the same words on every row read as a default nobody set, which
                // is exactly what a provenance column must not look like.
                patient.ConsentSource ??= index % 2 == 0
                    ? "Phone request · logged"
                    : ConsentSourceFor(index);
            }

            patient.ReminderConsentUpdatedUtc = Stamp(today, 45 + index * 9);
            index++;
        }
    }

    /// <summary>
    /// Records how each patient found the practice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The design's five channels, spread across the seeded patients — and deliberately
    /// not across all of them. Every patient carried no source at all, which collapsed the
    /// reports screen's acquisition table to one row reading "Not recorded": true, but it
    /// proved nothing about the code that groups and ranks the sources.
    /// </para>
    /// <para>
    /// Roughly one in six is left blank on purpose. A source not captured at registration
    /// is acquisition the practice cannot attribute, and it is the one row on that table
    /// the front desk can act on today.
    /// </para>
    /// </remarks>
    private static void AlignReferralSources(MolargoDbContext db)
    {
        var sources = new[]
        {
            "Word of mouth",
            "Google Ads",
            "GP referral",
            "Instagram",
            "Word of mouth",
            null,
            "Walk-in",
            "Word of mouth",
            "Google Ads",
        };

        var index = 0;

        foreach (var patient in db.ChangeTracker.Entries<PatientEntity>()
            .Select(entry => entry.Entity)
            .OrderBy(entry => entry.PatientNumber))
        {
            // Only where the patient has none. A source set deliberately elsewhere in the
            // seed is a decision, and overwriting it here would quietly undo it.
            patient.ReferralSource ??= sources[index % sources.Length];
            index++;
        }
    }

    /// <summary>
    /// Records when each clinician's AHPRA registration lapses.
    /// </summary>
    /// <remarks>
    /// One current, one inside the ninety-day warning window, and one already expired —
    /// so the admin screen's three registration states each have a row, and the "cannot
    /// sign" count is not always zero. Cathy Brennan is not a clinician and gets nothing,
    /// which is the fourth case the pane has to handle.
    /// </remarks>
    private static void AlignRegistrations(MolargoDbContext db, DateOnly today)
    {
        var expiries = new Dictionary<string, int>
        {
            // Comfortably current.
            ["provider:vance"] = 280,

            // Inside the warning window: renewal notice already out.
            ["provider:ito"] = 46,

            // Lapsed. Dr Ellery is the session's default author, so the screen shows the
            // uncomfortable case honestly — the person the app is signing in as cannot
            // lawfully sign anything.
            ["provider:ellery"] = -23,
        };

        foreach (var provider in db.ChangeTracker.Entries<Provider>()
            .Select(entry => entry.Entity))
        {
            foreach (var (key, offset) in expiries)
            {
                if (provider.Id != Id(key)) continue;

                provider.AhpraExpiresOn = today.AddDays(offset);
                break;
            }
        }
    }

    /// <summary>
    /// Gives the sample staff a username and a password so the sign-in screen is usable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hashed through the real hasher, not stored as plaintext with a "demo" comment. The
    /// seed is the one place where writing a bare password would be excusable and it is
    /// also the place it would get copied from, so it goes through exactly the path a real
    /// password does.
    /// </para>
    /// <para>
    /// The password itself is the same weak string for everyone, which is fine for a
    /// demonstration build and would not be for anything else — the sign-in screen says the
    /// build is a demonstration, and there is no password-change screen to fix it with.
    /// </para>
    /// </remarks>
    private static void AlignCredentials(
        MolargoDbContext db, IPasswordHasher hasher, DateTime now)
    {
        // One hash computed once and shared. PBKDF2 at 210,000 iterations takes a couple
        // of hundred milliseconds; doing it per staff member would add a visible pause to
        // the app's very first launch for no benefit, since the password is identical.
        var shared = hasher.Hash(DemoPassword);

        foreach (var provider in db.ChangeTracker.Entries<Provider>()
            .Select(entry => entry.Entity))
        {
            if (!provider.IsActive) continue;

            // First initial and surname, lowercased — the convention a practice would
            // actually use, and stable enough to quote over the phone.
            provider.Username =
                $"{provider.FirstName[..1]}{provider.LastName}".ToLowerInvariant();

            provider.PasswordHash = shared;
            provider.PasswordUpdatedUtc = now;
        }
    }

    /// <summary>
    /// The seeded password, shown on no screen.
    /// </summary>
    /// <remarks>
    /// A constant rather than a literal at the call site so it is findable, and internal so
    /// nothing outside the seed can read it into a UI. Whoever runs the demonstration is
    /// told it out of band.
    /// </remarks>
    internal const string DemoPassword = "molargo-demo";

    private static DateTime Stamp(DateOnly today, int daysAgo) =>
        today.AddDays(-Math.Max(daysAgo, 0))
            .ToDateTime(new TimeOnly(9, 30), DateTimeKind.Local)
            .ToUniversalTime();

    private static string ConsentSourceFor(int index) => (index % 3) switch
    {
        0 => "Signed form · tablet",
        1 => "Portal settings",
        _ => "In person · front desk",
    };
}
