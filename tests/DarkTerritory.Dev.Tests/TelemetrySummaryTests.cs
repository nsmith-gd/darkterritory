using System.Text.Json.Nodes;
using DarkTerritory.Dev.Telemetry;
using DarkTerritory.Game.Telemetry;

namespace DarkTerritory.Dev.Tests;

/// <summary>
/// Queue #627, note 917: <c>dt telemetry summary</c> reads note 913's session files as the recorder writes them, counts what a
/// playtest needs (how nights end, where players quit, deaths, encounters, the funnel), skips what it can't read rather than
/// guessing, and reads only fields the schema has.
/// </summary>
public class TelemetrySummaryTests
{
    /// <summary>A player who delivered a night, died in it, and left the second for the menu three minutes in.</summary>
    static readonly string[] Dave =
    [
        """{"schema":1,"type":"session","t":0,"session":"dave","install":"i1","build":"1.0.0","commit":"abcdef0123456789","edition":"full","dev":false,"os":"Windows","gpu":"RTX","vr":false,"resolution":"1920x1080","renderScale":1,"vsync":true,"fov":80}""",
        """{"schema":1,"type":"night","t":5,"night":1,"role":"host","route":"frontier-7","tier":"Frontier","cars":6,"humans":2,"bots":2}""",
        """{"schema":1,"type":"first","t":17,"night":1,"what":"move","s":12}""",
        """{"schema":1,"type":"first","t":45,"night":1,"what":"job","s":40}""",
        """{"schema":1,"type":"minute","t":65,"night":1,"minute":1,"km":0.9,"alive":4,"crew":4,"frames":3600,"hitches":1,"p50":16,"p95":20,"p99":30,"pingMs":null,"loss":null}""",
        """{"schema":1,"type":"encounter","t":960,"night":1,"s":90,"kind":"Climber","channel":"Pressure","where":"gap","part":"middle","afoot":false,"engagedAt":95,"confrontedAt":96,"nearAt":94,"grabs":1,"punishes":0,"end":"Killed","endedAt":130}""",
        """{"schema":1,"type":"minute","t":125,"night":1,"minute":2,"km":1.8,"alive":4,"crew":4,"frames":3600,"hitches":3,"p50":18,"p95":24,"p99":40,"pingMs":null,"loss":null}""",
        """{"schema":1,"type":"death","t":155,"night":1,"s":150,"km":2.1,"cause":"Mauled","post":"Rider","me":true}""",
        """{"schema":1,"type":"first","t":305,"night":1,"what":"stop","s":300}""",
        """{"schema":1,"type":"first","t":965,"night":1,"what":"delivery","s":960}""",
        """{"schema":1,"type":"nightEnd","t":965,"night":1,"end":"Delivered","seconds":960,"km":7.4,"carsDelivered":6,"carsLost":0,"cargoDelivered":5,"net":900,"crewHome":3,"crewLost":1,"deaths":1,"encounters":1,"hitches":4,"p50":17,"p95":22,"p99":35}""",
        """{"schema":1,"type":"night","t":1000,"night":2,"role":"host","route":"frontier-8","tier":"Frontier","cars":7,"humans":2,"bots":2}""",
        """{"schema":1,"type":"quit","t":1200,"how":"menu","night":2,"s":200}""",
        """{"schema":1,"type":"nightEnd","t":1200,"night":2,"end":"None","seconds":200,"km":1.2,"carsDelivered":null,"carsLost":null,"cargoDelivered":null,"net":null,"crewHome":null,"crewLost":null,"deaths":0,"encounters":0,"hitches":0,"p50":16,"p95":19,"p99":25}""",
        """{"schema":1,"type":"quit","t":1201,"how":"app","night":0,"s":null}""",
    ];

    /// <summary>A joiner whose game closed hard in their first night (no end), with a line from a newer schema and one that isn't JSON.</summary>
    static readonly string[] Priya =
    [
        """{"schema":1,"type":"session","t":0,"session":"priya","install":"i2","build":"1.0.0","commit":"abcdef0123456789","edition":"demo","dev":false,"os":"Linux","gpu":"RX","vr":true,"resolution":"1280x720","renderScale":1,"vsync":false,"fov":90}""",
        """{"schema":1,"type":"night","t":3,"night":1,"role":"client","route":"local-2","tier":"Local","cars":3,"humans":2,"bots":0}""",
        """{"schema":1,"type":"first","t":30,"night":1,"what":"move","s":27}""",
        """{"schema":2,"type":"heartbeat","t":40,"night":1}""",
        "this isn't a line",
        """{"schema":1,"type":"minute","t":63,"night":1,"minute":1,"km":0.5,"alive":2,"crew":2,"frames":3500,"hitches":0,"p50":15,"p95":18,"p99":22,"pingMs":60,"loss":0.01}""",
    ];

    static JsonObject Summarise()
    {
        var (a, skippedA) = TelemetryFiles.Parse("dave.jsonl", Dave);
        var (b, skippedB) = TelemetryFiles.Parse("priya.jsonl", Priya);
        return TelemetrySummary.Summarise([a, b], skippedA + skippedB);
    }

