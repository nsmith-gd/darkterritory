using System.Globalization;
using System.Text.Json;
using Ballast;
using DarkTerritory.Game.Telemetry;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;



/// <summary>
/// <c>dt telemetry</c> (queue #624, note 913): the playtest recorder, headless. <c>schema</c> prints the event schema;
/// <c>record</c> plays a bot night (the harness's crew over a clean loopback, as <c>dt playthrough --bots</c>) with the
/// recorder on the host's world, writes its session as JSON lines, and says what it wrote.
/// </summary>
static class TelemetryCommands
{
    public static object Schema() => new { version = TelemetrySchema.Version, common = TelemetrySchema.Common, events = TelemetrySchema.Events };

    /// <summary><c>dt telemetry record [--route frontier:7] [--bots 4] [--cars 6] [--seconds 600] [--seed 1] [--out out/telemetry]</c>.</summary>
    public static object Record(string content, string[] args)
    {
        var routeTuning = RouteTuning.Load(content);
        var route = DarkTerritory.Sim.LineGen.Routes.Generate(content, Str(args, "--route", "frontier:7"), (int)Opt(args, "--cars", 6));
        int bots = (int)Opt(args, "--bots", 4), cars = (int)Opt(args, "--cars", 6);
        double seconds = Opt(args, "--seconds", 600);
        var c = AuditCommands.Load(content);
        double yard = route.GateOr(routeTuning.YardLength);
        string dir = Str(args, "--out", "out/telemetry");
        // The session's own clock is the night's (a bot night runs faster than real time): the lines say sim seconds.
        World? last = null;
        var sink = new MemoryTelemetrySink();
        var recorder = new SessionRecorder(sink, new FixedTelemetryGate(true, dir), clock: () => last is null ? 0 : last.Tick * SimConstants.TickSeconds);
        recorder.Begin(new SessionFacts("full", Dev: true, SessionFacts.ThisOs, Gpu: "headless", Vr: false, Resolution: "-", RenderScale: 1, VSync: false,
            FieldOfView: 0));
        Harness.Run(route.Build(), c.Train, c.Player, new HarnessOptions
        {
            Bots = bots,
            Cars = cars,
            Seconds = seconds,
            Seed = (int)Opt(args, "--seed", 1),
            Link = new Ballast.Net.LinkConditions(0, 0, 0),
            StartDistance = c.Run.DepartFrom(yard, Consist.Uniform(c.Train, cars, 1).LengthMetres),
            Combat = c.Combat,
            Enemies = c.Enemies,
            Route = route,
            Run = c.Run,
            Facilities = DataFile.Load<DarkTerritory.Sim.Run.FacilityTuning>(Path.Combine(content, DarkTerritory.Sim.Run.FacilityTuning.File)),
            Holdouts = DataFile.Load<DarkTerritory.Sim.Run.HoldoutTuning>(Path.Combine(content, DarkTerritory.Sim.Run.HoldoutTuning.File)),
            Sight = DataFile.Load<SightTuning>(Path.Combine(content, SightTuning.File)),
            YardLength = yard,
            Observe = (_, crew, world) =>
            {
                if (last is null)
                {
                    last = world;
                    recorder.NightBegin(world, "host", humans: 0, bots: crew.Count, me: -1);
                }
                // A frame a tick at the 60 fps a player would have (no window here), so the frame fields aren't empty.
                recorder.Frame(1000.0 / 60);
                recorder.Tick(world, [.. crew.Select((x, i) => (i + 1, x.State))]);
            },
            Until = world => world.Run?.Over == true,
        }, c.Boiler);
        if (last is null)
            throw new InvalidOperationException("the night never started");
        recorder.NightEnd(last);
        recorder.Quit("app", last);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"{recorder.SessionId}.jsonl");
        File.WriteAllLines(path, sink.Lines);
        var types = sink.Lines.Select(l => JsonDocument.Parse(l).RootElement.GetProperty("type").GetString()!)
            .GroupBy(t => t).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count());
        return new
        {
            path,
            schema = TelemetrySchema.Version,
            lines = recorder.Lines,
            bytes = recorder.Bytes,
            nightSeconds = last.Tick * SimConstants.TickSeconds,
            km = last.Train.Dynamics.Distance / 1000,
            end = last.Run?.End.ToString(),
            types,
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
        return i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}
