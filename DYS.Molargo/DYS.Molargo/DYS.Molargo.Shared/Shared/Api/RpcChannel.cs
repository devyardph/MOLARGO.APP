using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;

namespace DYS.Molargo.Shared.Api;

/// <summary>
/// One call to the server: arguments out, return value back.
/// </summary>
/// <remarks>
/// The only place in the app that knows a service call can be an HTTP request. Everything
/// above it holds a feature interface and cannot tell.
/// </remarks>
public sealed class RpcChannel
{
    private readonly HttpClient _http;
    private readonly IApiTokenStore _tokens;

    public RpcChannel(HttpClient http, IApiTokenStore tokens)
    {
        _http = http;
        _tokens = tokens;
    }

    /// <summary>
    /// Invokes one method and returns its result, already deserialised.
    /// </summary>
    /// <param name="service">The interface, as the catalogue names it.</param>
    /// <param name="method">The method key — name and arity.</param>
    /// <param name="arguments">
    /// The arguments in declaration order, the cancellation token excluded. Sent as an array
    /// rather than an object keyed by parameter name: the order is the signature, which both
    /// sides read from the same <see cref="MethodInfo"/>, whereas names would let a renamed
    /// parameter silently stop binding and pass null instead.
    /// </param>
    /// <param name="returns">
    /// The type to read the response as — the method's <c>Task&lt;T&gt;</c> payload, or null
    /// for a method that returns a bare <c>Task</c>.
    /// </param>
    public async Task<object?> InvokeAsync(
        string service,
        string method,
        object?[] arguments,
        Type? returns,
        CancellationToken ct)
    {
        HttpResponseMessage response;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"{MolargoRpc.Prefix}/{service}/{method}")
            {
                Content = Package(arguments),
            };

