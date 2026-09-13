namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// The practice's standard consent wording for a kind of procedure.
/// </summary>
/// <remarks>
/// <para>
/// A template, not the consent itself. When a form is raised the wording is <em>copied</em>
/// onto it — what a patient agreed to is the text that was in front of them that day, and
/// a consent that read its wording back through here would silently restate itself the
/// next time somebody edited the template.
/// </para>
/// <para>
/// Editable by the practice because consent wording is theirs: it follows their indemnity
/// insurer's advice, their jurisdiction, and what their clinicians actually say. Wording
/// compiled into the app is wording no practice can correct without a release.
/// </para>
/// </remarks>
public sealed class ConsentTemplate : EntityBase
{
    /// <summary>
    /// What the template is called, and the title a standalone consent takes from it.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The schedule category this is the default wording for — "Oral surgery".
    /// </summary>
    /// <remarks>
    /// Null for a template that is only ever chosen by hand, such as a sedation consent
    /// raised beside whatever is being done under it. Matching on the category rather than
    /// on the item number keeps one template covering a whole class of procedure: the risks
    /// of a surgical extraction do not change with the tooth.
    /// </remarks>
    public string? Category { get; set; }

    /// <summary>The risks, alternatives and costs, as the patient reads them.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Superseded wording stays for the consents already signed against it but is no
    /// longer offered.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }
}
