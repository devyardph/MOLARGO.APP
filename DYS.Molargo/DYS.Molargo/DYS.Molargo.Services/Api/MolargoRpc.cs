using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using DYS.Molargo.Services.Features.Admin;
using DYS.Molargo.Services.Features.Auth;
using DYS.Molargo.Services.Features.Billing;
using DYS.Molargo.Services.Features.Charting;
using DYS.Molargo.Services.Features.Comms;
using DYS.Molargo.Services.Features.Diary;
using DYS.Molargo.Services.Features.FrontDesk;
using DYS.Molargo.Services.Features.Help;
using DYS.Molargo.Services.Features.Inventory;
using DYS.Molargo.Services.Features.Patient;
using DYS.Molargo.Services.Features.Platform;
using DYS.Molargo.Services.Features.Prescribing;
using DYS.Molargo.Services.Features.Reports;
using DYS.Molargo.Services.Features.Comms;
using DYS.Molargo.Services.Features.Setup;
using DYS.Molargo.Services.Features.Treatment;

namespace DYS.Molargo.Services.Api;

/// <summary>
/// What may be called over the wire, and how a call is addressed.
/// </summary>
/// <remarks>
/// <para>
/// The app's feature interfaces carry 270-odd methods between them. Restating every one of
/// those as a hand-written REST route and a hand-written client method is several hundred
/// pieces of code whose only job is to copy a signature that already exists — and the first
/// one copied wrong is a bug that compiles.
/// </para>
/// <para>
/// So the interface is the contract. The server dispatches to the same service class the
/// device would have run locally, and the client is a proxy generated from the same
/// interface — which means a method added to a feature service is callable from both sides
/// with no further edit anywhere.
/// </para>
/// <para>
/// The REST endpoints under <c>/patients</c>, <c>/billing</c> and the rest are not replaced
/// by this and are not for the app: they are the documented surface a third party
/// integrates against, where a stable URL and an OpenAPI document matter more than covering
/// every method. This surface is the app talking to its own server, where they do not.
/// </para>
/// <para>
/// <see cref="Services"/> is written out rather than discovered by scanning the assembly for
/// <c>I*Service</c>. It is the whitelist of what a signed-in caller may invoke by name, and
/// a whitelist that grows by itself whenever somebody adds a type is not a whitelist.
/// </para>
/// </remarks>
public static class MolargoRpc
{
    /// <summary>The route prefix both sides agree on.</summary>
    public const string Prefix = "/rpc";

    /// <summary>
    /// The multipart part holding the JSON argument array, when a file is being sent.
    /// </summary>
    /// <remarks>
    /// Here rather than on the client that writes it. These two names are wire format — the
    /// client names the parts and the server looks them up — so a server reading them off a
    /// client type would be the server referencing the client to learn its own protocol.
    /// Both sides now take them from the contract they already share.
    /// </remarks>
    public const string ArgumentsPart = "args";

    /// <summary>Prefix for a part holding one stream argument, followed by its index.</summary>
    public const string StreamPart = "arg";

    /// <summary>
    /// The interfaces callable over the wire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ISessionService</c> is deliberately absent. It is who is signed in on this device,
    /// read synchronously as properties and raising an event when it changes; none of that
    /// survives a round trip, and proxying it would move the answer to the wrong machine. It
    /// stays local in both modes.
    /// </para>
    /// <para>
    /// <c>IAuthService</c> is absent for a related reason: signing in both checks a
    /// credential, which belongs on the server, and starts the local session, which does
    /// not. See <c>ApiAuthService</c> — the one hand-written client here.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyList<Type> Services =
    [
        typeof(IPatientService),
        typeof(IFrontDeskService),
        typeof(IChartingService),
        typeof(IPerioService),
        typeof(IPrescribingService),
        typeof(IReferralService),
        typeof(IBillingService),
        typeof(IPayrollService),
        typeof(ISubscriptionService),
        typeof(IMedicalHistoryCatalogue),
        typeof(ITreatmentPlanService),
        typeof(IInventoryService),
        typeof(ICommsService),
        typeof(IReportService),
        typeof(IAdminService),
        typeof(INotificationSettingsService),
        typeof(IPrinterSettingsService),
        typeof(IRegistrationService),
        typeof(IPasswordResetService),
        typeof(IPlatformService),
        typeof(IPlatformUserService),
        typeof(IPlanService),
        typeof(ISmsGatewayService),
        typeof(IPlatformMailService),
        typeof(ISubscriptionBillingService),
        typeof(IDiaryService),
        typeof(IAppointmentService),
        typeof(IHelpService),
        typeof(ISetupService),

        // Not a feature service, but the session cannot load without it and the
        // session is the one thing that stays local. See ISessionDirectory.
        typeof(ISessionDirectory),

        // Both are injected directly by view models, so both have to cross. The guard
        // especially: it re-reads the staff record on every call, which is the whole point
        // of it, and a local stand-in that answered from the session would be answering
        // from a copy taken at sign-in — exactly the stale answer it exists to avoid.
        typeof(IPracticeGuard),
        typeof(IReminderRunner),
    ];

