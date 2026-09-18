using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Repositories;
using DYS.Molargo.Shared.Services;

namespace DYS.Molargo.Shared.Features.Charting.Services;

/// <summary>
/// One tooth as the perio chart draws it: its six sites plus the whole-tooth findings.
/// </summary>
public sealed record PerioTooth(
    string ToothNumber,
    IReadOnlyDictionary<PerioSite, PerioSiteReading> Sites,
    PerioToothReading? Tooth)
{
    public PerioSiteReading? At(PerioSite site) => Sites.GetValueOrDefault(site);

    public int? DepthAt(PerioSite site) => At(site)?.ProbingDepthMm;

    public bool BleedingAt(PerioSite site) => At(site)?.Bleeding ?? false;

    public bool SuppurationAt(PerioSite site) => At(site)?.Suppuration ?? false;

    /// <summary>The worst pocket on the tooth, which is what a summary line reports.</summary>
    public int? DeepestMm => Sites.Values
        .Select(reading => reading.ProbingDepthMm)
        .Where(depth => depth is not null)
        .DefaultIfEmpty(null)
        .Max();

    /// <summary>Recession, taken as the worst site — the figure a chart writes per tooth.</summary>
    public int? RecessionMm => Sites.Values
        .Select(reading => reading.RecessionMm)
        .Where(value => value is not null)
        .DefaultIfEmpty(null)
        .Max();

    public bool HasAnyReading => Sites.Count > 0 || Tooth is not null;
}

/// <summary>
/// The figures that make one exam comparable with another.
/// </summary>
/// <param name="DeepSiteCount">
/// Sites at or over <see cref="PerioService.DeepPocketMm"/>. The count clinicians act on.
/// </param>
/// <param name="BleedingPercent">
/// Bleeding sites as a share of sites probed, not of all possible sites — a partial exam
/// would otherwise look like an improvement simply for having probed less.
/// </param>
public sealed record PerioSummary(
    Guid ExamId,
    DateOnly ExamDate,
    bool IsComplete,
    int SitesProbed,
    decimal? MeanDepthMm,
    int? DeepestMm,
    int DeepSiteCount,
    int BleedingPercent)
{
    public bool HasReadings => SitesProbed > 0;
}

/// <summary>
/// One patient's periodontal chart: the exam being worked on and the history behind it.
/// </summary>
public sealed record PerioChart(
    PerioExam? Current,
    IReadOnlyList<PerioTooth> Teeth,
    IReadOnlyList<PerioSummary> History)
{
    public PerioTooth? Tooth(string fdi) =>
        Teeth.FirstOrDefault(tooth => tooth.ToothNumber == fdi);

    /// <summary>The summary of the exam on screen.</summary>
    public PerioSummary? CurrentSummary =>
        Current is null ? null : History.FirstOrDefault(entry => entry.ExamId == Current.Id);
}

/// <summary>
/// Reading and writing periodontal examinations.
/// </summary>
public interface IPerioService
{
    /// <summary>
    /// The patient's newest exam and the history behind it. Never creates anything — a
    /// patient who has never been probed has no exam, and the screen says so.
    /// </summary>
    Task<PerioChart> GetChartAsync(Guid patientId, CancellationToken ct = default);

    /// <summary>The same, for a named exam, so an earlier one can be reviewed.</summary>
    Task<PerioChart> GetChartAsync(
        Guid patientId, Guid examId, CancellationToken ct = default);

    /// <summary>Starts a new exam dated today.</summary>
    Task<PerioExam> StartExamAsync(
        Guid patientId, Guid? providerId, CancellationToken ct = default);

    /// <summary>
    /// Records a pocket depth. Passing null clears the reading, which is not the same as
    /// recording 0mm.
    /// </summary>
    Task SetDepthAsync(
        Guid examId, string toothNumber, PerioSite site, int? depthMm,
        CancellationToken ct = default);

    /// <summary>Flips bleeding on probing for a site.</summary>
    Task ToggleBleedingAsync(
        Guid examId, string toothNumber, PerioSite site, CancellationToken ct = default);

    /// <summary>Flips suppuration for a site.</summary>
    Task ToggleSuppurationAsync(
        Guid examId, string toothNumber, PerioSite site, CancellationToken ct = default);

