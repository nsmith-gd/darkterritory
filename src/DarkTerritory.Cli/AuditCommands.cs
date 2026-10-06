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
    /// dt balance --pairs|--triples [--wide] [--routes a,b] [--crews 2,4,8] [--seeds n] [--every-hazard] [--at-stops] [--sample n]
    /// [--seconds s] [--hazards clear,wet] [--only a,b] [--parallel p]: GDD §34's combination fairness, over a grid of routes and
    /// crew sizes (note 204).
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
        // The grid (note 204): every combination on every route with every crew size, seeds a cell. The default's quick; the
        // nightly's --wide. --routes, --crews and --seeds override either (--route and --crew still name one).
        var baseGrid = args.Contains("--wide") ? t.Wide : t.Grid;
        var grid = new CombinationGrid(
            Str(args, "--routes", Str(args, "--route", "")) is { Length: > 0 } rs ? [.. rs.Split(',')] : baseGrid.Routes,
            Str(args, "--crews", Str(args, "--crew", "")) is { Length: > 0 } cs ? [.. cs.Split(',').Select(x => int.Parse(x, CultureInfo.InvariantCulture))] : baseGrid.Crews,
            (int)Opt(args, "--seeds", baseGrid.Seeds));
        int cars = (int)Opt(args, "--cars", t.Cars);
        double seconds = Opt(args, "--seconds", t.Seconds);
        var routeTuning = RouteTuning.Load(content);
        var facilities = DataFile.Load<DarkTerritory.Sim.Run.FacilityTuning>(Path.Combine(content, DarkTerritory.Sim.Run.FacilityTuning.File));
        var sight = DataFile.Load<SightTuning>(Path.Combine(content, SightTuning.File));
        bool Crane(RouteFeature f) => facilities.ModulesOf(f).Contains(DarkTerritory.Sim.Run.ModuleKind.Crane);
        var routes = grid.Routes.Distinct().ToDictionary(r => r, r => DarkTerritory.Sim.LineGen.Routes.Generate(content, r, cars));
        var yards = routes.ToDictionary(x => x.Key, x => x.Value.GateOr(routeTuning.YardLength));
        // Where each combination starts on each route: it depends on the kinds and the line only (the same every night).
        var stages = routes.SelectMany(x =>
        {
            var stageLine = x.Value.Build();
            return combos.Select(k => (Key: (x.Key, string.Join("+", k)), Stage: DarkTerritory.Sim.Net.Combinations.Stage(x.Value, stageLine, k, c.Enemies, yards[x.Key],
                t.LeadM, t.ApproachM, Crane, atStops: args.Contains("--at-stops"))));
        }).ToDictionary(x => x.Key, x => x.Stage);
        var nights = DarkTerritory.Sim.Net.Combinations.Nights(combos, hazards, every, grid);
        var runs = new CombinationRun[nights.Count];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int done = 0;
        Parallel.For(0, nights.Count, new ParallelOptions { MaxDegreeOfParallelism = (int)Opt(args, "--parallel", Environment.ProcessorCount) }, i =>
        {
            var (kinds, hz, routeName, crew, seed) = nights[i];
            var route = routes[routeName];
            double yard = yards[routeName];
            var stage = stages[(routeName, string.Join("+", kinds))];
            var night = new CombinationNight(kinds, hz, seed, stage.StartM, stage.Unstaged, routeName);
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
                Look = t.Look,
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
            // What no place suited, on any of the routes (each says which in its cell's nights' start).
            var unstaged = grid.Routes.Distinct().SelectMany(r => stages[(r, string.Join("+", key.Kinds))].Unstaged).Distinct().ToList();
            return DarkTerritory.Sim.Net.Combinations.Judge(key.Kinds, key.Hazards, mine, unstaged, t);
        }).ToList();
        var result = DarkTerritory.Sim.Net.Combinations.Report(size, rows);
        Print(new
        {
            seconds = Math.Round(clock.Elapsed.TotalSeconds),
            routes = grid.Routes,
            crews = grid.Crews,
            nightSeconds = seconds,
            seeds = grid.Seeds,
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
