using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The director, 9 Oct 2026: "if a player goes to the cab to drive the bot in there should find another task to do or
/// position to take and shouldnt interfere with the player driving." Someone playing who works the controls has them: the
/// bot driver keeps the fire from the fireman's side and leaves the regulator, brake and reverser alone, and takes them back
/// once nobody playing has been in the cab a while.
/// </summary>
public class DriverHandsOverTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly TrainTuning T = Tuning.Train;

    sealed class Cab
    {
        public readonly World World;
        public readonly ConductorBot Driver = new(new CrewCalls(), 0);
        public PlayerState Bot, Player;
        /// <summary>What the player does with the controls this tick (their notch, their brake).</summary>
        public PlayerIntent PlayerHands;
        public bool PlayerAboard = true;
        TrainControls _controls = new() { Reverser = 1 };
        uint _tick;

        public Cab()
        {
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(30_000)])), 2_000, Tuning.Boiler);
            World = new World(train);
            World.EnableBodies();
            Bot = PlayerMotor.SpawnInCab(train, P);
            Player = PlayerMotor.SpawnInCab(train, P, 0.5);
        }

        public TrainOnLine Train => World.Train;
        public TrainControls Controls => World.Controls;

        /// <summary>A bot intent with nothing on the controls.</summary>
        public PlayerIntent BotIntent;

        public void Run(double seconds)
        {
            for (int t = 0; t < seconds * SimConstants.TickRate; t++)
            {
                World.BeginTick();
                if (CabControls.Clears(_controls, Train, CabControls.ReleasesBrake(PlayerHands, Player, Train)))
                    _controls.Brake = 0;
                Driver.Players = PlayerAboard ? [Player] : [];
                Driver.Crewmates = PlayerAboard ? [Player] : [];
                BotIntent = Driver.Decide(Bot, World, _tick, out _);
                if (CabControls.ReleasesBrake(BotIntent, Bot, Train))
                    _controls.Brake = 0;
                CabControls.Apply(ref _controls, BotIntent, Bot, Train);
                if (PlayerAboard)
                    CabControls.Apply(ref _controls, PlayerHands, Player, Train);
                var b = Bot;
                World.CrewAct(ref b, BotIntent, 1);
                World.Step(_controls);
                PlayerMotor.Step(ref b, BotIntent, Train, P, T, SimConstants.TickSeconds, applyLook: false);
                Bot = b;
                PlayerHands = default;
                _tick++;
            }
        }

        public void PlayerNotches(sbyte notch)
        {
            PlayerHands = new PlayerIntent { ThrottleNotch = notch };
            Run(1.0 / SimConstants.TickRate);
        }
    }

    /// <summary>The host's word that the player holds the controls (as a snapshot brings it, note 574).</summary>
    static void TheyHoldThem(Cab c) => c.World.HoldControls(7, bot: false);

    [Fact]
    public void SomeonePlayingWithTheControlsHasThemAndTheBotGoesOutToWork()
    {
        var c = new Cab();
        c.Run(60);
        Assert.False(c.Driver.HandedOver);
        Assert.True(c.Train.Dynamics.Speed > 5, $"the bot never drove off: {c.Train.Dynamics.Speed:0.0} m/s");
        TheyHoldThem(c);
        c.Run(1);
        Assert.True(c.Driver.HandedOver);
        Assert.False(c.Driver.Driving);
        bool touched = false;
        for (int s = 0; s < 60 * SimConstants.TickRate; s++)
        {
            c.Run(1.0 / SimConstants.TickRate);
            touched |= c.BotIntent.ThrottleNotch != 0 || c.BotIntent.Has(PlayerButtons.Brake) || c.BotIntent.Has(PlayerButtons.Reverser);
        }
        Assert.False(touched, "the bot worked the controls under the player");
        Assert.False(PlayerMotor.InCab(c.Bot, c.Train), $"still in the cab: {c.Bot.Surface} on {c.Bot.Parent}");
        Assert.True(c.Bot.Alive);
    }

    [Fact]
    public void SomeoneInTheCabWhoDoesntTouchTheControlsIsntDriving()
    {
        // In the cab (warming up, say), but nobody's taken the controls: the bot drives on.
        var c = new Cab();
        c.Run(120);
        Assert.False(c.Driver.HandedOver);
        Assert.True(c.Train.Dynamics.Speed > 5);
    }

    [Fact]
    public void AnotherBotWithTheControlsIsNoHandover()
    {
        var c = new Cab();
        c.Run(30);
        c.World.HoldControls(7, bot: true);
        c.Run(5);
        Assert.False(c.Driver.HandedOver);
    }

    [Fact]
    public void TheMomentNobodyHasTheControlsTheBotComesBackAndDrives()
    {
        // Note 16 of the director's notes: they jumped off at speed, and no bot went back to the cab.
        var c = new Cab();
        c.Run(60);
        TheyHoldThem(c);
        c.Run(20);
        Assert.False(PlayerMotor.InCab(c.Bot, c.Train));
        c.World.HoldControls(-1, false);
        bool back = false;
        for (int s = 0; s < 120 && !back; s++)
        {
            c.Run(1);
            back = !c.Driver.HandedOver && PlayerMotor.InCab(c.Bot, c.Train);
        }
        Assert.True(back, $"never back in the cab: {c.Bot.Surface} on {c.Bot.Parent}");
        Assert.True(c.Driver.Driving);
        c.Run(60);
        Assert.True(c.Train.Dynamics.Speed > 5, $"not driving again: {c.Train.Dynamics.Speed:0.0} m/s");
    }
}
