using DYS.Molargo.Domain;
using DYS.Molargo.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace DYS.Molargo.Shared.Features.Platform.Services;

/// <summary>
/// The one check that decides whether a caller may act as the vendor.
/// </summary>
/// <remarks>
/// <para>
/// Extracted so there is exactly one implementation of it. Two services need the answer —
/// the tenant list and the operator list — and a security check copied into two files is a
/// check that will eventually be tightened in one of them.
/// </para>
/// <para>
/// Static, and takes the context it should read through. It deliberately has no access to
/// <c>ISessionService</c>: the caller passes the provider id the session claims, and this
/// goes to the staff record to find out what that provider actually is.
/// </para>
/// </remarks>
internal static class PlatformGuard
{
    /// <summary>
    /// The refusal every platform method shares.
    /// </summary>
    /// <remarks>
    /// Identical whatever the reason — not signed in, a clinic's own administrator, an
    /// account that has been deactivated. Naming which would tell a practice's admin that
    /// a platform role exists and roughly how close they are to it.
    /// </remarks>
    internal const string NotPermitted = "That is not something this account can do.";

    /// <summary>
    /// Whether the account really holds the platform role, per the database.
    /// </summary>
    /// <remarks>
    /// Read on every call rather than taken from the session. The session's copy is set
    /// when it starts and would keep saying "super admin" for the rest of a circuit after
    /// the role was taken away — which for this role means keeping the run of every clinic
    /// on the platform until the person closes their tab.
    ///
    /// No filter bypass: a super admin's own record lives in the platform tenant, which is
    /// the tenant their session is already in.
    /// </remarks>
    internal static async Task<bool> IsSuperAdminAsync(
        MolargoDbContext db, Guid? providerId, CancellationToken ct)
    {
        if (providerId is not { } id) return false;

        var actor = await db.Providers
            .AsNoTracking()
            .FirstOrDefaultAsync(provider => provider.Id == id, ct)
            .ConfigureAwait(false);

        return actor is { IsActive: true } && ProviderRoles.IsPlatform(actor.Role);
    }
}
