using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkTerritory.Dev.Telemetry;

/// <summary>One recorded session read back (queue #627, note 917): its file, its <c>session</c> line, and every line in order.</summary>
public sealed record TelemetrySession(string Id, string Path, JsonObject? Session, IReadOnlyList<JsonObject> Lines)
{
    /// <summary>Its lines of one type, in order.</summary>
    public IEnumerable<JsonObject> Of(string type) => Lines.Where(l => TelemetryFiles.Str(l, "type") == type);

    /// <summary>The nights it began (<c>night</c> lines' numbers), in order.</summary>
    public IReadOnlyList<int> Nights => [.. Of("night").Select(l => TelemetryFiles.Int(l, "night") ?? 0).Distinct()];
}

/// <summary>
/// Reading note 913's session files (schema v1, one JSON object a line). A line that isn't JSON, or is of a schema newer than
/// this reader knows, is counted and skipped, never guessed at; a field the reader doesn't know is ignored (note 913: adding a
/// type or a field at the end is compatible).
/// </summary>
public static class TelemetryFiles
{
    /// <summary>The newest schema version this reader understands (note 913's <c>TelemetrySchema.Version</c>).</summary>
    public const int Schema = 1;

    /// <summary>Every <c>*.jsonl</c> under <paramref name="dir"/> (or that one file), a session each, by id; and how many lines were skipped.</summary>
    public static (IReadOnlyList<TelemetrySession> Sessions, int Skipped) Read(string dir)
    {
        var files = File.Exists(dir) ? [dir] : Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.jsonl", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : throw new DirectoryNotFoundException($"no telemetry at {dir}");
        var sessions = new List<TelemetrySession>();
        int skipped = 0;
        foreach (var f in files)
        {
            var (s, bad) = ReadFile(f);
            skipped += bad;
            if (s.Lines.Count > 0)
                sessions.Add(s);
        }
        // By session id, so a folder gives the same summary however its files are laid out.
        return ([.. sessions.OrderBy(s => s.Id, StringComparer.Ordinal)], skipped);
    }

    /// <summary>One session's file.</summary>
    public static (TelemetrySession Session, int Skipped) ReadFile(string path) => Parse(path, File.ReadLines(path));

    /// <summary>One session from its lines (tests, and <see cref="ReadFile"/>).</summary>
    public static (TelemetrySession Session, int Skipped) Parse(string path, IEnumerable<string> text)
    {
        var lines = new List<JsonObject>();
        int skipped = 0;
        foreach (var t in text)
        {
            if (string.IsNullOrWhiteSpace(t))
                continue;
            JsonObject? o;
            try
            {
                o = JsonNode.Parse(t) as JsonObject;
            }
            catch (JsonException)
            {
                o = null;
            }
            if (o is null || Int(o, "schema") is not { } v || v > Schema || Str(o, "type") is null)
            {
                skipped++;
                continue;
            }
            lines.Add(o);
        }
        var session = lines.FirstOrDefault(l => Str(l, "type") == "session");
        string id = (session is null ? null : Str(session, "session")) ?? System.IO.Path.GetFileNameWithoutExtension(path);
        return (new TelemetrySession(id, path, session, lines), skipped);
    }

