using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T96 (playtest: "if I get off the train, it never stops for me to get back on"): the driver bot stops for a crewmate
/// left on the ground behind the train, sets back to them at yard speed, waits while they climb on, and goes on again.
/// </summary>
public class LeftBehindTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly RailLine Line = new(new LineDefinition("t", [new TrackSegment(80_000)]));

    [Fact]
    public void TheDriverStopsSetsBackForACrewmateLeftBehindAndGoesOnOnceTheyreAboard()
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), Line, 5_000, Tuning.Boiler);
        train.Dynamics.Velocity = 12;
        var world = new World(train, Tuning.Combat);
        var bot = new ConductorBot(new CrewCalls(), 0);
        var driver = PlayerMotor.SpawnInCab(train, P);
        double at = train.Dynamics.RearDistance - 250;
        var them = PlayerMotor.SpawnOnGround(Line.Sample(RailLine.MainPath, at).Position + new Ballast.Double3(3, 0, 0), Line, at, P);
        var c = new TrainControls { Reverser = 1 };
        double slowest = double.MaxValue;
        void Run(double seconds)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                bot.Crewmates = [them];
                var intent = bot.Decide(driver, world, world.Tick, out _);
                if (CabControls.CanDrive(driver, train) && CabControls.Clears(c, train, CabControls.ReleasesBrake(intent, driver, train)))
                    c.Brake = 0;
                CabControls.Apply(ref c, intent, driver, train);
                world.BeginTick();
                world.CrewAct(ref driver, intent, 1);
                world.Step(c);
                PlayerMotor.Step(ref driver, intent, train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
                slowest = Math.Min(slowest, train.Dynamics.Velocity);
            }
        }

        Run(150);
        Assert.True(slowest < -0.5, "it set back");
        Assert.InRange(train.Dynamics.RearDistance - at, -12, 25);
        Assert.True(train.Dynamics.Speed < 0.1, $"standing for them at {train.Dynamics.Speed:0.0} m/s");
        // They're on: forward again.
        them = PlayerMotor.SpawnOnRoof(train, 2, 0, P);
        Run(40);
        Assert.Equal(1, c.Reverser);
        Assert.True(train.Dynamics.Velocity > 3, $"under way again at {train.Dynamics.Velocity:0.0} m/s");
    }
}
