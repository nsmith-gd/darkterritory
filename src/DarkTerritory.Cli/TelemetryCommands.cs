using System.Globalization;
using System.Text.Json;
using Ballast;
using DarkTerritory.Dev.Telemetry;
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

    /// <summary><c>dt telemetry record [--route frontier:7] [--bots 4] [--cars 6] [--seconds 600] [--seed 1] [--me n] [--out out/telemetry]</c>.</summary>
    public static object Record(string content, string[] args)
    {
        var routeTuning = RouteTuning.Load(content);
        var route = DarkTerritory.Sim.LineGen.Routes.Generate(content, Str(args, "--route", "frontier:7"), (int)Opt(args, "--cars", 6));
        int bots = (int)Opt(args, "--bots", 4), cars = (int)Opt(args, "--cars", 6);
        double seconds = Opt(args, "--seconds", 600);
        int me = (int)Opt(args, "--me", -1);
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
                    // --me n (note 917): record as crewmate n's machine, a player among bots, so the night has a player's firsts
                    // (move, job) and their own deaths, as a playtester's would.
                    recorder.NightBegin(world, "host", humans: me > 0 ? 1 : 0, bots: crew.Count - (me > 0 ? 1 : 0), me: me);
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

    /// <summary>
    /// <c>dt telemetry summary &lt;dir&gt; [--out out/telemetry/summary.json] [--tables out/telemetry/tables]</c> (queue #627, note
    /// 917): every session file under the folder read back and summed up (<see cref="TelemetrySummary"/>), written and printed;
    /// <c>--tables</c> the same as flat row tables, a file each, for the playtest dashboard.
    /// </summary>
    public static object Summary(string[] args)
    {
        string dir = Positional(args, 2) ?? "out/telemetry";
        var (sessions, skipped) = TelemetryFiles.Read(dir);
        var summary = TelemetrySummary.Summarise(sessions, skipped);
        string output = Str(args, "--out", "out/telemetry/summary.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, summary.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        summary["written"] = Path.GetFullPath(output);
        // --tables dir: the same as flat row tables, a JSON file each, for the playtest dashboard's datasets.
        if (Str(args, "--tables", "") is { Length: > 0 } tables)
        {
            Directory.CreateDirectory(tables);
            foreach (var (name, rows) in TelemetrySummary.Tables(summary))
                File.WriteAllText(Path.Combine(tables, name + ".json"), rows.ToJsonString());
            summary["tables"] = Path.GetFullPath(tables);
        }
        return summary;
    }

    /// <summary>
    /// <c>dt telemetry map &lt;session.jsonl&gt; [--out out/telemetry/map]</c> (queue #627, note 917): each night of a session on its
    /// route's map (the <c>linegen</c> map, its line rebuilt from the route the night names): where each death was (red, its
    /// cause and minute), where the player quit (amber), and the encounters (violet, at the train's km when they began).
    /// </summary>
    public static object Map(string content, string[] args)
    {
        string file = Positional(args, 2) ?? throw new ArgumentException("dt telemetry map <session.jsonl>");
        var (session, _) = TelemetryFiles.ReadFile(file);
        string dir = Str(args, "--out", "out/telemetry/map");
        Directory.CreateDirectory(dir);
        var nights = new List<object>();
        foreach (var n in TelemetrySummary.NightsOf(session))
        {
            // The route's name is RunParameters.RouteId's, "frontier-7": the spec dt and the game take is "frontier:7".
            int dash = n.Route.LastIndexOf('-');
            if (dash <= 0)
            {
                nights.Add(new { n.Number, n.Route, skipped = "no route to draw" });
                continue;
            }
            string spec = $"{n.Route[..dash]}:{n.Route[(dash + 1)..]}";
            var route = DarkTerritory.Sim.LineGen.Routes.Generate(content, spec, Math.Max(1, n.Cars));
            if (route.Plan is not { } plan)
            {
                nights.Add(new { n.Number, n.Route, skipped = "a hand-made route has no plan to draw" });
                continue;
            }
            var line = route.Build();
            bool Mine(System.Text.Json.Nodes.JsonObject l) => TelemetryFiles.Int(l, "night") == n.Number;
            // Where the train was at a second of the night: between the minutes' km (the night starts in the yard, at the first's).
            var minutes = session.Of("minute").Where(Mine)
                .Select(m => (S: (TelemetryFiles.Num(m, "minute") ?? 0) * 60, Km: TelemetryFiles.Num(m, "km") ?? 0)).OrderBy(m => m.S).ToList();
            double KmAt(double s)
            {
                if (minutes.Count == 0)
                    return n.Km;
                if (s <= minutes[0].S)
                    return minutes[0].Km;
                for (int i = 1; i < minutes.Count; i++)
                    if (s <= minutes[i].S)
                        return minutes[i - 1].Km + (minutes[i].Km - minutes[i - 1].Km) * (s - minutes[i - 1].S) / (minutes[i].S - minutes[i - 1].S);
                return n.Km;
            }
            var deaths = session.Of("death").Where(Mine).Select(d => (Km: TelemetryFiles.Num(d, "km") ?? KmAt(TelemetryFiles.Num(d, "s") ?? 0),
                Cause: TelemetryFiles.Str(d, "cause") ?? "?", Minute: (TelemetryFiles.Num(d, "s") ?? 0) / 60)).ToList();
            var quits = session.Of("quit").Where(Mine).Select(q => (Km: KmAt(TelemetryFiles.Num(q, "s") ?? 0), How: TelemetryFiles.Str(q, "how") ?? "?",
                Minute: (TelemetryFiles.Num(q, "s") ?? 0) / 60)).ToList();
            var encounters = session.Of("encounter").Where(Mine).Select(e => (Km: KmAt(TelemetryFiles.Num(e, "s") ?? 0), Kind: TelemetryFiles.Str(e, "kind") ?? "?"))
                .ToList();
            string path = Path.Combine(dir, $"{session.Id}-night{n.Number}.png");
            DarkTerritory.Game.LineGen.PlanImages.Map(plan, line, path, overlay: (c, at) =>
            {
                foreach (var e in encounters)
                {
                    var (x, y) = at(e.Km * 1000);
                    c.Dot(x, y, 4, (170, 120, 230));
                }
                foreach (var q in quits)
                {
                    var (x, y) = at(q.Km * 1000);
                    c.Rect(x - 7, y - 7, x + 7, y + 7, (250, 180, 40));
                    c.Text(x + 10, y - 3, $"QUIT {q.How} {q.Minute:0}m", (250, 190, 70), 2);
                }
                foreach (var d in deaths)
                {
                    var (x, y) = at(d.Km * 1000);
                    c.Dot(x, y, 7, (235, 60, 50));
                    c.Text(x + 10, y + 8, $"{d.Cause} {d.Minute:0}m", (240, 110, 100), 2);
                }
                c.Text(10, c.Height - 26, $"night {n.Number}  {n.End}  {n.Minutes:0} min  {n.Km:0.0} km   red: deaths ({deaths.Count})   amber: quits ({quits.Count})   violet: encounters ({encounters.Count})",
                    (230, 230, 230), 2);
            });
            nights.Add(new { n.Number, n.Route, n.End, map = Path.GetFullPath(path), deaths = deaths.Count, quits = quits.Count, encounters = encounters.Count });
        }
        return new { session = session.Id, nights };
    }

    /// <summary>The argument at <paramref name="at"/>, if it's there and isn't an option.</summary>
    static string? Positional(string[] args, int at) => args.Length > at && !args[at].StartsWith("--", StringComparison.Ordinal) ? args[at] : null;

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
