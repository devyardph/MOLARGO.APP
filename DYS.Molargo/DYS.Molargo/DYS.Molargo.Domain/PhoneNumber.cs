namespace DYS.Molargo.Domain;

/// <summary>
/// Turns the mobile number on a patient's record into the form a carrier will accept.
/// </summary>
/// <remarks>
/// <para>
/// A front desk types a number the way the patient says it — <c>0400 123 456</c>,
/// <c>(02) 9000 1200</c>, <c>0917-555-0134</c> — and every one of those is a national
/// format that means nothing to an international SMS gateway. Sent as typed, the message
/// is either refused or, worse, delivered to whoever holds that number in the carrier's
/// own country. So the number is converted here, once, rather than at each call site.
/// </para>
/// <para>
/// Not a general phone-number library. It does the one conversion an SMS send needs and
/// refuses anything it is not sure about, because a number this cannot place is a message
/// that should not be sent rather than one sent hopefully.
/// </para>
/// </remarks>
public static class PhoneNumber
{
    /// <summary>
    /// International dialling codes, by ISO country.
    /// </summary>
    /// <remarks>
    /// Held here rather than derived: .NET's <c>RegionInfo</c> carries currencies and
    /// names but no dialling code, and there is no framework source for one.
    ///
    /// A country missing from this table is not a silent pass-through — see
    /// <see cref="ToInternational"/>. Add the country here when a gateway is set up for
    /// it, which is the same moment somebody would notice.
    /// </remarks>
    private static readonly Dictionary<string, string> DiallingCodes = new(StringComparer.Ordinal)
    {
        ["AE"] = "971", ["AR"] = "54", ["AT"] = "43", ["AU"] = "61", ["BD"] = "880",
        ["BE"] = "32", ["BG"] = "359", ["BH"] = "973", ["BR"] = "55", ["CA"] = "1",
        ["CH"] = "41", ["CL"] = "56", ["CN"] = "86", ["CO"] = "57", ["CY"] = "357",
        ["CZ"] = "420", ["DE"] = "49", ["DK"] = "45", ["EE"] = "372", ["EG"] = "20",
        ["ES"] = "34", ["FI"] = "358", ["FJ"] = "679", ["FR"] = "33", ["GB"] = "44",
        ["GR"] = "30", ["HK"] = "852", ["HR"] = "385", ["HU"] = "36", ["ID"] = "62",
        ["IE"] = "353", ["IL"] = "972", ["IN"] = "91", ["IS"] = "354", ["IT"] = "39",
        ["JP"] = "81", ["KE"] = "254", ["KR"] = "82", ["KW"] = "965", ["LK"] = "94",
        ["LT"] = "370", ["LU"] = "352", ["LV"] = "371", ["MA"] = "212", ["MT"] = "356",
        ["MX"] = "52", ["MY"] = "60", ["NG"] = "234", ["NL"] = "31", ["NO"] = "47",
        ["NZ"] = "64", ["OM"] = "968", ["PE"] = "51", ["PG"] = "675", ["PH"] = "63",
        ["PK"] = "92", ["PL"] = "48", ["PT"] = "351", ["QA"] = "974", ["RO"] = "40",
        ["RS"] = "381", ["SA"] = "966", ["SE"] = "46", ["SG"] = "65", ["SI"] = "386",
        ["SK"] = "421", ["TH"] = "66", ["TR"] = "90", ["TW"] = "886", ["UA"] = "380",
        ["US"] = "1", ["VN"] = "84", ["ZA"] = "27",
    };

    /// <summary>What came of reading a number.</summary>
    /// <param name="Number">The number in E.164 form, or null where it could not be read.</param>
    /// <param name="Problem">Why not, in words a receptionist can act on.</param>
    public readonly record struct Normalised(string? Number, string? Problem)
    {
        public bool Ok => Number is not null;
    }

    /// <summary>
    /// Converts a number to E.164 — a leading <c>+</c>, the country code, then the line.
    /// </summary>
    /// <param name="typed">Whatever is on the patient's record.</param>
    /// <param name="countryCode">
    /// The two-letter country of the gateway that will carry the message, not of the
    /// patient. They are the same in every ordinary case, and where they differ it is the
    /// carrier that decides how a national number is read.
    /// </param>
    public static Normalised ToInternational(string? typed, string? countryCode)
    {
        var digits = new string((typed ?? string.Empty)
            .Where(character => char.IsAsciiDigit(character) || character == '+')
            .ToArray());

        if (digits.Length == 0)
        {
            return new Normalised(null, "There is no mobile number on this record.");
        }

        // Already international. Taken as given rather than re-derived — somebody who
        // typed the + knew which country they meant, and a patient abroad is exactly the
        // case where the practice's own country is the wrong answer.
        if (digits.StartsWith('+'))
        {
            var international = "+" + new string(digits.Where(char.IsAsciiDigit).ToArray());

            return international.Length is >= 8 and <= 16
                ? new Normalised(international, null)
                : new Normalised(null, $"\"{typed}\" is not a whole mobile number.");
        }

        // 0011, 011, 00 — how a national line dials out. Read as international rather than
        // treated as a trunk prefix: stripping the leading zero of 0011 61 400… would
        // produce a number in no country at all.
        foreach (var exitCode in new[] { "0011", "011", "00" })
        {
            if (digits.StartsWith(exitCode, StringComparison.Ordinal)
                && digits.Length > exitCode.Length + 6)
            {
                return new Normalised("+" + digits[exitCode.Length..], null);
            }
        }

        var country = (countryCode ?? string.Empty).Trim().ToUpperInvariant();

        if (!DiallingCodes.TryGetValue(country, out var dialling))
        {
            // Refused, not sent hopefully. A national number handed to an international
            // gateway is delivered to whoever holds it in the carrier's own country, and a
            // reminder arriving at a stranger's phone is worse than one that never went.
            return new Normalised(null,
                $"\"{typed}\" is a local number and there is no dialling code on file for "
                    + $"{(country.Length == 2 ? country : "that country")}. Store it with "
                    + "its country code — +63917 555 0134 — or add the country to the list.");
        }

        // The trunk prefix. Nearly every country writes a national number with a leading 0
        // that is dropped when the country code goes on the front, and the exceptions —
        // Italy keeps its zero — are not countries this app has gateways for. Worth
        // revisiting if one is added rather than guessing at it now.
        var line = digits.TrimStart('0');

        if (line.Length < 6)
        {
            return new Normalised(null, $"\"{typed}\" is too short to be a mobile number.");
        }

        return new Normalised("+" + dialling + line, null);
    }
}
