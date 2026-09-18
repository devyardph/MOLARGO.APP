using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain;

/// <summary>
/// Which roles may act clinically.
/// </summary>
/// <remarks>
/// In Domain because three separate places need the same answer and got it differently:
/// the booking form filtered its provider list one way, the session's stand-in sign-in did
/// not filter at all, and the certificate guard checked for a licence number instead. The
/// consequence of the middle one was that every clinical author in the app — signed notes,
/// prescriptions, referral letters — was the practice receptionist, because "Brennan"
/// sorts before "Vance".
/// </remarks>
public static class ProviderRoles
{
    /// <summary>
    /// Whether someone in this role may author clinical records — notes, findings,
    /// prescriptions, referrals and certificates.
    /// </summary>
    /// <remarks>
    /// A dental assistant is excluded despite working chairside: they assist with
    /// treatment but do not diagnose, prescribe or certify, and the record has to name
    /// whoever is answerable for the decision.
    /// </remarks>
    public static bool IsClinical(ProviderRole role) => role is
        ProviderRole.Dentist
        or ProviderRole.Hygienist
        or ProviderRole.OralHealthTherapist
        or ProviderRole.DentalTherapist
        or ProviderRole.Prosthetist
        or ProviderRole.Specialist;

    /// <summary>Whether the role may be booked into a chair in the diary.</summary>
    public static bool IsBookable(ProviderRole role) => IsClinical(role);

    /// <summary>
    /// Whether the role belongs to the vendor rather than to a clinic.
    /// </summary>
    /// <remarks>
    /// Both of the above already exclude it, because they are whitelists — a super admin
    /// cannot author a clinical note or be booked into a chair, which is right. This is
    /// the positive form, for the one thing the role does grant.
    /// </remarks>
    public static bool IsPlatform(ProviderRole role) => role is ProviderRole.SuperAdmin;

    /// <summary>
    /// The roles a clinic may assign to its own staff.
    /// </summary>
    /// <remarks>
    /// Every value except <see cref="ProviderRole.SuperAdmin"/>. The practice's Users
    /// screen reads this rather than <c>Enum.GetValues</c>: offering the vendor's own role
    /// in a clinic's role picker would suggest a practice can promote itself to
    /// administering every other practice.
    /// </remarks>
    public static readonly ProviderRole[] Assignable = Enum
        .GetValues<ProviderRole>()
        .Where(role => !IsPlatform(role))
        .ToArray();
}
