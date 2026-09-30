using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The rail model the line generator needs (linegen plan §8): clothoid transitions, parabolic vertical curves, and
/// alternates that leave the main line at a facing switch and come back onto it through a trailing one (§6.2).
/// </summary>
public class AlternateTests
{
    static readonly TrainTuning T = Tuning.Train;
    const double Toe = 1000, R = 500, Theta = 0.3;

    /// <summary>A straight main line with a bulge loop off it to the left: out θ, back 2θ, in θ, all at 500 m.</summary>
    static RailLine Line() => new(new LineDefinition("loop", [new TrackSegment(5000)]),
        [new BranchDefinition(BranchKind.Alternate, Toe, -1,
            [new TrackSegment(R * Theta, R), new TrackSegment(2 * R * Theta, -R), new TrackSegment(R * Theta, R)]) { Rejoin = Toe + 4 * R * Math.Sin(Theta) }]);

    static void Near(Double3 expected, Double3 actual, double tolerance) =>
        Assert.True((expected - actual).Length < tolerance, $"expected {expected}, got {actual}");

    [Fact]
    public void AClothoidEasesTheCurvatureInLinearly()
    {
        // Straight into 300 m radius over 100 m, then a 200 m arc, then out again: the deflection is the arc's plus half
        // of each spiral's, and the curvature ramps.
        var line = new RailLine(new LineDefinition("spiral",
        [
            new TrackSegment(100, 0) { EndRadius = 300 }, new TrackSegment(200, 300), new TrackSegment(100, 300) { EndRadius = 0 }, new TrackSegment(50),
        ]));
        Assert.Equal(1 / 600.0, line.Sample(50).Curvature, 6);
        Assert.Equal(1 / 300.0, line.Sample(200).Curvature, 9);
        Assert.Equal(0, line.Sample(420).Curvature, 9);
        double deflection = 200 / 300.0 + 2 * (100 / 300.0 / 2);
        var t = line.Sample(420).Tangent;
        Assert.Equal(deflection, Math.Atan2(-t.X, -t.Z), 4);
    }

    [Fact]
    public void AVerticalCurveIsAParabola()
    {
        // From level to 2% over 200 m: the rise is the mean grade times the length, and the grade ramps.
        var line = new RailLine(new LineDefinition("vc", [new TrackSegment(100), new TrackSegment(200, 0, 0) { EndGradePercent = 2 }, new TrackSegment(100, 0, 2)]));
        Assert.Equal(1, line.Sample(200).GradePercent, 6);
        Assert.Equal(2, line.Sample(300).Position.Y, 2);
        Assert.Equal(0.5, line.Sample(200).Position.Y, 2);
        Assert.Equal(4, line.Sample(400).Position.Y, 2);
    }

    [Fact]
    public void AnAlternateComesBackOntoTheMainLine()
    {
        var line = Line();
        var alt = Assert.Single(line.Branches);
        Assert.True(alt.Rejoins);
        // The loop's end is where the main line is at the rejoin, running the same way.
        var end = alt.Local.Sample(alt.Local.Length);
        var main = line.Sample(alt.Rejoin);
        Near(main.Position, end.Position, 0.05);
        Near(main.Tangent, end.Tangent, 1e-3);
        // Out to the left of the main line in between.
        Assert.True(line.Sample(alt.Index, Toe + 300).Position.X < -30);
        // The loop is longer than the main line it bypasses; past its end the path is the main line, offset.
        Assert.True(alt.Offset < 0);
        Assert.Equal(line.Length - alt.Offset, line.PathLength(alt.Index), 6);
        Near(line.Sample(3000).Position, line.Sample(alt.Index, 3000 - alt.Offset).Position, 1e-6);
        Assert.Equal(3000, line.MainDistance(alt.Index, 3000 - alt.Offset), 6);
        Assert.True(double.IsNaN(line.MainDistance(alt.Index, Toe + 300)));
        Assert.True(line.OnMain(alt.Index, Toe - 1));
    }

    static TrainOnLine Train(RailLine line, int cars, double front) => new(new TrainDynamics(Consist.Uniform(T, cars, 1)), line, front);

    static void Drive(TrainOnLine train, Func<bool> until, double speed = 10, int reverser = 1)
    {
        for (int i = 0; i < SimConstants.TickRate * 600 && !until(); i++)
            train.Step(SimConstants.TickSeconds, new TrainControls { Throttle = train.Dynamics.Speed < speed ? 1 : 0, Reverser = reverser });
    }