            // Set per request rather than on a header the client keeps, because the token
            // changes when somebody signs out and in again on the same device, and a default
            // header set once at construction would still be the first person's.
            if (_tokens.Token is { Length: > 0 } token)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller walked off the screen. Not a failure, and not something to dress up
            // as one — let it propagate as cancellation so the view model's own handling
            // applies.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // A timeout arrives as TaskCanceledException with nobody having cancelled, which
            // is why it is caught beside the transport failure: to the screen they are the
            // same event.
            throw new ApiCallException(
                ApiCallException.Unreachable(_http.BaseAddress, ex), inner: ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new ApiCallException(ApiCallException.Expired, unauthorised: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ApiCallException(await DescribeAsync(response).ConfigureAwait(false));
            }

            if (returns is null) return null;

            // Nothing came back. Belt as well as the braces on the server, which now writes
            // the literal null: an empty body cannot mean anything but "no value", and
            // handing it to the deserialiser produces "the input does not contain any JSON
            // tokens" — a parse error for what is actually a perfectly ordinary answer.
            //
            // Also covers a 204, which a later route may reasonably use for the same thing.
            if (response.StatusCode == HttpStatusCode.NoContent
                || response.Content.Headers.ContentLength == 0)
            {
                return null;
            }

            await using var body = await response.Content
                .ReadAsStreamAsync(ct).ConfigureAwait(false);

            return await JsonSerializer
                .DeserializeAsync(body, returns, MolargoRpc.Json, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The request body: plain JSON, or multipart when a file is being sent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One method on one interface takes a <see cref="Stream"/> — attaching a scan to a
    /// patient — and a stream is the one thing JSON cannot carry. Handled here rather than
    /// by hand-writing a client for that whole interface: twenty-one methods would have been
    /// restated to make one of them work, and the twenty that did not need it are exactly
    /// the ones that would drift.
    /// </para>
    /// <para>
    /// The arguments still travel as the same JSON array, with the stream's slot written as
    /// null, and each stream rides beside it as a part named for its index. So the server
    /// reads the arguments the same way it always does and then fills the holes — the
    /// ordinary path stays the ordinary path.
    /// </para>
    /// <para>
    /// The streams are not copied into memory first. A radiograph is tens of megabytes and
    /// a device has little to spare; <see cref="StreamContent"/> sends it as it reads it.
    /// </para>
    /// </remarks>
    private static HttpContent Package(object?[] arguments)
    {
        var files = arguments
            .Select((value, index) => (Value: value as Stream, Index: index))
            .Where(entry => entry.Value is not null)
            .ToList();

        if (files.Count == 0)
        {
            return JsonContent.Create(arguments, options: MolargoRpc.Json);
        }

        var form = new MultipartFormDataContent();

        var withoutStreams = arguments
            .Select(value => value is Stream ? null : value)
            .ToArray();

        form.Add(JsonContent.Create(withoutStreams, options: MolargoRpc.Json), MolargoRpc.ArgumentsPart);

        foreach (var (stream, index) in files)
        {
            // Named for the argument position, which is how the far side knows which hole
            // it fills. A file name would read better and would be the thing to get wrong:
            // the caller already passes the real one as its own string argument.
            form.Add(new StreamContent(stream!), $"{MolargoRpc.StreamPart}{index}", $"{MolargoRpc.StreamPart}{index}");
        }

        return form;
    }

    /// <summary>
    /// Turns a refusal into something a person can read.
    /// </summary>
    /// <remarks>
    /// The server sends a ProblemDetails for a refusal it meant, and that document's detail
    /// is the sentence the service itself wrote — so it is shown rather than replaced. The
    /// status code is the fallback, and a bare "500" is still better than "an error
    /// occurred", because it says which side failed.
    /// </remarks>
    private static async Task<string> DescribeAsync(HttpResponseMessage response)
    {
        try
        {
            var problem = await response.Content
                .ReadFromJsonAsync<RpcProblem>(MolargoRpc.Json).ConfigureAwait(false);

            if (problem?.Detail is { Length: > 0 } detail) return detail;
            if (problem?.Title is { Length: > 0 } title) return title;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Not a problem document. Fall through to the status code.
        }

        return $"The server refused the request ({(int)response.StatusCode} "
            + $"{response.ReasonPhrase}).";
    }

    /// <summary>The slice of ProblemDetails this reads. Its own type so the API's is not a dependency.</summary>
    private sealed record RpcProblem(string? Title, string? Detail);
}

/// <summary>
/// Implements a feature interface by sending each call to the server.
/// </summary>
/// <remarks>
/// <para>
/// One class for all of them, rather than twenty-eight hand-written clients. Every method on
/// every feature interface has the same shape — arguments in, <c>Task</c> or
/// <c>Task&lt;T&gt;</c> out — so there is nothing per-method for a hand-written client to
/// add beyond a URL, and a URL is derivable. What a hand-written client would add instead is
/// 270 chances to transcribe a signature wrongly.
/// </para>
/// <para>
/// <see cref="DispatchProxy"/> rather than a source generator: it needs no build step, and
/// an interface gaining a method needs no regeneration — the proxy simply answers it. The
/// cost is that a mistake shows up when the method is called rather than when it is
/// compiled, which is why <see cref="Invoke"/> refuses anything it cannot carry by name
/// instead of returning a default.
/// </para>
/// </remarks>
public class RpcServiceProxy : DispatchProxy
{
    private RpcChannel _channel = null!;
    private string _service = null!;

    /// <summary>
    /// Handlers subscribed to this service's events, by event name.
    /// </summary>
    /// <remarks>
    /// Held so that unsubscribing works — a component that added a handler on initialise
    /// removes it on dispose, and a remove that threw would fail every teardown. Nothing
    /// raises them: see the add accessor.
    /// </remarks>
    private readonly Dictionary<string, Delegate?> _events = new(StringComparer.Ordinal);

    private static readonly MethodInfo Unwrap =
        typeof(RpcServiceProxy).GetMethod(
            nameof(UnwrapAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// The two-type-argument, no-parameter <c>DispatchProxy.Create&lt;T, TProxy&gt;()</c>.
    /// </summary>
    /// <remarks>
    /// Found by shape rather than by <c>GetMethod(name)</c>, which throws
    /// <c>AmbiguousMatchException</c>: <c>Create</c> is overloaded, and asking for it by
    /// name alone stopped working the moment a second overload existed. Matching on the
    /// arity instead keeps working whatever else is added beside it.
    /// </remarks>
    private static readonly MethodInfo CreateProxy =
        typeof(DispatchProxy)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method =>
                method.Name == nameof(DispatchProxy.Create)
                && method.IsGenericMethodDefinition
                && method.GetGenericArguments().Length == 2
                && method.GetParameters().Length == 0);

    /// <summary>Builds a proxy implementing <paramref name="service"/>.</summary>
    public static object Create(Type service, RpcChannel channel)
    {
        var proxy = CreateProxy
            .MakeGenericMethod(service, typeof(RpcServiceProxy))
            .Invoke(null, null)!;

        var rpc = (RpcServiceProxy)proxy;
        rpc._channel = channel;
        rpc._service = MolargoRpc.NameOf(service);

        return proxy;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method is null) throw new ArgumentNullException(nameof(method));

        var all = method.GetParameters();
        var values = args ?? [];

        // The token is pulled out of the arguments rather than dropped, so aborting the
        // request still works — it is the local half of what the far side's own token means.
        var ct = CancellationToken.None;
        var payload = new List<object?>(all.Length);

        for (var i = 0; i < all.Length; i++)
        {
            if (all[i].ParameterType == typeof(CancellationToken))
            {
                if (i < values.Length && values[i] is CancellationToken token) ct = token;
                continue;
            }

            payload.Add(i < values.Length ? values[i] : null);
        }

        var returnType = method.ReturnType;

        // Task<T>: the call returns T, and the Task has to be the declared one — returning
        // Task<object> would throw an invalid cast at the await, far from the cause.
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var result = returnType.GetGenericArguments()[0];

            var call = _channel.InvokeAsync(
                _service, MolargoRpc.KeyOf(method), [.. payload], result, ct);

            return Unwrap.MakeGenericMethod(result).Invoke(null, [call]);
        }

        // Bare Task: nothing to read back, and the Task<object?> is already a Task.
        if (returnType == typeof(Task))
        {
            return _channel.InvokeAsync(
                _service, MolargoRpc.KeyOf(method), [.. payload], returns: null, ct);
        }

        // An event's add/remove accessor. Kept locally: subscribing is how a component says
        // "tell me when this changes", and throwing on it took down the whole app bar — the
        // error that found this said SetupService.remove_Changed could not cross the wire,
        // which was true and was not a reason to refuse.
        //
        // The handler is held and never invoked, because nothing on the server can call back
        // down an HTTP response. So a subscriber is not told about a change made elsewhere;
        // it still sees the new state the next time it loads. That is a real loss of
        // liveness, and much smaller than the screen not rendering.
        if (method.IsSpecialName && method.Name.StartsWith("add_", StringComparison.Ordinal))
        {
            if (values.Length > 0 && values[0] is Delegate handler)
            {
                lock (_events)
                {
                    _events[method.Name[4..]] =
                        Delegate.Combine(_events.GetValueOrDefault(method.Name[4..]), handler);
                }
            }

            return null;
        }

        if (method.IsSpecialName && method.Name.StartsWith("remove_", StringComparison.Ordinal))
        {
            if (values.Length > 0 && values[0] is Delegate handler)
            {
                lock (_events)
                {
                    _events[method.Name[7..]] =
                        Delegate.Remove(_events.GetValueOrDefault(method.Name[7..]), handler);
                }
            }

            return null;
        }

        // A property or a synchronous method. Refused loudly rather than given a default: a
        // proxy that answered false or null for "may this user do that" would be a permission
        // check that passes because the network was not involved.
        throw new NotSupportedException(
            $"{_service}.{method.Name} returns {returnType.Name}, which cannot cross the "
            + "wire. Only Task and Task<T> members can be called in API mode — a member that "
            + "has to answer synchronously belongs on a local service.");
    }

    private static async Task<T> UnwrapAsync<T>(Task<object?> call)
    {
        var value = await call.ConfigureAwait(false);

        // default rather than a cast through null: a method declared Task<string?> that the
        // server answered with null is answering legitimately, and (T)null! would throw for
        // a value type while saying nothing useful about which call it was.
        return value is null ? default! : (T)value;
    }
}
