using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The Draggers' grab (GDD v1.2 §21, App. A.4; tools/blender/dragger.py; ARCHITECTURE §8 note 129): gripping the roof
/// through it, and in its last moment yanked back across the roof and down under the lip.
/// </summary>
public class DraggerArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly CreatureArt Art = new(Look, Content);

    static Vertex[] Drawn(double seconds, double window)
    {
        var d = new Dragger(7);
        d.Restore(SpinePhase.Grab, seconds, 1, 1, default, 0, 0, 0, 1, 0, holding: 1, grabWindow: window);
        var mesh = new MeshBuilder();
        Assert.True(Art.Enemy(mesh, Matrix4x4.Identity, d));
        return mesh.Flattened();
    }

    [Fact]
    public void AtTheEndOfItsGrabItDragsThemUnder()
    {
        // Gripping through the grab, its hands are up on the roof; in the last of it, going down under the lip.
        float gripping = Drawn(3, 8).Max(p => p.Position.Y);
        float under = Drawn(7.95, 8).Max(p => p.Position.Y);
        Assert.True(under < gripping - 0.5f, $"its highest at {gripping} m gripping, {under} m dragging them under");
    }
}