    [Fact]
    public void ASummaryCountsSessionsNightsAndHowEachNightEnded()
    {
        var s = Summarise();
        Assert.Equal(2, (int)s["sessions"]!);
        Assert.Equal(3, (int)s["nights"]!);
        Assert.Equal(2, (int)s["skippedLines"]!);
        // Delivered; left for the menu mid-night (its end None reads as a quit); and one never ended.
        var ends = s["ends"]!.AsObject();
        Assert.Equal(1, (int)ends["Delivered"]!);
        Assert.Equal(1, (int)ends["quit"]!);
        Assert.Equal(1, (int)ends["unfinished"]!);
        Assert.Equal(16 + 200 / 60.0 + 1, (double)s["minutes"]!, 1);
        Assert.Equal(1, (int)s["vr"]!);
        Assert.Equal(1, (int)s["editions"]!["demo"]!);
    }

    [Fact]
    public void QuitsMidNightAreCountedByMinuteAndClosingBetweenNightsIsNot()
    {
        var q = Summarise()["quits"]!;
        Assert.Equal(1, (int)q["midNight"]!);
        Assert.Equal(1, (int)q["all"]!["menu"]!);
        Assert.Equal(1, (int)q["all"]!["app"]!);
        // The menu quit, 200 s into the night: minute 3.
        Assert.Equal([0, 0, 0, 1], q["byMinute"]!.AsArray().Select(n => (int)n!));
    }

    [Fact]
    public void DeathsAndEncountersAreCountedByCauseKindAndMinute()
    {
        var s = Summarise();
        var d = s["deaths"]!;
        Assert.Equal(1, (int)d["total"]!);
        Assert.Equal(1, (int)d["mine"]!);
        Assert.Equal(1, (int)d["byCause"]!["Mauled"]!);
        Assert.Equal(1, (int)d["byPost"]!["Rider"]!);
        Assert.Equal([0, 0, 1], d["byMinute"]!.AsArray().Select(n => (int)n!));
        var e = s["encounters"]!;
        Assert.Equal(1, (int)e["byKind"]!["Climber"]!);
        Assert.Equal(1, (int)e["grabs"]!);
    }

    [Fact]
    public void TheFunnelCountsSessionsThatReachedEachStep()
    {
        var funnel = Summarise()["funnel"]!.AsArray().ToDictionary(f => (string)f!["step"]!, f => (int)f!["sessions"]!);
        Assert.Equal(TelemetrySummary.Funnel.Select(f => f.Step), funnel.Keys);
        Assert.Equal(2, funnel["launch"]);
        Assert.Equal(2, funnel["night"]);
        Assert.Equal(2, funnel["move"]);
        Assert.Equal(1, funnel["job"]);
        Assert.Equal(1, funnel["stop"]);
        Assert.Equal(1, funnel["delivery"]);
        Assert.Equal(1, funnel["secondNight"]);
        // Each step's count is no more than the one before it on these sessions: a funnel narrows.
        var counts = funnel.Values.ToList();
        Assert.All(counts.Zip(counts.Skip(1)), p => Assert.True(p.Second <= p.First));
    }

    [Fact]
    public void HowItRanIsTheMinutesMedians()
    {
        var p = Summarise()["performance"]!;
        Assert.Equal(3, (int)p["minutes"]!);
        Assert.Equal(16, (double)p["p50"]!);
        Assert.Equal(40, (double)p["worstP99"]!);
        Assert.Equal(60, (double)p["pingMs"]!);
    }

    [Fact]
    public void TheDashboardsTablesAreFlatRowsOfTheSummary()
    {
        var t = TelemetrySummary.Tables(Summarise());
        Assert.Equal(["deathsByCause", "encountersByChannel", "encountersByKind", "ends", "funnel", "nights", "overview", "tiers", "timeline"], t.Keys);
        var overview = Assert.Single(t["overview"]);
        Assert.Equal(3, (int)overview!["nights"]!);
        Assert.Equal(1, (int)overview["delivered"]!);
        // Every row is flat: a value per column, nothing nested, so a dataset reads it as a table.
        Assert.All(t.Values.SelectMany(rows => rows), r => Assert.All(r!.AsObject(), kv => Assert.True(kv.Value is null or JsonValue)));
        // The timeline lines the minutes up: the death in minute 2, the quit in minute 3.
        var timeline = t["timeline"].ToDictionary(r => (int)r!["minute"]!, r => r!);
        Assert.Equal(1, (int)timeline[2]["deaths"]!);
        Assert.Equal(1, (int)timeline[3]["quits"]!);
        Assert.Equal(3, t["nights"].Count);
    }

    [Fact]
    public void ItReadsOnlyFieldsTheRecordersSchemaHas()
    {
        Assert.True(TelemetryFiles.Schema <= TelemetrySchema.Version, "the summary reads a newer schema than the recorder writes");
        foreach (var (type, field) in TelemetrySummary.Reads)
        {
            Assert.True(TelemetrySchema.Events.TryGetValue(type, out var fields), $"no `{type}` in the schema");
            Assert.Contains(field, fields!);
        }
    }

    [Fact]
    public void AFolderOfSessionsReadsAsTheyWereWritten()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dt-telemetry-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "cohort"));
            File.WriteAllLines(Path.Combine(dir, "dave.jsonl"), Dave);
            File.WriteAllLines(Path.Combine(dir, "cohort", "priya.jsonl"), Priya);
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "not a session");
            var (sessions, skipped) = TelemetryFiles.Read(dir);
            Assert.Equal(["dave", "priya"], sessions.Select(x => x.Id));
            Assert.Equal(2, skipped);
            Assert.Equal(Summarise().ToJsonString(), TelemetrySummary.Summarise(sessions, skipped).ToJsonString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
