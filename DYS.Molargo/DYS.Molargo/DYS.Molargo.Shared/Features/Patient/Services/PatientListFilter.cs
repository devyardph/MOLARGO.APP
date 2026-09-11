using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Shared.Features.Patient.Services;

/// <summary>
/// One position of the patients list's segmented filter.
/// </summary>
/// <remarks>
/// Carries both dimensions because the prototype's four segments do not all filter the
/// same thing: All, Recall due and Archived are statuses, but Debtors is a balance. A
/// status-only parameter would leave the view toggling two commands to move between
/// segments that are visually one control — and able to reach combinations the control
/// cannot show, like "Archived debtors" with both highlighted at once.
/// </remarks>
/// <param name="Status">Null means every status except archived.</param>
/// <param name="OutstandingOnly">True for the debtors segment.</param>
public sealed record PatientListFilter(PatientStatus? Status, bool OutstandingOnly);
