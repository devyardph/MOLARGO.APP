namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// Operational flags the front desk sets by hand on a patient record. Deliberately a
/// closed set rather than free text: each one changes how the practice treats the
/// booking — longer appointments for a nervous patient, an interpreter to arrange, a
/// payment conversation before treatment starts — so an arbitrary label would not be
/// actionable.
/// </summary>
/// <remarks>
/// Values are explicit powers of two because they are persisted and travel on the wire.
/// Add new members with the next unused bit; never renumber an existing one, which would
/// silently reinterpret every stored row.
/// </remarks>
[Flags]
public enum PatientTags
{
    None = 0,
    Vip = 1 << 0,
    NervousPatient = 1 << 1,
    HighRisk = 1 << 2,
    InterpreterNeeded = 1 << 3,
    Debtor = 1 << 4,
}
