namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A bookable kind of visit — "Exam &amp; clean", "Crown prep", "Emergency".
///
/// Carries the default duration and colour so the front desk books a known quantity
/// rather than guessing at a length, which is the single biggest source of a diary that
/// runs late all afternoon.
/// </summary>
public sealed class AppointmentType : EntityBase
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Default length in minutes. The booking may still override it.</summary>
    public int DefaultDurationMinutes { get; set; } = 30;

    /// <summary>CSS hex colour for the diary block.</summary>
    public string? Colour { get; set; }

    /// <summary>
    /// Which clinical roles may be booked for it, so a hygiene slot cannot be filled with
    /// a surgical procedure. Null means no restriction.
    /// </summary>
    public string? AllowedRoles { get; set; }

    /// <summary>True where the type needs a chair equipped for surgery.</summary>
    public bool RequiresSurgicalRoom { get; set; }

    /// <summary>Offered on the public online-booking widget, as opposed to internal only.</summary>
    public bool IsBookableOnline { get; set; }

    public bool IsActive { get; set; } = true;
}
