using System.Numerics;
using System.Text.Json.Nodes;
using Ballast;
using Ballast.Render;
using DarkTerritory.Dev.Live;
using DarkTerritory.Dev.Replay;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Dev.Tests;

/// <summary>
/// ARCHITECTURE §8 note 524: an agent drives a night over MCP, and a shot is framed on a subject named, not on coordinates.
/// The protocol's tests talk to the server over in-memory streams as a client would; the subject camera's need no GPU (the
/// subject's box is projected through the camera). One short night is hosted for the class (frontier:7, a bot and the host's
/// player, the threats on).
/// </summary>
public class LiveControlTests : IClassFixture<LiveControlTests.Night>
{
    static readonly string Content = DataFile.FindContentRoot();

    public sealed class Night : IDisposable
    {
        public readonly string Shots = Path.Combine(Path.GetTempPath(), "dt-live-" + Guid.NewGuid().ToString("N"));
        public readonly LiveSession Live;

        public Night()
        {
            Live = new LiveSession(Content, Shots);
            Live.Start(new NightStart(Route: "frontier:7", Cars: 4, Bots: 1, Enemies: true));
            Live.Step(2 * SimConstants.TickRate);
        }

        public void Dispose()
        {
            Live.Dispose();
            if (Directory.Exists(Shots))
                Directory.Delete(Shots, recursive: true);
        }
    }

    readonly Night _night;

    public LiveControlTests(Night night) => _night = night;

