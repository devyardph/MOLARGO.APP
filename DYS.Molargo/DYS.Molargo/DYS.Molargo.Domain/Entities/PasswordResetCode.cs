namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One outstanding password-reset code.
/// </summary>
/// <remarks>
/// <para>
/// A code rather than a link, and the reason is this app's shape rather than fashion. There
/// is no server: every head keeps its own SQLite file, so anything issued on a tablet exists
/// only in that tablet's database. A link invites the recipient to open it wherever their
/// mail happens to be, which is usually somewhere else — and there it is indistinguishable
/// from one somebody invented. A code makes them come back to the install that issued it,
/// which is the only place that can honour it. That is what makes this work identically on
/// iOS, Android, Windows and the web, with no deep linking anywhere.
/// </para>
/// <para>
/// The cost is entropy. Six digits is a million possibilities against a link token's 2^256,
/// so this row carries an attempt count and a short life where the token needed neither.
/// Those two are what keep it from being weaker than what it replaced — see
/// <see cref="Attempts"/>.
/// </para>
/// </remarks>
public sealed class PasswordResetCode : EntityBase
{
    /// <summary>Whose account the code resets.</summary>
    public Guid ProviderId { get; set; }

    /// <summary>
    /// The verifier for the code: algorithm, iterations, salt and hash in one string.
    /// </summary>
    /// <remarks>
    /// The same PBKDF2 treatment a password gets, not the plain SHA-256 a link token would
    /// have taken. A fast unsalted hash is fine over 256 bits of randomness and useless over
    /// six digits: a million preimages is a table anybody can build in seconds, so a stolen
    /// database would give up every outstanding code at once.
    ///
    /// Which also means no lookup by hash. The account is found first, from the clinic code
    /// and username the person re-enters, and the code is then verified against that
    /// account's live rows.
    /// </remarks>
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>
    /// Wrong guesses against this code so far.
    /// </summary>
    /// <remarks>
    /// The whole reason six digits is safe enough. Five wrong attempts burn every live code
    /// on the account, so an attacker gets roughly fifteen guesses per request cycle against
    /// a million — and only after producing a clinic code and username that are already
    /// correct. Without this the code would be walked in an afternoon.
    /// </remarks>
    public int Attempts { get; set; }

    /// <summary>
    /// When the code stops working.
    /// </summary>
    /// <remarks>
    /// Ten minutes, where a link token had thirty. It only has to cover walking to an inbox
    /// and back to the app, and a shorter window is the cheapest way to cut how many guesses
    /// an attacker can fit in.
    /// </remarks>
    public DateTime ExpiresUtc { get; set; }

    /// <summary>
    /// When it was spent or burned, null while it is still good.
    /// </summary>
    /// <remarks>
    /// Kept rather than deleted. "A reset code was used at 14:12" is what explains an
    /// unexpected password change, and a deleted row explains nothing.
    /// </remarks>
    public DateTime? UsedUtc { get; set; }

    /// <summary>True while the code would still be accepted.</summary>
    public bool IsLive(DateTime utcNow, int attemptLimit) =>
        UsedUtc is null && Attempts < attemptLimit && ExpiresUtc > utcNow;
}
