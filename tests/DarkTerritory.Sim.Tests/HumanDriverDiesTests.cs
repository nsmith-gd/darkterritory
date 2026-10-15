using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 741 (the director's second playtest, 14 Oct: "I was driving the train and then I died. And the bots did not take
/// over driving the train. They just stood there"): someone playing drives, the bots out at their work (the driver bot
/// handed over, note 574), and they die at the controls. A bot comes to the cab and drives on.
/// </summary>
public class HumanDriverDiesTests
{
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>A host, its bots (<see cref="BotCrew.Make"/>'s crew, thinking as the app's do) and someone playing, over a loopback.</summary>
    sealed class Crew
    {
        public readonly HostSession Host;
        public readonly List<(ClientSession Session, IBot Bot)> Bots = [];
        public readonly ClientSession Player;
        readonly Ballast.Net.LoopbackNetwork _net = new();
        readonly CrewCalls _calls = new();
        uint _t;

        public Crew(int bots)
        {
            Host = new HostSession(_net.CreateHost(), Train(), Tuning.Train, P, Tuning.Combat);
            for (int i = 0; i < bots; i++)
                Bots.Add((new ClientSession(_net.CreateClient(), Train(), Tuning.Train, P, Tuning.Combat) { Bot = true }, BotCrew.Make(i, bots, _calls, Tuning.Combat, P, 1)));
            Player = new ClientSession(_net.CreateClient(), Train(), Tuning.Train, P, Tuning.Combat) { Name = "Priya" };
        }

        static TrainOnLine Train() =>
            new(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(60_000)])), 2_000, Tuning.Boiler);

        public byte PlayerId => Player.PlayerId!.Value;
        public PlayerState Of(byte id) => Host.Players.First(p => p.Id == id).State;
        public IEnumerable<PlayerState> BotStates => Bots.Select(b => Of(b.Session.PlayerId!.Value));

        /// <summary>Runs on, the one playing sending <paramref name="hands"/> each tick.</summary>
        public void Run(double seconds, Func<PlayerIntent>? hands = null, Func<bool>? until = null)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                _net.Advance(SimConstants.TickSeconds);
                Host.Step();
                _calls.Advance(_t);
                foreach (var (session, bot) in Bots)
                    session.Step(session.Connected ? BotCrew.Think(session, bot, _t, _calls) : default);
                Player.Step(hands?.Invoke() ?? default);
                _t++;
                if (until?.Invoke() == true)
                    return;
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheDriverPlayingDiesAndABotTakesTheCabAndDrivesOn(bool driverBotDeadFirst)
    {
        var c = new Crew(bots: 2);
        c.Run(2);
        Assert.All(c.Bots, b => Assert.NotNull(b.Session.PlayerId));
        // The one playing goes into the cab and takes the controls: the brake off, a notch up.
        c.Host.SetPlayerState(c.PlayerId, PlayerMotor.SpawnInCab(c.Host.Train, P, 0.5));
        int tick = 0;
        c.Run(90, () => ++tick % 20 == 0 ? new PlayerIntent { ThrottleNotch = 1 } : default);
        Assert.True(c.PlayerId == c.Host.World.ControlsHolder, $"they never took the controls: the holder {c.Host.World.ControlsHolder}, they are {c.PlayerId}, {c.Of(c.PlayerId).Surface} on {c.Of(c.PlayerId).Parent}");
        Assert.True(c.Host.Train.Dynamics.Speed > 3, $"they never drove off: {c.Host.Train.Dynamics.Speed:0.0} m/s");
        // The driver bot (out at work since they took the controls) died before them: only the gunner's left.
        if (driverBotDeadFirst)
        {
            byte driverBot = c.Bots[0].Session.PlayerId!.Value;
            c.Host.SetPlayerState(driverBot, c.Of(driverBot) with { Health = 0, Death = DeathCause.Mauled });
            c.Run(20, () => ++tick % 20 == 0 ? new PlayerIntent { ThrottleNotch = 1 } : default);
        }
        // They die at the controls.
        c.Host.SetPlayerState(c.PlayerId, c.Of(c.PlayerId) with { Health = 0, Death = DeathCause.Mauled });
        bool someoneIn = false;
        c.Run(180, until: () => someoneIn = c.BotStates.Any(s => s.Alive && PlayerMotor.InCab(s, c.Host.Train)) && c.Host.World.ControlsHolderBot);
        Assert.True(someoneIn, $"no bot took the cab: {string.Join(", ", c.BotStates.Select(s => $"{s.Surface} on {s.Parent}"))}; holder {c.Host.World.ControlsHolder}");
        c.Run(90);
        Assert.True(c.Host.Train.Dynamics.Speed > 3, $"not driven on: {c.Host.Train.Dynamics.Speed:0.0} m/s");
    }
}
