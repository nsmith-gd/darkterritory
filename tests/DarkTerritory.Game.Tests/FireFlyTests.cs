using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The Fire Flies (GDD v1.2 §21, App. A.5; tools/blender/fire_fly.py; ARCHITECTURE §8 note 123): moths at a lit lamp, a
/// few at first, beating round it; the longer they linger the more of them, and the more of those settled on its glass.
/// </summary>
public class FireFlyTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly CreatureArt Art = new(Look, Content);
    static readonly Vector3 Lamp = new(0, 2.4f, -4);

    static Vertex[] Drawn(double seconds)
    {
        var f = new FireFlies(25);
        f.Restore(SpinePhase.Telegraph, seconds, 1, 1, default, 0, 0, 0, 0, 0);
        var mesh = new MeshBuilder();
        Assert.True(Art.Enemy(mesh, Matrix4x4.CreateTranslation(Lamp), f));
        var v = mesh.Flattened();
        Assert.NotEmpty(v);
        return v;
    }

    [Fact]
    public void TheSwarmThickensRoundTheLampAsItLingers()
    {
        var early = Drawn(1);
        var late = Drawn(19);
        Assert.True(late.Length > early.Length * 2.5, $"{early.Length} vertices of them at 1 s, {late.Length} at 19 s");
        // All of it round the lamp: none further out than the loops they fly.
        foreach (var v in new[] { early, late })
            Assert.True(v.Max(p => (p.Position - Lamp).Length()) < 0.75f, "a fly far off the lamp");
    }

    [Fact]
    public void LingeringTheyreOnTheGlass()
    {
        // The share of them close in on the lamp (on its glass: its flame, a body's breadth further than the glass).
        static float OnGlass(Vertex[] v) => v.Count(p => (p.Position - Lamp).Length() < 0.17f) / (float)v.Length;
        float early = OnGlass(Drawn(1)), late = OnGlass(Drawn(19));
        Assert.True(late > 0.55f && late > early + 0.2f, $"{early:P0} on the glass at 1 s, {late:P0} at 19 s");
    }
}
