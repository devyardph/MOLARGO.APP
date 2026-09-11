namespace DYS.Molargo.Domain;

/// <summary>
/// The hours the practice sells chair time in.
/// </summary>
/// <remarks>
/// <para>
/// One definition, in Domain, because two screens disagreeing about when the day ends is
/// incoherent rather than merely untidy: the diary was drawing bookings until 18:00 while
/// the front desk measured its gaps against a day ending at 17:00, so the last hour of
/// every day was simultaneously bookable and outside opening hours.
/// </para>
/// <para>
/// Practice-wide until <c>PracticeLocation</c> carries its own hours. It will need to —
/// a second site keeps different ones, and Saturday is shorter everywhere — so this is
/// the seam that becomes a lookup, not a constant to sprinkle around.
/// </para>
/// </remarks>
public static class PracticeHours
{
    public static readonly TimeOnly Open = new(8, 0);

    public static readonly TimeOnly Close = new(18, 0);

    /// <summary>
    /// The diary's slot granularity, in minutes. Bookings snap to it, and it is the
    /// smallest move a reschedule can make.
    /// </summary>
    public const int SlotMinutes = 15;

    public static int OpenMinutes => Open.Hour * 60 + Open.Minute;

    public static int CloseMinutes => Close.Hour * 60 + Close.Minute;

    /// <summary>Chair minutes available per chair per working day.</summary>
    public static int WorkingMinutes => CloseMinutes - OpenMinutes;

    /// <summary>
    /// Whether the practice trades at all on a date.
    /// </summary>
    /// <remarks>
    /// Sunday only. Public holidays close the practice too, but they are data — a
    /// hard-coded list goes stale annually and differs by state, so the month view marks
    /// Sundays and leaves holidays to the roster once that exists.
    /// </remarks>
    public static bool IsOpenOn(DateOnly date) => date.DayOfWeek != DayOfWeek.Sunday;

    /// <summary>
    /// Why a slot cannot be used, or null if it can.
    /// </summary>
    /// <remarks>
    /// One implementation for both ways a slot gets set — dragging an existing booking and
    /// saving a new one. Two copies of "does this fit the day" would drift, and the first
    /// symptom would be a booking the diary accepts on save but refuses on the next move.
    /// </remarks>
    public static string? RefuseSlot(DateTime startLocal, int durationMinutes)
    {
        if (durationMinutes <= 0) return "Give the appointment a length.";

        if (!IsOpenOn(DateOnly.FromDateTime(startLocal)))
        {
            return "The practice is closed that day.";
        }

        var startMinutes = (int)startLocal.TimeOfDay.TotalMinutes;

        if (startMinutes < OpenMinutes || startMinutes + durationMinutes > CloseMinutes)
        {
            return $"That runs outside opening hours ({Open:HH\\:mm}–{Close:HH\\:mm}).";
        }

        return null;
    }
}
