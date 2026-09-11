using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Repositories;

namespace DYS.Molargo.Shared.Services;

/// <summary>
/// The one check that decides whether the signed-in person may administer something.
/// </summary>
/// <remarks>
/// <para>
/// Modelled on <c>PlatformGuard</c>, which does the same job for the vendor's own role, and
/// for the same reason: several services need the answer and a security check copied into
/// several files is a check that will eventually be tightened in one of them.
/// </para>
/// <para>
/// This — not the nav item and not the route redirect — is the boundary. <c>MainLayout</c>
/// hides Admin from somebody who cannot use it and bounces them off the route, but that is
/// courtesy: it stops a person staring at a screen full of refusals. A check that lives
/// only in markup is a check that is missing from whichever screen somebody forgets.
/// </para>
/// </remarks>
public interface IPracticeGuard
{
    /// <summary>
    /// Null where the caller may act, or the refusal to return to them.
    /// </summary>
    /// <remarks>
    /// Shaped to match how every service in this app already reports a refusal — a nullable
    /// string returned from the write — so guarding a method is one line at the top of it
    /// rather than a try/catch and an exception type.
    /// </remarks>
    Task<string?> RefuseAsync(
        PracticePermissions wanted, CancellationToken ct = default);

    /// <summary>The same question, where the caller wants to hide a control rather than refuse.</summary>
    Task<bool> AllowsAsync(PracticePermissions wanted, CancellationToken ct = default);

    /// <summary>
    /// Null where the caller owns the practice, or the refusal.
    /// </summary>
    /// <remarks>
    /// For the two things no permission grants: writing a database snapshot, and changing
    /// who owns the practice or who administers what.
    /// </remarks>
    Task<string?> RefuseUnlessOwnerAsync(CancellationToken ct = default);

    /// <summary>Whether the caller owns the practice.</summary>
    Task<bool> IsOwnerAsync(CancellationToken ct = default);
}

/// <inheritdoc cref="IPracticeGuard"/>
public sealed class PracticeGuard : IPracticeGuard
{
    /// <summary>
    /// The refusal every guarded write shares.
    /// </summary>
    /// <remarks>
    /// One wording whatever the reason — no permission, ownership required, an account
    /// deactivated mid-session — and it names who to ask rather than what is missing.
    /// Spelling out which permission would have let it through turns a refusal into a list
    /// of what to request, and the person who can grant it is the one to have the
    /// conversation with anyway.
    /// </remarks>
    public const string NotPermitted =
        "You do not have access to change this. A practice owner can grant it under "
        + "Admin → Users.";

    /// <summary>The refusal for the two owner-only actions, which name themselves.</summary>
    public const string OwnerOnly = "Only a practice owner can do this.";

    private readonly IRepository<Provider> _providers;
    private readonly ISessionService _session;

    public PracticeGuard(IRepository<Provider> providers, ISessionService session)
    {
        _providers = providers;
        _session = session;
    }

    public async Task<string?> RefuseAsync(
        PracticePermissions wanted, CancellationToken ct = default) =>
        await AllowsAsync(wanted, ct).ConfigureAwait(false) ? null : NotPermitted;

    public async Task<bool> AllowsAsync(
        PracticePermissions wanted, CancellationToken ct = default)
    {
        var actor = await ActorAsync(ct).ConfigureAwait(false);

        return actor is not null
            && PracticeAccess.Allows(actor.IsOwner, actor.Permissions, wanted);
    }

    public async Task<string?> RefuseUnlessOwnerAsync(CancellationToken ct = default) =>
        await IsOwnerAsync(ct).ConfigureAwait(false) ? null : OwnerOnly;

    public async Task<bool> IsOwnerAsync(CancellationToken ct = default)
    {
        var actor = await ActorAsync(ct).ConfigureAwait(false);

        return actor?.IsOwner == true;
    }

    /// <summary>
    /// The signed-in person's staff record, or null where there is nobody to check.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read on every call rather than taken from the session. The session's copy is set
    /// when it starts, so it would keep saying "owner" for the rest of the circuit after
    /// ownership was taken away — the person would keep the run of the practice until they
    /// closed the tab.
    /// </para>
    /// <para>
    /// Fails closed in every direction. No provider on the session is a refusal, a record
    /// that has been deleted is a refusal, and a deactivated account is a refusal — an
    /// account switched off mid-session must stop being able to write, which is most of
    /// the point of switching it off.
    /// </para>
    /// <para>
    /// The repository applies the tenant filter, so this can only ever find a staff record
    /// belonging to the practice whose session this is.
    /// </para>
    /// </remarks>
    private async Task<Provider?> ActorAsync(CancellationToken ct)
    {
        if (_session.ProviderId is not { } id) return null;

        var actor = await _providers.GetByIdAsync(id, ct).ConfigureAwait(false);

        return actor is { IsActive: true } ? actor : null;
    }
}
