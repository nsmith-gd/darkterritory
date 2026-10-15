using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 740 (the director's second playtest, 14 Oct: "When I pull the horn, the bots should understand that they need to
/// get back on the train"; "We need some way to signal to the driver that everybody is on the train"; GDD §17): the horn
/// pulled at a stand by someone playing calls the bots aboard, and each calls "Aboard!" once it's on.
/// </summary>
public class HornCallsAboardTests
{
    static readonly PlayerTuning P = Tuning.Player;

    sealed class Crew
    {
        public readonly HostSession Host;
        public readonly List<(ClientSession Session, IBot Bot)> Bots = [];
        public readonly ClientSession Player;
        public readonly CrewCalls Calls = new();
        readonly Ballast.Net.LoopbackNetwork _net = new();
        uint _t;

        public Crew(int bots)
        {
            Host = new HostSession(_net.CreateHost(), Train(), Tuning.Train, P, Tuning.Combat);
            for (int i = 0; i < bots; i++)
                Bots.Add((new ClientSession(_net.CreateClient(), Train(), Tuning.Train, P, Tuning.Combat) { Bot = true }, BotCrew.Make(i, bots, Calls, Tuning.Combat, P, 1)));
            Player = new ClientSession(_net.CreateClient(), Train(), Tuning.Train, P, Tuning.Combat) { Name = "Priya" };
        }

        static TrainOnLine Train() =>
            new(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(60_000)])), 2_000, Tuning.Boiler);

        public byte PlayerId => Player.PlayerId!.Value;
        public byte Id(int bot) => Bots[bot].Session.PlayerId!.Value;
        public PlayerState Of(byte id) => Host.Players.First(p => p.Id == id).State;

        public void Run(double seconds, Func<PlayerIntent>? hands = null, Func<bool>? until = null, Action? watch = null)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                _net.Advance(SimConstants.TickSeconds);
                Host.Step();
                Calls.Advance(_t);
                foreach (var (session, bot) in Bots)
                    session.Step(session.Connected ? BotCrew.Think(session, bot, _t, Calls) : default);
                Player.Step(hands?.Invoke() ?? default);
                _t++;
                watch?.Invoke();
                if (until?.Invoke() == true)
                    return;
            }
        }
    }

    [Fact]
    public void TheHornAtAStandCallsTheBotsAboardAndEachCallsBack()
    {
        var c = new Crew(bots: 2);
        c.Run(2);
        // Someone playing at the controls, the train standing on its brake; the walker bot down on the ballast beside car 2.
        c.Host.SetPlayerState(c.PlayerId, PlayerMotor.SpawnInCab(c.Host.Train, P, 0.5));
        c.Run(5, () => new PlayerIntent { Buttons = PlayerButtons.Brake });
        Assert.True(c.Host.Train.Dynamics.Speed < CabControls.StandingBelow);
        Assert.False(c.Calls.CalledAboard);
        // The Whistler's whistle (no hand on the cord) calls nobody.
        c.Host.World.Whistled(1.0);
        c.Run(1);
        Assert.False(c.Calls.CalledAboard);
        // The horn, pulled by the one playing.
        var aboard = new HashSet<int>();
        c.Run(1, () => new PlayerIntent { Actions = PlayerActions.Whistle, Buttons = PlayerButtons.Brake }, watch: () =>
        {
            foreach (var e in c.Host.World.Emotes.Where(e => e.Kind == Emote.Aboard))
                aboard.Add(e.By);
        });
        Assert.True(c.Calls.CalledAboard, "the horn didn't call the crew aboard");
        Assert.True(c.Calls.Leaving);
        c.Run(60, () => new PlayerIntent { Buttons = PlayerButtons.Brake }, until: () => aboard.Count == c.Bots.Count, watch: () =>
        {
            foreach (var e in c.Host.World.Emotes.Where(e => e.Kind == Emote.Aboard))
                aboard.Add(e.By);
        });
        Assert.Equal(c.Bots.Select((_, i) => (int)c.Id(i)).Order(), aboard.Order());
        Assert.All(c.Bots.Select((_, i) => c.Of(c.Id(i))), s => Assert.NotEqual(PlayerState.World, s.Parent));
        // Off the brake and away: the call's over.
        c.Run(30, () => new PlayerIntent { ThrottleNotch = 1 }, until: () => !c.Calls.CalledAboard);
        Assert.False(c.Calls.CalledAboard);
        Assert.False(c.Calls.Leaving);
    }

    [Fact]
    public void ABotOnTheGroundComesAboardToTheHorn()
    {
        var c = new Crew(bots: 2);
        c.Run(2);
        c.Host.SetPlayerState(c.PlayerId, PlayerMotor.SpawnInCab(c.Host.Train, P, 0.5));
        c.Run(5, () => new PlayerIntent { Buttons = PlayerButtons.Brake });
        // The walker down on the ballast, 12 m off the side of car 2.
        var train = c.Host.Train;
        var beside = train.Frames[2].ToWorld(new Double3(train.Frames[2].Shape.HalfWidth + 12, 0, 0));
        byte walker = c.Id(1);
        c.Host.SetPlayerState(walker, c.Of(walker) with { Parent = PlayerState.World, Position = beside, Velocity = default, Surface = Surface.Ground, LineHint = train.Cars[2].FrontDistance });
        c.Run(1, () => new PlayerIntent { Actions = PlayerActions.Whistle, Buttons = PlayerButtons.Brake });
        c.Run(90, () => new PlayerIntent { Buttons = PlayerButtons.Brake }, until: () => c.Of(walker).Parent != PlayerState.World);
        Assert.NotEqual(PlayerState.World, c.Of(walker).Parent);
    }
}
