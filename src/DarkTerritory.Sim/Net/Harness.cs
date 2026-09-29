using Ballast.Net;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Net;

public sealed record HarnessOptions
{
    public int Bots { get; init; } = 8;
    public int Cars { get; init; } = 10;
    public double Seconds { get; init; } = 120;
    public int Seed { get; init; } = 1;
    public LinkConditions Link { get; init; } = LinkConditions.Rough;
    public double StartDistance { get; init; } = 600;
    public CombatTuning? Combat { get; init; }
    /// <summary>With enemy tuning the host runs the director and the route's threats.</summary>
    public EnemyTuning? Enemies { get; init; }
    public Route.Route? Route { get; init; }
    /// <summary>Real UDP sockets on localhost instead of the simulated network (runs in real time's order, not faster).</summary>
    public bool Udp { get; init; }
    /// <summary>With a route: run the night as a game (departure, facilities, terminus, dawn) and report the result.</summary>
    public Run.RunTuning? Run { get; init; }
    public double YardLength { get; init; } = 600;
    /// <summary>
    /// The facilities' loading modules. With a crew for it (a driver, a shunter and two for the winch: five bots, or four
    /// with the gunner lending a hand), the bots stop at the winch facilities and load (T32).
    /// </summary>
    public Run.FacilityTuning? Facilities { get; init; }
    /// <summary>With it, the crew can revive the dead (spec C.2); bots don't hold Vigils yet.</summary>
    public Run.VigilTuning? Vigil { get; init; }
    /// <summary>Another network to run over (the CLI's fake Steam lobby), in place of the loopback or UDP.</summary>
    public IHarnessNetwork? Network { get; init; }
    /// <summary>
    /// Called every tick after everyone has stepped, with each bot and its player's state on the host, and the host's world
    /// (to trace a night).
    /// </summary>
    public Action<uint, IReadOnlyList<(IBot Bot, PlayerState State)>, World>? Observe { get; init; }
}

/// <summary>Transports for the harness from elsewhere: the Sim doesn't reference platform code, so the CLI brings it.</summary>
public interface IHarnessNetwork : IDisposable
{
    string Name { get; }
    ITransport Host();
    ITransport Client(int index);
    /// <summary>Once a tick, before anyone steps: pump the platform.</summary>
    void Pump();
}

public sealed record ClientReport(byte Id, string Bot, double MaxCorrectionM, int Corrections, int Snapshots, int HostMissedInputs,
    bool Alive, string Surface, string Where, long BytesUp, long BytesDown);

/// <param name="WarmUps">Times a bot went in out of the cold (down to a coupler plate, in through a door and shut it; T31).</param>
/// <param name="Stops">The facility stops the crew worked, as the driver saw them (T32).</param>
public sealed record HarnessReport(int Ticks, double Seconds, string Link, double TrainDistance, double TrainSpeed, double BoilerPressure, double Tender,
    int SnapshotBytes, double DownKbpsPerClient, double UpKbpsPerClient, double MaxCorrectionM, int Deaths,
    IReadOnlyList<ClientReport> Clients, ThreatReport? Threats = null, Run.RunReport? Run = null, int WarmUps = 0,
    IReadOnlyList<StopRecord>? Stops = null);

/// <summary>What the director and the enemies did (GDD §34 / App. B.9 audit).</summary>
public sealed record ThreatReport(double Budget, double Spent, IReadOnlyDictionary<string, int> Spawned, IReadOnlyDictionary<string, int> Punishes,
    IReadOnlyDictionary<string, int> DeathsByCause, int FairnessViolations, bool Derailed, double ChoirPeak, double MeanCargoIntegrity, int RoundsFired)
{
    /// <summary>The conflict-table pairs the director put together (App. B.1).</summary>
    public IReadOnlyList<string> Pairs { get; init; } = [];
}

/// <summary>
/// Host plus N bot clients in one process over a <see cref="LoopbackNetwork"/> with simulated lag and loss,
/// faster than real time (GDD §34 "Stability: desync and crash detection across 2–8 clients").
/// </summary>
public static class Harness
{
    public static HarnessReport Run(RailLine line, TrainTuning trainTuning, PlayerTuning playerTuning, HarnessOptions o, BoilerTuning? boiler = null)
    {
        var net = new LoopbackNetwork(o.Seed, o.Link);
        var udpHost = o.Udp ? UdpTransport.Host(0, bind: System.Net.IPAddress.Loopback) : null;
        ITransport ClientTransport(int i) => o.Network?.Client(i)
            ?? (udpHost is null ? net.CreateClient() : UdpTransport.Connect(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, udpHost.Port)));
        var hostTransport = new CountingTransport(o.Network?.Host() ?? udpHost ?? net.CreateHost());
        var host = new HostSession(hostTransport, NewTrain(line, trainTuning, o, boiler), trainTuning, playerTuning, o.Combat);
        if (o.Enemies is { } et)
            host.EnableEnemies(et, o.Route, (ulong)o.Seed, o.Bots);
        host.World.EnableBodies();
        host.World.Stock();
        if (o.Run is { } rt && o.Route is { } route)
            host.World.EnableRun(rt, route, o.YardLength, authority: true, o.Facilities);
        if (o.Vigil is { } vt)
            host.World.EnableVigil(vt);

