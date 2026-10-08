using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The cab's glass in the cold (note 485; the art checklist's `cold`, GDD §22, §26 "frost on windows and metal, breath
/// vapour"): the frost grows in from each pane's frame as the cold deepens and never over the middle of the view ahead, and
/// a face near the glass fogs it.
/// </summary>
public class CabGlassTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static CarFrame Engine()
    {
        var t = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var line = RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        return new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 3, 1)), line, 1200).Frames[0];
    }

    /// <summary>A camera-relative effect vertex back in the engine's frame (drawn with the eye at the engine's origin).</summary>
    static Vector3 Local(in CarFrame engine, Vector3 p)
    {
        var d = new Double3(p.X, p.Y, p.Z);
        return new Vector3((float)Double3.Dot(d, engine.Right), (float)Double3.Dot(d, engine.Up), (float)Double3.Dot(d, engine.Back));
    }

    /// <summary>How far in from the nearest edge of <paramref name="pane"/> a point on it is (m).</summary>
    static float InFromFrame(TrainKit.Pane pane, Vector3 p)
    {
        float a = pane.U.Length(), b = pane.V.Length();
        var d = p - pane.Corner;
        float x = Vector3.Dot(d, pane.U / a), y = Vector3.Dot(d, pane.V / b);
        return MathF.Min(MathF.Min(x, a - x), MathF.Min(y, b - y));
    }

    [Fact]
    public void ThePanesAreTheCabsWindowsFacingIntoIt()
    {
        var shape = Engine().Shape;
        var cab = shape.Cab!.Value;
        var panes = TrainKit.CabPanes(shape).ToList();
        // Two front windows and each side's slit in three bays.
        Assert.Equal(8, panes.Count);
        foreach (var pane in panes)
        {
            Assert.True(pane.U.Length() > 0.15f && pane.V.Length() > 0.15f, $"a pane {pane.U.Length():0.00} by {pane.V.Length():0.00} m");
            var inside = pane.Centre + pane.In * 0.3f;
            Assert.True(cab.Contains(new Double3(inside.X, inside.Y, inside.Z)), $"the pane at {pane.Centre} faces out of the cab");
            Assert.InRange(pane.Centre.Y, (float)cab.Min.Y + 1, (float)cab.Max.Y);
        }
        // The front windows across the cab's front, either side of its middle; the slits along its sides.
        foreach (var front in panes.Take(2))
        {
            Assert.InRange(front.Centre.Z, (float)cab.Min.Z, (float)cab.Min.Z + 0.12f);
            Assert.Equal(Vector3.UnitZ, front.In);
        }
        Assert.True(panes[0].Centre.X < 0 && panes[1].Centre.X > 0);
        foreach (var side in panes.Skip(2))
            Assert.InRange(MathF.Abs(side.Centre.X), (float)shape.HalfWidth - 0.1f, (float)shape.HalfWidth);
    }

    [Fact]
    public void TheFrostCreepsInFromTheFramesAsItDeepensAndLeavesTheMiddleClear()
    {
        var look = Look.Load(Content);
        var engine = Engine();
        var panes = TrainKit.CabPanes(engine.Shape).ToList();
        var mesh = new MeshBuilder();
        // How far in from its frame the ice on this pane lies: on average, and at its furthest.
        (float Mean, float Most) Reach(float frost, TrainKit.Pane pane)
        {
            mesh.Clear();
            look.Art.Breath = 0;
            look.Art.CabGlass(mesh, engine, engine.Origin, frost, 1);
            var on = mesh.AlphaFx.Select(v => Local(engine, v.Position))
                .Where(p => MathF.Abs(Vector3.Dot(p - pane.Corner, pane.In)) < 0.01f && InFromFrame(pane, p) > -0.01f)
                .Select(p => InFromFrame(pane, p)).ToList();
            return on.Count == 0 ? (-1, -1) : (on.Average(), on.Max());
        }
        // A warm night: none.
        mesh.Clear();
        look.Art.Breath = 0;
        look.Art.CabGlass(mesh, engine, engine.Origin, 0, 1);
        Assert.Empty(mesh.AlphaFx);
        foreach (var pane in panes.Take(2))
        {
            var light = Reach(0.3f, pane);
            var deep = Reach(1, pane);
            Assert.True(light.Mean > 0, "no frost on a front window at 0.3");
            Assert.True(deep.Mean > light.Mean * 2, $"the frost reaches {light.Mean:0.000} m in at 0.3 and only {deep.Mean:0.000} m at its full");
            // Never over the middle of the driver's view: at its deepest, a corner's, it's not half way to the middle.
            float half = MathF.Min(pane.U.Length(), pane.V.Length()) / 2;
            Assert.True(deep.Most <= SceneArt.FrostMostOfPane * half + 0.005f, $"the frost reaches {deep.Most:0.00} m into a window {2 * half:0.00} m across");
            Assert.True(deep.Mean < 0.3f * half, $"on average the frost is {deep.Mean:0.00} m into a window {2 * half:0.00} m across");
        }
    }

    [Fact]
    public void AFaceAtTheGlassFogsItAndOneAcrossTheCabDoesNot()
    {
        var look = Look.Load(Content);
        var engine = Engine();
        var pane = TrainKit.CabPanes(engine.Shape).Skip(1).First();
        var mesh = new MeshBuilder();
        int Fog(Vector3 at)
        {
            mesh.Clear();
            look.Art.Breath = 1;
            look.Art.CabGlass(mesh, engine, engine.ToWorld(new Double3(at.X, at.Y, at.Z)), 0, 1, eyeBreathes: true);
            // (Drawn with the eye where it is: back into the engine's frame by the eye's offset.)
            var eye = engine.ToWorld(new Double3(at.X, at.Y, at.Z)) - engine.Origin;
            var offset = new Vector3((float)eye.X, (float)eye.Y, (float)eye.Z);
            foreach (var v in mesh.AlphaFx)
            {
                var p = Local(engine, v.Position + offset);
                Assert.True(InFromFrame(pane, p) > -0.005f, $"the fog spills off the pane at {p}");
            }
            return mesh.AlphaFx.Count(v => v.Layer < 0 && v.Colour.W > 0.02f);
        }
        Assert.True(Fog(pane.Centre + pane.In * 0.3f) > 0, "a breath 0.3 m from the glass doesn't fog it");
        Assert.Equal(0, Fog(pane.Centre + pane.In * 1.5f));
    }

    [Fact]
    public void TheFogComesOnTheBreathOutAndGoesBackBetween()
    {
        var levels = Enumerable.Range(0, 68).Select(i => SceneArt.BreathOut(i * 0.05, 3, hard: false)).ToList();
        Assert.All(levels, l => Assert.InRange(l, SceneArt.FogStays, 1));
        Assert.True(levels.Max() > 0.95f && levels.Min() < SceneArt.FogStays + 0.1f, "the fog doesn't come and go with the breath");
    }
}
