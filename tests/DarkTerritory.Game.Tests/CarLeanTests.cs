using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// ARCHITECTURE §8 note 370: the cars lean out on a bend taken too fast (App. F.1's overspeed telegraph, "the cars straining
/// and leaning"), each about its outer rail by its own strain, drawn only: the sim's frames stay upright.
/// </summary>
public class CarLeanTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning Tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));

    static TrainOnLine Train() =>
        new(new TrainDynamics(Consist.Uniform(Tuning, 3, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(5_000)])), 1_000);

    /// <summary>Its frames leaned by <paramref name="strain"/> (every car alike, outer rail on the right), settled.</summary>
    static List<CarFrame> Leaned(TrainOnLine train, CarLean lean, float strain, int outer, double from = 0)
    {
        var frames = new List<CarFrame>();
        for (int i = 0; i < 120; i++)
        {
            frames.Clear();
            frames.AddRange(train.Frames);
            lean.Apply(frames, train, [.. train.Frames.Select(_ => (strain, outer))], from + i / 60.0);
        }
        return frames;
    }

    [Fact]
    public void NothingAtTheBoardAndAllOfItAtTheLimit()
    {
        var t = Tuning.Overspeed;
        Assert.True(t.LeanDegrees > 0);
        Assert.Equal(0, CarLean.Angle(0, t));
        Assert.Equal(t.LeanDegrees * Math.PI / 180, CarLean.Angle(1, t), 9);
        // Growing faster towards the limit: half way leans well under half as far.
        Assert.InRange(CarLean.Angle(0.5f, t), 0.2 * CarLean.Angle(1, t), 0.45 * CarLean.Angle(1, t));
    }

    [Fact]
    public void ACarLeansOutAboutItsOuterRailAndItsInnerWheelsLift()
    {
        var train = Train();
        var upright = train.Frames[1];
        double angle = CarLean.Angle(1, Tuning.Overspeed);
        foreach (int outer in new[] { 1, -1 })
        {
            var f = CarLean.Lean(upright, outer * angle);
            double g = TrainKit.HalfGauge;
            // The outer rail's head stays where it was; the inner comes up by the gauge's rise.
            Assert.True((f.ToWorld(new Double3(outer * g, 0, 0)) - upright.ToWorld(new Double3(outer * g, 0, 0))).Length < 1e-9);
            double lift = Double3.Dot(f.ToWorld(new Double3(-outer * g, 0, 0)) - upright.ToWorld(new Double3(-outer * g, 0, 0)), Double3.Up);
            Assert.Equal(2 * g * Math.Sin(angle), lift, 6);
            // The roof goes out over the outer rail, and the frame stays square.
            var roof = f.ToWorld(new Double3(0, upright.Shape.RoofHeight, 0)) - upright.ToWorld(new Double3(0, upright.Shape.RoofHeight, 0));
            Assert.True(outer * Double3.Dot(roof, upright.Right) > 0.15);
            Assert.Equal(0, Double3.Dot(f.Right, f.Up), 9);
            Assert.Equal(1, f.Up.Length, 9);
            Assert.Equal(upright.Back, f.Back);
        }
    }

    [Fact]
    public void ItSettlesIntoTheLeanAndBackOutOfIt()
    {
        var train = Train();
        var lean = new CarLean();
        double full = CarLean.Angle(1, Tuning.Overspeed);
        // At the limit the rock rides on top of the lean: within its share either side.
        var frames = Leaned(train, lean, 1, 1);
        double rolled = Math.Asin(Double3.Dot(frames[1].Up, train.Frames[1].Right));
        Assert.InRange(rolled, full * (1 - Tuning.Overspeed.LeanRock) - 1e-6, full * (1 + Tuning.Overspeed.LeanRock) + 1e-6);
        // Not straight there: one frame in from upright it's only started.
        var fresh = new CarLean();
        (float, int)[] strain = [.. train.Frames.Select(_ => (0.6f, 1))];
        var first = new List<CarFrame>(train.Frames);
        fresh.Apply(first, train, strain, 0);
        first = new List<CarFrame>(train.Frames);
        fresh.Apply(first, train, strain, 1 / 60.0);
        Assert.InRange(Math.Asin(Double3.Dot(first[1].Up, train.Frames[1].Right)), 1e-6, CarLean.Angle(0.6f, Tuning.Overspeed) * 0.2);
        // Off the bend, back upright.
        frames = Leaned(train, lean, 0, 1, from: 2);
        Assert.True(Math.Abs(Double3.Dot(frames[1].Up, train.Frames[1].Right)) < 1e-3);
    }

    [Fact]
    public void WithoutABendToKnowItsNothingAndTheSimsFramesNeverLean()
    {
        var train = Train();
        var before = train.Frames.ToList();
        var frames = new List<CarFrame>(train.Frames);
        new CarLean().Apply(frames, train, (IReadOnlyList<(float, int)>?)null, 0);
        Assert.Equal(before, frames);
        Leaned(train, new CarLean(), 1, 1);
        Assert.Equal(before, train.Frames);
    }
}
