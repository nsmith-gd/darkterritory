using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The Grumbler's healing seen (note 487; GDD App. A.8 "it heals if only one player has hit it ... no one player can kill
/// it"; the art checklist's grumbler-anim "a tell for its healing"): while its health climbs the scene draws it knitting, so
/// a lone crewmate's blow is seen closing again; a gang's blows, which it doesn't heal, and a Grumbler whole and steady,
/// draw nothing of it.
/// </summary>
public class GrumblerHealingTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);

    static TrainOnLine Train()
    {
        var t = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var line = RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        return new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 3, 1)), line, 1200);
    }

    /// <summary>The effects drawn within 1.5 m of the Grumbler in a frame, its health <paramref name="from"/> the frame before and <paramref name="to"/> now.</summary>
    static int Around(double from, double to)
    {
        var train = Train();
        var side = train.Frames[1];
        var at = side.ToWorld(new Double3(-(side.Shape.HalfWidth + 6), 0, 0));
        var g = new Grumbler(27);
        var scene = new GreyboxScene { Look = Look, Enemies = [g] };
        var eye = at + new Double3(3, 1.7, 3);
        var mesh = new MeshBuilder();
        // First seen whole, as it is when it comes (its top is the most the scene's seen of it), then hit, then the frame.
        foreach (var (health, time) in new[] { (6.0, 0.5), (from, 1.0), (to, 1.05) })
        {
            g.Restore(SpinePhase.Telegraph, 1, health, Enemy.Loose, at, 0, 0, 0, -1, 1);
            scene.Time = time;
            mesh.Clear();
            scene.Build(mesh, train, eye);
        }
        var feet = new Vector3((float)(at - eye).X, (float)(at - eye).Y, (float)(at - eye).Z);
        return mesh.AlphaFx.Concat(mesh.AdditiveFx).Count(v => new Vector2(v.Position.X - feet.X, v.Position.Z - feet.Z).Length() < 1.5f);
    }

    [Fact]
    public void ItsHealingIsSeenAndAGangsBlowsAndAWholeOneShowNone()
    {
        int whole = Around(6, 6);
        int healing = Around(3, 3.05);
        int ganged = Around(3, 2.5);
        Assert.True(healing > whole + 40, $"healing draws {healing} effect vertices round it, whole {whole}");
        Assert.Equal(whole, ganged);
    }

    [Fact]
    public void TheMoreHurtTheHarderItKnits()
    {
        // The drops: more of them the further down it is.
        Assert.True(Around(1, 1.05) > Around(5, 5.05), "a nearly beaten Grumbler heals no harder than a scratched one");
    }
}
