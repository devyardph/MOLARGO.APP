using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// A file on the patient record — a radiograph, a photograph, a signed consent, a
/// specialist's report.
/// </summary>
public sealed class PatientDocument : EntityBase
{
    public Guid PatientId { get; set; }

    public DocumentKind Kind { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Path to the file, relative to the app's document store — never absolute. An
    /// absolute path recorded on one device is meaningless on another, and on iOS it
    /// changes between installs of the same app.
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    public string? ContentType { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>When the document was taken or signed, which may long precede its upload.</summary>
    public DateTime? DocumentDateUtc { get; set; }

    /// <summary>What it relates to, so the record screen can group by tooth or visit.</summary>
    public string? RelatesTo { get; set; }

    public Guid? AppointmentId { get; set; }

    public Guid? UploadedByProviderId { get; set; }
}
