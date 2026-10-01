using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The Passenger's model (GDD v1.2 §21, App. A.8; tools/blender/passenger.py; ARCHITECTURE §8 note 124): a conductor off a
/// lost train, a man's height in his cap, in the colour of the crewmate it copies; dragging someone, it's a stride ahead
/// of them (they're at its feet in the sim).
/// </summary>
public class PassengerArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly CreatureArt Art = new(Look, Content);

    static Vertex[] Drawn(SpinePhase phase, int looks, float pace = 0)
    {
        var p = new Passenger(48);
        p.Restore(phase, 2, 5, 1, default, 0, 0, 0, looks, 0);
        var mesh = new MeshBuilder();
        Assert.True(Art.Enemy(mesh, Matrix4x4.Identity, p, pace: pace));
        var v = mesh.Flattened();
        Assert.NotEmpty(v);
        return v;
    }

    static Vector3 Centre(Vertex[] v) => new(v.Average(p => p.Position.X), v.Average(p => p.Position.Y), v.Average(p => p.Position.Z));

    [Fact]
    public void ItsAMansHeightInItsCapAndWearsTheColourOfWhoItCopies()
    {
        var two = Drawn(SpinePhase.Telegraph, 2);
        Assert.InRange(two.Max(p => p.Position.Y), 1.78f, 1.95f);
        // The scarf is dyed (look.json crewColours): another crewmate's copy is another colour, and nothing else changes.
        var five = Drawn(SpinePhase.Telegraph, 5);
        Assert.Equal(two.Length, five.Length);
        int differ = Enumerable.Range(0, two.Length).Count(i => (two[i].Color - five[i].Color).Length() > 0.01f);
        Assert.InRange(differ, 1, two.Length / 4);
    }

    [Fact]
    public void DraggingItsAStrideAheadOfWhoItDrags()
    {
        // Facing −Z (Extra2 0); they're at its feet (the origin) in the sim, so it's drawn ahead of them, leant into the haul.
        var standing = Centre(Drawn(SpinePhase.Telegraph, 2));
        var dragging = Centre(Drawn(SpinePhase.Grab, 2, pace: 1.3f));
        Assert.InRange(standing.Z, -0.15f, 0.15f);
        Assert.InRange(dragging.Z, -1.0f, -0.5f);
    }
}
