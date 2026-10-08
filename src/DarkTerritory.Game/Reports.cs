using System.Text;
using System.Text.Json;
using Ballast;

namespace DarkTerritory.Game;

/// <summary>
/// Where reports go (content/ui/reports.json; note 452, the director, 8 Oct: "point reports, crashes, logs, etc. into a
/// nicely formatted reporting structure that is dev friendly for debugging"). The player sends them, from their own mail
/// program: a game can't hold a mail server's credentials, and nothing leaves a player's machine without them.
/// </summary>
public sealed record ReportsTuning
{
    public const string File = "ui/reports.json";
    /// <summary>The studio's address; empty, and nothing offers to send a report.</summary>
    public string To { get; init; } = "";
    /// <summary>What every report's subject starts with, so the studio's mail can sort them.</summary>
    public string Subject { get; init; } = "Dark Territory";
    /// <summary>How long the mail's body may run before it's cut: a mailto: link's length is limited by the mail programs.</summary>
    public int MailBodyChars { get; init; } = 1500;

    public static ReportsTuning Load(string content)
    {
        var path = Path.Combine(content, File);
        return System.IO.File.Exists(path) ? DataFile.Load<ReportsTuning>(path) : new();
    }
}

/// <summary>A labelled line of a report's header: "gpu  NVIDIA GeForce RTX 3060".</summary>
public sealed record ReportField(string Name, string Value);

