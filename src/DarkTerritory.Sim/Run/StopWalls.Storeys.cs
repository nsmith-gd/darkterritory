using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Run;

/// <summary>What a piece of a storeyed house's shell is (note 1005): the art's material for it (<see cref="Roof"/>: a town house's porch roof, note 1022).</summary>
public enum ShellPart : byte { Wall, Lintel, Plinth, Step, Floor, Cellar, Tread, Upper, Rail, Roof }

/// <summary>A piece of a storeyed house's shell, in its own frame: its middle and half sizes, its bottom and top over its ground.</summary>
public readonly record struct ShellBox(ShellPart Part, double X, double Y, double HalfX, double HalfY, double Bottom, double Top);

/// <summary>
/// The houses with more than one floor (queue #710, ARCHITECTURE §8 note 1005; the director, 23 Oct 2026: "more complex houses
/// that have multiple levels, including basements. Those basements should get pretty dark. They should have windows that you
/// can escape from, especially. Maybe a couple doors"). An open plain house (<see cref="StopGenerator.Storeyed"/>) built as
/// one of <see cref="HouseForm"/>: one room on the ground floor (no back room: the floors are its rooms), a front door and a
/// back door (in the end wall away from the front, or the variant's end when the front door's in a side), and:
/// <list type="bullet">
/// <item>An upstairs: a straight flight of <see cref="Rises"/> along the side wall away from the door, from near the front
/// up toward the back, each tread a step the motor takes (under player.json's stepUp both ways); the upper floor a slab with
/// the stairwell open over the flight, railed along its open side and its front end.</item>
/// <item>A basement: the house raised <see cref="Plinth"/> on its plinth, steps up to each door; the ground floor a slab, a
/// hatch let into it in the back corner away from the stair, a ladder down it to the basement's floor <see cref="Cellar"/>
/// under the boards; two cellar windows high in the side walls just over the outside ground, each with crates stacked
/// under it to climb, so a crewmate climbs out of one onto the ground outside (a world ladder, as the houseboats' are,
/// note 973: its top at the sill, over it and out), or back in.</item>
/// </list>
/// Inside a basement's footprint the earth isn't what a body stands on (<see cref="Under"/>): the basement's floor is. Heights
/// here are over the house's ground (its middle's, as the stop's level zone has it). Not design numbers: a cottage's.
/// </summary>
public sealed partial class StopWalls
{
    /// <summary>A house with a basement stands this high on its plinth (its ground floor's boards over the ground; m).</summary>
    public const double Plinth = 0.9;

    /// <summary>Floor to floor (m), a slab's thickness, and a basement's depth under the ground floor's boards.</summary>
    public const double Storey = 2.8, Slab = 0.25, Cellar = 2.5;

    /// <summary>A doorway's height (m), the lintel over it.</summary>
    public const double Lintel = 2.15;

    /// <summary>
    /// The flight up: its rises (the last onto the upper floor), each tread's depth, its width (m). A tread deeper than a
    /// crewmate is round (player.json radius 0.3), or the one after next is in their way before they're on the next.
    /// </summary>
    public const int Rises = 10;

    /// <inheritdoc cref="Rises"/>
    public const double Tread = 0.4, StairWidth = 1.0;

    /// <summary>How far the flight's foot stands in from the front end's wall (m): room to come at it.</summary>
    public const double StairFoot = 0.9;

    /// <summary>The hatch down (square, m), how far in from the corner's walls it is, and its ladder's step off over the top.</summary>
    public const double HatchSize = 0.9, HatchIn = 0.2, HatchOnto = 0.6;

    /// <summary>A cellar window: its width, its sill and head over the outside ground (m), and its step off outside.</summary>
    public const double CellarWindow = 0.9, WindowSill = 0.05, WindowHead = 0.65, WindowOnto = 0.75;

    /// <summary>The steps up to a raised house's doors: how many (the last onto the threshold), each tread's depth (m).</summary>
    public const int DoorSteps = 3;

