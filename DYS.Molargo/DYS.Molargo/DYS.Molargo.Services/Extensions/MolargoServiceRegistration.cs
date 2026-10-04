using DYS.Molargo.Services.Settings;
using DYS.Molargo.Domain.Data;
using DYS.Molargo.Services.Features.Admin;
using DYS.Molargo.Services.Features.Auth;
using DYS.Molargo.Services.Features.Billing;
using DYS.Molargo.Services.Features.Charting;
using DYS.Molargo.Services.Features.Comms;
using DYS.Molargo.Services.Features.Diary;
using DYS.Molargo.Services.Features.FrontDesk;
using DYS.Molargo.Services.Features.Inventory;
using DYS.Molargo.Services.Features.Patient;
using DYS.Molargo.Services.Features.Platform;
using DYS.Molargo.Services.Features.Prescribing;
using DYS.Molargo.Services.Features.Reports;
using DYS.Molargo.Services.Features.Treatment;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DYS.Molargo.Services.Extensions;

/// <summary>
/// The rules half of the composition root.
/// </summary>
/// <remarks>
/// Called by the API directly, and by the heads through <c>AddMolargoCore</c>. It registers
/// nothing that renders: see this project's csproj for why that is enforced by what the
/// project cannot reference rather than by anybody remembering to.
/// </remarks>
public static class MolargoServiceRegistration
{
    /// <summary>
    /// Everything the feature services need, and nothing a screen needs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called by the heads through <see cref="AddMolargoCore"/>, and directly by the API,
    /// which runs these same service classes against PostgreSQL so a device in API mode
    /// gets the rules it would have applied locally rather than a second implementation of
    /// them. Two implementations of "may this invoice be voided" is one of them being
    /// wrong, and no way to tell which.
    /// </para>
    /// <para>
    /// The split is enforced by what is absent: no navigator, no view models, nothing from
    /// <c>Microsoft.AspNetCore.Components</c>. A feature service that needed any of those
    /// would stop compiling on the server, which is the point of registering them apart.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMolargoDomainServices(this IServiceCollection services)
    {
        // Applied here rather than lazily on first map, so a bad rule fails at startup
        // instead of halfway through saving a patient.
        MappingConfiguration.Apply();

        services.AddSingleton<IClock, SystemClock>();

        // Stateless and deliberately expensive per call — a singleton so the iteration
        // count is configured in one place rather than per resolution.
        services.AddSingleton<IPasswordHasher, PasswordHasher>();

        // The two reads the session needs before it can say where somebody is working.
        // Registered here rather than with the store, although it is the store it reads:
        // every host that has a session also has repositories, and keeping it beside the
        // session is what makes the pair readable.
        services.AddScoped<ISessionDirectory, SessionDirectory>();

        // Who is signed in. The same class on both sides: on a head it is filled by
        // AuthService once a password has been checked, and on the API it is filled per
        // request from the token's claims. It reads locations and the staff record through
        // the repository and holds nothing else, so neither filling is a special case.
        services.AddScoped<ISessionService, SessionService>();

        // Scoped beside the session it reads the caller from. Not a singleton: it resolves
        // the acting person per circuit, and a singleton would have answered for whoever
        // signed in first on the whole server.
        services.AddScoped<IPracticeGuard, PracticeGuard>();

        // Scoped for the same reason as the guard: it stamps the acting person, and a
        // singleton would have logged every clinic in the process as whoever signed in
        // first.
        services.AddScoped<IAuditLog, AuditLog>();

        // The first thing here that reaches the network, and no longer a singleton. The
        // sending is still stateless — the account comes in per call — but every send is
        // audited now, and the log stamps the acting person. A singleton holding a scoped
        // writer is a captive dependency: it would have filed every clinic's mail under
        // whoever signed in first, which is the one field an audit trail cannot be wrong
        // about.
        services.AddScoped<IEmailSender, SmtpEmailSender>();

        // Reads the vendor's SMS gateway for whichever clinic is signed in. Scoped, like
        // everything that follows the tenant.
        services.AddScoped<ISmsGatewayResolver, SmsGatewayResolver>();

        // The other thing here that reaches the network. Scoped because it resolves the
        // gateway for the signed-in clinic, and a singleton would have sent every
        // practice's texts through whichever country was resolved first.
        services.AddScoped<ISmsSender, SmsSender>();

        // Where the vendor takes subscription money, per country. Registered as a list:
        // the resolver is handed every provider this build knows and picks the one the
        // country's row names, so a second provider is one more line here and nothing
        // else. Singleton because a provider holds no state and no tenant — which country
        // applies is an argument, not something it was constructed with.
        services.AddSingleton<IPaymentProvider, PayMongoPaymentProvider>();

        // Scoped, unlike the providers: it opens a context to read the gateway row.
        services.AddScoped<IPaymentGatewayResolver, PaymentGatewayResolver>();

        // The vendor's screen for the rows above.
        services.AddScoped<Features.Platform.IPaymentGatewayService,
            Features.Platform.PaymentGatewayService>();

        // Keeps a patient's recall in step with the visits they have. Scoped like the
        // services on either side of it, and shared by the front desk and the diary so the
        // rule cannot drift between "visit finished" and "next one booked".
        services.AddScoped<Features.Diary.IRecallScheduler,
            Features.Diary.RecallScheduler>();

        // Sends the reminders a practice's cadence has fallen due. Scoped like the notifier
        // it sends through — it follows the signed-in clinic's settings.
        services.AddScoped<Features.Comms.IReminderRunner,
            Features.Comms.ReminderRunner>();

        // Tells a patient about a booking. Separate from the appointment service so a
        // mail server being down cannot fail the save that already wrote the slot.
        services.AddScoped<Features.Comms.IAppointmentNotifier,
            Features.Comms.AppointmentNotifier>();

        // Unauthenticated by design — it is what somebody who cannot sign in uses.
        services.AddScoped<Features.Auth.IPasswordResetService,
            Features.Auth.PasswordResetService>();

        // The device default: nothing to persist, because a BlazorWebView has no reload.
        // TryAdd, so the web head's cookie-backed implementation — registered before this
        // runs — is left in place.
        services.TryAddScoped<ISessionHandoff, InMemorySessionHandoff>();

        services.AddMolargoFeatureServices();

        return services;
    }

