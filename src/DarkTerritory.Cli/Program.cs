using System.Diagnostics;
using System.Text.Json;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

// `dt` — the headless command-line entry point. Everything an agent needs to inspect or verify
// the game without a window goes through here. Output is JSON unless stated otherwise.

var content = DataFile.FindContentRoot(Environment.CurrentDirectory);
var train = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
var player = DataFile.Load<PlayerTuning>(Path.Combine(content, PlayerTuning.File));
var boiler = DataFile.Load<BoilerTuning>(Path.Combine(content, BoilerTuning.File));

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
    ["harness", ..] => Print(Harness.Run(LoadLine(Str(args, "--line", "test-loop")), train, player, new HarnessOptions
    {
        Bots = (int)Opt(args, "--bots", 8),
        Cars = (int)Opt(args, "--cars", 10),
        Seconds = Opt(args, "--seconds", 120),
        Seed = (int)Opt(args, "--seed", 1),
        Link = new Ballast.Net.LinkConditions(Opt(args, "--latency", 0.09), Opt(args, "--jitter", 0.02), Opt(args, "--loss", 0.03)),
    }, args.Contains("--no-boiler") ? null : boiler)),
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

RailLine LoadLine(string name) => RailLine.Load(Path.Combine(content, "lines", name + ".json"));

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

    var line = RailLine.Load(Path.Combine(content, "lines", lineName + ".json"));
    var consist = Consist.Uniform(t, cars, 1);
    var train = new TrainOnLine(new TrainDynamics(consist), line, Opt(args, "--at", 1200));
    // --cut N: cut behind car N and pull the engine forward, to see a split train.
    if (Opt(args, "--cut", -1) is var cutAt and >= 0)
    {
        train.Uncouple((int)cutAt);
        for (int i = 0; i < SimConstants.TickRate * 12; i++)
            train.Step(SimConstants.TickSeconds, new TrainControls { Throttle = i < SimConstants.TickRate * 6 ? 1 : 0, Brake = i < SimConstants.TickRate * 6 ? 0 : 1, Reverser = 1 });
    }
    var camera = Views.Get(view, train, (int)Opt(args, "--car", 2));

    var clock = Stopwatch.StartNew();
    using var gpu = new GpuContext("dt screenshot");
    using var renderer = new GreyboxRenderer(gpu, width, height);
    var mesh = new MeshBuilder();
    new GreyboxScene().Build(mesh, train, camera.Position);
    var lighting = Views.Lighting(train);
    var pixels = renderer.Render(mesh, camera, lighting, lighting.FogColor);
    PngWriter.Write(output, pixels, width, height, scale);
    return new { path = Path.GetFullPath(output), view, device = gpu.DeviceName, triangles = mesh.Count / 3, width = width * scale, height = height * scale, ms = clock.ElapsedMilliseconds };
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
                     [--width w] [--height h] [--scale k] [--out file.png]
          harness [--bots n] [--cars n] [--seconds t] [--seed s] [--latency s] [--jitter s] [--loss 0..1] [--line name]
                     host + bot clients over a simulated network; reports prediction error, bandwidth, deaths
        """);
    return 2;
}
