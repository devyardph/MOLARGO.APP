using DYS.Molargo.Domain.Entities;

namespace DYS.Molargo.Domain;

/// <summary>
/// The help articles a new database starts with.
/// </summary>
/// <remarks>
/// <para>
/// The seed, not the library. The articles live in a table so the vendor can correct them
/// without a release — see <see cref="HelpArticle"/> — and this is what that table is
/// filled with the first time it exists.
/// </para>
/// <para>
/// Written to be honest about what is not built, because a help page that describes
/// features the app does not have is worse than no help page: it sends somebody hunting for
/// a button that was never there, and they conclude the fault is theirs.
/// </para>
/// </remarks>
public static class HelpLibrary
{
    /// <summary>
    /// Every shipped article, in the order they are worth reading.
    /// </summary>
    /// <remarks>
    /// A method rather than a static list, because each caller needs its own instances to
    /// stamp with a tenant and an id — handing the same objects to two seeds would have the
    /// second one overwrite the first one's keys.
    /// </remarks>
    public static IEnumerable<HelpArticle> Seed()
    {
        yield return new HelpArticle
        {
            Slug = "what-this-is",
            Category = "Getting started",
            Title = "What Molargo is, and what it is not",
            Summary = "The shape of the app, and an honest list of what has not been built.",
            Keywords = "overview about introduction offline sync",
            DisplayOrder = 0,
            Body =
                """
                Molargo runs a dental practice from one patient record: the diary, the chart,
                billing, prescribing and the worklists that hang off them. It is offline-first —
                the database is a file on this machine, so the surgery keeps working when the
                connection does not.

                What is not built, said plainly so nobody goes looking:

                • No online booking, and no patient portal.
                • No payment gateway. Invoices and subscription charges are records; no card is
                  ever taken.
                • No health-fund claiming. Claims can be recorded, not submitted.
                • No electronic prescribing token. A script is printed or handed over.
                • No syncing between machines. Each installation is its own database.

                Where a screen needs something that does not exist, it says so on the screen
                rather than showing an empty box.
                """,
        };

        yield return new HelpArticle
        {
            Slug = "signing-in",
            Category = "Getting started",
            Title = "Signing in, and getting back in",
            Summary = "Clinic code, username, password — and what to do when one of them is lost.",
            Keywords = "login password reset locked two-factor 2fa mfa forgot",
            DisplayOrder = 10,
            Body =
                """
                Signing in takes three things: the clinic code, your username and your password.
                The clinic code identifies the practice, so two practices can both have a
                "jsmith" without colliding.

                Five wrong passwords lock the account for fifteen minutes. Every attempt,
                successful or not, is written to the audit log under Admin.

                Forgotten password: use the link on the sign-in screen. The code is emailed
                through the practice's own mail account, so that account has to be set up under
                Admin → Settings first — otherwise nobody can reset anything and another user
                with staff access has to set a new password on your record.

                Two-step sign-in can be switched on per person under Admin → Users. It emails a
                six-digit code, so it needs both an email address on the record and the mail
                account configured. The screen refuses to switch it on without them, because an
                account whose correct password is met by a code that can never arrive is an
                account nobody can use.
                """,
        };

        yield return new HelpArticle
        {
            Slug = "creating-a-practice",
            Category = "Getting started",
            Title = "Creating a practice",
            Summary = "What signup asks for, and the emailed code that finishes it.",
            Keywords = "signup sign up register new clinic trial verification",
            DisplayOrder = 20,
            Body =
                """
                Create practice takes four steps: the practice, your details, a password, and a
                six-digit code emailed to the address you gave.

                Nothing is created until that code comes back. An address nobody reads leaves no
                practice, no staff record and nothing for anybody to clean up.

                The code goes through the platform's own mail account, which whoever runs
                Molargo sets up under Platform → Email. If it has not been set up, the last step
                says so — the fix is theirs, not yours.

                A new practice starts with reference data already in place: the medical history
                questionnaire, consent forms, message templates, stock categories, the fee
                catalogue and the formulary. No patients and no appointments — those are yours.
                """,
        };

        yield return new HelpArticle
        {
            Slug = "the-diary",
            Category = "Diary",
            Title = "The diary: day, week, month and list",
            Summary = "Four views of the same appointments, and when each one earns its place.",
            Keywords = "calendar appointments schedule find search booking",
            DisplayOrder = 30,
            Body =
                """
                Day is the working view: one column per chair, drawn against the site's own
                opening hours. Anything the grid cannot place — no chair, or a time outside
                opening — appears beneath it rather than being hidden, because an appointment
                that exists in the database and on no screen is the worst outcome available.

                Week and month are for finding space.

                List is different: every appointment at the site, newest first, with a search box
                and paging. Day, week and month answer "what is on then"; List answers "where is
                that appointment", which is why it is the only one with a search box. It searches
                patient name and number, provider, chair, reason, date and status — so "yuen
                crown" finds Margaret Yuen's crown prep. The date arrows disappear in List,
                because it is drawn against no date.
                """,
        };

        yield return new HelpArticle
        {
            Slug = "booking",
            Category = "Diary",
            Title = "Booking and changing an appointment",
            Summary = "What the form checks before it saves, and how to tell the patient.",
            Keywords = "appointment new book reschedule move cancel notify",
            DisplayOrder = 40,
            Body =
                """
                New appointment takes a patient, a type, a provider, a chair and a time. The
                pre-booking panel on the right shows their medical alerts, outstanding lab work,
                account balance and missed-appointment history before anything is saved.

                The form warns rather than blocks in most cases — a double-booking is sometimes
                deliberate — but it refuses a day the practice is closed.

                "Tell the patient" sends an email, a text, or both, as soon as the booking is
                saved. Each box explains itself when it cannot be used: no address on the record,
                no mail account set up, texts switched off for the practice, or the patient
                having asked not to be sent reminders. Whatever is sent is written to their
                communication log, including a message that failed — that log is the answer to
                "were they told".
                """,
        };

        yield return new HelpArticle
        {
            Slug = "recalls",
            Category = "Diary",
            Title = "Recalls: how a patient gets back on the list",
            Summary = "Created automatically when a visit finishes, and cleared when the next one is booked.",
            Keywords = "recall due overdue chase hygiene six month reminder list",
            DisplayOrder = 50,
            Body =
                """
                A recall is created when a visit is marked Completed, if the appointment's type
                carries a recall interval. Exam & clean and Scale & polish do; a crown fit, an
                extraction and a root canal do not, because they finish a course of treatment
                rather than starting the next one. The intervals live on the appointment type, so
                a practice can change them.

                One recall per patient. Finishing another visit rolls the existing one forward
                rather than adding a second — two rows would be two people ringing the same
                patient in the same week.

                The patient's own interval wins over the type's. A periodontal patient on three
                months does not revert to six because they came in for a routine clean.

                Booking an appointment at or near the due date marks the recall Booked and takes
                it off the worklist. A booking well before the due date does not: somebody coming
                in for a broken tooth next week has not had their check-up.

                A patient who has declined recalls is never put back on the list by attending.
                """,
        };

        yield return new HelpArticle
        {
            Slug = "short-notice-list",
            Category = "Diary",
            Title = "The short-notice list",
            Summary = "Who to ring when a slot frees up, and how they get on it.",
            Keywords = "waitlist cancellation gap fill short notice offer",
            DisplayOrder = 60,
            Body =
                """
                Waitlist → Add someone puts a patient on the short-notice list: what they want,
                when they can come, and how urgent it is. Everything except the patient is
                optional, because somebody taking a cancellation call has thirty seconds.

                The list is ordered urgent first, and within a priority the people who have not
                yet been offered anything — somebody already rung and not reached is a poor bet
                for a slot that expires this hour.

                Three buttons per row, because an offered slot has three outcomes. Log offer
                records that you rang and leaves them on the list. Booked and Remove take them
                off — the row is kept either way, so "you never call me" has an answer.

                One entry per patient. Offers are made by hand: automatic offers and
                first-reply-wins would need a messaging gateway racing replies, which is not
                built.
                """,
        };

        yield return new HelpArticle
        {
            Slug = "reminders",
            Category = "Diary",
            Title = "Appointment reminders",
            Summary = "Setting the cadence, and the run that sends what is due.",
            Keywords = "reminder sms email cadence schedule automatic send",
            DisplayOrder = 70,
            Body =
                """
                Reminders → When to remind sets the cadence in days before the appointment.
                "7, 1" reminds a week out and again the day before. One number per step,
                furthest out first.

                Nothing runs on a timer. Reminders go when somebody presses "Send what is due",
                so this is a screen to open each morning rather than one that works unattended.

                Running it twice is harmless. Every attempt is recorded against the appointment
                and the step, and an attempt that already exists is never repeated — so a run
                interrupted halfway through can simply be run again.

                A patient booked inside the whole cadence gets only the most urgent step, not
                every one of them minutes apart. Cancellations and no-shows are never reminded
                about.

                A patient who cannot be reached — no mobile, no address, no consent — is recorded
                as skipped rather than retried every run until the appointment. Those are the
                rows on the screen worth acting on.
                """,
        };

        yield return new HelpArticle
        {
            Slug = "prescribing",
            Category = "Clinical",
            Title = "Prescriptions",
            Summary = "Writing a script, the allergy check, and signing it.",
            Keywords = "script rx medicine drug allergy interaction sign print",
            DisplayOrder = 80,
            Body =
                """
                New prescription draws from the practice's formulary, which arrives seeded with
                the dental shortlist — the antibiotics, the analgesics and the rinses — with
                interaction and allergy flags already on them.

                The allergy and interaction screen runs against the patient's recorded allergies
                and medications when the script is issued, and its findings are shown before
                signing. It is a screen written by hand, not a pharmacological database, and the
                prescribing screen says so.

                Issuing opens the script as a sheet of paper. Sign it with a finger or stylus and
                print it. Nothing is transmitted: an eRx token needs a prescription-exchange
                gateway, which is not built, so the printed sheet is the script.
                """,
        };

        yield return new HelpArticle
        {
            Slug = "certificates",
            Category = "Clinical",
            Title = "Medical certificates",
            Summary = "Drafting, editing the wording, and signing.",
            Keywords = "certificate sick note unfit work study days off print sign",
            DisplayOrder = 90,
            Body =
                """
                Certificates → set the days unfit and, if it is not today, the day the patient is
                unfit from. A patient seen late on a Friday is often unfit from Monday.

                Five days is the cap. Beyond that the patient needs their own doctor.

                The draft's wording can be edited freely — "light duties only", "unfit for heavy
                lifting". Keep the treatment out of it: an employer has no right to the clinical
                detail, which is why the standard wording says "dental treatment" and stops.

                Once issued the wording is fixed. Rewriting a document the patient already has
                would change what the practice said after it said it; draft a new one instead.

                Open to sign and print gives the same sheet a prescription gets. A certificate
                printed before it is issued carries a visible "Draft — not issued" band, and an
                issued one nobody signed is flagged in the list, because an unsigned sheet is one
                an employer has no reason to accept.

                Deleting takes it off the list, not out of the record: the wording is kept and
                the audit log names who removed it and what it covered.
                """,
        };

        yield return new HelpArticle
        {
            Slug = "charting",
            Category = "Clinical",
            Title = "Charting and clinical notes",
            Summary = "The tooth chart, perio, and what signing a note means.",
            Keywords = "chart teeth perio notes clinical record sign",
            DisplayOrder = 100,
            Body =
                """
                The chart records what is present and what has been done, per tooth and per
                surface. Perio charting records pocket depths and bleeding.

                A clinical note, once signed, is the record. Later changes are additions rather
                than edits, because a note that could be quietly rewritten is a note nobody can
                rely on in a complaint.
                """,
        };

        yield return new HelpArticle
        {
            Slug = "billing",
            Category = "Billing",
            Title = "Invoices, payments and claims",
            Summary = "What the billing section does, and where it stops.",
            Keywords = "invoice payment claim fee item catalogue receipt refund",
            DisplayOrder = 110,
            Body =
                """
                Invoices are raised from the fee catalogue, which a new practice starts with.
                Payments are recorded against them; the ledger is the truth, and the cached total
                on the invoice follows it.

                Claims can be recorded and tracked. Submitting them cannot: that needs HICAPS or
                a fund's own channel, which is not built.

                No card is ever taken. Every payment on an invoice is somebody recording money
                that arrived somewhere else.
                """,
        };

        yield return new HelpArticle
        {
            Slug = "inventory",
            Category = "Inventory",
            Title = "Stock, suppliers and categories",
            Summary = "Counting what is on the shelf, and what reorder levels do.",
            Keywords = "stock supplies reorder supplier category purchase order",
            DisplayOrder = 120,
            Body =
                """
                Inventory → Stock lists what the practice holds, with a search box and paging.
                Use/adjust records what came off the shelf; the movements below the item show
                what has happened to it.

                An item below its reorder level is flagged. Nothing orders anything: purchase
                orders are recorded, not sent.

                Suppliers and categories have their own tabs. Renaming a category carries its
                items with it.
                """,
        };

        yield return new HelpArticle
        {
            Slug = "comms",
            Category = "Comms",
            Title = "Templates, campaigns and the mail account",
            Summary = "Setting up sending, and what the templates are for.",
            Keywords = "email smtp app password template merge campaign sending",
            DisplayOrder = 130,
            Body =
                """
                A practice sends from its own mail account, set up under Admin → Settings: the
                address, the SMTP host and port, and an app password. That account carries
                reminders, receipts, password resets and two-step sign-in codes — so nothing that
                emails a patient works until it is set up.

                The app password is stored in this database in plain text. There is no secret
                store here and no server to hold one. Treat the database file as carrying it, and
                rotate the password at the provider if a copy of the file ever leaves.

                Templates cover SMS, email and portal messages, with merge fields for the
                patient's name, the appointment and the practice. The automated tab is the same
                templates filtered to those with a trigger.

                Campaign sending is not built.
                """,
        };

        yield return new HelpArticle
        {
            Slug = "text-messages",
            Category = "Comms",
            Title = "Text messages",
            Summary = "How texts are set up, what they cost, and what is included.",
            Keywords = "sms text message gateway cost allowance charge mobile",
            DisplayOrder = 140,
            Body =
                """
                Texts go through a gateway the vendor sets up per country, not through an account
                the practice holds. A sender id has to be registered with a carrier country by
                country, which no single clinic is going to do.

                A practice switches texts on under Admin → Plan, where it can see the rate per
                message, how many have gone this month and what they come to.

                Each plan includes a number of texts per site per month. Messages past the
                allowance are charged at the country's rate and land on the following month's
                subscription charge — a month's usage is billed on the next month's charge, once
                the month is finished and the total cannot move.

                Only messages that actually went are counted. Queued, failed and suppressed ones
                are not charged for.
                """,
        };

        yield return new HelpArticle
        {
            Slug = "staff-and-access",
            Category = "Admin",
            Title = "Staff, roles and access",
            Summary = "Adding people, and what each role can reach.",
            Keywords = "users staff roles permissions access licence clinician seat",
            DisplayOrder = 150,
            Body =
                """
                Admin → Users adds staff. Email and mobile must each be unique within the
                practice — a number that reaches two people is a code going to whichever of them
                a query found first. The same clinician working at two practices is fine; the
                rule is per clinic.

                Roles control what somebody can reach. Adding a colleague does not let somebody
                grant themselves ownership of the practice: that is checked separately, against
                the stored record rather than what is being saved.

                Clinicians are billable on the plan; assistants and reception are free and
                unlimited. That is not generosity — charging per front-desk login is what makes a
                practice share one reception account, and a shared account makes the audit log
                useless.
                """,
        };

        yield return new HelpArticle
        {
            Slug = "the-plan",
            Category = "Admin",
            Title = "Your plan and what it costs",
            Summary = "How the price is worked out, and what is and is not charged.",
            Keywords = "plan price subscription billing cost upgrade seats sites",
            DisplayOrder = 160,
            Body =
                """
                Two plans. Solo is one clinician at one location. Practice is a team, at one
                location or several.

                The price is a base, then sites beyond what the base covers, then clinician seats
                beyond the allowance — three numbers, in that order. Counted from active staff in
                a clinical role.

                The line between the plans is arithmetic rather than a block: Solo's extra
                clinician is deliberately dearer, so Practice is level at the third clinician and
                plainly cheaper at the fourth. Nothing stops you adding a clinician on Solo.

                Charges are raised from these prices and nothing collects money. There is no
                invoice, no payment gateway and no renewal date, so every Paid or Failed was
                recorded by a person after money did or did not arrive somewhere else.
                """,
        };

        yield return new HelpArticle
        {
            Slug = "audit-and-privacy",
            Category = "Admin",
            Title = "The audit log",
            Summary = "What is recorded, and what it is for.",
            Keywords = "audit log privacy who accessed deleted history trail",
            DisplayOrder = 170,
            Body =
                """
                Admin → Audit log records who did what: opening a patient record, signing a
                note, issuing an invoice, releasing a sterilisation load, sending a message,
                changing a setting. It is searchable and paged.

                Views are recorded as well as changes. "Who looked at this record" is the
                question a privacy complaint actually asks, and a log of changes alone cannot
                answer it.

                Deletions throughout the app are soft: the row stays, marked deleted, and the
                audit entry names who removed it and what it was. Something a patient was given
                cannot be made to have never existed.
                """,
        };
    }
}