    /// <summary>How a service is named in a URL: the interface name without its leading I.</summary>
    public static string NameOf(Type service) =>
        service.Name.Length > 1 && service.Name[0] == 'I' ? service.Name[1..] : service.Name;

    /// <summary>
    /// The methods of one interface, keyed as the wire addresses them.
    /// </summary>
    /// <remarks>
    /// Keyed by name and argument count, because <c>IPerioService</c> has two
    /// <c>GetChartAsync</c> overloads and a key that ignored arity would dispatch "the whole
    /// chart" and "this exam's chart" to whichever one reflection happened to return first.
    /// Both sides build the key from the same <see cref="MethodInfo"/>, so they cannot
    /// disagree about it.
    /// </remarks>
    public static IReadOnlyDictionary<string, MethodInfo> MethodsOf(Type service)
    {
        var map = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);

        foreach (var method in service.GetMethods())
        {
            // Two methods, one key, means the wire cannot tell them apart. Thrown while the
            // catalogue is built — at startup on the server — rather than left to surface as
            // the wrong method being called at runtime, which reads as a data problem for a
            // week before anybody suspects the routing.
            if (!map.TryAdd(KeyOf(method), method))
            {
                throw new InvalidOperationException(
                    $"{service.Name}.{method.Name} takes the same number of arguments as "
                    + "another overload, so a caller cannot address one of them. Give one a "
                    + "distinct name.");
            }
        }

        return map;
    }

    /// <summary>How one method is addressed: name and arity, the cancellation token aside.</summary>
    public static string KeyOf(MethodInfo method) =>
        $"{method.Name}/{ArgumentsOf(method).Length}";

    /// <summary>
    /// A method's parameters, minus the <see cref="CancellationToken"/>.
    /// </summary>
    /// <remarks>
    /// The token does not travel. Cancelling is a local act — the caller left the screen —
    /// and the request is cancelled by aborting it, which the server already observes as its
    /// own token. Serialising one would send a struct that cannot mean anything on another
    /// machine.
    /// </remarks>
    public static ParameterInfo[] ArgumentsOf(MethodInfo method) =>
        method.GetParameters()
            .Where(parameter => parameter.ParameterType != typeof(CancellationToken))
            .ToArray();

    /// <summary>
    /// The serialiser both ends use.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One instance, taken from here by both sides: any difference between two copies of
    /// these options is a silent data bug rather than a failure.
    /// </para>
    /// <para>
    /// Cycles are ignored rather than preserved, and entities do have them — an invoice knows
    /// its lines and each line knows its invoice, so the default serialiser throws on the way
    /// out. <c>IgnoreCycles</c> writes null the second time it reaches the same object, which
    /// for a screen rendering a graph downwards is exactly the branch it was never going to
    /// read.
    /// </para>
    /// <para>
    /// <c>Preserve</c> was the first choice, because it keeps the graph intact rather than
    /// pruning it. It was dropped for what it does to the envelope: under <c>Preserve</c> a
    /// root-level array is written as <c>{"$id":"1","$values":[…]}</c>, so the argument list
    /// stops being an array, and the reference table is shared across arguments — meaning an
    /// argument cannot be deserialised on its own against its own parameter type, which is
    /// the one thing the dispatcher has to do. Fidelity inside a single returned graph was
    /// not worth a wire format that cannot be taken apart.
    /// </para>
    /// <para>
    /// The consequence to know: a back-reference may arrive null. Read downwards from what
    /// the service returned, never back up through a navigation property.
    /// </para>
    /// <para>
    /// Property names stay as declared. A camel-case policy would be the usual choice for a
    /// public API, and this is not one — both ends are this codebase, and matching the C#
    /// exactly means a name seen in a payload is a name that can be searched for.
    /// </para>
    /// </remarks>
    public static readonly JsonSerializerOptions Json = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        PropertyNameCaseInsensitive = true,
    };
}
