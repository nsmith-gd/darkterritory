using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// GDD App. D over the real netcode (a host and clients on the loopback): joining (D.3), the queue that only the dead
/// see (D.6), the spectator's ears and the dead channel (D.10), Call Out and the Live Mic (D.7).
/// </summary>
[Collection(nameof(LineGenTests))]
public class HoldoutSessionTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Route.Route Line = Routes.Generate(Content, "frontier:7", 6);
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly byte[] Opus = [1, 2, 3, 4, 5, 6, 7, 8];

    internal sealed class Crew
    {
        readonly LoopbackNetwork _net;
        public readonly HostSession Host;
        public readonly List<ClientSession> Clients = new();
        public readonly List<PlayerIntent> Intents = new();

        public Crew(double front)
        {
            _net = new LoopbackNetwork();
            Host = new HostSession(_net.CreateHost(), Build(front, authority: true), T, P);
            Host.World.EnableBodies();
        }

        World Build(double front, bool authority)
        {
            var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), Line.Build(), front, Tuning.Boiler), Tuning.Combat);
            world.EnableRun(Tuning.Run, Line, Line.GateOr(600), authority);
            world.EnableHoldouts(Tuning.Holdouts);
            return world;
        }

        /// <summary>Everyone aboard at once, at the fortress (they're all in the session as the run starts, D.3).</summary>
        public Crew(double front, int players) : this(front)
        {
            for (int i = 0; i < players; i++)
                Add();
            Run(0.3);
        }

        ClientSession Add()
        {
            var c = new ClientSession(_net.CreateClient(), Build(Host.Train.Dynamics.Distance, authority: false), T, P);
            Clients.Add(c);
            Intents.Add(default);
            return c;
        }

        /// <summary>Someone joins now.</summary>
        public ClientSession Join()
        {
            var c = Add();
            Run(0.3);
            return c;
        }

        public World World => Host.World;
        public byte Id(int i) => Clients[i].PlayerId!.Value;
        public PlayerState State(int i) => Host.Players.First(p => p.Id == Id(i)).State;

        public void Run(double seconds, double? holdSpeed = null)
        {
            for (int t = 0; t < Math.Max(1, seconds * SimConstants.TickRate); t++)
            {
                if (holdSpeed is { } v)
                    Host.Train.Dynamics.Velocity = v;
                _net.Advance(SimConstants.TickSeconds);
                Host.Step();
                for (int i = 0; i < Clients.Count; i++)
                    Clients[i].Step(Intents[i]);
            }
        }

        public void Kill(int i, double at)
        {
            var s = PlayerMotor.SpawnOnGround(Host.Train.Line.Sample(at).Position + new Double3(4, 0, 0), Host.Train.Line, at, P);
            Host.SetPlayerState(Id(i), s with { Health = 0, Death = DeathCause.Mauled });
        }

        public List<VoiceFrame> Heard(int i)
        {
            var list = Clients[i].VoiceFrames.ToList();
            Clients[i].VoiceFrames.Clear();
            return list;
        }
    }

    static PlanHoldout Site => Line.Plan!.Holdouts.First(h => h.SiteKind == HoldoutSiteKind.Facility && !h.Second);

    [Fact]
    public void AJoinerInTheYardIsAboardAtTheFortressAndOneOnceTheGatesOpenIsLobbied()
    {
        var crew = new Crew(front: 300);
        crew.Join();
        Assert.True(crew.State(0).Alive);
        Assert.Equal(RunPhase.Yard, crew.World.Run!.Phase);
        // Out through the gates.
        crew.Run((Line.GateOr(600) + 100 - crew.Host.Train.Dynamics.Distance) / 10, holdSpeed: 10);
        Assert.Equal(RunPhase.Underway, crew.World.Run.Phase);
        var late = crew.Join();
        crew.Run(0.5);
        var s = crew.State(1);
        Assert.False(s.Alive);
        Assert.True(s.Has(PlayerFlags.Lobbied));
        Assert.Equal(DeathCause.None, s.Death);
        // At the back of the queue, with no body; and they're watching (snapshots) at once.
        var entry = crew.World.Holdouts!.Queue.Of(crew.Id(1));
        Assert.Equal(QueueKind.Lobbied, entry!.Kind);
        Assert.DoesNotContain(crew.World.Bodies.All, b => b.Owner == crew.Id(1));
        Assert.True(late.Connected);
        Assert.True(late.Predicted.Has(PlayerFlags.Lobbied));
        // Watching the one living crewmate.
        Assert.Equal(crew.Id(0), crew.World.Dead.FollowedBy(crew.Id(1)));
        Assert.Equal(crew.Id(0), late.World.Dead.FollowedBy(crew.Id(1)));
    }

    [Fact]
    public void InTheYardTheDeadAreBackAtTheFortressSoTheQueueIsEmptyAtTheGates()
    {
        var crew = new Crew(front: 300, players: 2);
        crew.Kill(1, 250);
        crew.Run(0.5);
        Assert.True(crew.State(1).Alive);
        Assert.Equal(0, crew.World.Holdouts!.Queue.Count);
        crew.Run((Line.GateOr(600) + 50 - crew.Host.Train.Dynamics.Distance) / 10, holdSpeed: 10);
        Assert.Equal(RunPhase.Underway, crew.World.Run!.Phase);
        Assert.Equal(0, crew.World.Holdouts.Queue.Count);
    }

    [Fact]
    public void TheRunFailsWhenNoLivingCrewRemainLobbiedOrNot()
    {
        var crew = new Crew(front: Line.GateOr(600) + 150, players: 1);
        crew.Run(1);
        crew.Join();
        crew.Run(0.3);
        Assert.True(crew.State(1).Has(PlayerFlags.Lobbied));
        crew.Kill(0, Line.GateOr(600) + 100);
        crew.Run(0.3);
        Assert.Equal(RunEnd.CrewLost, crew.World.Run!.End);
    }

    [Fact]
    public void OnlyTheDeadAndLobbiedAreSentTheQueue()
    {
        var crew = new Crew(front: Line.GateOr(600) + 150, players: 2);
        crew.Run(1);
        Assert.True(Site.Zone.S0 > crew.Host.Train.Dynamics.Distance + 100);
        crew.Kill(1, Line.GateOr(600) + 100);
        crew.Run(1);
        Assert.Equal(1, crew.World.Holdouts!.Queue.Count);
        // The dead see the queue and their place; the living see nothing.
        Assert.Equal(0, crew.Clients[1].World.Holdouts!.Queue.PositionOf(crew.Id(1)));
        Assert.Equal(0, crew.Clients[0].World.Holdouts!.Queue.Count);
        // Into the zone: the Holdout's lamp is everyone's to see, but not who's in it.
        crew.Run((Site.Zone.S0 + 150 - crew.Host.Train.Dynamics.Distance) / 20, holdSpeed: 20);
        crew.Run(0.5, holdSpeed: 0);
        var h = crew.World.Holdouts.Of(Site.Id)!;
        Assert.Equal(crew.Id(1), h.Assigned);
        Assert.Equal(HoldoutPhase.Occupied, crew.Clients[0].World.Holdouts!.All[h.Index].Phase);
        Assert.Equal(-1, crew.Clients[0].World.Holdouts!.All[h.Index].Assigned);
        Assert.Equal(crew.Id(1), crew.Clients[1].World.Holdouts!.All[h.Index].Assigned);
    }

    [Fact]
    public void TheDeadHearWhatTheOneTheyWatchHearsAndTheDeadChannelNeverReachesTheLiving()
    {
        var crew = new Crew(front: 4000, players: 4);
        crew.Run(1);
        var train = crew.Host.Train;
        // 0 and 1 alive, together on car 2's roof; 2 dead, watching 0; 3 dead too.
        crew.Host.SetPlayerState(crew.Id(0), PlayerMotor.SpawnOnRoof(train, 2, 1, P));
        crew.Host.SetPlayerState(crew.Id(1), PlayerMotor.SpawnOnRoof(train, 2, -1, P));
        crew.Kill(2, 3000);
        crew.Kill(3, 3000);
        crew.Run(1);
        crew.Clients[2].Send(new Request(RequestKind.Follow, crew.Id(0)));
        crew.Run(0.5);
        Assert.Equal(crew.Id(0), crew.World.Dead.FollowedBy(crew.Id(2)));
        foreach (int i in Enumerable.Range(0, 4))
            crew.Heard(i);

        // A living crewmate talks: their mate beside them hears it, and so does the dead player watching that mate.
        crew.Clients[1].SendVoice(1, false, Opus);
        crew.Run(0.2);
        Assert.Contains(crew.Heard(0), f => f.Speaker == crew.Id(1) && f.Path.HasFlag(VoicePath.Proximity));
        Assert.Contains(crew.Heard(2), f => f.Speaker == crew.Id(1) && f.Path.HasFlag(VoicePath.Proximity));

        // The dead talk: the dead hear it on their channel; the living get nothing at all.
        long before = crew.Host.DeadFramesForwarded;
        crew.Clients[3].SendVoice(1, true, Opus);
        crew.Run(0.2);
        Assert.Contains(crew.Heard(2), f => f.Speaker == crew.Id(3) && f.Path == VoicePath.Dead);
        Assert.DoesNotContain(crew.Heard(0), f => f.Speaker == crew.Id(3));
        Assert.DoesNotContain(crew.Heard(1), f => f.Speaker == crew.Id(3));
        Assert.True(crew.Host.DeadFramesForwarded > before);
        Assert.Equal(0, crew.Host.DeadFramesToTheLiving);
        // Nothing the dead say goes where the Soot Children listen.
        Assert.Null(crew.World.Voices.LastSpoke(crew.Id(3)));
    }

    [Fact]
    public void TheCameraSwitchesToTheNextLivingPlayerWhenItsTargetDiesAndCantBeTurnedOnTheDead()
    {
        var crew = new Crew(front: 4000, players: 3);
        crew.Run(1);
        crew.Kill(2, 3000);
        crew.Run(0.5);
        crew.Clients[2].Send(new Request(RequestKind.Follow, crew.Id(0)));
        crew.Run(0.3);
        Assert.Equal(crew.Id(0), crew.World.Dead.FollowedBy(crew.Id(2)));
        // No watching the dead, nor oneself.
        crew.Clients[2].Send(new Request(RequestKind.Follow, crew.Id(2)));
        crew.Run(0.3);
        Assert.Equal(crew.Id(0), crew.World.Dead.FollowedBy(crew.Id(2)));
        Assert.Contains(crew.Host.Requests, r => r.Player == crew.Id(2) && r.Request.Kind == RequestKind.Follow && !r.Allowed);
        // 0 dies: to the next living player.
        crew.Kill(0, 3000);
        crew.Run(0.3);
        Assert.Equal(crew.Id(1), crew.World.Dead.FollowedBy(crew.Id(2)));
        Assert.Equal(crew.Id(1), crew.Clients[2].World.Dead.FollowedBy(crew.Id(2)));
    }

    /// <summary>Two dead players and one alive at the Holdout's door, in its zone.</summary>
    static Crew AtTheHoldout(out Holdout h)
    {
        var crew = new Crew(front: Site.Zone.S0 + 150, players: 3);
        crew.Run(0.5);
        crew.Kill(1, 200);
        crew.Kill(2, 200);
        crew.Run(0.5, holdSpeed: 0);
        h = crew.World.Holdouts!.Of(Site.Id)!;
        Assert.Equal(crew.Id(1), h.Assigned);
        var door = h.Door + (h.Door - h.Centre).Normalized * 1.5;
        crew.Host.SetPlayerState(crew.Id(0), PlayerMotor.SpawnOnGround(door, crew.Host.Train.Line, crew.Host.Train.Dynamics.Distance, P));
        crew.Run(0.3, holdSpeed: 0);
        return crew;
    }

    [Fact]
    public void TheLiveMicIsTheAssignedPlayersOnlyAndIsHeardAtTheDoorAsProximityVoice()
    {
        var crew = AtTheHoldout(out var h);
        // Only the one inside may open it.
        crew.Clients[2].Send(new Request(RequestKind.LiveMic, 1));
        crew.Run(0.2, holdSpeed: 0);
        Assert.False(h.LiveMic);
        crew.Clients[1].Send(new Request(RequestKind.LiveMic, 1));
        crew.Run(0.2, holdSpeed: 0);
        Assert.True(h.LiveMic);
        foreach (int i in Enumerable.Range(0, 3))
            crew.Heard(i);
        crew.Clients[1].SendVoice(1, false, Opus);
        crew.Run(0.2, holdSpeed: 0);
        // The rescuer at the door hears it from the Holdout; still not on the dead channel.
        var heard = crew.Heard(0).Where(f => f.Speaker == crew.Id(1)).ToList();
        Assert.NotEmpty(heard);
        Assert.All(heard, f => Assert.Equal(VoicePath.Proximity | VoicePath.Holdout, f.Path & ~VoicePath.Occluded));
        Assert.All(heard, f => Assert.Equal(h.Index, f.Source));
        // The other dead still hear them on the dead channel.
        Assert.Contains(crew.Heard(2), f => f.Speaker == crew.Id(1) && f.Path == VoicePath.Dead);
        Assert.Equal(0, crew.Host.DeadFramesToTheLiving);
        // Past 26 m, nothing.
        var off = h.Door + (h.Door - h.Centre).Normalized * 40;
        crew.Host.SetPlayerState(crew.Id(0), PlayerMotor.SpawnOnGround(off, crew.Host.Train.Line, crew.Host.Train.Dynamics.Distance, P));
        crew.Run(0.3, holdSpeed: 0);
        crew.Heard(0);
        crew.Clients[1].SendVoice(2, false, Opus);
        crew.Run(0.2, holdSpeed: 0);
        Assert.DoesNotContain(crew.Heard(0), f => f.Speaker == crew.Id(1));
        // Nothing the Live Mic carries is the kind of talking the Soot Children (or anything listening) keep.
        Assert.Null(crew.World.Voices.LastSpoke(crew.Id(1)));
    }

    [Fact]
    public void CallOutIsAnyDeadPlayersWithTheCrewNearAndOneCooldownForTheHoldout()
    {
        var crew = AtTheHoldout(out var h);
        // The living can't.
        crew.Clients[0].Send(new Request(RequestKind.CallOut, h.Index));
        crew.Run(0.1, holdSpeed: 0);
        Assert.Equal(0, h.CallOuts);
        // Either of the dead can, and it's the Holdout's cooldown that holds the other back.
        crew.Clients[2].Send(new Request(RequestKind.CallOut, h.Index));
        crew.Run(0.1, holdSpeed: 0);
        Assert.Equal(1, h.CallOuts);
        crew.Clients[1].Send(new Request(RequestKind.CallOut, h.Index));
        crew.Run(0.1, holdSpeed: 0);
        Assert.Equal(1, h.CallOuts);
        crew.Run(Tuning.Holdouts.CallOut.CooldownSeconds, holdSpeed: 0);
        crew.Clients[1].Send(new Request(RequestKind.CallOut, h.Index));
        crew.Run(0.1, holdSpeed: 0);
        Assert.Equal(2, h.CallOuts);
        // Clients learn of it (the sound plays from the Holdout).
        Assert.Equal(2, crew.Clients[0].World.Holdouts!.All[h.Index].CallOuts);
        // With no living crew within 200 m, it isn't available.
        var far = h.Centre + (h.Door - h.Centre).Normalized * (Tuning.Holdouts.CallOut.ActiveRadiusM + 50);
        crew.Host.SetPlayerState(crew.Id(0), PlayerMotor.SpawnOnGround(far, crew.Host.Train.Line, crew.Host.Train.Dynamics.Distance, P));
        crew.Run(Tuning.Holdouts.CallOut.CooldownSeconds + 0.5, holdSpeed: 0);
        crew.Clients[2].Send(new Request(RequestKind.CallOut, h.Index));
        crew.Run(0.1, holdSpeed: 0);
        Assert.Equal(2, h.CallOuts);
    }
}
