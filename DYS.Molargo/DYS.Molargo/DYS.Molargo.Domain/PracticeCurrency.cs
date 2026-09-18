using System.Globalization;

namespace DYS.Molargo.Domain;

/// <summary>One currency a practice can charge in.</summary>
/// <param name="Code">The ISO 4217 code stored on the practice — "AUD", "JPY".</param>
/// <param name="Name">The currency's English name, for the picker.</param>
public readonly record struct CurrencyOption(string Code, string Name)
{
    /// <summary>"AUD — Australian dollar", as the picker lists it.</summary>
    public string Label => $"{Code} — {Name}";
}

/// <summary>
/// The currency a practice charges in, and how its figures are written.
/// </summary>
/// <remarks>
/// <para>
/// A currency code, not a culture, because the code is what a claim, an export and an
/// accounting package all need — "AUD", not "en-AU". The culture is only how it is
/// rendered on screen, and several currencies share the dollar sign, so keeping the code
/// as the stored value is what stops "$" from being ambiguous.
/// </para>
/// <para>
/// Per practice, not per site. A site's fees can differ — the fee schedule already allows
/// that — but a patient's balance is summed across every invoice regardless of where the
/// work was done, so two currencies inside one practice would produce a balance that is
/// the arithmetic sum of two different kinds of money. Currency also follows the tax and
/// claiming jurisdiction, and a practice operating in two of those is really two practices.
/// </para>
/// <para>
/// The list is read out of the platform rather than typed here. A hand-kept list of world
/// currencies is a list that is wrong within a year and cannot be corrected without a
/// release — and every entry needs a locale to render in, which .NET already knows and a
/// person writing the list would have to guess.
/// </para>
/// </remarks>
public static class PracticeCurrency
{
    /// <summary>What a practice charges in unless it says otherwise.</summary>
    public const string Default = "AUD";

