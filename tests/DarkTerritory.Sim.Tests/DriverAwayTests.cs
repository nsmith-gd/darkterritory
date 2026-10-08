using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 463 (queue #199, found by D1.3 on #183's 8-bot express sweep, seed 3): the driver drawn back along the engine's
/// hood (a rescue heed, for a walker a Dragger had on the engine's roof) never came back to the cab, and the fire went out
/// under it with the tender full. The driver who's left the cab walks back to the fire when it wants feeding.
/// </summary>
public class DriverAwayTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly RailLine Line = new(new LineDefinition("t", [new TrackSegment(80_000)]));

    [Fact]
    public void ADriverBackAlongTheHoodWalksBackToTheFireWhenItWantsFeeding()
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), Line, 5_000, Tuning.Boiler);
        train.Dynamics.Velocity = 18;
        var world = new World(train, Tuning.Combat);
        var bot = new ConductorBot(new CrewCalls(), 0);
        var cab = train.Frames[0].Shape.Cab!.Value;
        // Where seed 3's driver stood: on the engine's deck, along the hood behind the cab.
        var driver = PlayerMotor.SpawnInCab(train, P) is var s ? s with { Position = s.Position with { X = cab.Min.X + 0.3, Z = cab.Max.Z + 3 } } : default;
        Assert.False(PlayerMotor.InCab(driver, train));
        Assert.Equal(Surface.Deck, driver.Surface);
        train.Boiler.Firebox = 0.15 * Tuning.Boiler.FireboxCapacity; // well under the low fire
        double tender = train.Boiler.Tender;
        var c = new TrainControls { Reverser = 1 };
        bool inCab = false;
        for (int i = 0; i < 40 * SimConstants.TickRate; i++)
        {
            var intent = bot.Decide(driver, world, world.Tick, out _);
            CabControls.Apply(ref c, intent, driver, train);
            world.BeginTick();
            world.CrewAct(ref driver, intent, 1);
            world.Step(c);
            PlayerMotor.Step(ref driver, intent, train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
            inCab |= PlayerMotor.InCab(driver, train);
        }
        Assert.True(inCab, $"never back in the cab: at {driver.Position}");
        Assert.True(train.Boiler.Tender < tender, "never fired");
    }
}
