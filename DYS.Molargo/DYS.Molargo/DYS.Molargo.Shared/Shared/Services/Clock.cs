namespace DYS.Molargo.Shared.Services;

/// <summary>
/// Wall-clock access, injected so the "today" used for ages, recall intervals, overdue
/// prompts and next-visit calculations can be pinned in tests. Reading
/// <see cref="DateTime.UtcNow"/> directly makes all of that untestable.
/// </summary>
public interface IClock
{
    /// <summary>Now, in UTC. Everything persisted or synced uses this.</summary>
    DateTime UtcNow { get; }

    /// <summary>
    /// Now, in the device's local zone. For display only — a diary column header, an
    /// appointment time read out to a patient. Never persist it.
    /// </summary>
    DateTime Now { get; }

    /// <summary>Today's date in the device's local zone, for ages and recall arithmetic.</summary>
    DateOnly Today { get; }
}

/// <summary>The real clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime Now => DateTime.Now;

    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}
