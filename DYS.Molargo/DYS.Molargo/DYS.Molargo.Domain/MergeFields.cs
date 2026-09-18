using System.Text;

namespace DYS.Molargo.Domain;

/// <summary>
/// The placeholders a message template may carry, and how they are substituted.
/// </summary>
/// <remarks>
/// <para>
/// In Domain rather than beside the comms screen because two things have to agree about
/// this vocabulary: the editor that offers the tokens, and whatever eventually renders a
/// message for sending. A token the editor offers and the sender does not know goes out
/// to a patient as literal braces.
/// </para>
/// <para>
/// The design shows the tokens as <c>[First name]</c>. Stored as
/// <c>{{PatientFirstName}}</c>, matching what <c>MessageTemplate.Body</c> already
/// documents: square brackets appear in ordinary prose — "[see reverse]" — and a
/// substituting renderer would eat them.
/// </para>
/// </remarks>
public static class MergeFields
{
    /// <summary>One placeholder: its token, the label staff see, and an example.</summary>
    public sealed record Field(string Token, string Label, string Example);

    /// <summary>
    /// The tokens the editor offers, in the design's order.
    /// </summary>
    /// <remarks>
    /// A closed list. Free-form tokens would let a template reference something no sender
    /// can resolve, and the failure surfaces as a half-empty message already delivered.
    /// </remarks>
    public static readonly Field[] All =
    [
        new("PatientFirstName", "First name", "Margaret"),
        new("AppointmentDate", "Appt date", "Wed 16 Sep"),
        new("AppointmentTime", "Appt time", "8:45 AM"),
        new("ProviderName", "Provider", "Dr Vance"),
        new("Procedure", "Procedure", "Crown fit"),
        new("BookingLink", "Booking link", "molargo.example/book"),
        new("PracticeName", "Practice", "Molargo Dental — Sydney CBD"),
        new("PracticePhone", "Practice phone", "(02) 9000 1200"),
        new("AmountOwing", "Amount owing", "$340.00"),
    ];

    /// <summary>
    /// The tokens a staff notice may carry, kept apart from the patient catalogue.
    /// </summary>
    /// <remarks>
    /// A second list rather than more entries in <see cref="All"/>. The patient editor
    /// offers All and a patient-facing renderer resolves it; a staff token loose in that
    /// catalogue could be dropped into an appointment reminder, where nothing can resolve
    /// it and the patient receives literal braces.
    ///
    /// Both lists are accepted by <see cref="Find"/> and therefore by
    /// <see cref="UnknownTokensIn"/>: validation should know every token the app can
    /// resolve, even where a given editor does not offer all of them.
    /// </remarks>
    public static readonly Field[] StaffAll =
    [
        new("StaffName", "Staff name", "Cathy Brennan"),
        new("StaffUsername", "Username", "cbrennan"),
        new("ClinicCode", "Clinic code", "molargo-dental"),
        new("ResetBy", "Reset by", "Dr Vance"),
        new("ResetAt", "Reset at", "11 Sep 2026, 14:20"),
        new("ResetCode", "Reset code", "418302"),

        // Separate from ResetCode, though both are six digits. A template that resolved
        // either would let the sign-in wording be pasted into the reset email and still
        // render — and the two say opposite things about what to do if it was not you.
        new("SignInCode", "Sign-in code", "418302"),
        new("CodeExpiry", "Code expires in", "10 minutes"),
        new("PracticeName", "Practice", "Molargo Dental Group"),
        new("PracticePhone", "Practice phone", "(02) 9000 1200"),
    ];

    /// <summary>
    /// The catalogue an editor should offer for a template of this purpose.
    /// </summary>
    /// <remarks>
    /// A staff notice and a patient message have almost no tokens in common, and offering
    /// the wrong set is how a template ends up referencing something its sender cannot
    /// resolve.
    /// </remarks>
    public static Field[] For(Enums.MessagePurpose purpose) =>
        purpose == Enums.MessagePurpose.SecurityNotice ? StaffAll : All;