    [Fact]
    public void ATrainRunsRoundTheLoopAndIsBackInMainLineDistance()
    {
        var line = Line();
        var alt = line.Branches[0];
        var train = Train(line, 6, Toe - 100);
        Assert.True(train.ThrowSwitch(0, true, 12));
        double maxJump = 0;
        var last = train.Cars.Select(c => c.Centre).ToArray();
        bool sawVia = false;
        Drive(train, () =>
        {
            var now = train.Cars.Select(c => c.Centre).ToArray();
            maxJump = Math.Max(maxJump, now.Zip(last, (a, b) => (a - b).Length).Max());
            last = now;
            sawVia |= RailLine.ViaOf(train.Dynamics.Path) == 0;
            return train.Dynamics.Path == RailLine.MainPath && train.Dynamics.Distance > alt.Rejoin + 50;
        });
        // Round the loop and out onto the main line, addressed in main-line distance, tail and all.
        Assert.True(sawVia);
        Assert.Equal(RailLine.MainPath, train.Dynamics.Path);
        Assert.True(train.OnMain);
        foreach (var car in train.Cars)
            Near(line.Sample(car.FrontDistance - car.Length / 2).Position, car.Centre, 0.6);
        // Nothing jumped on the way: never more than a tick's travel.
        Assert.InRange(maxJump, 0, 10.5 * SimConstants.TickSeconds + 0.05);
    }

    [Fact]
    public void BackingUpOffTheMainLineReturnsOntoTheLoop()
    {
        var line = Line();
        var alt = line.Branches[0];
        var train = Train(line, 3, Toe - 100);
        train.ThrowSwitch(0, true, 12);
        Drive(train, () => train.Dynamics.Distance > alt.Rejoin + 20 && train.Dynamics.Path != alt.Index);
        Assert.Equal(RailLine.ViaPath(0), train.Dynamics.Path);
        // Stop, then reverse: back onto the loop, not down the main line (a spring switch lies for where you came from).
        for (int i = 0; i < SimConstants.TickRate * 20; i++)
            train.Step(SimConstants.TickSeconds, new TrainControls { Brake = 1, Reverser = 1 });
        Drive(train, () => train.Dynamics.Distance < Toe + 400 && train.Dynamics.Path == alt.Index, speed: 5, reverser: -1);
        Assert.Equal(alt.Index, train.Dynamics.Path);
        Assert.False(train.OnMain);
        Assert.True(train.Cars[0].Centre.X < -30);
    }

    [Fact]
    public void AnEngineComingOffTheLoopCouplesOntoCarsOnTheMainLine()
    {
        var line = Line();
        var alt = line.Branches[0];
        var train = Train(line, 4, alt.Rejoin + 200);
        // Cut two cars and leave them on the main line past the rejoin; the engine backs away down the main line, then
        // goes back the long way round: round the loop, and onto them from behind.
        Assert.True(train.Uncouple(2));
        Assert.Equal(2, train.Rakes.Count);
        var cut = train.Rakes.First(r => r != train.Dynamics);
        cut.Handbrake = true;
        // The engine's rake is the front half; put it back behind the points and send it round.
        var engine = train.Dynamics;
        Assert.True(engine.Distance > cut.Distance);
        engine.Distance = Toe - 200;
        engine.PreviousDistance = engine.Distance;
        train.ThrowSwitch(0, true, 12);
        for (int i = 0; i < SimConstants.TickRate * 900 && train.Rakes.Count > 1; i++)
        {
            // Briskly round, then a crawl onto them so the buckeyes couple rather than collide.
            bool close = RailLine.ViaOf(engine.Path) == 0 && cut.RearDistance - engine.Distance < 60;
            double speed = close ? 1.0 : 8;
            train.Step(SimConstants.TickSeconds, new TrainControls { Throttle = engine.Speed < speed ? 0.6 : 0, Brake = engine.Speed > speed + 0.3 ? 1 : 0, Reverser = 1 });
        }
        Assert.Single(train.Rakes);
        Assert.Equal(5, train.Dynamics.Consist.Vehicles.Count);
        foreach (var car in train.Cars)
            Assert.InRange(car.Centre.X, -1, 1); // all on the main line
    }
}
