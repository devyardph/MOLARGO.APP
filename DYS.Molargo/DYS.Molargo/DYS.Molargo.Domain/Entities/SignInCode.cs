namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One outstanding sign-in code, for a staff member who has two-step sign-in switched on.
/// </summary>
/// <remarks>
/// <para>
/// Its own table rather than a purpose column on <see cref="PasswordResetCode"/>. The two
/// look identical and are not: a reset code proves somebody controls the mailbox and is
/// therefore allowed to replace the password, while this one is the second half of a
/// credential the person has already produced. Sharing a table means one query away from a
/// reset code being accepted as a login — which would turn "I can read your email" into "I
/// am you", silently, with no password involved.
/// </para>
/// <para>
/// Six digits, emailed, with the same three defences the reset code carries: a short life,
/// a hard attempt limit shared across every live code on the account, and single use. That
/// is what makes a million-possibility secret safe enough to send to an inbox.
/// </para>
/// <para>
/// Email rather than an authenticator app, because it needs nothing enrolled and nothing
/// installed — and because the practice's mail account is already configured for recalls
/// and reset codes. The honest cost is that it is only as strong as the mailbox: anybody
/// reading a clinician's email can complete a sign-in they already have the password for.
/// It defends the stolen or guessed password, which is the case that actually happens, not
/// a targeted attacker with mailbox access.
/// </para>
/// </remarks>
public sealed class SignInCode : EntityBase
{
    /// <summary>Whose sign-in the code completes.</summary>
    public Guid ProviderId { get; set; }

    /// <summary>
    /// The verifier for the code: algorithm, iterations, salt and hash in one string.
    /// </summary>
    /// <remarks>
    /// PBKDF2, the same treatment a password gets. A fast hash over six digits is a
    /// million-row rainbow table anybody can build in seconds, so a copied database file
    /// would hand over every outstanding code at once — and this database sits on a tablet
    /// in a surgery.
    /// </remarks>
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>
    /// Wrong guesses against this code so far.
    /// </summary>
    /// <remarks>
    /// The reason six digits is enough. Five wrong attempts burn every live code on the
    /// account, so guessing is bounded at a handful of tries per issued code rather than a
    /// million — and each fresh code costs the attacker another correct password.
    /// </remarks>
    public int Attempts { get; set; }

    /// <summary>When the code stops working.</summary>
    /// <remarks>
    /// Five minutes, half the reset code's ten. This one only has to cover glancing at a
    /// phone while standing at the machine, where a reset involves reading an email,
    /// choosing a new password and typing it twice.
    /// </remarks>
    public DateTime ExpiresUtc { get; set; }

    /// <summary>
    /// When it was spent or burned, null while it is still good.
    /// </summary>
    /// <remarks>
    /// Kept rather than deleted. "A code was issued at 08:12 and used at 08:13" is what
    /// distinguishes a normal morning from somebody else signing in as this person, and a
    /// deleted row distinguishes nothing.
    /// </remarks>
    public DateTime? UsedUtc { get; set; }

    /// <summary>True while the code would still be accepted.</summary>
    public bool IsLive(DateTime utcNow, int attemptLimit) =>
        UsedUtc is null && Attempts < attemptLimit && ExpiresUtc > utcNow;
}
