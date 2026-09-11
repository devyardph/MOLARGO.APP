using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Features.Patient.Services;

/// <summary>
/// An existing patient that looks like the one being entered.
/// </summary>
/// <param name="Score">
/// Confidence, 0-100. Shown to the user as a percentage, so it has to be explainable —
/// see <see cref="DuplicateMatcher"/> for the weights and why they are what they are.
/// </param>
/// <param name="MatchedOn">
/// Which fields agreed, for the "Name + DOB + mobile match" line. Naming them is what
/// makes the score reviewable rather than a number the user has to trust.
/// </param>
public sealed record DuplicateCandidate(
    PatientEntity Patient,
    int Score,
    IReadOnlyList<string> MatchedOn);

/// <summary>
/// Scores how likely a patient being entered is a duplicate of one already on file.
/// </summary>
/// <remarks>
/// <para>
/// Duplicate patient records are the single most expensive data problem a practice has:
/// clinical history splits across two records, one of them has the allergies, and the
/// balance is on the other. Catching it at entry is worth a false positive or two.
/// </para>
/// <para>
/// Deliberately simple and explainable rather than fuzzy. A practice will override this
/// judgement — "no, they really are two different Margaret Yuens" — and a score they can
/// see the reasoning for is one they can argue with. A trained similarity model would
/// score better and be impossible to dispute at the front desk.
/// </para>
/// </remarks>
public static class DuplicateMatcher
{
    /// <summary>
    /// Below this, the candidate is not worth interrupting for. Tuned so that a shared
    /// surname alone never trips it — a household of five would otherwise flag every new
    /// member — while name plus any second identifier does.
    /// </summary>
    public const int ReportThreshold = 60;

    // Weights sum to 100. Date of birth carries the most because it is the one field a
    // patient almost never gets wrong and a coincidence almost never matches; a surname
    // carries least because households and common names share them constantly.
    private const int DateOfBirthWeight = 40;
    private const int MobileWeight = 30;
    private const int FirstNameWeight = 20;
    private const int LastNameWeight = 10;

    /// <summary>
    /// Scores <paramref name="candidates"/> against the details being entered and returns
    /// those worth showing, best first.
    /// </summary>
    public static IReadOnlyList<DuplicateCandidate> Match(
        string firstName,
        string lastName,
        DateOnly? dateOfBirth,
        string? mobile,
        IEnumerable<PatientEntity> candidates,
        Guid? excludingId = null)
    {
        var results = new List<DuplicateCandidate>();

        foreach (var candidate in candidates)
        {
            // Editing a patient always "matches" itself.
            if (excludingId is { } id && candidate.Id == id) continue;

            var score = 0;
            var matched = new List<string>();

            if (NameEquals(candidate.LastName, lastName))
            {
                score += LastNameWeight;
                matched.Add("surname");
            }

            if (NameEquals(candidate.FirstName, firstName))
            {
                score += FirstNameWeight;
                matched.Add("first name");
            }

            // Only counts when both sides have one. A missing date of birth on either
            // record is not agreement, and treating two blanks as a match made every
            // lead with no DOB look like a duplicate of every other.
            if (dateOfBirth is { } dob && candidate.DateOfBirth == dob)
            {
                score += DateOfBirthWeight;
                matched.Add("date of birth");
            }

            if (MobileEquals(candidate.Mobile, mobile))
            {
                score += MobileWeight;
                matched.Add("mobile");
            }

            if (score >= ReportThreshold)
            {
                results.Add(new DuplicateCandidate(candidate, score, matched));
            }
        }

        return results
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Patient.LastName)
            .ToList();
    }

    private static bool NameEquals(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Compares mobile numbers on their digits alone.
    /// </summary>
    /// <remarks>
    /// "0412 883 021", "0412883021" and "+61 412 883 021" are one number typed three
    /// ways, and a string comparison calls them three different people. The leading
    /// country code is dropped to a common form by comparing the last nine digits, which
    /// is what an Australian mobile has after the trunk zero.
    /// </remarks>
    private static bool MobileEquals(string? left, string? right)
    {
        var a = Digits(left);
        var b = Digits(right);

        if (a.Length < 9 || b.Length < 9) return false;

        return a[^9..] == b[^9..];
    }

    private static string Digits(string? value) =>
        value is null ? string.Empty : new string(value.Where(char.IsDigit).ToArray());
}
