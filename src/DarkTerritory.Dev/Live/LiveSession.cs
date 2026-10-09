using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ballast;
using Ballast.Render;
using DarkTerritory.Dev.Replay;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Dev.Live;

/// <summary>
/// A night for <see cref="LiveSession.Start"/> to host headless (note 524): a route, the train's length, a bot crew aboard
/// with the host's own player (who stands idle: nobody drives them), and the threats on or off.
/// </summary>
/// <param name="Bots">Bot crewmates (the first drives); the crew cap less one at most.</param>
public sealed record NightStart(string Route = "frontier:7", int Cars = 6, int Bots = 3, bool Enemies = true)
{
    /// <summary>Only these kinds come, whenever their spawns can place them (<see cref="World.Insist"/>; note 186).</summary>
    public IReadOnlyList<EnemyKind>? Insist { get; init; }
    /// <summary>With <see cref="Insist"/>: seconds after one's gone before it's sent again.</summary>
    public double InsistEvery { get; init; } = 10;
    /// <summary>Recorded as a developer build records a night (note 515), for <c>dt replay</c> (or replay_open) to play again.</summary>
    public bool Record { get; init; }
}

/// <summary>
/// Live control (ARCHITECTURE §8 note 524): one night an agent drives, a hosted one or a recording opened to play again, and
/// what it asks of it: step it on, read its state, insist on what comes, draw it from a camera. Its readings are compact
/// JSON, the crew, the threats and what happened since the last step first, so an agent can follow a night step by step.
/// </summary>
public sealed class LiveSession(string content, string shots = "out/live", string recordings = "out/recordings") : IDisposable
{
    // Compact, and what isn't so (no death, no subject) left out rather than written null.
    static readonly JsonSerializerOptions Json = new(DataFile.Options) { WriteIndented = false, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    NetPlaySession? _night;
    NightRecorder? _recorder;
    NightReplay? _replay;
    WorldShot? _shooter;
    Sim.Route.Route? _shooterRoute;
    string _what = "";
    // What the last step reported up to: the incident log's and the director's spawn log's lengths.
    int _incidents, _spawns;

    /// <summary>The host of the night in hand (a replay's is the recording's host, built again), or null with none.</summary>
    public HostSession? Host => _night?.Host ?? _replay?.Host;

    public NightReplay? Replay => _replay;

    /// <summary>The recording of the night in hand, when it was started with <see cref="NightStart.Record"/>.</summary>
    public string? Recording => _recorder?.Current;

    HostSession Live => Host ?? throw new InvalidOperationException("no night yet: night_start hosts one, replay_open plays a recording");

    /// <summary>Hosts a night (closing the one in hand), its crew aboard; its state, in full.</summary>
    public JsonObject Start(NightStart start)
    {
        Close();
        if (start.Route.Split(':') is not [var tier, var seed] || !Enum.TryParse<Sim.Route.RouteTier>(tier, ignoreCase: true, out _) || !ulong.TryParse(seed, out _))
            throw new ArgumentException($"route '{start.Route}' isn't tier:seed (tiers: {string.Join(", ", Enum.GetNames<Sim.Route.RouteTier>().Select(Camel))}; e.g. frontier:7)");
        _recorder = start.Record ? new NightRecorder(recordings) : null;
        // Private and on loopback: no beacon on the network, no lobby, no port anyone else can join on.
        _night = NetPlaySession.HostGame(content, new SessionSetup(Route: start.Route, Cars: start.Cars, Enemies: start.Enemies),
            port: null, bots: start.Bots, listed: false, tap: _recorder);
        _what = $"{start.Route}, {start.Cars} cars, {_night.Host!.Players.Count()} aboard, threats {(start.Enemies ? "on" : "off")}";
        JsonObject? insisted = start.Insist is { Count: > 0 } kinds ? Insist(kinds, start.InsistEvery) : null;
        var state = Summary(full: true);
        if (insisted is not null)
            state["insist"] = insisted;
        if (_recorder?.Current is { } file)
            state["recording"] = Path.GetFullPath(file);
        return state;
    }

    static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    /// <summary>
    /// Only these kinds from now on (the "spawns" control): each comes whenever its own spawn can place it, again
    /// <paramref name="every"/> seconds after the last one's gone, and the director's own choices are skipped. None: the
    /// director's night again. A replay takes none (it plays what was recorded).
    /// </summary>
    public JsonObject Insist(IReadOnlyList<EnemyKind> kinds, double every)
    {
        if (_replay is not null)
            throw new InvalidOperationException("a replay plays the night as it was recorded and takes no spawns: night_start a night to insist on kinds");
        var world = Live.World;
        if (world.Director is null)
            throw new InvalidOperationException("this night has no threats: night_start it with enemies: true");
        world.Insist = kinds.Count > 0 ? kinds : null;
        world.InsistEvery = every;
        // Set between the host's steps, where its transport can't hear it: noted in the recording, for the replay to do alike.
        _recorder?.Mark("insist", new JsonObject { ["kinds"] = new JsonArray([.. kinds.Select(k => (JsonNode)k.ToString())]), ["every"] = every });
        return Node(new
        {
            insist = kinds.Select(EnemyNames.Name),
            every,
            // Nothing comes in the fortress yard (note 263), insisted or not.
            note = world.SafeYard ? "the train's in the fortress yard: nothing comes till the run's under way" : null,
        });
    }

    /// <summary>
    /// Steps the night on <paramref name="ticks"/> (a replay plays on, to the end of its recording), stopping sooner at
    /// <paramref name="until"/> or the run's end; the state, with what's happened since the last step.
    /// </summary>
    /// <param name="until">enemy (a new one's about), incident (a new line in the incident log), death, facility (stopped at
    /// one), or an enemy kind (one of it is about).</param>
    public JsonObject Step(int ticks, string? until = null)
    {
        var host = Live;
        var world = host.World;
        var stop = Until(until, host);
        bool over = world.Run?.Over == true;
        var watch = Stopwatch.StartNew();
        string why = "ticks";
        uint from = host.Tick;
        for (int i = 0; i < ticks; i++)
        {
            if (_replay is { } replay)
            {
                if (!replay.Advance())
                {
                    why = "the recording's played out";
                    break;
                }
            }
            else
                _night!.Step(default);
            if (stop?.Invoke() == true)
            {
                why = $"until {until}";
                break;
            }
            if (!over && world.Run?.Over == true)
            {
                why = $"the run ended ({world.Run.End.ToString().ToLowerInvariant()})";
                break;
            }
        }
        var state = Summary(full: false);
        state["stepped"] = host.Tick - from;
        state["stopped"] = why;
        state["wallSeconds"] = Math.Round(watch.Elapsed.TotalSeconds, 1);
        if (_replay?.DivergedAt is { } parted)
            state["divergedAt"] = parted;
        return state;
    }

    static Func<bool>? Until(string? until, HostSession host)
    {
        if (string.IsNullOrWhiteSpace(until))
            return null;
        var world = host.World;
        switch (until.Trim().ToLowerInvariant())
        {
            case "enemy" or "spawn":
                var known = world.ActiveEnemies.Select(e => e.Id).ToHashSet();
                return () => world.ActiveEnemies.Any(e => !e.Gone && !known.Contains(e.Id));
            case "incident":
                int lines = world.Attribution.Log.Count;
                return () => world.Attribution.Log.Count > lines;
            case "death":
                int alive = host.Players.Count(p => p.State.Alive);
                return () => host.Players.Count(p => p.State.Alive) < alive;
            case "facility" or "stop":
                bool at = world.Run?.Phase == RunPhase.AtFacility;
                return () => !at && world.Run?.Phase == RunPhase.AtFacility;
        }
        if (EnemyNames.Parse(until) is { } kind)
            return () => world.ActiveEnemies.Any(e => !e.Gone && e.Kind == kind);
        throw new ArgumentException($"until '{until}' isn't one of enemy, incident, death, facility or an enemy kind (e.g. gaunt)");
    }

    /// <summary>The state in full: the cars, the director, every incident tonight, where everyone is.</summary>
    public JsonObject State() => Summary(full: true);

    /// <summary>Opens a recording (closing the night in hand) and plays it to <paramref name="to"/>, or to its end.</summary>
    public JsonObject OpenReplay(string file, uint? to)
    {
        if (!File.Exists(file))
            throw new FileNotFoundException($"no recording at {Path.GetFullPath(file)} (`dt replay list` shows this machine's; night_start with record: true makes one)");
        Close();
        _replay = NightReplay.Open(file, content);
        _replay.Run(to);
        var setup = SessionSetup.Decode(_replay.Header.Setup);
        _what = $"replay of {Path.GetFileName(file)}: {setup.Route ?? setup.Line}, {setup.Cars} cars, recorded {_replay.Header.RecordedAt:yyyy-MM-dd HH:mm} at {_replay.Header.Commit}";
        var state = Summary(full: true);
        state["replay"] = Node(new
        {
            file = Path.GetFullPath(file),
            // Every tick's sends as the recording's so far; else the first tick that differed.
            tickForTick = _replay.DivergedAt is null,
            divergedAt = _replay.DivergedAt,
            finished = _replay.Finished,
            truncated = _replay.Truncated,
            contentDifferences = _replay.ContentDifferences,
            marks = _replay.Marks.Select(m => new { m.Kind, m.Tick, seconds = R(m.Tick * SimConstants.TickSeconds, 1) }),
        });
        return state;
    }

    /// <summary>
    /// The world drawn from a camera (<see cref="WorldShot.Camera"/>: a view, <c>eye:N</c>, or <c>subject:who</c>), as a PNG
    /// saved under the shots folder (or at <paramref name="path"/>); a copy no wider than <paramref name="inline"/> to hand
    /// back (a client's context isn't the place for every pixel: the file has them), and what it was.
    /// </summary>
    public (byte[]? Png, JsonObject Info) Shot(string view, int width = 1280, int height = 720, int car = 2, string? path = null, int inline = 960)
    {
        var host = Live;
        var world = host.World;
        var crew = host.Players.ToList();
        double aspect = (double)width / height;
        // The camera first: a view that isn't one, or a subject that isn't there, is said before anything's drawn.
        var (camera, subject, note) = WorldShot.Aim(view, world, crew, car, aspect, host.PlayerTuning);
        var watch = Stopwatch.StartNew();
        var shooter = Shooter(world.Route, width, height);
        double making = watch.Elapsed.TotalSeconds;
        var pixels = shooter.Render(world, crew, camera, WorldShot.EyeOf(view));
        path ??= Path.Combine(shots, $"{host.Tick:D6}-{Slug(view)}.png");
        if (Path.GetDirectoryName(path) is { Length: > 0 } dir)
            Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, Png(pixels, width, height));
        byte[]? handed = null;
        int scale = 1;
        if (inline > 0)
        {
            scale = (int)Math.Ceiling(width / (double)inline);
            handed = scale <= 1 ? File.ReadAllBytes(path) : Png(Shrink(pixels, width, height, scale), width / scale, height / scale);
        }
        var info = Node(new
        {
            path = Path.GetFullPath(path),
            view,
            subject,
            note,
            tick = host.Tick,
            seconds = R(host.Tick * SimConstants.TickSeconds, 1),
            size = new[] { width, height },
            handedBack = handed is null ? null : new[] { width / scale, height / scale },
            camera = new { at = At(camera.Position), yawDeg = R(camera.Yaw * 180 / Math.PI, 1), pitchDeg = R(camera.Pitch * 180 / Math.PI, 1), fovDeg = camera.FovYDegrees },
            // The renderer's made once a night (the look and its textures loaded): what that took, when this shot made it.
            rendererSeconds = making > 0.05 ? R(making, 1) : null,
            wallSeconds = R(watch.Elapsed.TotalSeconds, 1),
        });
        return (handed, info);
    }

