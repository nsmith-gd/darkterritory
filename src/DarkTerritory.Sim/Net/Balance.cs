using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Net;

/// <summary>Mirror of content/tuning/balance.json: GDD §34's sweep targets. Field docs live in that file.</summary>
public sealed record BalanceTuning(int SurvivableCrew, double SurvivableDelivered, int NonTrivialCrew, double NonTrivialPunishes,
    double MaxQuietSeconds = 30, double MeanQuietSeconds = 20)
{
    public const string File = "tuning/balance.json";
    /// <summary>GDD §34's combination fairness sweep (note 186).</summary>
    public CombinationTuning Combinations { get; init; } = new();
    /// <summary>GDD §34's degraded comms: named voice conditions the harness can put the crew's calls through (note 186).</summary>
    public IReadOnlyDictionary<string, Bots.VoiceConditions> Comms { get; init; } = new Dictionary<string, Bots.VoiceConditions>();
    /// <summary>GDD §34's cascade audit: how long each recovery may take (note 186).</summary>
    public Audit.CascadeTuning Cascades { get; init; } = new();
    /// <summary>App. A.9 / B.10's per-tree GRAB check: the crew sizes it's run at, and where the friends stand (note 186).</summary>
    public Audit.GrabAuditTuning Grabs { get; init; } = new();
    /// <summary>GDD App. F.1's solo target: a few runs, then friends (note 300).</summary>
    public SoloTuning Solo { get; init; } = new();
    /// <summary>The orchestrator's pacing targets (orchestrator.md §4; note 379).</summary>
    public PacingTargets Pacing { get; init; } = new();
}

/// <summary>
/// The orchestrator's pacing targets (docs/design/orchestrator.md §4; note 379): P1, nobody slack past the census's press for
/// long; P3, something engaged for this share of the night on the line. Advisory checks don't fail the sweep. Field docs
/// live in balance.json.
/// </summary>
public sealed record PacingTargets
{
    public bool Advisory { get; init; } = true;
    public double SlackOverSeconds { get; init; } = 30;
    public double[] Engaged { get; init; } = [0.45, 0.60];
}

/// <summary>
/// GDD App. F.1 (the director, 6 Oct 2026): a solo player finishes one to three runs, then it's seriously hard. Judged on a
/// sweep's crew-of-one nights: the first runs' cell (tier and train) delivered at least <see cref="StartDelivered"/>, the
/// cell a crew advances to at most <see cref="HardDelivered"/> (note 300). Field docs live in balance.json.
/// </summary>
public sealed record SoloTuning(string StartTier = "local", int StartCars = 3, double StartDelivered = 0.66,
    string HardTier = "frontier", int HardCars = 6, double HardDelivered = 0.34);

/// <summary>One night of a sweep: a route, the crew's size, the train's length.</summary>
public sealed record BalanceNight(RouteTier Tier, ulong Seed, int Crew, int Cars);

/// <summary>How a night went, in the terms the sweep is judged by.</summary>
public sealed record BalanceRow(RouteTier Tier, ulong Seed, int Crew, int Cars, string End, bool Delivered, double Net, int CrewLost,
    IReadOnlyDictionary<string, int> DeathsByCause, int Punishes, int FairnessViolations, double Seconds, double DistanceKm,
    double LongestQuietSeconds = 0, double MeanQuietSeconds = 0)
{
    /// <summary>The most seconds any crewmate spent at or past the census's slackPress (note 345); 0 with no census.</summary>
    public double SlackOver { get; init; }
    /// <summary>The share of the night out on the line that was quiet (the harness's pacing quietShare).</summary>
    public double QuietShare { get; init; }
}

/// <summary>The nights with one crew size (or train length) together.</summary>
public sealed record BalanceGroup(int Value, int Nights, double DeliveredRate, double MeanNet, double MeanCrewLost, double MeanPunishes);

/// <param name="Advisory">Reported but never failing the sweep (note 379's pacing targets, until the director sets them).</param>
public sealed record BalanceCheck(string Name, bool Pass, string Detail, bool Advisory = false);

public sealed record BalanceReport(IReadOnlyList<BalanceRow> Nights, IReadOnlyList<BalanceGroup> ByCrew, IReadOnlyList<BalanceGroup> ByCars,
    IReadOnlyList<BalanceCheck> Checks, bool Pass);

/// <summary>
/// GDD §34's balance sweeps over harness nights (roadmap M7 "balance sweeps"): the crew-size sweep ("survivable at 2,
/// non-trivial at 8"), the train-length sweep ("where is the real progression cap?", reported, not judged: that's the
/// director's call), and App. A.1's fairness contract on every night. `dt balance` runs the nights; this judges them.
/// </summary>
public static class Balance
{
    /// <summary>The sweep's grid: every tier and seed, at every crew size and train length.</summary>
    public static IReadOnlyList<BalanceNight> Grid(IEnumerable<RouteTier> tiers, IEnumerable<ulong> seeds, IEnumerable<int> crews, IEnumerable<int> cars) =>
        [.. from t in tiers from s in seeds from c in crews from n in cars select new BalanceNight(t, s, c, n)];

