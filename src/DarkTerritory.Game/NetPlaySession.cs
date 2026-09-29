using System.Net;
using System.Text.Json;
using Ballast;
using Ballast.Net;
using Ballast.Render;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>What a joining machine needs to build the host's world. Travels in the Welcome as JSON.</summary>
/// <param name="Route">A route spec (<c>frontier:7</c>), or null to play <paramref name="Line"/>.</param>
public sealed record SessionSetup(string? Route = null, string Line = "test-loop", int Cars = 6, bool Enemies = true)
{
    /// <summary>
    /// The host's tuning, file by file (content/tuning/*.json, line endings normalised). A joiner whose tuning
    /// differs would predict a different game from the one the host runs, so it's refused by name.
    /// </summary>
    public Dictionary<string, string>? Content { get; init; }

    public static Dictionary<string, string> HashContent(string content)
    {
        var hashes = new Dictionary<string, string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(content, "tuning"), "*.json").Order(StringComparer.Ordinal))
        {
            var text = File.ReadAllText(file).Replace("\r\n", "\n");
            var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
            hashes["tuning/" + Path.GetFileName(file)] = Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
        }
        return hashes;
    }

    /// <summary>Files that differ between the host's content and this machine's.</summary>
    public IReadOnlyList<string> ContentDifferences(string content)
    {
        if (Content is null)
            return [];
        var mine = HashContent(content);
        return [.. Content.Keys.Union(mine.Keys).Where(k => Content.GetValueOrDefault(k) != mine.GetValueOrDefault(k)).Order()];
    }

    public string Encode() => JsonSerializer.Serialize(this, DataFile.Options);
    public static SessionSetup Decode(string json) => JsonSerializer.Deserialize<SessionSetup>(json, DataFile.Options) ?? new SessionSetup();

    /// <summary>The world this setup describes, identically on every machine.</summary>
    /// <param name="authority">The host's world runs the night; a joiner's mirrors it.</param>
    public (World World, Route? Route) Build(string content, bool authority = false)
    {
        var trainTuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
        var boiler = DataFile.Load<BoilerTuning>(Path.Combine(content, BoilerTuning.File));
        var combat = DataFile.Load<CombatTuning>(Path.Combine(content, CombatTuning.File));
        Route? route = null;
        RailLine line;
        var consist = Consist.Uniform(trainTuning, Cars, 1);
        double start = 600;
        if (Route is { Length: > 0 } spec)
        {
            var (tier, seed) = Sim.Route.Route.ParseSpec(spec);
            route = RouteGenerator.Generate(DataFile.Load<RouteTuning>(Path.Combine(content, RouteTuning.File)), tier, seed);
            line = route.Build();
            start = consist.LengthMetres + 150; // the fortress yard, as in the prototype
        }
        else
        {
            line = RailLine.Load(Path.Combine(content, "lines", Line + ".json"));
        }
        var train = new TrainOnLine(new TrainDynamics(consist), line, start, boiler);
        var world = new World(train, combat);
        if (route is not null)
            world.EnableRun(DataFile.Load<Sim.Run.RunTuning>(Path.Combine(content, Sim.Run.RunTuning.File)), route,
                DataFile.Load<RouteTuning>(Path.Combine(content, RouteTuning.File)).YardLength, authority);
        return (world, route);
    }
}

/// <summary>
/// A networked game over UDP (T13): the host runs <see cref="HostSession"/> and plays through its own
/// <see cref="ClientSession"/> over localhost, exactly like everyone else, so the host has no advantage and
/// no separate code path. A joiner builds its world from the host's <see cref="SessionSetup"/>.
/// </summary>
public sealed class NetPlaySession : IPlaySession, IDisposable
{
    public const int DefaultPort = 27450;
    readonly UdpTransport? _hostTransport;
    readonly UdpTransport _clientTransport;
    readonly List<Crewmate> _crew = new();
    readonly List<CarFrame> _frames = new();
    PlayerState _previous;

    NetPlaySession(HostSession? host, UdpTransport? hostTransport, ClientSession client, UdpTransport clientTransport, SessionSetup setup, Route? route)
    {
        Host = host;
        _hostTransport = hostTransport;
        Client = client;
        _clientTransport = clientTransport;
        Setup = setup;
        Route = route;
    }

