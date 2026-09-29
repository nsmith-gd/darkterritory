using System.Diagnostics;
using System.Text.Json;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

// `dt` — the headless command-line entry point. Everything an agent needs to inspect or verify
// the game without a window goes through here. Output is JSON unless stated otherwise.

var content = DataFile.FindContentRoot(Environment.CurrentDirectory);
var train = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
var player = DataFile.Load<PlayerTuning>(Path.Combine(content, PlayerTuning.File));
var boiler = DataFile.Load<BoilerTuning>(Path.Combine(content, BoilerTuning.File));
var routeTuning = DataFile.Load<RouteTuning>(Path.Combine(content, RouteTuning.File));

return args switch
{
    ["train", "table"] => Print(TrainTable(train, player)),
    ["boiler", "table"] => Print(new[] { 3, 6, 10, 15, 20 }.Select(n => new
    {
        cars = n,
        holdSecondsPerUnit = Math.Round(BoilerScenarios.HoldingSecondsPerUnit(boiler, n), 1),
        tenderEnduranceMin = Math.Round(BoilerScenarios.TenderEnduranceMinutes(boiler, n)),
        rebuildFromZeroS = Math.Round(BoilerScenarios.RebuildSeconds(train, boiler, player, n)),
        tenMinutesFullThrottle = BoilerScenarios.Run(train, boiler, player, n, 600, 1),
    })),
    ["boiler", "run", var cars, ..] => Print(BoilerScenarios.Run(train, boiler, player, int.Parse(cars), Opt(args, "--seconds", 300), Opt(args, "--throttle", 1),
        args.Contains("--no-fireman") ? null : Opt(args, "--fire-at", 88), Opt(args, "--pressure", 75), Opt(args, "--firebox", 4), Opt(args, "--speed", 0), args.Contains("--vent"))),
    ["train", "stop", var cars, ..] => Print(TrainScenarios.StopFrom(train, int.Parse(cars), Opt(args, "--from", train.MaxSpeed), Opt(args, "--load", 1), Opt(args, "--grade", 0))),
    ["train", "climb", var cars, var grade, ..] => Print(TrainScenarios.Climb(train, int.Parse(cars), double.Parse(grade), Opt(args, "--from", 10), Opt(args, "--load", 1))),
    ["line", "info", var name, ..] => Print(LineInfo(LoadLine(name), Opt(args, "--every", 100))),
    ["line", "drive", var name, ..] => Print(Drive(train, LoadLine(name), (int)Opt(args, "--cars", 3), Opt(args, "--start", -1), Opt(args, "--from", 0), Opt(args, "--throttle", 1), (int)Opt(args, "--seconds", 120))),
    ["screenshot", ..] => Print(Screenshot(train, content, args)),
    ["route", "gen", ..] => Print(GenerateRoute(routeTuning, content, args)),
    ["route", "sweep", ..] => Print(SweepRoutes(routeTuning, (int)Opt(args, "--seeds", 200))),
    ["harness", ..] => Print(RunHarness(args)),
    ["online", "check"] => Print(OnlineCheck()),
    ["audio", "render", ..] => Print(RenderAudio(content, args)),
    ["edit", ..] => Edit(content, args),
    ["voice", "bench", ..] => Print(DarkTerritory.Game.Sound.VoiceBench.Run(content, (int)Opt(args, "--car", 3), Opt(args, "--z", 4), args.Contains("--radio"),
        Opt(args, "--seconds", 2), new Ballast.Net.LinkConditions(Opt(args, "--latency", 0), Opt(args, "--jitter", 0), Opt(args, "--loss", 0)))),

    _ => Usage(),
};

static object TrainTable(TrainTuning t, PlayerTuning p) => t.Performance.Select(r =>
{
    var consist = Consist.Uniform(t, r.Cars, 1);
    var stop = TrainScenarios.StopFrom(t, r.Cars, t.MaxSpeed);
    var dyn = new TrainDynamics(consist);
    return new
    {
        cars = r.Cars,
        lengthM = consist.LengthMetres,
        roofTraverseS = Math.Round(consist.LengthMetres / p.RoofRun, 1),
        massT = consist.MassTonnes,
        stopS = Math.Round(stop.Seconds, 1),
        stopM = Math.Round(stop.Metres),
        maxGradePct = Math.Round(dyn.MaxClimbableGradePercent(), 2),
    };
}).ToList();

