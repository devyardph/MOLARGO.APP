using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A warning attached to a patient — an allergy, a condition, a medication, a care note.
///
/// A row per alert rather than free text on the patient, so each one can carry its own
/// severity, be acknowledged individually, and be checked against a prescription by code
/// rather than by someone reading a paragraph.
/// </summary>
public sealed class PatientAlert : EntityBase
{
    public Guid PatientId { get; set; }

    public AlertKind Kind { get; set; }

    public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;

    /// <summary>The short form shown on the chip — "Penicillin".</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>The full note — reaction, date, source.</summary>
    public string? Detail { get; set; }

    /// <summary>When it was first recorded clinically, which is not when the row was created.</summary>
    public DateOnly? OnsetDate { get; set; }

    /// <summary>
    /// Set when an alert no longer applies — a course of medication finished, a pregnancy
    /// ended. Resolved alerts stay on the record; a cleared allergy history is itself
    /// clinically relevant.
    /// </summary>
    public DateOnly? ResolvedDate { get; set; }

    public bool IsResolved => ResolvedDate is not null;

    /// <summary>Who recorded it.</summary>
    public Guid? RecordedByProviderId { get; set; }
}
