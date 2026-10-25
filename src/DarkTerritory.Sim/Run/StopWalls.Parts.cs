using Ballast;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Run;

/// <summary>A flight up in a house of parts (note 1022), in the house's own frame: along x or along y, its wall side and open side across it, where its first tread's front edge is and the way it climbs.</summary>
public readonly record struct PartFlight(int Part, bool AlongX, double Start, int Dir, double Wall, double Open)
{
    /// <summary>Its top tread's back edge along its axis.</summary>
    public double End => Start + Dir * (StopWalls.Rises - 1) * StopWalls.Tread;

    /// <summary>A point by (along its axis, across it) in the house's frame.</summary>
    public (double X, double Y) At(double along, double across) => AlongX ? (along, across) : (across, along);
}

/// <summary>
/// The villages' L, cross and pair houses with floors (queue #725, ARCHITECTURE §8 note 1022; note 1005's "L, cross and paired
/// houses with floors"). A house of parts' floors are in its biggest part (an L's long block, a cross's first bar; a pair's
/// in each cottage), standing on the ground as a one-floor house of parts does (no plinth: the town houses' way, note 1022):
/// <list type="bullet">
/// <item>A cellar: the part's ground floor a slab over it, a hatch in a corner of the part clear of its doors and the way
/// through, a ladder down its room side to the cellar's floor <see cref="Cellar"/> under the boards; its own walls round
/// it under the boards (the wing's earth stays the wing's). Dark, and its way out is the hatch.</item>
/// <item>An upstairs (a part long enough for a flight and a landing, along an outside wall with no door in it): the flight
/// up, the upper floor over the part round its well, railed; walls upstairs along the part's edges where another part
/// meets it (the wing's a storey: nothing to step off into), and a lintel over every doorway in the house.</item>
/// </list>
/// What the house keeps down there or up there is against the part's far wall, as a plain house's (<see cref="KeptPart"/>).
/// </summary>
public sealed partial class StopWalls
{
    /// <summary>Whether a house of parts has floors (note 1022): an open L, cross or pair whose form isn't plain.</summary>
    public static bool PartFloored(StopBuilding b) => b is { Kind: BuildingKind.House, Open: true } && b.Form != HouseForm.Plain && b.Parts.Count > 1;

    /// <summary>Whether an open house has floors (notes 1005, 1022): a plain house's, or a house of parts'.</summary>
    public static bool Floored(StopBuilding b) => Storeyed(b) || PartFloored(b);

    /// <summary>A house of parts' floors' boards over its ground (m; no plinth): the cellar's, the ground floor's, upstairs.</summary>
    public static double PartFloorAt(int level) => level switch { < 0 => -Cellar, > 0 => Storey, _ => 0 };

    /// <summary>A floor's boards over a house's ground, either kind of house with floors (0 for one without).</summary>
    public static double LevelAt(StopBuilding b, int level) => Storeyed(b) ? FloorAt(b, level) : PartFloored(b) ? PartFloorAt(level) : 0;

    /// <summary>The parts a house of parts has its floors in: a pair's two cottages, else its biggest part (the first of them).</summary>
    public static IReadOnlyList<int> FlooredParts(StopBuilding b)
    {
        if (!PartFloored(b))
            return [];
        if (b.Shape == HouseShape.Pair)
            return [.. Enumerable.Range(0, b.Parts.Count)];
        int best = 0;
        for (int i = 1; i < b.Parts.Count; i++)
            if (b.Parts[i].Length * b.Parts[i].Width > b.Parts[best].Length * b.Parts[best].Width + 1e-9)
                best = i;
        return [best];
    }

    /// <summary>A part's inside, its walls' inner faces (x0, y0, x1, y1).</summary>
    static (double X0, double Y0, double X1, double Y1) Inner(FootprintPart p)
    {
        double t = WallThickness;
        return (p.X - p.Length / 2 + t, p.Y - p.Width / 2 + t, p.X + p.Length / 2 - t, p.Y + p.Width / 2 - t);
    }

