using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Features.Admin.Services;
using DYS.Molargo.Shared.Services;

// "Patient" alone binds to the Features.Patient *namespace* from anywhere under Features —
// name resolution walks up and finds it there — so the entity has to be named through an
// alias.
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Features.Setup.Services;

/// <summary>Which band of the checklist a step belongs to.</summary>
public enum SetupGroup
{
    /// <summary>Facts about the practice itself — address, hours, chairs.</summary>
    Practice = 0,

    /// <summary>The people, and the ways the practice reaches them.</summary>
    People = 1,

    /// <summary>The first real work: a patient, and a booking.</summary>
    Live = 2,
}

/// <summary>
/// One line of the setup checklist.
/// </summary>
/// <param name="Why">
/// What goes wrong while it is undone. The whole reason a checklist beats a settings
/// menu: a list of tasks with no consequences attached gets skipped in the order it is
/// written, rather than in the order that matters.
/// </param>
/// <param name="State">What is true right now — the address on file, the hours in force.</param>
/// <param name="IsRequired">
/// False for a step a practice can legitimately never do. A solo dentist has no team to
/// add, and a checklist that nags them forever about it teaches them to ignore it.
/// </param>
public sealed record SetupStep(
    string Key,
    SetupGroup Group,
    string Title,
    string Why,
    string State,
    bool IsDone,
    bool IsRequired,
    string Href,
    string Action);

/// <summary>Where a practice has got to.</summary>
public sealed record SetupProgress(
    IReadOnlyList<SetupStep> Steps,
    string PracticeName,
    string? SiteName,
    int? TrialDaysLeft)
{
    public static readonly SetupProgress Empty = new([], string.Empty, null, null);

    public int Done => Steps.Count(step => step.IsDone);

    public int Total => Steps.Count;

    /// <summary>
    /// Required steps still outstanding — the number the nav badge carries.
    /// </summary>
    /// <remarks>
    /// Required only. Counting the optional ones would leave a practice that will never
    /// send a text staring at a permanent "2", and a badge that never reaches zero is a
    /// badge people stop reading.
    /// </remarks>
    public int Outstanding => Steps.Count(step => step.IsRequired && !step.IsDone);

    public bool IsComplete => Outstanding == 0;

    public int Percent => Total == 0 ? 100 : (int)Math.Round(Done * 100.0 / Total);
}

/// <summary>
/// The initial-setup checklist for a practice that has just signed up.
/// </summary>
/// <remarks>
/// <para>
/// Every step's state is derived from the data, and nothing is stored. A "completed"
/// column would start lying the first time somebody switched a thing back off: the box
/// would stay ticked while the mail account it referred to had gone, and the one screen
/// meant to tell a practice what is missing would be the screen most confidently wrong.
/// </para>
/// <para>
/// The steps link out rather than editing anything here. Each one already has a screen
/// that owns it — with its own validation, its own audit entry and its own refusals — and
/// a second form over the same fields is a second set of rules to keep in step. This
/// screen's job is to say what is missing and why it matters, not to be a shorter Admin.
/// </para>
/// </remarks>
public interface ISetupService
{
    /// <summary>Recomputes the checklist from the database.</summary>
    Task<SetupProgress> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Required steps outstanding, for the nav badge.
    /// </summary>
    /// <remarks>
    /// Served from the cached snapshot where there is one, so the app bar — which renders
    /// on every screen — does not put seven counts through the database each time somebody
    /// navigates.
    /// </remarks>
    Task<int> OutstandingAsync(CancellationToken ct = default);

    /// <summary>Raised when the outstanding count changes, so the nav can catch up.</summary>
    event EventHandler? Changed;
}

/// <inheritdoc cref="ISetupService"/>
public sealed class SetupService : ISetupService
{
    private readonly ISessionService _session;
    private readonly ITenantContext _tenant;
    private readonly INotificationSettingsService _notifications;
    private readonly IRepository<Tenant> _tenants;
    private readonly IRepository<PracticeLocation> _locations;
    private readonly IRepository<Operatory> _chairs;
    private readonly IRepository<Provider> _providers;
    private readonly IRepository<PatientEntity> _patients;
    private readonly IRepository<Appointment> _appointments;
    private readonly IClock _clock;

    private int? _outstanding;

    public SetupService(
        ISessionService session,
        ITenantContext tenant,
        INotificationSettingsService notifications,
        IRepository<Tenant> tenants,
        IRepository<PracticeLocation> locations,
        IRepository<Operatory> chairs,
        IRepository<Provider> providers,
        IRepository<PatientEntity> patients,
        IRepository<Appointment> appointments,
        IClock clock)
    {
        _session = session;
        _tenant = tenant;
        _notifications = notifications;
        _tenants = tenants;
        _locations = locations;
        _chairs = chairs;
        _providers = providers;
        _patients = patients;
        _appointments = appointments;
        _clock = clock;
    }

    public event EventHandler? Changed;

