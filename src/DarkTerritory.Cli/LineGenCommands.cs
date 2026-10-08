using System.Diagnostics;
using System.Globalization;
using DarkTerritory.Game.LineGen;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;

/// <summary>`dt linegen`: the line generator's tools (plan §20.2).</summary>
static class LineGenCommands
{
    public static object Run(string content, string verb, string[] args) => verb switch
    {
        "generate" => Generate(content, args),
        "sweep" => Sweep(content, args),
        "bench" => Bench(content, args),
        "transect" => Transect(content, args),
        "water" => Water(content, args),
        "lineside" => LinesideReport(content, args),
        "sky" => Sky(content, args),
        "prints" => Prints(content, args),
        "debug" => LineGenerator.Debug(LineGenContent.Load(content), Parameters(args), (int)Opt(args, "--attempt", 0)).ToList(),
        _ => throw new ArgumentException($"linegen {verb}? (generate, sweep)"),
    };

    /// <summary>
    /// The line's and the land's checksums for some nights (T116 cross-play): a joiner builds the host's night itself and
    /// is refused if either differs, so CI runs this on Windows and Linux and compares. Each part of the plan gets its own
    /// checksum too, so a mismatch says where (the alignment, the pieces, the validator's run) rather than just that.
    /// </summary>
    static object Prints(string content, string[] args)
    {
        var specs = Str(args, "--routes", "frontier:1,frontier:7,deadLines:3,deepTerritory:2").Split(',');
        return specs.Select(spec =>
        {
            var route = Routes.Generate(content, spec, (int)Opt(args, "--cars", 6));
            var line = route.Build();
            using var plan = System.Text.Json.JsonDocument.Parse(route.Plan!.ToJson());
            var parts = plan.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => Streams.Hash(p.Value.GetRawText()).ToString("x16"));
            return new { route = spec, plan = route.Plan!.Fingerprint(), terrain = new PlanConditions(route.Plan!, line).Terrain.Print(), lineside = LinesidePrint(content, route, line), parts };
        }).ToList();
    }

    /// <summary>The night's lineside (note 371) as every machine stands it.</summary>
    static DarkTerritory.Sim.Run.LinesideProps? LinesideOf(Route route, DarkTerritory.Sim.Rail.RailLine line) => DarkTerritory.Sim.Run.LinesideProps.Of(route, line);

    /// <summary>A checksum of the lineside's trees, boulders and poles (note 371): they're walls, so a machine that stood them differently would predict wrong.</summary>
    static string LinesidePrint(string content, Route route, DarkTerritory.Sim.Rail.RailLine line)
    {
        if (LinesideOf(route, line) is not { } side)
            return "";
        var text = new System.Text.StringBuilder();
        foreach (var p in side.Props(0, line.Length))
            text.Append(CultureInfo.InvariantCulture, $"{(int)p.Kind} {p.Along:R} {p.Lateral:R} {p.Height:R} {p.Size:R} {p.Sink:R} {p.Species} {p.Dead} {p.Seed};");
        return Streams.Hash(text.ToString()).ToString("x16");
    }

    /// <summary>
    /// `dt linegen lineside --route r`: what the Sim stands beside a generated line (note 371), by kind and biome, how many
    /// are within reach of the track, and what building them and their walls costs a machine at the run's start.
    /// </summary>
    static object LinesideReport(string content, string[] args)
    {
        var route = Routes.Generate(content, Str(args, "--route", "frontier:7"), (int)Opt(args, "--cars", 6));
        var line = route.Build();
        var clock = Stopwatch.StartNew();
        var side = LinesideOf(route, line) ?? throw new InvalidOperationException("a hand-laid line has no generated lineside");
        var forts = DarkTerritory.Sim.Run.Fortresses.Of(route, line, LineGenContent.Cached(content).Route.YardLength, 400);
        double solidReach = Opt(args, "--reach", new DarkTerritory.Sim.Run.WallTuning().LinesideReachM);
        var props = side.Props(0, line.Length).ToList();
        long propsMs = clock.ElapsedMilliseconds;
        var timed = new Dictionary<string, long>();
        foreach (double reach in new[] { 0.0, 20, 40, 60 })
        {
            clock.Restart();
            int n = side.Props(0, line.Length, reach).Count();
            timed[$"{reach:0}m ({n})"] = clock.ElapsedMilliseconds;
        }
        clock.Restart();
        var walls = side.Walls(forts, solidReach).ToList();
        long wallsMs = clock.ElapsedMilliseconds;
        clock.Restart();
        DarkTerritory.Sim.Run.StopWalls.Of(walls);
        long indexMs = clock.ElapsedMilliseconds;
        double km = line.Length / 1000;
        return new
        {
            route = route.Plan!.Route.Id,
            km = Math.Round(km, 2),
            propsMs,
            solidReach,
            solids = walls.Count,
            wallsMs,
            indexMs,
            reachMs = timed,
            print = LinesidePrint(content, route, line),
            kinds = props.GroupBy(p => p.Kind.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            withinTwentyM = props.GroupBy(p => p.Kind.ToString()).ToDictionary(g => g.Key, g => g.Count(p => Math.Abs(p.Lateral) <= 20)),
            treesPerKmByBiome = props.Where(p => p.Kind == DarkTerritory.Sim.Run.LinesideKind.Tree).GroupBy(p => DarkTerritory.Game.Art.WorldArt.BiomeAt(route, p.Along) ?? "?")
                .ToDictionary(g => g.Key, g => g.Count()),
            species = props.Where(p => p.Kind == DarkTerritory.Sim.Run.LinesideKind.Tree).GroupBy(p => p.Dead ? "dead" : p.Species).ToDictionary(g => g.Key, g => g.Count()),
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

    static RunParameters Parameters(string[] args)
    {
        // --route tier:seed[:severity], as the rest of dt and the game take it; or --tier, --seed, --severity.
        if (Str(args, "--route", "") is { Length: > 0 } spec)
            return RunParameters.Parse(spec, (int)Opt(args, "--cars", 8));
        var tier = Enum.Parse<RouteTier>(Str(args, "--tier", "frontier"), ignoreCase: true);
        ulong seed = ulong.Parse(Str(args, "--seed", "1"));
        double severity = args.Contains("--severity") ? Opt(args, "--severity", 0.5) : RunParameters.SeverityOf(tier, seed);
        return new RunParameters(tier, seed, severity, (int)Opt(args, "--cars", 8), []);
    }

    static object Generate(string content, string[] args)
    {
        var c = LineGenContent.Load(content);
        var p = Parameters(args);
        var clock = Stopwatch.StartNew();
        // --attempt n: that one attempt as it came out, passed or not (for looking at why one failed).
        var plan = args.Contains("--attempt") ? LineGenerator.Attempt(c, p, (int)Opt(args, "--attempt", 0)).Plan ?? throw new InvalidOperationException("that attempt didn't build")
            : LineGenerator.Generate(c, p);
        long ms = clock.ElapsedMilliseconds;
        var route = plan.ToRoute(c.Route);
        var line = route.Build();
        string output = Str(args, "--out", $"out/linegen/{p.RouteId}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, plan.ToJson(indented: true));
        string map = Str(args, "--map", Path.ChangeExtension(output, ".map.png"));
        string profile = Str(args, "--profile", Path.ChangeExtension(output, ".profile.png"));
        PlanImages.Map(plan, line, map);
        PlanImages.Profile(plan, line, profile);
        return new
        {
            plan = Path.GetFullPath(output),
            map = Path.GetFullPath(map),
            profile = Path.GetFullPath(profile),
            ms,
            route = plan.Route,
            lengthKm = Math.Round(plan.Km(plan.TerminusM), 2),
            attempts = plan.Validation.Attempts,
            fallback = plan.Validation.Fallback,
            passed = plan.Validation.Passed,
            failed = plan.Validation.Checks.Where(k => !k.Pass).Select(k => $"{k.Name}: {k.Detail}"),
            warnings = plan.Validation.Warnings,
            compressedKb = Math.Round(plan.CompressedBytes() / 1024.0, 1),
            edges = plan.Alignment.Select(a => new { a.Edge, a.Role, lengthKm = Math.Round(a.Length / 1000, 2), a.Toe, a.Rejoin }),
            pieces = plan.Pieces.GroupBy(x => x.Type).ToDictionary(g => g.Key, g => g.Count()),
            metrics = plan.Validation.Metrics,
        };
    }

    /// <summary>The land across the line at --at (main-line metres): its height over the rail, out to 300 m either side.</summary>
    static object Transect(string content, string[] args)
    {
        var c = LineGenContent.Cached(content);
        var route = Routes.Generate(content, Str(args, "--route", "frontier:7"), (int)Opt(args, "--cars", 6));
        var line = route.Build();
        var terrain = ((PlanConditions)line.Conditions!).Terrain;
        double at = Opt(args, "--at", 5000);
        var t = line.Sample(at);
        return new[] { -300, -200, -150, -100, -60, -30, -15, -8, -4, 0, 4, 8, 15, 30, 60, 100, 150, 200, 300 }.Select(l =>
        {
            double x = t.Position.X - t.Tangent.Z * l, z = t.Position.Z + t.Tangent.X * l;
            return new { lateral = l, height = Math.Round(terrain.Height(x, z) - t.Position.Y, 1) };
        }).ToList();
    }

    /// <summary>
    /// A route's lakes and shores (docs/design/maritime-rules.md): where each sits along the main line, which side and
    /// how far out, its level under the rail, and a lake's water sampled at its centre (the terrain must hold it).
    /// </summary>
    static object Water(string content, string[] args)
    {
        var route = Routes.Generate(content, Str(args, "--route", "frontier:7"), (int)Opt(args, "--cars", 6));
        var line = route.Build();
        var terrain = ((PlanConditions)line.Conditions!).Terrain;
        var plan = route.Plan!;
        return new
        {
            biomes = plan.Biomes.Select(b => new { b.Biome, s0 = Math.Round(b.S0), s1 = Math.Round(b.S1) }),
            lakes = plan.Lakes.Select(l =>
            {
                var n = terrain.Nearby(l.X, l.Z, 700).Where(q => q.Edge == 0).OrderBy(q => Math.Abs(q.Lateral)).FirstOrDefault();
                return new
                {
                    l.Id,
                    l.Crossed,
                    radius = l.RadiusM,
                    long_ = Math.Round(l.RadiusM * l.Stretch),
                    s = Math.Round(n.S),
                    lateral = Math.Round(n.Lateral),
                    belowRail = Math.Round(n.Rail - l.LevelM, 1),
                    wet = terrain.WaterAt(l.X, l.Z) is not null,
                };
            }),
            shores = plan.Shores.Select(sh => new { sh.Id, kind = sh.Kind.ToString(), s0 = Math.Round(sh.S0), s1 = Math.Round(sh.S1), sh.Side, sh.NearM, sh.CoveM, sh.DykeM }),
            tidal = plan.Water.Where(w => w.Type == "tidal").Select(w => new { w.Id, s = Math.Round((w.S0 + w.S1) / 2), span = Math.Round(w.S1 - w.S0) }),
        };
    }

    /// <summary>A route's far horizon (Art.PlanSky) as a PNG, to look at the band flat.</summary>
    static object Sky(string content, string[] args)
    {
        var route = Routes.Generate(content, Str(args, "--route", "frontier:7"), (int)Opt(args, "--cars", 6));
        var sky = DarkTerritory.Game.Art.PlanSky.For(route)!;
        string output = Str(args, "--out", $"out/linegen/{route.Name}.sky.png");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        Ballast.Render.PngWriter.Write(output, sky.Rgba, sky.Width, sky.Height, 1);
        return new { path = Path.GetFullPath(output) };
    }

    static object Bench(string content, string[] args)
    {
        var c = LineGenContent.Load(content);
        var plan = LineGenerator.Generate(c, Parameters(args));
        var line = plan.ToRoute(c.Route).Build();
        var train = new DarkTerritory.Sim.Train.TrainOnLine(new DarkTerritory.Sim.Train.TrainDynamics(DarkTerritory.Sim.Train.Consist.Uniform(c.Train, 8, 1)), line, 1500);
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < 50000; i++)
            train.Step(1 / 30.0, new DarkTerritory.Sim.Train.TrainControls { Throttle = train.Dynamics.Speed < 12 ? 1 : 0, Reverser = 1 });
        double step = clock.Elapsed.TotalMilliseconds;
        clock.Restart();
        double k = 0;
        for (int i = 0; i < 50000; i++)
            foreach (var car in train.Cars)
                k += line.Sample(-1, car.FrontDistance - i % 100).Curvature;
        double samples = clock.Elapsed.TotalMilliseconds;
        // §17.5: the terrain, as the art and the players' ground ask for it, and a whole tile.
        var terrain = ((PlanConditions)line.Conditions!).Terrain;
        clock.Restart();
        double h = 0;
        for (int i = 0; i < 20000; i++)
        {
            var t = line.Sample(1500 + i * 0.9);
            h += terrain.Height(t.Position.X + (i % 41 - 20) * 3, t.Position.Z + (i % 37 - 18) * 3);
        }
        double heightUs = clock.Elapsed.TotalMilliseconds * 1000 / 20000;
        clock.Restart();
        var at = line.Sample(6000).Position;
        var (tx, tz) = terrain.TileOf(at.X, at.Z);
        for (int i = 0; i < 3; i++)
            h += terrain.TileHeights(tx + i, tz)[0];
        double tileMs = clock.Elapsed.TotalMilliseconds / 3;
        return new { stepMs = step, samplesMs = samples, k, heightUs, tileMs, h };
    }

    /// <summary>
    /// §16.5 / §20.2: generation, validation and metrics over many seeds per tier and consist; checks that deeper is
    /// harder at a matched consist (GDD B.9). With --fallbacks, the first passing seeds per band go to fallback_seeds.json.
    /// </summary>
    static object Sweep(string content, string[] args)
    {
        var c = LineGenContent.Load(content);
        string tierArg = Str(args, "--tier", "all");
        var tiers = tierArg == "all" ? Enum.GetValues<RouteTier>() : [Enum.Parse<RouteTier>(tierArg, ignoreCase: true)];
        var cars = Str(args, "--cars", "3,10,20").Split(',').Select(int.Parse).ToArray();
        int seeds = (int)Opt(args, "--seeds", 50);
        ulong first = ulong.Parse(Str(args, "--first", "1"));
        string report = Str(args, "--report", "");
        var rows = new List<string> { "tier,cars,seed,attempts,fallback,passed,ms,lengthKm,idealMin,dawnSlackMin,minRadius,ruling,tunnelM,bridgeM,junctions,alternates,deadLines,stackMax,terrainCost,requiredTells,restricted,sleepers,failed" };
        var summary = new List<object>();
        foreach (var tier in tiers)
            foreach (int n in cars)
            {
                var results = new (LinePlan Plan, long Ms)[seeds];
                Parallel.For(0, seeds, i =>
                {
                    var clock = Stopwatch.StartNew();
                    ulong seed = first + (ulong)i;
                    LinePlan plan;
                    try
                    {
                        plan = LineGenerator.Generate(c, new RunParameters(tier, seed, RunParameters.SeverityOf(tier, seed), n, []));
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException($"{tier}:{seed} at {n} cars: {e.Message}", e);
                    }
                    results[i] = (plan, clock.ElapsedMilliseconds);
                });
                var failures = new Dictionary<string, int>();
                foreach (var (plan, ms) in results)
                {
                    var m = plan.Validation.Metrics;
                    double M(string k) => m.TryGetValue(k, out var v) ? v : 0;
                    // Each failed check once per attempt, with the first words of why ("quotas: climb_long 0/1").
                    var failed = plan.Validation.Warnings.Where(w => w.StartsWith("attempt")).SelectMany(w => w.Split(": ", 2)[1].Split("; "))
                        .Select(f => string.Join(' ', f.Split(' ').Take(f.StartsWith("quotas") || f.StartsWith("geometry") || f.StartsWith("tells") ? 4 : 1)).TrimEnd(',', ':'))
                        .Concat(plan.Validation.Checks.Where(k => !k.Pass).Select(k => "final " + k.Name)).ToList();
                    foreach (var f in failed)
                        failures[f] = failures.GetValueOrDefault(f) + 1;
                    rows.Add(string.Join(",", tier, n, plan.Route.Id.Split('-')[^1], plan.Validation.Attempts, plan.Validation.Fallback, plan.Validation.Passed, ms,
                        M("lengthKm"), M("idealTransitMin"), M("dawnSlackMin"), M("minRadius"), M("rulingGradeMain"), M("tunnelM"), M("bridgeM"), M("junctions"),
                        M("alternates"), M("deadLines"), M("stackDepthMax"), M("terrainCost"), M("requiredTells"), M("restrictedZones"), M("sleepers"),
                        "\"" + string.Join(";", failed.Distinct()) + "\""));
                }
                var plans = results.Select(r => r.Plan).ToList();
                summary.Add(new
                {
                    tier,
                    cars = n,
                    seeds,
                    firstAttempt = Math.Round(plans.Count(p => p.Validation.Attempts == 1 && !p.Validation.Fallback) / (double)seeds, 3),
                    withinTwo = Math.Round(plans.Count(p => p.Validation.Attempts <= 2 && !p.Validation.Fallback) / (double)seeds, 3),
                    fallbacks = plans.Count(p => p.Validation.Fallback),
                    unpassed = plans.Count(p => !p.Validation.Passed),
                    meanMs = Math.Round(results.Average(r => r.Ms)),
                    maxMs = results.Max(r => r.Ms),
                    meanIdealMin = Math.Round(plans.Average(p => p.Validation.Metrics.GetValueOrDefault("idealTransitMin")), 1),
                    meanTerrainCost = Math.Round(plans.Average(p => p.Validation.Metrics.GetValueOrDefault("terrainCost")), 1),
                    meanDemands = Math.Round(plans.Average(p => p.Authority.Demands.Count), 1),
                    meanAverageSpeed = Math.Round(plans.Average(p => p.Validation.Metrics.GetValueOrDefault("averageSpeed")), 2),
                    meanHardBends = Math.Round(plans.Average(p => p.Validation.Metrics.GetValueOrDefault("hardBends")), 2),
                    sBends = plans.Sum(p => (int)p.Validation.Metrics.GetValueOrDefault("sBends")),
                    nightsWithSBends = plans.Count(p => p.Validation.Metrics.GetValueOrDefault("sBends") > 0),
                    failures = failures.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
                });
            }
        if (report.Length > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
            File.WriteAllLines(report, rows);
        }
        if (args.Contains("--fallbacks"))
            WriteFallbacks(c, content, tiers);
        return summary;
    }

    /// <summary>§16.4: for each tier and consist band, seeds whose first attempt passes, written to fallback_seeds.json.</summary>
    static void WriteFallbacks(LineGenContent c, string content, RouteTier[] tiers)
    {
        var bands = new[] { (1, 5), (6, 10), (11, 15), (16, 20) };
        var lines = new List<string>
        {
            "// Plan §16.4: seeds proven to pass, per tier and consist band, used after ten failed attempts (every use is logged:",
            "// it's a generator bug). Found by `dt linegen sweep --fallbacks`: each seed's first attempt passes at both ends of its band.",
            "{",
            "  \"seeds\": {",
        };
        var tierLines = new List<string>();
        foreach (var tier in Enum.GetValues<RouteTier>())
        {
            var bandLines = new List<string>();
            foreach (var (lo, hi) in bands)
            {
                var found = new List<ulong>();
                for (ulong seed = 1000; seed < 1400 && found.Count < 4; seed++)
                {
                    bool ok = new[] { lo, hi }.All(n =>
                    {
                        var p = new RunParameters(tier, seed, RunParameters.SeverityOf(tier, seed), n, []);
                        var (plan, _) = LineGenerator.Attempt(c, p, 0);
                        return plan is not null && plan.Validation.Passed;
                    });
                    if (ok)
                        found.Add(seed);
                }
                bandLines.Add($"\"{lo}-{hi}\": [{string.Join(", ", found)}]");
            }
            tierLines.Add($"    \"{char.ToLowerInvariant(tier.ToString()[0])}{tier.ToString()[1..]}\": {{ {string.Join(", ", bandLines)} }}");
        }
        lines.Add(string.Join(",\n", tierLines));
        lines.Add("  }");
        lines.Add("}");
        File.WriteAllText(Path.Combine(content, LineGenConfig.Directory, "fallback_seeds.json"), string.Join("\n", lines) + "\n");
    }
}
