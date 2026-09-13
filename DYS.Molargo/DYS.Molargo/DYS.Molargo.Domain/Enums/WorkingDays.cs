namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// The days of the week a clinician works, as a set.
/// </summary>
/// <remarks>
/// <para>
/// Flags rather than a table of rows, because the question asked of it is always "is this
/// person in on a Tuesday" and the answer is one bit. A shift table will be needed
/// eventually — it is what the Rosters pane and the diary's Roster tab both say they are
/// waiting for, and it is the only way to express "Tuesdays at Newtown, Thursdays at
/// Sydney CBD", a fortnightly pattern, or leave. This is deliberately the smaller thing:
/// the weekly pattern a practice actually types into a booking screen.
/// </para>
/// <para>
/// <see cref="None"/> means the pattern has not been set, not that the person never works.
/// Absent is not the same as empty, and a clinician whose days nobody filled in must stay
/// bookable rather than quietly vanishing from every diary.
/// </para>
/// </remarks>
[Flags]
public enum WorkingDays
{
    /// <summary>Not recorded. Treated as available, not as unavailable.</summary>
    None = 0,

    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,

    /// <summary>The ordinary week, for the "weekdays" shortcut on the staff form.</summary>
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
}
