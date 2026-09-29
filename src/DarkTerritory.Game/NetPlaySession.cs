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
    public string Encode() => JsonSerializer.Serialize(this, DataFile.Options);
    public static SessionSetup Decode(string json) => JsonSerializer.Deserialize<SessionSetup>(json, DataFile.Options) ?? new SessionSetup();

    /// <summary>The world this setup describes, identically on every machine.</summary>
    public (World World, Route? Route) Build(string content)
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
        return (new World(train, combat), route);
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
        var (hostWorld, route) = setup.Build(content);
        var hostTransport = UdpTransport.Host(port);
        var host = new HostSession(hostTransport, hostWorld, trainTuning, playerTuning) { SessionInfo = setup.Encode() };
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
        string link = !Client.Connected ? "connecting…" : Lost ? "CONNECTION LOST" : $"{Client.RemoteIds.Count() + 1} aboard, ping {_clientTransport.RoundTrip(PeerId.Host) * 1000:0} ms";
        string where = p.Parent == PlayerState.World ? "ground" : PlayerMotor.InCab(p, Train) ? "cab" : p.Parent == 0 ? "engine" : $"car {p.Parent}";
        string state = p.Alive ? $"{p.Surface} {where} hp {p.Health}" : $"DEAD ({p.Death})";
        return $"{d.Speed,5:0.0} m/s | thr {Controls.Throttle:0.00} brk {Controls.Brake:0} | P {Train.Boiler.Pressure,3:0} fire {Train.Boiler.Firebox:0.0} | " +
               $"choir {World.Choir.Aggro:0} | {d.Distance / 1000:0.00}/{Train.Line.Length / 1000:0.0} km | {state} | {role} | {link}";
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