    /// <summary>The token as it is written into a body.</summary>
    public static string Placeholder(string token) => "{{" + token + "}}";

    public static Field? Find(string token) =>
        All.FirstOrDefault(field => string.Equals(field.Token, token, StringComparison.Ordinal))
        ?? StaffAll.FirstOrDefault(field =>
            string.Equals(field.Token, token, StringComparison.Ordinal));

    /// <summary>
    /// Every token appearing in <paramref name="body"/>, in the order found.
    /// </summary>
    /// <remarks>
    /// Hand-scanned rather than a regular expression: this assembly is compiled for
    /// WebAssembly as well, where a compiled Regex is a startup cost paid for something a
    /// single pass over a few hundred characters does.
    /// </remarks>
    public static IReadOnlyList<string> TokensIn(string? body)
    {
        if (string.IsNullOrEmpty(body)) return [];

        var found = new List<string>();
        var index = 0;

        while (index < body.Length - 3)
        {
            var open = body.IndexOf("{{", index, StringComparison.Ordinal);
            if (open < 0) break;

            var close = body.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0) break;

            var token = body[(open + 2)..close].Trim();

            if (token.Length > 0 && !found.Contains(token, StringComparer.Ordinal))
            {
                found.Add(token);
            }

            index = close + 2;
        }

        return found;
    }

    /// <summary>
    /// Tokens in the body that this catalogue does not know. A template carrying one can
    /// never render, whatever data it is given.
    /// </summary>
    public static IReadOnlyList<string> UnknownTokensIn(string? body) =>
        TokensIn(body).Where(token => Find(token) is null).ToList();

    /// <summary>
    /// The result of substituting into a body.
    /// </summary>
    /// <param name="Text">The rendered text.</param>
    /// <param name="Unresolved">
    /// Tokens the supplied values had nothing for — a reminder token on a patient with no
    /// booking, say. Named rather than swallowed: a body rendered with the gaps left blank
    /// reads "your appointment on  at  with" and is worse than not sending.
    /// </param>
    public sealed record Rendered(string Text, IReadOnlyList<string> Unresolved)
    {
        public bool IsComplete => Unresolved.Count == 0;
    }

    /// <summary>
    /// Substitutes <paramref name="values"/> into <paramref name="body"/>.
    /// </summary>
    /// <remarks>
    /// An unresolved token is left in place as its own placeholder rather than blanked, so
    /// a preview shows exactly where the hole is. Nothing is sent from a preview, and the
    /// caller has <see cref="Rendered.Unresolved"/> to refuse on.
    /// </remarks>
    public static Rendered Render(string? body, IReadOnlyDictionary<string, string?> values)
    {
        if (string.IsNullOrEmpty(body)) return new Rendered(string.Empty, []);

        var unresolved = new List<string>();
        var text = new StringBuilder(body.Length);
        var index = 0;

        while (index < body.Length)
        {
            var open = body.IndexOf("{{", index, StringComparison.Ordinal);

            if (open < 0)
            {
                text.Append(body, index, body.Length - index);
                break;
            }

            var close = body.IndexOf("}}", open + 2, StringComparison.Ordinal);

            if (close < 0)
            {
                // Unterminated braces are literal text, not a broken token. A body that
                // genuinely contains "{{" reads as itself rather than losing its tail.
                text.Append(body, index, body.Length - index);
                break;
            }

            text.Append(body, index, open - index);

            var token = body[(open + 2)..close].Trim();

            if (values.TryGetValue(token, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                text.Append(value);
            }
            else
            {
                text.Append(Placeholder(token));

                if (!unresolved.Contains(token, StringComparer.Ordinal))
                {
                    unresolved.Add(token);
                }
            }

            index = close + 2;
        }

        return new Rendered(text.ToString(), unresolved);
    }
}
