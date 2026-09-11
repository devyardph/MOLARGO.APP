namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// What a formulary medicine is for. Groups the list and drives nothing clinical on its
/// own — the checks match on named allergy families, not on the class.
/// </summary>
public enum MedicineClass
{
    Antibiotic = 0,
    Analgesic = 1,
    AntiInflammatory = 2,
    Antifungal = 3,
    Antiviral = 4,

    /// <summary>Mouthwashes and topical antiseptics.</summary>
    Antiseptic = 5,

    /// <summary>Pre-medication for anxiety.</summary>
    Anxiolytic = 6,

    Other = 7,
}