    public HostSession? Host { get; }
    public ClientSession Client { get; }
    public SessionSetup Setup { get; }
    public Route? Route { get; }
    public TrainOnLine Train => Client.Train;
    public World World => Client.World;
    public PlayerState Player => Client.Predicted;
    public TrainControls Controls => Client.Controls;
    public long Tick { get; private set; }
    public bool Lost { get; private set; }

    /// <summary>Hosts on <paramref name="port"/> (0 = any) and joins it from this machine.</summary>
    public static NetPlaySession HostGame(string content, SessionSetup setup, int port = DefaultPort, int expectedCrew = 4)
    {
        var trainTuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
        var playerTuning = DataFile.Load<PlayerTuning>(Path.Combine(content, PlayerTuning.File));
        setup = setup with { Content = SessionSetup.HashContent(content) };
        var (hostWorld, route) = setup.Build(content, authority: true);
        var hostTransport = UdpTransport.Host(port);
        var host = new HostSession(hostTransport, hostWorld, trainTuning, playerTuning) { SessionInfo = setup.Encode() };
        hostWorld.EnableBodies();
        hostWorld.Stock();
        // Spec E: drop-in at POIs only: in the yard, stopped at a facility, or home. Mid-run joiners wait by
        // the train at the facility, "like a pickup".
        if (hostWorld.Run is { } run)
        {
            host.CanBoard = () => run.Phase is Sim.Run.RunPhase.Yard or Sim.Run.RunPhase.AtFacility or Sim.Run.RunPhase.Arrived;
            host.BoardAt = n => run.Phase == Sim.Run.RunPhase.Yard ? PlayerMotor.SpawnOnRoof(hostWorld.Train, 1 + (n - 1) % Math.Max(1, hostWorld.Train.Frames.Count - 1), 0, playerTuning)
                : Beside(hostWorld.Train, n, playerTuning);
        }
        if (setup.Enemies && route is not null)
            host.EnableEnemies(DataFile.Load<EnemyTuning>(Path.Combine(content, EnemyTuning.File)), route, route.Seed, expectedCrew);
        var (clientWorld, _) = setup.Build(content);
        var clientTransport = UdpTransport.Connect(new IPEndPoint(IPAddress.Loopback, hostTransport.Port));
        var client = new ClientSession(clientTransport, clientWorld, trainTuning, playerTuning);
        // The host's own player comes aboard before anyone else can: first aboard takes the cab.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (client.PlayerId is null && clock.Elapsed.TotalSeconds < 5)
        {
            host.Step();
            client.Step(default);
            Thread.Sleep(1);
        }
        return new NetPlaySession(host, hostTransport, client, clientTransport, setup, route);
    }

    public int Port => _hostTransport?.Port ?? 0;

    /// <summary>On the ballast beside the engine, a few metres apart: where someone who was waiting at a stop is standing.</summary>
    static PlayerState Beside(TrainOnLine train, int n, PlayerTuning p)
    {
        var engine = train.Frames[0];
        double side = n % 2 == 0 ? 1 : -1;
        var world = engine.ToWorld(new Ballast.Double3(side * (engine.Shape.HalfWidth + 2.5), 0, engine.Shape.HalfLength - 4 - 2 * (n / 2)));
        return PlayerMotor.SpawnOnGround(world, train.Line, train.Dynamics.Distance - train.Dynamics.Tuning.Geometry.EngineLength / 2, p);
    }

