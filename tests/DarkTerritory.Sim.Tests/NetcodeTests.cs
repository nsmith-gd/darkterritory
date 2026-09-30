using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

public class NetcodeTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly RailLine TestLoop = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));

    [Fact]
    public void OnAPerfectLinkPredictionMatchesTheHostExactly()
    {
        var r = Harness.Run(TestLoop, T, P, new HarnessOptions { Bots = 8, Seconds = 90, Link = LinkConditions.Perfect });
        // Exact, except that another crew member's cab input reaches us a round trip late: a roof
        // walker mid-jump when the conductor brakes lands a fraction of a millimetre off.
        Assert.All(r.Clients, c => Assert.True(c.MaxCorrectionM < 0.001, $"player {c.Id} corrected by {c.MaxCorrectionM} m"));
        Assert.True(r.Clients.Count(c => c.MaxCorrectionM == 0) >= r.Clients.Count - 2);
        Assert.All(r.Clients, c => Assert.InRange(c.Snapshots, r.Ticks - 5, r.Ticks));
        Assert.Equal(0, r.Deaths);
        Assert.InRange(r.TrainSpeed, 12, 16); // the conductor bot holds spec B.3 cruise
    }

    [Fact]
    public void OnARoughLinkCorrectionsStaySmallAndRare()
    {
        var r = Harness.Run(TestLoop, T, P, new HarnessOptions { Bots = 8, Seconds = 180, Link = LinkConditions.Rough, Seed = 3 });
        Assert.All(r.Clients, c => Assert.True(c.MaxCorrectionM < 2, $"player {c.Id} corrected by {c.MaxCorrectionM} m"));
        Assert.All(r.Clients, c => Assert.True(c.Corrections < 30, $"player {c.Id} corrected {c.Corrections} times"));
        Assert.All(r.Clients, c => Assert.True(c.Snapshots > r.Ticks * 0.9));
    }

    [Fact]
    public void EightPlayerSnapshotFitsInOnePacket() =>
        Assert.True(Harness.Run(TestLoop, T, P, new HarnessOptions { Bots = 8, Seconds = 2 }).SnapshotBytes < 1200);

    static (LoopbackNetwork Net, HostSession Host, ClientSession[] Clients) Session(int clients)
    {
        var net = new LoopbackNetwork();
        var host = new HostSession(net.CreateHost(), new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), TestLoop, 600), T, P);
        var cs = Enumerable.Range(0, clients)
            .Select(_ => new ClientSession(net.CreateClient(), new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), TestLoop, 600), T, P))
            .ToArray();
        return (net, host, cs);
    }

    static void Run(LoopbackNetwork net, HostSession host, ClientSession[] clients, int ticks, Func<int, PlayerIntent> intent)
    {
        for (int t = 0; t < ticks; t++)
        {
            net.Advance(SimConstants.TickSeconds);
            host.Step();
            for (int i = 0; i < clients.Length; i++)
                clients[i].Step(intent(i));
        }
    }

    [Fact]
    public void OnlySomeoneOnTheEngineCanDrive()
    {
        var (net, host, clients) = Session(2);
        // Client 0 spawns on the engine, client 1 on a car. Only client 1 asks for throttle.
        Run(net, host, clients, 60, i => i == 1 ? new PlayerIntent { ThrottleNotch = 4 } : default);
        Assert.Equal(0, host.Controls.Throttle);

        Run(net, host, clients, 5, i => i == 0 ? new PlayerIntent { ThrottleNotch = 4 } : default);
        Assert.Equal(1, host.Controls.Throttle);
    }

    [Fact]
    public void RemotePlayersInterpolateOnTheirCar()
    {
        var (net, host, clients) = Session(2);
        Run(net, host, clients, 30, i => i == 1 ? new PlayerIntent { MoveZ = 1 } : default);
        var viewer = clients[0];
        var remoteId = Assert.Single(viewer.RemoteIds);
        Assert.True(viewer.TryGetRemote(remoteId, 0.5, out var early));
        Run(net, host, clients, 10, i => i == 1 ? new PlayerIntent { MoveZ = 1 } : default);
        Assert.True(viewer.TryGetRemote(remoteId, 0.5, out var later));
        Assert.Equal(early.Parent, later.Parent);
        Assert.NotEqual(PlayerState.World, later.Parent);
        // Walking forward along the roof is −Z in the car's frame; 10 ticks at safe roof-walk speed.
        double expected = P.RoofWalkSafe * 10 * SimConstants.TickSeconds;
        Assert.InRange(early.Position.Z - later.Position.Z, expected * 0.99, expected * 1.01);
    }

    [Fact]
    public void AClientCutsTheTrainAndPredictsItExactly()
    {
        var (net, host, clients) = Session(2);
        // Warm up, then put client 1 on the coupler plate behind car 3 and hold Use.
        Run(net, host, clients, 10, _ => default);
        var cutter = clients[1];
        var plate = new PlayerState
        {
            Parent = 3,
            Surface = Surface.Coupler,
            Position = new Ballast.Double3(0, T.Geometry.CouplerHeight, T.Geometry.CarLength / 2 + 0.7),
            Health = 100,
        };
        HostTeleport(host, cutter.PlayerId!.Value, plate);
        Run(net, host, clients, 5, _ => default);
        foreach (var c in clients)
            c.ResetStats();
        Run(net, host, clients, 90, i => i == 1 ? new PlayerIntent { Buttons = PlayerButtons.Use } : default);

        Assert.Equal(2, host.Train.Rakes.Count);
        Assert.All(clients, c => Assert.Equal(2, c.Train.Rakes.Count));
        Assert.All(clients, c => Assert.Equal(0, c.MaxCorrection));
    }

    [Fact]
    public void AClientGunnerIsPredictedExactlyAndWakesTheSameChoir()
    {
        var net = new LoopbackNetwork();
        var combat = Tuning.Combat;
        TrainOnLine NewTrain() => new(new TrainDynamics(Consist.Uniform(T, 6, 1)), TestLoop, 600);
        var host = new HostSession(net.CreateHost(), NewTrain(), T, P, combat);
        var clients = new[] { new ClientSession(net.CreateClient(), NewTrain(), T, P, combat), new ClientSession(net.CreateClient(), NewTrain(), T, P, combat) };
        Run(net, host, clients, 10, _ => default);

        var mount = host.Train.Frames[0].Shape.Gun!.Value;
        var gunner = PlayerMotor.SpawnOnRoof(host.Train, 0, mount.Position.Z + 0.7, P);
        HostTeleport(host, clients[1].PlayerId!.Value, gunner);
        Run(net, host, clients, 5, _ => default);
        foreach (var c in clients)
            c.ResetStats();

        Run(net, host, clients, 90, i => i == 1 ? new PlayerIntent { Buttons = PlayerButtons.Fire } : default);
        Run(net, host, clients, 3, _ => default);
        int fired = combat.Guns.Ammo - host.Train.Vehicles[0].Gun.Ammo;
        Assert.InRange(fired, 8, 10); // 3 s at 3 rounds a second
        Assert.All(clients, c => Assert.Equal(host.Train.Vehicles[0].Gun.Ammo, c.Train.Vehicles[0].Gun.Ammo));
        Assert.All(clients, c => Assert.Equal(host.World.Choir.Aggro, c.World.Choir.Aggro, 6));
        Assert.All(clients, c => Assert.Equal(0, c.MaxCorrection));
    }

    static void HostTeleport(HostSession host, byte id, PlayerState state) => host.SetPlayerState(id, state);

    [Fact]
    public void GarbageFromAClientCannotCrashTheHost()
    {
        var net = new LoopbackNetwork();
        var host = new HostSession(net.CreateHost(), new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 3, 1)), TestLoop, 600), T, P);
        var evil = net.CreateClient();
        host.Step();
        var rng = new Random(9);
        for (int i = 0; i < 500; i++)
        {
            var junk = new byte[rng.Next(1, 64)];
            rng.NextBytes(junk);
            junk[0] = (byte)MessageType.Input;
            evil.Send(PeerId.Host, junk, Delivery.Unreliable);
            net.Advance(SimConstants.TickSeconds);
            host.Step();
        }
        var player = Assert.Single(host.Players).State;
        Assert.True(double.IsFinite(player.Position.X) && double.IsFinite(player.Yaw));
    }
}

