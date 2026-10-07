using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T107: a train that's lost the hill rolls back down it, and nothing in the driver's cruising ever braked one going
/// backwards. deepTerritory:1 (crew of 8) stalled near the top of a 3.2 % climb and ran back 9.5 km to the gate at 22 m/s.
/// </summary>
public class RollBackTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;

    [Fact]
    public void RollingBackDownAClimbTheDriverBrakesItToAStand()
    {
        // Standing on a 3.2 % climb with the gauge at the power floor (no steam to hold it), rolling back.
        var line = new LineDefinition("climb", [new TrackSegment(400), new TrackSegment(3000, 0, 3.2), new TrackSegment(400)]);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 10, 1)), new RailLine(line), 1500, Tuning.Boiler);
        var world = new World(train);
        train.Boiler.Pressure = Tuning.Boiler.PowerFloor;
        train.Dynamics.Velocity = -1;
        var driver = new ConductorBot();
        var d = PlayerMotor.SpawnInCab(train, P);
        var c = new TrainControls { Reverser = 1 };
        double start = train.Dynamics.Distance, fastest = 0;
        for (uint tick = 0; tick < 60 * SimConstants.TickRate; tick++)
        {
            var di = driver.Decide(d, world, tick, out _);
            if (CabControls.Clears(c, train, CabControls.ReleasesBrake(di, d, train)))
                c.Brake = 0;
            CabControls.Apply(ref c, di, d, train);
            world.BeginTick();
            world.CrewAct(ref d, di, 1);
            world.Step(c);
            PlayerMotor.Step(ref d, di, train, P, T, SimConstants.TickSeconds, applyLook: false);
            fastest = Math.Max(fastest, -train.Dynamics.Velocity);
        }
        Assert.True(fastest < 3, $"it ran back at {fastest:0.0} m/s");
        Assert.True(start - train.Dynamics.Distance < 30, $"and {start - train.Dynamics.Distance:0} m back down the hill");
    }

    [Fact]
    public void WithTheDriverOffTheEngineTheFiremanBrakesItRollingBack()
    {
        // As deepTerritory:1 had it: the driver pulled off onto the ballast by Climbers, the fireman in the cab, and the train
        // rolling back down the climb.
        var line = new LineDefinition("climb", [new TrackSegment(400), new TrackSegment(3000, 0, 3.2), new TrackSegment(400)]);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 10, 1)), new RailLine(line), 1500, Tuning.Boiler);
        var world = new World(train);
        train.Boiler.Pressure = Tuning.Boiler.PowerFloor;
        train.Dynamics.Velocity = -1;
        var calls = new CrewCalls();
        var driver = new ConductorBot(calls, 0);
        var fireman = new ConductorBot(calls, 1) { Fireman = true };
        var cab = PlayerMotor.SpawnInCab(train, P);
        var frame = train.Frames[0];
        var d = PlayerMotor.SpawnOnGround(frame.ToWorld(new Ballast.Double3(frame.Shape.HalfWidth + 3, 0, 0)), train.Line, train.Dynamics.Distance, P);
        var f = cab with { Position = cab.Position + new Ballast.Double3(-1.2, 0, 0) };
        var c = new TrainControls { Reverser = 1 };
        double start = train.Dynamics.Distance, fastest = 0;
        for (uint tick = 0; tick < 60 * SimConstants.TickRate; tick++)
        {
            var di = driver.Decide(d, world, tick, out _);
            var fi = fireman.Decide(f, world, tick, out _);
            if (CabControls.Clears(c, train, CabControls.ReleasesBrake(fi, f, train)))
                c.Brake = 0;
            CabControls.Apply(ref c, fi, f, train);
            world.BeginTick();
            world.CrewAct(ref f, fi, 2);
            world.Step(c);
            PlayerMotor.Step(ref f, fi, train, P, T, SimConstants.TickSeconds, applyLook: false);
            fastest = Math.Max(fastest, -train.Dynamics.Velocity);
        }
        Assert.True(fastest < 3, $"it ran back at {fastest:0.0} m/s");
        Assert.True(start - train.Dynamics.Distance < 30, $"and {start - train.Dynamics.Distance:0} m back down the hill");
    }

    [Fact]
    public void BackingOntoCarsRollingAwayDownTheGradeTheDriverCatchesThemAndCouples()
    {
        // Note 188: the cut left on the main line at a stop on a grade, its brakes let off (the Passenger), rolls away down it.
        // Backing out onto it, the driver braked for where it was, not for where it was going: it settled a couple of metres
        // behind at the cars' own speed for 400 m, and never touched them. Closing on them over their speed, it couples.
        var line = new LineDefinition("climb", [new TrackSegment(400), new TrackSegment(3000, 0, 2), new TrackSegment(400)]);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), new RailLine(line), 1500, Tuning.Boiler);
        var world = new World(train);
        train.Boiler.Pressure = 70;
        Assert.True(train.Uncouple(train.Dynamics.Consist.Vehicles[3].Id));
        var cut = train.Rakes.Single(r => r != train.Dynamics);
        cut.Handbrake = false;
        var d = PlayerMotor.SpawnInCab(train, P);
        var c = new TrainControls { Reverser = 1, Brake = 1 };
        double gap = T.Geometry.CouplingGap;
        uint tick = 0;
        void Step(PlayerIntent di)
        {
            if (CabControls.Clears(c, train, CabControls.ReleasesBrake(di, d, train)))
                c.Brake = 0;
            CabControls.Apply(ref c, di, d, train);
            world.BeginTick();
            world.CrewAct(ref d, di, 1);
            world.Step(c);
            PlayerMotor.Step(ref d, di, train, P, T, SimConstants.TickSeconds, applyLook: false);
            tick++;
        }
        // Left standing on the brake while the cut gets going.
        for (int t = 0; t < 30 * SimConstants.TickRate; t++)
            Step(new PlayerIntent { Buttons = PlayerButtons.Brake });
        Assert.True(cut.Velocity < -0.3, $"the cut rolls ({cut.Velocity:0.00} m/s)");
        double from = cut.Distance;
        for (int t = 0; t < 300 * SimConstants.TickRate && train.TrainRakes > 1; t++)
        {
            var left = train.Rakes.First(r => r != train.Dynamics);
            double rolling = Math.Max(0, -left.Velocity), target = left.Distance + gap - 0.5;
            bool close = train.Dynamics.RearDistance - target < 15;
            Step(StopDriver.Toward(world, target, -1, close ? 0.8 + rolling : StopDriver.SetBackTop + rolling, rear: true, away: rolling));
        }
        Assert.Equal(1, train.TrainRakes);
        Assert.False(world.Derailed);
        Assert.True(from - train.Dynamics.RearDistance < 300, $"caught {from - train.Dynamics.RearDistance:0} m on");
    }
}