object RunHarness(string[] args)
{
    Route? route = Str(args, "--route", "") is { Length: > 0 } spec ? RouteGenerator.Generate(routeTuning, Route.ParseSpec(spec).Tier, Route.ParseSpec(spec).Seed) : null;
    var line = route?.Build() ?? LoadLine(Str(args, "--line", "test-loop"));
    var combat = DataFile.Load<DarkTerritory.Sim.Combat.CombatTuning>(Path.Combine(content, DarkTerritory.Sim.Combat.CombatTuning.File));
    var enemies = DataFile.Load<DarkTerritory.Sim.Enemies.EnemyTuning>(Path.Combine(content, DarkTerritory.Sim.Enemies.EnemyTuning.File));
    using var online = args.Contains("--online") ? new DarkTerritory.Game.FakeLobbyNetwork() : null;
    return Harness.Run(line, train, player, new HarnessOptions
    {
        Bots = (int)Opt(args, "--bots", 8),
        Cars = (int)Opt(args, "--cars", 10),
        Seconds = Opt(args, "--seconds", 120),
        Seed = (int)Opt(args, "--seed", 1),
        Link = new Ballast.Net.LinkConditions(Opt(args, "--latency", 0.09), Opt(args, "--jitter", 0.02), Opt(args, "--loss", 0.03)),
        StartDistance = route is null ? 600 : 400,
        Combat = args.Contains("--no-combat") ? null : combat,
        Enemies = args.Contains("--enemies") ? enemies : null,
        Route = route,
        Udp = args.Contains("--udp"),
        Network = online,
        Run = route is null ? null : DataFile.Load<DarkTerritory.Sim.Run.RunTuning>(Path.Combine(content, DarkTerritory.Sim.Run.RunTuning.File)),
        YardLength = routeTuning.YardLength,
    }, args.Contains("--no-boiler") ? null : boiler);
}

// Can this machine reach Steam? Says who it's signed in as, or exactly what's missing.
static object OnlineCheck()
{
    using var steam = Ballast.Online.Steam.SteamBackend.TryStart(Ballast.Online.Steam.SteamBackend.DevAppId, out var error);
    return steam is null
        ? new { steam = false, error }
        : new { steam = true, error = (string?)null, user = steam.NameOf(steam.Me), id = steam.Me.ToString() };
}

RailLine LoadLine(string name) => RailLine.Load(Path.Combine(content, "lines", name + ".json"));

// Generates a route; writes its line to content/lines/<name>.json (so screenshot/app can use it),
// its features next to it, and a map PNG. Prints a summary.
static object GenerateRoute(RouteTuning t, string content, string[] args)
{
    var tier = Enum.Parse<RouteTier>(Str(args, "--tier", "frontier"), ignoreCase: true);
    ulong seed = (ulong)Opt(args, "--seed", 1);
    var route = RouteGenerator.Generate(t, tier, seed);
    string name = Str(args, "--name", "generated");
    DataFile.Save(Path.Combine(content, "lines", name + ".json"), route.Line with { Name = name });
    DataFile.Save(Path.Combine(content, "lines", name + ".route.json"), route);
    string map = Str(args, "--map", $"out/routes/{route.Name}.png");
    PngWriter.Write(map, RouteMap.Render(route, 900, 700), 900, 700);
    return Summarise(route, map);
}

static object Summarise(Route r, string? map = null) => new
{
    r.Name,
    lengthKm = Math.Round(r.Length / 1000, 2),
    dawnMinutes = Math.Round(r.DawnSeconds / 60, 1),
    maxGradePct = r.Line.Segments.Max(s => Math.Abs(s.GradePercent)),
    minRadiusM = r.Line.Segments.Where(s => s.Radius != 0).Select(s => Math.Abs(s.Radius)).DefaultIfEmpty(0).Min(),
    facilities = r.Of(FeatureKind.Facility).Select(f => $"{f.Facility} @ {f.Start / 1000:0.0} km"),
    tunnels = r.Of(FeatureKind.Tunnel).Count(),
    bridges = r.Of(FeatureKind.Bridge).Select(b => b.MaxCars > 0 ? $"weak ({b.MaxCars} cars)" : "sound"),
    junctions = r.Of(FeatureKind.Junction).Count(),
    sleepers = r.Of(FeatureKind.Sleepers).Count(),
    grease = r.Of(FeatureKind.Grease).Count(),
    r.Weather,
    map = map is null ? null : Path.GetFullPath(map),
};