    /// <inheritdoc cref="DoorSteps"/>
    public const double DoorStepTread = 0.32;

    /// <summary>A rail's height over the floor (m) and half its thickness.</summary>
    public const double RailHeight = 1.0, RailHalf = 0.04;

    /// <summary>How high a piece of furniture stands as a wall (m): under a ceiling, so it's not stood up through the floor over it.</summary>
    public const double FurnitureTop = 2.0;

    /// <summary>Whether an open house is built with more than one floor (note 1005).</summary>
    public static bool Storeyed(StopBuilding b) => b.Form != HouseForm.Plain && StopGenerator.Storeyed(b);

    /// <summary>A storeyed house's floor's boards over its ground (m): −1 the basement's, 0 the ground floor's, 1 upstairs.</summary>
    public static double FloorAt(StopBuilding b, int level) => (b.Basement ? Plinth : 0) + level switch { < 0 => -Cellar, > 0 => Storey, _ => 0 };

    /// <summary>
    /// How high over its house's ground a stop's container's find lies (note 1005): on its floor's boards in a storeyed house
    /// (down in the basement, up the stairs, or on a raised ground floor), or a house of parts' (note 1022), else on the ground (0).
    /// </summary>
    public static double FindUp(StopLayout stop, StopContainer c) =>
        c.Building >= 0 && c.Building < stop.Buildings.Count && stop.Buildings[c.Building] is { Open: true } b && Floored(b) && Walled(stop, c.Building)
            ? LevelAt(b, c.Level) : 0;


    /// <summary>The way along a storeyed house's axis to its back end (±1): the back door's in it.</summary>
    public static int BackEnd(StopBuilding b)
    {
        var (fx, _) = Front(b);
        return fx != 0 ? -(int)fx : b.Variant % 2 == 0 ? 1 : -1;
    }

    /// <summary>The side (±1, across its axis) a storeyed house's stair is against: away from the door in a side, else the variant's.</summary>
    public static int StairSide(StopBuilding b)
    {
        var (_, fy) = Front(b);
        return fy != 0 ? -(int)fy : b.Variant / 2 % 2 == 0 ? 1 : -1;
    }

    /// <summary>The middle of a storeyed house's back doorway, on its outside edge, and its way out (its own frame).</summary>
    public static (double X, double Y, double OutX, double OutY) BackDoor(StopBuilding b)
    {
        int e = BackEnd(b);
        return (e * b.Length / 2, 0, e, 0);
    }

    /// <summary>
    /// The flight up, in the house's own frame: where it starts (x of its first tread's front edge), the way it climbs (±1 along
    /// x), and its side (y from the wall's inner face to its open edge).
    /// </summary>
    public static (double X0, int Dir, double Y0, double Y1) Stair(StopBuilding b)
    {
        int e = BackEnd(b), s = StairSide(b);
        double t = WallThickness, inner = b.Width / 2 - t;
        double x0 = -e * (b.Length / 2 - t - StairFoot);
        return (x0, e, s * inner, s * (inner - StairWidth));
    }

    /// <summary>The hatch down to the basement, in the house's own frame: its middle, and the ladder's way off at the top (into the room).</summary>
    public static (double X, double Y, double InX, double InY) Hatch(StopBuilding b)
    {
        int e = BackEnd(b), s = StairSide(b);
        double t = WallThickness, off = t + HatchIn + HatchSize / 2;
        return (e * (b.Length / 2 - off), -s * (b.Width / 2 - off), 0, s);
    }

    /// <summary>The cellar windows, in the house's own frame: the middle of each on its wall's inside face, and the way out through it.</summary>
    public static IEnumerable<(double X, double Y, double OutX, double OutY)> CellarWindows(StopBuilding b)
    {
        if (!b.Basement)
            yield break;
        // In the side walls, toward the front (away from the ladder down, so it's the dark between).
        double x = -BackEnd(b) * b.Length / 4, inner = b.Width / 2 - WallThickness;
        yield return (x, inner, 0, 1);
        yield return (x, -inner, 0, -1);
    }

