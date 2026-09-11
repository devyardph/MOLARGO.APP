namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One medicine on a prescription.
/// </summary>
public sealed class PrescriptionItem : EntityBase
{
    public Guid PrescriptionId { get; set; }

    /// <summary>Generic name, which is what is prescribed.</summary>
    public string MedicineName { get; set; } = string.Empty;

    /// <summary>Brand, where the prescriber has specified one.</summary>
    public string? BrandName { get; set; }

    /// <summary>Strength as written — "500mg", "2% with adrenaline 1:80,000".</summary>
    public string Strength { get; set; } = string.Empty;

    public string? Form { get; set; }

    /// <summary>
    /// Directions, in full and in the words the label will carry — "One tablet four times
    /// a day for five days". Free text: abbreviations are the classic source of a
    /// dispensing error, so the field holds what the patient should read.
    /// </summary>
    public string Directions { get; set; } = string.Empty;

    /// <summary>Number of units or packs to dispense.</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>Repeats authorised. Zero means dispense once.</summary>
    public int Repeats { get; set; }

    /// <summary>
    /// True where the prescriber requires the exact brand — for a patient stable on one
    /// formulation, or where excipients matter to a recorded allergy.
    /// </summary>
    public bool BrandSubstitutionNotPermitted { get; set; }
}
