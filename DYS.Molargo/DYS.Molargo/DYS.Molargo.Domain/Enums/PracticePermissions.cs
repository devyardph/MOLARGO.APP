namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// The administrative areas a practice's owner may hand to a member of staff.
/// </summary>
/// <remarks>
/// <para>
/// A second axis, entirely separate from <see cref="ProviderRole"/>. The role says what
/// somebody is registered to do clinically; this says what they may run. They have to be
/// separate because the two overlap freely: a practice owner is usually also a dentist, and
/// folding "owner" into the role enum would have dropped them out of
/// <c>ProviderRoles.IsClinical</c> and <c>IsBookable</c> — so the principal would have
/// silently vanished from the diary, been unable to sign a note, and been unable to appear
/// on a claim as the billing provider.
/// </para>
/// <para>
/// Flags rather than a Staff/Manager/Owner ladder. A ladder forces a decision about what
/// "manager" means in general, and every practice answers it differently — the front desk
/// at one place runs the stock and never touches pricing, and the other way round down the
/// road. Ticking the two boxes that apply needs no such decision.
/// </para>
/// <para>
/// <see cref="None"/> is the default for a new account, so the restriction is what happens
/// when nobody does anything rather than something an owner has to remember to switch off.
/// </para>
/// <para>
/// Two things are deliberately NOT in here, because they are owner-only and ungrantable:
/// writing a database snapshot, which is a complete unencrypted copy of every patient
/// record, and granting these permissions in the first place. If "may grant permissions"
/// were itself grantable, anyone holding it could grant themselves the rest and then
/// ownership, and the whole scheme would be decoration.
/// </para>
/// </remarks>
[Flags]
public enum PracticePermissions
{
    /// <summary>What a new account gets. Clinical work only, nothing administrative.</summary>
    None = 0,

    /// <summary>
    /// Admin → Users and Roles: adding staff, changing their details, setting passwords.
    /// </summary>
    /// <remarks>
    /// The most powerful of these by some distance. Anyone who can set another person's
    /// password can sign in as them, so this is closer to ownership than the rest and is
    /// worth granting last.
    /// </remarks>
    ManageStaff = 1,

    /// <summary>
    /// The item catalogue's editor: fees, per-site prices, adding and withdrawing items.
    /// </summary>
    /// <remarks>
    /// Editing only. Reading the catalogue is not gated at all — the front desk quotes
    /// from it every day, and a price list they cannot see is a practice that cannot take
    /// a booking.
    /// </remarks>
    ManagePricing = 2,

    /// <summary>Admin → Sites: locations, their details, and the diary's chairs.</summary>
    ManageSites = 4,

    /// <summary>
    /// Stock cost prices, suppliers and purchase orders.
    /// </summary>
    /// <remarks>
    /// Not receiving stock or marking it used, which is front-desk and surgery work and
    /// stays open. What this gates is the money side: what an item costs and who it is
    /// bought from.
    /// </remarks>
    ManageInventory = 8,

    /// <summary>
    /// Admin → Settings: the mail account, notification rules and the printers.
    /// </summary>
    /// <remarks>
    /// Gated mainly for the mail credentials. The app password for the practice's sending
    /// account is stored in plain text in the database, so this screen hands over the
    /// ability to send mail as the practice.
    /// </remarks>
    ManageSettings = 16,
}
