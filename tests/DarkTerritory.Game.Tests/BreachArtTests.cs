using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// ARCHITECTURE §8 note 464: a Holdout's breach seen where it's worked (App. D.7), from the sim's replicated progress and on
/// the breach clips' own beats: a struck lock jumps and sparks at each blow and hangs more bent as the breach goes on, and a
/// barricade's boards come away one at a time.
/// </summary>
public class BreachArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);

    /// <summary>Frontier:7's Holdouts, the art's view of one of them breached <paramref name="breach"/> of the way (−1: shut,
    /// nobody at it) at the scene's <paramref name="time"/>: what's drawn within 3 m of its door, relative to the door.</summary>
    static List<Vertex> At(HoldoutKind kind, double breach, double time, bool quiet = false)
    {
        var night = Sim.LineGen.Routes.Generate(Content, "frontier:7", 6);
        var line = night.Build();
        var holdouts = new Holdouts(DataFile.Load<HoldoutTuning>(Path.Combine(Content, HoldoutTuning.File)), night, line);
        var h = holdouts.All.First(x => x.Layout.Kind == kind);
        foreach (var o in holdouts.All)
            holdouts.Mirror(o.Index, HoldoutState.Occupied, 1, 0);
        if (breach >= 0)
        {
            holdouts.Mirror(h.Index, HoldoutState.Breaching, 1, 0, quiet);
            holdouts.Mirror(h.Index, HoldoutState.Breaching, 1, breach * h.Breach(holdouts.Tuning).Seconds, quiet);
        }
        var mesh = new MeshBuilder();
        Look.Art.World.Entrances(mesh, line, night, holdouts, h.Door, 18, time);
        return [.. mesh.Vertices.ToArray().Where(v => (v.Position with { Y = 0 }).Length() < 3)];
    }

    [Fact]
    public void AStruckLockJumpsAndSparksOnTheBlowAndBendsAsTheBreachGoesOn()
    {
        // crew_clips' smash lands its blow 0.3 s into each 0.8 s; a third of a second on, its sparks are gone.
        var blow = At(HoldoutKind.PrisonCar, 0.5, 0.3 + 0.8 * 3);
        var after = At(HoldoutKind.PrisonCar, 0.5, 0.75 + 0.8 * 3);
        var shut = At(HoldoutKind.PrisonCar, -1, 0.3);
        Assert.Contains(blow, v => v.Emissive >= 1);
        Assert.DoesNotContain(after, v => v.Emissive >= 1);
        Assert.DoesNotContain(shut, v => v.Emissive >= 1);
        // Further into the breach, between blows, it hangs lower on the bent hasp.
        float Lowest(List<Vertex> vs, List<Vertex> but) => vs.Where(v => !but.Any(b => Vector3.Distance(b.Position, v.Position) < 1e-4f)).Min(v => v.Position.Y);
        var early = At(HoldoutKind.PrisonCar, 0.1, 0.75);
        var late = At(HoldoutKind.PrisonCar, 0.9, 0.75);
        Assert.True(Lowest(late, shut) < Lowest(early, shut) - 0.02f, $"the lock hangs at {Lowest(early, shut):0.000} early and {Lowest(late, shut):0.000} late");
        // Picked with the kit, quiet: no sparks, ever.
        Assert.DoesNotContain(At(HoldoutKind.PrisonCar, 0.5, 0.3, quiet: true), v => v.Emissive >= 1);
    }

    [Fact]
    public void ABarricadesBoardsComeAwayOneAtATimeAsItsPried()
    {
        // What stands up in the doorway (the boards across it) against what lies before it: fewer boards up, more down, as
        // the pry goes on, and the one being worked stands out from the wall.
        static (int Up, int Down) Boards(List<Vertex> vs)
        {
            float ground = vs.Min(v => v.Position.Y);
            return (vs.Count(v => v.Position.Y > ground + 0.5f), vs.Count(v => v.Position.Y < ground + 0.25f));
        }
        var counts = new[] { -1, 0.1, 0.5, 0.9 }.Select(b => Boards(At(HoldoutKind.Shelter, b, 0.2))).ToList();
        for (int i = 1; i < counts.Count; i++)
        {
            Assert.True(counts[i].Up < counts[i - 1].Up || i == 1, $"at step {i}: {counts[i].Up} up after {counts[i - 1].Up}");
            Assert.True(counts[i].Down >= counts[i - 1].Down, $"at step {i}: {counts[i].Down} down after {counts[i - 1].Down}");
        }
        Assert.True(counts[^1].Up < counts[0].Up && counts[^1].Down > counts[0].Down);
    }
}
