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

    /// <summary>Puts the player at the driver's stand, by the brake valve.</summary>
    static void AtTheStand(Cab c)
    {
        var levers = c.Train.Frames[0].Shape.Levers!.Value;
        c.Player.Position = new Double3(levers.Brake.X, c.Player.Position.Y, levers.Brake.Z + 0.4);
        Assert.True(PlayerMotor.InCab(c.Player, c.Train));
    }

    [Fact]
    public void APlayerAtTheDriversStandHasTheControlsAndTheBotGoesOutToWork()
    {
        var c = new Cab();
        c.Run(60);
        Assert.False(c.Driver.HandedOver);
        Assert.True(c.Train.Dynamics.Speed > 5, $"the bot never drove off: {c.Train.Dynamics.Speed:0.0} m/s");
        AtTheStand(c);
        c.Run(3);
        Assert.True(c.Driver.HandedOver, "the bot never saw a player had taken the stand");
        Assert.False(c.Driver.Driving);
        // The player brakes and lets off: nothing from the bot on the controls meanwhile, and it's out of the cab.
        bool touched = false;
        for (int s = 0; s < 60 * SimConstants.TickRate; s++)
        {
            if (s % 90 == 0)
                c.PlayerHands = new PlayerIntent { Buttons = PlayerButtons.Brake };
            c.Run(1.0 / SimConstants.TickRate);
            touched |= c.BotIntent.ThrottleNotch != 0 || c.BotIntent.Has(PlayerButtons.Brake) || c.BotIntent.Has(PlayerButtons.Reverser);
        }
        Assert.False(touched, "the bot worked the controls under the player");
        Assert.False(PlayerMotor.InCab(c.Bot, c.Train), $"still in the cab: {c.Bot.Surface} on {c.Bot.Parent}");
        Assert.True(c.Bot.Alive);
    }

    [Fact]
    public void SomeoneWarmingUpInTheCabIsntDriving()
    {
        // In the cab, but not at the stand (by the fire, warming): the bot drives on.
        var c = new Cab();
        c.Run(120);
        Assert.False(c.Driver.HandedOver);
        Assert.True(c.Train.Dynamics.Speed > 5);
    }

    [Fact]
    public void WithNobodyAtTheStandAWhileTheBotComesBackAndDrives()
    {
        var c = new Cab();
        c.Run(60);
        AtTheStand(c);
        c.Run(20);
        Assert.True(c.Driver.HandedOver);
        Assert.False(PlayerMotor.InCab(c.Bot, c.Train));
        c.PlayerAboard = false;
        c.Run(25);
        Assert.True(c.Driver.HandedOver, "back too soon");
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
