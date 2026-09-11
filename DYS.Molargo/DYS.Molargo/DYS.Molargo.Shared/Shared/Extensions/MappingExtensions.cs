using DYS.Molargo.Domain.Dtos;
using Mapster;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Extensions;

/// <summary>
/// Entity/DTO conversion, as extension methods so call sites read as
/// <c>entity.ToDto()</c> and <c>dto.ToModel()</c> rather than naming a mapper.
/// </summary>
public static class MappingExtensions
{
    public static PatientDto ToDto(this PatientEntity entity) => entity.Adapt<PatientDto>();

    public static PatientEntity ToModel(this PatientDto dto) => dto.Adapt<PatientEntity>();

    public static IReadOnlyList<PatientDto> ToDtos(this IEnumerable<PatientEntity> entities) =>
        entities.Select(ToDto).ToList();
}
