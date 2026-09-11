using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One charted finding on one tooth.
/// </summary>
/// <remarks>
/// A row per finding, not per tooth. A tooth commonly carries an existing restoration, a
/// new area of decay on a different surface, and a periodontal note, all at once — and
/// each has its own date and its own author.
/// </remarks>
public sealed class ToothChartEntry : EntityBase
{
    public Guid PatientId { get; set; }

    /// <summary>
    /// The tooth, in the notation named by <see cref="Notation"/>. A string because
    /// Palmer notation is not numeric and deciduous teeth are lettered in some systems.
    /// </summary>
    public string ToothNumber { get; set; } = string.Empty;

    public ToothNotation Notation { get; set; } = ToothNotation.Fdi;

    public ToothCondition Condition { get; set; }

    /// <summary>The surfaces involved. <c>None</c> for a whole-tooth finding.</summary>
    public ToothSurface Surfaces { get; set; } = ToothSurface.None;

    /// <summary>
    /// Restorative material, or the specific finding — "composite", "amalgam", "3mm
    /// recession". Free text: the vocabulary is large, changes, and is written by
    /// clinicians who will not tolerate a dropdown that lacks their term.
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>
    /// When the finding was observed. Distinct from <see cref="EntityBase.CreatedUtc"/>:
    /// charting an old restoration records something that happened years ago.
    /// </summary>
    public DateOnly ObservedOn { get; set; }

    public Guid? ChartedByProviderId { get; set; }

    public Guid? AppointmentId { get; set; }

    /// <summary>The plan item that will address it, once one exists.</summary>
    public Guid? TreatmentPlanItemId { get; set; }

    /// <summary>
    /// Set when the finding no longer holds — the decay was filled, the tooth extracted.
    /// Superseded rather than deleted, so the chart can be replayed as at any past date,
    /// which is what a medico-legal question actually asks for.
    /// </summary>
    public DateOnly? SupersededOn { get; set; }

    public bool IsCurrent => SupersededOn is null && !IsDeleted;
}
