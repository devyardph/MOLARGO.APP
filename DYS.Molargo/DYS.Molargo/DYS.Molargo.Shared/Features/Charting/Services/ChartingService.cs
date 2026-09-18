using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Components;
using DYS.Molargo.Shared.Repositories;
using DYS.Molargo.Shared.Services;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Features.Charting.Services;

/// <summary>
/// The chart for one patient: the current finding on every tooth, plus the visit's note.
/// </summary>
/// <param name="Current">
/// Current findings only — anything superseded is excluded. The chart shows the mouth as
/// it is now; the history is still in the table for the timeline to read.
/// </param>
/// <param name="Amends">
/// The locked note <paramref name="Note"/> corrects, when it is an amendment. Carried so
/// the screen can show what is being corrected — a correction read without the original
/// says nothing.
/// </param>
public sealed record PatientChart(
    PatientEntity Patient,
    IReadOnlyList<ToothChartEntry> Current,
    ClinicalNote? Note,
    ClinicalNote? Amends = null)
{
    /// <summary>
    /// The findings on one tooth, most recently observed first.
    /// </summary>
    /// <remarks>
    /// A list, not one entry. A tooth routinely carries an existing restoration on one
    /// surface and new decay on another, and collapsing them to a single condition is how
    /// a chart loses the finding that mattered.
    /// </remarks>
    public IReadOnlyList<ToothChartEntry> For(string fdi) =>
        Current
            .Where(entry => entry.ToothNumber == fdi)
            .OrderByDescending(entry => entry.ObservedOn)
            .ToList();

    /// <summary>
    /// The one finding that decides how a tooth is drawn.
    /// </summary>
    /// <remarks>
    /// Missing wins over everything — an extracted tooth is not also carrying a
    /// restoration. After that the most recent finding shows, which is what a clinician
    /// glancing at the arch expects to see.
    /// </remarks>
    public ToothChartEntry? DominantFor(string fdi)
    {
        var entries = For(fdi);

        return entries.FirstOrDefault(entry => entry.Condition == ToothCondition.Missing)
            ?? entries.FirstOrDefault();
    }
}

/// <summary>
/// A clinical note as the timeline shows it, with its author resolved.
/// </summary>
/// <param name="Amends">
/// The note this one corrects. Present on the amendment, so the timeline can say what
/// changed instead of showing two near-identical notes and leaving the reader to diff
/// them.
/// </param>
public sealed record TimelineNote(
    ClinicalNote Note,
    string ProviderName,
    ClinicalNote? Amends)
{
    public bool IsAmendment => Amends is not null;

    /// <summary>Whether a later note corrects this one.</summary>
    public bool IsAmended { get; init; }
}

/// <summary>
/// One day of a patient's clinical history: the notes written and the findings charted.
/// </summary>
/// <remarks>
/// Grouped by the date of the care, not the date of the keystrokes. A note typed up the
/// following morning belongs on the day of the appointment, which is the day anyone
/// reading the record back will look under.
/// </remarks>
public sealed record VisitHistory(
    DateOnly Date,
    IReadOnlyList<TimelineNote> Notes,
    IReadOnlyList<ToothChartEntry> Charted);

/// <summary>
/// Reading and writing a patient's tooth chart, and the note that goes with the visit.
/// </summary>
public interface IChartingService
{
    Task<PatientChart?> GetChartAsync(Guid patientId, CancellationToken ct = default);

