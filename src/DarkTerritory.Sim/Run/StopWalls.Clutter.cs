using Ballast;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Sim.Run;

/// <summary>What lies about a ransacked house (the director, 8 Oct 2026: "procedurally generated furniture scattered about").</summary>
public enum ClutterKind : byte
{
    // Solid: against a wall, a crewmate goes round it.
    Table, Bed, Dresser,
    // Underfoot: low, stepped over or kicked aside.
    Chair, Crate, Drawer, Planks, Rags, Papers, Crockery, Frame, Bucket,
}

/// <summary>
/// A piece of a ransacked house's clutter, in the house's own frame (x along its axis, y across): its middle on the floor,
/// its half length (along <see cref="Yaw"/>) and half depth, its yaw from the house's x toward its y, whether it's knocked
/// over, and whether it stands in the way (<see cref="StopWalls"/>).
/// </summary>
public readonly record struct Clutter(ClutterKind Kind, double X, double Y, double HalfX, double HalfY, double Yaw, bool Tipped)
{
    public bool Solid => Kind is ClutterKind.Table or ClutterKind.Bed or ClutterKind.Dresser;

    /// <summary>Its footprint as a box square to the house (half sizes along x and y): a solid piece's is exact, being square to it.</summary>
    public (double HalfX, double HalfY) Box
    {
        get
        {
            double c = Math.Abs(DMath.Cos(Yaw)), s = Math.Abs(DMath.Sin(Yaw));
            return (HalfX * c + HalfY * s, HalfX * s + HalfY * c);
        }
    }
}

/// <summary>
/// The ransacked houses (the director, 8 Oct 2026, GDD App. F.3: "they need procedurally generated furniture scattered about,
/// like the place has been ransacked many times before"; ARCHITECTURE §8 note 326). Each open house's clutter is dealt from the
/// stop's seed and the house's index on a stream of its own, so the stop is laid as before, and the same on every machine: the
/// host collides with the solid pieces as a client predicts it. A table, a bed or a dresser stands (or lies) against a wall,
/// never by a door, a find, what a find is kept in or the Gaunt's nest (two, sometimes three); eight to fourteen chairs knocked over, drawers pulled out and emptied,
/// shelves' planks, rags, papers, broken crockery, a picture off its nail and a bucket lie about the floor between, never on
/// a find or in a doorway.
/// </summary>
public sealed partial class StopWalls
{
    const ulong ClutterStream = 0x434c5554;

    /// <summary>The furniture a ransack leaves (half length, half depth): solid, against a wall.</summary>
    static readonly (ClutterKind Kind, double HalfX, double HalfY)[] Heavy =
        [(ClutterKind.Table, 0.6, 0.38), (ClutterKind.Bed, 0.95, 0.45), (ClutterKind.Dresser, 0.8, 0.3)];

    /// <summary>What lies underfoot (half length, half depth), with how often each turns up.</summary>
    static readonly (ClutterKind Kind, double HalfX, double HalfY, double Weight)[] Loose =
    [
        (ClutterKind.Chair, 0.22, 0.22, 3), (ClutterKind.Crate, 0.2, 0.16, 2), (ClutterKind.Drawer, 0.24, 0.18, 2),
        (ClutterKind.Planks, 0.5, 0.12, 2), (ClutterKind.Rags, 0.3, 0.22, 2), (ClutterKind.Papers, 0.25, 0.2, 2),
        (ClutterKind.Crockery, 0.18, 0.14, 1.5), (ClutterKind.Frame, 0.25, 0.2, 1), (ClutterKind.Bucket, 0.13, 0.13, 1),
    ];

    /// <summary>
    /// Where the Gaunt nests in a house (its roost, level-design H.2), in the house's own frame, or null if it doesn't nest in
    /// this one: what the clutter keeps clear of and the art makes a nest of.
    /// </summary>
    public static (double X, double Y)? Nest(StopLayout stop, int building)
    {
        if (stop.Lairs.FirstOrDefault(l => l.Kind == LairKind.GauntRoost && l.Building == building) is not { } roost)
            return null;
        return Plan.Local(roost.At, stop.Buildings[building]);
    }

