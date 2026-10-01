using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T102 (playtest: "I dont understand how Bots are doing test runs if they cannot traverse into all the car positions they
/// need"): at the gate the crew walk and climb aboard to their posts, nobody put there. The driver and fireman go up the
/// cab steps from the ballast; the driver waits on the brake until they're on.
/// </summary>
public class WalkAboardTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly Route.Route Frontier = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 7);

    static (World World, PlayerState Driver, PlayerState Fireman) AtTheGate(int car)
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), Frontier.Build(), 590, Tuning.Boiler);
        var world = new World(train, Tuning.Combat);
        world.EnableRun(Tuning.Run, Frontier, 600, authority: true);
        var frame = train.Frames[car];
        var at = frame.ToWorld(new Double3(frame.Shape.HalfWidth + 2.2, 0, 0));
        var fireman = PlayerMotor.SpawnOnGround(at, train.Line, train.Cars[car].FrontDistance - frame.Shape.HalfLength, P);
        return (world, PlayerMotor.SpawnInCab(train, P), fireman);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(6)]
    public void TheFiremanWalksUpTheCabStepsFromBesideAnyCar(int car)
    {
        var (world, driverState, fireState) = AtTheGate(car);
        var calls = new CrewCalls();
        var driver = new ConductorBot(calls, 0);
        var fireman = new ConductorBot(calls, 1) { Fireman = true };
        var c = new TrainControls { Reverser = 1, Brake = 1 };
        for (int i = 0; i < 90 * SimConstants.TickRate && !PlayerMotor.InCab(fireState, world.Train); i++)
        {
            driver.Crewmates = [fireState];
            var d = driver.Decide(driverState, world, world.Tick, out _);
            var f = fireman.Decide(fireState, world, world.Tick, out _);
            if (CabControls.CanDrive(driverState, world.Train) && CabControls.Clears(c, world.Train, CabControls.ReleasesBrake(d, driverState, world.Train)))
                c.Brake = 0;
            CabControls.Apply(ref c, d, driverState, world.Train);
            world.BeginTick();
            world.CrewAct(ref driverState, d, 1);
            world.CrewAct(ref fireState, f, 2);
            world.Step(c);
            PlayerMotor.Step(ref driverState, d, world.Train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
            PlayerMotor.Step(ref fireState, f, world.Train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
            // All aboard: the train doesn't go while the fireman's still down there.
            Assert.True(world.Train.Dynamics.Speed < 0.1, $"it pulled away with the fireman not yet aboard ({fireState.Position})");
        }
        Assert.True(PlayerMotor.InCab(fireState, world.Train), $"the fireman got to {fireState.Parent} {fireState.Surface} {fireState.Position}");
        // All aboard: away.
        for (int i = 0; i < 40 * SimConstants.TickRate; i++)
        {
            driver.Crewmates = [fireState];
            var d = driver.Decide(driverState, world, world.Tick, out _);
            if (CabControls.CanDrive(driverState, world.Train) && CabControls.Clears(c, world.Train, CabControls.ReleasesBrake(d, driverState, world.Train)))
                c.Brake = 0;
            CabControls.Apply(ref c, d, driverState, world.Train);
            world.BeginTick();
            world.CrewAct(ref driverState, d, 1);
            world.Step(c);
            PlayerMotor.Step(ref driverState, d, world.Train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
            PlayerMotor.Step(ref fireState, default, world.Train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
        }
        Assert.True(world.Train.Dynamics.Speed > 1, $"under way at {world.Train.Dynamics.Speed:0.0} m/s");
    }
}