    public async Task<SetupProgress> GetAsync(CancellationToken ct = default)
    {
        var practice = await _tenants.GetByIdAsync(_tenant.TenantId, ct).ConfigureAwait(false);

        if (practice is null) return SetupProgress.Empty;

        await _session.EnsureLoadedAsync(ct).ConfigureAwait(false);

        // The site being worked in, not "the practice's address". They are the same thing
        // for the single-site clinic this screen is written for, and they stop being the
        // same the moment a second site opens — at which point the checklist has to be
        // about somewhere in particular or it is about nowhere.
        var sites = await _locations.ListAsync(null, ct).ConfigureAwait(false);

        var site = sites.FirstOrDefault(row => row.Id == _session.LocationId)
            ?? sites.OrderBy(row => row.DisplayOrder).FirstOrDefault();

        // Typed rather than var: a conditional whose arms are an empty collection
        // expression and a Task result has no natural type to infer from.
        IReadOnlyList<Operatory> chairs = site is null
            ? []
            : await _chairs
                .ListAsync(chair => chair.PracticeLocationId == site.Id, ct)
                .ConfigureAwait(false);

        var staff = await _providers
            .CountAsync(provider => provider.IsActive, ct)
            .ConfigureAwait(false);

        var mail = await _notifications.GetAsync(ct).ConfigureAwait(false);
        var patients = await _patients.CountAsync(null, ct).ConfigureAwait(false);
        var appointments = await _appointments.CountAsync(null, ct).ConfigureAwait(false);

        var steps = new List<SetupStep>
        {
            AddressStep(site),
            HoursStep(site),
            ChairsStep(chairs),
            TeamStep(staff),
            MailStep(mail),
            RemindersStep(mail, practice),
            SmsStep(practice),
            FirstPatientStep(patients),
            FirstAppointmentStep(appointments, patients),
        };

        var progress = new SetupProgress(
            steps,
            practice.Name,
            site?.Name,
            practice.TrialDaysLeft(_clock.Today));

        Remember(progress.Outstanding);

        return progress;
    }

    public async Task<int> OutstandingAsync(CancellationToken ct = default)
    {
        if (_outstanding is { } cached) return cached;

        var progress = await GetAsync(ct).ConfigureAwait(false);

        return progress.Outstanding;
    }

    // ---- the steps -------------------------------------------------------

    private static SetupStep AddressStep(PracticeLocation? site)
    {
        var hasAddress = !string.IsNullOrWhiteSpace(site?.AddressLine);
        var hasPhone = !string.IsNullOrWhiteSpace(site?.Phone);

        return new SetupStep(
            "address",
            SetupGroup.Practice,
            "Address and phone number",
            "They head every receipt, referral letter and prescription this practice "
            + "prints. Without them a patient holding a receipt has no way to ring you "
            + "about it, and a specialist holding a referral has nowhere to send the "
            + "reply.",
            (hasAddress, hasPhone) switch
            {
                (true, true) => $"{site!.AddressLine} · {site.Phone}",
                (true, false) => $"{site!.AddressLine} — no phone number yet",
                (false, true) => $"{site!.Phone} — no address yet",
                _ => "Nothing on file yet.",
            },
            hasAddress && hasPhone,
            IsRequired: true,
            "/admin?tab=sites",
            "Open Admin → Sites");
    }

    private static SetupStep HoursStep(PracticeLocation? site)
    {
        var hours = site?.Hours ?? PracticeHours.Default;

        // Deliberately not Effective: the point of the step is to tell the difference
        // between hours somebody set and the fallback standing in for them, and Effective
        // exists precisely to hide that difference from every other screen.
        var isSet = hours.Days != WorkingDays.None
            && hours.CloseMinutes > hours.OpenMinutes;

        return new SetupStep(
            "hours",
            SetupGroup.Practice,
            "Opening hours",
            "The diary is drawn against them, and a booking outside them is refused. "
            + "Until they are set the app assumes Mon–Sat, 08:00–18:00 — which is a guess, "
            + "and the wrong guess quietly blocks the slots you actually trade.",
            isSet
                ? hours.Summary
                : $"Assuming {PracticeHours.Default.Summary} until you say otherwise.",
            isSet,
            IsRequired: true,
            "/admin?tab=sites",
            "Open Admin → Sites");
    }

    private static SetupStep ChairsStep(IReadOnlyList<Operatory> chairs)
    {
        var active = chairs.Where(chair => chair.IsActive).ToList();

        // Signing up creates one chair called "Chair 1", so a bare count would read as
        // done on day one and the step would be furniture. Untouched means the default is
        // still sitting there under its given name.
        var isTouched = active.Count > 1
            || active.Any(chair => !string.Equals(chair.Name, "Chair 1", StringComparison.Ordinal));

        return new SetupStep(
            "chairs",
            SetupGroup.Practice,
            "Chairs and rooms",
            "One column of the day view per chair. Names the staff use — \"Surgery 1\", "
            + "\"Hygiene\" — are what makes the diary readable at a glance.",
            active.Count switch
            {
                0 => "No chairs — nothing can be booked.",
                1 when !isTouched => "One chair, still called \"Chair 1\".",
                1 => $"One chair: {active[0].Name}.",
                _ => $"{active.Count} chairs: {string.Join(", ", active.Select(chair => chair.Name))}.",
            },

            // No chairs at all is a genuine failure rather than an untouched default, so
            // it can never read as done however the names look.
            active.Count > 0 && isTouched,
            IsRequired: active.Count == 0,
            "/admin?tab=sites",
            "Open Admin → Sites");
    }