    /// <summary>An open house's clutter (none for a shut one).</summary>
    public static IReadOnlyList<Clutter> ClutterOf(StopLayout stop, int building)
    {
        var b = stop.Buildings[building];
        if (!b.Open || !Walled(stop, building))
            return [];
        var rng = new Ballast.Pcg32(StopSeed.Of(stop.Seed, ClutterStream, (ulong)building));
        var o = Outline(b);
        double t = WallThickness;
        var kept = stop.Containers.Where(c => c.Building == building).ToList();
        // What's to be kept clear: the finds and the spot in front of each (where a crewmate stands to search), what they're
        // kept in, each door's inside, and the nest.
        var clear = new List<(double X, double Y, double R)>();
        foreach (var c in kept)
        {
            var (fx, fy) = InsideLocal(b, c.Kind, c.Index);
            var (_, _, nx, ny) = Kept(b, c.Kind, c.Index);
            clear.Add((fx, fy, 0.75));
            clear.Add((fx + nx * 0.45, fy + ny * 0.45, 0.75));
        }
        foreach (var (_, inside) in Doorways(b))
            clear.Add((inside.X, inside.Y, 1.4));
        if (Nest(stop, building) is { } nest)
            clear.Add((nest.X, nest.Y, 1.5));
        var boxes = Furniture(b, kept).Select(f => (f.X, f.Y, f.HalfX, f.HalfY)).ToList();
        var pieces = new List<Clutter>();

        // A box (axis-aligned in the house's frame) clear of the walls' insides, the furniture, the other pieces and the clear spots.
        bool Fits(double x, double y, double hx, double hy, double pad)
        {
            foreach (var (cx, cy) in new[] { (x - hx, y - hy), (x + hx, y - hy), (x - hx, y + hy), (x + hx, y + hy), (x, y) })
                if (!InParts(b, cx, cy))
                    return false;
            foreach (var (wx, wy, whx, why) in OpenWalls(b))
                if (Math.Abs(x - wx) < hx + whx + 0.01 && Math.Abs(y - wy) < hy + why + 0.01)
                    return false;
            foreach (var (bx, by, bhx, bhy) in boxes)
                if (Math.Abs(x - bx) < hx + bhx + pad && Math.Abs(y - by) < hy + bhy + pad)
                    return false;
            foreach (var (cx, cy, r) in clear)
            {
                double dx = Math.Max(0, Math.Abs(cx - x) - hx), dy = Math.Max(0, Math.Abs(cy - y) - hy);
                if (dx * dx + dy * dy < r * r)
                    return false;
            }
            return true;
        }

        // The heavy pieces: two, sometimes three, each against a run of wall with no door, along it.
        var runs = Enumerable.Range(0, o.Runs.Count).Where(k => !o.Doors.Contains(k)).Select(k => o.Runs[k]).ToList();
        int heavy = 2 + (rng.Chance(0.35) ? 1 : 0);
        for (int n = 0, tries = 0; n < heavy && tries < 30 && runs.Count > 0; tries++)
        {
            var (kind, hl, hd) = Heavy[(int)(rng.NextDouble() * Heavy.Length)];
            var r = runs[(int)(rng.NextDouble() * runs.Count)];
            if (r.Length < 2 * (hl + t) + 0.2)
                continue;
            double along = rng.Range(r.A + t + hl + 0.1, r.B - t - hl - 0.1), inward = -r.Out;
            double across = r.At + inward * (t + hd + 0.03);
            var (x, y, hx, hy) = r.AlongX ? (along, across, hl, hd) : (across, along, hd, hl);
            if (!Fits(x, y, hx, hy, 0.35))
                continue;
            // Its yaw: its length along the wall (0 along x, a quarter turn along y), its front to the room.
            double yaw = r.AlongX ? (inward > 0 ? 0 : Math.PI) : (inward > 0 ? -Math.PI / 2 : Math.PI / 2);
            // A table's as likely overturned as stood; a dresser's always down on its face; a bed's stripped where it stands.
            bool tipped = kind == ClutterKind.Dresser || kind == ClutterKind.Table && rng.Chance(0.5);
            pieces.Add(new Clutter(kind, x, y, hl, hd, yaw, tipped));
            boxes.Add((x, y, hx, hy));
            n++;
        }

        // The loose things: eight to fourteen, anywhere on the floor that's free.
        int loose = rng.RangeInclusive(8, 14);
        double total = Loose.Sum(l => l.Weight);
        var cells = o.Cells;
        double area = cells.Sum(c => (c.X1 - c.X0) * (c.Y1 - c.Y0));
        for (int n = 0, tries = 0; n < loose && tries < 120; tries++)
        {
            double pick = rng.NextDouble() * total;
            var l = Loose[^1];
            foreach (var x in Loose)
            {
                if ((pick -= x.Weight) < 0)
                {
                    l = x;
                    break;
                }
            }
            // A point on the floor, the cells by their area.
            double at = rng.NextDouble() * area;
            var cell = cells[^1];
            foreach (var c in cells)
                if ((at -= (c.X1 - c.X0) * (c.Y1 - c.Y0)) < 0)
                {
                    cell = c;
                    break;
                }
            double px = rng.Range(cell.X0, cell.X1), py = rng.Range(cell.Y0, cell.Y1), yaw2 = rng.Range(0, 2 * Math.PI);
            bool tip = l.Kind is ClutterKind.Chair && rng.Chance(0.7) || l.Kind is ClutterKind.Crate && rng.Chance(0.3);
            // Its footprint, turned, as a box round it: loose pieces lie at any angle.
            double c0 = Math.Abs(DMath.Cos(yaw2)), s0 = Math.Abs(DMath.Sin(yaw2));
            double bx = l.HalfX * c0 + l.HalfY * s0, by = l.HalfX * s0 + l.HalfY * c0;
            if (!Fits(px, py, bx, by, 0.05))
                continue;
            if (pieces.Any(p => !p.Solid && Math.Abs(p.X - px) < 0.4 && Math.Abs(p.Y - py) < 0.4))
                continue;
            pieces.Add(new Clutter(l.Kind, px, py, l.HalfX, l.HalfY, yaw2, tip));
            n++;
        }
        return pieces;
    }
}
