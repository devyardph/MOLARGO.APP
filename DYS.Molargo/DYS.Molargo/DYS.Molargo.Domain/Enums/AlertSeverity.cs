namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// How hard an alert should interrupt. Separate from <see cref="AlertKind"/> because the
/// same kind spans both extremes: a penicillin anaphylaxis and a mild latex sensitivity
/// are both allergies.
/// </summary>
public enum AlertSeverity
{
    /// <summary>Shown on the record. Does not interrupt.</summary>
    Information = 0,

    /// <summary>Shown prominently, and on the appointment.</summary>
    Warning = 1,

    /// <summary>Interrupts: prescribing and treatment start require acknowledgement.</summary>
    Critical = 2,
}
