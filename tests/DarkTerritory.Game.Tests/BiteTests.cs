using System.Numerics;
using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// A car eaten from its rear end by a Car Hugger (GDD v1.2 App. A.3 FEED; Art/BiteKit, Shaders/bite.glsl): nothing until
/// it feeds, then a ragged frontier moving forward in bites, deepest down the middle, never under the floor, eaten
/// through when the sim drops the car; the thing's head and hands follow it in.
/// </summary>
public class BiteTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly TrainTuning Tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly CarShape Van = CarShape.Build(Tuning.Geometry, VehicleKind.Guard, hasCarBehind: false);

    static Bite At(double eaten) => Bite.Of(Look.Tuning.Bite, Van, eaten, 1 - eaten, Bite.ScarSeed(3));

    [Fact]
    public void NothingIsEatenUntilItFeeds()
    {
        Assert.False(At(0).Any);
        Assert.False(At(0).Eats(new Vector3(0, 2, (float)Van.HalfLength)));
        // The first mouthful shows.
        Assert.True(At(0.001).Any);
    }

    [Fact]
    public void ItEatsForwardInBitesDeepestDownTheMiddle()
    {
        var t = Look.Tuning.Bite;
        var last = At(0);
        for (double e = 0.02; e <= 1.0001; e += 0.02)
        {
            var b = At(e);
            Assert.True(b.Centre >= last.Centre && b.Side >= last.Side && b.Advance >= last.Advance, $"{e}: never less eaten for eating more");
            Assert.True(b.Side <= b.Centre, $"{e}: the sides, which its hands hold, go last");
            Assert.True(b.Advance <= t.Advance + 1e-4f);
            // Once the rear platform's gone, the frontier steps a bite at a time into the body (each its own torn-edge mesh).
            float body = b.Centre - (b.RearZ - (float)Van.HalfLength);
            if (e >= t.Platform && body >= Bite.Step)
                Assert.Equal(0, MathF.Round(body / Bite.Step) * Bite.Step - body, 3);
            last = b;
        }
        // Eaten through (when the sim drops it): its share of the body gone, the rest still there.
        var through = At(1);
        float l = (float)Van.HalfLength;
        Assert.InRange(through.Centre - (through.RearZ - l), t.Depth * 2 * l - Bite.Step, t.Depth * 2 * l);
        Assert.True(through.Eats(new Vector3(0, 2, l - 1)));
        Assert.False(through.Eats(new Vector3(0, 2, -l + 1)));
    }

    [Fact]
    public void TheUnderframeAndTrucksStaySoItRolls()
    {
        var b = At(1);
        float l = (float)Van.HalfLength;
        for (float z = -l; z <= l + 1.2f; z += 0.25f)
        {
            Assert.False(b.Eats(new Vector3(0, b.Floor - 0.01f, z)), $"z {z}");
            Assert.False(b.Eats(new Vector3(0, 0.4f, z)), $"z {z}");
        }
    }

    [Fact]
    public void ItsHandsKeepHoldAheadOfTheSideWallsTornEdge()
    {
        float l = (float)Van.HalfLength, w = (float)Van.HalfWidth;
        for (double e = 0.05; e <= 1; e += 0.05)
        {
            var b = At(e);
            // Where the hands hold: 0.75 m inside the end at first, then as far forward again as they've gone.
            float held = l - 0.75f - b.Grip;
            foreach (float y in new[] { 2.05f, 3.72f })
                Assert.True(held < b.FrontierZ(w, y) + 0.02f, $"{e}: the hand at y {y} ({held}) is on wall that's there (edge {b.FrontierZ(w, y)})");
        }
    }

    [Fact]
    public void TheTornEdgeIsTheCarsOwnAndGrowsWithTheBite()
    {
        var half = At(0.5).Edge(Look, Van, TrainKit.Livery.Planked, 3).Vertices;
        Assert.NotEmpty(half);
        Assert.Equal(half.Select(v => v.Position), At(0.5).Edge(Look, Van, TrainKit.Livery.Planked, 3).Vertices.Select(v => v.Position));
        // It lies along the frontier, not scattered over the car.
        var b = At(0.5);
        foreach (var v in half)
            Assert.True(v.Position.Z > b.FrontierZ(v.Position.X, v.Position.Y) - 1.6f, $"{v.Position}");
    }
}
