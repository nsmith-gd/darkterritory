using System.Text.Json;
using System.Text.RegularExpressions;
using Ballast;
using DarkTerritory.Game.Telemetry;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The playtest telemetry's session recorder (queue #624, note 913): one versioned JSON-lines schema, written in note 913
/// and pinned here; only its fields ever written; nothing without consent; no names; and a night the same with it or without.
/// </summary>
public class TelemetrySchemaTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static PrototypeSession Night() =>
        new(Content, RouteGenerator.Generate(RouteTuning.Load(Content), RouteTier.Frontier, 7), 4, enemies: true);

    static SessionFacts Facts => new("full", Dev: true, "test", "test", Vr: false, "1280x720", 1, VSync: true, FieldOfView: 75);

    /// <summary>A solo night of <paramref name="seconds"/>, recorded (or not); a crewmate who dies at <paramref name="dieAt"/> s.</summary>
    static (MemoryTelemetrySink Sink, SessionRecorder Recorder, PrototypeSession Session) Recorded(double seconds, bool enabled = true, double dieAt = 100)
    {
        var sink = new MemoryTelemetrySink();
        var s = Night();
        var recorder = new SessionRecorder(sink, new FixedTelemetryGate(enabled, "unused", "0123abcd"), clock: () => s.World.Tick * SimConstants.TickSeconds);
        recorder.Begin(Facts);
        recorder.NightBegin(s.World, "solo", humans: 1, bots: 0, me: 1);
        var friend = s.Player;
        for (int t = 0; t < seconds * SimConstants.TickRate; t++)
        {
            s.Step(default);
            recorder.Frame(t % 300 == 0 ? 80 : 16);
            if (t == (int)(dieAt * SimConstants.TickRate))
                friend = friend with { Health = 0, Death = DeathCause.Mauled };
            recorder.Tick(s.World, [(1, s.Player), (2, friend)]);
        }
        recorder.Prompt("hatch", acted: true);
        recorder.NightEnd(s.World);
        recorder.Purchase("car", 120, 380);
        recorder.Rebind("Fire");
        recorder.Menu("settings", 12.5);
        recorder.Quit("app");
        return (sink, recorder, s);
    }

    [Fact]
    public void TheSchemaIsNote913s()
    {
        // Note 913's "The schema" lists every event as "- `type`: `field`, `field`, ...", in the order written.
        string root = Path.GetDirectoryName(Content)!;
        string note = File.ReadAllText(Path.Combine(root, "docs", "notes", "913.md"));
        Assert.Contains($"**Schema version {TelemetrySchema.Version}.**", note);
        var documented = Regex.Matches(note, @"^\s*- `(\w+)`: ((?:`\w+`(?:, )?)+)\s*$", RegexOptions.Multiline)
            .ToDictionary(m => m.Groups[1].Value, m => Regex.Matches(m.Groups[2].Value, @"`(\w+)`").Select(f => f.Groups[1].Value).ToArray());
        Assert.Equal(TelemetrySchema.Events.Keys.Order(StringComparer.Ordinal), documented.Keys.Order(StringComparer.Ordinal));
        foreach (var (type, fields) in TelemetrySchema.Events)
            Assert.Equal(fields, documented[type]);
    }

    [Fact]
    public void ANightWritesOnlyTheSchemasFields()
    {
        var (sink, recorder, _) = Recorded(150);
        Assert.Equal(sink.Lines.Count, recorder.Lines);
        var types = new List<string>();
        foreach (string line in sink.Lines)
        {
            using var doc = JsonDocument.Parse(line);
            var o = doc.RootElement;
            Assert.Equal(TelemetrySchema.Version, o.GetProperty("schema").GetInt32());
            string type = o.GetProperty("type").GetString()!;
            types.Add(type);
            Assert.True(TelemetrySchema.Events.TryGetValue(type, out var fields), type);
            Assert.Equal([.. TelemetrySchema.Common, .. fields], o.EnumerateObject().Select(p => p.Name));
        }
        Assert.Equal("session", types[0]);
        Assert.Equal("night", types[1]);
        Assert.Equal(2, types.Count(t => t == "minute"));
        Assert.Contains("nightEnd", types);
        Assert.Equal("quit", types[^1]);
        // The crewmate who died, with how and when.
        var death = JsonDocument.Parse(Assert.Single(sink.Lines, l => l.Contains("\"type\":\"death\""))).RootElement;
        Assert.Equal("Mauled", death.GetProperty("cause").GetString());
        Assert.False(death.GetProperty("me").GetBoolean());
        // The frames: a hitch every 10 s (an 80 ms frame among 16 ms ones).
        var first = JsonDocument.Parse(sink.Lines.First(l => l.Contains("\"type\":\"minute\""))).RootElement;
        Assert.Equal(16.5, first.GetProperty("p50").GetDouble());
        Assert.InRange(first.GetProperty("hitches").GetInt32(), 5, 7);
    }

    [Fact]
    public void WithoutConsentNothingIsWritten()
    {
        var (sink, recorder, _) = Recorded(70, enabled: false);
        Assert.Empty(sink.Lines);
        Assert.False(recorder.Enabled);
        Assert.Equal(0, recorder.Bytes);
    }

    [Fact]
    public void ConsentIsAskedOnEveryLine()
    {
        // #625: switched off mid-night, nothing more's written; switched on after the game's up, the session line comes first.
        var sink = new MemoryTelemetrySink();
        var gate = new FixedTelemetryGate(false, "unused", "0123abcd");
        var s = Night();
        var recorder = new SessionRecorder(sink, gate, clock: () => s.World.Tick * SimConstants.TickSeconds);
        recorder.Begin(Facts);
        Assert.Empty(sink.Lines);
        gate.Recording = true;
        recorder.NightBegin(s.World, "solo", humans: 1, bots: 0, me: 1);
        Assert.Equal(["session", "night"], sink.Lines.Select(l => JsonDocument.Parse(l).RootElement.GetProperty("type").GetString()));
        Assert.Equal("0123abcd", JsonDocument.Parse(sink.Lines[0]).RootElement.GetProperty("install").GetString());
        for (int t = 0; t < 30 * SimConstants.TickRate; t++)
        {
            s.Step(default);
            recorder.Tick(s.World, [(1, s.Player)]);
        }
        gate.Recording = false;
        int written = sink.Lines.Count;
        for (int t = 0; t < 90 * SimConstants.TickRate; t++)
        {
            s.Step(default);
            recorder.Tick(s.World, [(1, s.Player)]);
        }
        recorder.NightEnd(s.World);
        recorder.Quit("app");
        Assert.Equal(written, sink.Lines.Count);
    }

    [Fact]
    public void NoNameIsEverWritten()
    {
        // The crew is counted, never named; an incident is its kind and cause, never its line (which names players).
        var (sink, _, s) = Recorded(150);
        string all = string.Join('\n', sink.Lines);
        foreach (string name in s.World.Names.Values.Where(n => n.Length > 2))
            Assert.DoesNotContain(name, all, StringComparison.OrdinalIgnoreCase);
        Assert.All(s.World.Attribution.Log, i => Assert.DoesNotContain(i.What, all));
    }

    [Fact]
    public void TheNightRunsTheSameWithTheRecorderOrWithout()
    {
        // It only reads: the world, ticked alike, ends where it would have.
        var (_, _, with) = Recorded(90);
        var without = Night();
        for (int t = 0; t < 90 * SimConstants.TickRate; t++)
            without.Step(default);
        Assert.Equal(without.World.Tick, with.World.Tick);
        Assert.Equal(without.World.Train.Dynamics.Distance, with.World.Train.Dynamics.Distance);
        Assert.Equal(without.Player.Position, with.Player.Position);
        Assert.Equal(without.World.Attribution.Log.Count, with.World.Attribution.Log.Count);
    }

    [Fact]
    public void ANightStaysSmall()
    {
        // Note 907: a 45-minute night "well under 1 MB". Fifteen minutes of a solo night, at the minutes' and the events'
        // rate, is a few dozen lines.
        var (sink, recorder, _) = Recorded(900);
        Assert.InRange(recorder.Bytes, 1, 100_000);
        Assert.InRange(sink.Lines.Count(l => l.Contains("\"type\":\"minute\"")), 14, 15);
    }
}
