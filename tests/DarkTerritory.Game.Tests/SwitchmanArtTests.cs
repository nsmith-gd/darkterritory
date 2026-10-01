using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The Switchman at its lever (GDD v1.2 §21, App. A.7; tools/models/recipes/switchman.py; ARCHITECTURE §8 note 126): its
/// hand on the lever while it waits to throw it under the train, then the lever thrown across it.
/// </summary>
public class SwitchmanArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly CreatureArt Art = new(Look, Content);

    static Vertex[] Drawn(SpinePhase phase, double seconds)
    {
        var mesh = new MeshBuilder();
        Assert.True(Art.Enemy(mesh, Matrix4x4.Identity, EnemyKind.Switchman, phase, seconds, 0, extra2: 1));
        return mesh.Flattened();
    }

    [Fact]
    public void GrippingAndThrowingItHasTheLeverInItsHand()
    {
        int waiting = Drawn(SpinePhase.Telegraph, 1).Length;
        var gripping = Drawn(SpinePhase.Commit, 1);
        var thrown = Drawn(SpinePhase.Punish, 2);
        // The lever and its stand's post are drawn with it, and nothing of them while it only waits.
        Assert.Equal(gripping.Length, thrown.Length);
        Assert.True(gripping.Length > waiting, $"{waiting} vertices waiting, {gripping.Length} gripping");
        // Thrown, the lever's gone over: heaved from its side across in front of it, so its right side is drawn in.
        static float Right(Vertex[] v) => v.Max(p => p.Position.X);
        Assert.True(Right(thrown) < Right(gripping) - 0.05f, $"out to {Right(gripping)} m gripping, {Right(thrown)} m thrown");
    }
}
