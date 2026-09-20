using DYS.Molargo.Shared.Data;
using DYS.Molargo.Shared.Documents;
using DYS.Molargo.Shared.Features.Billing.Services;
using DYS.Molargo.Shared.Features.Billing.ViewModels;
using DYS.Molargo.Shared.Features.Charting.Services;
using DYS.Molargo.Shared.Features.Charting.ViewModels;
using DYS.Molargo.Shared.Features.Diary.Services;
using DYS.Molargo.Shared.Features.Diary.ViewModels;
using DYS.Molargo.Shared.Features.FrontDesk.Services;
using DYS.Molargo.Shared.Features.FrontDesk.ViewModels;
using DYS.Molargo.Shared.Features.Patient.Services;
using DYS.Molargo.Shared.Features.Admin.Services;
using DYS.Molargo.Shared.Features.Auth.Services;
using DYS.Molargo.Shared.Features.Auth.ViewModels;
using DYS.Molargo.Shared.Features.Admin.ViewModels;
using DYS.Molargo.Shared.Features.Comms.Services;
using DYS.Molargo.Shared.Features.Comms.ViewModels;
using DYS.Molargo.Shared.Features.Inventory.Services;
using DYS.Molargo.Shared.Features.Inventory.ViewModels;
using DYS.Molargo.Shared.Features.Reports.Services;
using DYS.Molargo.Shared.Features.Reports.ViewModels;
using DYS.Molargo.Shared.Features.Prescribing.Services;
using DYS.Molargo.Shared.Features.Prescribing.ViewModels;
using DYS.Molargo.Shared.Features.Patient.ViewModels;
using DYS.Molargo.Shared.Features.Platform.Services;
using DYS.Molargo.Shared.Features.Platform.ViewModels;
using DYS.Molargo.Shared.Features.Treatment.Services;
using DYS.Molargo.Shared.Features.Treatment.ViewModels;
using DYS.Molargo.Shared.Repositories;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DYS.Molargo.Shared.Extensions;

