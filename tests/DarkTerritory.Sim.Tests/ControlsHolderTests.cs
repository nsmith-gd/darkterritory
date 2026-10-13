using Ballast.Net;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 574 (the director, 9 Oct 2026, on the short-nights test build: "yes the bot was still fighting me for the
/// controls"): the cab's controls are whoever's working them. Someone playing who works them takes them off a bot, and
/// while they hold them the bot's hand on them doesn't count; everyone's told who holds them.
/// </summary>
public class ControlsHolderTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly RailLine Line = new(new LineDefinition("t", [new TrackSegment(10_000)]));

    static TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(T, 4, 1)), Line, 2_000);

    static (LoopbackNetwork Net, HostSession Host, ClientSession Bot, ClientSession Person) Cab()
    {
        var net = new LoopbackNetwork();
        // Both of them in the cab (the first aboard is anyway; the second put there).
        var host = new HostSession(net.CreateHost(), Train(), T, P) { BoardAt = _ => PlayerMotor.SpawnInCab(Train(), P, 0.5) };
        var bot = new ClientSession(net.CreateClient(), Train(), T, P) { Bot = true, Name = "Dunmore" };
        var person = new ClientSession(net.CreateClient(), Train(), T, P) { Name = "nat" };
        return (net, host, bot, person);
    }

    static void Run(LoopbackNetwork net, HostSession host, ClientSession bot, ClientSession person, int ticks, Func<int, PlayerIntent> botHands, Func<int, PlayerIntent> theirs)
    {
        for (int t = 0; t < ticks; t++)
        {
            net.Advance(SimConstants.TickSeconds);
            host.Step();
            bot.Step(botHands(t));
            person.Step(theirs(t));
        }
    }

    [Fact]
    public void SomeonePlayingWhoWorksTheControlsHasThemOffTheBot()
    {
        var (net, host, bot, person) = Cab();
        var braking = new PlayerIntent { Buttons = PlayerButtons.Brake };
        // Joined and aboard, the bot holding the brake: it holds the controls.
        Run(net, host, bot, person, 60, _ => braking, _ => default);
        Assert.True(PlayerMotor.InCab(host.Players.Single(p => p.Id == person.PlayerId).State, host.Train));
        Assert.Equal(bot.PlayerId, (byte)host.World.ControlsHolder);
        Assert.True(host.World.ControlsHolderBot);
        Assert.Equal(1, host.Controls.Brake);
        // They let the brake off (a notch up): the controls are theirs, and the bot's brake, held every tick, no longer counts.
        Run(net, host, bot, person, 1, _ => braking, _ => new PlayerIntent { ThrottleNotch = 1 });
        Run(net, host, bot, person, 30, _ => braking, _ => default);
        Assert.Equal(person.PlayerId, (byte)host.World.ControlsHolder);
        Assert.False(host.World.ControlsHolderBot);
        Assert.Equal(0, host.Controls.Brake);
        // Everyone's told: the bot sees someone playing has them (and hands over, ConductorBot.HandedOver).
        Assert.Equal(person.PlayerId, (byte)bot.World.ControlsHolder);
        Assert.False(bot.World.ControlsHolderBot);
    }

    [Fact]
    public void ABotNeverTakesTheControlsOffSomeonePlaying()
    {
        var (net, host, bot, person) = Cab();
        Run(net, host, bot, person, 60, _ => default, t => t == 30 ? new PlayerIntent { Buttons = PlayerButtons.Brake } : default);
        Assert.Equal(person.PlayerId, (byte)host.World.ControlsHolder);
        // The bot notches and brakes all it likes: still theirs.
        Run(net, host, bot, person, 60, t => new PlayerIntent { ThrottleNotch = 1, Buttons = PlayerButtons.Brake }, _ => default);
        Assert.Equal(person.PlayerId, (byte)host.World.ControlsHolder);
        Assert.Equal(0, host.Controls.Throttle);
    }
}
