using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain;

/// <summary>
/// When one site trades: which days, and between what times.
/// </summary>
/// <remarks>
/// <para>
/// A value rather than a set of constants, which is the seam the old version predicted it
/// would have to become. It was hard-coded to Mon–Sat, 08:00–18:00, and a practice that
/// opens on Sunday — or closes at 17:00, or on a Wednesday — had no way to say so: the
/// booking form simply refused the slot and named no way to change its mind.
/// </para>
/// <para>
/// It is deliberately not a roster. One open and one close for the whole week, no
/// per-day times, no public holidays, no seasonal hours. Those need the shift entity the
/// Rosters pane is still waiting for; this is the practice's own trading pattern, which is
/// the thing every screen in the app already assumed it knew.
/// </para>
/// </remarks>
/// <param name="Days">
/// The days the site trades. <see cref="WorkingDays.None"/> would close the practice
/// permanently, so it is read as the default rather than obeyed — see <see cref="Effective"/>.
/// </param>
public readonly record struct PracticeHours(WorkingDays Days, TimeOnly Open, TimeOnly Close)
{
    /// <summary>
    /// The diary's slot granularity, in minutes. Bookings snap to it, and it is the
    /// smallest move a reschedule can make.
    /// </summary>
    /// <remarks>
    /// Still a constant, and correctly so: this is how the diary grid is drawn, not a
    /// decision a practice makes. Every site's columns are built on it.
    /// </remarks>
    public const int SlotMinutes = 15;

    /// <summary>
    /// What a site trades when nobody has said otherwise — Mon–Sat, 08:00 to 18:00.
    /// </summary>
    /// <remarks>
    /// The values the app was hard-coded to, kept as the default so an existing practice
    /// behaves exactly as it did before the setting existed.
    /// </remarks>
    public static readonly PracticeHours Default = new(
        WorkingDays.Weekdays | WorkingDays.Saturday,
        new TimeOnly(8, 0),
        new TimeOnly(18, 0));

    public int OpenMinutes => Open.Hour * 60 + Open.Minute;

    public int CloseMinutes => Close.Hour * 60 + Close.Minute;

    /// <summary>Chair minutes available per chair per trading day.</summary>
    public int WorkingMinutes => Math.Max(0, CloseMinutes - OpenMinutes);

    /// <summary>
    /// This value with anything unusable replaced by the default.
    /// </summary>
    /// <remarks>
    /// A site with no days set, or a close at or before its open, would make every slot in
    /// the app unbookable and give no clue why. Reading those as "not configured" keeps a
    /// half-filled row harmless; the Sites screen is where they get corrected.
    /// </remarks>
    public PracticeHours Effective =>
        Days == WorkingDays.None || CloseMinutes <= OpenMinutes ? Default : this;

    /// <summary>
    /// Whether the practice trades at all on a date.
    /// </summary>
    /// <remarks>
    /// Public holidays close the practice too, but they are data — a hard-coded list goes
    /// stale annually and differs by state, so the month view marks the closed weekdays
    /// and leaves holidays to the roster once that exists.
    /// </remarks>
    public bool IsOpenOn(DateOnly date) =>
        Effective.Days.HasFlag(ProviderAvailability.Of(date.DayOfWeek));

    /// <summary>"Mon, Tue, Wed, Thu, Fri, Sat · 08:00–18:00" — for a settings summary.</summary>
    public string Summary
    {
        get
        {
            var hours = Effective;

            return $"{ProviderAvailability.DaysLabel(hours.Days) ?? "No days"} · "
                + $"{hours.Open:HH\\:mm}–{hours.Close:HH\\:mm}";
        }
    }

    /// <summary>
    /// Why a slot cannot be used, or null if it can.
    /// </summary>
    /// <remarks>
    /// One implementation for both ways a slot gets set — dragging an existing booking and
    /// saving a new one. Two copies of "does this fit the day" would drift, and the first
    /// symptom would be a booking the diary accepts on save but refuses on the next move.
    /// </remarks>
    public string? RefuseSlot(DateTime startLocal, int durationMinutes)
    {
        if (durationMinutes <= 0) return "Give the appointment a length.";

        var hours = Effective;

        if (!hours.IsOpenOn(DateOnly.FromDateTime(startLocal)))
        {
            return $"The practice is closed on a {startLocal.DayOfWeek} "
                + $"— it opens {ProviderAvailability.DaysLabel(hours.Days)}.";
        }

        var startMinutes = (int)startLocal.TimeOfDay.TotalMinutes;

        if (startMinutes < hours.OpenMinutes
            || startMinutes + durationMinutes > hours.CloseMinutes)
        {
            return $"That runs outside opening hours "
                + $"({hours.Open:HH\\:mm}–{hours.Close:HH\\:mm}).";
        }

        return null;
    }
}