    /// <summary>
    /// Records a finding on a tooth, superseding whatever conflicting finding was there.
    /// </summary>
    /// <param name="surfaces">
    /// Which surfaces are involved. <c>None</c> for a whole-tooth finding such as a
    /// missing tooth or a crown.
    /// </param>
    Task ChartAsync(
        Guid patientId,
        string toothNumber,
        ToothCondition condition,
        ToothSurface surfaces,
        string? detail = null,
        Guid? providerId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Supersedes every current finding on a tooth, leaving it charted as sound.
    /// </summary>
    /// <remarks>
    /// Supersedes rather than deletes. "This tooth was charted as decayed and is now
    /// charted as sound" is a different clinical statement from "this tooth was never
    /// charted", and only the first is true.
    /// </remarks>
    Task ClearToSoundAsync(
        Guid patientId, string toothNumber, Guid? providerId = null, CancellationToken ct = default);

    /// <summary>Creates or updates the draft note for the visit. Refuses once locked.</summary>
    Task<ClinicalNote> SaveNoteAsync(
        Guid patientId,
        Guid providerId,
        string? presenting,
        string? treatmentProvided,
        string? plan,
        CancellationToken ct = default);

    /// <summary>
    /// Signs the note off. After this its text is immutable and a correction has to be a
    /// new note referencing it.
    /// </summary>
    Task<ClinicalNote?> LockNoteAsync(Guid noteId, CancellationToken ct = default);

    /// <summary>
    /// Starts a correction to a locked note: a new, empty note that references it.
    /// </summary>
    /// <remarks>
    /// The only way to change what a signed note says. The original is never touched, so
    /// the record shows both what was written and what was corrected — which is the whole
    /// reason the lock exists.
    /// </remarks>
    Task<ClinicalNote> AmendNoteAsync(
        Guid noteId, Guid providerId, CancellationToken ct = default);

    /// <summary>
    /// The patient's clinical history, most recent day first.
    /// </summary>
    /// <remarks>
    /// Includes superseded findings. The point of a history is the entries that no longer
    /// hold — the decay that was filled is exactly what a reader is looking for.
    /// </remarks>
    Task<IReadOnlyList<VisitHistory>> GetHistoryAsync(
        Guid patientId, CancellationToken ct = default);
}

/// <inheritdoc cref="IChartingService"/>
public sealed class ChartingService : IChartingService
{
    private readonly IRepository<PatientEntity> _patients;
    private readonly IRepository<ToothChartEntry> _chart;
    private readonly IRepository<ClinicalNote> _notes;
    private readonly IRepository<Provider> _providers;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;

    public ChartingService(
        IRepository<PatientEntity> patients,
        IRepository<ToothChartEntry> chart,
        IRepository<ClinicalNote> notes,
        IRepository<Provider> providers,
        IClock clock,
        IAuditLog audit)
    {
        _patients = patients;
        _chart = chart;
        _notes = notes;
        _providers = providers;
        _clock = clock;
        _audit = audit;
    }

    public async Task<PatientChart?> GetChartAsync(Guid patientId, CancellationToken ct = default)
    {
        var patient = await _patients.GetByIdAsync(patientId, ct).ConfigureAwait(false);
        if (patient is null) return null;

        var entries = await _chart
            .ListAsync(entry => entry.PatientId == patientId && entry.SupersededOn == null, ct)
            .ConfigureAwait(false);

        // This visit's note, locked or not.
        //
        // Filtering to unlocked notes here was wrong and looked like the lock button
        // being broken: signing a note removed it from the chart entirely, so the screen
        // re-rendered an empty editable form as though nothing had happened. The lock
        // governs whether the note can be EDITED, which IsLocked already says — not
        // whether it can be seen.
        var notes = await _notes
            .ListAsync(note => note.PatientId == patientId, ct)
            .ConfigureAwait(false);

        var today = _clock.Today;

        // Most recently written first, so an amendment supersedes the note it corrects on
        // screen while both stay in the table.
        var todays = notes
            .Where(note => DateOnly.FromDateTime(note.TreatmentDateUtc.ToLocalTime()) == today)
            .OrderByDescending(note => note.CreatedUtc)
            .FirstOrDefault();

        var amends = todays?.AmendsNoteId is { } amendedId
            ? notes.FirstOrDefault(note => note.Id == amendedId)
            : null;

        return new PatientChart(patient, entries, todays, amends);
    }

    public async Task ChartAsync(
        Guid patientId,
        string toothNumber,
        ToothCondition condition,
        ToothSurface surfaces,
        string? detail = null,
        Guid? providerId = null,
        CancellationToken ct = default)
    {
        var today = _clock.Today;

        var existing = await _chart
            .ListAsync(entry => entry.PatientId == patientId
                && entry.ToothNumber == toothNumber
                && entry.SupersededOn == null, ct)
            .ConfigureAwait(false);

        foreach (var entry in existing.Where(entry => Conflicts(entry, condition, surfaces)))
        {
            entry.SupersededOn = today;
            await _chart.SaveAsync(entry, ct).ConfigureAwait(false);
        }

        await _chart
            .SaveAsync(
                new ToothChartEntry
                {
                    PatientId = patientId,
                    ToothNumber = toothNumber,

                    // FDI always, whatever notation the clinician is looking at. See
                    // ToothNumbering for why the stored form is fixed.
                    Notation = ToothNotation.Fdi,
                    Condition = condition,
                    Surfaces = surfaces,
                    Detail = detail,
                    ObservedOn = today,
                    ChartedByProviderId = providerId,
                },
                ct)
            .ConfigureAwait(false);

        // One per tooth charted, which is a handful per visit rather than the hundreds a
        // perio chart would produce — so unlike the periodontal readings, these are worth
        // logging individually. A finding that changes on a tooth nobody treated that day
        // is the thing somebody eventually needs to trace.
        await _audit
            .RecordAsync(
                AuditAction.Updated,
                "Tooth chart",
                patientId,
                $"Charted tooth {toothNumber} as {condition}"
                    + (surfaces == ToothSurface.None ? string.Empty : $" ({surfaces})"),
                patientId,
                ct)
            .ConfigureAwait(false);
    }

