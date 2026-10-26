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
    /// [--seconds s] [--hazards clear,wet] [--only a,b] [--parallel p] [--trace dir]: GDD §34's combination fairness, over a grid of routes and
    /// crew sizes (note 204). dt balance --conflicts [--only a+b,...]: App. B.10's contradiction quality, the director's
    /// conflict table's pairs instead of the roster's, each staged where it's made (note 1032).
    /// </summary>
    public static int Combinations(string content, string[] args)
    {
        var c = Load(content);
        var t = c.Balance.Combinations;
        int size = args.Contains("--triples") ? 3 : 2;
        var roster = !args.Contains("--conflicts") && Str(args, "--only", "") is { Length: > 0 } o
            ? [.. o.Split(',').Select(k => Enum.Parse<EnemyKind>(k, ignoreCase: true))]
            : DarkTerritory.Sim.Net.Combinations.Roster;
        // App. B.10 "Contradiction quality: every seeded pair is solvable" (--conflicts, note 1032): the director's conflict
        // table instead of the roster's pairs, each pair's creatures insisted on and staged where it's made: at a stop for
        // what comes at one (as --at-stops) or a pair with the stop in it, short of a grade for a pair with one.
        var conflicts = args.Contains("--conflicts")
            ? DarkTerritory.Sim.Net.Combinations.Conflicts(c.Enemies.Director.Conflicts)
                .Where(p => Str(args, "--only", "") is not { Length: > 0 } only || only.Split(',').Contains(p.Name)).ToList()
            : null;
        if (conflicts?.FirstOrDefault(p => p.Unknown.Count > 0) is { } typo)
            throw new ArgumentException($"conflict {typo.Name}: no creature or condition {string.Join(", ", typo.Unknown)}");
        var combos = conflicts is not null
            ? [.. conflicts.Select(p => p.Kinds).DistinctBy(k => string.Join("+", k))]
            : DarkTerritory.Sim.Net.Combinations.Of(roster, Math.Min(size, roster.Count), (int)Opt(args, "--sample", size == 3 ? t.TripleSample : 0),
                (ulong)Opt(args, "--sample-seed", 1));
        // A combination's conditions, from the conflict pairs it stands for (none on the roster's sweep).
        bool Stop(IReadOnlyList<EnemyKind> k) => conflicts?.Any(p => p.AtStop && p.Kinds.SequenceEqual(k)) == true;
        bool Grade(IReadOnlyList<EnemyKind> k) => conflicts?.Any(p => p.Grade && p.Kinds.SequenceEqual(k)) == true;
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
        // A pair whose stop none of the grid's routes has (note 1036: the Track Doll at a coaling tower's loading) is run on
        // the first of siteRoutes that has one, instead, in that cell (--conflicts only).
        var siteRoutes = conflicts is null ? [] : t.SiteRoutes.Where(r => !grid.Routes.Contains(r)).ToList();
        var routes = grid.Routes.Concat(siteRoutes).Distinct().ToDictionary(r => r, r => DarkTerritory.Sim.LineGen.Routes.Generate(content, r, cars));
        var yards = routes.ToDictionary(x => x.Key, x => x.Value.GateOr(routeTuning.YardLength));
        // Where each combination starts on each route: it depends on the kinds and the line only (the same every night).
        var stages = routes.SelectMany(x =>
        {
            var stageLine = x.Value.Build();
            return combos.Select(k => (Key: (x.Key, string.Join("+", k)), Stage: DarkTerritory.Sim.Net.Combinations.Stage(x.Value, stageLine, k, c.Enemies, yards[x.Key],
                t.LeadM, t.ApproachM, facilities.ModulesOf, atStops: args.Contains("--at-stops") || conflicts is not null, stop: Stop(k), gradeWithin: Grade(k) ? t.GradeWithinM : 0,
                fastClear: t.FastClearM)));
        }).ToDictionary(x => x.Key, x => x.Stage);
        // Where each combination's cell on a grid route is really run: there, or a site route that stages what it couldn't.
        string RunOn(string route, IReadOnlyList<EnemyKind> k)
        {
            var stage = stages[(route, string.Join("+", k))];
            bool site = stage.Unstaged.Any(u => DarkTerritory.Sim.Net.Combinations.AtSites.Any(a => a.ToString() == u));
            return site && siteRoutes.FirstOrDefault(r => stages[(r, string.Join("+", k))].Unstaged.Count < stage.Unstaged.Count) is { } other ? other : route;
        }
        var nights = DarkTerritory.Sim.Net.Combinations.Nights(combos, hazards, every, grid)
            .Select(n => n with { Route = RunOn(n.Route, n.Kinds) }).Distinct().ToList();
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
                // At a stop, long enough to get there and get down to work; for a fast train's kind (note 1036), to get up to
                // speed and hold it long enough to bring it.
                Seconds = stage.Loading ? Opt(args, "--loading-seconds", t.LoadingSeconds) : stage.AtStop || stage.Long ? Opt(args, "--stop-seconds", t.StopSeconds) : stage.Fast ? Opt(args, "--fast-seconds", t.FastSeconds) : seconds,
                Express = stage.Fast ? t.ExpressSpeed : null,
                // At a coaling tower (note 1036), a tender short enough of coal that the crew stop to fill it.
                Script = stage.Coal && c.Boiler is { } coalBoiler
                    ? (tick, w) =>
                    {
                        if (tick == 0)
                            w.Train.Boiler.Tender = Math.Min(w.Train.Boiler.Tender, coalBoiler.TenderCapacity * t.CoalTenderShare);
                    }
                : null,
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
                InsistByRule = DarkTerritory.Sim.Net.Combinations.ByRule,
                Look = t.Look,
                Hazards = hz,
                // --trace dir (note 1036): each night a second at a time, to read what its verdict came of.
                Observe = Str(args, "--trace", "") is { Length: > 0 } traceDir ? Trace(traceDir, $"{string.Join("+", kinds)}-{hz.Name}-{routeName.Replace(':', '_')}-c{crew}-s{seed}") : null,
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
            var unstaged = grid.Routes.Distinct().Select(r => RunOn(r, key.Kinds)).Distinct().SelectMany(r => stages[(r, string.Join("+", key.Kinds))].Unstaged).Distinct().ToList();
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
            conflicts = conflicts?.Select(p => new
            {
                pair = p.Name,
                kinds = string.Join("+", p.Kinds),
                p.AtStop,
                p.Grade,
                // Where it's staged on each grid route (note 1036): the route it's really run on, the start, and how.
                staged = grid.Routes.Distinct().Select(r => (Grid: r, On: RunOn(r, p.Kinds))).Select(x => (x.Grid, x.On, S: stages[(x.On, string.Join("+", p.Kinds))]))
                    .Select(x => new { route = x.Grid, runOn = x.On, x.S.StartM, x.S.AtStop, x.S.Fast, x.S.Coal, x.S.Unstaged }),
            }),
            result
        });
        return result.Pass ? 0 : 1;
    }

    /// <summary>
    /// dt harness stability [--crews 2,3,8] [--seconds s] [--links perfect,rough] [--parallel p]: GDD §34 "Stability: desync
    /// and crash detection across 2–8 clients" (note 1032). Each crew size on each link, a night of bots on the test loop;
    /// exits 1 on a desync or a crash.
    /// </summary>
    public static int Stability(string content, string[] args)
    {
        var c = Load(content);
        var t = c.Balance.Stability;
        var crews = Str(args, "--crews", "") is { Length: > 0 } cs ? [.. cs.Split(',').Select(x => int.Parse(x, CultureInfo.InvariantCulture))] : t.Crews;
        var links = Str(args, "--links", "perfect,rough").Split(',');
        double seconds = Opt(args, "--seconds", t.Seconds);
        var line = DarkTerritory.Sim.Rail.RailLine.Load(Path.Combine(content, "lines/test-loop.json"));
        var nights = (from crew in crews from link in links select (crew, link)).ToList();
        var rows = new StabilityRow[nights.Count];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, nights.Count, new ParallelOptions { MaxDegreeOfParallelism = (int)Opt(args, "--parallel", Environment.ProcessorCount) }, i =>
        {
            var (crew, link) = nights[i];
            HarnessReport? report = null;
            string? crash = null;
            try
            {
                report = Harness.Run(line, c.Train, c.Player, new HarnessOptions
                {
                    Bots = crew,
                    Seconds = seconds,
                    Seed = crew,
                    Link = link == "perfect" ? Ballast.Net.LinkConditions.Perfect : Ballast.Net.LinkConditions.Rough,
                }, c.Boiler);
            }
            catch (Exception e)
            {
                crash = $"{e.GetType().Name}: {e.Message}";
            }
            rows[i] = DarkTerritory.Sim.Net.Stability.Judge(crew, link, report, crash, t);
            Console.Error.WriteLine($"crew {crew} {link}: {(rows[i].Pass ? "ok" : "FAILED")}, {clock.Elapsed.TotalSeconds:0} s");
        });
        var result = DarkTerritory.Sim.Net.Stability.Report(rows);
        Print(new { seconds = Math.Round(clock.Elapsed.TotalSeconds), nightSeconds = seconds, result });
        return result.Pass ? 0 : 1;
    }

    /// <summary>
    /// <c>--trace dir</c> (note 1036): each sweep night a second at a time in <c>dir/kinds-hazards-route-cCrew-sSeed.txt</c>: the
    /// train's speed and distance, every crewmate (car, place in its frame, yaw, health, death, surface, flags), every creature
    /// not gone (its phase, the car it's on, where along the line), the roof warning and the Choir. What a night's verdict
    /// came of, read without a window.
    /// </summary>
    static Action<uint, IReadOnlyList<(DarkTerritory.Sim.Bots.IBot Bot, DarkTerritory.Sim.Player.PlayerState State)>, DarkTerritory.Sim.World> Trace(string dir, string name)
    {
        Directory.CreateDirectory(dir);
        var w = new StreamWriter(Path.Combine(dir, name + ".txt")) { AutoFlush = true };
        return (t, bots, world) =>
        {
            if (t % DarkTerritory.Sim.SimConstants.TickRate != 0)
                return;
            var sb = new System.Text.StringBuilder().Append(CultureInfo.InvariantCulture, $"{t * DarkTerritory.Sim.SimConstants.TickSeconds:0}s v{world.Train.Dynamics.Velocity:0.0} d{world.Train.Dynamics.Distance:0}");
            foreach (var (b, s) in bots)
                sb.Append(CultureInfo.InvariantCulture, $" [{b.Name} car{s.Parent} {s.Position.X:0.0},{s.Position.Y:0.0},{s.Position.Z:0.0} y{s.Yaw:0.00} hp{s.Health} {s.Death} {s.Surface} {s.Flags} {Harness.Describe(b, s)}]");
            foreach (var e in world.ActiveEnemies.Where(e => !e.Gone))
                sb.Append(CultureInfo.InvariantCulture, $" <{e.Kind}#{e.Id} {e.Phase} car{e.Attached} d{e.LineDistance:0} lat{e.Lateral:0.0} x{e.Extra:0.00}>");
            sb.Append(CultureInfo.InvariantCulture, $" towersDown={world.TowersDown} run={world.Run?.Phase}{(world.Run is { Facility: >= 0 } at ? "@" + at.Facility : "")} bodies={world.Bodies?.All.Count() ?? 0}");
            sb.Append(CultureInfo.InvariantCulture, $" roofWarning={world.Lineside?.Warning(world.Train)?.Kind.ToString() ?? "-"} choir={world.Choir.Build:0.00}{(world.Choir.Present ? " here" : "")}");
            w.WriteLine(sb.ToString());
        };
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
