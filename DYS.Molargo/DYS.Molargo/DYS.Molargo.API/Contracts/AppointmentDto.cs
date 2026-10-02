using DYS.Molargo.Domain.Entities;

namespace DYS.Molargo.Api.Contracts;

/// <summary>
/// An appointment as the API returns it.
/// </summary>
/// <remarks>
/// Times are UTC and named so. The device renders in the site's own zone — which the site
/// row carries, and which is not the server's — so an API returning local times without
/// saying which local would be handing every caller a different guess.
/// </remarks>
public sealed record AppointmentDto(
    Guid Id,
    Guid PatientId,
    string PatientName,
    Guid ProviderId,
    Guid? OperatoryId,
    DateTime StartUtc,
    int DurationMinutes,
    string Status,
    string? Reason,
    Guid? SeriesId,
    int? SeriesPosition)
{
    public static AppointmentDto From(
        Appointment appointment, IReadOnlyDictionary<Guid, string> names) => new(
        appointment.Id,
        appointment.PatientId,
        names.GetValueOrDefault(appointment.PatientId, "Unknown patient"),
        appointment.ProviderId,
        appointment.OperatoryId,
        appointment.StartUtc,
        appointment.DurationMinutes,
        appointment.Status.ToString(),
        appointment.Reason,
        appointment.AppointmentSeriesId,
        appointment.SeriesPosition);
}
