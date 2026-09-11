namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// How serious a pre-prescribing finding is.
/// </summary>
/// <remarks>
/// Three levels, not two. A recorded penicillin allergy and "monitor INR" are both worth
/// showing, but collapsing them into one "warning" means the one that must stop the script
/// looks like the one that must not — and a prescriber who learns to click past warnings
/// clicks past both.
/// </remarks>
public enum PrescribingRisk
{
    /// <summary>Worth knowing. Does not block.</summary>
    Information = 0,

    /// <summary>Prescribe with care, or adjust the dose. Does not block.</summary>
    Caution = 1,

    /// <summary>
    /// Do not prescribe this to this patient. Blocks issuing until it is overridden with a
    /// reason.
    /// </summary>
    Contraindicated = 2,
}