    /// <summary>Connects to a host and waits (up to the transport's connect timeout) for its Welcome.</summary>
    /// <param name="whileWaiting">Called each time round the wait (a test steps its in-process host here).</param>
    public static NetPlaySession Join(string content, IPEndPoint address, Action? whileWaiting = null, UdpOptions? options = null)
    {
        var transport = UdpTransport.Connect(address, options);
        var early = new List<TransportEvent>();
        string? session = null;
        var poll = new List<TransportEvent>();
        while (session is null)
        {
            poll.Clear();
            transport.Poll(poll);
            foreach (var e in poll)
            {
                if (e.Kind == TransportEventKind.Disconnected)
                {
                    transport.Dispose();
                    throw new IOException($"no answer from {address}");
                }
                if (e is { Kind: TransportEventKind.Data, Payload: { Length: > 0 } p } && p[0] == (byte)MessageType.Welcome)
                {
                    var r = new NetReader(p);
                    r.U8();
                    session = Messages.ReadWelcome(ref r).Session;
                }
            }
            early.AddRange(poll);
            whileWaiting?.Invoke();
            Thread.Sleep(5);
        }
        var setup = SessionSetup.Decode(session);
        if (setup.ContentDifferences(content) is { Count: > 0 } differ)
        {
            transport.Dispose();
            throw new IOException($"your content differs from the host's: {string.Join(", ", differ)}");
        }
        var (world, route) = setup.Build(content);
        var client = new ClientSession(new Replay(transport, early), world,
            DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File)), DataFile.Load<PlayerTuning>(Path.Combine(content, PlayerTuning.File)));
        return new NetPlaySession(null, null, client, transport, setup, route);
    }

    public void Step(in PlayerIntent intent)
    {
        Host?.Step();
        _previous = Client.Predicted;
        Client.Step(intent);
        Tick++;
        if (!_clientTransport.IsConnected && Client.Connected)
            Lost = true;
    }

    public IReadOnlyList<CarFrame> InterpolatedFrames(double alpha)
    {
        Train.FramesAt(alpha, _frames);
        return _frames;
    }

    public Camera EyeCamera(IReadOnlyList<CarFrame> frames, double alpha, double pendingYaw, double pendingPitch) =>
        Eyes.From(Player, _previous, frames, alpha, pendingYaw, pendingPitch);

    public IReadOnlyList<Crewmate> Crew(IReadOnlyList<CarFrame> frames, double alpha)
    {
        _crew.Clear();
        foreach (byte id in Client.RemoteIds)
            if (Client.TryGetRemote(id, alpha, out var s))
            {
                var (feet, yaw) = Eyes.World(s, frames);
                _crew.Add(new Crewmate(id, feet, yaw, s.Alive));
            }
        return _crew;
    }

    public string Status()
    {
        var d = Train.Dynamics;
        var p = Player;
        string role = Host is not null ? $"hosting :{Port}" : "joined";
        string link = Client.Waiting ? $"WAITING: {Client.WaitingReason}" : !Client.Connected ? "connecting…" : Lost ? "CONNECTION LOST"
            : $"{Client.RemoteIds.Count() + 1} aboard, ping {_clientTransport.RoundTrip(PeerId.Host) * 1000:0} ms";
        string where = PrototypeSession.Where(p, Train);
        string state = p.Alive ? $"{p.Surface} {where} hp {p.Health}" : $"DEAD ({p.Death})";
        return $"{d.Speed,5:0.0} m/s | thr {Controls.Throttle:0.00} brk {Controls.Brake:0} | P {Train.Boiler.Pressure,3:0} fire {Train.Boiler.Firebox:0.0} tender {Train.Boiler.Tender:0} | " +
               $"choir {World.Choir.Aggro:0} | {d.Distance / 1000:0.00}/{Train.Line.Length / 1000:0.0} km | {state} | {role} | {link}" +
               PrototypeSession.RouteStatus(Route, World, Train);
    }

    public void Dispose()
    {
        _clientTransport.Dispose();
        _hostTransport?.Dispose();
    }

    /// <summary>Hands the events read while waiting for the Welcome to the session, then gets out of the way.</summary>
    sealed class Replay(ITransport inner, List<TransportEvent> early) : ITransport
    {
        public PeerId LocalId => inner.LocalId;
        public void Send(PeerId to, ReadOnlySpan<byte> payload, Delivery delivery) => inner.Send(to, payload, delivery);
        public void Disconnect(PeerId peer) => inner.Disconnect(peer);
        public void Dispose() => inner.Dispose();

        public void Poll(List<TransportEvent> into)
        {
            into.AddRange(early);
            early.Clear();
            inner.Poll(into);
        }
    }
}