    public static BalanceRow Row(BalanceNight n, HarnessReport r)
    {
        var run = r.Run;
        var threats = r.Threats;
        return new BalanceRow(n.Tier, n.Seed, n.Crew, n.Cars, run?.End.ToString() ?? "none", run?.End == Run.RunEnd.Delivered,
            run?.Net ?? 0, run?.CrewLost ?? r.Deaths, threats?.DeathsByCause ?? new Dictionary<string, int>(),
            threats?.Punishes.Values.Sum() ?? 0, threats?.FairnessViolations ?? 0, r.Seconds, run?.DistanceKm ?? r.TrainDistance / 1000,
            r.Pacing?.LongestQuietSeconds ?? 0, r.Pacing?.MeanQuietSeconds ?? 0)
        {
            SlackOver = threats?.Slack.Values.Select(v => v.Over).DefaultIfEmpty(0).Max() ?? 0,
            QuietShare = r.Pacing?.QuietShare ?? 0,
        };
    }

    public static BalanceReport Judge(IReadOnlyList<BalanceRow> rows, BalanceTuning t)
    {
        static IReadOnlyList<BalanceGroup> By(IEnumerable<BalanceRow> rows, Func<BalanceRow, int> key) =>
            [.. rows.GroupBy(key).OrderBy(g => g.Key).Select(g => new BalanceGroup(g.Key, g.Count(),
                Math.Round(g.Average(r => r.Delivered ? 1.0 : 0.0), 3), Math.Round(g.Average(r => r.Net)), Math.Round(g.Average(r => (double)r.CrewLost), 2),
                Math.Round(g.Average(r => (double)r.Punishes), 1)))];
        var byCrew = By(rows, r => r.Crew);
        var byCars = By(rows, r => r.Cars);
        var checks = new List<BalanceCheck>();
        if (byCrew.FirstOrDefault(g => g.Value == t.SurvivableCrew) is { } small)
            checks.Add(new BalanceCheck($"survivable at {t.SurvivableCrew}", small.DeliveredRate >= t.SurvivableDelivered,
                $"{small.DeliveredRate:P0} of {small.Nights} nights delivered (at least {t.SurvivableDelivered:P0})"));
        if (byCrew.FirstOrDefault(g => g.Value == t.NonTrivialCrew) is { } large)
            checks.Add(new BalanceCheck($"non-trivial at {t.NonTrivialCrew}", large.MeanPunishes >= t.NonTrivialPunishes,
                $"{large.MeanPunishes} punishes a night (at least {t.NonTrivialPunishes})"));
        // The pace (after the playtest): never quiet longer than MaxQuietSeconds, and quiet stretches MeanQuietSeconds on average.
        if (rows.Count > 0)
        {
            var slowest = rows.MaxBy(r => r.LongestQuietSeconds)!;
            checks.Add(new BalanceCheck($"never quiet over {t.MaxQuietSeconds:0} s", slowest.LongestQuietSeconds <= t.MaxQuietSeconds,
                $"longest {slowest.LongestQuietSeconds} s ({slowest.Tier}:{slowest.Seed}, crew {slowest.Crew})"));
            double mean = rows.Average(r => r.MeanQuietSeconds);
            checks.Add(new BalanceCheck($"quiet {t.MeanQuietSeconds:0} s at a time, or less", mean <= t.MeanQuietSeconds, $"{mean:0.0} s on average"));
        }
        // App. F.1, solo (note 300): the first runs can be finished alone; where a crew goes next can't, really.
        var solo = t.Solo;
        IReadOnlyList<BalanceRow> Alone(string tier, int cars) =>
            [.. rows.Where(r => r.Crew == 1 && r.Cars == cars && r.Tier == Enum.Parse<RouteTier>(tier, ignoreCase: true))];
        static double Rate(IReadOnlyList<BalanceRow> nights) => Math.Round(nights.Average(r => r.Delivered ? 1.0 : 0.0), 3);
        if (Alone(solo.StartTier, solo.StartCars) is { Count: > 0 } first)
            checks.Add(new BalanceCheck($"solo finishes {solo.StartTier} at {solo.StartCars} cars", Rate(first) >= solo.StartDelivered,
                $"{Rate(first):P0} of {first.Count} nights delivered (at least {solo.StartDelivered:P0})"));
        if (Alone(solo.HardTier, solo.HardCars) is { Count: > 0 } hard)
            checks.Add(new BalanceCheck($"solo is hard on {solo.HardTier} at {solo.HardCars} cars", Rate(hard) <= solo.HardDelivered,
                $"{Rate(hard):P0} of {hard.Count} nights delivered (at most {solo.HardDelivered:P0})"));
        // The orchestrator's pacing targets (orchestrator.md §4; note 379), on crews the census steers (two or more).
        var p = t.Pacing;
        if (rows.Where(r => r.Crew >= 2).ToList() is { Count: > 0 } crewed)
        {
            var slackest = crewed.MaxBy(r => r.SlackOver)!;
            checks.Add(new BalanceCheck($"P1: nobody slack for long (≤ {p.SlackOverSeconds:0} s past the press)", slackest.SlackOver <= p.SlackOverSeconds,
                $"most {slackest.SlackOver:0} s ({slackest.Tier}:{slackest.Seed}, crew {slackest.Crew})", p.Advisory));
            double engaged = crewed.Average(r => 1 - r.QuietShare);
            checks.Add(new BalanceCheck($"P3: something engaged {p.Engaged[0]:P0}–{p.Engaged[1]:P0} of the night", engaged >= p.Engaged[0] && engaged <= p.Engaged[1],
                $"{engaged:P0} on average", p.Advisory));
        }
        int unfair = rows.Sum(r => r.FairnessViolations);
        checks.Add(new BalanceCheck("fair (App. A.1)", unfair == 0, $"{unfair} commits without the reaction window"));
        return new BalanceReport(rows, byCrew, byCars, checks, checks.All(c => c.Pass || c.Advisory));
    }
}