    /// <summary>Records recession across the three sites on one side of a tooth.</summary>
    Task SetRecessionAsync(
        Guid examId, string toothNumber, PerioSite site, int? recessionMm,
        CancellationToken ct = default);

    Task SetMobilityAsync(
        Guid examId, string toothNumber, ToothMobility? mobility, CancellationToken ct = default);

    Task SetFurcationAsync(
        Guid examId, string toothNumber, FurcationGrade? furcation, CancellationToken ct = default);

    /// <summary>
    /// Marks the exam finished, so it can be compared against as a complete picture.
    /// </summary>
    Task CompleteExamAsync(Guid examId, CancellationToken ct = default);
}

/// <inheritdoc cref="IPerioService"/>
public sealed class PerioService : IPerioService
{
    /// <summary>
    /// The depth at which a pocket counts as deep.
    /// </summary>
    /// <remarks>
    /// 5mm, not 4mm. A 4mm pocket is common and often stable; 5mm is the threshold where
    /// the site cannot be cleaned by the patient and becomes a treatment decision. One
    /// constant, so the count on the chart and the count in any future report agree.
    /// </remarks>
    public const int DeepPocketMm = 5;

    /// <summary>
    /// The deepest reading the chart accepts.
    /// </summary>
    /// <remarks>
    /// A probe is marked to 15mm at most, so anything beyond is a typo or a mis-tap rather
    /// than a measurement — and a stray 55 would swamp the mean depth for every exam it
    /// appeared in.
    /// </remarks>
    public const int MaximumDepthMm = 15;

    private readonly IRepository<PerioExam> _exams;
    private readonly IRepository<PerioSiteReading> _sites;
    private readonly IRepository<PerioToothReading> _teeth;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;

    public PerioService(
        IRepository<PerioExam> exams,
        IRepository<PerioSiteReading> sites,
        IRepository<PerioToothReading> teeth,
        IClock clock,
        IAuditLog audit)
    {
        _exams = exams;
        _sites = sites;
        _teeth = teeth;
        _clock = clock;
        _audit = audit;
    }

    public async Task<PerioChart> GetChartAsync(Guid patientId, CancellationToken ct = default) =>
        await BuildAsync(patientId, null, ct).ConfigureAwait(false);

    public async Task<PerioChart> GetChartAsync(
        Guid patientId, Guid examId, CancellationToken ct = default) =>
        await BuildAsync(patientId, examId, ct).ConfigureAwait(false);

    public async Task<PerioExam> StartExamAsync(
        Guid patientId, Guid? providerId, CancellationToken ct = default)
    {
        var today = _clock.Today;

        var existing = await _exams
            .ListAsync(exam => exam.PatientId == patientId && exam.ExamDate == today, ct)
            .ConfigureAwait(false);

        // Today's exam is reused rather than a second one started. Two exams on one date
        // split the day's probing across both, and the comparison then shows two
        // half-exams instead of one visit.
        if (existing.OrderByDescending(exam => exam.CreatedUtc).FirstOrDefault() is { } open)
        {
            return open;
        }

        var created = new PerioExam
        {
            PatientId = patientId,
            ProviderId = providerId,
            ExamDate = today,
        };

        await _exams.SaveAsync(created, ct).ConfigureAwait(false);
        return created;
    }

    public async Task SetDepthAsync(
        Guid examId, string toothNumber, PerioSite site, int? depthMm,
        CancellationToken ct = default)
    {
        var reading = await FindOrCreateSiteAsync(examId, toothNumber, site, ct)
            .ConfigureAwait(false);

        reading.ProbingDepthMm = depthMm is { } depth
            ? Math.Clamp(depth, 0, MaximumDepthMm)
            : null;

        await SaveOrRemoveSiteAsync(reading, ct).ConfigureAwait(false);
    }

    public async Task ToggleBleedingAsync(
        Guid examId, string toothNumber, PerioSite site, CancellationToken ct = default)
    {
        var reading = await FindOrCreateSiteAsync(examId, toothNumber, site, ct)
            .ConfigureAwait(false);

        reading.Bleeding = !reading.Bleeding;

        await SaveOrRemoveSiteAsync(reading, ct).ConfigureAwait(false);
    }