    static string Request(int id, string method, JsonObject? parameters = null) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters ?? [] }.ToJsonString();

    static string Call(int id, string tool, JsonObject? arguments = null) =>
        Request(id, "tools/call", new JsonObject { ["name"] = tool, ["arguments"] = arguments ?? [] });

    /// <summary>The server run over these lines to their end, as a client's stdio would carry them; its answers, by id.</summary>
    static async Task<Dictionary<string, JsonObject>> Talk(McpServer server, params string[] lines)
    {
        var output = new StringWriter();
        await server.RunAsync(new StringReader(string.Join('\n', lines)), output, TestContext.Current.CancellationToken);
        var replies = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => (JsonObject)JsonNode.Parse(l)!).ToList();
        return replies.ToDictionary(r => r["id"]?.ToJsonString() ?? "null");
    }

    static JsonObject Result(Dictionary<string, JsonObject> replies, int id) => (JsonObject)replies[id.ToString(System.Globalization.CultureInfo.InvariantCulture)]["result"]!;

    static string Text(JsonObject result) => (string)result["content"]![0]!["text"]!;

    static JsonObject State(JsonObject result)
    {
        Assert.False((bool)result["isError"]!, Text(result));
        return (JsonObject)JsonNode.Parse(Text(result))!;
    }

    [Fact]
    public async Task ANightIsStartedSteppedAndReadOverTheProtocol()
    {
        using var live = new LiveSession(Content, _night.Shots);
        var server = new McpServer("dt", "test", LiveTools.For(live), LiveTools.Instructions);
        var replies = await Talk(server,
            Request(1, "initialize", new JsonObject { ["protocolVersion"] = "2025-06-18", ["capabilities"] = new JsonObject(), ["clientInfo"] = new JsonObject { ["name"] = "test", ["version"] = "1" } }),
            new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" }.ToJsonString(),
            Request(2, "tools/list"),
            Call(3, "night_start", new JsonObject { ["route"] = "frontier:7", ["cars"] = 3, ["bots"] = 1, ["enemies"] = false }),
            Call(4, "night_step", new JsonObject { ["seconds"] = 5 }),
            Call(5, "night_state"),
            Call(6, "night_insist", new JsonObject { ["kinds"] = new JsonArray("gaunt") }));
        // Every request answered, the notification not.
        Assert.Equal(["1", "2", "3", "4", "5", "6"], replies.Keys.Order(StringComparer.Ordinal));

        var hello = Result(replies, 1);
        Assert.Equal("2025-06-18", (string)hello["protocolVersion"]!);
        Assert.Equal("dt", (string)hello["serverInfo"]!["name"]!);
        Assert.NotNull(hello["capabilities"]!["tools"]);

        var names = Result(replies, 2)["tools"]!.AsArray().Select(t => (string)t!["name"]!).ToList();
        Assert.Equal(["night_start", "night_step", "night_state", "night_insist", "replay_open", "shot"], names);
        Assert.All(Result(replies, 2)["tools"]!.AsArray(), t => Assert.Equal("object", (string)t!["inputSchema"]!["type"]!));

        var started = State(Result(replies, 3));
        var stepped = State(Result(replies, 4));
        var state = State(Result(replies, 5));
        Assert.Equal(5 * SimConstants.TickRate, (int)stepped["stepped"]!);
        Assert.Equal((uint)started["tick"]! + 5 * SimConstants.TickRate, (uint)state["tick"]!);
        // The crew: the bot and the host's own player, each with where they are.
        var crew = state["crew"]!.AsArray();
        Assert.Equal(2, crew.Count);
        Assert.All(crew, c =>
        {
            Assert.True((int)c!["id"]! > 0);
            Assert.NotNull(c["post"]);
            Assert.True((bool)c["alive"]!);
        });
        Assert.Contains(crew, c => (string?)c!["plays"] == "conductor");
        Assert.Equal(3 + 1, (int)state["train"]!["cars"]!);
        Assert.NotNull(state["cars"]);

        // A night without threats takes no insisting: said, as the tool's failure.
        var insisted = Result(replies, 6);
        Assert.True((bool)insisted["isError"]!);
        Assert.Contains("no threats", Text(insisted), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhatsWrongIsAnErrorNotACrash()
    {
        using var live = new LiveSession(Content, _night.Shots);
        var server = new McpServer("dt", "test", LiveTools.For(live));
        var replies = await Talk(server,
            "not json at all",
            Request(1, "resources/list"),
            Call(2, "night_brew"),
            Call(3, "night_step", new JsonObject { ["seconds"] = "five" }),
            Call(4, "night_step", new JsonObject { ["secs"] = 5 }),
            Call(5, "night_insist"),
            Call(6, "night_step", new JsonObject { ["seconds"] = 1 }),
            Call(7, "replay_open", new JsonObject { ["file"] = Path.Combine(_night.Shots, "nowhere.dtrec") }),
            Call(8, "shot", new JsonObject { ["view"] = "subject:gaunt" }),
            Request(9, "ping"));
        Assert.Equal(McpServer.ParseError, (int)replies["null"]["error"]!["code"]!);
        Assert.Equal(McpServer.MethodNotFound, (int)replies["1"]["error"]!["code"]!);
        // The caller's mistakes are invalid params: an unknown tool, a wrong type, an argument it doesn't take, one it needs.
        foreach (var (id, says) in new[] { ("2", "no tool 'night_brew'"), ("3", "'seconds' must be a number"), ("4", "takes no 'secs'"), ("5", "needs 'kinds'") })
        {
            var error = replies[id]["error"]!;
            Assert.Equal(McpServer.InvalidParams, (int)error["code"]!);
            Assert.Contains(says, (string)error["message"]!, StringComparison.Ordinal);
        }
        // The tool's own failures are results it says them in: no night yet, no recording there.
        foreach (var (id, says) in new[] { (6, "no night yet"), (7, "no recording at"), (8, "no night yet") })
        {
            var result = Result(replies, id);
            Assert.True((bool)result["isError"]!);
            Assert.Contains(says, Text(result), StringComparison.Ordinal);
        }
        // And it's still there to answer.
        Assert.Empty(Result(replies, 9));
    }

    /// <summary>Where a world point falls on the frame: x, y from -1 to 1 across and down it.</summary>
    static Vector2 Ndc(Camera camera, Double3 at, float aspect)
    {
        var clip = Vector4.Transform(new Vector4(at.RelativeTo(camera.Position), 1), camera.ViewProjection(aspect));
        Assert.True(clip.W > 0, "behind the camera");
        return new Vector2(clip.X / clip.W, clip.Y / clip.W);
    }

    [Fact]
    public void TheSubjectIsInTheMiddleOfTheFrameAndWholeInIt()
    {
        var host = _night.Live.Host!;
        var world = host.World;
        var train = world.Train;
        var crew = host.Players.ToList();
        // A Gaunt asleep on the ground off car 2's left, as the director puts one down.
        var car = train.Frames[2];
        var spot = car.ToWorld(new Double3(-(car.Shape.HalfWidth + 8), 0, 0));
        double hint = train.Dynamics.Distance;
        spot = spot with { Y = PlayerMotor.GroundAt(spot, train.Line, ref hint) };
        var gaunt = world.AddEnemy(id => Gaunt.Asleep(id, spot, world.Enemies!.Gaunt));
        const float aspect = 16f / 9;
        var subjects = new[] { "engine", "car:2", "car:4", "gaunt", $"enemy:{gaunt.Id}" }.Concat(crew.Select(c => $"crew:{c.Id}")).ToList();
        foreach (var who in subjects)
        {
            var subject = SubjectCamera.Find(who, world, crew, host.PlayerTuning);
            var framing = SubjectCamera.Frame(subject, world, aspect);
            var camera = framing.Camera;
            // The same camera as the view by name gives it (dt replay --view subject:..., the shot tool).
            Assert.Equal(camera.Position, WorldShot.Camera(SubjectCamera.Prefix + who, world, crew, aspect: aspect, player: host.PlayerTuning).Position);
            var middle = Ndc(camera, subject.Centre, aspect);
            Assert.True(Math.Abs(middle.X) < 0.02 && Math.Abs(middle.Y) < 0.02, $"{who}: its middle is at {middle} on the frame");
            if (framing.Inside)
                continue;
            Assert.Null(framing.Note);
            // All of it in the frame, and filling a fair part of it (not a speck in the distance).
            double widest = 0;
            foreach (int i in new[] { -1, 1 })
                foreach (int j in new[] { -1, 1 })
                    foreach (int k in new[] { -1, 1 })
                    {
                        var corner = subject.Centre + subject.Right * (i * subject.Half.X) + subject.Up * (j * subject.Half.Y) + subject.Back * (k * subject.Half.Z);
                        var p = Ndc(camera, corner, aspect);
                        Assert.True(Math.Abs(p.X) <= 1 && Math.Abs(p.Y) <= 1, $"{who}: a corner of it is off the frame at {p}");
                        widest = Math.Max(widest, Math.Max(Math.Abs(p.X), Math.Abs(p.Y)));
                    }
            Assert.True(widest > 0.5, $"{who}: it reaches only {widest:0.00} of the way to the frame's edge");
            // Stood over the ground and out of the train's bodywork.
            Assert.False(SubjectCamera.InBodywork(camera.Position, train), $"{who}: the camera's in the train");
            Assert.True(camera.Position.Y >= PlayerMotor.GroundAt(camera.Position, train.Line, ref hint) + 0.9, $"{who}: the camera's in the ground");
        }
        Assert.Contains("gaunt", SubjectCamera.Find("gaunt", world, crew).Name, StringComparison.Ordinal);
        gaunt.Dismiss();
    }

    [Fact]
    public async Task ASubjectThatIsntThereIsSaidNotFramed()
    {
        var host = _night.Live.Host!;
        var world = host.World;
        var crew = host.Players.ToList();
        Assert.DoesNotContain(world.ActiveEnemies, e => e.Kind == EnemyKind.TrackDoll && !e.Gone);
        var absent = Assert.Throws<ArgumentException>(() => SubjectCamera.Find("trackDoll", world, crew));
        // It says what's there instead: the train, the crew by id.
        Assert.Contains("no trackDoll in the world", absent.Message, StringComparison.Ordinal);
        Assert.Contains("engine, car:1..car:4", absent.Message, StringComparison.Ordinal);
        Assert.Contains($"crew:{crew[0].Id}", absent.Message, StringComparison.Ordinal);
        Assert.Contains("no crewmate 99", Assert.Throws<ArgumentException>(() => SubjectCamera.Find("crew:99", world, crew)).Message, StringComparison.Ordinal);
        Assert.Contains("no car 9", Assert.Throws<ArgumentException>(() => SubjectCamera.Find("car:9", world, crew)).Message, StringComparison.Ordinal);
        Assert.Contains("isn't a subject", Assert.Throws<ArgumentException>(() => SubjectCamera.Find("banana", world, crew)).Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => WorldShot.Camera("subject:trackDoll", world, crew));
        // Through the shot tool, the tool's failure, before anything's drawn (no GPU wanted to say it).
        var server = new McpServer("dt", "test", LiveTools.For(_night.Live));
        var replies = await Talk(server, Call(1, "shot", new JsonObject { ["view"] = "subject:trackDoll" }));
        var result = Result(replies, 1);
        Assert.True((bool)result["isError"]!);
        Assert.Contains("no trackDoll in the world", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public void WhatsInsistedOnARecordedNightIsInItsReplay()
    {
        // Insisting is done to the host between its steps, where its transport can't hear it: the recording notes it, so the
        // night replays tick for tick (without the note it parts at the director's next think).
        string file;
        using (var live = new LiveSession(Content, _night.Shots, Path.Combine(_night.Shots, "recordings")))
        {
            live.Start(new NightStart(Route: "frontier:7", Cars: 3, Bots: 1, Enemies: true) { Record = true });
            live.Step(SimConstants.TickRate);
            live.Insist([EnemyKind.CinderHound, EnemyKind.Gaunt], every: 5);
            live.Step(2 * SimConstants.TickRate);
            file = live.Recording!;
        }
        using var replay = NightReplay.Open(file, Content);
        replay.Run();
        Assert.Null(replay.DivergedAt);
        Assert.Equal([EnemyKind.CinderHound, EnemyKind.Gaunt], replay.Host.World.Insist!);
        Assert.Equal(5, replay.Host.World.InsistEvery);
        // It's the night's own doing, not a note someone made in it.
        Assert.Empty(replay.Marks);
    }

    [Fact]
    public void EnemyKindsAreReadAsAnAgentWritesThem()
    {
        Assert.Equal(EnemyKind.CinderHound, EnemyNames.Parse("Cinder Hounds"));
        Assert.Equal(EnemyKind.CinderHound, EnemyNames.Parse("cinderhound"));
        Assert.Equal(EnemyKind.Gaunt, EnemyNames.Parse("GAUNT"));
        Assert.Equal(EnemyKind.Gaunt, EnemyNames.Parse("gaunts"));
        Assert.Equal(EnemyKind.FireFlies, EnemyNames.Parse("fire_flies"));
        Assert.Equal(EnemyKind.CarHugger, EnemyNames.Parse("car-hugger"));
        Assert.Null(EnemyNames.Parse("banana"));
        Assert.Null(EnemyNames.Parse("16"));
        Assert.Contains("'banana'", Assert.Throws<ArgumentException>(() => EnemyNames.ParseAll(["gaunt", "banana"])).Message, StringComparison.Ordinal);
    }
}