/// <summary>The same netcode over real sockets: what two machines on a LAN (or an itch.io build) will run.</summary>
public class UdpNetcodeTests
{
    [Fact]
    public void EightBotsPlayOverRealUdpSockets()
    {
        var line = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));
        var r = Harness.Run(line, Tuning.Train, Tuning.Player, new HarnessOptions { Bots = 8, Seconds = 20, Udp = true });
        Assert.Equal("udp localhost", r.Link);
        Assert.Equal(8, r.Clients.Count);
        Assert.All(r.Clients, c => Assert.True(c.Snapshots > r.Ticks * 0.9, $"player {c.Id} got {c.Snapshots} of {r.Ticks} snapshots"));
        Assert.All(r.Clients, c => Assert.True(c.MaxCorrectionM < 0.01, $"player {c.Id} corrected by {c.MaxCorrectionM} m"));
        Assert.Equal(0, r.Deaths);
        Assert.True(r.TrainSpeed > 5, "the conductor should have the train moving");
    }
}

/// <summary>Thrown objects and bodies over the network: host-simulated, mirrored on every client.</summary>
public class BodyNetcodeTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;

    [Fact]
    public void AClientThrowsACrateAndEveryoneSeesItFlyAndLand()
    {
        var line = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));
        var net = new LoopbackNetwork();
        var host = new HostSession(net.CreateHost(), new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), line, 600), T, P);
        host.World.EnableBodies();
        var clients = Enumerable.Range(0, 2).Select(_ => new ClientSession(net.CreateClient(), new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), line, 600), T, P)).ToArray();
        void Run(int ticks, Func<int, PlayerIntent> intent)
        {
            for (int t = 0; t < ticks; t++)
            {
                net.Advance(SimConstants.TickSeconds);
                host.Step();
                for (int i = 0; i < clients.Length; i++)
                    clients[i].Step(intent(i));
            }
        }
        Run(10, _ => default);
        byte thrower = clients[1].PlayerId!.Value;
        var roof = PlayerMotor.SpawnOnRoof(host.Train, 3, 0, P) with { Yaw = -Math.PI / 2 };
        host.SetPlayerState(thrower, roof);
        var crate = host.World.Bodies.SpawnCrate(host.Train, 3, roof.Position + new Ballast.Double3(0.6, 0, 0));
        Run(10, _ => default);
        var watcher = clients[0];
        Assert.Single(watcher.World.Bodies.All);

        Run(2, i => i == 1 ? new PlayerIntent { Buttons = PlayerButtons.Use } : default);
        Run(2, _ => default);
        Assert.Equal(thrower, crate.Carrier);
        Assert.Equal(thrower, watcher.World.Bodies.All[0].Carrier);
        Run(2, i => i == 1 ? new PlayerIntent { Buttons = PlayerButtons.Throw } : default);
        Run(60, _ => default);
        Assert.Equal(-1, crate.Carrier);
        // Off the side, over the edge, onto the ground: everyone saw it end up where the host has it.
        Assert.Equal(PlayerState.World, crate.Parent);
        var seen = watcher.World.Bodies.All[0];
        Assert.Equal(PlayerState.World, seen.Parent);
        Assert.InRange((seen.Centre - crate.Centre).Length, 0, 1.0);
    }

    [Fact]
    public void WhoeverDiesLeavesABodyEveryoneCanSee()
    {
        var line = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));
        var net = new LoopbackNetwork();
        var host = new HostSession(net.CreateHost(), new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), line, 600), T, P);
        host.World.EnableBodies();
        var clients = Enumerable.Range(0, 2).Select(_ => new ClientSession(net.CreateClient(), new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), line, 600), T, P)).ToArray();
        for (int t = 0; t < 10; t++)
        {
            net.Advance(SimConstants.TickSeconds);
            host.Step();
            foreach (var c in clients)
                c.Step(default);
        }
        byte victim = clients[1].PlayerId!.Value;
        host.SetPlayerState(victim, PlayerMotor.SpawnOnRoof(host.Train, 2, 0, P) with { Health = 0, Death = DeathCause.Mauled });
        for (int t = 0; t < 30; t++)
        {
            net.Advance(SimConstants.TickSeconds);
            host.Step();
            foreach (var c in clients)
                c.Step(default);
        }
        var body = Assert.Single(clients[0].World.Bodies.All);
        Assert.Equal(Physics.BodyKind.Ragdoll, body.Kind);
        Assert.Equal(victim, body.Owner);
        Assert.Equal(2, body.Parent);
    }
}