    public async Task ToggleSuppurationAsync(
        Guid examId, string toothNumber, PerioSite site, CancellationToken ct = default)
    {
        var reading = await FindOrCreateSiteAsync(examId, toothNumber, site, ct)
            .ConfigureAwait(false);

        reading.Suppuration = !reading.Suppuration;

        await SaveOrRemoveSiteAsync(reading, ct).ConfigureAwait(false);
    }

    public async Task SetRecessionAsync(
        Guid examId, string toothNumber, PerioSite site, int? recessionMm,
        CancellationToken ct = default)
    {
        // Applied across the three sites on the same side. Recession is read off the
        // gingival margin, which runs continuously across a face rather than stopping at
        // each probing point, and asking for it three times per face is how it stops
        // being recorded at all.
        foreach (var each in PerioSites.SidesOf(site))
        {
            var reading = await FindOrCreateSiteAsync(examId, toothNumber, each, ct)
                .ConfigureAwait(false);

            reading.RecessionMm = recessionMm;

            await SaveOrRemoveSiteAsync(reading, ct).ConfigureAwait(false);
        }
    }

    public async Task SetMobilityAsync(
        Guid examId, string toothNumber, ToothMobility? mobility, CancellationToken ct = default)
    {
        var reading = await FindOrCreateToothAsync(examId, toothNumber, ct).ConfigureAwait(false);

        reading.Mobility = mobility;

        await SaveOrRemoveToothAsync(reading, ct).ConfigureAwait(false);
    }

    public async Task SetFurcationAsync(
        Guid examId, string toothNumber, FurcationGrade? furcation, CancellationToken ct = default)
    {
        var reading = await FindOrCreateToothAsync(examId, toothNumber, ct).ConfigureAwait(false);

        reading.Furcation = furcation;

        await SaveOrRemoveToothAsync(reading, ct).ConfigureAwait(false);
    }

    public async Task CompleteExamAsync(Guid examId, CancellationToken ct = default)
    {
        var exam = await _exams.GetByIdAsync(examId, ct).ConfigureAwait(false);
        if (exam is null || exam.IsComplete) return;

        exam.CompletedUtc = _clock.UtcNow;

        await _exams.SaveAsync(exam, ct).ConfigureAwait(false);

        // The completed exam, not the hundreds of pocket depths that make it up. One entry
        // per six-point chart would be six hundred rows for one appointment, and a log
        // nobody can read is the same as no log.
        await _audit
            .RecordAsync(
                AuditAction.Updated,
                nameof(PerioExam),
                examId,
                "Completed a periodontal chart",
                exam.PatientId,
                ct)
            .ConfigureAwait(false);
    }

    // ---- helpers ---------------------------------------------------------

    private async Task<PerioChart> BuildAsync(Guid patientId, Guid? examId, CancellationToken ct)
    {
        var exams = await _exams
            .ListAsync(exam => exam.PatientId == patientId, ct)
            .ConfigureAwait(false);

        var ordered = exams
            .OrderByDescending(exam => exam.ExamDate)
            .ThenByDescending(exam => exam.CreatedUtc)
            .ToList();

        if (ordered.Count == 0) return new PerioChart(null, [], []);

        var current = examId is { } wanted
            ? ordered.FirstOrDefault(exam => exam.Id == wanted) ?? ordered[0]
            : ordered[0];

        // Every exam's readings, in one pass. The history panel needs a mean and a deep
        // count per exam, so the alternative is a query per exam — and a patient on
        // 3-monthly maintenance has a lot of exams.
        var examIds = ordered.Select(exam => exam.Id).ToHashSet();

        var siteReadings = await _sites
            .ListAsync(reading => examIds.Contains(reading.PerioExamId), ct)
            .ConfigureAwait(false);

        var toothReadings = await _teeth
            .ListAsync(reading => examIds.Contains(reading.PerioExamId), ct)
            .ConfigureAwait(false);

        var currentSites = siteReadings
            .Where(reading => reading.PerioExamId == current.Id)
            .GroupBy(reading => reading.ToothNumber);

        var currentTeeth = toothReadings
            .Where(reading => reading.PerioExamId == current.Id)
            .ToDictionary(reading => reading.ToothNumber);

        var teeth = currentSites
            .Select(group => new PerioTooth(
                group.Key,
                group
                    .GroupBy(reading => reading.Site)
                    .ToDictionary(bySite => bySite.Key, bySite => bySite.First()),
                currentTeeth.GetValueOrDefault(group.Key)))
            .ToList();

        // Teeth with only a whole-tooth finding — mobility charted, nothing probed yet.
        foreach (var (fdi, reading) in currentTeeth)
        {
            if (teeth.Any(tooth => tooth.ToothNumber == fdi)) continue;

            teeth.Add(new PerioTooth(
                fdi,
                new Dictionary<PerioSite, PerioSiteReading>(),
                reading));
        }

        var history = ordered
            .Select(exam => Summarise(
                exam,
                siteReadings.Where(reading => reading.PerioExamId == exam.Id).ToList()))
            .ToList();

        return new PerioChart(current, teeth, history);
    }