// Generates many routes per tier and reports ranges: the tier-progression check (App. B.9).
static object SweepRoutes(RouteTuning t, int seeds) => Enum.GetValues<RouteTier>().Select(tier =>
{
    var routes = Enumerable.Range(1, seeds).Select(s => RouteGenerator.Generate(t, tier, (ulong)s)).ToList();
    return new
    {
        tier = tier.ToString(),
        lengthKm = new[] { routes.Min(r => r.Length), routes.Max(r => r.Length) }.Select(v => Math.Round(v / 1000, 1)),
        dawnMinutes = new[] { routes.Min(r => r.DawnSeconds), routes.Max(r => r.DawnSeconds) }.Select(v => Math.Round(v / 60)),
        meanSleepers = Math.Round(routes.Average(r => r.Of(FeatureKind.Sleepers).Count()), 1),
        meanGrease = Math.Round(routes.Average(r => r.Of(FeatureKind.Grease).Count()), 1),
        meanFacilities = Math.Round(routes.Average(r => r.Of(FeatureKind.Facility).Count()), 1),
        weakBridgeShare = Math.Round(routes.SelectMany(r => r.Of(FeatureKind.Bridge)).DefaultIfEmpty().Average(b => b is { MaxCars: > 0 } ? 1.0 : 0), 2),
        meanClimbM = Math.Round(routes.Average(r => r.Line.Segments.Where(s => s.GradePercent > 0).Sum(s => s.Length * s.GradePercent / 100))),
    };
}).ToList();

static object LineInfo(RailLine line, double every) => new
{
    name = line.Name,
    lengthM = line.Length,
    segments = line.Segments.Count,
    profile = Enumerable.Range(0, (int)(line.Length / every) + 1).Select(i => line.Sample(i * every)).Select(p => new
    {
        s = p.Distance,
        x = Math.Round(p.Position.X, 1),
        y = Math.Round(p.Position.Y, 2),
        z = Math.Round(p.Position.Z, 1),
        gradePct = p.GradePercent,
        radiusM = p.Curvature == 0 ? 0 : Math.Round(1 / p.Curvature),
    }),
};

// Runs the train along a line with fixed controls and reports every 10 s: the quickest way
// to answer "can a train this long make this climb from here?" without a window.
static object Drive(TrainTuning t, RailLine line, int cars, double start, double from, double throttle, int seconds)
{
    var consist = Consist.Uniform(t, cars, 1);
    var run = new TrainOnLine(new TrainDynamics(consist), line, start < 0 ? consist.LengthMetres : start);
    run.Dynamics.Velocity = from;
    var controls = new TrainControls { Throttle = throttle, Reverser = 1 };
    var samples = new List<object>();
    for (int tick = 0; tick <= seconds * SimConstants.TickRate; tick++)
    {
        if (tick % (10 * SimConstants.TickRate) == 0)
            samples.Add(new
            {
                t = tick / SimConstants.TickRate,
                s = Math.Round(run.Dynamics.Distance, 1),
                v = Math.Round(run.Dynamics.Velocity, 2),
                band = SpeedBands.Classify(t, run.Dynamics.Velocity).ToString(),
                gradePct = Math.Round(run.AverageGrade(), 2),
            });
        if (run.AtEndOfLine && tick > 0)
            break;
        run.Step(SimConstants.TickSeconds, controls);
    }
    return new { line = line.Name, cars, massT = consist.MassTonnes, samples };
}

// Renders a greybox frame to PNG with no window. On machines without a GPU this uses Mesa lavapipe.
static object Screenshot(TrainTuning t, string content, string[] args)
{
    string view = Str(args, "--view", "trackside");
    string lineName = Str(args, "--line", "test-loop");
    int cars = (int)Opt(args, "--cars", 6);
    int width = (int)Opt(args, "--width", 640), height = (int)Opt(args, "--height", 360), scale = (int)Opt(args, "--scale", 2);
    string output = Str(args, "--out", $"out/shots/{view}.png");

