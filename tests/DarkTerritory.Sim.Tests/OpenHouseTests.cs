using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Explorable village interiors (GDD App. F.3, the director, 7 Oct: "the villages don't have explorable interiors ...
/// things that we can search through in order to find things"; ARCHITECTURE §8 note 326): the plain houses stand open,
/// you walk in at the door in the face toward the line, and the finds are inside. Every house: an L or a cross is one floor
/// in its outline's walls, a pair two cottages, a door each.
/// </summary>
[Collection(nameof(LineGenTests))]
public class OpenHouseTests
{
    static readonly string Content = DataFile.FindContentRoot();
    const double Crewmate = 0.3;

    static bool Blocked(StopWalls walls, Double3 p) =>
        walls.Near(p).Any(w => Math.Abs(w.ToLocal(p).X) <= w.HalfLength + Crewmate && Math.Abs(w.ToLocal(p).Z) <= w.HalfWidth + Crewmate);

    [Theory]
    [InlineData("frontier:7")]
    [InlineData("deadLines:2")]
    public void YouWalkInAtTheDoorAndTheFindsAreInside(string spec)
    {
        var route = Routes.Generate(Content, spec, 6);
        var line = route.Build();
        var walls = StopWalls.Of(route, line);
        int open = 0, composite = 0, pairs = 0, inside = 0, furniture = 0;
        foreach (var f in route.Features.Where(f => f.Stop is not null))
        {
            var stop = f.Stop!;
            for (int i = 0; i < stop.Buildings.Count; i++)
            {
                var b = stop.Buildings[i];
                // Every village house stands open, whatever its shape.
                Assert.Equal(b.Kind == BuildingKind.House, b.Open);
                if (!b.Open || !StopWalls.Walled(stop, i))
                    continue;
                open++;
                composite += StopWalls.Composite(b) ? 1 : 0;
                var doors = StopWalls.Doorways(b).ToList();
                // A unit a door: a pair of cottages has two.
                Assert.Equal(b.Shape == HouseShape.Pair ? 2 : 1, doors.Count);
                pairs += b.Shape == HouseShape.Pair ? 1 : 0;
                var walk = Reach(walls, line, f, b);
                // In at each door, from its step.
                foreach (var (o, n) in doors)
                {
                    Assert.True(walk.Reached(o.X, o.Y) && walk.Reached(n.X, n.Y), $"{spec} at {f.Start:0}: house {i}'s door is shut ({b.Shape})");
                    Assert.True(StopWalls.InParts(b, n.X, n.Y) && !StopWalls.InParts(b, o.X, o.Y), $"{spec} at {f.Start:0}: house {i}'s door isn't in its wall");
                }
                // And nowhere else: anywhere a crewmate goes from outside the house to inside it, it's at a door.
                foreach (var (x, y) in walk.Crossings)
                    Assert.True(doors.Any(d => Math.Abs(x - (d.Out.X + d.In.X) / 2) < 1.5 && Math.Abs(y - (d.Out.Y + d.In.Y) / 2) < 1.5),
                        $"{spec} at {f.Start:0}: house {i} ({b.Shape}) has a way in at ({x:0.0}, {y:0.0})");
                foreach (var c in stop.Containers.Where(c => c.Building == i))
                {
                    // Inside, and a crewmate gets to just in front of it (within a search's reach, loot.json search.reach), round
                    // the furniture; a cupboard or a cabinet is solid.
                    inside++;
                    var (ix, iy) = StopWalls.InsideLocal(b, c.Kind, c.Index);
                    var (kx, ky, fx, fy) = StopWalls.Kept(b, c.Kind, c.Index);
                    Assert.True(StopWalls.InParts(b, ix, iy) && StopWalls.InParts(b, kx, ky), $"{spec} at {f.Start:0}: a {c.Kind} outside its house {i} ({b.Shape})");
                    Assert.True(walk.Reached(ix + fx * 0.45, iy + fy * 0.45), $"{spec} at {f.Start:0}: a {c.Kind} in house {i} ({b.Shape}) can't be got to");
                    if (c.Kind is ContainerKind.Cupboard or ContainerKind.Cabinet)
                    {
                        furniture++;
                        Assert.False(walk.Reached(kx, ky), $"{spec} at {f.Start:0}: walked through a {c.Kind}");
                    }
                }
            }
        }
        Assert.True(open > 0 && composite > 0 && pairs > 0 && inside > 0 && furniture > 0,
            $"{spec}: {open} open houses ({composite} of parts, {pairs} pairs), {inside} finds inside, {furniture} cupboards and cabinets");
    }

    [Fact]
    public void EveryOpenHouseIsRansackedTheSameOnEveryMachine()
    {
        // The director, 8 Oct: "furniture scattered about, like the place has been ransacked many times before". Every open
        // house has its heavy furniture against a wall and things underfoot, from the stop's seed (twice over, the same); the
        // heavy pieces are walls (the walk-in test goes round them), the nest's ground is clear, and the stop is laid as before.
        var a = Routes.Generate(Content, "frontier:7", 6);
        var b2 = Routes.Generate(Content, "frontier:7", 6);
        int houses = 0, heavy = 0, loose = 0, nests = 0;
        foreach (var (f, g) in a.Features.Zip(b2.Features).Where(x => x.First.Stop is not null))
        {
            var stop = f.Stop!;
            for (int i = 0; i < stop.Buildings.Count; i++)
            {
                if (!stop.Buildings[i].Open || !StopWalls.Walled(stop, i))
                    continue;
                houses++;
                var clutter = StopWalls.ClutterOf(stop, i);
                Assert.Equal(clutter, StopWalls.ClutterOf(g.Stop!, i));
                heavy += clutter.Count(c => c.Solid);
                loose += clutter.Count(c => !c.Solid);
                Assert.True(clutter.Count(c => !c.Solid) >= 2, $"house {i} at {f.Start:0} is hardly touched");
                foreach (var c in clutter)
                    Assert.True(StopWalls.InParts(stop.Buildings[i], c.X, c.Y), $"a {c.Kind} outside house {i}");
                if (StopWalls.Nest(stop, i) is { } nest)
                {
                    nests++;
                    Assert.True(StopWalls.InParts(stop.Buildings[i], nest.X, nest.Y), $"house {i}'s nest is outside it");
                    Assert.All(clutter.Where(c => c.Solid), c => Assert.True(Math.Abs(c.X - nest.X) > c.Box.HalfX + 1 || Math.Abs(c.Y - nest.Y) > c.Box.HalfY + 1));
                }
            }
        }
        Assert.True(houses > 0 && heavy >= houses && loose >= 3 * houses && nests > 0, $"{houses} houses: {heavy} heavy, {loose} loose, {nests} nests");
    }

    /// <summary>Where a crewmate gets to from outside a house, on a 10 cm grid in its own frame.</summary>
    sealed record Walk(double X0, double Y0, bool[,] Grid, List<(double X, double Y)> Crossings)
    {
        const double Step = 0.1;
        public bool Reached(double x, double y)
        {
            int i = (int)Math.Round((x - X0) / Step), j = (int)Math.Round((y - Y0) / Step);
            return i >= 0 && j >= 0 && i < Grid.GetLength(0) && j < Grid.GetLength(1) && Grid[i, j];
        }

        public static double StepOf => Step;
    }

    static Walk Reach(StopWalls walls, Rail.RailLine line, Route.RouteFeature f, StopBuilding b)
    {
        // The stop's zone is straight and level: the house's frame to the world's is affine.
        var o = Run.Run.StopWorld(line, f, StopWalls.InHouse(b, 0, 0));
        var ex = Run.Run.StopWorld(line, f, StopWalls.InHouse(b, 1, 0)) - o;
        var ey = Run.Run.StopWorld(line, f, StopWalls.InHouse(b, 0, 1)) - o;
        double step = Walk.StepOf, x0 = -b.Length / 2 - 1.5, y0 = -b.Width / 2 - 1.5;
        int nx = (int)((b.Length + 3) / step) + 1, ny = (int)((b.Width + 3) / step) + 1;
        var free = new bool[nx, ny];
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
                free[i, j] = !Blocked(walls, o + ex * (x0 + i * step) + ey * (y0 + j * step));
        var reached = new bool[nx, ny];
        var queue = new Queue<(int, int)>();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
                if ((i == 0 || j == 0 || i == nx - 1 || j == ny - 1) && free[i, j])
                {
                    reached[i, j] = true;
                    queue.Enqueue((i, j));
                }
        var crossings = new List<(double, double)>();
        while (queue.Count > 0)
        {
            var (i, j) = queue.Dequeue();
            bool inHere = StopWalls.InParts(b, x0 + i * step, y0 + j * step);
            foreach (var (a, c) in new[] { (i + 1, j), (i - 1, j), (i, j + 1), (i, j - 1) })
            {
                if (a < 0 || c < 0 || a >= nx || c >= ny || !free[a, c] || reached[a, c])
                    continue;
                reached[a, c] = true;
                queue.Enqueue((a, c));
                if (StopWalls.InParts(b, x0 + a * step, y0 + c * step) && !inHere)
                    crossings.Add((x0 + a * step, y0 + c * step));
            }
        }
        return new Walk(x0, y0, reached, crossings);
    }
}
