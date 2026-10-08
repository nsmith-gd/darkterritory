using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The crew cap (ARCHITECTURE §8 note 254; GDD §1 "Players: 2–8"): the host turns a new joiner away once the crew, the
/// waiting and the places held for the dropped come to player.json crew.cap, and says so; a held place's token still gets in.
/// </summary>
public class CrewCapTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly RailLine Line = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));

    static TrainOnLine NewTrain() => new(new TrainDynamics(Consist.Uniform(T, 6, 1)), Line, 1500);

    static PlayerTuning Capped(int cap, double reserveSeconds = 180) =>
        P with { Crew = P.Crew with { Cap = cap }, Rejoin = P.Rejoin with { ReserveSeconds = reserveSeconds } };

    sealed class Night
    {
        public readonly LoopbackNetwork Net = new();
        public readonly HostSession Host;
        public readonly List<ClientSession> Clients = [];
        public readonly List<ITransport> Links = [];
        readonly PlayerTuning _tuning;

        Night(PlayerTuning tuning)
        {
            _tuning = tuning;
            Host = new HostSession(Net.CreateHost(), NewTrain(), T, tuning);
            Host.World.EnableBodies();
        }

        public static Night With(int clients, PlayerTuning tuning)
        {
            var night = new Night(tuning);
            for (int i = 0; i < clients; i++)
                night.Add();
            return night;
        }

        public ClientSession Add()
        {
            var link = Net.CreateClient();
            Links.Add(link);
            var client = new ClientSession(link, NewTrain(), T, _tuning) { Name = $"crew{Clients.Count}" };
            Clients.Add(client);
            return client;
        }

        public void Run(int ticks)
        {
            for (int t = 0; t < ticks; t++)
            {
                Net.Advance(SimConstants.TickSeconds);
                Host.Step();
                foreach (var c in Clients)
                    c.Step(default);
            }
        }

        public void Drop(int i) => Links[i].Dispose();

        public void Redial(int i)
        {
            Links[i] = Net.CreateClient();
            Clients[i].Reconnect(Links[i]);
        }
    }

    [Fact]
    public void TheWireCarriesTheRefusal()
    {
        var w = new NetWriter();
        Messages.WriteRefused(w, new Refusal(RefusalReason.CrewFull, 8, 8));
        var r = new NetReader(w.Written);
        Assert.Equal((byte)MessageType.Refused, r.U8());
        var refusal = Messages.ReadRefused(ref r);
        Assert.Equal(new Refusal(RefusalReason.CrewFull, 8, 8), refusal);
        Assert.Equal("CREW FULL (8/8)", refusal.ToString());
        Messages.WriteLeave(w);
        Assert.Equal([(byte)MessageType.Leave], w.Written.ToArray());
        Assert.Equal(38, Protocol.Version);
        // The GDD's largest crew is the content's cap.
        Assert.Equal(8, P.Crew.Cap);
    }

    [Fact]
    public void ANinthJoinerIsTurnedAwayWithTheReason()
    {
        var night = Night.With(9, P);
        night.Run(30);
        Assert.Equal(8, night.Host.PlayerCount);
        Assert.True(night.Host.Full);
        Assert.Equal(1, night.Host.Refusals);
        // The first eight are aboard; the ninth (the last to connect) was told why, and never welcomed.
        Assert.All(night.Clients.Take(8), c => Assert.True(c.Connected));
        var ninth = night.Clients[8];
        Assert.Null(ninth.PlayerId);
        Assert.Equal(new Refusal(RefusalReason.CrewFull, 8, 8), ninth.Refused);
        Assert.Equal("CREW FULL (8/8)", ninth.Refused.ToString());
        foreach (var c in night.Clients.Take(8))
            Assert.Equal(7, c.RemoteIds.Count());
        Assert.False(ninth.Dropped);
    }

    [Fact]
    public void AHeldPlaceCountsTowardAFullCrewAndItsTokenStillGetsIn()
    {
        var night = Night.With(3, Capped(3));
        night.Run(20);
        Assert.True(night.Host.Full);
        byte id = night.Clients[1].PlayerId!.Value;

        night.Drop(1);
        night.Run(10);
        // Two aboard and one place held: still full (note 253's place is someone's).
        Assert.Equal(2, night.Host.PlayerCount);
        Assert.True(night.Host.IsReserved(id));
        Assert.Equal(3, night.Host.Occupied);
        Assert.True(night.Host.Full);
        var stranger = night.Add();
        night.Run(10);
        Assert.Equal("CREW FULL (3/3)", stranger.Refused?.ToString());
        Assert.Equal(2, night.Host.PlayerCount);

        // The one it's held for comes back at a full crew, with its token: in, as itself.
        night.Redial(1);
        night.Run(20);
        Assert.True(night.Clients[1].Connected);
        Assert.Equal(id, night.Clients[1].PlayerId);
        Assert.Null(night.Clients[1].Refused);
        Assert.Equal(3, night.Host.PlayerCount);
        Assert.Equal(1, night.Host.Refusals);
    }

    [Fact]
    public void ThePlaceOpensWhenTheReserveRunsOut()
    {
        var night = Night.With(2, Capped(2, reserveSeconds: 1));
        night.Run(20);
        Assert.True(night.Host.Full);
        night.Drop(1);
        night.Run(10);
        Assert.True(night.Host.Full);

        night.Run(SimConstants.TickRate);
        Assert.Equal(1, night.Host.ReservesExpired);
        Assert.False(night.Host.Full);
        var newcomer = night.Add();
        night.Run(20);
        Assert.True(newcomer.Connected);
        Assert.Null(newcomer.Refused);
        Assert.Equal(2, night.Host.PlayerCount);

        // Back too late: the place ran out, and someone has it. Turned away like any stranger, with the reason.
        night.Redial(1);
        night.Run(20);
        Assert.False(night.Clients[1].Connected);
        Assert.Equal("CREW FULL (2/2)", night.Clients[1].Refused?.ToString());
    }

    [Fact]
    public void APlayerWhoLeavesGivesTheirPlaceUpAtOnce()
    {
        var night = Night.With(3, Capped(3));
        night.Run(20);
        Assert.True(night.Host.Full);
        night.Clients[2].Leave();
        night.Run(5);
        // Not held: a quit isn't a drop. The body stays (spec E), the place is free now.
        Assert.Equal(0, night.Host.Reserved);
        Assert.Equal(1, night.Host.Leaves);
        Assert.Equal(2, night.Host.Occupied);
        Assert.False(night.Host.Full);
        Assert.True(night.Clients[2].Left);
        var newcomer = night.Add();
        night.Run(20);
        Assert.True(newcomer.Connected);
    }

    [Fact]
    public void TheRefusedAreHungUpOnAfterTheirRefusalHasHadTimeToArrive()
    {
        var night = Night.With(2, Capped(1));
        night.Run(5);
        Assert.NotNull(night.Clients[1].Refused);
        Assert.Equal(1, night.Host.PlayerCount);
        Assert.Equal(1, night.Host.TurnedAway);
        // The host keeps the refused link open crew.refuseLingerSeconds, then hangs up. Never welcomed, it isn't "dropped".
        night.Run((int)Math.Ceiling(P.Crew.RefuseLingerSeconds * SimConstants.TickRate) + 2);
        Assert.Equal(0, night.Host.TurnedAway);
        Assert.Null(night.Clients[1].PlayerId);
        Assert.False(night.Clients[1].Dropped);
        Assert.Equal(1, night.Host.PlayerCount);
    }

    [Fact]
    public void ASilentConnectionTakesNoPlace()
    {
        // A connection that never says hello (an abandoned redial) is no one: it doesn't count toward the cap, so it can't
        // fill the crew or shut the lobby, and a real joiner after it still gets the last place.
        var night = Night.With(1, Capped(2));
        night.Run(10);
        using var silent = night.Net.CreateClient();
        night.Run((int)Math.Ceiling(P.Rejoin.GreetSeconds * SimConstants.TickRate) + 10);
        Assert.Equal(1, night.Host.Occupied);
        Assert.False(night.Host.Full);
        var joiner = night.Add();
        night.Run(20);
        Assert.True(joiner.Connected);
        Assert.Null(joiner.Refused);
        Assert.Equal(2, night.Host.PlayerCount);
    }

    [Fact]
    public void AModsHigherCapTakesALargerCrew()
    {
        // "2–8+": a mod patches player.json crew.cap; everyone on the night runs the same content, so they agree on it.
        var night = Night.With(10, Capped(10));
        night.Run(30);
        Assert.Equal(10, night.Host.PlayerCount);
        Assert.Equal(0, night.Host.Refusals);
        Assert.All(night.Clients, c => Assert.True(c.Connected));
    }

    [Fact]
    public void MoreBotsThanTheCapAreTurnedAwayAtTheDoor()
    {
        // dt harness --bots 9: the crew is eight, the parts a crew of eight's; the ninth is told CREW FULL (8/8).
        var r = Harness.Run(Line, T, P, new HarnessOptions { Bots = 9, Seconds = 4, Link = LinkConditions.Perfect });
        var crew = Assert.IsType<CrewCapReport>(r.Crew);
        Assert.Equal(8, crew.Cap);
        Assert.Equal(8, crew.Occupied);
        Assert.Equal(1, crew.Refusals);
        Assert.EndsWith(": CREW FULL (8/8)", Assert.Single(crew.Refused));
        Assert.Equal(8, r.Clients.Count);
        Assert.All(r.Clients, c => Assert.NotEqual(0, c.Id));
    }
}
