using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Shared.Features.Patient.Services;

/// <summary>
/// Which slice of the patient base the list screen wants. A query object rather than a
/// widening parameter list, so adding a filter changes neither <c>IPatientService</c>'s
/// signature nor any caller that does not care about the new filter.
/// </summary>
public sealed class PatientQuery
{
    /// <summary>
    /// Matched against name, patient number and mobile. One box rather than three fields
    /// because the front desk types whatever the caller offers.
    /// </summary>
    public string? SearchTerm { get; set; }

    /// <summary>
    /// Restrict to one lifecycle status. Null means every status except
    /// <see cref="PatientStatus.Archived"/> — an archived patient is excluded by default,
    /// since including them in routine lists is how recall letters reach the deceased.
    /// </summary>
    public PatientStatus? Status { get; set; }

    /// <summary>Restrict to patients carrying all of these flags. <c>None</c> means no filter.</summary>
    public PatientTags Tags { get; set; } = PatientTags.None;

    /// <summary>Only patients with money owing. Drives the debtors worklist.</summary>
    public bool OutstandingBalanceOnly { get; set; }

    /// <summary>Zero-based.</summary>
    public int Page { get; set; }

    /// <summary>
    /// Matches the prototype's ten-row patients table. Kept modest deliberately: the
    /// device head renders these from SQLite on a tablet, and a page of hundreds costs
    /// more in layout than the extra round trip saves.
    /// </summary>
    public int PageSize { get; set; } = 17;
}
