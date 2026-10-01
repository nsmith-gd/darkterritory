using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The Soot Children's model (GDD v1.2 §21, App. A.6; tools/blender/soot_child.py; ARCHITECTURE §8 note 125): one child,
/// two ways; squatted in the ash, its head comes up when it calls; on someone, it's up on them, clinging to their chest.
/// </summary>
public class SootChildTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly CreatureArt Art = new(Look, Content);

    static Vertex[] Drawn(SpinePhase phase, double seconds, bool calling, bool soot, CreatureArt.Prey? prey = null)
    {
        var c = new SootChildren(41);
        c.Restore(phase, seconds, 1, Enemy.Loose, default, 0, 0, 0, calling ? 1 : 0, soot ? 1 : 0);
        var mesh = new MeshBuilder();
        Assert.True(Art.Enemy(mesh, Matrix4x4.Identity, c, prey: prey));
        var v = mesh.Flattened();
        Assert.NotEmpty(v);
        return v;
    }

    static Vector3 Centre(Vertex[] v) => new(v.Average(p => p.Position.X), v.Average(p => p.Position.Y), v.Average(p => p.Position.Z));

    [Fact]
    public void ASootChildIsTheSameChildWithItsOwnEyesHandsAndFeet()
    {
        // The variants: the same figure (the same height, the same place), but not the same eyes, hands and feet.
        var real = Drawn(SpinePhase.Telegraph, 1, false, false);
        var soot = Drawn(SpinePhase.Telegraph, 1, false, true);
        Assert.NotEqual(real.Length, soot.Length);
        Assert.InRange(soot.Max(p => p.Position.Y) - real.Max(p => p.Position.Y), -0.01f, 0.01f);
    }

    [Fact]
    public void SquattedItsHeadComesUpWhenItCalls()
    {
        float huddled = Drawn(SpinePhase.Telegraph, 1, false, true).Max(p => p.Position.Y);
        float calling = Drawn(SpinePhase.Telegraph, 1, true, true).Max(p => p.Position.Y);
        Assert.InRange(huddled, 0.35f, 0.9f);
        Assert.True(calling > huddled + 0.05f, $"its head's at {huddled} m huddled, {calling} m calling");
    }

    [Fact]
    public void OnSomeoneItsUpOnThemAtTheirChest()
    {
        // Them at the origin facing −Z: it's in front of them, off the ground, at their chest.
        var them = new CreatureArt.Prey(Vector3.Zero, -Vector3.UnitZ);
        var v = Drawn(SpinePhase.Grab, 2, false, true, them);
        var c = Centre(v);
        Assert.InRange(c.Y, 0.8f, 1.4f);
        Assert.InRange(c.Z, -0.6f, -0.1f);
        Assert.True(v.Min(p => p.Position.Y) > 0.3f, "its feet are off the ground, round them");
    }
}