    /// <summary>
    /// A storeyed house's shell, in its own frame (middles and half sizes) with their bottoms and tops over its ground, each
    /// tagged with what it is: its outside walls from <paramref name="low"/> to <paramref name="top"/> (its door, the back door
    /// and its cellar windows cut out of them), the lintels and the plinth under each door, the steps up, the floors, the
    /// flight and the rails. The sim stands it (<see cref="StoreyedSolids"/>), the art draws it.
    /// </summary>
    public static IEnumerable<ShellBox> Shell(StopBuilding b, double low, double top)
    {
        double th = WallThickness, f0 = FloorAt(b, 0);
        double hx = b.Length / 2, hy = b.Width / 2, ix = hx - th, iy = hy - th, door = DoorWidth / 2;
        var (fx, fy) = Front(b);
        int e = BackEnd(b);
        // The openings in the outside walls: (along x?, at, from, to along it, bottom, top).
        var holes = new List<(bool AlongX, double At, double A, double B, double Lo, double Hi)>
        {
            // The back door, in the back end's wall.
            (false, e * (hx - th / 2), -door, door, f0, f0 + Lintel),
        };
        foreach (var (x, y, _, _) in CellarWindows(b))
            holes.Add((true, Math.Sign(y) * (hy - th / 2), x - CellarWindow / 2, x + CellarWindow / 2, WindowSill, WindowHead));
        foreach (var (x, y, wx, wy) in OpenWalls(b))
        {
            bool alongX = wx > wy;
            double at = alongX ? y : x, mid = alongX ? x : y, half = alongX ? wx : wy, across = alongX ? wy : wx;
            var cuts = holes.Where(h => h.AlongX == alongX && Math.Abs(h.At - at) < 1e-6 && h.B > mid - half && h.A < mid + half).OrderBy(h => h.A).ToList();
            double from = mid - half;
            foreach (var h in cuts)
            {
                if (h.A - from > 0.01)
                    yield return Tag(ShellPart.Wall, Along(alongX, at, from, h.A, across, low, top));
                yield return Tag(h.Lo > f0 - 0.01 ? ShellPart.Plinth : ShellPart.Wall, Along(alongX, at, h.A, h.B, across, low, h.Lo));
                yield return Tag(ShellPart.Lintel, Along(alongX, at, h.A, h.B, across, h.Hi, top));
                from = h.B;
            }
            if (mid + half - from > 0.01)
                yield return Tag(ShellPart.Wall, Along(alongX, at, from, mid + half, across, low, top));
        }
        // The front doorway: the lintel over it, and (raised) the plinth under its threshold.
        var (dx, dy, dwx, dwy) = fx != 0 ? (fx * (hx - th / 2), 0.0, th / 2, door) : (0.0, fy * (hy - th / 2), door, th / 2);
        yield return new(ShellPart.Lintel, dx, dy, dwx, dwy, f0 + Lintel, top);
        if (b.Basement)
        {
            yield return new(ShellPart.Plinth, dx, dy, dwx, dwy, low, f0);
            // The steps up to each door, the top one at the threshold.
            foreach (var (ox, oy, nx, ny) in new[] { (dx + fx * th / 2, dy + fy * th / 2, fx, fy), (e * hx, 0.0, (double)e, 0.0) })
                for (int i = 1; i <= DoorSteps; i++)
                {
                    double out_ = (DoorSteps - i + 0.5) * DoorStepTread;
                    bool acrossX = Math.Abs(nx) > 0.5;
                    yield return new(ShellPart.Step, ox + nx * out_, oy + ny * out_, acrossX ? DoorStepTread / 2 : door + 0.2, acrossX ? door + 0.2 : DoorStepTread / 2, -1, f0 * i / DoorSteps);
                }
            // The ground floor's boards, the hatch left open; and the basement's floor.
            var (hxc, hyc, _, _) = Hatch(b);
            foreach (var (x, y, wx, wy) in Less(-ix, -iy, ix, iy, hxc - HatchSize / 2, hyc - HatchSize / 2, hxc + HatchSize / 2, hyc + HatchSize / 2))
                yield return new(ShellPart.Floor, x, y, wx, wy, f0 - Slab, f0);
            yield return new(ShellPart.Cellar, 0, 0, ix, iy, FloorAt(b, -1) - 0.3, FloorAt(b, -1));
        }
        if (b.Upstairs)
        {
            var (x0, dir, y0, y1) = Stair(b);
            double ym = (y0 + y1) / 2, yh = Math.Abs(y0 - y1) / 2;
            // Each tread a block from the floor to its top: the last rise is onto the upper floor.
            for (int i = 1; i < Rises; i++)
            {
                double xm = x0 + dir * (i - 0.5) * Tread;
                yield return new(ShellPart.Tread, xm, ym, Tread / 2, yh, f0 - 0.1, f0 + Storey * i / Rises);
            }
            // The upper floor, open over the flight (from just short of its foot to its top tread's back edge).
            double w0 = x0 - dir * 0.1, w1 = x0 + dir * (Rises - 1) * Tread;
            double f1 = FloorAt(b, 1);
            foreach (var (x, y, wx, wy) in Less(-ix, -iy, ix, iy, Math.Min(w0, w1), Math.Min(y0, y1), Math.Max(w0, w1), Math.Max(y0, y1)))
                yield return new(ShellPart.Upper, x, y, wx, wy, f1 - Slab, f1);
            // Its rails: along the well's open side, and across its end over the flight's foot.
            int s = Math.Sign(y0);
            yield return new(ShellPart.Rail, (w0 + w1) / 2, y1 - s * RailHalf, Math.Abs(w1 - w0) / 2, RailHalf, f1 - Slab, f1 + RailHeight);
            yield return new(ShellPart.Rail, w0 - dir * RailHalf, ym, RailHalf, yh, f1 - Slab, f1 + RailHeight);
        }
    }

