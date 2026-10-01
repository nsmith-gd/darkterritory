using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The Ribbits (GDD v1.2 §21, App. A.6; tools/blender/ribbit.py): hopping in step with the sim's bursts, and on their catch
/// the tongue's out to them, from the mouth to their chest.
/// </summary>
public class RibbitTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly CreatureArt Art = new(Look, Content);

    static Vertex[] Draw(Ribbit r, CreatureArt.Prey? prey = null)
    {
        var mesh = new MeshBuilder();
        Assert.True(Art.Enemy(mesh, Matrix4x4.CreateTranslation(0, 0, -6), r, prey: prey));
        return mesh.Flattened();
    }

    [Fact]
    public void ItLeapsWhenTheSimMovesItAndSitsWhenItDoesnt()
    {
        // Ribbit.Hop: a leap on the half seconds where (PhaseSeconds * 2 + Id) is even, sat on the others.
        foreach (int id in new[] { 60, 61 })
        {
            float Height(double t)
            {
                var r = new Ribbit(id, 60);
                r.Restore(SpinePhase.Dormant, t, 3, Enemy.Loose, new Double3(0, 0, 0), 0, 0, 0, 1, 0);
                var v = Draw(r, new CreatureArt.Prey(new Vector3(0, 0, -12), Vector3.UnitZ));
                Assert.True(v.Min(p => p.Position.Y) > -0.05f, $"id {id} at {t}: nothing under the ground ({v.Min(p => p.Position.Y)})");
                return v.Average(p => p.Position.Y);
            }
            foreach (double t in new[] { 2.2, 3.2 })
            {
                bool leapsFirst = (int)(t * 2 + id) % 2 == 0;
                float first = Height(t), second = Height(t + 0.5);
                Assert.True(leapsFirst ? first > second + 0.1f : second > first + 0.1f,
                    $"id {id} at {t}: {(leapsFirst ? "leaping, then sat" : "sat, then leaping")}, but {first} then {second} m");
            }
        }
    }

    [Fact]
    public void OnItsCatchItsTongueIsOutToThem()
    {
        var r = new Ribbit(60, 60);
        r.Restore(SpinePhase.Grab, 1, 3, Enemy.Loose, new Double3(0, 0, 0), 0, 0, 0, 1, 0);
        // Its catch 3.5 m off, ahead of it.
        var feet = new Vector3(0, 0, -9.5f);
        var v = Draw(r, new CreatureArt.Prey(feet, Vector3.UnitZ));
        var chest = feet + Vector3.UnitY * 1.15f;
        Assert.InRange(v.Min(p => Vector3.Distance(p.Position, chest)), 0, 0.08f);
        // And a tongue's all the way: something drawn at every half metre between.
        for (float z = -7f; z > -9.3f; z -= 0.5f)
            Assert.Contains(v, p => MathF.Abs(p.Position.Z - z) < 0.3f && p.Position.Y > 0.3f);
        // Without anyone to reach, there's no tongue: the Ribbit's all there is.
        Assert.True(Draw(r).Min(p => p.Position.Z) > -7.6f);
    }
}
