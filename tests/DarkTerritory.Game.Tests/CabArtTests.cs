using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The cab's controls as they're worked (note 445; the art checklist's crew-cab): the reverser swung over when it's thrown,
/// the driver's hand going to it and back; and the whistle's lever on its cap, pulled down by its rod only while a hand is
/// on the cord, so from the roofs a crewmate's whistle is told from the Whistler's (App. A.4).
/// </summary>
public class CabArtTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static TrainOnLine Train()
    {
        var t = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var line = RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        return new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 3, 1)), line, 1200);
    }

    [Fact]
    public void TheReverserSwingsOverWithTheDriversHandOnIt()
    {
        var look = Look.Load(Content);
        var train = Train();
        var engine = train.Frames[0];
        var levers = engine.Shape.Levers!.Value;
        var mesh = new MeshBuilder();
        Double3 Grip(int reverser, double time)
        {
            mesh.Clear();
            Assert.True(look.Art.CabControls(mesh, engine, engine.Origin, new TrainControls { Reverser = reverser }, time: time));
            return look.Art.ReverserGrip;
        }
        var ahead = engine.ToWorld(levers.ReverserAt(1));
        var back = engine.ToWorld(levers.ReverserAt(-1));
        Assert.True((Grip(1, 0) - ahead).Length < 1e-6, "at rest, forward");
        Assert.Equal(0, look.Art.ReverserHand(0.5));
        // Thrown at 1 s: the hand goes to it first, and the lever stays where it was...
        Assert.True((Grip(-1, 1) - ahead).Length < 1e-6, "it jumped before the hand was on it");
        Assert.InRange(look.Art.ReverserHand(1 + SceneArt.ReverserReach / 2), 0.1, 0.9);
        // ...then it's hauled over, the hand on it...
        double mid = 1 + SceneArt.ReverserReach + SceneArt.ReverserThrowSeconds / 2;
        var halfway = Grip(-1, mid);
        Assert.InRange((halfway - ahead).Length, 0.05, (back - ahead).Length - 0.05);
        Assert.Equal(1, look.Art.ReverserHand(mid));
        // ...and stays over, the hand back off it.
        double done = 1 + 2 * SceneArt.ReverserReach + SceneArt.ReverserThrowSeconds + SceneArt.ReverserHold;
        Assert.True((Grip(-1, done) - back).Length < 1e-6, "not over at the end of the throw");
        Assert.Equal(0, look.Art.ReverserHand(done + 0.01));
    }

    [Fact]
    public void TheWhistlesLeverIsDownOnlyWhileAHandIsOnTheCord()
    {
        var look = Look.Load(Content);
        // First seen as it is (a still, or joining mid-blast), then eased over a fraction of a second either way.
        Assert.Equal(0, look.Art.Pull(false, 0));
        Assert.Equal(0, look.Art.Pull(true, 1));
        Assert.InRange(look.Art.Pull(true, 1.05), 0.01, 0.99);
        Assert.Equal(1, look.Art.Pull(true, 1.5));
        Assert.Equal(1, look.Art.Pull(false, 2));
        Assert.Equal(0, look.Art.Pull(false, 2.5));
        Assert.Equal(1, Look.Load(Content).Art.Pull(true, 0));
    }

    [Fact]
    public void TheLeverStandsOnTheWhistlesCapAndItsRodRunsToTheRoofOverTheCord()
    {
        var shape = Train().Frames[0].Shape;
        var cab = shape.Cab!.Value;
        float roof = (float)shape.Solids.Where(x => x.Part == PartKind.CabRoof).Max(x => x.Box.Max.Y);
        var pivot = TrainKit.WhistleLeverPivot(shape);
        // On the whistle's cap: half a metre over the roof, just behind the cab.
        Assert.InRange(pivot.Y - roof, 0.4f, 0.65f);
        Assert.InRange(pivot.Z, (float)cab.Max.Z, (float)cab.Max.Z + 1);
        // Up at rest and down pulled, the tip over the roof's width either way.
        foreach (float angle in new[] { TrainKit.WhistleLeverRest, TrainKit.WhistleLeverPulled })
        {
            var tip = pivot + new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0) * TrainKit.WhistleLeverLength;
            Assert.InRange(tip.X, 0, (float)shape.HalfWidth);
            Assert.True(tip.Y > roof, "the lever goes through the roof");
        }
        Assert.True(TrainKit.WhistleLeverRest > 0 && TrainKit.WhistleLeverPulled < -0.3f, "rest and pulled too alike to tell apart from the roofs");
        // The crank on the cab roof over the cord's handle in the cab.
        var crank = TrainKit.WhistleCrank(shape, cab);
        var cord = TrainKit.WhistleCordHandle(shape, pulled: false);
        Assert.Equal(roof, crank.Y, 3);
        Assert.InRange(crank.Z, (float)cab.Min.Z, (float)cab.Max.Z);
        Assert.InRange(crank.Z - (float)cord.Z, -0.01f, 0.7f);
    }
}