        // On a night with facilities, the crew call to each other at the stops, and each has a part: the walkers first (a
        // shunter, the winch pair, then crates), and the gunner only if it takes them to make up the winch pair.
        var calls = o.Run is not null && o.Route is not null && o.Facilities is not null ? new CrewCalls() : null;
        bool gunner = o.Combat is not null;
        var hands = Enumerable.Range(1, Math.Max(0, o.Bots - 1)).OrderBy(i => i == 1 && gunner ? 1 : 0).ToList();
        StopJob JobOf(int i) => hands.IndexOf(i) switch
        {
            0 => StopJob.Shunter,
            1 => StopJob.Winch0,
            2 => StopJob.Winch1,
            _ => i == 1 && gunner ? StopJob.None : StopJob.Crates,
        };
        StopHand? Hand(int i) => calls is null ? null : new StopHand(JobOf(i), calls, i, playerTuning.Cold);
        var clients = new List<(ClientSession Session, IBot Bot, CountingTransport Transport)>();
        for (int i = 0; i < o.Bots; i++)
        {
            var transport = new CountingTransport(ClientTransport(i));
            IBot bot = i == 0 ? new ConductorBot(calls, i)
                : i == 1 && o.Combat is { } c ? new GunnerBot(c.Guns, c.Choir, o.Seed * 1000 + i, playerTuning.Cold, Hand(i))
                : new RoofWalkerBot(o.Seed * 1000 + i, playerTuning.Cold, Hand(i));
            var session = new ClientSession(transport, NewTrain(line, trainTuning, o, boiler), trainTuning, playerTuning, o.Combat);
            if (o.Vigil is { } v)
                session.World.EnableVigil(v);
            // Clients see the night as players do: the phase, and each site's winch (mirrored from the host).
            if (o.Run is { } crt && o.Route is { } croute)
                session.World.EnableRun(crt, croute, o.YardLength, authority: false, o.Facilities);
            clients.Add((session, bot, transport));
        }

        int ticks = (int)(o.Seconds * SimConstants.TickRate);
        var events = new List<EnemyEvent>();
        var deaths = new Dictionary<string, int>();
        double choirPeak = 0;
        int rounds = 0;
        for (uint t = 0; t < ticks; t++)
        {
            if (host.World.Run is { Over: true })
            {
                ticks = (int)t;
                break;
            }
            net.Advance(SimConstants.TickSeconds);
            o.Network?.Pump();
            host.Step();
            events.AddRange(host.World.EnemyEvents);
            rounds += host.World.Shots.Count;
            choirPeak = Math.Max(choirPeak, host.World.Choir.Aggro);
            // Once everyone's in, the gunner goes to the guard gun (a host-side respawn at their post).
            if (t == 30)
                PostGunner(host, clients.Select(c => (c.Session, c.Bot)).ToList());
            if (t == 60)
                foreach (var c in clients)
                    c.Session.ResetStats();
            foreach (var (session, bot, _) in clients)
            {
                PlayerIntent intent = default;
                if (session.Connected)
                {
                    // Its part at a stop needs its own id for the heavy crates (T45).
                    if (((bot as GunnerBot)?.Job ?? (bot as RoofWalkerBot)?.Job) is { } part)
                        part.PlayerId = session.PlayerId;
                    intent = bot is IWorldBot wb ? wb.Decide(session.Predicted, session.World, t, out _) : bot.Decide(session.Predicted, session.Train, t);
                    intent = Heed.Rattles(intent, session.Predicted, session.World, playerTuning);
                }
                session.Step(intent);
            }
            if (o.Observe is { } observe)
            {
                var states = host.Players.ToDictionary(p => p.Id, p => p.State);
                observe(t, [.. clients.Select(c => (c.Bot, c.Session.PlayerId is { } pid ? states.GetValueOrDefault(pid) : default))], host.World);
            }
        }

        var hostPlayers = host.Players.ToDictionary(p => p.Id, p => p.State);
        var reports = clients.Select(c =>
        {
            byte id = c.Session.PlayerId ?? 0;
            var s = hostPlayers.GetValueOrDefault(id);
            string where = s.Parent == PlayerState.World ? "ground" : s.Parent == 0 ? "engine" : $"car {s.Parent}";
            return new ClientReport(id, c.Bot.Name, Math.Round(c.Session.MaxCorrection, 4), c.Session.Corrections, c.Session.SnapshotsReceived,
                host.MissedInputs(id), s.Alive, s.Surface.ToString(), where, c.Transport.BytesSent, c.Transport.BytesReceived);
        }).ToList();