    public async Task ClearToSoundAsync(
        Guid patientId, string toothNumber, Guid? providerId = null, CancellationToken ct = default)
    {
        var today = _clock.Today;

        var existing = await _chart
            .ListAsync(entry => entry.PatientId == patientId
                && entry.ToothNumber == toothNumber
                && entry.SupersededOn == null, ct)
            .ConfigureAwait(false);

        foreach (var entry in existing)
        {
            entry.SupersededOn = today;
            await _chart.SaveAsync(entry, ct).ConfigureAwait(false);
        }

        await ChartAsync(patientId, toothNumber, ToothCondition.Sound, ToothSurface.None,
            providerId: providerId, ct: ct).ConfigureAwait(false);
    }

    public async Task<ClinicalNote> SaveNoteAsync(
        Guid patientId,
        Guid providerId,
        string? presenting,
        string? treatmentProvided,
        string? plan,
        CancellationToken ct = default)
    {
        var today = _clock.Today;

        var notes = await _notes
            .ListAsync(note => note.PatientId == patientId && note.LockedUtc == null, ct)
            .ConfigureAwait(false);

        // Today's draft specifically. Reusing the most recent unlocked note of any date
        // would append this visit's findings to a draft abandoned weeks ago.
        var note = notes
                .Where(candidate =>
                    DateOnly.FromDateTime(candidate.TreatmentDateUtc.ToLocalTime()) == today)
                .OrderByDescending(candidate => candidate.CreatedUtc)
                .FirstOrDefault()
            ?? new ClinicalNote
            {
                PatientId = patientId,
                ProviderId = providerId,
                TreatmentDateUtc = _clock.UtcNow,
            };

        // Guarded even though GetChartAsync only ever hands back an unlocked note: this is
        // the last point before the write, and a locked clinical note being editable is
        // the one thing that makes the whole record worthless in a dispute.
        if (note.IsLocked)
        {
            throw new InvalidOperationException(
                "That note is signed and locked. Record a correction as a new note instead.");
        }

        note.Presenting = Blank(presenting);
        note.TreatmentProvided = Blank(treatmentProvided);
        note.Plan = Blank(plan);

        await _notes.SaveAsync(note, ct).ConfigureAwait(false);
        return note;
    }

    public async Task<ClinicalNote?> LockNoteAsync(Guid noteId, CancellationToken ct = default)
    {
        var note = await _notes.GetByIdAsync(noteId, ct).ConfigureAwait(false);
        if (note is null) return null;

        // Already locked is not an error — two taps on the button must not throw, and the
        // first timestamp is the one that counts.
        if (note.IsLocked) return note;

        note.LockedUtc = _clock.UtcNow;
        await _notes.SaveAsync(note, ct).ConfigureAwait(false);

        // Audited as a change to the note rather than under an action of its own. A signed
        // note cannot be edited afterwards and nothing else writes an entry against one, so
        // there is no other "Changed / Clinical note" line for this to be confused with —
        // and a verb per clinical event turns the filter row into a menu nobody reads.
        //
        // After the save, so a log that refuses cannot stop a clinician signing their note.
        var patient = await _patients.GetByIdAsync(note.PatientId, ct).ConfigureAwait(false);

        await _audit
            .RecordAsync(
                AuditAction.Updated,
                "Clinical note",
                note.Id,
                $"Signed and locked the note for {patient?.FullName ?? "a patient"}, "
                    + MolargoFormat.Date(DateOnly.FromDateTime(note.TreatmentDateUtc.ToLocalTime())),
                note.PatientId,
                ct)
            .ConfigureAwait(false);

        return note;
    }

