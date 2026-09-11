namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// How a steriliser cycle ended. A failed cycle means every instrument in it has to be
/// reprocessed, so this is the field an audit asks for first.
/// </summary>
public enum SterilisationResult
{
    /// <summary>Still running.</summary>
    InProgress = 0,

    /// <summary>Completed and all indicators passed.</summary>
    Passed = 1,

    /// <summary>Completed but an indicator failed. The load is not sterile.</summary>
    Failed = 2,

    /// <summary>Interrupted before completion. Treated as not sterile.</summary>
    Aborted = 3,
}