/// <summary>
/// A crash, or a problem a player reports (note 452), as a developer reads it: an id and when; the build (its version and
/// commit); the machine, the settings and mods; what the game was doing; then the exception with its stack, and the last
/// lines the game printed. Written as text a person reads at a glance (<see cref="ToText"/>) and as JSON beside it for
/// tooling (<see cref="ToJson"/>, read back by <see cref="FromJson"/>).
/// </summary>
public sealed record Report(string Id, string Kind, DateTime Time, string Version, string Commit, IReadOnlyList<ReportField> Fields,
    string? ExceptionType, string? ExceptionMessage, string? Exception, IReadOnlyList<string> Log, string? Note = null)
{
    /// <summary>A crash report's kind; a player's own is <see cref="Problem"/>.</summary>
    public const string Crash = "crash", Problem = "problem";

    /// <summary>The build this assembly is: "1.0.0" and its commit (the SDK stamps it into the informational version).</summary>
    public static (string Version, string Commit) Build()
    {
        var info = typeof(Report).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "";
        int plus = info.IndexOf('+');
        return plus < 0 ? (info.Length > 0 ? info : "unknown", "unknown") : (info[..plus], info[(plus + 1)..]);
    }

    /// <summary>The header's width for the field names: every value starts in one column.</summary>
    const int Column = 10;

    public string ToText()
    {
        string title = $"DARK TERRITORY {(Kind == Problem ? "PROBLEM" : "CRASH")} REPORT";
        var t = new StringBuilder().AppendLine(title).AppendLine(new string('=', title.Length));
        void Field(string name, string value) => t.AppendLine($"{name.PadRight(Column)}{value}");
        Field("id", Id);
        Field("time", $"{Time:yyyy-MM-dd HH:mm:ss} ({Time.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ})");
        Field("build", $"{Version}, commit {Commit}");
        foreach (var f in Fields)
            Field(f.Name, f.Value);
        if (Note is { Length: > 0 })
            Section(t, "WHAT HAPPENED", Note);
        if (Exception is { Length: > 0 })
            Section(t, "EXCEPTION", Exception);
        Section(t, $"LAST {Log.Count} LINES (oldest first)", string.Join(Environment.NewLine, Log.Select(l => "  " + l)));
        return t.ToString();
    }

    static void Section(StringBuilder t, string title, string body) =>
        t.AppendLine().AppendLine(title).AppendLine(new string('-', title.Length)).AppendLine(body);

    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("id", Id);
            w.WriteString("kind", Kind);
            w.WriteString("time", Time.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
            w.WriteString("timeUtc", Time.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture));
            w.WriteString("version", Version);
            w.WriteString("commit", Commit);
            w.WriteStartObject("fields");
            foreach (var f in Fields)
                w.WriteString(f.Name, f.Value);
            w.WriteEndObject();
            if (Note is not null)
                w.WriteString("note", Note);
            if (Exception is not null)
            {
                w.WriteStartObject("exception");
                w.WriteString("type", ExceptionType);
                w.WriteString("message", ExceptionMessage);
                w.WriteString("text", Exception);
                w.WriteEndObject();
            }
            w.WriteStartArray("log");
            foreach (var l in Log)
                w.WriteStringValue(l);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    public static Report FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        string? Opt(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var ex = r.TryGetProperty("exception", out var e) ? e : (JsonElement?)null;
        return new Report(Opt(r, "id") ?? "", Opt(r, "kind") ?? Crash,
            DateTime.Parse(Opt(r, "time") ?? "2000-01-01T00:00:00", System.Globalization.CultureInfo.InvariantCulture),
            Opt(r, "version") ?? "", Opt(r, "commit") ?? "",
            [.. r.GetProperty("fields").EnumerateObject().Select(p => new ReportField(p.Name, p.Value.GetString() ?? ""))],
            ex is { } x ? Opt(x, "type") : null, ex is { } y ? Opt(y, "message") : null, ex is { } z ? Opt(z, "text") : null,
            [.. r.GetProperty("log").EnumerateArray().Select(l => l.GetString() ?? "")], Opt(r, "note"));
    }

    /// <summary>The report written beside <paramref name="text"/> as JSON (its twin), or null if there's none to read.</summary>
    public static Report? Load(string text)
    {
        try
        {
            var json = Path.ChangeExtension(text, ".json");
            return System.IO.File.Exists(json) ? FromJson(System.IO.File.ReadAllText(json)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or FormatException or KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// A mail to the studio about this report, as a mailto: link: the subject says what it is and which build, and the body
    /// carries its header and the exception's first lines, cut to the tuning's length, and asks for the file to be attached
    /// (a link can't attach it). The player's mail program opens on it; they send it.
    /// </summary>
    public string MailTo(ReportsTuning t, string file)
    {
        string what = Kind == Problem ? "problem" : $"crash, {ExceptionType ?? "unknown"}";
        string subject = $"{t.Subject} {what}: {Id} ({Version}, {Short(Commit)})";
        var body = new StringBuilder();
        body.AppendLine(Kind == Problem ? "What happened (in your own words):" : "What you were doing when it stopped (in your own words):")
            .AppendLine().AppendLine().AppendLine($"Please attach the report: {Path.GetFileName(file)}")
            .AppendLine($"It's in {Path.GetDirectoryName(file)}").AppendLine().AppendLine("----");
        body.AppendLine($"id: {Id}").AppendLine($"build: {Version}, commit {Commit}");
        foreach (var f in Fields)
            body.AppendLine($"{f.Name}: {f.Value}");
        if (Exception is { Length: > 0 })
            body.AppendLine().AppendLine(string.Join("\n", Exception.Split('\n').Take(8).Select(l => l.TrimEnd('\r'))));
        string text = body.ToString().Replace("\r\n", "\n");
        if (text.Length > t.MailBodyChars)
            text = text[..Math.Max(0, t.MailBodyChars - 1)] + "…";
        return $"mailto:{t.To}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(text)}";
    }

    static string Short(string commit) => commit.Length > 12 ? commit[..12] : commit;
}

/// <summary>What a report says of the game's state (note 452): the settings in a line, and where a night had got to.</summary>
public static class ReportFields
{
    /// <summary>The settings a bug most often hangs on, in one line.</summary>
    public static string Settings(Settings s) => string.Join(", ",
        $"{s.Resolution} {(s.Fullscreen ? "fullscreen" : "windowed")}", $"render scale {s.RenderScale * 100:0}%", $"vsync {(s.VSync ? "on" : "off")}",
        $"fov {s.EyeFov:0}", $"text size {s.TextScale * 100:0}%", $"colours {s.Colours.ToString().ToLowerInvariant()}",
        $"captions {(s.Captions ? "on" : "off")}", $"text backing {(s.TextBacking ? "on" : "off")}", $"hold keys {(s.ToggleHolds ? "toggle" : "hold")}",
        $"hud {(s.Hud ? "on" : "off")}", $"voice {(s.PushToTalk ? "push to talk" : "open mic")}", $"keys rebound {s.Keys.Count}");

    /// <summary>Where a night is: its phase and time, where the train is and how fast, the cars, and the player and the link.</summary>
    public static string Night(IPlaySession s)
    {
        var run = s.World.Run;
        var parts = new List<string>
        {
            run is null ? "no run" : $"{run.Phase.ToString().ToLowerInvariant()}, {run.Seconds:0} s in",
            $"km {s.Train.Dynamics.Distance / 1000:0.00} at {s.Train.Dynamics.Speed:0.0} m/s",
            $"{s.Train.Vehicles.Count} vehicles",
            $"player {s.PlayerId} {(s.Player.Alive ? "alive" : $"dead ({s.Player.Death})")}",
        };
        if (s.Link is { } link)
            parts.Add($"{link.Role.ToLowerInvariant()}, {link.Aboard} aboard{(link.PingMs is { } ping ? $", ping {ping:0} ms" : "")}{(link.Lost ? ", link lost" : "")}");
        return string.Join(", ", parts);
    }
}
