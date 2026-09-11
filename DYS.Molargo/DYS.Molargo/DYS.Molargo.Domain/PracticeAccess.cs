using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain;

/// <summary>
/// Who may administer what, in one place.
/// </summary>
/// <remarks>
/// In Domain beside <see cref="ProviderRoles"/> and for the same reason: several layers need
/// the same answer — the guard that refuses a write, the session that decides whether to
/// show a nav item, the editor that decides whether to render a fee box — and a permission
/// rule answered differently in three places is a rule that will be tightened in one of
/// them and left open in the other two.
/// </remarks>
public static class PracticeAccess
{
    /// <summary>Every grantable permission, for the owner's implicit set and the picker.</summary>
    /// <remarks>
    /// Derived from the enum rather than written out, so a permission added later is
    /// granted to owners and offered in the picker without anyone remembering to come
    /// back here. <see cref="PracticePermissions.None"/> is excluded because it is the
    /// absence of a permission, not one of them.
    /// </remarks>
    public static readonly PracticePermissions[] Grantable = Enum
        .GetValues<PracticePermissions>()
        .Where(permission => permission != PracticePermissions.None)
        .ToArray();

    /// <summary>
    /// Whether this account may act in <paramref name="wanted"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An owner passes everything, computed here rather than stored as a full set of flags
    /// on their record. Storing it would mean an owner could be left holding a partial set
    /// — by a stray click, or by a permission added to the enum after their account was
    /// made — and locked out of their own practice's settings with nobody able to let them
    /// back in. There is no password reset and no vendor override in this app.
    /// </para>
    /// <para>
    /// Asking for <see cref="PracticePermissions.None"/> returns true: it is not a
    /// permission, and a caller that guards an ungated action with it is saying "anyone",
    /// which is the honest answer.
    /// </para>
    /// </remarks>
    public static bool Allows(
        bool isOwner, PracticePermissions held, PracticePermissions wanted) =>
        isOwner || wanted == PracticePermissions.None || held.HasFlag(wanted);

    /// <summary>
    /// The permissions that unlock something under <c>/admin</c>.
    /// </summary>
    /// <remarks>
    /// Not every permission does. Pricing is edited on Billing → Item catalogue and stock
    /// costs on Inventory, both outside the Admin section — so holding either is no reason
    /// to be shown an Admin tab whose every pane would refuse. Only these three lead
    /// anywhere: staff to Users and Roles, sites to Sites, settings to Settings.
    /// </remarks>
    public const PracticePermissions AdminSection =
        PracticePermissions.ManageStaff
        | PracticePermissions.ManageSites
        | PracticePermissions.ManageSettings;

    /// <summary>
    /// Whether this account may reach the Admin section at all.
    /// </summary>
    /// <remarks>
    /// One of <see cref="AdminSection"/> is enough — somebody who may only manage sites
    /// still needs a way in, and requiring all three would leave their one permission
    /// unreachable.
    /// </remarks>
    public static bool CanReachAdmin(bool isOwner, PracticePermissions held) =>
        isOwner || (held & AdminSection) != PracticePermissions.None;

    /// <summary>
    /// Whether this account may write a database snapshot.
    /// </summary>
    /// <remarks>
    /// Owner only, and not a grantable flag. The file is a complete unencrypted copy of
    /// every patient record the practice holds — whoever can produce one can walk out with
    /// the practice.
    /// </remarks>
    public static bool CanExportEverything(bool isOwner) => isOwner;

    /// <summary>
    /// Whether this account may change who owns the practice and who administers what.
    /// </summary>
    /// <remarks>
    /// Owner only, and deliberately not a permission. Were it grantable, whoever held it
    /// could grant themselves every other permission and then ownership, which would make
    /// the rest of this class decoration.
    /// </remarks>
    public static bool CanGrantAccess(bool isOwner) => isOwner;
}
