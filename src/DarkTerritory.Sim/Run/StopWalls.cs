using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Run;

/// <summary>A building's wall, standing in the world: an upright box turned to the building's axis (horizontal).</summary>
public readonly record struct Wall(Double3 Centre, Double3 Axis, double HalfLength, double HalfWidth, double Bottom, double Top)
{
    /// <summary>A world point in the wall's own frame: x along its axis, z across, y as is.</summary>
    public Double3 ToLocal(Double3 p)
    {
        var d = p - Centre;
        return new Double3(d.X * Axis.X + d.Z * Axis.Z, p.Y, d.X * -Axis.Z + d.Z * Axis.X);
    }

    public Double3 ToWorld(Double3 local) =>
        new(Centre.X + local.X * Axis.X - local.Z * Axis.Z, local.Y, Centre.Z + local.X * Axis.Z + local.Z * Axis.X);

    /// <summary>A direction in the wall's own frame, turned back to the world's.</summary>
    public Double3 DirToWorld(Double3 local) =>
        new(local.X * Axis.X - local.Z * Axis.Z, local.Y, local.X * Axis.Z + local.Z * Axis.X);

    /// <summary>The wall's box in its own frame (heights as the world's).</summary>
    public Box Box => new(new Double3(-HalfLength, Bottom, -HalfWidth), new Double3(HalfLength, Top, HalfWidth));
}

/// <summary>
/// The stops' buildings and the fortresses' as walls a crewmate can't walk through and a ball stops at (T114 playtest:
/// "collisions"; T124: "fort buildings have no collision, and gun shots hit nothing"). A village's houses, barns and
/// outbuildings, each part of a house's footprint its own box; a dead town's station, goods shed and derelicts (note 302); a fortress's walls, gun towers, gatehouse and houses
/// (<see cref="Fortresses"/>). Not the yards' sheds (the crates are loaded at them), nor a Holdout's building (its occupant
/// comes back out of it), nor a well. Built from the route alike on every machine, so a client predicts walking into one
/// exactly as the host has it.
/// </summary>
public sealed class StopWalls
{
    const double Cell = 32;
    readonly List<Wall> _walls = [];
    readonly Dictionary<(int, int), List<int>> _cells = [];
    static readonly List<int> None = [];

    public IReadOnlyList<Wall> All => _walls;

    /// <summary>The walls that may touch a point (its cell's: each wall is listed in every cell it reaches).</summary>
    public IEnumerable<Wall> Near(Double3 p)
    {
        foreach (int i in _cells.TryGetValue(CellOf(p.X, p.Z), out var list) ? list : None)
            yield return _walls[i];
    }

    static (int, int) CellOf(double x, double z) => ((int)Math.Floor(x / Cell), (int)Math.Floor(z / Cell));

    /// <summary>Whether a stop's building stands as walls.</summary>
    public static bool Walled(StopLayout stop, int building) =>
        stop.Buildings[building].Kind is BuildingKind.House or BuildingKind.Barn or BuildingKind.Outbuilding
            or BuildingKind.Station or BuildingKind.GoodsShed or BuildingKind.Derelict
        && !stop.Holdouts.Any(h => h.Building == building);

    /// <summary>
    /// Where a find in a walled building is put out (the houses are shut, with no way in to search): on its step, a
    /// little out from the wall that faces the line, spread along it by <paramref name="slot"/>.
    /// </summary>
    public static Pt Doorstep(StopBuilding b, int slot)
    {
        // The building's own axis and across, in the stop's (S, D); which of its four faces looks most towards the line.
        double c = DMath.Cos(b.Yaw), s = DMath.Sin(b.Yaw);
        (double x, double y)[] faces = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        var best = faces.OrderBy(f => Math.Sign(b.D) * (f.x * s + f.y * c)).First();
        double along = (slot % 3 - 1) * 0.9;
        double x = best.x != 0 ? best.x * (b.Length / 2 + 0.8) : along, y = best.y != 0 ? best.y * (b.Width / 2 + 0.8) : along;
        // Axis u = (cos, sin), across v = (−sin, cos), in (S, D).
        return new Pt(b.S + x * c - y * s, b.D + x * s + y * c);
    }

    /// <param name="forts">The line's fortresses (<see cref="Fortresses.Of"/>); none when null.</param>
    public static StopWalls Of(Route.Route route, RailLine line, IReadOnlyList<Fort>? forts = null)
    {
        var walls = new StopWalls();
        foreach (var fort in forts ?? [])
            foreach (var w in Fortresses.Solids(fort, line))
                walls.Add(w);
        foreach (var f in route.Features)
        {
            if (f.Stop is not { } stop || f.Start < 0 || f.End > line.Length)
                continue;
            for (int i = 0; i < stop.Buildings.Count; i++)
            {
                if (!Walled(stop, i))
                    continue;
                var b = stop.Buildings[i];
                var parts = b.Parts.Count > 0 ? b.Parts : [new FootprintPart(0, 0, b.Length, b.Width)];
                foreach (var p in parts)
                {
                    var centre = Plan.World(b, p.X, p.Y);
                    var at = Run.StopWorld(line, f, centre);
                    var t = line.Sample(f.Start + centre.S);
                    var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
                    var tangent = new Double3(t.Tangent.X, 0, t.Tangent.Z).Normalized;
                    var axis = (tangent * DMath.Cos(b.Yaw) + right * DMath.Sin(b.Yaw)).Normalized;
                    walls.Add(new Wall(at with { Y = 0 }, axis, p.Length / 2, p.Width / 2, at.Y - 3, at.Y + 9));
                }
            }
        }
        return walls;
    }

    /// <summary>More walls standing beside the stops' (a fortress town's square: note 281).</summary>
    public void Add(IEnumerable<Wall> walls)
    {
        foreach (var w in walls)
            Add(w);
    }

    void Add(Wall w)
    {
        int index = _walls.Count;
        _walls.Add(w);
        double r = Math.Sqrt(w.HalfLength * w.HalfLength + w.HalfWidth * w.HalfWidth) + 1;
        var (x0, z0) = CellOf(w.Centre.X - r, w.Centre.Z - r);
        var (x1, z1) = CellOf(w.Centre.X + r, w.Centre.Z + r);
        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                if (!_cells.TryGetValue((x, z), out var list))
                    _cells[(x, z)] = list = [];
                list.Add(index);
            }
    }
}
