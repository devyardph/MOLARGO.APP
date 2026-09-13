using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One physical site. A multi-site practice shares one patient record across sites but
/// keeps separate diaries, chairs, stock and takings, so almost everything else in this
/// domain is scoped by a location id.
/// </summary>
public sealed class PracticeLocation : EntityBase
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Short form for the diary header and the location chip — "Sydney CBD".</summary>
    public string? ShortName { get; set; }

    public string? AddressLine { get; set; }
    public string? Suburb { get; set; }
    public string? State { get; set; }
    public string? Postcode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    /// <summary>
    /// ABN, which appears on every invoice. On the location rather than a global setting
    /// because sites are sometimes separate legal entities.
    /// </summary>
    public string? Abn { get; set; }

    /// <summary>
    /// IANA time zone id — "Australia/Sydney". Stored rather than assumed: appointment
    /// times are UTC in the database and a site in Perth renders the same diary three
    /// hours out from one in Sydney.
    /// </summary>
    public string TimeZoneId { get; set; } = "Australia/Sydney";

    /// <summary>
    /// Order in the app bar's location switcher, and which site the app opens on.
    /// Explicit rather than alphabetical: the practice's main site is the one staff
    /// should land on, and sorting by name put a satellite clinic first.
    /// </summary>
    public int DisplayOrder { get; set; }

    // ---- trading hours ---------------------------------------------------

    /// <summary>
    /// The days this site opens. <c>None</c> means nobody has set them.
    /// </summary>
    /// <remarks>
    /// On the location, not on a practice-wide setting, because this is the thing that
    /// most obviously differs between sites — a satellite clinic runs three days a week,
    /// and Saturday is shorter almost everywhere. Read through
    /// <see cref="PracticeHours"/>, which supplies the defaults for a site nobody has
    /// configured.
    /// </remarks>
    public WorkingDays OpeningDays { get; set; } = WorkingDays.None;

    /// <summary>When the site opens. Null falls back to the default.</summary>
    public TimeOnly? OpensAt { get; set; }

    /// <summary>When it closes. Null falls back to the default.</summary>
    public TimeOnly? ClosesAt { get; set; }

    /// <summary>This site's trading pattern, with defaults where it has none.</summary>
    public PracticeHours Hours => new PracticeHours(
        OpeningDays,
        OpensAt ?? PracticeHours.Default.Open,
        ClosesAt ?? PracticeHours.Default.Close)
        .Effective;

    /// <summary>Retired sites stay for their historical records but take no new bookings.</summary>
    public bool IsActive { get; set; } = true;
}