        double seconds = ticks * SimConstants.TickSeconds;
        ThreatReport? threats = null;
        if (host.World.Director is { } d)
        {
            foreach (var p in host.Players.Where(p => !p.State.Alive))
                deaths[p.State.Death.ToString()] = deaths.GetValueOrDefault(p.State.Death.ToString()) + 1;
            int unfair = events.Count(e => e.To == SpinePhase.Commit && (e.From != SpinePhase.Telegraph || e.SecondsInFrom < o.Enemies!.MinReactionSeconds - 1e-9));
            threats = new ThreatReport(Math.Round(d.Budget, 1), Math.Round(d.Spent, 1),
                d.Log.GroupBy(l => l.Kind.ToString()).ToDictionary(g => g.Key, g => g.Count()),
                events.Where(e => e.To == SpinePhase.Punish).GroupBy(e => e.Kind.ToString()).ToDictionary(g => g.Key, g => g.Count()),
                deaths, unfair, host.World.Derailed, Math.Round(choirPeak, 1),
                Math.Round(host.Train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo).DefaultIfEmpty().Average(v => v?.CargoIntegrity ?? 1), 3), rounds)
            { Pairs = [.. d.Pairs] };
        }
        if (o.Udp || o.Network is not null)
        {
            foreach (var c in clients)
                c.Transport.Dispose();
            hostTransport.Dispose();
        }
        string link = o.Network is { } n ? n.Name : o.Udp ? "udp localhost" : $"{o.Link.LatencySeconds * 1000:0}ms ±{o.Link.JitterSeconds * 1000:0} loss {o.Link.LossRate:P0}";
        return new HarnessReport(ticks, seconds, link,
            Math.Round(host.Train.Dynamics.Distance, 1), Math.Round(host.Train.Dynamics.Speed, 2),
            Math.Round(host.Train.Boiler.Pressure, 1), Math.Round(host.Train.Boiler.Tender), host.LastSnapshotBytes,
            Math.Round(reports.Average(r => r.BytesDown) * 8 / 1000 / seconds, 1), Math.Round(reports.Average(r => r.BytesUp) * 8 / 1000 / seconds, 1),
            reports.Max(r => r.MaxCorrectionM), reports.Count(r => !r.Alive), reports, threats,
            host.World.Run is { } run ? run.Report ?? run.Tally(host.World, [.. host.Players.Select(p => p.State)]) : null,
            clients.Sum(c => c.Bot switch { RoofWalkerBot r => r.WarmUps, GunnerBot g => g.WarmUps, _ => 0 }),
            clients.Select(c => c.Bot).OfType<ConductorBot>().FirstOrDefault()?.Stops?.Log);
    }

    /// <summary>A bot and what it's doing, in a few words (the harness trace).</summary>
    public static string Describe(IBot bot, in PlayerState s)
    {
        string where = !s.Alive ? $"dead ({s.Death})" : s.Parent == PlayerState.World ? "ground" : $"{s.Surface} {s.Parent}";
        string doing = bot switch
        {
            ConductorBot c => c.Stops?.Doing.ToString() ?? "",
            RoofWalkerBot r => r.WarmUpStep is { } w and not "Off" ? $"warm:{w}" : r.Job?.Doing ?? "",
            GunnerBot g => g.Job?.Doing ?? "",
            _ => "",
        };
        return doing.Length > 0 ? $"{bot.Name}[{doing}] {where}" : $"{bot.Name} {where}";
    }

    static void PostGunner(HostSession host, List<(ClientSession Session, IBot Bot)> clients)
    {
        var gunner = clients.FirstOrDefault(c => c.Bot is GunnerBot);
        if (gunner.Session?.PlayerId is not { } id)
            return;
        var guard = host.Train.Dynamics.Consist.Vehicles.LastOrDefault(v => v.Kind == VehicleKind.Guard);
        if (guard is null || host.Train.Frames[guard.Id].Shape.Gun is not { } mount)
            return;
        var post = PlayerMotor.SpawnOnRoof(host.Train, guard.Id, mount.Position.Z - mount.Facing.Z * 0.7, host.PlayerTuning);
        post.Yaw = Math.PI;
        host.SetPlayerState(id, post);
    }

    /// <summary>On a night the cars leave the fortress as they do in the game, part loaded (run.json departureLoad).</summary>
    static TrainOnLine NewTrain(RailLine line, TrainTuning t, HarnessOptions o, BoilerTuning? boiler) =>
        new(new TrainDynamics(Consist.Uniform(t, o.Cars, o.Run is { } r && o.Route is not null ? r.DepartureLoad : 1)), line, o.StartDistance, boiler);

    /// <summary>Counts payload bytes both ways for bandwidth reporting.</summary>
    sealed class CountingTransport(ITransport inner) : ITransport
    {
        public long BytesSent, BytesReceived;
        public PeerId LocalId => inner.LocalId;

        public void Send(PeerId to, ReadOnlySpan<byte> payload, Delivery delivery)
        {
            BytesSent += payload.Length;
            inner.Send(to, payload, delivery);
        }

        public void Poll(List<TransportEvent> into)
        {
            int start = into.Count;
            inner.Poll(into);
            for (int i = start; i < into.Count; i++)
                BytesReceived += into[i].Payload?.Length ?? 0;
        }

        public void Disconnect(PeerId peer) => inner.Disconnect(peer);
        public void Dispose() => inner.Dispose();
    }
}