    // --route tier:seed generates the night in memory; --coaling stops the train at its coaling tower, chute pouring.
    Route? generated = Str(args, "--route", "") is { Length: > 0 } spec
        ? RouteGenerator.Generate(DataFile.Load<RouteTuning>(Path.Combine(content, RouteTuning.File)), Route.ParseSpec(spec).Tier, Route.ParseSpec(spec).Seed)
        : null;
    var line = generated?.Build() ?? RailLine.Load(Path.Combine(content, "lines", lineName + ".json"));
    var consist = Consist.Uniform(t, cars, 1);
    DarkTerritory.Sim.Run.Run? run = null;
    double at = Opt(args, "--at", 1200);
    RouteFeature? tower = generated?.Of(FeatureKind.Facility).FirstOrDefault(f => f.Facility == FacilityKind.CoalingTower);
    if (args.Contains("--coaling") && generated is not null && tower is not null)
    {
        run = new DarkTerritory.Sim.Run.Run(DataFile.Load<DarkTerritory.Sim.Run.RunTuning>(Path.Combine(content, DarkTerritory.Sim.Run.RunTuning.File)), generated);
        at = run.ChuteAt(tower, line).SpoutAlong + t.Geometry.EngineLength - t.Geometry.Engine.TenderLength / 2;
        int index = generated.Of(FeatureKind.Facility).ToList().IndexOf(tower);
        run.Mirror(DarkTerritory.Sim.Run.RunPhase.AtFacility, DarkTerritory.Sim.Run.RunEnd.None, 900, index, true, [.. Enumerable.Repeat(200.0, run.FacilityCount)]);
    }
    var train = new TrainOnLine(new TrainDynamics(consist), line, at);
    // --cut N: cut behind car N and pull the engine forward, to see a split train.
    if (Opt(args, "--cut", -1) is var cutAt and >= 0)
    {
        train.Uncouple((int)cutAt);
        for (int i = 0; i < SimConstants.TickRate * 12; i++)
            train.Step(SimConstants.TickSeconds, new TrainControls { Throttle = i < SimConstants.TickRate * 6 ? 1 : 0, Brake = i < SimConstants.TickRate * 6 ? 0 : 1, Reverser = 1 });
    }
    var camera = Views.Get(view, train, (int)Opt(args, "--car", 2));
    // --cam s,lateral,height --target s,lateral,height: place the camera anywhere by line coordinates.
    if (Str(args, "--cam", "") is { Length: > 0 } cam)
    {
        Double3 At(string spec)
        {
            var p = spec.Split(',').Select(double.Parse).ToArray();
            var t = line.Sample(p[0]);
            var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            return t.Position + right * p[1] + Double3.Up * p[2];
        }
        camera = Camera.LookAt(At(cam), At(Str(args, "--target", cam)), (float)Opt(args, "--fov", 65));
    }
    string routeFile = Path.Combine(content, "lines", lineName + ".route.json");
    var route = generated ?? (File.Exists(routeFile) ? DataFile.Load<Route>(routeFile) : null);

    var clock = Stopwatch.StartNew();
    using var gpu = new GpuContext("dt screenshot");
    using var renderer = new GreyboxRenderer(gpu, width, height);
    var mesh = new MeshBuilder();
    new GreyboxScene
    {
        Route = route,
        Run = run,
        Time = 0.37,
        Enemies = args.Contains("--threats") ? Staging.Threats(train) : null,
        Bodies = args.Contains("--bodies") ? Staging.Bodies(train, content).All : null
    }.Build(mesh, train, camera.Position);
    var lighting = Views.Lighting(train);
    if (route is not null)
        lighting.FogDensity = (float)route.Weather.FogDensity;
    var pixels = renderer.Render(mesh, camera, lighting, lighting.FogColor);
    PngWriter.Write(output, pixels, width, height, scale);
    return new { path = Path.GetFullPath(output), view, trainAt = Math.Round(at, 1), device = gpu.DeviceName, triangles = mesh.Count / 3, width = width * scale, height = height * scale, ms = clock.ElapsedMilliseconds };
}

// The designer's editor: tuning and routes in a local web page (T18). --screenshot captures both pages headless.
static int Edit(string content, string[] args)
{
    using var server = new DarkTerritory.Editor.EditorServer(content, (int)Opt(args, "--port", 0));
    server.Start();
    if (Str(args, "--screenshot", "") is { Length: > 0 } shot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(shot))!);
        var script = Path.Combine(Path.GetDirectoryName(content)!, "tools", "editor-shot.cjs");
        var psi = new ProcessStartInfo("node", [script, server.Url, Path.GetFullPath(shot)]) { RedirectStandardError = true };
        psi.Environment["NODE_PATH"] = Environment.GetEnvironmentVariable("NODE_PATH") ?? "/opt/node22/lib/node_modules";
        using var node = Process.Start(psi)!;
        string errors = node.StandardError.ReadToEnd();
        node.WaitForExit();
        return Print(new { tuning = Path.GetFullPath(shot.Replace(".png", "-tuning.png")), routes = Path.GetFullPath(shot.Replace(".png", "-routes.png")), exit = node.ExitCode, errors });
    }
    Console.WriteLine($"Dark Territory editor: {server.Url}  (Ctrl+C to stop)");
    Console.WriteLine("Edits save into content/ in place; a running game picks them up.");
    var done = new ManualResetEventSlim();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.Set(); };
    done.Wait();
    return 0;
}

