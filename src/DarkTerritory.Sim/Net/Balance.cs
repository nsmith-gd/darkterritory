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
    /// <summary>What counts as a shared problem, and how many stops should have one (note 861).</summary>
    public SharedTuning Shared { get; init; } = new();
    /// <summary>App. B.10's want balance: how near its share each want's spend lands (note 1032).</summary>
    public WantBalanceTuning WantBalance { get; init; } = new();
    /// <summary>App. B.10's tier progression: the harder tier against the easier at matched crew and cars (note 1032).</summary>
    public TierProgressionTuning Tiers { get; init; } = new();
}

/// <summary>GDD App. B.10 "Want balance" (note 1032). Field docs live in balance.json.</summary>
public sealed record WantBalanceTuning(double Tolerance = 0.10, double MinSpent = 40);

/// <summary>GDD App. B.10 "Tier progression" (note 1032). Field docs live in balance.json.</summary>
public sealed record TierProgressionTuning(string Easier = "local", string Harder = "deepTerritory", double PunishRatio = 1.25);

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
    /// <summary>The stops the train stood at, and those with a shared problem in them (note 861).</summary>
    public int Stops { get; init; }
    public int StopsWithShared { get; init; }
    /// <summary>What the director spent on each want tonight (App. B.1 v1.1; note 1032).</summary>
    public IReadOnlyDictionary<string, double> SpentByWant { get; init; } = new Dictionary<string, double>();
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
            Stops = r.Shared?.Stops.Count ?? 0,
            StopsWithShared = r.Shared?.StopsWithShared ?? 0,
            SpentByWant = threats?.SpentByWant ?? new Dictionary<string, double>(),
        };
    }

    /// <param name="wantShares">enemies.json <c>director.wantShares</c>, App. B.10's want balance's targets; none, not judged.</param>
    public static BalanceReport Judge(IReadOnlyList<BalanceRow> rows, BalanceTuning t, IReadOnlyDictionary<string, double>? wantShares = null)
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
        // Note 861 (the director's playtest of 16 Oct evening, line 8): a crew's stops give them something to share.
        if (rows.Where(r => r.Crew >= 2).Sum(r => r.Stops) is > 0 and var stops)
        {
            double share = (double)rows.Where(r => r.Crew >= 2).Sum(r => r.StopsWithShared) / stops;
            checks.Add(new BalanceCheck($"stops shared (≥ {t.Shared.StopsWithShared:P0} with a problem for two)", share >= t.Shared.StopsWithShared,
                $"{share:P0} of {stops} stops", Advisory: true));
        }
        if (wantShares is { Count: > 0 } && WantBalance(rows, wantShares, t.WantBalance) is { } wants)
            checks.Add(wants);
        checks.AddRange(TierProgression(rows, t.Tiers));
        int unfair = rows.Sum(r => r.FairnessViolations);
        checks.Add(new BalanceCheck("fair (App. A.1)", unfair == 0, $"{unfair} commits without the reaction window"));
        return new BalanceReport(rows, byCrew, byCars, checks, checks.All(c => c.Pass || c.Advisory));
    }

    /// <summary>
    /// App. B.10 "Want balance: generated runs land near the target shares" (note 1032): the sweep's spend by want, each
    /// want's share of it within the tolerance of its target. Null until the sweep has spent enough to have shares.
    /// </summary>
    public static BalanceCheck? WantBalance(IReadOnlyList<BalanceRow> rows, IReadOnlyDictionary<string, double> shares, WantBalanceTuning t)
    {
        var spent = rows.SelectMany(r => r.SpentByWant).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));
        double total = spent.Values.Sum();
        if (total < t.MinSpent)
            return null;
        double targets = shares.Values.Sum();
        var off = shares.Keys.Concat(spent.Keys).Distinct().Order(StringComparer.Ordinal)
            .Select(w => (Want: w, Share: spent.GetValueOrDefault(w) / total, Target: shares.GetValueOrDefault(w) / targets)).ToList();
        var worst = off.MaxBy(x => Math.Abs(x.Share - x.Target));
        return new BalanceCheck($"want balance (each want within {t.Tolerance:P0} of its share)", Math.Abs(worst.Share - worst.Target) <= t.Tolerance + 1e-9,
            string.Join(", ", off.Select(x => $"{x.Want} {x.Share:P0} (of {x.Target:P0})")) + $" over {total:0} spent");
    }

    /// <summary>
    /// App. B.10 "Tier progression: Deep territory is meaningfully harder than Local at matched crew and length" (note
    /// 1032): at each crew size and train length the sweep ran on both tiers, the harder gets home no more often and its
    /// threats land (punishes an hour) at least <see cref="TierProgressionTuning.PunishRatio"/> times as often. Nothing
    /// when the sweep has no cell on both.
    /// </summary>
    public static IEnumerable<BalanceCheck> TierProgression(IReadOnlyList<BalanceRow> rows, TierProgressionTuning t)
    {
        var easier = Enum.Parse<RouteTier>(t.Easier, ignoreCase: true);
        var harder = Enum.Parse<RouteTier>(t.Harder, ignoreCase: true);
        static (double Delivered, double PerHour, int Nights) Of(IEnumerable<BalanceRow> nights)
        {
            var list = nights.ToList();
            double hours = list.Sum(r => r.Seconds) / 3600;
            return (list.Average(r => r.Delivered ? 1.0 : 0.0), hours > 0 ? list.Sum(r => r.Punishes) / hours : 0, list.Count);
        }
        foreach (var cell in rows.GroupBy(r => (r.Crew, r.Cars)).OrderBy(g => g.Key.Crew).ThenBy(g => g.Key.Cars))
        {
            if (!cell.Any(r => r.Tier == easier) || !cell.Any(r => r.Tier == harder))
                continue;
            var e = Of(cell.Where(r => r.Tier == easier));
            var h = Of(cell.Where(r => r.Tier == harder));
            bool pass = h.Delivered <= e.Delivered && h.PerHour >= t.PunishRatio * e.PerHour && (h.PerHour > 0 || h.Delivered < e.Delivered);
            yield return new BalanceCheck($"{t.Harder} harder than {t.Easier} (crew {cell.Key.Crew}, {cell.Key.Cars} cars)", pass,
                $"delivered {h.Delivered:P0} against {e.Delivered:P0}; {h.PerHour:0.0} punishes an hour against {e.PerHour:0.0} (at least ×{t.PunishRatio}); {h.Nights} and {e.Nights} nights");
        }
    }
}