/// <summary>M2's remainder: interest management and inert bodies (joining mid-run is GDD App. D.3's: HoldoutSessionTests).</summary>
public class SessionRulesTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly RailLine Line = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));

    static (LoopbackNetwork Net, HostSession Host, List<ClientSession> Clients, List<ITransport> Transports) Session(int clients, int cars = 6, Action<HostSession>? configure = null)
    {
        var net = new LoopbackNetwork();
        var host = new HostSession(net.CreateHost(), new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), Line, 1500), T, P);
        host.World.EnableBodies();
        configure?.Invoke(host);
        var transports = new List<ITransport>();
        var list = new List<ClientSession>();
        for (int i = 0; i < clients; i++)
        {
            var t = net.CreateClient();
            transports.Add(t);
            list.Add(Client(t, cars));
        }
        return (net, host, list, transports);
    }

    static ClientSession Client(ITransport t, int cars = 6) =>
        new(t, new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), Line, 1500), T, P);

    static void Run(LoopbackNetwork net, HostSession host, IEnumerable<ClientSession> clients, int ticks)
    {
        for (int t = 0; t < ticks; t++)
        {
            net.Advance(SimConstants.TickSeconds);
            host.Step();
            foreach (var c in clients)
                c.Step(default);
        }
    }

    [Fact]
    public void FarAwayEnemiesAndBodiesArentSent()
    {
        var (net, host, clients, _) = Session(2, cars: 20);
        host.EnableEnemies(Tuning.Enemies, null, 1, 2);
        Assert.Equal(Tuning.Enemies.InterestRadius, host.InterestRadius);
        // Shorter than the train, so each end is out of the other's interest.
        host.InterestRadius = 220;
        Run(net, host, clients, 10);
        // Client 0 has the cab; client 1 goes to the guard car, ~300 m back.
        int guard = host.Train.Dynamics.Consist.Vehicles[^1].Id;
        host.SetPlayerState(clients[1].PlayerId!.Value, PlayerMotor.SpawnOnRoof(host.Train, guard, 0, P));
        var shape = host.Train.Frames[guard].Shape;
        host.World.AddEnemy(id => new DarkTerritory.Sim.Enemies.Clinger(id) { Attached = guard, Local = new Ballast.Double3(shape.HalfWidth + 0.15, 2, 0) });
        host.World.Bodies.SpawnCrate(host.Train, 1, new Ballast.Double3(0, T.Geometry.CarHeight, 0));
        Run(net, host, clients, 20);
        Assert.Empty(clients[0].World.ActiveEnemies);
        Assert.Single(clients[1].World.ActiveEnemies);
        Assert.Single(clients[0].World.Bodies.All);
        Assert.Empty(clients[1].World.Bodies.All);
        Assert.True(host.RecordsSkipped > 0);
    }

    [Fact]
    public void AFollowerIsNeverSentToTheOneItsFollowing()
    {
        // App. A.3: "visible ONLY to other players, never to the carrier".
        var (net, host, clients, _) = Session(2);
        host.EnableEnemies(Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9 } }, null, 1, 2);
        Run(net, host, clients, 10);
        byte carrier = clients[1].PlayerId!.Value;
        var near = host.Train.Line.Sample(host.Train.Dynamics.Distance - 30);
        var ground = PlayerMotor.SpawnOnGround(near.Position + Ballast.Double3.Cross(near.Tangent, Ballast.Double3.Up).Normalized * 3.5, host.Train.Line,
            host.Train.Dynamics.Distance - 30, P);
        host.SetPlayerState(carrier, ground);
        host.World.AddEnemy(id => DarkTerritory.Sim.Enemies.Follower.Behind(id, host.Train, ground, carrier, Tuning.Enemies.Followers));
        Run(net, host, clients, 20);
        Assert.Single(clients[0].World.ActiveEnemies, e => e is DarkTerritory.Sim.Enemies.Follower);
        Assert.DoesNotContain(clients[1].World.ActiveEnemies, e => e is DarkTerritory.Sim.Enemies.Follower);
    }

    [Fact]
    public void SomeoneWhoDropsOutLeavesTheirBodyBehind()
    {
        // Spec E: "Character remains as an inert body until recovered or the run ends."
        var (net, host, clients, transports) = Session(2);
        Run(net, host, clients, 10);
        byte leaver = clients[1].PlayerId!.Value;
        transports[1].Dispose();
        Run(net, host, [clients[0]], 20);
        Assert.Equal(1, host.PlayerCount);
        var body = Assert.Single(clients[0].World.Bodies.All);
        Assert.Equal(leaver, body.Owner);
    }
}
