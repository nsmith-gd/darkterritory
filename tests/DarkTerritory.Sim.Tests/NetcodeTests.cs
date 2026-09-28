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
        Assert.All(r.Clients, c => Assert.Equal(0, c.MaxCorrectionM));
        Assert.All(r.Clients, c => Assert.InRange(c.Snapshots, r.Ticks - 5, r.Ticks));
        Assert.Equal(0, r.Deaths);
        Assert.True(r.TrainSpeed > 20, "the conductor bot should have driven the train up to speed");
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