    /// <summary>
    /// A storeyed house's solids, as the sim stands them (middles and half sizes, bottoms and tops over its ground): its
    /// <see cref="Shell"/>, its walls from under its basement to run.json's walls.topM, and the furniture kept on each floor.
    /// </summary>
    public static IEnumerable<(double X, double Y, double HalfX, double HalfY, double Bottom, double Top, bool Level)> StoreyedSolids(StopLayout stop, int building,
        WallTuning t)
    {
        var b = stop.Buildings[building];
        double f0 = FloorAt(b, 0);
        foreach (var w in Shell(b, b.Basement ? FloorAt(b, -1) - 0.6 : -3, t.TopM))
            yield return (w.X, w.Y, w.HalfX, w.HalfY, w.Bottom, w.Top, w.Part is not (ShellPart.Wall or ShellPart.Tread));
        // What the finds are kept in, on their floors: under the ceiling over them.
        foreach (var c in stop.Containers.Where(c => c.Building == building))
            foreach (var p in Furniture(b, [c]))
                yield return (p.X, p.Y, p.HalfX, p.HalfY, FloorAt(b, c.Level) - 0.05, FloorAt(b, c.Level) + FurnitureTop, c.Level != 0);
        // A ransack's heavy pieces, on the ground floor.
        foreach (var x in ClutterOf(stop, building).Where(x => x.Solid))
            yield return (x.X, x.Y, x.Box.HalfX, x.Box.HalfY, f0 - 0.05, f0 + FurnitureTop, false);
    }


    static ShellBox Tag(ShellPart part, (double X, double Y, double HalfX, double HalfY, double Bottom, double Top) b) =>
        new(part, b.X, b.Y, b.HalfX, b.HalfY, b.Bottom, b.Top);

