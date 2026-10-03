using System.Reflection;
using System.Text.Json;
using DYS.Molargo.Api.Infrastructure;
using DYS.Molargo.Services.Api;
using DYS.Molargo.Services;

namespace DYS.Molargo.Api.Endpoints;

/// <summary>
/// The app's own surface: every feature service, callable by name.
/// </summary>
/// <remarks>
/// <para>
/// This is what a device in API mode talks to. It runs the same service classes the device
/// would have run against its own SQLite file, against PostgreSQL instead — so "may this
/// invoice be voided", "does this clash with another booking" and every other rule is
/// answered by one implementation, not by two that drift.
/// </para>
/// <para>
/// It does not replace the REST endpoints beside it. Those are the documented surface for
/// anyone integrating from outside, where a stable URL matters; this one is an internal
/// transport between two halves of the same program, and is versioned by the fact that both
/// halves ship together.
/// </para>
/// </remarks>
public sealed class RpcEndpoint : IEndpoint
{
    /// <summary>
    /// The callable surface, resolved once.
    /// </summary>
    /// <remarks>
    /// Built at class initialisation rather than per request, and built from
    /// <see cref="MolargoRpc.Services"/> so the server cannot offer a service the catalogue
    /// does not list. A reflection walk per call would also be the kind of cost that only
    /// shows up under load.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, ServiceEntry> Catalogue =
        MolargoRpc.Services.ToDictionary(
            MolargoRpc.NameOf,
            service => new ServiceEntry(service, MolargoRpc.MethodsOf(service)),
            StringComparer.Ordinal);

    private sealed record ServiceEntry(Type Contract, IReadOnlyDictionary<string, MethodInfo> Methods);

    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(MolargoRpc.Prefix).WithTags("App");

        // POST for every method, including the reads. A GET would be the better shape for
        // half of these, but only half — and the half is decided by the method's own
        // semantics, which a dispatcher cannot see. One verb for all of them means the
        // client never has to know which kind it is calling, and a read whose arguments are
        // a filter object has nowhere to put them on a GET anyway.
        group.MapPost("/{service}/{method}/{arity:int}", DispatchAsync)

