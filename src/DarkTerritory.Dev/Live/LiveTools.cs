using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using DarkTerritory.Dev.Replay;
using DarkTerritory.Sim;

namespace DarkTerritory.Dev.Live;

/// <summary>
/// How this process starts another dt for the <c>dt</c> tool (note 524): the program, what goes before a command's own
/// arguments (dt.dll, under the dotnet host; the edition and mods this dt was started with), and the folder it runs in.
/// </summary>
public sealed record DtLauncher(string Program, IReadOnlyList<string> Prefix, string WorkingDirectory)
{
    /// <summary>Runs dt with <paramref name="args"/>, its stdin closed (the protocol's is ours), till it ends or the time's up.</summary>
    public (int Exit, string Out, string Err, bool TimedOut) Run(IReadOnlyList<string> args, TimeSpan timeout)
    {
        var start = new ProcessStartInfo(Program)
        {
            WorkingDirectory = WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in Prefix.Concat(args))
            start.ArgumentList.Add(a);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"couldn't start {Program}");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        bool done = process.WaitForExit(timeout);
        if (!done)
            process.Kill(entireProcessTree: true);
        process.WaitForExit();
        return (done ? process.ExitCode : -1, output.Result, error.Result, !done);
    }
}

/// <summary>
/// The live-control tools (ARCHITECTURE §8 note 524) over one <see cref="LiveSession"/>: a night hosted, stepped, read,
/// insisted on and drawn; a recording opened; and the rest of dt as a child process.
/// </summary>
public static class LiveTools
{
    /// <summary>What a client's model is told of the server as a whole, at <c>initialize</c>.</summary>
    public const string Instructions = """
        Dark Territory's dt, live: one night at a time, hosted headless with a bot crew (night_start) or a recorded night
        played again (replay_open). Step it on (night_step: it says what happened since the last step: spawns, incidents,
        the crew, the threats), read it (night_state), say what comes (night_insist), and look at it (shot: a PNG, framed on
        a view, a crewmate's eyes, or a subject: subject:gaunt, subject:crew:2, subject:car:3, subject:engine). The rest of
        the CLI is the dt tool (any command it doesn't know, e.g. help, answers with its usage). Time is the sim's: a step of 60 s is 1,800 ticks, and takes real seconds.
        """;

    static JsonObject Schema(string json) => (JsonObject)JsonNode.Parse(json)!;

