using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The Climbers (GDD v1.2 §21, App. A.4; tools/blender/climber.py; ARCHITECTURE §8 notes 122, 136): pacing the train it
/// runs low on all fours along the line, not at the train; on the roofs it slinks flattened; in a dark car it waits (note 1015).
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
    public void InADarkCarItWaitsUnderTheDoorwaysAndOnTheRoofsItsCrouched()
    {
        // (Since its redo, note 1015, it waits stood up on its hind legs as a stoat stands to look: under the car's 2.1 m
        // doorways and well under its roof, not folded on the floor.)
        float waiting = Top(Drawn(SpinePhase.Commit, 2, -1, 1));
        Assert.True(waiting < 1.6f, $"stood up in the corner, {waiting} high");
        Assert.True(Top(Drawn(SpinePhase.Commit, 2, 2, 1)) < 1.7f, "walking the roof, crouched");
    }
}
