using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Run;

/// <summary>A building's wall, standing in the world: an upright box turned to the building's axis (horizontal).</summary>
public readonly record struct Wall(Double3 Centre, Double3 Axis, double HalfLength, double HalfWidth, double Bottom, double Top)
{
    /// <summary>
    /// A fortress's (note 274): the crew and the balls meet it as any wall, but a creature doesn't need holding out of one (note
    /// 279): a fort drives off whatever comes into it (note 273), and a pack held at a gate pier never came in to be driven off.
    /// </summary>
    public bool Fort { get; init; }

    /// <summary>Which stop building it's part of (1 up, alike on every machine; 0 for none): all of a building's walls share its axis.</summary>
    public int Owner { get; init; }

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
/// "collisions"; T124: "fort buildings have no collision, and gun shots hit nothing"; the director's "the world is solid",
/// note 279). Shut buildings stand as their footprint: a village's houses (each part of a footprint its own box), barns
/// and outbuildings, a dead town's station, goods shed and derelicts (note 302), a yard's powerhouse, the signal boxes,
/// lamp rooms, water towers, lockups and prison cars, and the wells. An open house stands as its four walls with a door
/// (note 326). A yard's sheds and its hero stand as their walls, with the bay door the art draws to the track each serves
/// and a gantry's cut open to its track: the crates inside are fetched through the door. A Holdout stands as its walls with
/// its door open where the crew breaches it: its occupant comes back inside (GDD App. D.14) and walks out. A fortress's
/// walls, gun towers, gatehouse and houses (<see cref="Fortresses"/>). Built from the route alike on every machine, so a
/// client predicts walking into one exactly as the host has it.
/// </summary>
public sealed partial class StopWalls
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

    /// <summary>Whether a stop's building stands shut (no way in: a find in it is put on its doorstep).</summary>
    public static bool Walled(StopLayout stop, int building) =>
        stop.Buildings[building].Kind is BuildingKind.House or BuildingKind.Barn or BuildingKind.Outbuilding
            or BuildingKind.Station or BuildingKind.GoodsShed or BuildingKind.Derelict or BuildingKind.Powerhouse
            or BuildingKind.SignalBox or BuildingKind.LampRoom or BuildingKind.WaterTower or BuildingKind.Lockup or BuildingKind.PrisonCar
            or BuildingKind.Well
        && !stop.Holdouts.Any(h => h.Building == building);

    /// <summary>Whether a stop's building stands as its walls with a door into it (a yard's shed or hero, a Holdout; note 279).</summary>
    public static bool Shelled(StopLayout stop, int building) =>
        stop.Buildings[building].Kind is BuildingKind.Shed or BuildingKind.Hero || stop.Holdouts.Any(h => h.Building == building);

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
        if (Composite(b))
        {
            foreach (var w in CompositeWalls(b))
                yield return w;
            yield break;
        }
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
        if (Composite(b))
        {
            var k = KeptComposite(b, kind, index);
            return (k.FindX, k.FindY);
        }
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
        if (Composite(b))
        {
            var k = KeptComposite(b, kind, index);
            return (k.X, k.Y, k.FaceX, k.FaceY);
        }
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
        foreach (var c in kept)
        {
            if (c.Kind is not (ContainerKind.Cupboard or ContainerKind.Cabinet))
                continue;
            // Its back to the wall it faces away from: its depth along the way it faces.
            var (x, y, faceX, _) = Kept(b, c.Kind, c.Index);
            (double depth, double width) = c.Kind == ContainerKind.Cupboard ? (CupboardDepth, CupboardWidth) : (CabinetDepth, CabinetWidth);
            bool deepAlongX = Math.Abs(faceX) > 0.5;
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

    /// <summary>A door in a building's wall, in its own frame: across the side <c>Side</c> (±1) or the end <c>End</c> (±1), centred at <c>At</c> along that face.</summary>
    public readonly record struct Door(int Side, int End, double At, double Width);

    /// <summary>
    /// A shelled building's doors (note 279), in its own frame (x along its axis, y across): a shed's or the hero's bay door
    /// in the middle of each roofed length, on the side to the track it serves (as StructureKit.Shed draws it); a Holdout's
    /// door on the face it's breached at.
    /// </summary>
    public static IEnumerable<Door> Doors(StopLayout stop, int building, WallTuning t)
    {
        var b = stop.Buildings[building];
        if (stop.Holdouts.FirstOrDefault(h => h.Building == building) is { } holdout)
        {
            var (x, y) = Plan.Local(holdout.Door, b);
            yield return Math.Abs(x) / b.Length > Math.Abs(y) / b.Width ? new Door(0, Math.Sign(x), Math.Clamp(y, -b.Width / 2, b.Width / 2), t.PersonDoorM)
                : new Door(Math.Sign(y), 0, Math.Clamp(x, -b.Length / 2, b.Length / 2), t.PersonDoorM);
            yield break;
        }
        int door = DoorSide(stop, b);
        foreach (var (lo, hi) in Roofed(stop, building))
            yield return new Door(door, 0, (lo + hi) / 2, t.BayDoorM);
    }

    /// <summary>The side of a shed its doors face: the track it serves (its first), or the main line.</summary>
    static int DoorSide(StopLayout stop, StopBuilding b) => (b.Tracks.Count > 0 ? stop.Tracks[b.Tracks[0]].FaceStart.D : 0) >= b.D ? 1 : -1;

    /// <summary>A shed's roofed lengths along its axis: the whole of it, or either side of a gantry's cut (3 m or more).</summary>
    static IEnumerable<(double Lo, double Hi)> Roofed(StopLayout stop, int building)
    {
        var b = stop.Buildings[building];
        double half = b.Length / 2;
        (double, double)[] spans = CraneCut(stop, building) is { } g ? [(-half, g.From - b.S), (g.To - b.S, half)] : [(-half, half)];
        foreach (var (g0, g1) in spans)
            if (Math.Min(g1, half) - Math.Max(g0, -half) >= 3)
                yield return (Math.Max(g0, -half), Math.Min(g1, half));
    }

    /// <summary>
    /// A shelled building's walls as boxes in its own frame (x along its axis, y across), each with its height over the
    /// ground: four walls round each roofed length (a shed either side of a gantry's cut), its door left open (<see cref="Doors"/>);
    /// the cut left open (the gantry's legs and its operator's stand are in it).
    /// </summary>
    public static IEnumerable<(FootprintPart Part, double Top)> Shell(StopLayout stop, int building, WallTuning t)
    {
        var b = stop.Buildings[building];
        var doors = Doors(stop, building, t).ToList();
        bool holdout = stop.Holdouts.Any(h => h.Building == building);
        double th = t.WallM;
        // A wall from a to b along a face, less any door on it.
        IEnumerable<(FootprintPart, double)> Face(int side, int end, double a, double c, double fixedAt)
        {
            var gaps = doors.Where(d => d.Side == side && d.End == end && d.At > a && d.At < c).OrderBy(d => d.At).ToList();
            double from = a;
            foreach (var d in gaps.Append(new Door(side, end, c + 1e9, 0)))
            {
                double to = Math.Min(c, d.At - d.Width / 2);
                if (to - from > 0.05)
                    yield return (side != 0 ? new FootprintPart((from + to) / 2, fixedAt, to - from, th) : new FootprintPart(fixedAt, (from + to) / 2, th, to - from), t.TopM);
                from = Math.Max(from, d.At + d.Width / 2);
            }
        }
        double w2 = b.Width / 2, sideAt = w2 - th / 2;
        var rooms = holdout ? [(-b.Length / 2, b.Length / 2)] : Roofed(stop, building).ToList();
        foreach (var (lo, hi) in rooms)
        {
            foreach (int side in new[] { -1, 1 })
                foreach (var wall in Face(side, 0, lo, hi, side * sideAt))
                    yield return wall;
            // The ends: a holdout's door may be on one (its own frame's y along the end).
            foreach (var wall in Face(0, -1, -w2, w2, lo + th / 2))
                yield return wall;
            foreach (var wall in Face(0, 1, -w2, w2, hi - th / 2))
                yield return wall;
        }
        // A gantry's cut stays open: its legs stand in it, the operator at the foot of one (Crane.Controls), and the castings
        // lie in the open under the hook. The art's low wall behind it is drawn, not stood.
    }

    /// <summary>Where a yard gantry's runway cuts through a shed (zone S, its legs either side), as the art lays it; null if none does.</summary>
    static (double From, double To)? CraneCut(StopLayout stop, int building)
    {
        foreach (var c in stop.Containers)
            if (c.Kind == ContainerKind.CraneBay && c.Building == building && c.Track >= 0 && stop.Tracks[c.Track].Crane is { } rw)
                return (rw.From - 1.5, rw.To + 1.5);
        return null;
    }

    /// <param name="forts">The line's fortresses (<see cref="Fortresses.Of"/>); none when null.</param>
    /// <param name="tuning">run.json's <c>walls</c> (note 279); the defaults when null.</param>
    public static StopWalls Of(Route.Route route, RailLine line, IReadOnlyList<Fort>? forts = null, WallTuning? tuning = null)
    {
        var t = tuning ?? new WallTuning();
        var walls = new StopWalls();
        foreach (var fort in forts ?? [])
            foreach (var w in Fortresses.Solids(fort, line))
                walls.Add(w with { Fort = true });
        int owner = 0;
        foreach (var f in route.Features)
        {
            if (f.Stop is not { } stop || f.Start < 0 || f.End > line.Length)
                continue;
            for (int i = 0; i < stop.Buildings.Count; i++)
            {
                var b = stop.Buildings[i];
                owner++;
                // An open house stands as its four walls with a door, its cupboards and cabinets, and the heavy furniture a
                // ransack left against its walls (note 326); the rest of the shut ones as their footprints' boxes; a shed, the hero
                // and a Holdout as their shells by their doors (note 279).
                int index = i;
                IEnumerable<(double X, double Y, double HalfX, double HalfY, double Top)> boxes = !Walled(stop, i)
                    ? Shelled(stop, i) ? Shell(stop, i, t).Select(w => (w.Part.X, w.Part.Y, w.Part.Length / 2, w.Part.Width / 2, w.Top)) : []
                    : b.Open
                        ? OpenWalls(b).Concat(Furniture(b, stop.Containers.Where(c => c.Building == index)).Select(x => (x.X, x.Y, x.HalfX, x.HalfY)))
                            .Concat(ClutterOf(stop, index).Where(x => x.Solid).Select(x => (x.X, x.Y, x.Box.HalfX, x.Box.HalfY)))
                            .Select(w => (w.X, w.Y, w.HalfX, w.HalfY, t.TopM))
                        : (b.Parts.Count > 0 ? b.Parts : [new FootprintPart(0, 0, b.Length, b.Width)])
                            .Select(p => (p.X, p.Y, p.Length / 2, p.Width / 2, b.Kind == BuildingKind.Well ? t.WellTopM : t.TopM));
                foreach (var (x, y, hx, hy, top) in boxes)
                {
                    var centre = Plan.World(b, x, y);
                    var at = Run.StopWorld(line, f, centre);
                    var tg = line.Sample(f.Start + centre.S);
                    var right = Double3.Cross(tg.Tangent, Double3.Up).Normalized;
                    var tangent = new Double3(tg.Tangent.X, 0, tg.Tangent.Z).Normalized;
                    var axis = (tangent * DMath.Cos(b.Yaw) + right * DMath.Sin(b.Yaw)).Normalized;
                    walls.Add(new Wall(at with { Y = 0 }, axis, hx, hy, at.Y - 3, at.Y + top) { Owner = owner });
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

    /// <summary>
    /// These walls less every stop building that has one of <paramref name="points"/> in it or within <paramref name="reach"/>
    /// of it (note 279): a facility's modules are laid from the spur, not round the stop's buildings, and where one stands in a
    /// building's footprint (a crane's stand in a shed) the building gives way, so the crew can work it. A fortress's stand.
    /// </summary>
    public StopWalls Clear(IEnumerable<Double3> points, double reach)
    {
        var at = points.ToList();
        var gone = new HashSet<int>();
        foreach (var group in _walls.Where(w => w.Owner > 0 && !w.Fort).GroupBy(w => w.Owner))
        {
            // The building's footprint, in its first wall's frame (they all share its axis).
            var w0 = group.First();
            double x0 = double.MaxValue, x1 = double.MinValue, z0 = double.MaxValue, z1 = double.MinValue;
            foreach (var w in group)
            {
                var c = w0.ToLocal(w.Centre);
                bool along = Math.Abs(w.Axis.X * w0.Axis.X + w.Axis.Z * w0.Axis.Z) > 0.5;
                double hx = along ? w.HalfLength : w.HalfWidth, hz = along ? w.HalfWidth : w.HalfLength;
                (x0, x1, z0, z1) = (Math.Min(x0, c.X - hx), Math.Max(x1, c.X + hx), Math.Min(z0, c.Z - hz), Math.Max(z1, c.Z + hz));
            }
            if (at.Any(p => w0.ToLocal(p) is var l && l.X > x0 - reach && l.X < x1 + reach && l.Z > z0 - reach && l.Z < z1 + reach))
                gone.Add(group.Key);
        }
        var kept = new StopWalls();
        foreach (var w in _walls)
            if (w.Fort || (w.Owner > 0 ? !gone.Contains(w.Owner) : !at.Any(p => Within(w, p, reach))))
                kept.Add(w);
        return kept;
    }

    static bool Within(Wall w, Double3 p, double reach)
    {
        var l = w.ToLocal(p);
        double dx = Math.Max(0, Math.Abs(l.X) - w.HalfLength), dz = Math.Max(0, Math.Abs(l.Z) - w.HalfWidth);
        return dx * dx + dz * dz < reach * reach;
    }

    /// <summary>Walls as given (a test's, a mod's).</summary>
    public static StopWalls Of(IEnumerable<Wall> walls)
    {
        var all = new StopWalls();
        foreach (var w in walls)
            all.Add(w);
        return all;
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
