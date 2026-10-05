using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// A roof walker pacing a standing train jumps its coupling gaps as it does a moving one (T102's walk aboard at the gate
/// showed it going over the side at every gap, down to the ballast and back up the next car's ladder).
/// </summary>
public class RoofWalkTests
{
    static readonly PlayerTuning P = Tuning.Player;

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    public void PacingTheRoofsItJumpsTheGapsAndStaysUpThere(double speed)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(40_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, 5_000);
        train.Dynamics.Velocity = speed;
        var world = new World(train);
        var walker = new RoofWalkerBot(3);
        var s = PlayerMotor.SpawnOnRoof(train, 2, 0, P);
        int down = 0, cars = 0, was = s.Parent;
        var path = new System.Text.StringBuilder();
        string last = "";
        for (int i = 0; i < 60 * SimConstants.TickRate; i++)
        {
            var intent = walker.Decide(s, world, world.Tick, out _);
            world.BeginTick();
            world.Step(new TrainControls { Reverser = 1 });
            train.Dynamics.Velocity = speed;
            bool before = s.Parent == PlayerState.World;
            PlayerMotor.Step(ref s, intent, train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: true);
            if (!before && s.Parent == PlayerState.World && s.Surface == Surface.Ground)
                down++;
            if (s.Parent > 0 && s.Parent != was)
                cars++;
            if (s.Parent >= 0)
                was = s.Parent;
            string now = $"{s.Parent}{s.Surface.ToString()[0]}";
            if (now != last)
                path.Append($" {i / 30.0:0.0}:{now}@{s.Position.Z:0.0}");
            last = now;
        }
        Assert.True(cars >= 3, $"it went from car to car ({cars}):{path}");
        Assert.True(down == 0, $"it went down onto the ballast {down} times");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(10)]
    [InlineData(-6)]
    public void BackingRoundATightCurveItWaitsAtTheGapsAndStaysUpThere(double frontPastCurve)
    {
        // T121 (the brakes cut): backing out of a Foundry's spur at shunting speed (6.2 m/s), a walker on car 1 jumped back
        // for car 2 round the 60 m turnout curve; the far roof went its own way under it, and it came down on the ballast and
        // was left there. Car 1's front was just off the curve, which is all the jump read. Every landing is checked as well
        // as the falls: one 1.47 m out on a 1.5 m half-roof is the same jump a few centimetres unluckier.
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(2_000), new TrackSegment(150, Radius: 60), new TrackSegment(2_000)]));
        const double speed = -6.2;
        var probe = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, 3_000);
        double car1Back = probe.Dynamics.Distance - probe.Cars[1].FrontDistance;
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, 2_150 + frontPastCurve + car1Back);
        train.Dynamics.Velocity = speed;
        var world = new World(train);
        // At car 1's back end, facing back down the train, and heading that way.
        var walker = new RoofWalkerBot(3);
        walker.Head(+1);
        var s = PlayerMotor.SpawnOnRoof(train, 1, train.Frames[1].Shape.HalfLength - 1.0, P) with { Yaw = Math.PI };
        int down = 0, cars = 0, was = s.Parent;
        double worst = 0;
        var path = new System.Text.StringBuilder();
        string last = "";
        for (int i = 0; i < 45 * SimConstants.TickRate; i++)
        {
            var intent = walker.Decide(s, world, world.Tick, out _);
            world.BeginTick();
            world.Step(new TrainControls { Reverser = -1 });
            train.Dynamics.Velocity = speed;
            bool before = s.Parent == PlayerState.World;
            var air = s.Surface;
            PlayerMotor.Step(ref s, intent, train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: true);
            if (!before && s.Parent == PlayerState.World && s.Surface == Surface.Ground)
                down++;
            if (air == Surface.Air && s.Surface == Surface.Roof)
                worst = Math.Max(worst, Math.Abs(s.Position.X));
            if (s.Parent > 0 && s.Parent != was)
                cars++;
            if (s.Parent >= 0)
                was = s.Parent;
            string now = $"{s.Parent}{s.Surface.ToString()[0]}";
            if (now != last)
                path.Append($" {i / 30.0:0.0}:{now}@{s.Position.X:0.00},{s.Position.Z:0.0}");
            last = now;
        }
        Assert.True(down == 0, $"it went down onto the ballast {down} times:{path}");
        Assert.True(worst < 1.0, $"it landed {worst:0.00} m off a roof's centreline:{path}");
        // Off the curve, it carries on along the roofs.
        Assert.True(cars >= 2, $"it went from car to car once the train was round ({cars}):{path}");
    }
}
