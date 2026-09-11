using System.Globalization;
using DYS.Molargo.Domain.Dtos;
using Mapster;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Settings;

/// <summary>
/// Entity-to-DTO mapping rules, declared once at startup. Only the rules that differ from
/// Mapster's name matching appear here; everything else maps by name, so a field added to
/// both sides needs no change.
/// </summary>
public static class MappingConfiguration
{
    /// <summary>The date form <see cref="PatientDto.DateOfBirth"/> carries.</summary>
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>Registers every rule. Call once during composition, before anything maps.</summary>
    public static void Apply()
    {
        TypeAdapterConfig<PatientEntity, PatientDto>.NewConfig()
            .Map(dto => dto.DateOfBirth, entity => Format(entity.DateOfBirth))
            .Compile();

        TypeAdapterConfig<PatientDto, PatientEntity>.NewConfig()
            .Map(entity => entity.DateOfBirth, dto => Parse(dto.DateOfBirth))

            // The DTO is what an edit form binds to, so it must not be able to set the
            // audit stamps or reverse a soft delete. The repository owns those.
            .Ignore(entity => entity.CreatedUtc)
            .Ignore(entity => entity.UpdatedUtc)
            .Ignore(entity => entity.IsDeleted)
            .Ignore(entity => entity.DeletedUtc!)
            .Compile();
    }

    private static string? Format(DateOnly? value) =>
        value?.ToString(DateFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses the wire and form representation, tolerating absence and rubbish. A
    /// half-typed date must come back as null rather than throwing: the form binds on
    /// every keystroke, so "1968-0" is a state the user passes through legitimately.
    /// </summary>
    private static DateOnly? Parse(string? value) =>
        DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
}