    /// <summary>
    /// Where several locales use one currency, the one to render it in.
    /// </summary>
    /// <remarks>
    /// Only for the currencies where the choice is visible. The euro is written
    /// "1.234,50 €" in Germany and "€1,234.50" in Ireland; the dollar is grouped one way
    /// in the United States and another in Spanish-speaking America. Picking alphabetically
    /// would settle those by accident, so the ones a practice is most likely to use are
    /// settled deliberately and the rest fall through to the rule below.
    /// </remarks>
    private static readonly Dictionary<string, string> Preferred = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AUD"] = "en-AU",
        ["NZD"] = "en-NZ",
        ["GBP"] = "en-GB",
        ["USD"] = "en-US",
        ["CAD"] = "en-CA",
        ["EUR"] = "en-IE",
        ["SGD"] = "en-SG",
        ["HKD"] = "en-HK",
        ["INR"] = "en-IN",
        ["ZAR"] = "en-ZA",
        ["CHF"] = "de-CH",
        ["JPY"] = "ja-JP",
        ["CNY"] = "zh-CN",
        ["PHP"] = "en-PH",
        ["MYR"] = "ms-MY",
        ["IDR"] = "id-ID",
        ["THB"] = "th-TH",
        ["VND"] = "vi-VN",
        ["AED"] = "ar-AE",
        ["SAR"] = "ar-SA",
        ["BRL"] = "pt-BR",
        ["MXN"] = "es-MX",
        ["KRW"] = "ko-KR",
        ["TWD"] = "zh-TW",
        ["SEK"] = "sv-SE",
        ["NOK"] = "nb-NO",
        ["DKK"] = "da-DK",
        ["PLN"] = "pl-PL",
        ["TRY"] = "tr-TR",
        ["ILS"] = "he-IL",
        ["KES"] = "en-KE",
        ["NGN"] = "en-NG",
        ["PKR"] = "en-PK",
        ["BDT"] = "bn-BD",
        ["LKR"] = "si-LK",
        ["NPR"] = "ne-NP",
        ["FJD"] = "en-FJ",
        ["PGK"] = "en-PG",
    };

    /// <summary>
    /// Every currency the platform knows, with the locale each is written in.
    /// </summary>
    /// <remarks>
    /// Built once. Enumerating the specific cultures and constructing a RegionInfo for each
    /// is not cheap, and this is read on every screen that shows a figure.
    /// </remarks>
    private static readonly Lazy<IReadOnlyDictionary<string, (string Culture, string Name)>> Catalogue =
        new(Build, isThreadSafe: true);

    private static IReadOnlyDictionary<string, (string Culture, string Name)> Build()
    {
        var found = new Dictionary<string, (string Culture, string Name)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            RegionInfo region;

            try
            {
                region = new RegionInfo(culture.Name);
            }
            catch (ArgumentException)
            {
                // Not every specific culture maps to a region — script-only names among
                // them. Skipped rather than guessed at.
                continue;
            }

            var code = region.ISOCurrencySymbol;

            if (string.IsNullOrWhiteSpace(code)) continue;

            var name = string.IsNullOrWhiteSpace(region.CurrencyEnglishName)
                ? code
                : region.CurrencyEnglishName;

            if (!found.TryGetValue(code, out var existing))
            {
                found[code] = (culture.Name, name);
                continue;
            }

            // A deliberate pick wins outright, matched on language and region so a script
            // subtag does not defeat it: ICU names Simplified Chinese "zh-Hans-CN", and an
            // exact match on "zh-CN" quietly never fired — leaving the yuan to be formatted
            // by Tibetan, which sorted earlier.
            if (Preferred.TryGetValue(code, out var wanted))
            {
                if (SameLocale(culture.Name, wanted)) found[code] = (culture.Name, name);

                continue;
            }

            if (Prefer(culture.Name, existing.Culture)) found[code] = (culture.Name, name);
        }

        // Globalization-invariant mode — a trimmed container, say — returns almost no
        // cultures, so the app would offer an empty currency list and the practice could
        // not set the one thing this type exists for. The default is seeded so there is
        // always something to choose.
        if (!found.ContainsKey(Default)) found[Default] = ("en-AU", "Australian Dollar");

        return found;
    }

    /// <summary>
    /// Which of two locales should render a currency neither map entry settles.
    /// </summary>
    /// <remarks>
    /// A two-letter language tag wins over a longer one. Picking alphabetically alone gave
    /// the Kenyan shilling to Taita ("dav-KE") over English, and minority-language locales
    /// sort early often enough that the long tail of currencies was being formatted by
    /// whichever language happened to come first in the alphabet. Length is a crude proxy
    /// for "the language most of that region writes money in", and it is right far more
    /// often than the alphabet is. Ties below that fall back to ordinal order, so the same
    /// machine always renders a currency the same way.
    /// </remarks>
    private static bool Prefer(string candidate, string current)
    {
        var candidateLanguage = LanguageLength(candidate);
        var currentLanguage = LanguageLength(current);

        if (candidateLanguage != currentLanguage) return candidateLanguage < currentLanguage;

        return string.CompareOrdinal(candidate, current) < 0;
    }

    /// <summary>
    /// Whether two culture names name the same language in the same region.
    /// </summary>
    /// <remarks>
    /// Compared on the first and last segments, so "zh-Hans-CN" and "zh-CN" are the same
    /// locale. The script in the middle changes how the language is written, not which
    /// country's money conventions apply, and the preference map is written the short way
    /// because that is how a person names a locale.
    /// </remarks>
    private static bool SameLocale(string candidate, string wanted)
    {
        if (string.Equals(candidate, wanted, StringComparison.OrdinalIgnoreCase)) return true;

        var left = candidate.Split('-');
        var right = wanted.Split('-');

        if (left.Length < 2 || right.Length < 2) return false;

        return string.Equals(left[0], right[0], StringComparison.OrdinalIgnoreCase)
            && string.Equals(left[^1], right[^1], StringComparison.OrdinalIgnoreCase);
    }

    private static int LanguageLength(string cultureName)
    {
        var dash = cultureName.IndexOf('-');

        return dash < 0 ? cultureName.Length : dash;
    }

    /// <summary>Every currency, code first, ordered by code.</summary>
    public static IReadOnlyList<CurrencyOption> Options { get; } =
        Catalogue.Value
            .Select(entry => new CurrencyOption(entry.Key.ToUpperInvariant(), entry.Value.Name))
            .OrderBy(option => option.Code, StringComparer.Ordinal)
            .ToList();

    /// <summary>The codes a practice can choose.</summary>
    public static IReadOnlyList<string> Codes { get; } =
        Options.Select(option => option.Code).ToList();

    /// <summary>The currency's name, for the picker.</summary>
    public static string NameOf(string? code) =>
        code is { Length: > 0 } && Catalogue.Value.TryGetValue(code, out var entry)
            ? entry.Name
            : code ?? Default;

    /// <summary>Whether a stored code is one the platform knows.</summary>
    public static bool IsKnown(string? code) =>
        code is { Length: > 0 } && Catalogue.Value.ContainsKey(code);

    /// <summary>
    /// What a country charges in — "PH" gives "PHP".
    /// </summary>
    /// <remarks>
    /// <para>
    /// From <see cref="RegionInfo"/> rather than a table written here. A table of two
    /// hundred countries is two hundred chances to be wrong, and it goes stale the first
    /// time a country redenominates — where the machine's own data is what every other
    /// part of this class already trusts.
    /// </para>
    /// <para>
    /// Falls back to <see cref="Default"/> rather than throwing. A practice signing up from
    /// a country whose currency this machine cannot name still gets a working clinic; what
    /// it does not get is a guess, and the currency is one field on the Settings screen.
    /// </para>
    /// </remarks>
    public static string ForCountry(string? countryCode)
    {
        if (countryCode is not { Length: 2 }) return Default;

        try
        {
            var currency = new RegionInfo(countryCode.ToUpperInvariant()).ISOCurrencySymbol;

            // Checked against the catalogue rather than returned straight out. The region
            // can name a currency this build has no locale for, and storing one would put
            // a code on the tenant that the formatter then cannot render.
            return IsKnown(currency) ? currency : Default;
        }
        catch (ArgumentException)
        {
            // Not a region this machine knows.
            return Default;
        }
    }

    /// <summary>
    /// The locale a currency's figures are written in.
    /// </summary>
    /// <remarks>
    /// Falls back to the default rather than throwing. A stored code the platform no longer
    /// knows should render as money in the practice's usual format, not take down every
    /// screen that shows a figure.
    /// </remarks>
    public static CultureInfo CultureFor(string? code)
    {
        var name = code is { Length: > 0 } && Catalogue.Value.TryGetValue(code, out var entry)
            ? entry.Culture
            : Catalogue.Value.TryGetValue(Default, out var fallback) ? fallback.Culture : null;

        if (name is null) return CultureInfo.InvariantCulture;

        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            // A locale the host OS does not carry. Invariant still formats the number
            // correctly; only the symbol is lost, which beats every figure in the app
            // throwing.
            return CultureInfo.InvariantCulture;
        }
    }
}
