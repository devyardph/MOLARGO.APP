using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Api.Contracts;

/// <summary>
/// A patient as the API returns them.
/// </summary>
/// <remarks>
/// Its own shape, not the entity. The entity carries the tenant id, the soft-delete flag
/// and the audit stamps — none of which a caller has any business seeing, and the first of
/// which says which clinic's database this came out of. A DTO is also the only way the wire
/// format can stay still while the schema moves.
/// </remarks>
public sealed record PatientDto(
    Guid Id,
    string? PatientNumber,
    string FirstName,
    string LastName,
    string FullName,
    DateOnly? DateOfBirth,
    string Sex,
    string? Mobile,
    string? Email,
    Guid? PracticeLocationId)
{
    /// <remarks>
    /// On the contract rather than in each endpoint. Three routes return a patient, and
    /// three copies of this mapping would differ the first time a field was added to one
    /// of them.
    /// </remarks>
    public static PatientDto From(PatientEntity patient) => new(
        patient.Id,
        patient.PatientNumber,
        patient.FirstName,
        patient.LastName,
        patient.FullName,
        patient.DateOfBirth,
        patient.Sex.ToString(),
        patient.Mobile,
        patient.Email,
        patient.PracticeLocationId);
}
