namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// Which set of teeth the chart is showing.
/// </summary>
/// <remarks>
/// A view over the same patient, not a property of them. A seven-year-old is charted on
/// the mixed arch today and the permanent one in five years, and the findings recorded
/// against a deciduous tooth stay valid after it has gone — which is why this selects what
/// the odontogram renders rather than being stored on the patient.
/// </remarks>
public enum Dentition
{
    /// <summary>The 32 permanent teeth.</summary>
    Permanent = 0,

    /// <summary>The 20 deciduous teeth.</summary>
    Deciduous = 1,

    /// <summary>
    /// The typical transitional arch — permanent incisors and first molars, deciduous
    /// canines and molars.
    /// </summary>
    Mixed = 2,
}