            // Left out of the OpenAPI document deliberately. It describes 270 methods whose
            // shapes are C# signatures, not resource representations; listing them would bury
            // the REST surface somebody actually integrates against under ten times its
            // volume of noise.
            .ExcludeFromDescription();
    }

    /// <summary>POST /rpc/{service}/{method}/{arity}</summary>
    private static async Task<IResult> DispatchAsync(
        string service,
        string method,
        int arity,
        HttpContext context,
        IApiCaller caller,
        ISessionService session,
        CancellationToken ct)
    {
        if (!Catalogue.TryGetValue(service, out var entry))
        {
            // Named, because the caller is this codebase and the useful failure says which
            // name was not found. There is nothing to withhold: the catalogue is a constant
            // in an assembly the client already ships.
            return Results.NotFound(new { error = $"No service called '{service}'." });
        }

        if (!entry.Methods.TryGetValue($"{method}/{arity}", out var target))
        {
            return Results.NotFound(new
            {
                error = $"{service} has no {method} taking {arity} argument(s).",
            });
        }

        object?[] arguments;

        try
        {
            arguments = await ReadArgumentsAsync(context.Request, target, ct)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            return Results.Problem(
                $"The arguments for {service}.{method} could not be read: {ex.Message}",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // The services read who is acting from the session, exactly as they do on a device.
        // Filling it from the token here is what lets them be the same classes: without it
        // every service would need a second way to find the caller, which is the duplication
        // this whole endpoint exists to avoid.
        await PrimeSessionAsync(session, caller, ct).ConfigureAwait(false);

        var instance = context.RequestServices.GetRequiredService(entry.Contract);
        var result = target.Invoke(instance, Fill(target, arguments, ct));

        return result switch
        {
            null => Results.Ok(),
            Task task => await AwaitAsync(task).ConfigureAwait(false),

            // Every member of every catalogued interface is async today. If one stops being,
            // this says so rather than serialising something the client's proxy has already
            // refused to call.
            _ => Results.Problem(
                $"{service}.{method} is not asynchronous and cannot be called over the wire.",
                statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// Reads the argument array, each element against its own parameter type.
    /// </summary>
    /// <remarks>
    /// Element by element rather than as a single <c>object?[]</c>: the elements have
    /// different types and only the signature knows them. A missing trailing element is
    /// taken as the parameter's default, so adding an optional argument to a service method
    /// does not break a client built before it existed.
    /// </remarks>
    private static async Task<object?[]> ReadArgumentsAsync(
        HttpRequest request, MethodInfo target, CancellationToken ct)
    {
        var expected = MolargoRpc.ArgumentsOf(target);

        // A file is on its way up: the arguments are a part of the form rather than the
        // whole body, and the stream arguments arrive beside them as their own parts. See
        // RpcChannel.Package — the JSON is identical either way, with a null where each
        // stream belongs, so everything below this is the ordinary path.
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(ct).ConfigureAwait(false);

            using var sent = JsonDocument.Parse(form[MolargoRpc.ArgumentsPart].ToString());
            var filled = Read(sent.RootElement, expected);

            foreach (var file in form.Files)
            {
                // The part name is the argument's position. Anything else in the form is
                // ignored rather than guessed at: a part this does not recognise is a client
                // sending something this build does not understand, and filling an argument
                // from it would be worse than leaving it null.
                if (!file.Name.StartsWith(MolargoRpc.StreamPart, StringComparison.Ordinal)
                    || !int.TryParse(file.Name[MolargoRpc.StreamPart.Length..], out var at)
                    || at < 0
                    || at >= filled.Length)
                {
                    continue;
                }

                filled[at] = file.OpenReadStream();
            }

            return filled;
        }

        using var document = await JsonDocument
            .ParseAsync(request.Body, cancellationToken: ct).ConfigureAwait(false);

        return Read(document.RootElement, expected);
    }

    /// <summary>Reads the JSON argument array against the signature.</summary>
    private static object?[] Read(JsonElement sent, ParameterInfo[] expected)
    {
        if (sent.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Expected an array of arguments.");
        }

        var values = new object?[expected.Length];

        for (var i = 0; i < expected.Length; i++)
        {
            if (i >= sent.GetArrayLength())
            {
                values[i] = expected[i].HasDefaultValue
                    ? expected[i].DefaultValue
                    : Fallback(expected[i].ParameterType);

                continue;
            }

            // A stream argument is null in the JSON by construction and is filled from its
            // own part afterwards. Deserialising null into Stream would throw here instead.
            values[i] = typeof(Stream).IsAssignableFrom(expected[i].ParameterType)
                ? null
                : sent[i].Deserialize(expected[i].ParameterType, MolargoRpc.Json);
        }

        return values;
    }

    /// <summary>
    /// Puts the cancellation token back where the signature wants it.
    /// </summary>
    /// <remarks>
    /// The token never travels — see <see cref="MolargoRpc.ArgumentsOf"/> — so the array read
    /// off the wire is one short wherever a method takes one, and the request's own token
    /// goes in that slot. That makes a client that gave up actually stop the work here,
    /// rather than leaving a query running for a screen nobody is looking at.
    /// </remarks>
    private static object?[] Fill(MethodInfo target, object?[] arguments, CancellationToken ct)
    {
        var all = target.GetParameters();
        var full = new object?[all.Length];

        for (int i = 0, read = 0; i < all.Length; i++)
        {
            full[i] = all[i].ParameterType == typeof(CancellationToken)
                ? ct
                : arguments[read++];
        }

        return full;
    }

    /// <summary>Awaits the call and returns whatever it produced.</summary>
    /// <remarks>
    /// <c>Task.Result</c> is read through reflection because the declared type is
    /// <c>Task&lt;T&gt;</c> for a T only known at runtime. Read after the await, never
    /// instead of it: reading it first would block a request thread and would wrap any
    /// refusal the service made in an <c>AggregateException</c>.
    /// </remarks>
    private static async Task<IResult> AwaitAsync(Task task)
    {
        await task.ConfigureAwait(false);

        var type = task.GetType();

        if (!type.IsGenericType) return Results.Ok();

        var value = type.GetProperty(nameof(Task<object>.Result))?.GetValue(task);

        // A null result is written as the JSON literal null, not left as an empty body.
        //
        // Results.Json(null) writes nothing at all — zero bytes — and zero bytes is not a
        // JSON document, so the client's deserialiser threw "the input does not contain any
        // JSON tokens" for every method that legitimately answers null. Which is most of
        // them: every GetSomethingAsync that may not find it, every "is there a conflict"
        // check whose good answer is no. It looked like a serialisation bug on whichever
        // screen happened to ask first.
        if (value is null) return Results.Text("null", "application/json");

        // Serialised with the catalogue's options rather than the host's. The two differ —
        // the host camel-cases names for the REST surface — and a payload written by one and
        // read by the other loses every property it renamed, silently and as nulls.
        return Results.Json(value, MolargoRpc.Json);
    }

    /// <summary>
    /// Starts the server's session as the token describes it.
    /// </summary>
    /// <remarks>
    /// The session is scoped, so this is one request's own and cannot leak to another. It
    /// reads the staff record itself for the display name and the acting person's sites,
    /// which is one indexed read and the same one a device does at sign-in.
    /// </remarks>
    private static async Task PrimeSessionAsync(
        ISessionService session, IApiCaller caller, CancellationToken ct)
    {
        if (session.IsSignedIn) return;

        await session.SignInAsync(
            caller.ProviderId,
            caller.DisplayName,
            caller.Role,
            caller.IsOwner,
            caller.Permissions,
            ct).ConfigureAwait(false);

        if (caller.LocationId != Guid.Empty) session.SelectLocation(caller.LocationId);
    }

    /// <summary>
    /// What a value-typed parameter gets when the caller sent nothing for it.
    /// </summary>
    /// <remarks>
    /// A reference type takes null, which is what the array already holds. A value type
    /// cannot, and <c>MethodInfo.Invoke</c> throws on a null for one — with a message naming
    /// neither the method nor the parameter.
    /// </remarks>
    private static object? Fallback(Type type) =>
        type.IsValueType ? Activator.CreateInstance(type) : null;
}
