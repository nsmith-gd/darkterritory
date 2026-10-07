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
        double c = DMath.Cos(b.Yaw), s = DMath.Sin(b.Yaw);
        var best = Front(b);
        double along = (slot % 3 - 1) * 0.9;
        double x = best.X != 0 ? best.X * (b.Length / 2 + 0.8) : along, y = best.Y != 0 ? best.Y * (b.Width / 2 + 0.8) : along;
        // Axis u = (cos, sin), across v = (−sin, cos), in (S, D).
        return new Pt(b.S + x * c - y * s, b.D + x * s + y * c);
    }

    /// <summary>
    /// Which of a building's four faces looks most towards the line, in its own frame (x along its axis, y across): (±1, 0)
    /// an end, (0, ±1) a side. An open house's door is in it, and a shut one's finds are put out on its step.
    /// </summary>
    public static (double X, double Y) Front(StopBuilding b)
    {
        // The building's own axis and across, in the stop's (S, D).
        double c = DMath.Cos(b.Yaw), s = DMath.Sin(b.Yaw);
        (double x, double y)[] faces = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        return faces.OrderBy(f => Math.Sign(b.D) * (f.x * s + f.y * c)).First();
    }

    /// <summary>
    /// An open house's door and walls (note 326): the doorway's width and the walls' thickness (m). Not design numbers:
    /// a cottage's front door, and walls a lamp doesn't shine through.
    /// </summary>
    public const double DoorWidth = 1.2, WallThickness = 0.2;

    /// <summary>How far in from a wall's inner face a find lies, at the foot of the cupboard or cabinet against it (m).</summary>
    public const double FindOut = 0.65;

    /// <summary>
    /// An open house's walls as boxes in its own frame (x along its axis, y across; middles and half sizes): the four walls,
    /// the one in its <see cref="Front"/> split either side of the door.
    /// </summary>
    public static IEnumerable<(double X, double Y, double HalfX, double HalfY)> OpenWalls(StopBuilding b)
    {
        double hx = b.Length / 2, hy = b.Width / 2, t = WallThickness, door = DoorWidth / 2;
        var (fx, fy) = Front(b);
        // A wall from a to b along its run, less the doorway if it's the front.
        static IEnumerable<(double A, double B)> Runs(double a, double b, bool front, double door) =>
            front ? [(a, -door), (door, b)] : [(a, b)];
        foreach (int sx in new[] { 1, -1 })
            foreach (var (a, b2) in Runs(-hy, hy, fx == sx, door))
                yield return (sx * (hx - t / 2), (a + b2) / 2, t / 2, (b2 - a) / 2);
        foreach (int sy in new[] { 1, -1 })
            foreach (var (a, b2) in Runs(-hx + t, hx - t, fy == sy, door))
                yield return ((a + b2) / 2, sy * (hy - t / 2), (b2 - a) / 2, t / 2);
    }

    /// <summary>
    /// Where a find is kept inside an open house, in its own frame (note 326): a cupboard against the back wall, a cabinet
    /// against a side wall, the cellar's hatch and the loose floorboards out on the floor, a second of a kind across the
    /// room from the first. Never in the doorway.
    /// </summary>
    public static (double X, double Y) InsideLocal(StopBuilding b, ContainerKind kind, int index)
    {
        var (fx, fy) = Front(b);
        // Out in the room in front of what it was kept in, which stands between it and the wall.
        double margin = WallThickness + FindOut;
        double ef = (fx != 0 ? b.Length : b.Width) / 2 - margin, es = (fx != 0 ? b.Width : b.Length) / 2 - margin;
        double sign = index % 2 == 0 ? 1 : -1;
        var (f, s) = kind switch
        {
            ContainerKind.Cupboard => (-ef, es * 0.5 * sign),
            ContainerKind.Cabinet => (-ef * 0.3, es * sign),
            ContainerKind.Cellar => (-ef * 0.4, -es * 0.45 * sign),
            ContainerKind.UnderFloor => (ef * 0.15, es * 0.35 * sign),
            _ => (0.0, 0.0),
        };
        // Along the front's outward direction f, across it s (a quarter turn from it).
        return (f * fx - s * fy, f * fy + s * fx);
    }

    /// <summary>
    /// What a find in an open house is kept in, in the house's own frame (note 326): the middle of the cupboard on the back
    /// wall or the cabinet on a side wall (each with its find out in front of it), or the hatch or the boards (under it), and
    /// the way it faces into the room (a unit vector). The art stands the furniture here, and a searched one is drawn opened.
    /// </summary>
    public static (double X, double Y, double FaceX, double FaceY) Kept(StopBuilding b, ContainerKind kind, int index)
    {
        var (x, y) = InsideLocal(b, kind, index);
        var (fx, fy) = Front(b);
        double sign = index % 2 == 0 ? 1 : -1;
        return kind switch
        {
            ContainerKind.Cupboard => (x - fx * CupboardBack, y - fy * CupboardBack, fx, fy),
            ContainerKind.Cabinet => (x - fy * sign * CabinetBack, y + fx * sign * CabinetBack, fy * sign, -fx * sign),
            _ => (x, y, fx, fy),
        };
    }

    /// <summary>
    /// A cupboard's and a cabinet's half sizes (m): their depth out from the wall they stand against, and their width along
    /// it. Not design numbers: a kitchen dresser and a sideboard.
    /// </summary>
    public const double CupboardDepth = 0.25, CupboardWidth = 0.5, CabinetDepth = 0.22, CabinetWidth = 0.42;

    /// <summary>How far behind its find a cupboard's and a cabinet's middles stand (m): its find lies just out from its face.</summary>
    public const double CupboardBack = FindOut - CupboardDepth, CabinetBack = FindOut - CabinetDepth;

    /// <summary>
    /// The furniture standing in an open house, as boxes in its own frame (middles and half sizes, x along its axis, y
    /// across): a cupboard or a cabinet for each of <paramref name="kept"/> that's one, against its wall. Solid, so a crewmate
    /// goes round it; and the art's, so what's drawn is what's walked into.
    /// </summary>
    public static IEnumerable<(ContainerKind Kind, double X, double Y, double HalfX, double HalfY)> Furniture(StopBuilding b, IEnumerable<StopContainer> kept)
    {
        bool endOn = Front(b).X != 0;
        foreach (var c in kept)
        {
            if (c.Kind is not (ContainerKind.Cupboard or ContainerKind.Cabinet))
                continue;
            var (x, y, _, _) = Kept(b, c.Kind, c.Index);
            // A cupboard's back is to the back wall (across the house from the door), a cabinet's to a side wall.
            (double depth, double width) = c.Kind == ContainerKind.Cupboard ? (CupboardDepth, CupboardWidth) : (CabinetDepth, CabinetWidth);
            bool deepAlongX = c.Kind == ContainerKind.Cupboard ? endOn : !endOn;
            yield return (c.Kind, x, y, deepAlongX ? depth : width, deepAlongX ? width : depth);
        }
    }

    /// <summary>A stop-frame point in an open house's own frame, out to the world's (for the art and the hiding spots).</summary>
    public static Pt InHouse(StopBuilding b, double x, double y) => Plan.World(b, x, y);

    /// <summary>Where a stop's container's find is put out: inside an open house, on a shut one's step, else where it is.</summary>
    public static Pt FindAt(StopLayout stop, StopContainer c)
    {
        if (c.Building < 0 || c.Building >= stop.Buildings.Count)
            return c.At;
        var b = stop.Buildings[c.Building];
        if (Walled(stop, c.Building) && b.Open)
        {
            var (x, y) = InsideLocal(b, c.Kind, c.Index);
            return Plan.World(b, x, y);
        }
        return Walled(stop, c.Building) ? Doorstep(b, c.Index) : c.At;
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
                // An open house stands as its four walls with a door, and its cupboards and cabinets (note 326); the rest as
                // their footprints' boxes.
                int index = i;
                List<(double X, double Y, double HalfX, double HalfY)> boxes = b.Open
                    ? [.. OpenWalls(b), .. Furniture(b, stop.Containers.Where(c => c.Building == index)).Select(x => (x.X, x.Y, x.HalfX, x.HalfY))]
                    : [.. (b.Parts.Count > 0 ? b.Parts : [new FootprintPart(0, 0, b.Length, b.Width)]).Select(p => (p.X, p.Y, p.Length / 2, p.Width / 2))];
                foreach (var (x, y, hx, hy) in boxes)
                {
                    var centre = Plan.World(b, x, y);
                    var at = Run.StopWorld(line, f, centre);
                    var t = line.Sample(f.Start + centre.S);
                    var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
                    var tangent = new Double3(t.Tangent.X, 0, t.Tangent.Z).Normalized;
                    var axis = (tangent * DMath.Cos(b.Yaw) + right * DMath.Sin(b.Yaw)).Normalized;
                    walls.Add(new Wall(at with { Y = 0 }, axis, hx, hy, at.Y - 3, at.Y + 9));
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
