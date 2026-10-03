using DYS.Molargo.Services.Extensions;
using DYS.Molargo.Shared.Features.Auth.Services;
using DYS.Molargo.Shared.Api;
using DYS.Molargo.Shared.Documents;
using DYS.Molargo.Services.Features.Billing;
using DYS.Molargo.Shared.Features.Billing.ViewModels;
using DYS.Molargo.Services.Features.Charting;
using DYS.Molargo.Shared.Features.Charting.ViewModels;
using DYS.Molargo.Services.Features.Diary;
using DYS.Molargo.Shared.Features.Diary.ViewModels;
using DYS.Molargo.Services.Features.FrontDesk;
using DYS.Molargo.Shared.Features.FrontDesk.ViewModels;
using DYS.Molargo.Services.Features.Patient;
using DYS.Molargo.Services.Features.Admin;
using DYS.Molargo.Services.Features.Auth;
using DYS.Molargo.Shared.Features.Auth.ViewModels;
using DYS.Molargo.Shared.Features.Admin.ViewModels;
using DYS.Molargo.Services.Features.Comms;
using DYS.Molargo.Shared.Features.Comms.ViewModels;
using DYS.Molargo.Services.Features.Inventory;
using DYS.Molargo.Shared.Features.Inventory.ViewModels;
using DYS.Molargo.Services.Features.Reports;
using DYS.Molargo.Shared.Features.Reports.ViewModels;
using DYS.Molargo.Services.Features.Prescribing;
using DYS.Molargo.Shared.Features.Prescribing.ViewModels;
using DYS.Molargo.Shared.Features.Patient.ViewModels;
using DYS.Molargo.Services.Features.Platform;
using DYS.Molargo.Shared.Features.Platform.ViewModels;
using DYS.Molargo.Services.Features.Treatment;
using DYS.Molargo.Shared.Features.Treatment.ViewModels;
using DYS.Molargo.Shared.Services;
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
        services.AddMolargoDomainServices();
        services.AddMolargoShell();

        return services;
    }

    /// <summary>
    /// What a screen needs, wherever the data comes from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The navigator, the session and the view models — nothing here touches a database or
    /// an interface that might be a proxy, which is what lets one head run against its own
    /// SQLite and another against the API with this half unchanged.
    /// </para>
    /// <para>
    /// Registered with <c>TryAdd</c> where the store may have its own answer. A head calls
    /// this and then exactly one of <see cref="AddMolargoLocalStore"/> or
    /// <see cref="AddMolargoApiStore"/>, and the store wins where they overlap.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMolargoShell(this IServiceCollection services)
    {
        MappingConfiguration.Apply();

        services.TryAddSingleton<IClock, SystemClock>();
        services.AddScoped<IAppNavigator, AppNavigator>();

        // The session stays local in both modes. It is read synchronously as properties and
        // raises an event when it changes, and neither survives a round trip — proxying it
        // would move the answer to "who is signed in" onto another machine.
        services.TryAddScoped<ISessionService, SessionService>();

        // The device default: nothing to persist, because a BlazorWebView has no reload.
        services.TryAddScoped<ISessionHandoff, InMemorySessionHandoff>();

        services.AddMolargoViewModels();

        return services;
    }

    /// <summary>
    /// The server as the store: every feature service is a call to the API.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The alternative to <see cref="AddMolargoLocalStore"/>, not an addition to it. A head
    /// calls one or the other — registering both would leave whichever ran last deciding
    /// where a practice's records live, which is not a thing to settle by ordering.
    /// </para>
    /// <para>
    /// No database, no repositories and no feature service classes here: in this mode they
    /// all run on the server, against PostgreSQL, and the device holds proxies to them. One
    /// implementation of every rule, which is the point — see <see cref="MolargoRpc"/>.
    /// </para>
    /// <para>
    /// The practical consequence, stated plainly because it is a change in what the app is:
    /// nothing works without a connection. There is no local copy to fall back to.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMolargoApiStore(
        this IServiceCollection services, MolargoApiOptions options)
    {
        services.AddSingleton(options);

        // Scoped, emphatically not singleton. On a device the two are the same thing — one
        // person, one process — and on the web head they are not: a singleton token store
        // is one bearer token shared by every user on the server, so whoever signed in last
        // would be who everybody else is acting as. The device pays nothing for the stricter
        // lifetime; the web head would pay everything for the looser one.
        services.AddScoped<IApiTokenStore, ApiTokenStore>();

        // One client for the process, which is what HttpClient is designed for — a new one
        // per call exhausts sockets under any real use. Its headers are never mutated: the
        // bearer token goes on each request, because a default header set once would still
        // be the first person's after somebody else signed in.
        services.AddSingleton(_ => new HttpClient
        {
            BaseAddress = options.BaseAddress,
            Timeout = options.Timeout,
        });

        // Scoped because it holds the token store. A singleton taking a scoped service is
        // a captive dependency: it would capture the first circuit's token and keep using
        // it for every later one, which is the same leak by another route.
        services.AddScoped<RpcChannel>();

        // The clinic, and with it the currency every figure is rendered in. Still local:
        // ApiAuthService fills it from what the sign-in returned.
        //
        // Scoped for the same reason as the token store — it says which clinic this caller
        // belongs to, and on the web head that differs per circuit.
        //
        // Known limit, and it is not fixed by the lifetime: FormattingTenantContext pushes
        // the currency into MolargoFormat, which is static. One process serving two clinics
        // on different currencies renders both in whichever signed in most recently. That
        // was safe while a web server meant one clinic; it stops being safe now that any
        // clinic can sign in to it. See the note in FormattingTenantContext.
        services.AddScoped<ITenantContext, FormattingTenantContext>();

        // Every catalogued interface, implemented by a generated proxy. One loop rather
        // than thirty-one registrations, for the same reason the proxy is one class: the
        // list is the catalogue, and a service added there should not also need a line here
        // that somebody can forget.
        foreach (var contract in MolargoRpc.Services)
        {
            services.AddScoped(contract, provider =>
                RpcServiceProxy.Create(contract, provider.GetRequiredService<RpcChannel>()));
        }

        // The one that cannot be a proxy: signing in checks a credential on the server and
        // starts a session on this device, and only half of that can travel.
        services.AddScoped<IAuthService, ApiAuthService>();

        // The other thing that cannot: a document's bytes. JSON has nowhere to put a stream
        // coming back, so this fetches it from its own route. No IDocumentStore is registered
        // here on purpose — the files live on the server, and a head with a store would be a
        // head keeping patient files on its own disk.
        services.AddScoped<IDocumentDownloader, ApiDocumentDownloader>();

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
        services.AddTransient<Features.Help.ViewModels.HelpViewModel>();
        services.AddTransient<PlatformHelpViewModel>();

        services.AddTransient<Features.Setup.ViewModels.SetupViewModel>();

        services.AddTransient<PlansViewModel>();
        services.AddTransient<SmsGatewaysViewModel>();
        services.AddTransient<PlatformMailViewModel>();
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