// Renders a staged moment through the real mixer to a WAV, and measures every tell against the bed.
static object RenderAudio(string content, string[] args)
{
    string scenario = Str(args, "--scenario", "chaos");
    if (Str(args, "--listener", "") == "all")
        return DarkTerritory.Game.Sound.AudioBench.Sweep(content, scenario, (int)Opt(args, "--cars", 20), Opt(args, "--speed", 22), Opt(args, "--seconds", 6));
    string output = Str(args, "--out", $"out/audio/{scenario}.wav");
    var clock = Stopwatch.StartNew();
    var (report, mix) = DarkTerritory.Game.Sound.AudioBench.Render(content, scenario, (int)Opt(args, "--cars", 20), Opt(args, "--speed", 22),
        (int)Opt(args, "--listener", 5), Opt(args, "--seconds", 6));
    Ballast.Audio.Wav.Write(output, mix);
    // The picture of it: a spectrogram beside the WAV, for looking at bands without listening.
    string picture = Path.ChangeExtension(output, ".png");
    PngWriter.Write(picture, DarkTerritory.Game.Sound.Spectrogram.Render(mix, 800, 300), 800, 300, 1);
    return new { path = Path.GetFullPath(output), spectrogram = Path.GetFullPath(picture), report, ms = clock.ElapsedMilliseconds };
}

static string Str(string[] args, string name, string fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}

static double Opt(string[] args, string name, double fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? double.Parse(args[i + 1]) : fallback;
}

static int Print(object value)
{
    Console.WriteLine(JsonSerializer.Serialize(value, DataFile.Options));
    return 0;
}

static int Usage()
{
    Console.Error.WriteLine("""
        usage: dt <command>
          train table                              spec table (B.4–B.6) as produced by current tuning
          train stop <cars> [--from v] [--load l] [--grade g]
          train climb <cars> <grade%> [--from v] [--load l]
          boiler table                             spec B.6 burn, endurance, rebuild as produced by boiler tuning
          boiler run <cars> [--seconds t] [--throttle 0..1] [--fire-at p | --no-fireman] [--pressure p] [--firebox u] [--vent]
          line info <name> [--every m]             position/grade profile of content/lines/<name>.json
          line drive <name> [--cars n] [--start s] [--from v] [--throttle 0..1] [--seconds t]
          screenshot [--view trackside|roof|cab|chase|ahead] [--line name] [--cars n] [--at s] [--car i] [--cut n]
                     [--cam s,lateral,height --target s,lateral,height --fov deg]   camera by line coordinates
                     [--width w] [--height h] [--scale k] [--out file.png] [--threats]   --threats stages one of each enemy
                     [--route tier:seed [--coaling]]   a generated night; --coaling stops at its coaling tower, chute pouring
                     [--bodies]   crates, a lamp and a crewmate's body on the roofs, settled by the physics
          route gen [--tier local|frontier|deadLines|deepTerritory] [--seed n] [--name generated] [--map file.png]
                     writes content/lines/<name>.json (+ .route.json) and a map; try `screenshot --line generated`
          route sweep [--seeds n]                  generate n routes per tier and report ranges
          harness [--bots n] [--cars n] [--seconds t] [--seed s] [--latency s] [--jitter s] [--loss 0..1] [--line name | --route tier:seed]
                     [--enemies] [--no-combat] [--no-boiler] [--udp | --online]   --udp: real sockets on localhost instead of the simulated link;
                     --online: every bot joins a lobby on the fake Steam and plays over relayed P2P
                     host + bot clients over a simulated network; reports prediction error, bandwidth, deaths,
                     and with --enemies the director's spawns, punishes, deaths by cause and fairness audit
          online check                             is Steam reachable from here (signed-in user, or what's missing)
          audio render [--scenario bed|tells|chaos] [--cars n] [--speed v] [--listener car (0 = cab) | all] [--seconds t] [--out file.wav]
                     renders through the mixer to a WAV and a spectrogram PNG, and reports each tell's margin over the bed (spec A.3)
          edit [--port p] [--screenshot file.png]   the designer's editor (tuning + routes) at http://127.0.0.1:<port>/
          voice bench [--car n (0 = cab)] [--z m] [--radio] [--latency s --jitter s --loss 0..1]
                     one speaker to a listener on car 3 through host routing, Opus and the mixer (spec A.5)
        """);
    return 2;
}
