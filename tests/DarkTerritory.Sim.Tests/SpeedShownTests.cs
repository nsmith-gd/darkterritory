using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The cab's speed is the speed the engine crosses the world at (queue #396, note 665; the director, 13 Oct: "It says we're
/// going 24 kilometers an hour, but honestly, it feels so slow ... We should really get a check to see if the kilometers per
/// hour that's displayed is accurate"). The cab shows <see cref="TrainDynamics.Speed"/>; the world draws the cars where
/// <see cref="TrainOnLine.Frames"/> puts them, and the eye rides in one.
/// </summary>
public class SpeedShownTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Theory]
    [InlineData(24 / 3.6)]
    [InlineData(50 / 3.6)]
    public void TheSpeedShownIsTheSpeedTheEngineCrossesTheWorldAt(double speed)
    {
        var route = Routes.Generate(Content, "frontier:7", 6);
        // Out from the departure's gate, as a night is.
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), gate + 50);
        int engine = train.Dynamics.Consist.Vehicles.First(v => v.IsEngine).Id;
        double dt = SimConstants.TickSeconds, worst = 0, bend = 0, grade = 0;
        int counted = 0;
        var last = train.Frames[engine].Origin;
        // Five minutes at about that speed, over whatever the line has: bends, grades.
        for (int i = 0; i < SimConstants.TickRate * 300; i++)
        {
            double shown = train.Dynamics.Speed;
            train.Step(dt, new TrainControls { Throttle = shown < speed ? 1 : 0, Brake = shown > speed + 0.3 ? 0.5 : 0, Reverser = 1 });
            var at = train.Frames[engine].Origin;
            double crossed = (at - last).Length / dt;
            last = at;
            if (shown < 1 || train.Dynamics.Speed < 1)
                continue;
            // Against the speed over the tick (the mean of before and after).
            double mean = (shown + train.Dynamics.Speed) / 2;
            worst = Math.Max(worst, Math.Abs(crossed - mean) / mean);
            var here = train.Line.Sample(train.Dynamics.Path, train.Dynamics.Distance);
            bend = Math.Max(bend, Math.Abs(here.Curvature));
            grade = Math.Max(grade, Math.Abs(here.GradePercent));
            counted++;
        }
        Assert.True(counted > SimConstants.TickRate * 120, $"moving only {counted * dt:0} s");
        Assert.True(bend > 1 / 5000.0 && grade > 0.3, $"no bend or grade: radius {1 / bend:0} m, grade {grade:0.0}%");
        Assert.True(worst < 0.02, $"the engine crossed the world {worst:P1} off the speed shown (bends to {1 / bend:0} m, grades to {grade:0.0}%)");
    }
}
