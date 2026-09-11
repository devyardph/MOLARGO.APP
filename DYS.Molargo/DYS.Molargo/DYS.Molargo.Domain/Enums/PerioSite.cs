namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// One of the six points probed around a tooth.
/// </summary>
/// <remarks>
/// Six, not four: the interproximal sites are where periodontal disease starts and where a
/// four-point chart misses it. The set is fixed by the standard and will not grow, which is
/// why it is an enum rather than practice configuration.
/// </remarks>
public enum PerioSite
{
    MesioBuccal = 0,
    Buccal = 1,
    DistoBuccal = 2,

    /// <summary>Palatal on an upper tooth. One name for both; the tooth number says which.</summary>
    MesioLingual = 3,
    Lingual = 4,
    DistoLingual = 5,
}