    static byte[] Png(byte[] rgba, int width, int height)
    {
        using var png = new MemoryStream();
        PngWriter.Write(png, rgba, width, height);
        return png.ToArray();
    }

    /// <summary>RGBA made <paramref name="by"/> times smaller each way, each pixel the mean of the block it covers.</summary>
    public static byte[] Shrink(byte[] rgba, int width, int height, int by)
    {
        int w = width / by, h = height / by;
        var small = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                for (int c = 0; c < 4; c++)
                {
                    int sum = 0;
                    for (int j = 0; j < by; j++)
                        for (int i = 0; i < by; i++)
                            sum += rgba[((y * by + j) * width + x * by + i) * 4 + c];
                    small[(y * w + x) * 4 + c] = (byte)((sum + by * by / 2) / (by * by));
                }
        return small;
    }

    /// <summary>The one renderer, kept: it's slow to make (the look, its textures). A new route or size makes it again.</summary>
    WorldShot Shooter(Sim.Route.Route? route, int width, int height)
    {
        if (_shooter is { } kept && ReferenceEquals(_shooterRoute, route) && kept.Width == width && kept.Height == height)
            return kept;
        _shooter?.Dispose();
        _shooter = null;
        _shooter = new WorldShot(content, route, width, height);
        _shooterRoute = route;
        return _shooter;
    }

