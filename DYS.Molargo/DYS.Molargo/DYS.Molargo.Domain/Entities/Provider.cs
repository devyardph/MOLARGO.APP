using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// Someone who works at the practice — clinical or not. One entity for both, because the
/// diary, the audit log and the roster all need to name a person, and splitting clinicians
/// from front desk means every one of those needs two foreign keys.
/// </summary>
public sealed class Provider : EntityBase
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    /// <summary>How they are addressed in the diary and on letters — "Dr R. Vance".</summary>
    public string? DisplayName { get; set; }

    public ProviderRole Role { get; set; } = ProviderRole.Dentist;

    /// <summary>
    /// Whether this person owns the practice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Its own field rather than a value of <see cref="Role"/>, because the two are
    /// independent and usually both true of the same person: the owner of a practice is
    /// normally also one of its dentists. Folding it into the role would have forced a
    /// choice between the two, and choosing "owner" would have dropped them out of
    /// <c>ProviderRoles.IsClinical</c> and <c>IsBookable</c> — no diary column, no signed
    /// notes, and no provider number on a claim, with no error to say why.
    /// </para>
    /// <para>
    /// Set on the account that creates the practice at signup. More than one is normal and
    /// supported: dental practices are frequently partnerships, and selling one means
    /// adding an owner before removing the previous one.
    /// </para>
    /// <para>
    /// An owner implicitly holds every permission — see
    /// <see cref="DYS.Molargo.Domain.PracticeAccess.Allows"/> — rather than carrying them
    /// as stored flags, so no click can leave a practice with an owner locked out of its
    /// own settings.
    /// </para>
    /// </remarks>
    public bool IsOwner { get; set; }

    /// <summary>
    /// The administrative areas this person may run, granted by an owner.
    /// </summary>
    /// <remarks>
    /// <see cref="PracticePermissions.None"/> by default, so a new account is clinical-only
    /// until somebody deliberately says otherwise. Meaningless while
    /// <see cref="IsOwner"/> is true, which grants everything regardless — stored anyway so
    /// that demoting an owner leaves whatever was explicitly ticked for them rather than
    /// silently stripping them to nothing.
    /// </remarks>
    public PracticePermissions Permissions { get; set; } = PracticePermissions.None;

    /// <summary>
    /// Medicare provider number, required on the claim for any item they bill. Absent for
    /// a non-clinical role, which is why it is nullable rather than required.
    /// </summary>
    public string? ProviderNumber { get; set; }

    /// <summary>AHPRA registration number, checked at credentialing and on audit.</summary>
    public string? AhpraNumber { get; set; }

    public string? Email { get; set; }
    public string? Mobile { get; set; }

    /// <summary>Their usual site. They may still be rostered elsewhere.</summary>
    public Guid? PrimaryLocationId { get; set; }

    // ---- availability ----------------------------------------------------

    /// <summary>
    /// The days of the week they work. <c>None</c> means nobody has said.
    /// </summary>
    /// <remarks>
    /// A weekly pattern, not a roster — see <c>ProviderAvailability</c> for what it can
    /// and cannot express. It lives on the provider rather than in a shift table because
    /// the question the booking form asks is "is this person in on Tuesday", and every
    /// practice can answer that long before it has the appetite to maintain shifts.
    /// </remarks>
    public WorkingDays WorkingDays { get; set; } = WorkingDays.None;

    /// <summary>
    /// When their day starts, where it differs from the practice's own opening time.
    /// </summary>
    /// <remarks>
    /// Nullable, and null means "whenever the practice opens" rather than midnight. A
    /// default of 00:00 would have read as a clinician available from midnight, which is
    /// both wrong and the sort of wrong that only shows up in a booking nobody can honour.
    /// </remarks>
    public TimeOnly? WorkingFrom { get; set; }

    /// <summary>When their day finishes. Null means whenever the practice closes.</summary>
    public TimeOnly? WorkingTo { get; set; }

    /// <summary>
    /// Diary colour, as a CSS hex value. Chosen per provider so a shared diary is readable
    /// at a glance; stored rather than derived from a palette by index, because staff
    /// object strongly to their colour moving when someone new is added.
    /// </summary>
    public string? DiaryColour { get; set; }

    /// <summary>A departed provider keeps their history but disappears from the diary.</summary>
    /// <summary>
    /// When the AHPRA registration lapses.
    /// </summary>
    /// <remarks>
    /// Held because an expired registration is not a reminder, it is a stop: the clinician
    /// may not practise, and anything they sign after it is worthless. The number alone
    /// cannot say that — a registration that has run out looks identical to a current one.
    /// </remarks>
    public DateOnly? AhpraExpiresOn { get; set; }

    // ---- sign-in ---------------------------------------------------------

    /// <summary>
    /// What they type to sign in, unique within the clinic.
    /// </summary>
    /// <remarks>
    /// Not the email, which is the design's choice. A username scoped to the tenant lets
    /// two clinics both have a "rvance", and lets a clinic issue a login to someone whose
    /// email it does not hold — a locum, or a nurse sharing a practice address.
    /// </remarks>
    public string? Username { get; set; }

    /// <summary>
    /// The verifier for their password: algorithm, iterations, salt and hash in one string.
    /// </summary>
    /// <remarks>
    /// Never the password, and never a bare digest. The parameters travel with the hash so
    /// the iteration count can be raised later without invalidating everyone's password —
    /// an old hash still verifies against the count it was made with.
    /// </remarks>
    public string? PasswordHash { get; set; }

    public DateTime? PasswordUpdatedUtc { get; set; }

    public DateTime? LastSignInUtc { get; set; }

    /// <summary>
    /// Consecutive failures since the last success.
    /// </summary>
    /// <remarks>
    /// Counted so repeated guessing can be slowed. Reset on a successful sign-in rather
    /// than on a timer, so an attacker cannot wait out the counter between attempts.
    /// </remarks>
    public int FailedSignInCount { get; set; }

    /// <summary>Locked out until this moment, after too many failures.</summary>
    public DateTime? LockedUntilUtc { get; set; }

    /// <summary>True where the account can be signed in to at all.</summary>
    public bool CanSignIn =>
        IsActive && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(PasswordHash);

    public bool IsActive { get; set; } = true;

    public string FullName => (DisplayName ?? $"{FirstName} {LastName}").Trim();
}
