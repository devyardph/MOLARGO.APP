namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One outstanding password-reset link.
/// </summary>
/// <remarks>
/// <para>
/// A row rather than a signed, stateless token. A stateless one cannot be revoked and
/// cannot be single-use: the same link would keep working until it expired, so a reset
/// email forwarded or left in a mailbox stays a working key to the account. A row can be
/// burned the moment it is spent, and that is most of the value.
/// </para>
/// <para>
/// This is the one entity in the domain written by somebody who is not signed in, which is
/// why its own rules are strict: short-lived, single-use, and rate-limited per account by
/// the service that issues it.
/// </para>
/// </remarks>
public sealed class PasswordResetToken : EntityBase
{
    /// <summary>Whose account the link resets.</summary>
    public Guid ProviderId { get; set; }

    /// <summary>
    /// SHA-256 of the token that went out in the email, hex-encoded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The token itself is never stored. A reset token is a credential for the minutes it
    /// lives — anybody holding one can take the account — so a stolen database backup
    /// should not hand over a working set of them.
    /// </para>
    /// <para>
    /// A plain SHA-256 rather than the PBKDF2 used for passwords, and deliberately: PBKDF2's
    /// cost exists to slow brute force against something guessable, and the token is 256
    /// bits of cryptographic randomness that no amount of guessing reaches. Unsalted so the
    /// lookup can be a single indexed equality — a salted hash would need every live row
    /// tried in turn.
    /// </para>
    /// </remarks>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// When the link stops working.
    /// </summary>
    /// <remarks>
    /// Short on purpose. The window only has to cover somebody walking to their inbox, and
    /// every minute past that is a minute the link is a live credential sitting in a
    /// mailbox.
    /// </remarks>
    public DateTime ExpiresUtc { get; set; }

    /// <summary>
    /// When it was spent, or null while it is still good.
    /// </summary>
    /// <remarks>
    /// Kept rather than deleted. "A reset link was used at 14:12" is the entry that
    /// explains an unexpected password change, and a deleted row explains nothing.
    /// </remarks>
    public DateTime? UsedUtc { get; set; }

    /// <summary>True while the link would still be accepted.</summary>
    public bool IsLive(DateTime utcNow) => UsedUtc is null && ExpiresUtc > utcNow;
}