    static (double, double, double, double, double, double) Along(bool alongX, double at, double a, double z, double across, double lo, double hi) =>
        alongX ? ((a + z) / 2, at, (z - a) / 2, across, lo, hi) : (at, (a + z) / 2, across, (z - a) / 2, lo, hi);

    /// <summary>A rectangle less a hole in it (both axis-aligned; corners), as up to four boxes (middles and half sizes).</summary>
    static IEnumerable<(double X, double Y, double HalfX, double HalfY)> Less(double x0, double y0, double x1, double y1, double hx0, double hy0, double hx1, double hy1)
    {
        hx0 = Math.Max(hx0, x0);
        hx1 = Math.Min(hx1, x1);
        hy0 = Math.Max(hy0, y0);
        hy1 = Math.Min(hy1, y1);
        static (double, double, double, double) Rect(double a, double b, double c, double d) => ((a + c) / 2, (b + d) / 2, (c - a) / 2, (d - b) / 2);
        if (hx0 - x0 > 0.01)
            yield return Rect(x0, y0, hx0, y1);
        if (x1 - hx1 > 0.01)
            yield return Rect(hx1, y0, x1, y1);
        if (hy0 - y0 > 0.01)
            yield return Rect(hx0, y0, hx1, hy0);
        if (y1 - hy1 > 0.01)
            yield return Rect(hx0, hy1, hx1, y1);
    }

    /// <summary>
    /// A storeyed house's ladders, in its own frame: (foot x, y, its height, its top's height, the way off over its top): the
    /// hatch's down to the basement (down its room side, the way off onto the boards), and each cellar
    /// window's crates (their foot against the wall under the sill, the way off out through it).
    /// </summary>
    public static IEnumerable<(double X, double Y, double Foot, double Top, double InX, double InY, double Onto)> StoreyedLadders(StopBuilding b)
    {
        if (!b.Basement)
            yield break;
        var (hx, hy, inX, inY) = Hatch(b);
        // Down the hatch's room side, its top at the boards (in reach from them, player.json ladder.grabRange): off its top onto
        // the boards beside the hatch.
        yield return (hx + inX * (HatchSize / 2 - 0.15), hy + inY * (HatchSize / 2 - 0.15), FloorAt(b, -1), FloorAt(b, 0), inX, inY, HatchOnto);
        foreach (var (x, y, ox, oy) in CellarWindows(b))
            yield return (x - ox * 0.15, y - oy * 0.15, FloorAt(b, -1), WindowSill + 0.05, ox, oy, WindowOnto);
    }

    // Basements' footprints, by the cells they reach, and the world's ladders (note 1005), shared by every copy (Clear).
    List<(int Owner, Wall Footprint, double Floor)> _basements = [];
    Dictionary<(int, int), List<int>> _basementCells = [];
    List<(int Owner, WorldLadder Ladder)> _ladders = [];
    List<(int Owner, Wall Hole)> _hatches = [];

    /// <summary>The basements' hatches (note 1005), as their holes in the boards: what a bot's path keeps off (<see cref="Bots.FootPath"/>).</summary>
    public IEnumerable<Wall> Hatches => _hatches.Select(h => h.Hole);

    /// <summary>The ladders standing in the stops' houses (note 1005): a basement's hatch's, its cellar windows' crates.</summary>
    public IEnumerable<WorldLadder> Ladders => _ladders.Select(l => l.Ladder);

    /// <summary>
    /// The ground under a point (world) as a body stands on it (note 1005): in a basement's footprint, its floor, not the earth
    /// (which is over it); anywhere else <paramref name="ground"/> as it is.
    /// </summary>
    public double Under(Double3 p, double ground)
    {
        if (_basements.Count == 0 || !_basementCells.TryGetValue(CellOf(p.X, p.Z), out var list))
            return ground;
        foreach (int i in list)
        {
            var (_, w, floor) = _basements[i];
            var l = w.ToLocal(p);
            if (Math.Abs(l.X) <= w.HalfLength && Math.Abs(l.Z) <= w.HalfWidth && p.Y < w.Top)
                return Math.Min(ground, floor);
        }
        return ground;
    }

