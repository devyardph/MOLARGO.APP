using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Shared.Data;

/// <summary>
/// The example patient's periodontal history.
/// </summary>
/// <remarks>
/// Two completed exams rather than one, because a single exam demonstrates nothing: the
/// perio pane exists to show whether treatment worked, and that needs a before and an
/// after. Today is deliberately left without an exam so the "start an exam" path is the
/// first thing a new reader meets.
/// </remarks>
internal static partial class SampleData
{
    /// <summary>
    /// Margaret Yuen's perio exams: a baseline, then a re-probe after scaling.
    /// </summary>
    /// <remarks>
    /// Generated from a per-tooth baseline plus a short list of named problem sites, not
    /// typed out site by site. 32 teeth × 6 sites × 2 exams is 384 readings, and a hand
    /// list that long is unreadable and impossible to keep consistent between the two
    /// exams — which is exactly the property the comparison depends on.
    /// </remarks>
    private static (List<PerioExam> Exams, List<PerioSiteReading> Sites, List<PerioToothReading> Teeth)
        Perio(DateOnly today)
    {
        var exams = new List<PerioExam>();
        var sites = new List<PerioSiteReading>();
        var teeth = new List<PerioToothReading>();

        // The baseline, before scaling and root planing.
        var baseline = new PerioExam
        {
            Id = Id("perio:baseline"),
            PatientId = RecordPatient,
            ProviderId = Id("provider:vance"),
            ExamDate = today.AddMonths(-7),
            Notes = "Baseline full-mouth charting. Generalised 4mm posteriors, "
                + "localised 6mm at 16. S&P planned over two visits.",
            CompletedUtc = today.AddMonths(-7).ToDateTime(new TimeOnly(10, 40), DateTimeKind.Local)
                .ToUniversalTime(),
        };

        // The re-probe after treatment. The same teeth, measurably better.
        var review = new PerioExam
        {
            Id = Id("perio:review"),
            PatientId = RecordPatient,
            ProviderId = Id("provider:ito"),
            ExamDate = today.AddMonths(-2),
            Notes = "Re-probe 3 months post S&P. Bleeding much reduced. "
                + "16 mesial still 5mm — 3-monthly maintenance.",
            CompletedUtc = today.AddMonths(-2).ToDateTime(new TimeOnly(9, 20), DateTimeKind.Local)
                .ToUniversalTime(),
        };

        exams.Add(baseline);
        exams.Add(review);

        // Sites that are worse than the tooth's baseline, per exam. Everything else takes
        // the baseline depth for its tooth type.
        var baselineProblems = new Dictionary<(string Fdi, PerioSite Site), int>
        {
            [("16", PerioSite.MesioBuccal)] = 6,
            [("16", PerioSite.MesioLingual)] = 6,
            [("16", PerioSite.DistoBuccal)] = 5,
            [("17", PerioSite.DistoBuccal)] = 5,
            [("26", PerioSite.MesioLingual)] = 5,
            [("36", PerioSite.DistoLingual)] = 5,
            [("46", PerioSite.MesioBuccal)] = 5,
        };

        var reviewProblems = new Dictionary<(string Fdi, PerioSite Site), int>
        {
            [("16", PerioSite.MesioBuccal)] = 5,
            [("16", PerioSite.MesioLingual)] = 4,
            [("17", PerioSite.DistoBuccal)] = 4,
            [("26", PerioSite.MesioLingual)] = 4,
        };

        foreach (var sextant in Enum.GetValues<PerioSextant>())
        {
            foreach (var fdi in PerioSites.TeethIn(sextant))
            {
                // 18 and 38 are charted as extracted, so they are not probed. A reading on
                // a tooth the odontogram says is missing is the kind of contradiction that
                // makes a whole chart untrustworthy.
                if (fdi is "18" or "38") continue;

                foreach (var site in PerioSites.ProbingOrder)
                {
                    // The baseline carries a generalised extra millimetre round the
                    // molars, not just the seven named sites. Without it both exams
                    // averaged 3.0mm and the comparison bars came out identical — which
                    // is the one thing that panel exists not to do.
                    sites.Add(Reading(
                        baseline.Id, fdi, site, baselineProblems, bleedFrom: 4, molarExtra: 1));

                    sites.Add(Reading(
                        review.Id, fdi, site, reviewProblems, bleedFrom: 5, molarExtra: 0));
                }
            }
        }

        // Recession at 33, matching the odontogram's "2mm recession, stable since Feb".
        foreach (var site in PerioSites.BuccalSites)
        {
            Recede(sites, baseline.Id, "33", site, 2);
            Recede(sites, review.Id, "33", site, 2);
        }

        teeth.Add(new PerioToothReading
        {
            Id = Id("perio:tooth:baseline-16"),
            PerioExamId = baseline.Id,
            ToothNumber = "16",
            Mobility = ToothMobility.Grade1,
            Furcation = FurcationGrade.Grade1,
            Plaque = true,
        });

        teeth.Add(new PerioToothReading
        {
            Id = Id("perio:tooth:review-16"),
            PerioExamId = review.Id,
            ToothNumber = "16",
            Mobility = ToothMobility.Grade1,
            Furcation = FurcationGrade.Grade1,
            Notes = "Furcation unchanged; mobility stable.",
        });

        return (exams, sites, teeth);
    }

