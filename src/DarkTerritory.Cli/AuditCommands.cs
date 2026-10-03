using System.Globalization;
using System.Text.Json;
using Ballast;
using DarkTerritory.Sim.Audit;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

/// <summary>
/// GDD §34's verification harness gaps (note 186): `dt balance --pairs|--triples` (combination fairness), `dt audit cascades`
/// (no failure chain is unrecoverable) and `dt audit grabs` (App. A.9 / B.10: every GRAB interruptible by the crew present).
/// Each prints JSON and exits 1 if it found something.
/// </summary>
static class AuditCommands
{
    public static AuditContent Load(string content) => new(
        DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File)),
        DataFile.Load<PlayerTuning>(Path.Combine(content, PlayerTuning.File)),
        DataFile.Load<BoilerTuning>(Path.Combine(content, BoilerTuning.File)),
        DataFile.Load<CombatTuning>(Path.Combine(content, CombatTuning.File)),
        DataFile.Load<EnemyTuning>(Path.Combine(content, EnemyTuning.File)),
        DataFile.Load<DarkTerritory.Sim.Run.RunTuning>(Path.Combine(content, DarkTerritory.Sim.Run.RunTuning.File)),
        DataFile.Load<BalanceTuning>(Path.Combine(content, BalanceTuning.File)));

    /// <summary>dt audit cascades|grabs.</summary>
    public static int Audit(string content, string verb, string[] args)
    {
        var c = Load(content);
        switch (verb)
        {
            case "cascades":
                {
                    var only = Str(args, "--only", "") is { Length: > 0 } o ? o.Split(',') : null;
                    using var trace = Str(args, "--trace", "") is { Length: > 0 } path ? new StreamWriter(path) : null;
                    var report = CascadeAudit.Run(c, only, (int)Opt(args, "--seed", 1), trace);
                    Print(report);
                    return report.Pass ? 0 : 1;
                }
            case "grabs":
                {
                    var kinds = Str(args, "--only", "") is { Length: > 0 } o ? o.Split(',').Select(k => Enum.Parse<EnemyKind>(k, ignoreCase: true)) : null;
                    var crews = Str(args, "--crews", "") is { Length: > 0 } cr ? cr.Split(',').Select(int.Parse) : null;
                    var report = GrabAudit.Run(c, kinds, crews);
                    // The table is the point: one line a check, then the verdicts.
                    Print(new
                    {
                        report.Pass,
                        report.Uninterruptible,
                        report.NeverGrabbed,
                        checks = report.Checks.Select(x => $"{x.Kind,-12} crew {x.Crew}: {(x.Grabbed ? x.Freed ? $"freed ({x.FreedBy})" : "NOT FREED" : "NO GRAB")} {x.Detail}"),
                    });
                    return report.Pass ? 0 : 1;
                }
            default:
                Console.Error.WriteLine($"audit {verb}? (cascades, grabs)");
                return 2;
        }
    }

    /// <summary>
    /// dt balance --pairs|--triples [--every-hazard] [--at-stops] [--sample n] [--seeds n] [--seconds s] [--crew n] [--hazards clear,wet]
    /// [--only a,b] [--parallel p]: GDD §34's combination fairness.
    /// </summary>
    public static int Combinations(string content, string[] args)
    {
        var c = Load(content);
        var t = c.Balance.Combinations;
        int size = args.Contains("--triples") ? 3 : 2;
        var roster = Str(args, "--only", "") is { Length: > 0 } o
            ? [.. o.Split(',').Select(k => Enum.Parse<EnemyKind>(k, ignoreCase: true))]
            : DarkTerritory.Sim.Net.Combinations.Roster;
        var combos = DarkTerritory.Sim.Net.Combinations.Of(roster, Math.Min(size, roster.Count), (int)Opt(args, "--sample", size == 3 ? t.TripleSample : 0),
            (ulong)Opt(args, "--sample-seed", 1));
        var hazards = Str(args, "--hazards", "") is { Length: > 0 } h ? [.. t.HazardSets.Where(x => h.Split(',').Contains(x.Name))] : t.HazardSets;
        // Every combination against every hazard set is the nightly's (--every-hazard). By default each combination meets one,
        // the sets taken in turn down the list, so every combination is run and every set is met, in a few minutes.
        bool every = args.Contains("--every-hazard");
        int seeds = (int)Opt(args, "--seeds", t.Seeds), crew = (int)Opt(args, "--crew", t.Crew), cars = (int)Opt(args, "--cars", t.Cars);
        double seconds = Opt(args, "--seconds", t.Seconds);
        var route = DarkTerritory.Sim.LineGen.Routes.Generate(content, Str(args, "--route", t.Route), cars);
        var routeTuning = RouteTuning.Load(content);
        var facilities = DataFile.Load<DarkTerritory.Sim.Run.FacilityTuning>(Path.Combine(content, DarkTerritory.Sim.Run.FacilityTuning.File));
        var sight = DataFile.Load<SightTuning>(Path.Combine(content, SightTuning.File));
        double yard = route.GateOr(routeTuning.YardLength);
        // Where each combination starts: it depends on the kinds only (the line's the same every night).
        var stageLine = route.Build();
        bool Crane(RouteFeature f) => facilities.ModulesOf(f).Contains(DarkTerritory.Sim.Run.ModuleKind.Crane);
        var stages = combos.ToDictionary(k => string.Join("+", k), k => DarkTerritory.Sim.Net.Combinations.Stage(route, stageLine, k, c.Enemies, yard, t.LeadM, t.ApproachM, Crane,
            atStops: args.Contains("--at-stops")));
        var nights = (from i in Enumerable.Range(0, combos.Count)
                      from hz in every ? hazards : [hazards[i % hazards.Count]]
                      from s in Enumerable.Range(1, seeds)
                      select (Kinds: combos[i], Hazards: hz, Seed: s)).ToList();
        var runs = new CombinationRun[nights.Count];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int done = 0;
        Parallel.For(0, nights.Count, new ParallelOptions { MaxDegreeOfParallelism = (int)Opt(args, "--parallel", Environment.ProcessorCount) }, i =>
        {
            var (kinds, hz, seed) = nights[i];
            var stage = stages[string.Join("+", kinds)];
            var night = new CombinationNight(kinds, hz, seed, stage.StartM, stage.Unstaged);
            var report = Harness.Run(route.Build(), c.Train, c.Player, new HarnessOptions
            {
                Bots = crew,
                Cars = cars,
                // At a stop, long enough to get there and get down to work.
                Seconds = stage.AtStop ? Opt(args, "--stop-seconds", t.StopSeconds) : seconds,
                Seed = seed,
                // A clean link: the meeting's under test here, not the netcode (the soak's rough-link nights are for that).
                Link = new Ballast.Net.LinkConditions(0, 0, 0),
                StartDistance = stage.StartM,
                Combat = c.Combat,
                Enemies = c.Enemies,
                Route = route,
                Run = c.Run,
                Facilities = facilities,
                Sight = sight,
                YardLength = yard,
                Insist = kinds,
                InsistEvery = t.InsistEvery,
                Hazards = hz,
            }, c.Boiler);
            runs[i] = DarkTerritory.Sim.Net.Combinations.Run(night, report, t, crew);
            int n = Interlocked.Increment(ref done);
            if (n % 20 == 0 || n == nights.Count)
                Console.Error.WriteLine($"{n}/{nights.Count} nights, {clock.Elapsed.TotalSeconds:0} s");
        });
        var rows = nights.Select(x => (x.Kinds, x.Hazards)).Distinct().Select(key =>
        {
            var mine = Enumerable.Range(0, nights.Count).Where(i => nights[i].Kinds == key.Kinds && nights[i].Hazards == key.Hazards).Select(i => runs[i]).ToList();
            return DarkTerritory.Sim.Net.Combinations.Judge(key.Kinds, key.Hazards, mine, stages[string.Join("+", key.Kinds)].Unstaged, t);
        }).ToList();
        var result = DarkTerritory.Sim.Net.Combinations.Report(size, rows);
        Print(new
        {
            seconds = Math.Round(clock.Elapsed.TotalSeconds),
            route = route.Name,
            crew,
            nightSeconds = seconds,
            seeds,
            hazards = hazards.Select(x => x.Name),
            everyHazard = every,
            atStops = args.Contains("--at-stops"),
            result
        });
        return result.Pass ? 0 : 1;
    }

    static string Str(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    static double Opt(string[] args, string name, double fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? double.Parse(args[i + 1], CultureInfo.InvariantCulture) : fallback;
    }

    static void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value, DataFile.Options));
}