    /// <summary>
    /// Feature services — the layer that gives the generic repository domain vocabulary.
    /// Scoped rather than singleton so a feature service may later hold per-user state
    /// without that becoming a cross-tab bug on the web head.
    /// </summary>
    private static IServiceCollection AddMolargoFeatureServices(this IServiceCollection services)
    {
        services.AddScoped<IPatientService, PatientService>();
        services.AddScoped<IFrontDeskService, FrontDeskService>();
        services.AddScoped<IChartingService, ChartingService>();
        services.AddScoped<IPerioService, PerioService>();
        services.AddScoped<IPrescribingService, PrescribingService>();
        services.AddScoped<IReferralService, ReferralService>();
        services.AddScoped<IBillingService, BillingService>();

        services.AddScoped<IPayrollService, PayrollService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<IMedicalHistoryCatalogue, MedicalHistoryCatalogue>();
        services.AddScoped<ITreatmentPlanService, TreatmentPlanService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<ICommsService, CommsService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<INotificationSettingsService, NotificationSettingsService>();
        services.AddScoped<IPrinterSettingsService, PrinterSettingsService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IRegistrationService, RegistrationService>();
        services.AddScoped<IPlatformService, PlatformService>();
        services.AddScoped<IPlatformUserService, PlatformUserService>();
        services.AddScoped<IPlanService, PlanService>();

        // The vendor's SMS providers, one per country. Scoped beside the plan service
        // for the same reason: it reads the acting person from the session.
        services.AddScoped<Features.Platform.ISmsGatewayService,
            Features.Platform.SmsGatewayService>();

        // The platform's own sending account. Scoped like the rest of the vendor's
        // services, and the one thing that can email somebody before their clinic exists.
        services.AddScoped<Features.Platform.IPlatformMailService,
            Features.Platform.PlatformMailService>();
        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
        services.AddScoped<IDiaryService, DiaryService>();
        services.AddScoped<IAppointmentService, AppointmentService>();

        // The knowledge base, read by every practice and written by the vendor. Scoped like
        // the other services that cross the tenant boundary deliberately.
        services.AddScoped<Features.Help.IHelpService,
            Features.Help.HelpService>();

        // The setup checklist. Scoped rather than transient because it caches the
        // outstanding count for the app bar, which renders on every screen — a transient
        // would hand each render a fresh instance with an empty cache and put seven counts
        // through the database per navigation.
        services.AddScoped<Features.Setup.ISetupService,
            Features.Setup.SetupService>();

        return services;
    }
}
