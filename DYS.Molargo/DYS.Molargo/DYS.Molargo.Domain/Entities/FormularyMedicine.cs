using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One medicine the practice prescribes, with the directions it is normally written with.
/// </summary>
/// <remarks>
/// A short practice formulary, not a national drug database. A dental practice prescribes
/// from a list of about a dozen medicines, and typing the directions out each time is how
/// "four times a day" becomes "4x/day" and then becomes a dispensing error — so the
/// template is the point of this table.
/// </remarks>
public sealed class FormularyMedicine : EntityBase
{
    /// <summary>Generic name, which is what gets prescribed.</summary>
    public string GenericName { get; set; } = string.Empty;

    public string? BrandName { get; set; }

    /// <summary>"500mg", "0.2%" — as it is written on the script.</summary>
    public string Strength { get; set; } = string.Empty;

    /// <summary>"Capsule", "Tablet", "Mouthwash".</summary>
    public string? Form { get; set; }

    public MedicineClass Class { get; set; } = MedicineClass.Other;

    /// <summary>
    /// The directions this medicine is normally written with, spelled out in the words the
    /// label will carry. Editable per script — this is a starting point, not a rule.
    /// </summary>
    public string DefaultDirections { get; set; } = string.Empty;

    public int DefaultQuantity { get; set; } = 1;

    public int DefaultRepeats { get; set; }

    /// <summary>
    /// Allergy families this medicine belongs to, separated by semicolons — a penicillin
    /// allergy has to stop amoxicillin, which shares no letters with it.
    /// </summary>
    /// <remarks>
    /// A delimited string rather than a table, matching <c>AppointmentType.AllowedRoles</c>.
    /// The lists are two or three words long and are only ever read whole; a join table
    /// for them would be more machinery than the thing it holds.
    /// </remarks>
    public string? AllergyClasses { get; set; }

    /// <summary>
    /// Patient medications this one is risky alongside, separated by semicolons —
    /// "warfarin", "methotrexate".
    /// </summary>
    public string? InteractsWith { get; set; }

    /// <summary>What to say when an interaction matches.</summary>
    public string? InteractionCaution { get; set; }

    /// <summary>
    /// Conditions to flag, separated by semicolons — "asthma", "renal", "pregnancy".
    /// </summary>
    public string? ConditionCautions { get; set; }

    /// <summary>What to say when a condition matches.</summary>
    public string? ConditionCaution { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    /// <summary>"Amoxicillin 500mg capsule" — the line the formulary list shows.</summary>
    public string Label => string.Join(" ", new[] { GenericName, Strength, Form?.ToLowerInvariant() }
        .Where(part => !string.IsNullOrWhiteSpace(part)));
}