    public static string? Str(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static double? Num(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    public static int? Int(JsonObject o, string key) => Num(o, key) is { } d ? (int)Math.Round(d) : null;

    public static bool? Bool(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;
}

/// <summary>
/// <c>dt telemetry summary &lt;dir&gt;</c> (queue #627, note 917; the plan, note 907): what a playtest's sessions say, as one JSON
/// object the director's dashboard reads. Sessions, nights and minutes; how nights end; where players quit; deaths by cause,
/// post and minute; encounters by kind and channel; the funnel (launch, a first night, first move, first job, first stop, first
/// delivery, a second night), counted in sessions; how the game ran; and a row a night. Nothing here names anyone: the
/// recorder never writes a name (note 913).
/// </summary>
public static class TelemetrySummary
{
    /// <summary>The funnel's steps, in order, and what reaching each means.</summary>
    public static readonly (string Step, string Means)[] Funnel =
    [
        ("launch", "the game ran, with consent"),
        ("night", "began a night"),
        ("move", "moved a metre from where they began"),
        ("job", "drove, fired a gun or sat at one"),
        ("stop", "the train stood at a stop"),
        ("delivery", "brought a night home"),
        ("secondNight", "began a second night"),
    ];

    public static JsonObject Summarise(IReadOnlyList<TelemetrySession> sessions, int skipped = 0)
    {
        var nights = sessions.SelectMany(NightsOf).ToList();
        var deaths = sessions.SelectMany(s => s.Of("death").Select(d => (s, d))).ToList();
        var encounters = sessions.SelectMany(s => s.Of("encounter")).ToList();
        var minutes = sessions.SelectMany(s => s.Of("minute")).ToList();
        // A quit mid-night (night > 0) is a player leaving the night; one between nights is just closing the game.
        var quits = sessions.SelectMany(s => s.Of("quit")).ToList();
        var midNight = quits.Where(q => (TelemetryFiles.Int(q, "night") ?? 0) > 0).ToList();

        return new JsonObject
        {
            ["schema"] = TelemetryFiles.Schema,
            ["sessions"] = sessions.Count,
            ["nights"] = nights.Count,
            ["minutes"] = Round(nights.Sum(n => n.Minutes), 1),
            ["skippedLines"] = skipped,
            ["builds"] = Count(sessions.Select(s => s.Session is { } x ? Short(TelemetryFiles.Str(x, "commit")) : "unknown")),
            ["editions"] = Count(sessions.Select(s => s.Session is { } x ? TelemetryFiles.Str(x, "edition") ?? "unknown" : "unknown")),
            ["vr"] = sessions.Count(s => s.Session is { } x && TelemetryFiles.Bool(x, "vr") == true),
            ["roles"] = Count(nights.Select(n => n.Role)),
            ["routes"] = Count(nights.Select(n => n.Route)),
            ["tiers"] = Count(nights.Select(n => n.Tier)),
            ["crew"] = Count(nights.Select(n => n.Humans + n.Bots).Select(c => c.ToString(CultureInfo.InvariantCulture))),
            ["ends"] = Count(nights.Select(n => n.End)),
            ["quits"] = new JsonObject
            {
                ["all"] = Count(quits.Select(q => TelemetryFiles.Str(q, "how") ?? "unknown")),
                ["midNight"] = midNight.Count,
                ["byMinute"] = ByMinute(midNight.Select(q => TelemetryFiles.Num(q, "s"))),
            },
            ["deaths"] = new JsonObject
            {
                ["total"] = deaths.Count,
                ["perNight"] = nights.Count == 0 ? 0 : Round((double)deaths.Count / nights.Count, 2),
                ["mine"] = deaths.Count(d => TelemetryFiles.Bool(d.d, "me") == true),
                ["byCause"] = Count(deaths.Select(d => TelemetryFiles.Str(d.d, "cause") ?? "unknown")),
                ["byPost"] = Count(deaths.Select(d => TelemetryFiles.Str(d.d, "post") ?? "unknown")),
                ["byMinute"] = ByMinute(deaths.Select(d => TelemetryFiles.Num(d.d, "s"))),
            },
            ["encounters"] = new JsonObject
            {
                ["total"] = encounters.Count,
                ["perNight"] = nights.Count == 0 ? 0 : Round((double)encounters.Count / nights.Count, 2),
                ["byKind"] = Count(encounters.Select(e => TelemetryFiles.Str(e, "kind") ?? "unknown")),
                ["byChannel"] = Count(encounters.Select(e => TelemetryFiles.Str(e, "channel") ?? "unknown")),
                ["afoot"] = encounters.Count(e => TelemetryFiles.Bool(e, "afoot") == true),
                ["grabs"] = encounters.Sum(e => TelemetryFiles.Int(e, "grabs") ?? 0),
                ["punishes"] = encounters.Sum(e => TelemetryFiles.Int(e, "punishes") ?? 0),
                ["byMinute"] = ByMinute(encounters.Select(e => TelemetryFiles.Num(e, "s"))),
            },
            ["incidents"] = Count(sessions.SelectMany(s => s.Of("incident")).Select(i => TelemetryFiles.Str(i, "kind") ?? "unknown")),
            ["funnel"] = new JsonArray([.. Funnel.Select(f => (JsonNode)new JsonObject
            {
                ["step"] = f.Step,
                ["means"] = f.Means,
                ["sessions"] = sessions.Count(s => Reached(s, f.Step)),
            })]),
            ["firsts"] = Firsts(sessions),
            ["purchases"] = Count(sessions.SelectMany(s => s.Of("purchase")).Select(p => TelemetryFiles.Str(p, "what") ?? "unknown")),
            ["rebinds"] = Count(sessions.SelectMany(s => s.Of("rebind")).Select(p => TelemetryFiles.Str(p, "control") ?? "unknown")),
            ["prompts"] = Prompts(sessions),
            ["performance"] = Performance(minutes),
            ["nightRows"] = new JsonArray([.. nights.Select(n => (JsonNode)n.ToJson())]),
        };
    }

    /// <summary>One night of one session, as its lines tell it.</summary>
    public sealed record Night(string Session, int Number, string Role, string Route, string Tier, int Cars, int Humans, int Bots, string End,
        double Minutes, double Km, int Deaths, int Encounters, string? QuitHow, double? QuitMinute)
    {
        public JsonObject ToJson() => new()
        {
            ["session"] = Session,
            ["night"] = Number,
            ["role"] = Role,
            ["route"] = Route,
            ["tier"] = Tier,
            ["cars"] = Cars,
            ["humans"] = Humans,
            ["bots"] = Bots,
            ["end"] = End,
            ["minutes"] = Round(Minutes, 1),
            ["km"] = Round(Km, 2),
            ["deaths"] = Deaths,
            ["encounters"] = Encounters,
            ["quit"] = QuitHow,
            ["quitMinute"] = QuitMinute is { } q ? Round(q, 1) : null,
        };
    }

    /// <summary>
    /// A session's nights. A night with no <c>nightEnd</c> (the game closed hard, or crashed) ends <c>unfinished</c>; one left with a
    /// quit before its end keeps the quit's way (note 913: the quit comes first, then the night's end).
    /// </summary>
    public static IEnumerable<Night> NightsOf(TelemetrySession s)
    {
        foreach (var n in s.Of("night"))
        {
            int no = TelemetryFiles.Int(n, "night") ?? 0;
            bool Mine(JsonObject l) => TelemetryFiles.Int(l, "night") == no;
            var end = s.Of("nightEnd").FirstOrDefault(Mine);
            var quit = s.Of("quit").FirstOrDefault(Mine);
            var lastMinute = s.Of("minute").Where(Mine).LastOrDefault();
            double seconds = (end is null ? null : TelemetryFiles.Num(end, "seconds"))
                ?? (quit is null ? null : TelemetryFiles.Num(quit, "s"))
                ?? (lastMinute is null ? 0 : (TelemetryFiles.Num(lastMinute, "minute") ?? 0) * 60);
            double km = (end is null ? null : TelemetryFiles.Num(end, "km")) ?? (lastMinute is null ? null : TelemetryFiles.Num(lastMinute, "km")) ?? 0;
            yield return new Night(s.Id, no, TelemetryFiles.Str(n, "role") ?? "unknown", TelemetryFiles.Str(n, "route") ?? "unknown",
                TelemetryFiles.Str(n, "tier") ?? "unknown", TelemetryFiles.Int(n, "cars") ?? 0, TelemetryFiles.Int(n, "humans") ?? 0,
                TelemetryFiles.Int(n, "bots") ?? 0,
                EndOf(end is null ? null : TelemetryFiles.Str(end, "end"), quit is not null),
                seconds / 60, km, s.Of("death").Count(Mine), s.Of("encounter").Count(Mine),
                quit is null ? null : TelemetryFiles.Str(quit, "how"),
                quit is null ? null : TelemetryFiles.Num(quit, "s") / 60);
        }
    }

    /// <summary>
    /// How a night ended: its <c>nightEnd</c>'s end; a night left mid-way (a quit before its end, the end then <c>None</c>) is
    /// <c>quit</c>; one with no end at all (closed hard, or a crash) <c>unfinished</c>.
    /// </summary>
    static string EndOf(string? end, bool quit) => quit && end is null or "None" ? "quit" : end ?? "unfinished";

    /// <summary>
    /// Every field of note 913's schema this summary reads, by type: <c>TelemetrySummaryTests</c> holds each to
    /// <c>TelemetrySchema</c>, so a field renamed or gone there fails here rather than reading as null.
    /// </summary>
    public static readonly (string Type, string Field)[] Reads =
    [
        ("session", "session"), ("session", "commit"), ("session", "edition"), ("session", "vr"),
        ("night", "night"), ("night", "role"), ("night", "route"), ("night", "tier"), ("night", "cars"), ("night", "humans"), ("night", "bots"),
        ("minute", "night"), ("minute", "minute"), ("minute", "km"), ("minute", "hitches"), ("minute", "p50"), ("minute", "p95"),
        ("minute", "p99"), ("minute", "pingMs"), ("minute", "loss"),
        ("death", "night"), ("death", "s"), ("death", "km"), ("death", "cause"), ("death", "post"), ("death", "me"),
        ("incident", "kind"),
        ("encounter", "night"), ("encounter", "s"), ("encounter", "kind"), ("encounter", "channel"), ("encounter", "afoot"),
        ("encounter", "grabs"), ("encounter", "punishes"),
        ("first", "night"), ("first", "what"), ("first", "s"),
        ("purchase", "what"), ("rebind", "control"), ("prompt", "name"), ("prompt", "acted"),
        ("nightEnd", "night"), ("nightEnd", "end"), ("nightEnd", "seconds"), ("nightEnd", "km"),
        ("quit", "how"), ("quit", "night"), ("quit", "s"),
    ];

    /// <summary>Whether a session reached a funnel step.</summary>
    public static bool Reached(TelemetrySession s, string step) => step switch
    {
        "launch" => true,
        "night" => s.Nights.Count >= 1,
        "secondNight" => s.Nights.Count >= 2,
        // A night brought home counts as a delivery even where the recorder had no `first` for it (a client's).
        "delivery" => s.Of("first").Any(f => TelemetryFiles.Str(f, "what") == "delivery")
            || s.Of("nightEnd").Any(e => TelemetryFiles.Str(e, "end") == "Delivered"),
        _ => s.Of("first").Any(f => TelemetryFiles.Str(f, "what") == step),
    };

    /// <summary>For each first (move, job, stop, delivery): how many sessions had it, and the median minute into their first night.</summary>
    static JsonObject Firsts(IReadOnlyList<TelemetrySession> sessions)
    {
        var o = new JsonObject();
        foreach (var what in new[] { "move", "job", "stop", "delivery" })
        {
            var at = sessions.Select(s => s.Of("first").FirstOrDefault(f => TelemetryFiles.Str(f, "what") == what) is { } f ? TelemetryFiles.Num(f, "s") : null)
                .Where(x => x is not null).Select(x => x!.Value / 60).ToList();
            o[what] = new JsonObject { ["sessions"] = at.Count, ["medianMinute"] = at.Count == 0 ? null : Round(Median(at), 1) };
        }
        return o;
    }

    /// <summary>Each prompt: how often shown, and the share acted on.</summary>
    static JsonObject Prompts(IReadOnlyList<TelemetrySession> sessions)
    {
        var o = new JsonObject();
        foreach (var g in sessions.SelectMany(s => s.Of("prompt")).GroupBy(p => TelemetryFiles.Str(p, "name") ?? "unknown").OrderBy(g => g.Key, StringComparer.Ordinal))
            o[g.Key] = new JsonObject { ["shown"] = g.Count(), ["acted"] = Round(g.Count(p => TelemetryFiles.Bool(p, "acted") == true) / (double)g.Count(), 2) };
        return o;
    }

    /// <summary>How the game ran: the minutes' frame percentiles (median and worst minute), hitches a minute, and a client's link.</summary>
    static JsonObject Performance(IReadOnlyList<JsonObject> minutes)
    {
        List<double> Of(string key) => [.. minutes.Select(m => TelemetryFiles.Num(m, key)).Where(x => x is not null).Select(x => x!.Value)];
        JsonNode? Med(List<double> v) => v.Count == 0 ? null : Round(Median(v), 1);
        JsonNode? Max(List<double> v) => v.Count == 0 ? null : Round(v.Max(), 1);
        var hitches = Of("hitches");
        return new JsonObject
        {
            ["minutes"] = minutes.Count,
            ["p50"] = Med(Of("p50")),
            ["p95"] = Med(Of("p95")),
            ["p99"] = Med(Of("p99")),
            ["worstP99"] = Max(Of("p99")),
            ["hitchesPerMinute"] = hitches.Count == 0 ? null : Round(hitches.Average(), 2),
            ["pingMs"] = Med(Of("pingMs")),
            ["loss"] = Of("loss") is { Count: > 0 } l ? Round(Median(l), 3) : null,
        };
    }

    /// <summary>Counts by minute of the night (seconds on the night's clock), from minute 0 to the last with any.</summary>
    static JsonArray ByMinute(IEnumerable<double?> seconds)
    {
        var mins = seconds.Where(s => s is not null).Select(s => (int)Math.Floor(s!.Value / 60)).Where(m => m >= 0).ToList();
        int last = mins.Count == 0 ? -1 : mins.Max();
        return new JsonArray([.. Enumerable.Range(0, last + 1).Select(m => (JsonNode)mins.Count(x => x == m))]);
    }

    /// <summary>Counts by key, most first, then by name (so the same files always give the same object).</summary>
    static JsonObject Count(IEnumerable<string> keys)
    {
        var o = new JsonObject();
        foreach (var g in keys.GroupBy(k => k).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
            o[g.Key] = g.Count();
        return o;
    }

    static string Short(string? commit) => commit is { Length: > 8 } c ? c[..8] : commit ?? "unknown";

    static double Median(List<double> v)
    {
        var s = v.OrderBy(x => x).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }

    static double Round(double v, int places) => Math.Round(v, places, MidpointRounding.AwayFromZero);
}