    private static PerioSummary Summarise(PerioExam exam, IReadOnlyList<PerioSiteReading> readings)
    {
        var probed = readings.Where(reading => reading.ProbingDepthMm is not null).ToList();

        return new PerioSummary(
            exam.Id,
            exam.ExamDate,
            exam.IsComplete,
            probed.Count,
            probed.Count == 0
                ? null
                : Math.Round(probed.Average(reading => (decimal)reading.ProbingDepthMm!.Value), 1),
            probed.Count == 0 ? null : probed.Max(reading => reading.ProbingDepthMm!.Value),
            probed.Count(reading => reading.ProbingDepthMm >= DeepPocketMm),

            // Share of probed sites, not of the whole mouth. See the remarks on the record.
            probed.Count == 0
                ? 0
                : (int)Math.Round(100m * probed.Count(reading => reading.Bleeding) / probed.Count));
    }

    private async Task<PerioSiteReading> FindOrCreateSiteAsync(
        Guid examId, string toothNumber, PerioSite site, CancellationToken ct)
    {
        var existing = await _sites
            .FindAsync(reading => reading.PerioExamId == examId
                && reading.ToothNumber == toothNumber
                && reading.Site == site, ct)
            .ConfigureAwait(false);

        return existing ?? new PerioSiteReading
        {
            PerioExamId = examId,
            ToothNumber = toothNumber,
            Site = site,
        };
    }

    /// <summary>
    /// Writes the reading, or deletes it once it holds nothing.
    /// </summary>
    /// <remarks>
    /// A row with no depth, no recession and neither flag set is not a reading of zero —
    /// it is a site that was not assessed, and leaving it behind would make a cleared
    /// mis-tap count towards "sites probed" for ever.
    /// </remarks>
    private async Task SaveOrRemoveSiteAsync(PerioSiteReading reading, CancellationToken ct)
    {
        var isEmpty = reading.ProbingDepthMm is null
            && reading.RecessionMm is null
            && !reading.Bleeding
            && !reading.Suppuration;

        if (isEmpty)
        {
            if (reading.Id != Guid.Empty)
            {
                await _sites.DeleteAsync(reading.Id, ct).ConfigureAwait(false);
            }

            return;
        }

        await _sites.SaveAsync(reading, ct).ConfigureAwait(false);
    }

    private async Task<PerioToothReading> FindOrCreateToothAsync(
        Guid examId, string toothNumber, CancellationToken ct)
    {
        var existing = await _teeth
            .FindAsync(reading => reading.PerioExamId == examId
                && reading.ToothNumber == toothNumber, ct)
            .ConfigureAwait(false);

        return existing ?? new PerioToothReading
        {
            PerioExamId = examId,
            ToothNumber = toothNumber,
        };
    }

    private async Task SaveOrRemoveToothAsync(PerioToothReading reading, CancellationToken ct)
    {
        var isEmpty = reading.Mobility is null
            && reading.Furcation is null
            && !reading.Plaque
            && string.IsNullOrWhiteSpace(reading.Notes);

        if (isEmpty)
        {
            if (reading.Id != Guid.Empty)
            {
                await _teeth.DeleteAsync(reading.Id, ct).ConfigureAwait(false);
            }

            return;
        }

        await _teeth.SaveAsync(reading, ct).ConfigureAwait(false);
    }
}
