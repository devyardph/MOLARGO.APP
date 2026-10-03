namespace DYS.Molargo.Shared.Api;

/// <summary>
/// Where the server is, when the app is running against one.
/// </summary>
/// <remarks>
/// A class rather than a constant: the address differs per build and per tester, and the
/// one thing it must never be is compiled in. The head reads it from its own configuration
/// and registers this.
/// </remarks>
public sealed class MolargoApiOptions
{
    /// <summary>
    /// The API's base address, including the scheme.
    /// </summary>
    /// <remarks>
    /// Refused unless it is absolute. A relative address would be resolved against whatever
    /// the <c>HttpClient</c> happened to have, which on a device is nothing — and the
    /// failure would arrive as a null-reference inside the handler rather than as "the
    /// server address is wrong".
    /// </remarks>
    public required Uri BaseAddress { get; init; }

    /// <summary>
    /// How long one call may take.
    /// </summary>
    /// <remarks>
    /// Thirty seconds, not the <c>HttpClient</c> default of a hundred. A screen that has
    /// been waiting half a minute has already failed as far as the person looking at it is
    /// concerned, and the useful thing is to say so rather than to keep the spinner turning
    /// for another seventy.
    /// </remarks>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Holds the bearer token for the signed-in session.
/// </summary>
/// <remarks>
/// <para>
/// An interface because the device and a test want different storage, and because the thing
/// being stored is a credential: naming the seam makes it searchable, and there is exactly
/// one place to look when asking where the token lives.
/// </para>
/// <para>
/// Nothing here persists it. A token that outlived the process would have to be written to
/// disk, and a bearer token on disk is a credential anybody with the file can replay for as
/// long as it lasts. Signing in again is cheap; this is not the thing to make convenient.
/// </para>
/// </remarks>
public interface IApiTokenStore
{
    /// <summary>The current token, or null when nobody is signed in.</summary>
    string? Token { get; }

    /// <summary>Whether a token is held. Not whether it is still valid — only the server knows that.</summary>
    bool HasToken { get; }

    void Set(string token);

    void Clear();
}

/// <inheritdoc cref="IApiTokenStore"/>
public sealed class ApiTokenStore : IApiTokenStore
{
    public string? Token { get; private set; }

    public bool HasToken => !string.IsNullOrEmpty(Token);

    public void Set(string token) => Token = token;

    public void Clear() => Token = null;
}

/// <summary>
/// A call that did not arrive, or arrived and was refused.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a transport exception reaching the view models. A screen catching
/// <c>HttpRequestException</c> would have to know this app talks HTTP at all, which in local
/// mode it does not — and the whole point of the switch is that nothing above the service
/// interface can tell which mode it is in.
/// </para>
/// <para>
/// <see cref="Unauthorised"/> is separated from the rest because it is the one failure with
/// an action attached: the token has expired, and the answer is to sign in again rather than
/// to retry.
/// </para>
/// </remarks>
public sealed class ApiCallException : Exception
{
    public ApiCallException(string message, bool unauthorised = false, Exception? inner = null)
        : base(message, inner)
    {
        Unauthorised = unauthorised;
    }

    /// <summary>Whether the server rejected the token rather than the request.</summary>
    public bool Unauthorised { get; }

    /// <summary>
    /// What a screen shows when the server could not be reached.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Says the practice's records are on the server, because in API mode they are and the
    /// person needs to know that waiting will not help — there is no local copy to fall back
    /// to. A generic "something went wrong" would have them retrying a dead connection.
    /// </para>
    /// <para>
    /// It names the address it tried. The first version did not, and the first time it fired
    /// in anger there was no way to tell a stopped server from a wrong port from a phone that
    /// cannot resolve "localhost" — three different problems behind one sentence. The address
    /// is configuration, not a secret, and it is the single most useful thing on the screen.
    /// </para>
    /// <para>
    /// In a debug build it adds what the transport actually said. That stays out of release
    /// builds deliberately: "No connection could be made because the target machine actively
    /// refused it" helps whoever is building the app and tells the front desk nothing.
    /// </para>
    /// </remarks>
    public static string Unreachable(Uri? address = null, Exception? cause = null)
    {
        var message =
            "The practice server could not be reached"
            + (address is null ? string.Empty : $" at {address}")
            + ". Check the connection and try again — records are held on the server, so "
            + "nothing can be loaded until it answers.";

#if DEBUG
        if (cause is not null) message += $"\n\n[debug] {cause.GetType().Name}: {cause.Message}";
#endif

        return message;
    }

    /// <summary>What a screen shows when the token is no longer good.</summary>
    public const string Expired = "Your session has expired. Sign in again to continue.";
}