    public static IReadOnlyList<McpTool> For(LiveSession live, DtLauncher? dt = null)
    {
        var tools = new List<McpTool>
        {
            new("night_start",
                "Host a night headless (closing any night in hand): the route as tier:seed, the train's cars, a bot crew (the first bot drives; the host's own player stands idle), the threats on or off, and kinds to insist on. Returns the night's state.",
                Schema("""
                {
                  "type": "object",
                  "properties": {
                    "route": { "type": "string", "description": "tier:seed (local, frontier, deadLines, deepTerritory), default frontier:7" },
                    "cars": { "type": "integer", "minimum": 1, "maximum": 30, "description": "cars behind the engine, default 6" },
                    "bots": { "type": "integer", "minimum": 0, "maximum": 15, "description": "bot crewmates, default 3 (the crew cap less one at most)" },
                    "enemies": { "type": "boolean", "description": "the threats on (default true)" },
                    "insist": { "type": "array", "items": { "type": "string" }, "description": "only these enemy kinds come (gaunt, cinderHound, carHugger...), whenever they can be placed" },
                    "insistEvery": { "type": "number", "description": "with insist: seconds after one's gone before it's sent again (default 10)" },
                    "record": { "type": "boolean", "description": "record the night (out/recordings) for replay_open or dt replay" }
                  },
                  "additionalProperties": false
                }
                """),
                a => Json(live.Start(new NightStart(
                    a.String("route") ?? "frontier:7",
                    a.Int("cars", 1, 30) ?? 6,
                    a.Int("bots", 0, 15) ?? 3,
                    a.Bool("enemies") ?? true)
                {
                    Insist = a.Strings("insist") is { } kinds ? EnemyNames.ParseAll(kinds) : null,
                    InsistEvery = a.Number("insistEvery") ?? 10,
                    Record = a.Bool("record") ?? false,
                }))),
            new("night_step",
                "Step the night on (a replay plays on): seconds or ticks (30 a second), stopping sooner at 'until'. Returns the tick, the train, the crew (post, car, health), the threats about (kind, phase, metres from the train) and what happened since the last step (spawns, incidents).",
                Schema("""
                {
                  "type": "object",
                  "properties": {
                    "seconds": { "type": "number", "minimum": 0, "maximum": 3600, "description": "sim seconds to step (default 1)" },
                    "ticks": { "type": "integer", "minimum": 0, "maximum": 108000, "description": "ticks to step, instead of seconds" },
                    "until": { "type": "string", "description": "stop sooner: enemy (a new one about), incident, death, facility (stopped at one), or an enemy kind (one about)" }
                  },
                  "additionalProperties": false
                }
                """),
                a =>
                {
                    if (a.Has("seconds") && a.Has("ticks"))
                        throw new BadArgumentsException("night_step takes seconds or ticks, not both");
                    int ticks = a.Int("ticks", 0, 108_000) ?? (int)Math.Round((a.Number("seconds") ?? 1) * SimConstants.TickRate);
                    if (ticks < 0 || ticks > 108_000)
                        throw new BadArgumentsException("night_step: seconds must be from 0 to 3600");
                    return Json(live.Step(ticks, a.String("until")));
                }),
            new("night_state",
                "The night's state in full: the train and each car, the run and the next stop, the crew (where they are), the threats, the director (pressure, budget, posts), and every incident tonight.",
                Schema("""{ "type": "object", "properties": {}, "additionalProperties": false }"""),
                _ => Json(live.State())),
            new("night_insist",
                "Say what comes, from now on: only these enemy kinds, each whenever its spawn can place it and again 'every' seconds after the last one's gone (the director's own choices are skipped). An empty list hands the night back to the director. Not on a replay; nothing comes in the fortress yard.",
                Schema("""
                {
                  "type": "object",
                  "properties": {
                    "kinds": { "type": "array", "items": { "type": "string" }, "description": "enemy kinds: gaunt, cinderHound, carHugger, whistler, ribbit..." },
                    "every": { "type": "number", "minimum": 0, "description": "seconds after one's gone before it's sent again (default 10)" }
                  },
                  "required": ["kinds"],
                  "additionalProperties": false
                }
                """),
                a => Json(live.Insist(EnemyNames.ParseAll(a.Strings("kinds")!), a.Number("every") ?? 10))),
            new("replay_open",
                "Open a recorded night (.dtrec: a dev build records every night it hosts; night_start with record: true makes one) and play it to a moment, checked tick for tick against the recording. After it, night_state and shot read the replayed world and night_step plays on.",
                Schema("""
                {
                  "type": "object",
                  "properties": {
                    "file": { "type": "string", "description": "the recording's path" },
                    "to": { "type": ["string", "integer"], "description": "a tick (1830) or minutes:seconds into the night (1:01); default the end" }
                  },
                  "required": ["file"],
                  "additionalProperties": false
                }
                """),
                a =>
                {
                    uint? to;
                    try
                    {
                        to = a.Text("to") is { Length: > 0 } at ? NightReplay.TickAt(at) : null;
                    }
                    catch (Exception e) when (e is FormatException or OverflowException)
                    {
                        throw new BadArgumentsException($"replay_open: 'to' is a tick or minutes:seconds, not {a.Text("to")}");
                    }
                    return Json(live.OpenReplay(a.String("file")!, to));
                }),
            new("shot",
                "Draw the night as it stands (a PNG, returned and saved under out/live). view: a named view (chase, roof, cab, trackside, ahead, gap...), eye:N (through crewmate N's eyes), or subject:WHO framed on someone or something: an enemy kind (the nearest one about: subject:gaunt), subject:enemy:ID, subject:crew:N, subject:car:N, subject:engine. A subject that isn't there is an error naming what is.",
                Schema("""
                {
                  "type": "object",
                  "properties": {
                    "view": { "type": "string", "description": "default chase" },
                    "width": { "type": "integer", "minimum": 64, "maximum": 3840, "description": "default 1280" },
                    "height": { "type": "integer", "minimum": 64, "maximum": 2160, "description": "default 720" },
                    "car": { "type": "integer", "minimum": 0, "description": "the car a named view is on (default 2)" },
                    "out": { "type": "string", "description": "where to save it (default out/live/<tick>-<view>.png)" },
                    "inline": { "type": "integer", "minimum": 0, "maximum": 3840, "description": "the widest the image handed back is (default 960: a 1280 shot comes back at 640, the file has it whole); 0 for the file alone" }
                  },
                  "additionalProperties": false
                }
                """),
                a =>
                {
                    var (png, info) = live.Shot(a.String("view") ?? "chase", a.Int("width", 64, 3840) ?? 1280, a.Int("height", 64, 2160) ?? 720,
                        a.Int("car", 0, 1000) ?? 2, a.String("out"), a.Int("inline", 0, 3840) ?? 960);
                    return new ToolResult(png is null ? [new ToolText(info.ToJsonString())] : [new ToolText(info.ToJsonString()), new ToolImage(png)]);
                }),
        };
        if (dt is not null)
            tools.Add(new("dt",
                "Run any dt command (the CLI's own JSON out): e.g. [\"linegen\", \"generate\", \"--route\", \"frontier:7\"], [\"screenshot\", \"--view\", \"roof\"], [\"replay\", \"list\"]. Returns its stdout (and stderr, if it failed). [\"help\"] fails with the usage: every command.",
                Schema("""
                {
                  "type": "object",
                  "properties": {
                    "args": { "type": "array", "items": { "type": "string" }, "description": "dt's arguments, the command first" },
                    "timeoutSeconds": { "type": "number", "minimum": 1, "maximum": 7200, "description": "default 600" }
                  },
                  "required": ["args"],
                  "additionalProperties": false
                }
                """),
                a => Dt(dt, a.Strings("args")!, a.Number("timeoutSeconds") ?? 600)));
        return tools;
    }

    static ToolResult Json(JsonObject state) => ToolResult.Text(state.ToJsonString());

    /// <summary>A dt's output past this is left in a file, its start returned: a client's context isn't a log.</summary>
    const int MostOut = 256 * 1024;

    static ToolResult Dt(DtLauncher dt, IReadOnlyList<string> args, double timeoutSeconds)
    {
        if (args is ["mcp", ..])
            throw new ArgumentException("dt mcp is this server: its tools are here already");
        var (exit, stdout, stderr, timedOut) = dt.Run(args, TimeSpan.FromSeconds(timeoutSeconds));
        string text = stdout;
        if (text.Length > MostOut)
        {
            string file = Path.GetFullPath(Path.Combine("out", "live", $"dt-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, stdout);
            text = $"{stdout[..MostOut]}\n… ({stdout.Length:N0} characters; all of it in {file})";
        }
        if (exit == 0)
            return ToolResult.Text(text);
        string why = timedOut ? $"timed out after {timeoutSeconds:0} s" : $"exit {exit}";
        return ToolResult.Failed($"dt {string.Join(' ', args)}: {why}\n{text}{(stderr.Length > 0 ? $"\n--- stderr ---\n{stderr[^Math.Min(stderr.Length, 16 * 1024)..]}" : "")}");
    }
}