    /// <summary>Whether any of a run's line just outside a part's edge is in another part (a seam: no outside wall there).</summary>
    static bool Seam(StopBuilding b, bool alongX, double at, double a, double z, int outward)
    {
        for (double s = a + 0.1; s <= z - 0.1 + 1e-9; s += 0.25)
        {
            var (x, y) = alongX ? (s, at + outward * 0.3) : (at + outward * 0.3, s);
            if (InParts(b, x, y))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Where a flight can go up in a house of parts' part (note 1022), or null: along an outside wall of the part's (no seam
    /// beside it), no doorway in that wall along it or at its foot, the way through to the back room clear, room at its foot
    /// and a landing at its top. Its axis the part's longer first, its side and its way up by the house's variant.
    /// </summary>
    public static PartFlight? PartStair(StopBuilding b, int part)
    {
        var p = b.Parts[part];
        var (x0, y0, x1, y1) = Inner(p);
        var o = Outline(b);
        var doors = o.Doors.Select(k => o.Runs[k]).ToList();
        var inner = Partition(b);
        double need = StairFoot + (Rises - 1) * Tread + 0.6;
        foreach (bool alongX in p.Length >= p.Width ? new[] { true, false } : [false, true])
        {
            var (a0, a1, c0, c1) = alongX ? (x0, x1, y0, y1) : (y0, y1, x0, x1);
            if (a1 - a0 < need || c1 - c0 < StairWidth + 1.4)
                continue;
            foreach (int side in b.Variant % 2 == 0 ? new[] { 1, -1 } : [-1, 1])
                foreach (int dir in b.Variant / 2 % 2 == 0 ? new[] { 1, -1 } : [-1, 1])
                {
                    double wall = side > 0 ? c1 : c0, open = wall - side * StairWidth;
                    double start = dir > 0 ? a0 + StairFoot : a1 - StairFoot;
                    var f = new PartFlight(part, alongX, start, dir, wall, open);
                    double lo = Math.Min(start - dir * StairFoot, f.End), hi = Math.Max(start - dir * StairFoot, f.End);
                    // Its wall's an outside wall all along it.
                    double edge = wall + side * WallThickness;
                    if (Seam(b, alongX, edge, lo, hi, side))
                        continue;
                    // No doorway in that wall along it (and its foot), nor in the part's end wall at its foot.
                    bool blocked = doors.Any(r =>
                    {
                        var (mx, my) = r.Point(0.5);
                        var (along, across) = alongX ? (mx, my) : (my, mx);
                        return r.AlongX == alongX && Math.Abs(across - edge) < 0.4 && along > lo - DoorWidth && along < hi + DoorWidth
                            || r.AlongX != alongX && Math.Abs(along - (start - dir * (StairFoot + WallThickness))) < 0.4
                                && across > Math.Min(wall, open) - DoorWidth && across < Math.Max(wall, open) + DoorWidth;
                    });
                    if (blocked)
                        continue;
                    if (inner is { } w)
                    {
                        var (dx, dy) = w.Doorway;
                        var (da, dc) = alongX ? (dx, dy) : (dy, dx);
                        if (da > lo - 1.2 && da < hi + 1.2 && dc > Math.Min(wall, open) - 1.2 && dc < Math.Max(wall, open) + 1.2)
                            continue;
                    }
                    return f;
                }
        }
        return null;
    }

    /// <summary>
    /// The hatch down in a house of parts' part (note 1022), its middle and the ladder's way off at its top (into the room),
    /// or null: a corner of the part's inside clear of the flight, the doorways' insides, the way through and the seams.
    /// </summary>
    public static (double X, double Y, double InX, double InY)? PartHatch(StopBuilding b, int part)
    {
        var p = b.Parts[part];
        var (x0, y0, x1, y1) = Inner(p);
        double off = HatchIn + HatchSize / 2;
        var clear = new List<(double X, double Y, double R)>();
        foreach (var (_, inside) in Doorways(b))
            clear.Add((inside.X, inside.Y, 1.6));
        foreach (var (one, other) in InnerDoorways(b))
        {
            clear.Add((one.X, one.Y, 1.2));
            clear.Add((other.X, other.Y, 1.2));
        }
        var stair = b.Upstairs ? PartStair(b, part) : null;
        int start = (b.Variant + part) % 4;
        for (int k = 0; k < 4; k++)
        {
            int c = (start + k) % 4;
            double x = c % 2 == 0 ? x0 + off : x1 - off, y = c / 2 == 0 ? y0 + off : y1 - off;
            // Its ladder down the side toward the part's middle along its longer axis, off onto the boards there.
            var (inX, inY) = p.Length >= p.Width ? (Math.Sign(p.X - x), 0.0) : (0.0, (double)Math.Sign(p.Y - y));
            if (clear.Any(q => Math.Max(0, Math.Abs(q.X - x) - HatchSize / 2) is var dx && Math.Max(0, Math.Abs(q.Y - y) - HatchSize / 2) is var dy
                && dx * dx + dy * dy < q.R * q.R))
                continue;
            if (stair is { } f && FlightClear(f).Any(r => Math.Abs(r.X - x) < r.HalfX + HatchSize / 2 + 0.3 && Math.Abs(r.Y - y) < r.HalfY + HatchSize / 2 + 0.3))
                continue;
            return (x, y, inX, inY);
        }
        return null;
    }

    /// <summary>A flight's footprint and the floor at its foot (middles and half sizes, the house's frame): kept clear.</summary>
    static IEnumerable<(double X, double Y, double HalfX, double HalfY)> FlightClear(PartFlight f)
    {
        double a = f.Start - f.Dir * StairFoot, z = f.End + f.Dir * 0.6, c = (f.Wall + f.Open) / 2, hc = Math.Abs(f.Wall - f.Open) / 2 + 0.1;
        var (mx, my) = f.At((a + z) / 2, c);
        var (hx, hy) = f.AlongX ? (Math.Abs(z - a) / 2, hc) : (hc, Math.Abs(z - a) / 2);
        yield return (mx, my, hx, hy);
        // And the floor in front of its foot, out into the room.
        double mouth = f.Open - Math.Sign(f.Wall - f.Open) * 0.6;
        var (fx, fy) = f.At(f.Start - f.Dir * StairFoot / 2, mouth);
        yield return (fx, fy, f.AlongX ? StairFoot / 2 + 0.4 : 0.6, f.AlongX ? 0.6 : StairFoot / 2 + 0.4);
    }

    /// <summary>What a house of parts keeps clear on its ground floor (note 1022): its flights, its hatches and room round them.</summary>
    public static IEnumerable<(double X, double Y, double HalfX, double HalfY)> PartKeepClear(StopBuilding b)
    {
        foreach (int part in FlooredParts(b))
        {
            if (b.Upstairs && PartStair(b, part) is { } f)
                foreach (var r in FlightClear(f))
                    yield return r;
            if (b.Basement && PartHatch(b, part) is { } h)
                yield return (h.X, h.Y, HatchSize / 2 + 0.5, HatchSize / 2 + 0.5);
        }
    }

    /// <summary>
    /// A house of parts' floors' solids (note 1022), in its own frame (middles, half sizes, bottoms and tops over its ground),
    /// tagged as a plain house's shell's are: each floored part's boards with the hatch let in, its cellar's floor and walls,
    /// its flight's treads, its upper floor round the well, the rails, the walls upstairs along its seams; and a lintel over
    /// every doorway of a house with an upstairs. The sim stands them beside the house's own walls, the art draws them.
    /// </summary>
    public static IEnumerable<ShellBox> PartShell(StopBuilding b, double top)
    {
        double t = WallThickness, f1 = Storey;
        foreach (int part in FlooredParts(b))
        {
            var p = b.Parts[part];
            var (x0, y0, x1, y1) = Inner(p);
            double mx = (x0 + x1) / 2, my = (y0 + y1) / 2, hx = (x1 - x0) / 2, hy = (y1 - y0) / 2;
            if (b.Basement && PartHatch(b, part) is { } hatch)
            {
                var (hcx, hcy, _, _) = hatch;
                foreach (var (x, y, wx, wy) in Less(x0, y0, x1, y1, hcx - HatchSize / 2, hcy - HatchSize / 2, hcx + HatchSize / 2, hcy + HatchSize / 2))
                    yield return new(ShellPart.Floor, x, y, wx, wy, -Slab, 0);
                yield return new(ShellPart.Cellar, mx, my, hx, hy, -Cellar - 0.3, -Cellar);
                // Its walls under the boards, all round (the outside walls go down only so far, and a seam has none).
                yield return new(ShellPart.Wall, mx, y0 - t / 2, hx + t, t / 2, -Cellar - 0.3, -Slab);
                yield return new(ShellPart.Wall, mx, y1 + t / 2, hx + t, t / 2, -Cellar - 0.3, -Slab);
                yield return new(ShellPart.Wall, x0 - t / 2, my, t / 2, hy, -Cellar - 0.3, -Slab);
                yield return new(ShellPart.Wall, x1 + t / 2, my, t / 2, hy, -Cellar - 0.3, -Slab);
            }
            if (b.Upstairs && PartStair(b, part) is { } f)
            {
                double cm = (f.Wall + f.Open) / 2, ch = Math.Abs(f.Wall - f.Open) / 2;
                for (int i = 1; i < Rises; i++)
                {
                    var (tx, ty) = f.At(f.Start + f.Dir * (i - 0.5) * Tread, cm);
                    yield return new(ShellPart.Tread, tx, ty, f.AlongX ? Tread / 2 : ch, f.AlongX ? ch : Tread / 2, -0.1, f1 * i / Rises);
                }
                double w0 = f.Start - f.Dir * 0.1, w1 = f.End;
                var (ax, ay) = f.At(Math.Min(w0, w1), Math.Min(f.Wall, f.Open));
                var (zx, zy) = f.At(Math.Max(w0, w1), Math.Max(f.Wall, f.Open));
                foreach (var (x, y, wx, wy) in Less(x0, y0, x1, y1, Math.Min(ax, zx), Math.Min(ay, zy), Math.Max(ax, zx), Math.Max(ay, zy)))
                    yield return new(ShellPart.Upper, x, y, wx, wy, f1 - Slab, f1);
                // Its rails: along the well's open side, and across its end over the flight's foot.
                int s = Math.Sign(f.Wall - f.Open);
                var (rx, ry) = f.At((w0 + w1) / 2, f.Open - s * RailHalf);
                double run = Math.Abs(w1 - w0) / 2;
                yield return new(ShellPart.Rail, rx, ry, f.AlongX ? run : RailHalf, f.AlongX ? RailHalf : run, f1 - Slab, f1 + RailHeight);
                var (ex, ey) = f.At(w0 - f.Dir * RailHalf, cm);
                yield return new(ShellPart.Rail, ex, ey, f.AlongX ? RailHalf : ch, f.AlongX ? ch : RailHalf, f1 - Slab, f1 + RailHeight);
                // Upstairs, a wall along each of its edges another part meets: the rest of the house is a storey.
                foreach (var (alongX, at, a, z, o) in new[] { (true, y0 - t / 2, x0 - t, x1 + t, -1), (true, y1 + t / 2, x0 - t, x1 + t, 1),
                    (false, x0 - t / 2, y0 - t, y1 + t, -1), (false, x1 + t / 2, y0 - t, y1 + t, 1) })
                    if (Seam(b, alongX, at + o * t / 2, a + t, z - t, o))
                        yield return alongX ? new(ShellPart.Wall, (a + z) / 2, at, (z - a) / 2, t / 2, f1 - Slab, top)
                            : new(ShellPart.Wall, at, (a + z) / 2, t / 2, (z - a) / 2, f1 - Slab, top);
            }
        }
        // A lintel over every doorway (each unit's door, the way through to the back room): upstairs is over them.
        if (b.Upstairs && FlooredParts(b).Any(part => PartStair(b, part) is not null))
        {
            var o = Outline(b);
            double door = DoorWidth / 2;
            foreach (var r in o.Doors.Select(k => o.Runs[k]))
            {
                double mid = (r.A + r.B) / 2, across = r.At - r.Out * t / 2;
                yield return r.AlongX ? new(ShellPart.Lintel, mid, across, door, t / 2, Lintel, top) : new(ShellPart.Lintel, across, mid, t / 2, door, Lintel, top);
            }
            if (o.Partition is { } w)
            {
                var (dx, dy) = w.Doorway;
                yield return w.AlongX ? new(ShellPart.Lintel, dx, dy, door, t / 2, Lintel, top) : new(ShellPart.Lintel, dx, dy, t / 2, door, Lintel, top);
            }
        }
    }

    /// <summary>Whether a house of parts has an upstairs it can go up to (its form's, and a flight that fits).</summary>
    public static bool PartUp(StopBuilding b) => PartFloored(b) && b.Upstairs && FlooredParts(b).Any(part => PartStair(b, part) is not null);

    /// <summary>Whether a house of parts has a cellar it can go down to (its form's, and a hatch that fits).</summary>
    public static bool PartDown(StopBuilding b) => PartFloored(b) && b.Basement && FlooredParts(b).Any(part => PartHatch(b, part) is not null);

    /// <summary>A house of parts' ladders (note 1022), in its own frame: each hatch's, down its room side, off onto the boards.</summary>
    public static IEnumerable<(double X, double Y, double Foot, double Top, double InX, double InY, double Onto)> PartLadders(StopBuilding b)
    {
        if (!b.Basement)
            yield break;
        foreach (int part in FlooredParts(b))
            if (PartHatch(b, part) is { } h)
                yield return (h.X + h.InX * (HatchSize / 2 - 0.15), h.Y + h.InY * (HatchSize / 2 - 0.15), -Cellar, 0, h.InX, h.InY, HatchOnto);
    }

    /// <summary>
    /// Where something's kept on a house of parts' other floors (note 1022), as a plain house's (<see cref="KeptStoreyed"/>):
    /// down in the cellar against the part's wall furthest from the hatch, a chest; upstairs against the wall furthest from the
    /// flight's top, a cabinet; a second across from the first. Its middle, the way it faces, and where its find lies.
    /// </summary>
    static (double X, double Y, double FaceX, double FaceY, double FindX, double FindY) KeptPart(StopBuilding b, int index, int level)
    {
        var parts = FlooredParts(b).Where(part => level < 0 ? PartHatch(b, part) is not null : PartStair(b, part) is not null).ToList();
        if (parts.Count == 0)
            return (0, 0, 1, 0, 0, 0);
        int part = parts[index % parts.Count];
        var p = b.Parts[part];
        var (x0, y0, x1, y1) = Inner(p);
        // The far end from the hatch (or the flight's top), along the part's longer axis.
        bool alongX = p.Length >= p.Width;
        double from = level < 0 ? PartHatch(b, part) is { } h ? alongX ? h.X : h.Y : 0
            : PartStair(b, part) is { } f ? f.AlongX == alongX ? f.End : alongX ? p.X : p.Y : 0;
        double centre = alongX ? p.X : p.Y;
        int far = from > centre ? -1 : 1;
        double end = alongX ? (far > 0 ? x1 : x0) : (far > 0 ? y1 : y0);
        double across = (index / Math.Max(1, parts.Count) % 2 == 0 ? -0.3 : 0.3) * (alongX ? y1 - y0 : x1 - x0) + (alongX ? (y0 + y1) / 2 : (x0 + x1) / 2);
        double depth = level < 0 ? 0.35 : CabinetDepth, find = level < 0 ? 0.95 : FindOut;
        (double, double) At(double a, double c) => alongX ? (a, c) : (c, a);
        var (kx, ky) = At(end - far * depth, across);
        var (nx, ny) = At(-far, 0);
        var (fx, fy) = At(end - far * find, across);
        return (kx, ky, nx, ny, fx, fy);
    }

    /// <summary>Stands a house of parts' floors (note 1022): their solids, the cellars' footprints, the hatches and their ladders.</summary>
    void AddParts(StopLayout stop, int building, int owner, WallTuning t, Func<double, double, double, double, double, double, Wall> place,
        Func<double, double, double, Double3> world)
    {
        var b = stop.Buildings[building];
        foreach (var p in PartShell(b, t.TopM))
            Add(place(p.X, p.Y, p.HalfX, p.HalfY, p.Bottom, p.Top) with { Level = p.Part is not (ShellPart.Wall or ShellPart.Tread) });
        // What's kept on the other floors, under the ceiling over it.
        foreach (var c in stop.Containers.Where(c => c.Building == building && c.Level != 0))
            foreach (var f in Furniture(b, [c]))
                Add(place(f.X, f.Y, f.HalfX, f.HalfY, PartFloorAt(c.Level) - 0.05, PartFloorAt(c.Level) + FurnitureTop) with { Level = c.Level > 0 });
        if (!b.Basement)
            return;
        foreach (int part in FlooredParts(b))
        {
            if (PartHatch(b, part) is not { } h)
                continue;
            var (x0, y0, x1, y1) = Inner(b.Parts[part]);
            AddBasement(owner, place((x0 + x1) / 2, (y0 + y1) / 2, (x1 - x0) / 2, (y1 - y0) / 2, -3, 0.5), world(0, 0, -Cellar).Y);
            _hatches.Add((owner, place(h.X, h.Y, HatchSize / 2, HatchSize / 2, -Cellar, 0)));
        }
        foreach (var (x, y, lo, hi, inX, inY, onto) in PartLadders(b))
        {
            var footAt = world(x, y, lo);
            var inward = world(x + inX, y + inY, lo) - footAt;
            _ladders.Add((owner, new WorldLadder(footAt, world(x, y, hi), inward.Normalized, onto)));
        }
    }
}