    private static SetupStep TeamStep(int staff) =>
        new(
            "team",
            SetupGroup.People,
            "The rest of the team",
            "Every clinical note, script and invoice is signed by whoever is signed in. "
            + "A shared login makes the audit trail worthless exactly when it is needed, "
            + "which is when somebody asks who did what.",
            staff <= 1
                ? "Just you so far."
                : $"{staff} people can sign in.",
            staff > 1,

            // A solo practice is a real practice, not an unfinished one.
            IsRequired: false,
            "/admin?tab=users",
            "Open Admin → Users");

    private static SetupStep MailStep(NotificationSettings mail)
    {
        var isDone = mail.IsConfigured && mail.EmailEnabled;

        return new SetupStep(
            "email",
            SetupGroup.People,
            "The practice's email account",
            "Password resets and two-step sign-in codes go through it, as do receipts "
            + "and reminders. Until it is set up nobody can reset their own password — "
            + "somebody else with staff access has to do it for them.",
            (mail.IsConfigured, mail.EmailEnabled) switch
            {
                (true, true) => $"Sending as {mail.SenderAddress}.",
                (true, false) => $"{mail.SenderAddress} is set up but sending is switched off.",
                _ => "No account yet.",
            },
            isDone,
            IsRequired: true,
            "/admin?tab=settings",
            "Open Admin → Settings");
    }

    private static SetupStep RemindersStep(NotificationSettings mail, Tenant practice)
    {
        var canSend = (mail.IsConfigured && mail.EmailEnabled) || practice.SmsEnabled;

        return new SetupStep(
            "reminders",
            SetupGroup.People,
            "Appointment reminders",
            "The cheapest thing in the app: a reminder the day before turns a large share "
            + "of no-shows into either an arrival or a cancellation you can refill.",
            (canSend, mail.RemindersEnabled) switch
            {
                (_, true) => "On.",
                (false, false) => "Off — and nothing could send yet. Set up email or texts first.",
                _ => "Off.",
            },
            mail.RemindersEnabled,
            IsRequired: false,
            "/diary?tab=reminders",
            "Open Diary → Reminders");
    }

    private static SetupStep SmsStep(Tenant practice) =>
        new(
            "sms",
            SetupGroup.People,
            "Text messaging",
            "Texts get read; email to a patient often does not. They are charged per "
            + "message on top of the plan, so this is a cost decision as much as a "
            + "setting — which is why it sits with the plan.",
            practice.SmsEnabled
                ? "On. Messages are added to the monthly charge."
                : "Off. Reminders and recalls fall back to email.",
            practice.SmsEnabled,
            IsRequired: false,
            "/admin?tab=licensing",
            "Open Admin → Plan");

    private static SetupStep FirstPatientStep(int patients) =>
        new(
            "patient",
            SetupGroup.Live,
            "Your first patient",
            "Everything else in the app hangs off a patient record — the chart, the "
            + "treatment plan, the invoice, the reminder. A practice starts with none of "
            + "them, because fictional people in a real list can only be told apart from "
            + "real ones by opening each one.",
            patients switch
            {
                0 => "None yet.",
                1 => "One patient on file.",
                _ => $"{patients} patients on file.",
            },
            patients > 0,
            IsRequired: true,
            "/patients/new",
            "Add a patient");

    private static SetupStep FirstAppointmentStep(int appointments, int patients) =>
        new(
            "appointment",
            SetupGroup.Live,
            "Your first appointment",
            "Booking one proves the chain end to end: the chair, the opening hours, the "
            + "provider and — if reminders are on — the message that goes out the day "
            + "before.",
            (appointments, patients) switch
            {
                (0, 0) => "None yet. Add a patient first.",
                (0, _) => "None yet.",
                (1, _) => "One appointment booked.",
                _ => $"{appointments} appointments booked.",
            },
            appointments > 0,
            IsRequired: true,

            // Straight into the booking form when there is somebody to book; to the list
            // otherwise, because the form's first field is a patient who does not exist.
            patients > 0 ? "/diary/appointments/new" : "/patients/new",
            patients > 0 ? "Book an appointment" : "Add a patient first");

    /// <summary>
    /// Caches the count, and tells the nav when it moves.
    /// </summary>
    /// <remarks>
    /// The event is what keeps the badge honest without the app bar querying on every
    /// navigation. It only fires when this screen recomputes, so a step finished elsewhere
    /// shows up on the next visit here rather than instantly — a badge one visit stale is
    /// a fair price for not running the checklist on every page load.
    /// </remarks>
    private void Remember(int outstanding)
    {
        if (_outstanding == outstanding) return;

        _outstanding = outstanding;

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
