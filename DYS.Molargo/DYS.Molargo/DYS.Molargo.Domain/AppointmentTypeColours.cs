namespace DYS.Molargo.Domain;

/// <summary>One colour a practice may give an appointment type.</summary>
public sealed record AppointmentTypeColour(string Label, string Value);

/// <summary>
/// The colours an appointment type may be given.
/// </summary>
/// <remarks>
/// <para>
/// A closed list rather than a colour field. The value goes straight into a
/// <c>style="background:…"</c>, so a free-text box would let somebody type a colour that
/// does not resolve and get an invisible swatch — with nothing on screen to say why. It
/// would also let them pick a colour that vanishes against the diary's own surface.
/// </para>
/// <para>
/// Design tokens rather than hex, so a type coloured today still matches the app after a
/// palette change. The same list feeds the seed and the editor: two lists would drift the
/// first time either was extended, and the seeded types would slowly stop being
/// reproducible from the editor.
/// </para>
/// </remarks>
public static class AppointmentTypeColours
{
    public const string Accent = "var(--color-accent)";
    public const string Accent2 = "var(--color-accent2)";
    public const string Warn = "var(--color-warn)";
    public const string Neutral = "var(--color-neutral-500)";
    public const string Dark = "var(--color-neutral-800)";

    /// <summary>
    /// The palette, in the order the picker shows it.
    /// </summary>
    /// <remarks>
    /// Declared after the constants it reads, which is not stylistic: a static field
    /// initialiser that reaches a field declared below it sees the default rather than the
    /// value, and the failure arrives as a type-initialisation exception far from here.
    /// </remarks>
    public static readonly AppointmentTypeColour[] All =
    [
        new("Operative", Accent),
        new("Urgent", Accent2),
        new("Review", Warn),
        new("Routine", Neutral),
        new("Consult", Dark),
    ];

    /// <summary>Whether a stored value is one this app will actually render.</summary>
    /// <remarks>
    /// Null passes. A type with no colour is drawn in the neutral default, which is a
    /// deliberate state rather than a missing one.
    /// </remarks>
    public static bool IsKnown(string? value) =>
        value is null || All.Any(colour => colour.Value == value);

    /// <summary>The name of a stored colour, for a list row.</summary>
    public static string LabelFor(string? value) =>
        All.FirstOrDefault(colour => colour.Value == value)?.Label ?? "No colour";
}
