using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The Followers (GDD v1.2 §21, App. A.6; tools/blender/follower.py; ARCHITECTURE §8 note 121): riding, a hand flat on its
/// carrier's back where their friends can see it; off them it scuttles; nesting, it swells as the nest builds.
/// </summary>
public class FollowerTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly CreatureArt Art = new(Look, Content);

    static Vertex[] Drawn(SpinePhase phase, double extra, double nest, CreatureArt.Prey? prey = null)
    {
        var f = new Follower(1);
        f.Restore(phase, 1, 1, Enemy.Loose, default, 0, 0, 0, extra, nest);
        var mesh = new MeshBuilder();
        Assert.True(Art.Enemy(mesh, Matrix4x4.CreateTranslation(0, 0, -4), f, prey: prey));
        var v = mesh.Flattened();
        Assert.NotEmpty(v);
        return v;
    }

    static Vector3 Centre(Vertex[] v) => new(v.Average(p => p.Position.X), v.Average(p => p.Position.Y), v.Average(p => p.Position.Z));

    [Fact]
    public void RidingItsFlatOnItsCarriersBack()
    {
        // The carrier 2 m off, facing −X: their back is to +X of them, at about shoulder-blade height.
        var carrier = new CreatureArt.Prey(new Vector3(2, 0, -4), -Vector3.UnitX);
        var v = Drawn(SpinePhase.Telegraph, 3, 0, carrier);
        var c = Centre(v);
        Assert.InRange(c.Y, 1.2f, 1.6f);
        Assert.InRange(c.X, 2.05f, 2.35f);
        Assert.InRange(c.Z, -4.15f, -3.85f);
        // Flat to the back: thin along the way they face (x), spread across it and up it.
        float thick = v.Max(p => p.Position.X) - v.Min(p => p.Position.X), tall = v.Max(p => p.Position.Y) - v.Min(p => p.Position.Y);
        Assert.True(thick < tall * 0.5f, $"{thick} thick, {tall} tall");
    }

    [Fact]
    public void OffItsCarrierItsSmallOnTheGroundAndItsNestSwellsIt()
    {
        var crawling = Drawn(SpinePhase.Commit, -1, 0);
        Assert.True(crawling.Max(p => p.Position.Y) < 0.15f, "it's down on its fingertips");
        static float Span(Vertex[] v) => v.Max(p => p.Position.Z) - v.Min(p => p.Position.Z);
        float building = Span(Drawn(SpinePhase.Commit, -1, 0.5)), built = Span(Drawn(SpinePhase.Punish, -1, 1));
        Assert.True(building > Span(crawling) * 1.3f && built > building * 1.2f, $"crawling {Span(crawling)}, half a nest {building}, a nest {built}");
    }
}
