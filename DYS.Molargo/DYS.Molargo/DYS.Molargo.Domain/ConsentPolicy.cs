namespace DYS.Molargo.Domain;

/// <summary>
/// Which procedures carry their own consent, and the practice's standard wording for each.
/// </summary>
/// <remarks>
/// <para>
/// Decided from the schedule category rather than a flag on every procedure code. A
/// <c>RequiresConsent</c> column would be authoritative in principle and wrong in practice:
/// it defaults to false, nobody revisits it when a code is added, and the item that
/// silently needed consent is the one that never gets it. The category is already
/// maintained, because the fee schedule groups by it.
/// </para>
/// <para>
/// Deliberately narrow. A form against every scale and clean teaches people to sign
/// without reading, which costs more protection than it buys.
/// </para>
/// <para>
/// The wording is the practice's template, stored onto each form as it is raised rather
/// than read back through here. What somebody agreed to is the text in front of them that
/// day; reading it through a template edited next year would restate a consent already
/// given.
/// </para>
/// </remarks>
public static class ConsentPolicy
{
    private static readonly HashSet<string> Consented = new(StringComparer.OrdinalIgnoreCase)
    {
        "Endodontics",
        "Oral surgery",
        "Periodontics",
        "Prosthodontics",
        "Implants",
        "Orthodontics",
        "Sedation",
    };

    /// <summary>Whether a procedure in this schedule category needs its own signed form.</summary>
    public static bool NeedsConsent(string? category) =>
        category is { Length: > 0 } && Consented.Contains(category);

    /// <summary>
    /// The starting wording for a consent in this category — the risks, the alternatives
    /// and the costs, in the voice a patient reads and signs.
    /// </summary>
    public static string Wording(string? category) => category?.ToLowerInvariant() switch
    {
        "endodontics" =>
            "Risks discussed: the tooth may stay tender for some days; an instrument can "
            + "separate in the canal; the root can perforate or fracture; and treatment can "
            + "fail and need retreatment, surgery or extraction. Alternatives — extraction, "
            + "or leaving the tooth — and the cost of each were presented. Questions answered.",

        "oral surgery" =>
            "Risks discussed: bleeding, bruising, swelling, infection, dry socket, damage to "
            + "neighbouring teeth or fillings, and numbness of the lip, chin or tongue that "
            + "may be temporary or permanent. Alternatives and costs presented. Questions "
            + "answered.",

        "periodontics" =>
            "Risks discussed: gum recession, sensitivity to cold, teeth feeling loose while "
            + "the gums heal, and that treatment controls the disease rather than curing it "
            + "— ongoing maintenance is needed. Alternatives and costs presented. Questions "
            + "answered.",

        "prosthodontics" =>
            "Risks discussed: permanent removal of tooth structure, post-operative "
            + "sensitivity, the possibility that the tooth later needs root canal treatment, "
            + "and fracture or debonding under grinding. Alternatives and costs presented. "
            + "Questions answered.",

        "implants" =>
            "Risks discussed: infection, the implant failing to integrate, damage to "
            + "neighbouring teeth, nerve or sinus involvement, and the maintenance the "
            + "implant needs for the rest of its life. Alternatives and costs presented. "
            + "Questions answered.",

        "orthodontics" =>
            "Risks discussed: shortening of the roots, decalcification and decay around the "
            + "brackets, gum inflammation, relapse if retainers are not worn, and jaw joint "
            + "discomfort. Alternatives and costs presented. Questions answered.",

        "sedation" =>
            "Risks discussed: drowsiness and no memory of the appointment, nausea, slowed "
            + "breathing, and the need for an escort home with no driving or work for the "
            + "rest of the day. Fasting and medication instructions given. Questions answered.",

        // Reached only if a category is added to the set above without wording. Says what
        // it is rather than inventing risks — a consent claiming a discussion that has not
        // been written down is worse than an obviously unfinished one.
        _ => "Risks, alternatives and costs discussed. Wording for this procedure has not "
             + "been set up — write what was actually put to the patient before signing.",
    };
}
