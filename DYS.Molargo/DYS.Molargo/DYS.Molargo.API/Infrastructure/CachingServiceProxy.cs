using System.Reflection;
using System.Text.Json;
using DYS.Molargo.Domain.Data;
using DYS.Molargo.Services.Api;
using Microsoft.Extensions.Caching.Hybrid;

namespace DYS.Molargo.Api.Infrastructure;

/// <summary>
/// Which reads of a service may be cached, and which writes empty that cache.
/// </summary>
/// <remarks>
/// <para>
/// Both lists are written out. Nothing is inferred from a method's name: "Get" is not a
/// promise that a method is free of side effects, and a convention that decided what to
/// cache would be a convention that eventually cached a write.
/// </para>
/// <para>
/// Anything named in neither list is forwarded untouched — neither cached nor treated as
/// invalidating. That is the safe default for a read that is too large or too varied to
/// cache, which is most of them.
/// </para>
/// </remarks>
/// <param name="Reads">Method names whose results may be held.</param>
/// <param name="Writes">
/// Method names that empty this service's cache for the clinic that called them.
/// </param>
public sealed record CachePlan(Type Contract, string[] Reads, string[] Writes);

/// <summary>
/// Caches a few small, constantly-read answers in front of a service.
/// </summary>
/// <remarks>
/// <para>
/// A proxy rather than a hand-written decorator, for the same reason the client is one:
/// <c>IAdminService</c> has thirty-one methods and <c>IBillingService</c> twenty-nine, and
/// caching one of each would mean writing out the other fifty-eight as forwarders — fifty-
/// eight chances to transcribe a signature wrongly, and fifty-eight edits every time one
/// changes.
/// </para>
/// <para>
/// What belongs behind this: reference data. Procedure codes, fees, the practice's
/// currency — a handful of rows, read on nearly every screen, edited a few times a year.
/// What does not: anything clinical, anything large, and <c>IPracticeGuard</c>, which
/// re-reads the staff record on every call because that is the entire point of it. A cached
/// permission is a withdrawn permission that keeps working until the entry expires.
/// </para>
/// </remarks>
public class CachingServiceProxy : DispatchProxy
{
    private object _inner = null!;
    private HybridCache _cache = null!;
    private ITenantContext _tenant = null!;
    private CachePlan _plan = null!;
    private string _service = null!;
    private ILogger _log = null!;

