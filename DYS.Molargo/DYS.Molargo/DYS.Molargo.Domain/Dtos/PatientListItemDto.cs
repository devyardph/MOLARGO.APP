using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Dtos;

/// <summary>
/// One row of the patients list — exactly the columns the prototype's table shows, and
/// nothing else.
/// </summary>
/// <remarks>
/// A separate, narrower type from <see cref="PatientDto"/> on purpose. The list is the
/// most-read screen in the app and renders 10 rows at a time; projecting to this in the
/// query means SQLite reads a third of the columns and the view model holds no field the
/// row cannot show. It is also what keeps a clinical note or a Medicare number from
/// reaching a list view that has no business holding one.
/// </remarks>
public sealed class PatientListItemDto
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string? PatientNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? Mobile { get; set; }

    public PatientStatus Status { get; set; }

    public PatientTags Tags { get; set; }

    public DateTime? LastSeenUtc { get; set; }

    public decimal Balance { get; set; }

    public int FailedToAttendCount { get; set; }

    /// <summary>
    /// Age in whole years at <paramref name="today"/>. Duplicated from the entity rather
    /// than shared through a helper because the calculation is three lines and the
    /// alternative is this DTO taking a dependency on the entity it exists to replace.
    /// </summary>
    public int? AgeAt(DateOnly today)
    {
        if (DateOfBirth is not { } dob) return null;

        var age = today.Year - dob.Year;
        if (dob.AddYears(age) > today) age--;
        return age;
    }
}
