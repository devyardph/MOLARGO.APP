using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain;

/// <summary>
/// When a clinician is in, and whether a slot falls inside it.
/// </summary>
/// <remarks>
/// <para>
/// One definition, in Domain, for the same reason <see cref="PracticeHours"/> is: the
/// booking form, the provider picker and the save validation all have to agree, and three
/// copies of "does Tuesday count" would drift into a form that offers a slot the save then
/// refuses.
/// </para>
/// <para>
/// A weekly pattern with an optional start and finish — not a roster. It cannot say
/// "Tuesdays at Newtown", cannot express a fortnight, and knows nothing about leave or
/// public holidays; those need the shift entity the Rosters pane and the diary's Roster
/// tab are both waiting for. What it does cover is the case a practice hits every day:
/// this clinician works these days, between these hours.
/// </para>
/// <para>
/// Everything unset means available. A practice that has not filled any of this in must
/// keep working exactly as it did before the fields existed — the alternative is an
/// upgrade that empties every diary.
/// </para>
/// </remarks>
public static class ProviderAvailability
{
    /// <summary>The days in the order a form should offer them.</summary>
    public static readonly WorkingDays[] Week =
    [
        WorkingDays.Monday,
        WorkingDays.Tuesday,
        WorkingDays.Wednesday,
        WorkingDays.Thursday,
        WorkingDays.Friday,
        WorkingDays.Saturday,
        WorkingDays.Sunday,
    ];

    /// <summary>The flag for a calendar day.</summary>
    public static WorkingDays Of(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => WorkingDays.Monday,
        DayOfWeek.Tuesday => WorkingDays.Tuesday,
        DayOfWeek.Wednesday => WorkingDays.Wednesday,
        DayOfWeek.Thursday => WorkingDays.Thursday,
        DayOfWeek.Friday => WorkingDays.Friday,
        DayOfWeek.Saturday => WorkingDays.Saturday,
        _ => WorkingDays.Sunday,
    };

    /// <summary>"Mon, Tue, Thu", or null where no pattern has been set.</summary>
    public static string? DaysLabel(WorkingDays days)
    {
        if (days == WorkingDays.None) return null;

        var names = Week
            .Where(day => days.HasFlag(day))
            .Select(ShortName)
            .ToList();

        return names.Count == 0 ? null : string.Join(", ", names);
    }

    public static string ShortName(WorkingDays day) => day switch
    {
        WorkingDays.Monday => "Mon",
        WorkingDays.Tuesday => "Tue",
        WorkingDays.Wednesday => "Wed",
        WorkingDays.Thursday => "Thu",
        WorkingDays.Friday => "Fri",
        WorkingDays.Saturday => "Sat",
        WorkingDays.Sunday => "Sun",
        _ => day.ToString(),
    };

    /// <summary>Whether the clinician works at all on a date.</summary>
    /// <remarks>
    /// True when no pattern is recorded. See the class remarks: unset means unknown, and
    /// unknown must not read as "never".
    /// </remarks>
    public static bool WorksOn(Provider provider, DateOnly date) =>
        WorksOn(provider.WorkingDays, date);

    /// <inheritdoc cref="WorksOn(Provider, DateOnly)"/>
    public static bool WorksOn(WorkingDays days, DateOnly date) =>
        days == WorkingDays.None || days.HasFlag(Of(date.DayOfWeek));

    /// <summary>
    /// Why the clinician cannot take this slot, or null if they can.
    /// </summary>
    /// <remarks>
    /// A sentence rather than a bool, because the two reasons want different fixes — the
    /// wrong day means pick another day, the wrong hour means move the time — and a screen
    /// that only knows "unavailable" makes the user work out which.
    ///
    /// Deliberately silent about whether the practice is open: that is
    /// <see cref="PracticeHours.RefuseSlot"/>'s answer, and reporting both at once would
    /// tell somebody their dentist is off on a day the practice is shut anyway.
    /// </remarks>
    public static string? RefuseSlot(Provider provider, DateTime startLocal, int durationMinutes) =>
        RefuseSlot(
            provider.FullName,
            provider.WorkingDays,
            provider.WorkingFrom,
            provider.WorkingTo,
            startLocal,
            durationMinutes);

    /// <summary>
    /// The same check against the three values alone, for callers holding a picker option
    /// rather than the entity.
    /// </summary>
    /// <remarks>
    /// Split this way so the booking form can answer "is this clinician in then" from the
    /// list it already has, instead of reading every provider back to ask. The rule stays
    /// in one place; only the shape of the input differs.
    /// </remarks>
    public static string? RefuseSlot(
        string name,
        WorkingDays days,
        TimeOnly? from,
        TimeOnly? to,
        DateTime startLocal,
        int durationMinutes)
    {
        var date = DateOnly.FromDateTime(startLocal);

        if (!WorksOn(days, date))
        {
            var pattern = DaysLabel(days) is { } label ? $" — they work {label}" : string.Empty;

            return $"{name} does not work on a {startLocal.DayOfWeek}{pattern}.";
        }

        var start = TimeOnly.FromDateTime(startLocal);
        var end = start.AddMinutes(durationMinutes);

        if (from is { } opens && start < opens)
        {
            return $"{name} starts at {opens:HH\\:mm}.";
        }

        // Compared against the end of the appointment, not its start: a 90-minute booking
        // beginning ten minutes before someone finishes is the case this is for, and
        // checking the start alone would wave it through.
        //
        // A finish time earlier in the day than the start would make every slot fail, so
        // an end that has wrapped past midnight is treated as running over too.
        if (to is { } closes && (end > closes || end < start))
        {
            return $"{name} finishes at {closes:HH\\:mm}.";
        }

        return null;
    }
}
