using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The Climbers (GDD v1.2 §21, App. A.4; tools/blender/climber.py; ARCHITECTURE §8 notes 122, 135): pacing the train it
/// runs low on its six limbs along the line, not at the train; on the roofs it creeps flattened; in a dark car it waits low.
/// </summary>
public class ClimberTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly CreatureArt Art = new(Look, Content);

    static Vertex[] Drawn(SpinePhase phase, int attached, double extra, double side)
    {
        var c = new Climber(1);
        c.Restore(phase, 0.3, 1, attached, default, 0, 0, 0, extra, side);
        var mesh = new MeshBuilder();
        Assert.True(Art.Enemy(mesh, Matrix4x4.CreateTranslation(0, 0, -4), c));
        var v = mesh.Flattened();
        Assert.NotEmpty(v);
        return v;
    }

    static float Top(Vertex[] v) => v.Max(p => p.Position.Y);

    [Fact]
    public void PacingItRunsLowAlongTheLineNotAtTheTrain()
    {
        var running = Drawn(SpinePhase.Dormant, Enemy.Loose, 2, 1);
        Assert.True(Top(running) < 1.2f, $"low, {Top(running)} high");
        // The basis here faces −Z (at the train); running, it's turned a quarter round, so it's longer across (x) than deep.
        float across = running.Max(p => p.Position.X) - running.Min(p => p.Position.X);
        float deep = running.Max(p => p.Position.Z) - running.Min(p => p.Position.Z);
        Assert.True(across > deep, $"{across} across, {deep} deep");
    }

    [Fact]
    public void InADarkCarItWaitsLowAndOnTheRoofsItsCrouched()
    {
        Assert.True(Top(Drawn(SpinePhase.Commit, 2, -1, 1)) < 1.3f, "crouched in the corner");
        Assert.True(Top(Drawn(SpinePhase.Commit, 2, 2, 1)) < 1.7f, "walking the roof, crouched");
    }
}
