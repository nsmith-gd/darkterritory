using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

public class RailLineTests
{
    static RailLine Line(params TrackSegment[] segments) => new(new LineDefinition("test", segments));

    static void Near(Double3 expected, Double3 actual, double tolerance = 0.01) =>
        Assert.True((expected - actual).Length < tolerance, $"expected {expected}, got {actual}");

    [Fact]
    public void StraightRunsAlongMinusZ()
    {
        var line = Line(new TrackSegment(100));
        Near(new Double3(0, 0, -100), line.Sample(100).Position);
        Near(new Double3(0, 0, -1), line.Sample(50).Tangent, 1e-9);
    }

    [Fact]
    public void QuarterCircleLeftEndsOnTheLeft()
    {
        const double r = 200;
        var line = Line(new TrackSegment(Math.PI * r / 2, Radius: r));
        Near(new Double3(-r, 0, -r), line.Sample(line.Length).Position, 0.05);
        Near(new Double3(-1, 0, 0), line.Sample(line.Length).Tangent, 0.01);
    }

    [Fact]
    public void GradeRaisesTheTrack()
    {
        var line = Line(new TrackSegment(100, GradePercent: 3));
        Assert.InRange(line.Sample(100).Position.Y, 2.99, 3.0);
        Assert.Equal(3, line.Sample(40).GradePercent);
    }

    [Fact]
    public void SamplingClampsToLineEnds()
    {
        var line = Line(new TrackSegment(50));
        Assert.Equal(0, line.Sample(-10).Distance);
        Assert.Equal(50, line.Sample(99).Distance);
    }

    [Fact]
    public void TestLoopContentLoads()
    {
        var line = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));
        Assert.Equal(7500, line.Length);
    }
}

public class TrainOnLineTests
{
    static readonly TrainTuning T = Tuning.Train;

    static TrainOnLine Train(int cars, params TrackSegment[] segments) =>
        new(new TrainDynamics(Consist.Uniform(T, cars, 1)), new RailLine(new LineDefinition("t", segments)), 500);

    [Fact]
    public void CarsAreSpacedByPitchOnStraightTrack()
    {
        var train = Train(3, new TrackSegment(1000));
        Assert.Equal(4, train.Cars.Count);
        var pitch = T.Geometry.CarLength + T.Geometry.CouplingGap;
        Assert.InRange((train.Cars[1].Centre - train.Cars[2].Centre).Length, pitch - 0.01, pitch + 0.01);
        Assert.InRange(train.Cars[0].Centre.Z, -500 + T.Geometry.EngineLength / 2 - 0.01, -500 + T.Geometry.EngineLength / 2 + 0.01);
    }

    [Fact]
    public void CarsFollowTheCurve()
    {
        var train = Train(5, new TrackSegment(200), new TrackSegment(1000, Radius: 150));
        train.Dynamics.Distance = 400;
        train.Step(SimConstants.TickSeconds, default);
        // Every car's forward points along the track under its centre.
        foreach (var car in train.Cars)
        {
            var tangent = train.Line.Sample(car.FrontDistance - car.Length / 2).Tangent;
            Assert.True(Double3.Dot(car.Forward, tangent) > 0.995);
        }
    }

    [Fact]
    public void GradeUnderTrainIsAveragedByMass()
    {
        // Engine sits on the climb, cars on the flat behind it.
        var train = Train(3, new TrackSegment(500), new TrackSegment(500, GradePercent: 3));
        train.Dynamics.Distance = 520;
        train.Step(SimConstants.TickSeconds, default);
        double engineShare = T.Mass.EngineTonnes / train.Dynamics.Consist.MassTonnes;
        Assert.InRange(train.AverageGrade(), 3 * engineShare - 0.01, 3 * engineShare + 0.01);
    }

    [Fact]
    public void BufferStopHaltsTheTrainAtLineEnd()
    {
        var train = Train(3, new TrackSegment(600));
        train.Dynamics.Velocity = 10;
        for (int i = 0; i < SimConstants.TickRate * 20; i++)
            train.Step(SimConstants.TickSeconds, new TrainControls { Throttle = 1, Reverser = 1 });
        Assert.Equal(600, train.Dynamics.Distance);
        Assert.Equal(0, train.Dynamics.Velocity);
    }

    static readonly RailLine TestLoop = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));

    // The test loop's 3% climb runs 2500–3300 m.
    static double ProgressFrom(int cars, double start, double speed, int seconds)
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), TestLoop, start);
        train.Dynamics.Velocity = speed;
        double best = start;
        for (int i = 0; i < SimConstants.TickRate * seconds && !train.AtEndOfLine; i++)
        {
            train.Step(SimConstants.TickSeconds, new TrainControls { Throttle = 1, Reverser = 1 });
            best = Math.Max(best, train.Dynamics.Distance);
        }
        return best;
    }

    [Fact]
    public void FromAStandingStartOnlyAShortTrainTopsTheClimb()
    {
        Assert.True(ProgressFrom(3, 2500, 0, 300) > 3300 + 66);
        Assert.True(ProgressFrom(20, 2500, 0, 300) < 3300);
    }

    [Fact]
    public void MomentumCarriesALongTrainOverAShortClimb()
    {
        // "Lose momentum, lose the summit" (GDD §22): at max speed 20 cars rush the 800 m climb.
        Assert.True(ProgressFrom(20, 2400, T.MaxSpeed, 120) > 3300 + 330);
    }
}
