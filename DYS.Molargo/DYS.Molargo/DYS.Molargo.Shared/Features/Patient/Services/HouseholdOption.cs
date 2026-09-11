namespace DYS.Molargo.Shared.Features.Patient.Services;

/// <summary>
/// An existing household, as the patient form offers it for linking.
/// </summary>
/// <remarks>
/// There is no household entity — members simply share a <c>HouseholdId</c>, because the
/// only question ever asked of it is "who else lives here" and a shared id survives a
/// member moving out without orphaning a row. So a household has no name of its own, and
/// this carries the surname its members share plus how many there are, which is what the
/// front desk recognises it by.
/// </remarks>
/// <param name="Surname">The surname its members share, or the commonest one.</param>
/// <param name="MemberCount">How many patients are in it.</param>
public sealed record HouseholdOption(Guid Id, string Surname, int MemberCount)
{
    /// <summary>"Yuen household (3)" — the label the select shows.</summary>
    public string Label => $"{Surname} household ({MemberCount})";
}
