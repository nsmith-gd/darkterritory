using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Explorable village interiors (GDD App. F.3, the director, 7 Oct: "the villages don't have explorable interiors ...
/// things that we can search through in order to find things"; ARCHITECTURE §8 note 326): the plain houses stand open,
/// you walk in at the door in the face toward the line, and the finds are inside.
/// </summary>
[Collection(nameof(LineGenTests))]
public class OpenHouseTests
{
    static readonly string Content = DataFile.FindContentRoot();
    const double Crewmate = 0.3;

    static bool Blocked(StopWalls walls, Double3 p) =>
        walls.Near(p).Any(w => Math.Abs(w.ToLocal(p).X) <= w.HalfLength + Crewmate && Math.Abs(w.ToLocal(p).Z) <= w.HalfWidth + Crewmate);

    /// <summary>Every 10 cm from a to b, clear of every wall by a crewmate's half-width.</summary>
    static bool Walkable(StopWalls walls, Double3 a, Double3 b)
    {
        var d = (b - a) with { Y = 0 };
        for (double x = 0; x <= d.Length; x += 0.1)
            if (Blocked(walls, a + d.Normalized * x))
                return false;
        return true;
    }

    [Theory]
    [InlineData("frontier:7")]
    [InlineData("deadLines:2")]
    public void YouWalkInAtTheDoorAndTheFindsAreInside(string spec)
    {
        var route = Routes.Generate(Content, spec, 6);
        var line = route.Build();
        var walls = StopWalls.Of(route, line);
        int open = 0, inside = 0, shut = 0, furniture = 0;
        foreach (var f in route.Features.Where(f => f.Stop is not null))
        {
            var stop = f.Stop!;
            for (int i = 0; i < stop.Buildings.Count; i++)
            {
                var b = stop.Buildings[i];
                // The plain houses stand open; the L, cross and paired ones stay shut.
                Assert.Equal(b.Kind == BuildingKind.House && b.Shape is HouseShape.Rect or HouseShape.Square, b.Open);
                if (!b.Open || !StopWalls.Walled(stop, i))
                    continue;
                open++;
                // In at the door: from the step to the middle of the house, clear of its walls; and nowhere else.
                var step = Run.Run.StopWorld(line, f, StopWalls.Doorstep(b, 1));
                var middle = Run.Run.StopWorld(line, f, b.Centre);
                Assert.True(Walkable(walls, step, middle), $"{spec} at {f.Start:0}: house {i}'s door is shut");
                var back = Run.Run.StopWorld(line, f, StopWalls.Doorstep(b, 1) + (b.Centre - StopWalls.Doorstep(b, 1)) * 2.2);
                Assert.False(Walkable(walls, back, middle), $"{spec} at {f.Start:0}: house {i} has a way in at the back");
            }
            foreach (var c in stop.Containers.Where(c => c.Building >= 0 && stop.Buildings[c.Building].Kind == BuildingKind.House))
            {
                var b = stop.Buildings[c.Building];
                var at = StopWalls.FindAt(stop, c);
                if (b.Open)
                {
                    // Inside, out in the room, and you can walk from the step to just in front of it (within a search's reach:
                    // loot.json search.reach), round any furniture.
                    inside++;
                    Assert.True(Plan(b, at), $"{spec} at {f.Start:0}: a {c.Kind} find outside its open house");
                    var middle = Run.Run.StopWorld(line, f, b.Centre);
                    var (ix, iy) = StopWalls.InsideLocal(b, c.Kind, c.Index);
                    var (kx, ky, fx, fy) = StopWalls.Kept(b, c.Kind, c.Index);
                    var front = Run.Run.StopWorld(line, f, StopWalls.InHouse(b, ix + fx * 0.45, iy + fy * 0.45));
                    Assert.True(Walkable(walls, Run.Run.StopWorld(line, f, StopWalls.Doorstep(b, 1)), middle) && Walkable(walls, middle, front),
                        $"{spec} at {f.Start:0}: a {c.Kind} find in house {c.Building} can't be walked to");
                    // A cupboard or a cabinet is solid: nobody stands in it.
                    if (c.Kind is ContainerKind.Cupboard or ContainerKind.Cabinet)
                    {
                        furniture++;
                        Assert.True(Blocked(walls, Run.Run.StopWorld(line, f, StopWalls.InHouse(b, kx, ky))), $"{spec} at {f.Start:0}: walked through a {c.Kind}");
                    }
                }
                else
                {
                    shut++;
                    Assert.False(Plan(b, at), "a shut house's find is put out on its step");
                }
            }
        }
        Assert.True(open > 0 && inside > 0 && furniture > 0, $"{spec}: {open} open houses, {inside} finds inside, {furniture} cupboards and cabinets");
        Assert.True(shut > 0, $"{spec}: no shut house with a find (the L, cross and paired houses keep theirs on the step)");
    }

    /// <summary>Whether a point in the stop's frame is within a building's footprint (its own frame's rectangle).</summary>
    static bool Plan(StopBuilding b, Pt p)
    {
        double c = Math.Cos(b.Yaw), s = Math.Sin(b.Yaw);
        double dx = p.S - b.S, dy = p.D - b.D;
        double x = dx * c + dy * s, y = -dx * s + dy * c;
        return Math.Abs(x) < b.Length / 2 && Math.Abs(y) < b.Width / 2;
    }
}
