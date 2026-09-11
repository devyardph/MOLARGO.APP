namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// Recorded sex, which drives clinical risk prompts and claiming, and is distinct from a
/// patient's gender identity — kept separate because Medicare and the health funds match
/// on the former while correspondence should use the latter.
/// </summary>
public enum Sex
{
    Unspecified = 0,
    Female = 1,
    Male = 2,

    /// <summary>Recorded as another value, or the patient declined to state one.</summary>
    Other = 3,
}
