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

    /// <summary>
    /// Months until the patient is due back after a visit of this type, or null where it
    /// generates no recall.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On the type rather than on the patient, because it is a property of the work: an
    /// exam and clean brings somebody back in six months whoever they are, and a crown fit
    /// brings them back for nothing. A per-patient interval exists too — see
    /// <see cref="Recall.IntervalMonths"/> — and it wins, because a periodontal patient on
    /// three months does not revert to six just because they had a routine clean.
    /// </para>
    /// <para>
    /// Null, not zero. Most types produce no recall at all — an emergency, a crown fit, a
    /// consultation — and zero months would mean "due immediately", which is a worklist
    /// entry for every visit the practice has ever done.
    /// </para>
    /// </remarks>
    public int? RecallIntervalMonths { get; set; }

    public bool IsActive { get; set; } = true;
}
