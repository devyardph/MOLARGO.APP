namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// What a clinician is registered to do. Gates which procedure codes they may chart and
/// whether their provider number can appear on a claim.
/// </summary>
public enum ProviderRole
{
    Dentist = 0,
    Hygienist = 1,
    OralHealthTherapist = 2,
    DentalTherapist = 3,
    Prosthetist = 4,

    /// <summary>Endodontist, periodontist, orthodontist and the like.</summary>
    Specialist = 5,

    /// <summary>Chairside assistant. Cannot be the billing provider on a claim.</summary>
    Assistant = 6,

    /// <summary>Front desk and practice management. No clinical scope at all.</summary>
    Administration = 7,

    /// <summary>
    /// The vendor's own operator, who administers the clinics subscribing to the app.
    /// </summary>
    /// <remarks>
    /// Not a role a clinic can grant. It belongs to the platform tenant — see
    /// <see cref="DYS.Molargo.Domain.Entities.Tenant.IsPlatform"/> — and a practice's own
    /// Users screen neither offers it nor can reach an account that holds it, because the
    /// tenant filter puts those accounts outside the practice's rows entirely.
    ///
    /// It is also the first role in this app that actually restricts anything. Every other
    /// value here changes two behaviours (authoring clinical records, and being bookable)
    /// and gates no screen.
    /// </remarks>
    SuperAdmin = 8,
}
