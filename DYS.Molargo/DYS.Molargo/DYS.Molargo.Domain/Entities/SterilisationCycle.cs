using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One autoclave run.
/// </summary>
/// <remarks>
/// The reason this is a first-class entity rather than a logbook page: infection-control
/// audit asks which cycle a given instrument came out of and whether that cycle passed,
/// and a failed cycle means recalling every load in it. That question has to be answerable
/// per patient, which is what <see cref="SterilisationCycleUse"/> exists for.
/// </remarks>
public sealed class SterilisationCycle : EntityBase
{
    public Guid PracticeLocationId { get; set; }

    /// <summary>Which autoclave, where the practice has more than one.</summary>
    public string SterilisorName { get; set; } = string.Empty;

    /// <summary>
    /// The machine's own cycle counter, which is what is written on the pouch and read
    /// back off it months later.
    /// </summary>
    public int CycleNumber { get; set; }

    public DateTime StartedUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }

    public SterilisationResult Result { get; set; } = SterilisationResult.InProgress;

    /// <summary>Cycle type — "B", "S", "N" — which decides what the load may contain.</summary>
    public string? CycleType { get; set; }

    public decimal? PeakTemperatureCelsius { get; set; }

    public decimal? HoldTimeMinutes { get; set; }

    /// <summary>Chemical indicator passed.</summary>
    public bool ChemicalIndicatorPassed { get; set; }

    /// <summary>
    /// Biological indicator result. Nullable because it is run periodically rather than
    /// every cycle, and "not tested" must not read as "failed".
    /// </summary>
    public bool? BiologicalIndicatorPassed { get; set; }

    /// <summary>What was in the load.</summary>
    public string? LoadContents { get; set; }

    public Guid? OperatedByProviderId { get; set; }

    /// <summary>What went wrong and what was done about it, for a failed cycle.</summary>
    public string? FailureNotes { get; set; }

    /// <summary>
    /// When the load was released for use, and by whom.
    /// </summary>
    /// <remarks>
    /// A separate act from running the cycle, and separately recorded. Somebody has to read
    /// the indicators and take responsibility for the load being safe — that signature is
    /// the whole point of a sterilisation record in an audit, and it is not the same person
    /// as the one who pressed start.
    /// </remarks>
    public DateTime? ReleasedUtc { get; set; }

    public Guid? ReleasedByProviderId { get; set; }

    public bool IsReleased => ReleasedUtc is not null;
}