/// <summary>
/// The composition root, one method per concern, so a head calls what it needs and adds
/// only what it alone can supply.
/// </summary>
/// <remarks>
/// Lifetimes, and why:
/// <list type="bullet">
/// <item>View models are <b>transient</b> — one per component instance, so two tabs on the
/// same screen do not share a search box.</item>
/// <item>Navigation and session are <b>scoped</b>, because they depend on
/// <c>NavigationManager</c>, which is scoped in both hosts. A singleton depending on a
/// scoped service fails to resolve at all on the web head.</item>
/// <item>The clock, the database owner and the repositories are <b>singleton</b>. None
/// holds per-request state: the repositories create a <c>DbContext</c> per call rather
/// than keeping one.</item>
/// </list>
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Head-agnostic registrations: view models, feature services, navigation, session,
    /// the clock, and the entity/DTO mapping rules. Every head calls this once.
    /// </summary>
    public static IServiceCollection AddMolargoCore(this IServiceCollection services)
    {
        // Applied here rather than lazily on first map, so a bad rule fails at startup
        // instead of halfway through saving a patient.
        MappingConfiguration.Apply();

        services.AddSingleton<IClock, SystemClock>();

        // Stateless and deliberately expensive per call — a singleton so the iteration
        // count is configured in one place rather than per resolution.
        services.AddSingleton<IPasswordHasher, PasswordHasher>();

        services.AddScoped<IAppNavigator, AppNavigator>();
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

        // Tells a patient about a booking. Separate from the appointment service so a
        // mail server being down cannot fail the save that already wrote the slot.
        services.AddScoped<Features.Comms.Services.IAppointmentNotifier,
            Features.Comms.Services.AppointmentNotifier>();

        // Unauthenticated by design — it is what somebody who cannot sign in uses.
        services.AddScoped<Features.Auth.Services.IPasswordResetService,
            Features.Auth.Services.PasswordResetService>();

        // The device default: nothing to persist, because a BlazorWebView has no reload.
        // TryAdd, so the web head's cookie-backed implementation — registered before this
        // runs — is left in place.
        services.TryAddScoped<ISessionHandoff, InMemorySessionHandoff>();

        services.AddMolargoFeatureServices();
        services.AddMolargoViewModels();

        return services;
    }

    /// <summary>
    /// The local SQLite store, and the generic repository over it.
    /// </summary>
    /// <remarks>
    /// The head must already have registered <see cref="IDatabasePathProvider"/> — only it
    /// knows where a writable file lives on its platform.
    /// </remarks>
    /// <param name="seedSampleData">
    /// Seeds the practice, providers and the prototype's patient list on first run. Safe
    /// while the app is offline-only, and the flag exists so it can be turned off in one
    /// place once a server is the source of truth — seeding a synced device would leave
    /// demo rows under local ids alongside the server's copies of the same records.
    /// </param>
    public static IServiceCollection AddMolargoLocalStore(
        this IServiceCollection services, bool seedSampleData = true)
    {
        // A factory rather than AddDbContext: a DbContext is not thread-safe, and
        // MolargoDatabase hands out one per unit of work.
        services.AddDbContextFactory<MolargoDbContext>((provider, options) =>
        {
            var path = provider.GetRequiredService<IDatabasePathProvider>().GetDatabasePath();
            options.UseSqlite($"Data Source={path}");
        });

        // The clinic every row belongs to.
        //
        // A singleton because one installation serves one clinic while the app is
        // offline-only. When a server exists this becomes scoped — resolved per request or
        // per circuit from the signed-in user — and nothing above the interface changes.
        services.AddSingleton<ITenantContext, TenantContext>();

        services.AddSingleton(provider => new MolargoDatabase(
            provider.GetRequiredService<IDbContextFactory<MolargoDbContext>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<IPasswordHasher>(),
            seedSampleData));

        // One open generic registration covers all 34 entities. Requesting
        // IRepository<Appointment> resolves EfRepository<Appointment> with no per-entity
        // line here — which is the main practical reason the repository is generic.
        services.AddSingleton(typeof(IRepository<>), typeof(EfRepository<>));

        // The seam the Supabase bucket replaces. Registered here rather than per head,
        // because "files on the local disk" is a decision about the store, not about the
        // platform — each head only supplies the root path.
        services.AddSingleton<IDocumentStore, LocalDocumentStore>();

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
        services.AddScoped<Features.Platform.Services.ISmsGatewayService,
            Features.Platform.Services.SmsGatewayService>();
        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
        services.AddScoped<IDiaryService, DiaryService>();
        services.AddScoped<IAppointmentService, AppointmentService>();

        return services;
    }

    /// <summary>
    /// Every view model, transient. Its own method so adding a feature is one line here
    /// rather than an edit to the core registration.
    /// </summary>
    private static IServiceCollection AddMolargoViewModels(this IServiceCollection services)
    {
        services.AddTransient<FrontDeskViewModel>();
        services.AddTransient<PatientsViewModel>();
        services.AddTransient<PatientViewModel>();
        services.AddTransient<PatientEditViewModel>();
        services.AddTransient<InvoiceDraftViewModel>();
        services.AddTransient<MedicalHistoryViewModel>();
        services.AddTransient<ConsentViewModel>();
        services.AddTransient<ChartViewModel>();

        services.AddTransient<PlanBuilderViewModel>();

        services.AddTransient<PlanPresentationViewModel>();
        services.AddTransient<ChartingWorklistViewModel>();
        services.AddTransient<PrescribingViewModel>();
        services.AddTransient<BillingViewModel>();
        services.AddTransient<InventoryViewModel>();
        services.AddTransient<CommsViewModel>();
        services.AddTransient<ReportsViewModel>();
        services.AddTransient<AdminViewModel>();
        services.AddTransient<PlatformViewModel>();
        services.AddTransient<PlatformUsersViewModel>();
        services.AddTransient<PlansViewModel>();
        services.AddTransient<SmsGatewaysViewModel>();
        services.AddTransient<SubscriptionBillingViewModel>();
        services.AddTransient<SignInViewModel>();
        services.AddTransient<CreatePracticeViewModel>();
        services.AddTransient<ForgotPasswordViewModel>();
        services.AddTransient<StockItemEditViewModel>();
        services.AddTransient<DiaryViewModel>();
        services.AddTransient<AppointmentEditViewModel>();

        return services;
    }
}
