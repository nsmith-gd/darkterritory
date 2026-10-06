using System.Numerics;
using Ballast;
using Ballast.Assets;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The Whistler (GDD v1.2 §21, App. A.4; tools/blender/whistler.py; ARCHITECTURE §8 note 133): coiled small enough to hide
/// under the bridge plate in a coupling gap, on the rail; reared up out of it to the cord when it whistles; and its front
/// turned to whoever looks.
/// </summary>
public class WhistlerTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly CreatureArt Art = new(Look, Content);
    static readonly TrainTuning Train = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));

    static Model Model => Art.Get("whistler") ?? throw new FileNotFoundException("content/art/models/whistler.glb: run tools/models/build.sh whistler");

    /// <summary>The lowest and highest skinned vertex over a clip's frames (m, model space).</summary>
    static (float Low, float High) Extent(string clip)
    {
        var m = Model;
        var c = m.Clips[clip];
        var pose = new Pose(m.Skeleton.Count);
        var skinner = new Skinner();
        float low = float.PositiveInfinity, high = float.NegativeInfinity;
        for (int f = 0; f < c.Frames; f += 2)
        {
            skinner.Evaluate(m, c, f / (double)c.Fps, true, pose);
            foreach (var p in m.Parts)
                for (int i = 0; i < p.Positions.Length; i++)
                {
                    var v = Vector3.Zero;
                    for (int k = 0; k < 4; k++)
                        if (p.Weights[i * 4 + k] > 0)
                            v += Vector3.Transform(p.Positions[i], pose.Skin[p.Joints[i * 4 + k]]) * p.Weights[i * 4 + k];
                    (low, high) = (MathF.Min(low, v.Y), MathF.Max(high, v.Y));
                }
        }
        return (low, high);
    }

    [Fact]
    public void FoldedItFitsUnderThePlateAndWhistlingItReachesTheEaves()
    {
        var g = Train.Geometry;
        var fold = Extent("fold");
        Assert.InRange(fold.Low, -0.05f, 0.08f);
        Assert.True(fold.High < g.CouplerHeight - 0.05, $"folded it's {fold.High} m: under the {g.CouplerHeight} m plate");
        Assert.True(Extent("watch").High < g.CouplerHeight + 0.4, "watching, it's still crouched in the gap");
        // Whistling, it's up out of the gap and the long arm's high over the plate: over the head of anyone stood on it
        // (a crewmate's 1.8 m), where the roofs either side look down on it.
        Assert.True(Extent("whistle").High > g.CouplerHeight + 1.8 + 0.1, $"whistle {Extent("whistle").High}");
    }

    [Fact]
    public void InItsGapItsFeetAreOnTheRailAndItFacesWhoeverLooks()
    {
        var mesh = new MeshBuilder();
        var w = new Whistler(1);
        // The sim's gap point, 0.6 m over the rail; the eye 3 m off to the model's +X (the model sits 3 m from it).
        w.Restore(SpinePhase.Dormant, 1, 3, 2, new Double3(0, 0.6, 7.75), 0, 0, 0, 0, 0);
        Assert.True(Art.Enemy(mesh, Matrix4x4.CreateTranslation(-3, 0.6f, 0), w));
        var v = mesh.Flattened();
        Assert.NotEmpty(v);
        Assert.InRange(v.Min(p => p.Position.Y), -0.06f, 0.1f);
        // Facing the eye (towards +X): its front, laid up over its coil, is on the eye's side of it.
        float mid = v.Average(p => p.Position.X), head = v.Where(p => p.Position.Y > 0.75f).Average(p => p.Position.X);
        Assert.True(head > mid + 0.05f, $"head at x {head}, middle {mid}");
    }

    [Fact]
    public void CarryingSomeoneOffItHoldsThemUpUnderTheArmsInFrontOfItsRearedFront()
    {
        // Loose on its run to the nest with player 4 (App. A.4 GRAB): its hooks are where the scene hangs them
        // (GreyboxScene.Hung), under the arms over feet on the ground, and its reared front is behind them, not through them.
        var mesh = new MeshBuilder();
        var w = new Whistler(1);
        w.Restore(SpinePhase.Grab, 1, 3, Enemy.Loose, default, 0, 0, 0, 0, 0, holding: 4);
        Art.Clutches.Clear();
        Assert.True(Art.Enemy(mesh, Matrix4x4.CreateTranslation(0, 0, -6), w));
        Assert.True(Art.Clutches.TryGetValue(4, out var clutch), "carrying, it says where it holds them");
        Assert.InRange(clutch.At.Y, CreatureArt.CarriedUnderarm - 0.08f, CreatureArt.CarriedUnderarm + 0.08f);
        var v = mesh.Flattened();
        var origin = new Vector3(0, 0, -6);
        float ahead = Vector3.Dot(clutch.At - origin, clutch.Forward);
        Assert.True(ahead > 0.4f, $"the hooks are {ahead} m ahead of its middle");
        // Its column, at their body's height: behind their back (0.15 m behind the hooks), not where they hang.
        var column = v.Where(p => p.Position.Y is > 0.5f and < 1.2f).Select(p => Vector3.Dot(p.Position - clutch.At, clutch.Forward)).ToList();
        Assert.NotEmpty(column);
        Assert.True(column.Average() < -0.2f, $"its column is {column.Average()} m from the hooks along its run");
        Assert.True(v.Max(p => p.Position.Y) > 2.0f, "reared up over them");
    }
}