    /// <summary>
    /// A town's houses with floors (note 1022): their cellars' footprints (the earth isn't stood on in one under its top:
    /// its floor is), their ladders and hatches, standing beside the stops' houses' (they're the town's walls, so no owner).
    /// </summary>
    public void AddFloors(IEnumerable<(Wall Footprint, double Floor)> cellars, IEnumerable<WorldLadder> ladders, IEnumerable<Wall> hatches)
    {
        foreach (var (foot, floor) in cellars)
            AddBasement(0, foot, floor);
        _ladders.AddRange(ladders.Select(l => (0, l)));
        _hatches.AddRange(hatches.Select(h => (0, h)));
    }

    /// <summary>A copy's basements and ladders (<see cref="Clear"/>): those of the buildings it still has.</summary>
    void KeepStoreys(StopWalls from, HashSet<int> gone)
    {
        foreach (var (owner, foot, floor) in from._basements)
            if (!gone.Contains(owner))
                AddBasement(owner, foot, floor);
        _ladders = [.. from._ladders.Where(l => !gone.Contains(l.Owner))];
        _hatches = [.. from._hatches.Where(h => !gone.Contains(h.Owner))];
    }

    void AddBasement(int owner, Wall foot, double floor)
    {
        int index = _basements.Count;
        _basements.Add((owner, foot, floor));
        double r = Math.Sqrt(foot.HalfLength * foot.HalfLength + foot.HalfWidth * foot.HalfWidth) + 1;
        var (x0, z0) = CellOf(foot.Centre.X - r, foot.Centre.Z - r);
        var (x1, z1) = CellOf(foot.Centre.X + r, foot.Centre.Z + r);
        for (int cx = x0; cx <= x1; cx++)
            for (int cz = z0; cz <= z1; cz++)
            {
                if (!_basementCells.TryGetValue((cx, cz), out var list))
                    _basementCells[(cx, cz)] = list = [];
                list.Add(index);
            }
    }

    /// <summary>
    /// Where something's kept in a storeyed house, in its own frame (note 1005), and the way it faces into the room, and where its
    /// find lies: down in the basement (a cellar's), against its front end's wall, the deep end away from the ladder; upstairs
    /// (a cabinet's) against the back end's wall; on the ground floor against the walls clear of the flight, the hatch and the
    /// doors, by kind and the container's index (a cupboard on a side wall, a cabinet on an end's, the boards and a floor's
    /// hatch out on the boards, a small thing lying in a corner).
    /// </summary>
    static (double X, double Y, double FaceX, double FaceY, double FindX, double FindY) KeptStoreyed(StopBuilding b, ContainerKind kind, int index, int level)
    {
        int e = BackEnd(b), s = StairSide(b);
        double t = WallThickness, ix = b.Length / 2 - t, iy = b.Width / 2 - t;
        // (u along the house toward its back end, v across it toward the stair's side; faces likewise.)
        (double, double, double, double, double, double) At(double u, double v, double fu, double fv, double depth, double find) =>
            (e * u + e * fu * depth, s * v + s * fv * depth, e * fu, s * fv, e * u + e * fu * find, s * v + s * fv * find);
        // (index: the container's slot, its rank among its kind's on its floor.)
        double across = (index % 3) switch { 0 => -0.45, 1 => 0.45, _ => 0 };
        if (level < 0)
            return At(-ix, across * iy, 1, 0, 0.35, 0.95);
        if (level > 0)
            return At(ix, across * iy, -1, 0, CabinetDepth, FindOut);
        return kind switch
        {
            ContainerKind.Cupboard => index switch
            {
                0 => At(-ix + 1.2, -iy, 0, 1, CupboardDepth, FindOut),
                1 => At(ix - 1.6, iy, 0, -1, CupboardDepth, FindOut),
                _ => At(-ix + 2.4, -iy, 0, 1, CupboardDepth, FindOut),
            },
            ContainerKind.Cabinet => index % 2 == 0 ? At(-ix, -(iy - 0.9), 1, 0, CabinetDepth, FindOut) : At(ix, iy - 0.9, -1, 0, CabinetDepth, FindOut),
            ContainerKind.Cellar => At(ix - 1.3, -(iy - 1.0) + index * 1.2, -1, 0, 0, 0),
            ContainerKind.UnderFloor => At(-0.6 + index % 2 * 1.4, -0.5, 1, 0, 0, 0),
            ContainerKind.Loose => (index % 4) switch
            {
                0 => At(-ix + 0.4, iy - 0.4, 1, 0, 0, 0),
                1 => At(ix - 0.4, iy - 0.4, -1, 0, 0, 0),
                2 => At(-ix + 0.4, -(iy - 0.4), 1, 0, 0, 0),
                _ => At(1.0, -(iy - 0.4), 0, 1, 0, 0),
            },
            _ => At(0, 0, 1, 0, 0, 0),
        };
    }

