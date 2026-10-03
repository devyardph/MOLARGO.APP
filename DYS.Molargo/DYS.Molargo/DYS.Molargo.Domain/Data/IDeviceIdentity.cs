namespace DYS.Molargo.Domain.Data;

/// <summary>
/// Which installation an action was taken from, for the audit trail.
/// </summary>
/// <remarks>
/// <para>
/// Its own interface rather than another method on <see cref="IMolargoContextSource"/>.
/// That one hands out a context and nothing else, deliberately — see its remarks — and a
/// server's context source has no device to identify. Two small interfaces let the API
/// answer this question its own way, from the request, while the device answers it from
/// the row it minted on first use.
/// </para>
/// <para>
/// The answer is a stable string, not a type: an audit entry records what it was told, and
/// a device id that changed shape between heads would make two years of entries
/// incomparable.
/// </para>
/// </remarks>
public interface IDeviceIdentity
{
    /// <summary>
    /// This installation's id. Meaningful offline: it is how an audit entry is traced to a
    /// specific tablet.
    /// </summary>
    Task<string> GetDeviceIdAsync(CancellationToken ct = default);
}
