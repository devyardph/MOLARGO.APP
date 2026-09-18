using System.Globalization;
using DYS.Molargo.Domain;

namespace DYS.Molargo.Shared.Components;

/// <summary>
/// How the app renders dates, times and money.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is pinned to an explicit culture rather than left to the host's. The
/// same record has to read identically on a surgery tablet, the front-desk PC and the web
/// head, and none of those has a locale this app controls — a machine configured as en-US
/// renders 03/04/1968 as March, and a clinician reading it as April has misread a date of
/// birth. Fixing the culture also stops a balance appearing in the wrong currency symbol.
/// </para>
/// <para>
/// Dates use a three-letter month, never a numeric one, for the same reason: "9 Mar 1996"
/// cannot be misread, where 09/03/1996 can.
/// </para>
/// </remarks>
public static class MolargoFormat
{
    /// <summary>
    /// The locale money is written in, from the practice's currency.
    /// </summary>
    /// <remarks>
    /// Set once by <c>TenantContext</c> as the clinic resolves, and read by every figure
    /// in the app. Static because this type is called from markup with no injection, and
    /// safe because the tenant is process-wide — see the note beside the call that sets it
    /// for what has to change when that stops being true.
    ///
    /// Defaulted rather than left null: a figure rendered before the tenant resolves is a
    /// splash screen, not a wrong invoice, and throwing there would take the app down
    /// before it could show anything.
    /// </remarks>
    private static CultureInfo MoneyCulture { get; set; } =
        PracticeCurrency.CultureFor(PracticeCurrency.Default);

    /// <summary>Points every figure in the app at the practice's currency.</summary>
    public static void UseCurrency(string? currencyCode) =>
        MoneyCulture = PracticeCurrency.CultureFor(currencyCode);

    /// <summary>The locale money is written in, for the few places that format it themselves.</summary>
    public static CultureInfo Currency => MoneyCulture;

    /// <summary>
    /// Used for the month abbreviation, and deliberately NOT <c>en-AU</c>.
    /// </summary>
    /// <remarks>
    /// CLDR's Australian abbreviated month names are not uniformly three letters — they
    /// are "Mar.", "June", "July", "Sept." — so an en-AU date column renders "12 Mar
    /// 1968" next to "3 June 1965" and the dates no longer line up or scan. The invariant
    /// culture gives the plain three-letter forms every clinician already reads on a
    /// referral. The day-month-year order comes from the format string, not the culture,
    /// so nothing about the Australian reading of the date is lost.
    /// </remarks>
    private static readonly CultureInfo DateCulture = CultureInfo.InvariantCulture;

    /// <summary>A date of birth or any other calendar date. Em dash when absent.</summary>
    public static string Date(DateOnly? value) =>
        value?.ToString("d MMM yyyy", DateCulture) ?? "—";

    /// <summary>
    /// A stored UTC timestamp, shown as a date in the reader's local zone. Used for
    /// "last seen", where the time of day is noise.
    /// </summary>
    public static string ShortDate(DateTime? value) =>
        value?.ToLocalTime().ToString("d MMM yy", DateCulture) ?? "—";

    /// <summary>"Mon 8 Sep" — a weekday and date, for a day's heading.</summary>
    public static string DayLabel(DateOnly value) => value.ToString("ddd d MMM", DateCulture);

    /// <summary>"Fri 11 Sep" — a stored UTC timestamp as a local weekday and date.</summary>
    public static string DayLabel(DateTime? value) =>
        value?.ToLocalTime().ToString("ddd d MMM", DateCulture) ?? "—";

    /// <summary>"Fri 11 Sep · 08:45" — the same, with the time.</summary>
    /// <remarks>
    /// Twenty-four hour, not "8:45". A twelve-hour clock without am/pm rendered a 16:40
    /// email as "4:40" in the comms log, which is a record of when the practice contacted
    /// someone — a field that must not be readable two ways.
    /// </remarks>
    public static string DayTime(DateTime? value) =>
        value?.ToLocalTime().ToString("ddd d MMM · HH:mm", DateCulture) ?? "—";

    /// <summary>A stored UTC timestamp as a full local date and time.</summary>
    public static string DateTime(DateTime? value) =>
        value?.ToLocalTime().ToString("dddd d MMMM yyyy, HH:mm", DateCulture) ?? "—";

    /// <summary>
    /// An amount, to whole dollars. Whole dollars because every column this appears in is
    /// scanned rather than reconciled — the cents belong on the invoice, not the list.
    /// </summary>
    public static string Money(decimal value) => value.ToString("C0", MoneyCulture);

    /// <summary>
    /// An amount, or an em dash where there is none to report.
    /// </summary>
    /// <remarks>
    /// An overload rather than a replacement: a call passing a plain <c>decimal</c> still
    /// binds to the non-nullable one above, so no existing caller changes behaviour. Used
    /// for a derived average whose denominator can be zero, where "$0" would read as free
    /// treatment rather than as nothing to average.
    /// </remarks>
    public static string Money(decimal? value) =>
        value is { } amount ? Money(amount) : "—";

    /// <summary>An amount including cents, for an invoice or a receipt.</summary>
    public static string MoneyExact(decimal value) => value.ToString("C", MoneyCulture);

    /// <summary>
    /// An amount in a named currency, whatever the practice's own is.
    /// </summary>
    /// <remarks>
    /// For the one thing on screen that is not the practice's money: what they pay Molargo.
    /// A plan priced in AUD rendered through a Philippine practice's culture reads as
    /// "₱149.00" — the right number wearing the wrong sign, which is the kind of wrong
    /// nobody queries until the card is charged.
    /// </remarks>
    public static string MoneyIn(string? currencyCode, decimal value) =>
        value.ToString("C", PracticeCurrency.CultureFor(currencyCode));

    /// <summary>
    /// A rate, or an em dash where there was nothing to divide by.
    /// </summary>
    /// <remarks>
    /// Nullable on purpose. A rate over an empty denominator is absent, not zero, and the
    /// difference matters most where the reading is reassuring: "0% failed to attend" over
    /// a period with no appointments reports perfect attendance.
    /// </remarks>
    public static string Percent(decimal? value) =>
        value is { } rate ? rate.ToString("0.#", DateCulture) + "%" : "—";
}