    private static readonly MethodInfo Unwrap =
        typeof(CachingServiceProxy).GetMethod(
            nameof(UnwrapAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo InvalidatingCall =
        typeof(CachingServiceProxy).GetMethod(
            nameof(InvalidatingCallAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo Fetch =
        typeof(CachingServiceProxy).GetMethod(
            nameof(FetchAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    public static object Create(
        object inner, HybridCache cache, ITenantContext tenant, CachePlan plan, ILogger log)
    {
        var proxy = typeof(DispatchProxy)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method =>
                method.Name == nameof(DispatchProxy.Create)
                && method.IsGenericMethodDefinition
                && method.GetGenericArguments().Length == 2
                && method.GetParameters().Length == 0)
            .MakeGenericMethod(plan.Contract, typeof(CachingServiceProxy))
            .Invoke(null, null)!;

        var caching = (CachingServiceProxy)proxy;
        caching._inner = inner;
        caching._cache = cache;
        caching._tenant = tenant;
        caching._plan = plan;
        caching._service = MolargoRpc.NameOf(plan.Contract);
        caching._log = log;

        return proxy;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method is null) throw new ArgumentNullException(nameof(method));

        // No tenant, no caching. An empty tenant is a request that has not resolved a
        // clinic, and every one of those shares a key.
        if (_tenant.TenantId == Guid.Empty) return method.Invoke(_inner, args);

        if (_plan.Writes.Contains(method.Name, StringComparer.Ordinal))
        {
            // The declared return type, not whatever is convenient. A write declared
            // Task<string?> — most of them are, because they answer with a refusal or null —
            // must be handed back a Task<string?>, or the caller's await fails with an
            // invalid cast naming a compiler-generated state machine and nothing useful.
            if (method.ReturnType is { IsGenericType: true } writeReturns
                && writeReturns.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var written = (Task<object?>)InvalidatingCall
                    .MakeGenericMethod(writeReturns.GetGenericArguments()[0])
                    .Invoke(this, [method, args ?? []])!;

                return Unwrap
                    .MakeGenericMethod(writeReturns.GetGenericArguments()[0])
                    .Invoke(null, [written]);
            }

            return InvalidateAsync(method, args);
        }

        if (!_plan.Reads.Contains(method.Name, StringComparer.Ordinal)
            || method.ReturnType is not { IsGenericType: true } returns
            || returns.GetGenericTypeDefinition() != typeof(Task<>))
        {
            return method.Invoke(_inner, args);
        }

        var result = returns.GetGenericArguments()[0];

        var call = (Task<object?>)Fetch
            .MakeGenericMethod(result)
            .Invoke(this, [method, args ?? []])!;

        return Unwrap.MakeGenericMethod(result).Invoke(null, [call]);
    }

    private async Task<object?> FetchAsync<T>(MethodInfo method, object?[] args)
    {
        try
        {
            return await _cache.GetOrCreateAsync(
                Key(method, args),
                (Proxy: this, Method: method, Args: args),
                static (state, _) => new ValueTask<T>(
                    (Task<T>)state.Method.Invoke(state.Proxy._inner, state.Args)!),
                tags: [Tag]).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A cache that cannot be reached is a slow app, not a broken one. Redis being
            // down must not take the practice down with it — the database still has the
            // answer, and asking it directly is exactly what the app did before anybody
            // added a cache.
            _log.LogWarning(
                ex, "Cache read failed for {Service}.{Method}; going to the database.",
                _service, method.Name);

            return await ((Task<T>)method.Invoke(_inner, args)!).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Empties this service's cached reads for this clinic, after the write has committed.
    /// </summary>
    /// <remarks>
    /// Coarse on purpose: every cached read of the service goes, not the entries a given
    /// write touched. Working out which fee row affects which catalogue page is a second
    /// model of the data that would be wrong the first time either changed — and these
    /// caches hold tens of rows, so refilling them costs one query.
    ///
    /// After the call, never before. Clearing first leaves a window where another request
    /// refills the cache from the old state and then the write lands behind it.
    /// </remarks>
    /// <summary>A write that answers with something, followed by the invalidation.</summary>
    private async Task<object?> InvalidatingCallAsync<T>(MethodInfo method, object?[] args)
    {
        var result = await ((Task<T>)method.Invoke(_inner, args)!).ConfigureAwait(false);

        await ClearAsync(method).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Empties the tag, and never lets failing to do so fail the write.
    /// </summary>
    /// <remarks>
    /// The write has already committed by the time this runs. Throwing here would report a
    /// save that succeeded as a save that failed — which is the worse of the two errors, and
    /// the one somebody acts on by saving again.
    ///
    /// The cost of swallowing it is a stale entry until it expires, which for the reference
    /// data behind this plan is a fee list a couple of minutes out of date. Logged as a
    /// warning so that is visible rather than merely survivable.
    /// </remarks>
    private async Task ClearAsync(MethodInfo method)
    {
        try
        {
            await _cache.RemoveByTagAsync(Tag).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(
                ex,
                "{Service}.{Method} committed, but its cached reads could not be cleared. "
                    + "They will be stale until they expire.",
                _service, method.Name);
        }
    }

    private async Task InvalidateAsync(MethodInfo method, object?[]? args)
    {
        var call = method.Invoke(_inner, args);

        if (call is Task running) await running.ConfigureAwait(false);

        await ClearAsync(method).ConfigureAwait(false);
    }

    /// <summary>One tag per service per clinic, so a write never empties another's cache.</summary>
    private string Tag => $"svc:{_service}:{_tenant.TenantId}";

    /// <summary>
    /// The key: clinic, service, method, and the arguments that varied it.
    /// </summary>
    /// <remarks>
    /// The tenant leads, and that is not decoration — a key without it serves one clinic's
    /// fees to another, which is the one mistake a multi-tenant cache must not make.
    /// Arguments are serialised because the same method asked two different questions is two
    /// different answers: a catalogue for one site is not the catalogue for another.
    /// </remarks>
    private string Key(MethodInfo method, object?[] args)
    {
        var carried = args
            .Where(argument => argument is not CancellationToken)
            .ToArray();

        var suffix = carried.Length == 0
            ? string.Empty
            : ":" + JsonSerializer.Serialize(carried, MolargoRpc.Json);

        return $"{Tag}:{method.Name}{suffix}";
    }

    private static async Task<T> UnwrapAsync<T>(Task<object?> call)
    {
        var value = await call.ConfigureAwait(false);

        return value is null ? default! : (T)value;
    }
}
