using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Rejoining a night after a drop (ARCHITECTURE §8 note 253): the host holds a dropped player's slot for player.json
/// rejoin.reserveSeconds, and a client that comes back with the slot's token (from its Welcome) gets its own crewmate back.
/// </summary>
public class RejoinTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly RailLine Line = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));

    static TrainOnLine NewTrain() => new(new TrainDynamics(Consist.Uniform(T, 6, 1)), Line, 1500);

    sealed class Night
    {
        public readonly LoopbackNetwork Net = new();
        public readonly HostSession Host;
        public readonly List<ClientSession> Clients = [];
        public readonly List<ITransport> Links = [];

        public Night(int clients, PlayerTuning? tuning = null)
        {
            Host = new HostSession(Net.CreateHost(), NewTrain(), T, tuning ?? P);
            Host.World.EnableBodies();
            for (int i = 0; i < clients; i++)
            {
                var link = Net.CreateClient();
                Links.Add(link);
                Clients.Add(new ClientSession(link, NewTrain(), T, tuning ?? P) { Name = $"crew{i}" });
            }
        }

        public void Run(int ticks, Func<int, PlayerIntent>? intent = null)
        {
            for (int t = 0; t < ticks; t++)
            {
                Net.Advance(SimConstants.TickSeconds);
                Host.Step();
                for (int i = 0; i < Clients.Count; i++)
                    Clients[i].Step(intent?.Invoke(i) ?? default);
            }
        }

        /// <summary>Client i's link goes (its end closed: the host hears it go, the client too).</summary>
        public void Drop(int i) => Links[i].Dispose();

        /// <summary>Client i connects again on a new link and asks for its slot back.</summary>
        public void Redial(int i)
        {
            Links[i] = Net.CreateClient();
            Clients[i].Reconnect(Links[i]);
        }

        public PlayerState HostState(byte id) => Host.Players.Single(p => p.Id == id).State;
    }

    [Fact]
    public void TheWireCarriesTheSlotsToken()
    {
        var w = new NetWriter();
        Messages.WriteWelcome(w, 3, 77, "{}", 0xDEADBEEF12345678);
        var r = new NetReader(w.Written);
        Assert.Equal((byte)MessageType.Welcome, r.U8());
        Assert.Equal(((byte)3, 77u, "{}", 0xDEADBEEF12345678ul), Messages.ReadWelcome(ref r));
        Messages.WriteHello(w, "Dave", 42);
        r = new NetReader(w.Written);
        r.U8();
        Assert.Equal(("Dave", 42ul, Messages.NoOutfit), Messages.ReadHello(ref r));
        // Note 298: and the outfit they come in.
        Messages.WriteHello(w, "Dave", 42, outfit: 5);
        r = new NetReader(w.Written);
        r.U8();
        Assert.Equal(("Dave", 42ul, (byte)5), Messages.ReadHello(ref r));
        Assert.Equal(37, Protocol.Version);
    }

    [Fact]
    public void ADroppedPlayerComesBackToTheirOwnCrewmate()
    {
        var night = new Night(3);
        night.Run(20);
        var leaver = night.Clients[1];
        byte id = leaver.PlayerId!.Value;
        Assert.NotEqual(0ul, leaver.Token);
        // Something worth getting back: the wrench, and a knock taken.
        var kit = Kit.Of([Tool.Crowbar, Tool.Wrench]);
        night.Host.SetPlayerState(id, night.HostState(id) with { Kit = kit, Health = 63 });
        night.Run(10);

        night.Drop(1);
        night.Run(30);
        Assert.True(leaver.Dropped);
        // While they're gone: an inert body where they stood (spec E), their kit on it, their place held. Nobody else.
        Assert.True(night.Host.IsReserved(id));
        Assert.Equal(2, night.Host.PlayerCount);
        var body = Assert.Single(night.Host.World.Bodies.All, b => b.Kind == BodyKind.Ragdoll);
        Assert.Equal(id, body.Owner);
        Assert.Equal(kit, body.Tools);
        foreach (var other in new[] { night.Clients[0], night.Clients[2] })
            Assert.DoesNotContain(id, other.RemoteIds);

        night.Redial(1);
        night.Run(30);
        // The same slot, standing up where the body lay, with what was on it.
        Assert.True(leaver.Connected);
        Assert.Equal(id, leaver.PlayerId);
        Assert.Equal(1, leaver.Reconnects);
        Assert.Equal(1, night.Host.Rejoins);
        Assert.Equal(0, night.Host.Reserved);
        Assert.Equal(3, night.Host.PlayerCount);
        var back = night.HostState(id);
        Assert.True(back.Alive);
        Assert.Equal(63, back.Health);
        Assert.Equal(kit, back.Kit);
        Assert.Equal(body.Parent, back.Parent);
        Assert.True((back.Position - body.Centre).Length < 1.5, $"stood up {(back.Position - body.Centre).Length:0.00} m from the body");
        // It sees its own state; the body's gone from every world, and everyone else sees it once, as itself.
        Assert.Equal(back.Position, leaver.Predicted.Position);
        Assert.DoesNotContain(night.Host.World.Bodies.All, b => b.Kind == BodyKind.Ragdoll);
        foreach (var c in night.Clients)
            Assert.DoesNotContain(c.World.Bodies.All, b => b.Kind == BodyKind.Ragdoll);
        foreach (var other in new[] { night.Clients[0], night.Clients[2] })
        {
            Assert.Single(other.RemoteIds, r => r == id);
            Assert.Equal(2, other.RemoteIds.Count());
        }
        Assert.DoesNotContain(id, leaver.RemoteIds);
        Assert.Equal("crew1", night.Host.World.Names[id]);

        // And it predicts as exactly as it did before (a perfect link: no corrections at all).
        leaver.ResetStats();
        night.Run(60, i => i == 1 ? new PlayerIntent { MoveZ = 1, LookYaw = 0.01f } : default);
        Assert.Equal(0, leaver.Corrections);
        Assert.True(leaver.SnapshotsReceived > 60);
    }

    [Fact]
    public void TheDeadComeBackDead()
    {
        // A player who'd died keeps the dead channel: they come back dead, of what killed them, not a fresh crewmate.
        var night = new Night(2);
        night.Run(20);
        byte id = night.Clients[1].PlayerId!.Value;
        night.Host.SetPlayerState(id, night.HostState(id) with { Health = 0, Death = DeathCause.Mauled });
        night.Run(10);
        int bodies = night.Host.World.Bodies.All.Count(b => b.Kind == BodyKind.Ragdoll);
        night.Drop(1);
        night.Run(20);
        night.Redial(1);
        night.Run(30);
        Assert.Equal(id, night.Clients[1].PlayerId);
        Assert.Equal(DeathCause.Mauled, night.HostState(id).Death);
        Assert.Equal(DeathCause.Mauled, night.Clients[1].Predicted.Death);
        // No second body: dropping out dead leaves nothing more.
        Assert.Equal(bodies, night.Host.World.Bodies.All.Count(b => b.Kind == BodyKind.Ragdoll));
    }

    [Fact]
    public void PastTheReserveTheSlotFrees()
    {
        var tuning = P with { Rejoin = P.Rejoin with { ReserveSeconds = 1 } };
        var night = new Night(2, tuning);
        night.Run(20);
        byte id = night.Clients[1].PlayerId!.Value;
        night.Drop(1);
        night.Run(10);
        Assert.True(night.Host.IsReserved(id));
        night.Run(SimConstants.TickRate);
        // Let go: the token's no good any more, and the body stays where it fell (D.2).
        Assert.False(night.Host.IsReserved(id));
        Assert.Equal(1, night.Host.ReservesExpired);
        night.Redial(1);
        night.Run(30);
        Assert.True(night.Clients[1].Connected);
        Assert.NotEqual(id, night.Clients[1].PlayerId);
        Assert.Equal(0, night.Host.Rejoins);
        Assert.Equal(2, night.Host.PlayerCount);
        Assert.Single(night.Host.World.Bodies.All, b => b.Kind == BodyKind.Ragdoll && b.Owner == id);
        // The one still playing sees the newcomer, and not the slot that was let go.
        Assert.Equal([night.Clients[1].PlayerId!.Value], night.Clients[0].RemoteIds);
    }

    [Fact]
    public void ANewLinkBeforeTheHostSawTheOldOneGoTakesOver()
    {
        // The client gave up on its link first (its own timeout, a socket error) and connects again while the host still holds
        // the old one: the new link takes the crewmate over where it stands, and the old one's closed. No body, no gap.
        var night = new Night(2);
        night.Run(20);
        var client = night.Clients[1];
        byte id = client.PlayerId!.Value;
        var before = night.HostState(id);
        var stale = night.Links[1];
        night.Links[1] = night.Net.CreateClient();
        client.Reconnect(night.Links[1]);
        night.Run(30);
        Assert.Equal(id, client.PlayerId);
        Assert.True(client.Connected);
        Assert.Equal(2, night.Host.PlayerCount);
        Assert.Equal(1, night.Host.Rejoins);
        Assert.DoesNotContain(night.Host.World.Bodies.All, b => b.Kind == BodyKind.Ragdoll);
        Assert.Equal(before.Parent, night.HostState(id).Parent);
        stale.Dispose();
        night.Run(10);
        Assert.Equal(2, night.Host.PlayerCount);
        Assert.Equal(0, night.Host.Reserved);
    }

    [Fact]
    public void ARedialGivenUpOnBeforeItSpokeIsNoOne()
    {
        // The 5 Oct CI failure: a redial the client gave up on before it said hello (its socket closed, the host's Accept
        // unheard) left a connection nobody was behind, and greetSeconds later the host welcomed it as a third crewmate.
        // It must hold up the line no longer than that, and never come aboard; the real redial behind it gets the slot.
        var night = new Night(2);
        night.Run(20);
        var client = night.Clients[1];
        byte id = client.PlayerId!.Value;
        night.Drop(1);
        night.Run(5);
        var abandoned = night.Net.CreateClient();
        var events = new List<TransportEvent>();
        for (int t = 0; t < 5; t++)
        {
            night.Run(1);
            abandoned.Poll(events);
        }
        night.Redial(1);
        night.Run(SimConstants.TickRate * 2);
        Assert.Equal(id, client.PlayerId);
        Assert.True(client.Connected);
        Assert.Equal(1, night.Host.Rejoins);
        Assert.Equal(2, night.Host.PlayerCount);
        night.Run(SimConstants.TickRate * 3);
        Assert.Equal(2, night.Host.PlayerCount);
        abandoned.Dispose();
    }

    [Fact]
    public void WithReclaimBodyOffTheBodyStaysAndTheyDropIn()
    {
        // GDD v1.4 App. D.2's letter (rejoin.reclaimBody false): the body stays for the crew to recover, and the player comes
        // back as a joiner would, in the same slot.
        var stand = PlayerMotor.SpawnOnRoof(NewTrain(), 4, 2, P);
        var night = new Night(2, P with { Rejoin = P.Rejoin with { ReclaimBody = false } });
        night.Host.BoardAt = _ => stand;
        night.Run(20);
        byte id = night.Clients[1].PlayerId!.Value;
        night.Drop(1);
        night.Run(10);
        night.Redial(1);
        night.Run(30);
        Assert.Equal(id, night.Clients[1].PlayerId);
        Assert.Single(night.Host.World.Bodies.All, b => b.Kind == BodyKind.Ragdoll && b.Owner == id);
        Assert.Equal(4, night.HostState(id).Parent);
        Assert.Equal(P.StartingKit, night.HostState(id).Kit);
    }

    [Fact]
    public void ABotDropsAndRejoinsMidNightOnALossyLink()
    {
        // The harness's --drop-rejoin: a roof walker's link goes 20 s in and it's back 6 s later, over the rough link.
        var r = Harness.Run(Line, T, P, new HarnessOptions { Bots = 8, Seconds = 50, Link = LinkConditions.Rough, Seed = 2, DropRejoin = new DropRejoin(3, 20, 6) });
        var back = Assert.IsType<RejoinReport>(r.Rejoin);
        Assert.Equal(back.Was, back.Back);
        Assert.True(back.Was > 0);
        Assert.InRange(back.ReconnectSeconds, 0, 2);
        Assert.Equal(1, back.HostRejoins);
        Assert.Equal(0, back.Reserved);
        Assert.True(back.OthersSeeOne);
        Assert.Equal(8, r.Clients.Count(c => c.Id > 0));
        Assert.Equal(8, r.Clients.Select(c => c.Id).Distinct().Count());
        Assert.True(back.MaxCorrectionAfterM < 2, $"corrected by {back.MaxCorrectionAfterM} m after coming back");
    }

    [Fact]
    public void OnAPerfectLinkARejoinerPredictsExactly()
    {
        var r = Harness.Run(Line, T, P, new HarnessOptions { Bots = 8, Seconds = 45, Link = LinkConditions.Perfect, DropRejoin = new DropRejoin(2, 15, 4) });
        var back = Assert.IsType<RejoinReport>(r.Rejoin);
        Assert.Equal(back.Was, back.Back);
        Assert.True(back.OthersSeeOne);
        Assert.True(back.MaxCorrectionAfterM < 0.001, $"corrected by {back.MaxCorrectionAfterM} m after coming back");
        Assert.All(r.Clients, c => Assert.True(c.MaxCorrectionM < 0.001, $"player {c.Id} corrected by {c.MaxCorrectionM} m"));
    }
}