    private static PerioSiteReading Reading(
        Guid examId,
        string fdi,
        PerioSite site,
        IReadOnlyDictionary<(string, PerioSite), int> problems,
        int bleedFrom,
        int molarExtra)
    {
        var depth = problems.TryGetValue((fdi, site), out var deep)
            ? deep
            : BaselineDepth(fdi, site) + MolarExtra(fdi, site, molarExtra);

        return new PerioSiteReading
        {
            Id = Id($"perio:site:{examId:N}:{fdi}:{(int)site}"),
            PerioExamId = examId,
            ToothNumber = fdi,
            Site = site,
            ProbingDepthMm = depth,

            // Bleeding follows depth rather than being sprinkled at random: it is what
            // makes the two exams comparable, and the improvement after scaling is the
            // whole thing this data exists to show.
            Bleeding = depth >= bleedFrom,
            Suppuration = depth >= 6,
        };
    }

    /// <summary>
    /// The depth a healthy-ish site of this tooth would read.
    /// </summary>
    /// <remarks>
    /// Molars deeper than anteriors, and the interproximal sites deeper than the mid-face
    /// — which is how a real mouth probes, and what makes the generated chart look like a
    /// chart rather than a grid of one number.
    /// </remarks>
    private static int BaselineDepth(string fdi, PerioSite site)
    {
        var position = fdi[1];

        var tooth = position switch
        {
            '6' or '7' or '8' => 3,
            '4' or '5' => 2,
            _ => 2,
        };

        var interproximal = site is PerioSite.MesioBuccal or PerioSite.DistoBuccal
            or PerioSite.MesioLingual or PerioSite.DistoLingual;

        return interproximal ? tooth + 1 : tooth;
    }

    /// <summary>
    /// The generalised loss round a molar, which is where untreated disease shows first.
    /// </summary>
    /// <remarks>
    /// Interproximal sites of molars only. Applying it everywhere would push the whole
    /// mouth to 5mm and report a patient in far worse shape than the notes describe.
    /// </remarks>
    private static int MolarExtra(string fdi, PerioSite site, int extra)
    {
        if (extra == 0 || fdi[1] is not ('6' or '7' or '8')) return 0;

        return site is PerioSite.Buccal or PerioSite.Lingual ? 0 : extra;
    }

    private static void Recede(
        List<PerioSiteReading> sites, Guid examId, string fdi, PerioSite site, int millimetres)
    {
        var reading = sites.FirstOrDefault(candidate => candidate.PerioExamId == examId
            && candidate.ToothNumber == fdi
            && candidate.Site == site);

        if (reading is not null) reading.RecessionMm = millimetres;
    }
}