    static string Slug(string view) => new([.. view.Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')]);

    JsonObject Summary(bool full)
    {
        var host = Live;
        var world = host.World;
        var train = world.Train;
        var crew = host.Players.OrderBy(c => c.Id).ToList();
        double behind = world.Director?.Tuning.Abandoned.BehindM ?? double.PositiveInfinity;
        var bots = _night?.BotCrew?.Bots.Where(b => b.Session.PlayerId is not null).ToDictionary(b => (int)b.Session.PlayerId!.Value, b => b.Bot.Name)
            ?? new Dictionary<int, string>();
        int me = _night?.PlayerId ?? -1;
        var log = world.Attribution.Log;
        var spawns = world.Director?.Log ?? [];
        var incidents = (full ? log : log.Skip(_incidents)).Select(i => new
        {
            kind = i.Kind,
            seconds = R(i.Seconds, 1),
            victim = i.Victim >= 0 ? IncidentLog.NameOf(world, i.Victim) : null,
            what = i.What,
            where = i.Where,
            actor = i.Actor >= 0 ? IncidentLog.NameOf(world, i.Actor) : null,
            action = i.Action.Length > 0 ? i.Action.Replace("{actor}", IncidentLog.NameOf(world, i.Actor)) : null,
            cause = i.Cause == Sim.Player.DeathCause.None ? (object?)null : i.Cause,
        }).ToList();
        var spawned = (full ? spawns : spawns.Skip(_spawns)).Select(s => new { kind = EnemyNames.Name(s.Kind), seconds = R(s.Tick * SimConstants.TickSeconds, 1), km = R(s.TrainDistance / 1000, 2) }).ToList();
        if (!full)
            (_incidents, _spawns) = (log.Count, spawns.Count);
        var run = world.Run;
        Sim.Route.RouteFeature? next = null;
        if (run is not null)
            for (int i = 0; i < run.Facilities.Count && next is null; i++)
                if (run.Facilities[i].Start > train.Dynamics.Distance && i != run.Facility)
                    next = run.Facilities[i];
        var state = Node(new
        {
            night = _what,
            tick = host.Tick,
            seconds = R(host.Tick * SimConstants.TickSeconds, 1),
            run = run is null ? null : new
            {
                phase = run.Phase,
                end = run.Over ? run.End.ToString().ToLowerInvariant() : null,
                seconds = R(run.Seconds, 1),
                dawnInMinutes = R(run.DawnIn / 60, 1),
                at = run.Phase == RunPhase.AtFacility ? run.FacilityFeature?.Facility?.ToString() : null,
                next = next is null ? null : new { facility = next.Facility?.ToString() ?? next.Kind.ToString(), km = R(next.Start / 1000, 2), kmAhead = R((next.Start - train.Dynamics.Distance) / 1000, 2) },
            },
            train = new
            {
                km = R(train.Dynamics.Distance / 1000, 3),
                kmh = R(train.Dynamics.Speed * 3.6, 1),
                cars = train.OwnVehicles,
                derailed = world.Derailed,
                throttle = R(world.Controls.Throttle, 2),
                brake = R(world.Controls.Brake, 2),
                pressure = train.BoilerTuning is null ? null : R(train.Boiler.Pressure, 1),
            },
            crew = crew.Select(c => new
            {
                id = c.Id,
                name = world.Names.GetValueOrDefault(c.Id),
                // Who's playing them: a bot's job, or the host's own player, whom nothing drives here.
                plays = bots.TryGetValue(c.Id, out var bot) ? bot : c.Id == me ? "host (idle)" : null,
                post = c.State.Alive ? Census.PostOf(c.State, train, behind) : (Post?)null,
                // The car they're aboard (its index in the train), or -1 on the ground.
                car = c.State.Parent,
                surface = c.State.Surface,
                health = c.State.Health,
                alive = c.State.Alive,
                death = c.State.Alive ? (object?)null : c.State.Death,
                at = full ? At(Sim.Player.PlayerMotor.WorldPosition(c.State, train)) : null,
            }),
            enemies = world.ActiveEnemies.Where(e => !e.Gone).OrderBy(e => e.Id).Select(e => new
            {
                id = e.Id,
                kind = EnemyNames.Name(e.Kind),
                phase = e.Phase,
                // Metres from the train's nearest bodywork (0 on or in it).
                m = R(SubjectCamera.FromTrain(e.WorldPosition(train), train), 1),
                car = e.Attached >= 0 ? e.Attached : (int?)null,
                holding = e.Holding >= 0 ? e.Holding : (int?)null,
                health = full ? R(e.Health, 2) : null,
                at = full ? At(e.WorldPosition(train)) : null,
            }),
            spawned,
            incidents,
        });
        if (!full)
            return state;
        state["cars"] = Nodes(train.Vehicles.Take(train.OwnVehicles).Select(v => new
        {
            id = v.Id,
            kind = v.Kind,
            cargo = v.Cargo,
            integrity = R(v.Integrity, 2),
            cargoIntegrity = R(v.CargoIntegrity, 2),
            lamp = v.LampLit,
            offRails = v.OffRails ? true : (bool?)null,
            breached = v.Breached ? true : (bool?)null,
            hotBox = v.HotBox > 0 ? R(v.HotBox, 1) : null,
            eaten = v.Eaten > 0 ? R(v.Eaten, 2) : null,
        }));
        if (world.Director is { } d)
            state["director"] = Node(new
            {
                pressure = R(d.Pressure, 2),
                budget = R(d.Budget, 1),
                spent = R(d.Spent, 1),
                held = d.HeldBecause,
                insist = world.Insist?.Select(EnemyNames.Name),
                posts = d.Posts.Entries.Select(p => new { p.Player, p.Post, slack = R(p.Slack, 0), on = p.On }),
            });
        return state;
    }

    static double? R(double v, int digits) => double.IsFinite(v) ? Math.Round(v, digits) : null;

    static double?[] At(Double3 p) => [R(p.X, 2), R(p.Y, 2), R(p.Z, 2)];

    static JsonObject Node(object value) => JsonSerializer.SerializeToNode(value, Json) as JsonObject ?? [];

    static JsonArray Nodes<T>(IEnumerable<T> values) => JsonSerializer.SerializeToNode(values.ToList(), Json) as JsonArray ?? [];

    /// <summary>Lets the night in hand go (a hosted one's recording closed with it).</summary>
    public void Close()
    {
        _night?.Dispose();
        _night = null;
        _recorder = null;
        _replay?.Dispose();
        _replay = null;
        (_incidents, _spawns) = (0, 0);
    }

    public void Dispose()
    {
        Close();
        _shooter?.Dispose();
        _shooter = null;
    }
}
