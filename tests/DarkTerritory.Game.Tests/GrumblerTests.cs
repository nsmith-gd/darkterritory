using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The Grumbler (GDD v1.2 §21, App. A.8; tools/blender/grumbler.py; ARCHITECTURE §8 note 119): face-down like a spider on
/// the crates, gnawing; hit, it rears up, then scuttles after them while it's going and bites when it's on them; on one it's
/// beaten down, it mauls, facing them.
/// </summary>
public class GrumblerTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly CreatureArt Art = new(Look, Content);
    static readonly Matrix4x4 There = Matrix4x4.CreateTranslation(0, 0, -4);

    static Grumbler At(SpinePhase phase, bool feral)
    {
        var g = new Grumbler(1);
        g.Restore(phase, 0.6, 8, Enemy.Loose, default, 0, 0, 0, -1, feral ? 1 : 0);
        return g;
    }

    static Vertex[] Drawn(Grumbler g, CreatureArt.Prey? prey = null, float pace = 0)
    {
        var mesh = new MeshBuilder();
        Assert.True(Art.Enemy(mesh, There, g, prey: prey, pace: pace));
        var v = mesh.Flattened();
        Assert.NotEmpty(v);
        return v;
    }

    static float Top(Vertex[] v) => v.Max(p => p.Position.Y);

    [Fact]
    public void ItsDownLikeASpiderTillItsHitAndThenItRearsUp()
    {
        // Gnawing, it's low: its back at a crouching child's height, its elbows and knees up over it.
        Assert.True(Top(Drawn(At(SpinePhase.Telegraph, false))) < 0.85f, $"gnawing {Top(Drawn(At(SpinePhase.Telegraph, false)))}");
        // Hit, it rears up on its back legs (the telegraph); on them and stood, it's up to bite; going, it's down again.
        float reared = Top(Drawn(At(SpinePhase.Telegraph, true)));
        Assert.True(reared > Top(Drawn(At(SpinePhase.Telegraph, false))) + 0.2f, $"reared {reared}");
        Assert.True(Top(Drawn(At(SpinePhase.Commit, true))) > Top(Drawn(At(SpinePhase.Commit, true), pace: 3)) + 0.2f, "it bites stood, scuttles going");
    }

    [Fact]
    public void AfterThemItFacesThem()
    {
        // Who it's after off its +X: its reach is that way, not ahead of it (−Z).
        var prey = new CreatureArt.Prey(new Vector3(2.5f, 0, -4), -Vector3.UnitX);
        var v = Drawn(At(SpinePhase.Commit, true), prey: prey);
        Assert.True(v.Max(p => p.Position.X) > 0.9f, $"reaches to x {v.Max(p => p.Position.X)}");
        Assert.True(v.Max(p => p.Position.X) > -(v.Min(p => p.Position.Z) + 4) * 0.9f, "its reach is towards them");
        // Gnawing it faces nobody: drawn the same with or without them.
        Assert.Equal(Drawn(At(SpinePhase.Telegraph, false)).Length, Drawn(At(SpinePhase.Telegraph, false), prey: prey).Length);
        Assert.Equal(Drawn(At(SpinePhase.Telegraph, false))[0].Position, Drawn(At(SpinePhase.Telegraph, false), prey: prey)[0].Position);
    }
}
