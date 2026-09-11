using System.Security.Cryptography;
using System.Text;

namespace DYS.Molargo.Shared.Services;

/// <summary>
/// Turns a password into a verifier, and checks one against it.
/// </summary>
/// <remarks>
/// <para>
/// PBKDF2 over HMAC-SHA-256, from the framework. Not a bare SHA-256: a fast hash lets an
/// attacker with the database file try billions of candidates a second, and a stolen
/// SQLite file is exactly the threat this app has — it sits unencrypted in a folder, as the
/// Admin screen's Data tab says.
/// </para>
/// <para>
/// Argon2id would be the better choice and is not in the framework. PBKDF2 with a high
/// iteration count is the strongest thing available without taking a dependency, and the
/// stored format carries its parameters so a move to something better can verify old
/// hashes and upgrade them on next sign-in.
/// </para>
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>Produces a verifier to store against the user.</summary>
    string Hash(string password);

    /// <summary>
    /// Checks a password against a stored verifier.
    /// </summary>
    /// <remarks>
    /// Returns false rather than throwing on a malformed or unknown-format verifier. A
    /// corrupt row must fail closed — throwing would surface as an error page that
    /// distinguishes a broken account from a wrong password, which is a hint worth denying.
    /// </remarks>
    bool Verify(string password, string? storedHash);
}

/// <inheritdoc cref="IPasswordHasher"/>
public sealed class PasswordHasher : IPasswordHasher
{
    /// <summary>
    /// Iterations for new hashes.
    /// </summary>
    /// <remarks>
    /// Chosen for the slowest device this runs on rather than the fastest. It is a tablet
    /// doing one sign-in, so a couple of hundred milliseconds is invisible to the user and
    /// multiplies an offline attacker's cost by the same factor.
    /// </remarks>
    private const int Iterations = 210_000;

    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>Identifies the scheme, so a future one can be told apart.</summary>
    private const string Scheme = "pbkdf2-sha256";

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);

        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashBytes);

        // Parameters stored beside the hash, so raising the iteration count later does not
        // invalidate every existing password.
        return string.Join('$',
            Scheme,
            Iterations.ToString(),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool Verify(string password, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        var parts = storedHash.Split('$');

        if (parts.Length != 4
            || !string.Equals(parts[0], Scheme, StringComparison.Ordinal)
            || !int.TryParse(parts[1], out var iterations)
            || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expected.Length);

        // Constant-time. A byte-by-byte comparison that returns early leaks how much of a
        // candidate hash was right through timing, which is enough to reconstruct it.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