    public async Task<ClinicalNote> AmendNoteAsync(
        Guid noteId, Guid providerId, CancellationToken ct = default)
    {
        var original = await _notes.GetByIdAsync(noteId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No note with id {noteId}.");

        var amendment = new ClinicalNote
        {
            PatientId = original.PatientId,
            AppointmentId = original.AppointmentId,
            ProviderId = providerId,

            // Dated to the original visit, not to now. The amendment describes care given
            // that day; its own CreatedUtc records when the correction was made.
            TreatmentDateUtc = original.TreatmentDateUtc,
            AmendsNoteId = original.Id,
        };

        await _notes.SaveAsync(amendment, ct).ConfigureAwait(false);

        // A correction to a signed note is the single most questioned act in a clinical
        // record. Both ids go in — the amendment and what it amends — because the pair is
        // what shows the original was kept rather than rewritten.
        await _audit
            .RecordAsync(
                AuditAction.Created,
                "Clinical note",
                amendment.Id,
                "Started a correction to the signed note of "
                    + MolargoFormat.Date(DateOnly.FromDateTime(original.TreatmentDateUtc.ToLocalTime())),
                original.PatientId,
                ct)
            .ConfigureAwait(false);

        return amendment;
    }

    public async Task<IReadOnlyList<VisitHistory>> GetHistoryAsync(
        Guid patientId, CancellationToken ct = default)
    {
        var notes = await _notes
            .ListAsync(note => note.PatientId == patientId, ct)
            .ConfigureAwait(false);

        // Every finding, superseded ones included — see the remarks on GetHistoryAsync.
        var findings = await _chart
            .ListAsync(entry => entry.PatientId == patientId, ct)
            .ConfigureAwait(false);

        var providers = await _providers.ListAsync(ct: ct).ConfigureAwait(false);

        var names = providers.ToDictionary(
            provider => provider.Id,
            provider => provider.DisplayName is { Length: > 0 } display
                ? display
                : $"{provider.FirstName} {provider.LastName}".Trim());

        var byId = notes.ToDictionary(note => note.Id);
        var amendedIds = notes
            .Where(note => note.AmendsNoteId is not null)
            .Select(note => note.AmendsNoteId!.Value)
            .ToHashSet();

        var timeline = notes.Select(note => new TimelineNote(
                note,
                names.TryGetValue(note.ProviderId, out var name) ? name : "Unknown author",
                note.AmendsNoteId is { } amendedId && byId.TryGetValue(amendedId, out var amended)
                    ? amended
                    : null)
            {
                IsAmended = amendedIds.Contains(note.Id),
            })
            .ToList();

        var days = timeline
            .Select(entry => DateOnly.FromDateTime(entry.Note.TreatmentDateUtc.ToLocalTime()))
            .Concat(findings.Select(entry => entry.ObservedOn))
            .Distinct()
            .OrderByDescending(date => date);

        return days
            .Select(date => new VisitHistory(
                date,
                timeline
                    .Where(entry =>
                        DateOnly.FromDateTime(entry.Note.TreatmentDateUtc.ToLocalTime()) == date)
                    .OrderByDescending(entry => entry.Note.CreatedUtc)
                    .ToList(),
                findings
                    .Where(entry => entry.ObservedOn == date)
                    .OrderBy(entry => entry.ToothNumber)
                    .ToList()))
            .ToList();
    }

    /// <summary>
    /// Whether a new finding replaces an existing one.
    /// </summary>
    /// <remarks>
    /// A whole-tooth finding — missing, crown, implant — supersedes everything on that
    /// tooth: there is no decay on a tooth that has been extracted. A surface finding only
    /// supersedes findings that touch the same surfaces, so charting decay on the mesial
    /// leaves an existing occlusal restoration alone. That distinction is the whole reason
    /// a tooth holds several entries.
    /// </remarks>
    private static bool Conflicts(ToothChartEntry existing, ToothCondition condition, ToothSurface surfaces)
    {
        if (IsWholeTooth(condition) || IsWholeTooth(existing.Condition)) return true;

        if (surfaces == ToothSurface.None || existing.Surfaces == ToothSurface.None) return true;

        return (existing.Surfaces & surfaces) != 0;
    }

    private static bool IsWholeTooth(ToothCondition condition) => condition is
        ToothCondition.Missing
        or ToothCondition.Unerupted
        or ToothCondition.Impacted
        or ToothCondition.Crown
        or ToothCondition.Bridge
        or ToothCondition.Implant
        or ToothCondition.RootCanalTreated
        or ToothCondition.Sound;

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