    /// <summary>
    /// What a ransack keeps clear of in a storeyed house (note 1005), boxes in its own frame (middles and half sizes): the
    /// flight and the floor at its foot and its top, the hatch and room round it, and the back doorway's inside.
    /// </summary>
    public static IEnumerable<(double X, double Y, double HalfX, double HalfY)> KeepClear(StopBuilding b)
    {
        double t = WallThickness, ix = b.Length / 2 - t;
        if (b.Upstairs)
        {
            var (x0, dir, y0, y1) = Stair(b);
            double a = x0 - dir * StairFoot, z = x0 + dir * ((Rises - 1) * Tread + 0.9);
            yield return ((a + z) / 2, (y0 + y1) / 2, Math.Abs(z - a) / 2, Math.Abs(y0 - y1) / 2 + 0.1);
            // And the floor in front of its foot, out into the room: the way to it, and to the corner beside it.
            double foot = x0 - dir * StairFoot / 2, mouth = y1 - Math.Sign(y0) * 0.7;
            yield return (foot, mouth, StairFoot / 2 + 0.4, 0.7);
        }
        if (b.Basement)
        {
            var (hx, hy, _, _) = Hatch(b);
            yield return (hx, hy, HatchSize / 2 + 0.5, HatchSize / 2 + 0.5);
        }
        int e = BackEnd(b);
        yield return (e * (ix - 0.7), 0, 0.7, DoorWidth / 2 + 0.4);
    }

    /// <summary>Stands a storeyed house (note 1005): its solids, its basement's footprint, and its ladders.</summary>
    void AddStoreyed(StopLayout stop, int building, int owner, WallTuning t, Func<double, double, double, double, double, double, Wall> place,
        Func<double, double, double, Double3> world)
    {
        var b = stop.Buildings[building];
        foreach (var (x, y, hx, hy, lo, hi, level) in StoreyedSolids(stop, building, t))
            Add(place(x, y, hx, hy, lo, hi) with { Level = level });
        if (!b.Basement)
            return;
        // Its footprint out to the walls' outside faces: the earth's not stood on anywhere in it under the ground floor's boards.
        AddBasement(owner, place(0, 0, b.Length / 2, b.Width / 2, -3, FloorAt(b, 0) - Slab), world(0, 0, FloorAt(b, -1)).Y);
        var (hatchX, hatchY, _, _) = Hatch(b);
        _hatches.Add((owner, place(hatchX, hatchY, HatchSize / 2, HatchSize / 2, FloorAt(b, -1), FloorAt(b, 0))));
        foreach (var (x, y, lo, hi, inX, inY, onto) in StoreyedLadders(b))
        {
            var footAt = world(x, y, lo);
            var inward = world(x + inX, y + inY, lo) - footAt;
            _ladders.Add((owner, new WorldLadder(footAt, world(x, y, hi), inward.Normalized, onto)));
        }
    }
}
