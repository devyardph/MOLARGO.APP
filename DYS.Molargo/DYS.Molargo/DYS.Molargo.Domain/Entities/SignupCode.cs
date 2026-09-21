namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A six-digit code proving somebody can read the address they signed up with.
/// </summary>
/// <remarks>
/// <para>
/// Keyed on the email address rather than on a provider, unlike <see cref="SignInCode"/>
/// and <see cref="PasswordResetCode"/>. At this point in signup there is no provider and no
/// clinic — that is the whole point. Nothing is created until a code comes back, so a
/// typed-in address that nobody reads produces no tenant, no staff record and no row in any
/// table a vendor later has to sort through.
/// </para>
/// <para>
/// It carries the vendor's own tenant id, because a row has to belong to somebody and the
/// clinic it might become does not exist. The same place the plans and the SMS gateways
/// live.
/// </para>
/// </remarks>
public sealed class SignupCode : EntityBase
{
    /// <summary>
    /// The address the code went to, lowercased.
    /// </summary>
    /// <remarks>
    /// Stored in the form it is matched in. Mail addresses are compared
    /// case-insensitively in practice, and a code issued to <c>Owner@Clinic.com</c> that
    /// would not verify against <c>owner@clinic.com</c> is a signup abandoned over
    /// capitalisation.
    /// </remarks>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// The verifier for the code: algorithm, iterations, salt and hash in one string.
    /// </summary>
    /// <remarks>
    /// PBKDF2, the same treatment the other two code types get. A fast hash over six digits
    /// is a million-row rainbow table anybody can build in seconds.
    /// </remarks>
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>Wrong guesses against this code so far.</summary>
    /// <remarks>
    /// Five, then the code is burned and a new one has to be sent. It is why six digits is
    /// enough: guessing is bounded at a handful of tries per issued code rather than a
    /// million.
    /// </remarks>
    public int Attempts { get; set; }

    /// <summary>When the code stops working.</summary>
    /// <remarks>
    /// Fifteen minutes, longer than the sign-in code's five and the reset's ten. Somebody
    /// mid-signup may have to go and find the password to a mailbox they rarely open, and a
    /// code that expires while they are doing it sends them back to the start of a form
    /// they have already filled in.
    /// </remarks>
    public DateTime ExpiresUtc { get; set; }

    /// <summary>When it was spent or burned, null while it is still good.</summary>
    /// <remarks>
    /// Kept rather than deleted, like the other code types. "Issued at 08:12, used at
    /// 08:14" is what says a signup was genuine, and it is the only evidence that the
    /// address on a practice was ever confirmed.
    /// </remarks>
    public DateTime? ConsumedUtc { get; set; }

    /// <summary>True while this code can still be presented.</summary>
    public bool IsLive(DateTime now) =>
        ConsumedUtc is null && !IsDeleted && ExpiresUtc > now;
}
