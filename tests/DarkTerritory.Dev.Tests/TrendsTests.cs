using System.Text.Json.Nodes;
using Ballast;
using DarkTerritory.Dev.Trends;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Dev.Tests;

/// <summary>
/// ARCHITECTURE §8 note 523: a trends line is what a commit's nights measured, a fall is a step past its band the bad way
/// (once, not on every line after it), the bisect calls a commit by the nearer end, and a measure gives every metric the
/// bands know, with sane values.
/// </summary>
public class TrendsTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static readonly TrendBands Bands = new()
    {
        Window = 5,
        MinHistory = 3,
        Metrics =
        [
            new TrendBand { Match = "balance.km", Bad = Worse.Down, Rel = 0.15, Abs = 1 },
            new TrendBand { Match = "balance.deaths", Bad = Worse.Up, Abs = 1 },
            new TrendBand { Match = "balance.grabs", Bad = Worse.Either, Abs = 1.5 },
            new TrendBand { Match = "perf.*.triangles", Bad = Worse.Up, Rel = 0.1 },
        ],
    };

    /// <summary>A commit's line with these metrics (and a seed's night each, these over again), on the trends' nights.</summary>
    static TrendLine Line(int n, Dictionary<string, double> metrics, TrendNights? nights = null) => new()
    {
        Commit = $"{n:x8}{new string('0', 32)}",
        Subject = $"Commit {n} (#{600 + n})",
        Pr = 600 + n,
        Options = nights ?? new TrendNights(),
        Metrics = metrics,
        Seeds = [new TrendSeed(1, 1, metrics)],
    };

    static List<TrendLine> Series(string metric, params double[] values) =>
        [.. values.Select((v, i) => Line(i, new Dictionary<string, double> { [metric] = v }))];

    [Fact]
    public void AStepPastItsBandTheBadWayIsAFall()
    {
        // km held about 19 (the band: 15 % of the median, 2.9 km), then 14.
        var fell = Series("balance.km", 19, 19.5, 18.8, 19.2, 14);
        var step = Assert.Single(TrendSeries.Steps(fell, Bands));
        Assert.True(step.Fall);
        Assert.Equal(4, fell.IndexOf(step.Line));
        Assert.Equal(19.1, step.Level, 6);
        Assert.Equal(0.15 * 19.1, step.Width, 6);
        Assert.Equal(19.2, step.PreviousValue);

        // Inside the band, nothing; the same distance the good way is a rise, not a fall.
        Assert.Empty(TrendSeries.Steps(Series("balance.km", 19, 19.5, 18.8, 19.2, 17), Bands));
        var rose = Assert.Single(TrendSeries.Steps(Series("balance.km", 19, 19.5, 18.8, 19.2, 24), Bands));
        Assert.False(rose.Fall);

        // Deaths are worse up: two more a night is a fall, two fewer a rise.
        Assert.True(Assert.Single(TrendSeries.Steps(Series("balance.deaths", 2, 2, 1.5, 2, 4), Bands)).Fall);
        Assert.False(Assert.Single(TrendSeries.Steps(Series("balance.deaths", 2, 2, 2.5, 2, 0), Bands)).Fall);
    }

    [Fact]
    public void AMetricThatStaysDownFellOnceNotOnEveryLineAfter()
    {
        var lines = Series("balance.km", 19, 19, 19, 19, 14, 14.2, 13.9, 14.1);
        var step = Assert.Single(TrendSeries.Steps(lines, Bands));
        Assert.True(step.Fall);
        Assert.Equal(4, lines.IndexOf(step.Line));
        // And a slow slide falls on the step that takes it past its band, not before.
        var slide = Series("balance.km", 19, 19, 19, 19, 18, 17, 16, 15.5);
        Assert.Equal(6, slide.IndexOf(Assert.Single(TrendSeries.Steps(slide, Bands)).Line));
    }

    [Fact]
    public void AMetricWorseBothWaysFallsBothWaysAndAnUntrackedOneNever()
    {
        Assert.True(Assert.Single(TrendSeries.Steps(Series("balance.grabs", 3, 3, 3, 3, 6), Bands)).Fall);
        Assert.True(Assert.Single(TrendSeries.Steps(Series("balance.grabs", 3, 3, 3, 3, 0), Bands)).Fall);
        // A metric no band names is tracked, never judged.
        Assert.Empty(TrendSeries.Steps(Series("balance.rescues", 3, 3, 3, 3, 30), Bands));
    }

    [Fact]
    public void AStepWithTooLittleBeforeItIsNotJudged()
    {
        Assert.Empty(TrendSeries.Steps(Series("balance.km", 19, 19, 5), Bands));
        Assert.Single(TrendSeries.Steps(Series("balance.km", 19, 19, 19, 5), Bands));
        // A line without the metric (an older build's) is skipped, not taken for a zero.
        var lines = Series("balance.km", 19, 19, 19, 19);
        lines.Insert(2, Line(9, new Dictionary<string, double> { ["balance.deaths"] = 1 }));
        Assert.Empty(TrendSeries.Steps(lines, Bands));
    }

    [Fact]
    public void APatternBandsEveryMetricItMatches()
    {
        Assert.Equal(Worse.Up, Bands.For("perf.vr.roof.triangles").Bad);
        Assert.Equal(Worse.None, Bands.For("perf.vr.roof.draws").Bad);
        var lines = Series("perf.pc.cab.triangles", 957_000, 957_100, 956_900, 957_000, 1_100_000);
        Assert.True(Assert.Single(TrendSeries.Steps(lines, Bands)).Fall);
    }

    [Fact]
    public void LinesOnOtherNightsStartTheirOwnSeriesAndACommitCountsOnce()
    {
        var shorter = TrendNights.Of("frontier:7", 4, 10, 900, [1, 2, 3], perf: false);
        var lines = Series("balance.km", 19, 19, 19);
        lines.Insert(0, Line(7, new Dictionary<string, double> { ["balance.km"] = 9 }, shorter));
        lines.Add(lines[^1] with { Metrics = new Dictionary<string, double> { ["balance.km"] = 19.1 } });
        var series = TrendSeries.Comparable(lines);
        Assert.Equal(3, series.Count);
        Assert.Equal(19.1, series[^1].Metrics["balance.km"]);
        Assert.DoesNotContain(series, l => l.Options.Seconds == 900);
    }

    [Fact]
    public void TheBisectCallsACommitByTheNearerEnd()
    {
        // km fell from 19 to 14: the line is drawn at 16.5, and a commit on it has taken its share of the fall.
        Assert.False(TrendBisect.IsBad(18.2, good: 19, bad: 14));
        Assert.True(TrendBisect.IsBad(15.1, good: 19, bad: 14));
        Assert.True(TrendBisect.IsBad(16.5, good: 19, bad: 14));
        Assert.False(TrendBisect.IsBad(16.6, good: 19, bad: 14));
        // Deaths rose from 1 to 3.
        Assert.True(TrendBisect.IsBad(2.5, good: 1, bad: 3));
        Assert.False(TrendBisect.IsBad(1.4, good: 1, bad: 3));
        Assert.Throws<ArgumentException>(() => TrendBisect.IsBad(2, good: 2, bad: 2));
    }

    [Fact]
    public void TheCommentGivesTheNumbersAndEndsWithTheFooter()
    {
        var lines = Series("balance.km", 19, 19.5, 18.8, 19.2, 14);
        string comment = Assert.IsType<string>(TrendMarkdown.Comment(lines, Bands, "https://example.invalid/run/1"));
        Assert.Contains("`balance.km`", comment);
        Assert.Contains("**14**", comment);
        Assert.Contains($"--good {lines[3].Short} --bad {lines[4].Short}", comment);
        Assert.EndsWith("\n---\n_Generated by [Claude Code](https://claude.ai/code)_\n", comment);
        Assert.Null(TrendMarkdown.Comment(Series("balance.km", 19, 19.5, 18.8, 19.2, 19), Bands));
        // The summary bolds the fall where it is.
        Assert.Contains("| **14** |", TrendMarkdown.Summary(lines, Bands, 10));
    }

    [Fact]
    public void ALineReadsBackAndASubjectNamesItsPullRequest()
    {
        var line = Line(3, new Dictionary<string, double> { ["balance.km"] = 19.25, ["perf.pc.triangles"] = 1_095_781 });
        string json = line.ToJson();
        Assert.DoesNotContain('\n', json);
        var back = TrendLine.FromJson(json);
        Assert.Equal(line.Commit, back.Commit);
        Assert.Equal(line.Metrics, back.Metrics);
        Assert.Equal(line.Options.Key, back.Options.Key);
        Assert.Equal(684, TrendLine.PrOf("Coordination docs merge on their own (queue #258, note 521) (#684)"));
        Assert.Equal(688, TrendLine.PrOf("Merge pull request #688 from nsmith-gd/claude/relaxed-franklin-xkfjgb"));
        Assert.Null(TrendLine.PrOf("Claim queue #292 (note 550): the train stands on a spur (seed 8)"));
        Assert.Equal(new[] { 1, 2, 3, 9 }, TrendNights.ParseSeeds("1-3,9"));
        Assert.Equal("1-8", TrendNights.Range(TrendNights.ParseSeeds("1-8")));
    }

    [Fact]
    public void PerfGivesEachViewsCountsAndTheCpuPhases()
    {
        var perf = JsonNode.Parse("""
            {"pc": {"worstCpuMs": 3.4, "views": [
              {"view": "roof", "triangles": 1084903, "drawsPerFrame": 374, "cpuMs": 2.66, "buildMs": 1.67, "prepareMs": 0.54, "recordMs": 0.45},
              {"view": "cab", "triangles": 957098, "drawsPerFrame": 264, "cpuMs": 3.22, "buildMs": 2.07, "prepareMs": 0.76, "recordMs": 0.39}]},
             "vr": null}
            """)!;
        var m = TrendMetrics.FromPerf(perf);
        Assert.Equal(1_084_903, m["perf.pc.roof.triangles"]);
        Assert.Equal(264, m["perf.pc.cab.draws"]);
        Assert.Equal(1_084_903, m["perf.pc.triangles"]);
        Assert.Equal(3.4, m["perf.pc.cpuMs"]);
        Assert.Equal(1.87, m["perf.pc.buildMs"], 6);
        Assert.DoesNotContain(m.Keys, k => k.StartsWith("perf.vr", StringComparison.Ordinal));
    }

    [Fact]
    public void AShortMeasureGivesEveryMetricItsBandAndSaneValues()
    {
        var nights = TrendNights.Of("frontier:7", 4, 6, 10, [1], perf: false);
        var line = TrendMeasure.Run(nights, new TrendCommit("abc123", "A commit (#5)", false), "abc123", seed => Night(nights, seed), null, 1, null,
            new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc));
        Assert.Equal(5, line.Pr);
        Assert.Equal(1, Assert.Single(line.Seeds).Seed);
        var m = line.Metrics;
        string[] expected = ["balance.km", "balance.deaths", "balance.delivered", "balance.carsLost", "balance.carsDelivered", "balance.cargo",
            "balance.net", "balance.crewHome", "balance.grabs", "balance.fairness", "pacing.beatsPerMinute", "pacing.longestQuietSeconds",
            "net.downKbps", "net.maxCorrectionM", "sim.cpuMsPerTick"];
        foreach (string name in expected)
            Assert.Contains(name, m.Keys);
        Assert.DoesNotContain(m.Keys, k => k.StartsWith("moments.", StringComparison.Ordinal));
        // Ten seconds out of the yard: under way, nobody dead, nothing lost, nobody home yet, the whole crew aboard.
        Assert.InRange(m["balance.km"], 0.1, 5);
        Assert.Equal(0, m["balance.deaths"]);
        Assert.Equal(0, m["balance.carsLost"]);
        Assert.Equal(0, m["balance.delivered"]);
        Assert.Equal(4, m["balance.crewHome"]);
        Assert.Equal(0, m["balance.fairness"]);
        Assert.InRange(m["net.downKbps"], 1, 500);
        Assert.InRange(m["sim.cpuMsPerTick"], 1e-6, 1e3);
        // The same night again is the same numbers (the bisect counts on it), and the line reads back as it was written.
        var again = TrendMeasure.Run(nights, new TrendCommit("abc123", null, false), "abc123", seed => Night(nights, seed), null, 1, null, DateTime.UtcNow);
        Assert.Equal(line.Metrics.Where(kv => !kv.Key.StartsWith("sim.", StringComparison.Ordinal)),
            again.Metrics.Where(kv => !kv.Key.StartsWith("sim.", StringComparison.Ordinal)));
        Assert.Equal(line.Metrics, TrendLine.FromJson(line.ToJson()).Metrics);
        // Every metric a night measures has a band of its own in tools/trends/metrics.json.
        var bands = TrendBands.Load(TrendBands.DefaultPath(Content));
        foreach (string name in m.Keys)
            Assert.Contains(bands.Metrics, b => b.Matches(name));
    }

    /// <summary>A short night as `dt harness` runs one on a route (Program.cs RunHarness): the run, the enemies and the upkeep on.</summary>
    static JsonNode Night(TrendNights nights, int seed)
    {
        var train = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var run = DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File));
        var route = Routes.Generate(Content, nights.Route, nights.Cars);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var report = Harness.Run(route.Build(), train, DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File)), new HarnessOptions
        {
            Bots = nights.Bots,
            Cars = nights.Cars,
            Seconds = nights.Seconds,
            Seed = seed,
            StartDistance = run.DepartFrom(gate, Consist.Uniform(train, nights.Cars, 1).LengthMetres),
            Combat = DataFile.Load<CombatTuning>(Path.Combine(Content, CombatTuning.File)),
            Enemies = DataFile.Load<EnemyTuning>(Path.Combine(Content, EnemyTuning.File)),
            Upkeep = DataFile.Load<UpkeepTuning>(Path.Combine(Content, UpkeepTuning.File)),
            Route = route,
            Run = run,
            Facilities = DataFile.Load<FacilityTuning>(Path.Combine(Content, FacilityTuning.File)),
            Holdouts = DataFile.Load<HoldoutTuning>(Path.Combine(Content, HoldoutTuning.File)),
            Sight = DataFile.Load<SightTuning>(Path.Combine(Content, SightTuning.File)),
            YardLength = gate,
        }, DataFile.Load<BoilerTuning>(Path.Combine(Content, BoilerTuning.File)));
        return TrendMeasure.AsPrinted(report, DataFile.Options);
    }
}
