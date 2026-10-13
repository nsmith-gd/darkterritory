using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Sim.Run;

/// <summary>
/// A straight run of an open house's outside wall, in the house's own frame (x along its axis, y across): along x at
/// y = <see cref="At"/> (or along y at x = <see cref="At"/>) from <see cref="A"/> to <see cref="B"/>, its outside toward
/// <see cref="Out"/> (±1 on the other axis), and which of the house's units it walls (a pair is two cottages).
/// </summary>
public readonly record struct WallRun(bool AlongX, double At, double A, double B, int Out, int Unit)
{
    /// <summary>A face of the house's inner wall (<see cref="InnerWall"/>), not its outside: no door goes in it, and it stands as the inner wall.</summary>
    public bool Inner { get; init; }

    public double Length => B - A;

    /// <summary>Its outward normal, in the house's frame.</summary>
    public (double X, double Y) Normal => AlongX ? (0, Out) : (Out, 0);

    /// <summary>A point on its line, <paramref name="f"/> of the way from A to B.</summary>
    public (double X, double Y) Point(double f) => AlongX ? (A + (B - A) * f, At) : (At, A + (B - A) * f);
}

/// <summary>
/// An open house's inner wall, in its own frame (note 326's rooms): along x at y = <see cref="At"/> (or along y at x =
/// <see cref="At"/>), from <see cref="A"/> to <see cref="B"/> on the outline's edges (it stands between the outside walls'
/// inner faces), with a doorway <see cref="StopWalls.DoorWidth"/> wide whose middle is <see cref="Door"/> along it.
/// </summary>
public readonly record struct InnerWall(bool AlongX, double At, double A, double B, double Door)
{
    /// <summary>The point on its line <paramref name="along"/> it.</summary>
    public (double X, double Y) Point(double along) => AlongX ? (along, At) : (At, along);

    /// <summary>Its doorway's middle, and the way through it (a unit vector, across the wall).</summary>
    public (double X, double Y) Doorway => Point(Door);
    public (double X, double Y) Through => AlongX ? (0, 1) : (1, 0);
}

/// <summary>
/// The open houses laid out from their outline (note 326): an L, a cross, a pair of cottages, and a plain house with
/// rooms. One floor; the walls follow the outline of the parts together, and each unit (a pair has two) has a door in its
/// longest run of wall facing the line. The bigger houses have rooms (the director, 8 Oct: "explorable interiors"; GDD App.
/// F.3): an L's wing is walled off along its seam with a doorway in the wall, and a plain house of
/// <see cref="RoomsFrom"/> or longer has a back room across its far end (<see cref="Partition"/>). What's kept stands
/// against the walls' inside faces, a run each (the inner wall's two faces among them), or out on the floor in front of
/// one. A plain house shorter than that keeps its own four walls (<see cref="OpenWalls"/>), unchanged.
/// </summary>
public sealed partial class StopWalls
{
    /// <summary>An open house laid out from its outline: more than one footprint part, or rooms.</summary>
    public static bool Composite(StopBuilding b) => b.Parts.Count > 1 || Partition(b) is not null;

    /// <summary>
    /// A plain house this long or longer (m) has a back room, its share <see cref="BackRoom"/> of the house's length. Not
    /// design numbers: a cottage's kitchen and its parlour, as the director's reference houses have them.
    /// </summary>
    public const double RoomsFrom = 9, BackRoom = 0.38;

    /// <summary>
    /// An open house's inner wall, or null if it's one room: along an L's seam between its two parts (the wing a room of
    /// its own), the doorway in the middle; across a plain house of <see cref="RoomsFrom"/> or longer, a back room
    /// <see cref="BackRoom"/> of its length at the end away from its door (either end, by the house's variant, when the
    /// door's in a side), the doorway off to one side of the middle. A cross's arms and a pair's cottages stay as they are.
    /// From the house alone, alike on every machine.
    /// </summary>
    public static InnerWall? Partition(StopBuilding b)
    {
        if (b.Kind != BuildingKind.House)
            return null;
        if (b.Shape == HouseShape.L && b.Parts.Count == 2)
        {
            var (p, q) = (b.Parts[0], b.Parts[1]);
            // Where the two touch: along x (one above the other), or along y (side by side).
            foreach (var (u, v) in new[] { (p, q), (q, p) })
            {
                if (Math.Abs(u.Y + u.Width / 2 - (v.Y - v.Width / 2)) < Eps)
                {
                    double a = Math.Max(u.X - u.Length / 2, v.X - v.Length / 2), z = Math.Min(u.X + u.Length / 2, v.X + v.Length / 2);
                    if (z - a >= DoorWidth + 2 * WallThickness + 1)
                        return new InnerWall(true, u.Y + u.Width / 2, a, z, (a + z) / 2);
                }
                if (Math.Abs(u.X + u.Length / 2 - (v.X - v.Length / 2)) < Eps)
                {
                    double a = Math.Max(u.Y - u.Width / 2, v.Y - v.Width / 2), z = Math.Min(u.Y + u.Width / 2, v.Y + v.Width / 2);
                    if (z - a >= DoorWidth + 2 * WallThickness + 1)
                        return new InnerWall(false, u.X + u.Length / 2, a, z, (a + z) / 2);
                }
            }
            return null;
        }
        if (b.Parts.Count > 1 || b.Length < RoomsFrom || b.Shape is not (HouseShape.Rect or HouseShape.Square))
            return null;
        var (fx, _) = Front(b);
        // The back room at the end away from the door; with the door in a side, at the end its variant says.
        int end = fx != 0 ? -(int)fx : b.Variant % 2 == 0 ? 1 : -1;
        double at = end * (b.Length / 2 - b.Length * BackRoom);
        // Its doorway off the middle, toward one side (by the variant), with wall either side of it.
        double door = (b.Variant / 2 % 2 == 0 ? 1 : -1) * b.Width * 0.2;
        return new InnerWall(false, at, -b.Width / 2, b.Width / 2, door);
    }

    /// <summary>
    /// A composite house's outline: the runs of its outside wall and its inner wall's two faces (<see cref="WallRun.Inner"/>),
    /// which run each unit's door is in, the floor's cells (the parts cut where their edges cross, and at the inner wall,
    /// each inside them), and its inner wall, if it has one.
    /// </summary>
    public sealed record HouseOutline(IReadOnlyList<WallRun> Runs, IReadOnlyList<int> Doors, IReadOnlyList<(double X0, double Y0, double X1, double Y1)> Cells)
    {
        public InnerWall? Partition { get; init; }
    }

    const double Eps = 1e-3;

    /// <summary>Whether a point is where an inner wall meets the outside wall (the outside's runs end there, so each room has its own).</summary>
    static bool Joint(InnerWall? w, double x, double y) =>
        w is { } p && new[] { p.Point(p.A), p.Point(p.B) }.Any(e => Math.Abs(e.X - x) < Eps && Math.Abs(e.Y - y) < Eps);

    /// <summary>Whether a point in a house's frame is within any of its parts (on a seam between two, it's in).</summary>
    public static bool InParts(StopBuilding b, double x, double y) =>
        (b.Parts.Count > 0 ? b.Parts : [new FootprintPart(0, 0, b.Length, b.Width)])
            .Any(p => Math.Abs(x - p.X) <= p.Length / 2 + 1e-9 && Math.Abs(y - p.Y) <= p.Width / 2 + 1e-9);

    public static HouseOutline Outline(StopBuilding b)
    {
        var parts = b.Parts.Count > 0 ? b.Parts : [new FootprintPart(0, 0, b.Length, b.Width)];
        var inner = Partition(b);
        static List<double> Edges(IEnumerable<double> e) => [.. e.Select(v => Math.Round(v, 6)).Distinct().Order()];
        // The floor's cut at the inner wall too, so each room is its own cells.
        var xs = Edges(parts.SelectMany(p => new[] { p.X - p.Length / 2, p.X + p.Length / 2 }).Concat(inner is { AlongX: false } ix ? [ix.At] : []));
        var ys = Edges(parts.SelectMany(p => new[] { p.Y - p.Width / 2, p.Y + p.Width / 2 }).Concat(inner is { AlongX: true } iy ? [iy.At] : []));
        int nx = xs.Count - 1, ny = ys.Count - 1;
        var unit = new int[nx, ny];
        var cells = new List<(double, double, double, double)>();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                unit[i, j] = InParts(b, (xs[i] + xs[i + 1]) / 2, (ys[j] + ys[j + 1]) / 2) ? 0 : -1;
                if (unit[i, j] == 0)
                    cells.Add((xs[i], ys[j], xs[i + 1], ys[j + 1]));
            }
        // The units: the cells that join, numbered in the order they're met.
        int units = 0;
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                if (unit[i, j] != 0)
                    continue;
                units++;
                var stack = new Stack<(int, int)>([(i, j)]);
                while (stack.Count > 0)
                {
                    var (a, c) = stack.Pop();
                    if (a < 0 || c < 0 || a >= nx || c >= ny || unit[a, c] != 0)
                        continue;
                    unit[a, c] = units;
                    stack.Push((a + 1, c));
                    stack.Push((a - 1, c));
                    stack.Push((a, c + 1));
                    stack.Push((a, c - 1));
                }
            }
        int Unit(int i, int j) => i >= 0 && j >= 0 && i < nx && j < ny ? unit[i, j] : -1;
        var runs = new List<WallRun>();
        // Along x, at each y edge: where the cell below and the cell above differ, a wall, its outside to the empty one.
        for (int j = 0; j <= ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int below = Unit(i, j - 1), above = Unit(i, j);
                if (below > 0 == above > 0)
                    continue;
                var run = new WallRun(true, ys[j], xs[i], xs[i + 1], below > 0 ? 1 : -1, Math.Max(below, above));
                if (runs.Count > 0 && runs[^1] is { AlongX: true } last && last.At == run.At && last.Out == run.Out && last.Unit == run.Unit && last.B == run.A
                    && !Joint(inner, run.A, run.At))
                    runs[^1] = last with { B = run.B };
                else
                    runs.Add(run);
            }
        for (int i = 0; i <= nx; i++)
            for (int j = 0; j < ny; j++)
            {
                int left = Unit(i - 1, j), right = Unit(i, j);
                if (left > 0 == right > 0)
                    continue;
                var run = new WallRun(false, xs[i], ys[j], ys[j + 1], left > 0 ? 1 : -1, Math.Max(left, right));
                if (runs.Count > 0 && runs[^1] is { AlongX: false } last && last.At == run.At && last.Out == run.Out && last.Unit == run.Unit && last.B == run.A
                    && !Joint(inner, run.At, run.A))
                    runs[^1] = last with { B = run.B };
                else
                    runs.Add(run);
            }
        // Each unit's door: in its longest run facing the line (Front), else its longest that faces it at all.
        var (fx, fy) = Front(b);
        var doors = new List<int>();
        for (int u = 1; u <= units; u++)
        {
            int best = -1;
            double score = double.MinValue;
            for (int k = 0; k < runs.Count; k++)
            {
                var r = runs[k];
                if (r.Unit != u || r.Length < DoorWidth + 1)
                    continue;
                // Out onto open ground: not a pair's side facing its neighbour across the gap between them.
                var (mx, my) = r.Point(0.5);
                if (InParts(b, mx + r.Normal.X * 0.8, my + r.Normal.Y * 0.8) || InParts(b, mx + r.Normal.X * 1.5, my + r.Normal.Y * 1.5))
                    continue;
                double facing = r.Normal.X * fx + r.Normal.Y * fy;
                double s = facing * 1000 + r.Length;
                if (s > score)
                    (best, score) = (k, s);
            }
            if (best >= 0)
                doors.Add(best);
        }
        // The inner wall's two faces, a room each side, either side of its doorway: furniture stands against them as against
        // any wall (At is the face's outside edge, as an outside run's is the house's: its wall is t in from it).
        if (inner is { } w)
        {
            double t = WallThickness, door = DoorWidth / 2;
            // (Only a plain house and an L have one, each a single unit.)
            foreach (int o in new[] { 1, -1 })
                foreach (var (a, z) in new[] { (w.A, w.Door - door), (w.Door + door, w.B) })
                    if (z - a > t)
                        runs.Add(new WallRun(w.AlongX, w.At + o * t / 2, a, z, o, 1) { Inner = true });
        }
        return new HouseOutline(runs, doors, cells) { Partition = inner };
    }

    /// <summary>A composite house's walls as boxes in its own frame, each unit's door left open (see <see cref="OpenWalls"/>).</summary>
    static IEnumerable<(double X, double Y, double HalfX, double HalfY)> CompositeWalls(StopBuilding b)
    {
        var o = Outline(b);
        double t = WallThickness, door = DoorWidth / 2;
        for (int k = 0; k < o.Runs.Count; k++)
        {
            var r = o.Runs[k];
            if (r.Inner)
                continue;
            double a = r.A, z = r.B, inward = -r.Out;
            // Cut where the inner wall meets it, the outside carrying straight on past (not an L's inside corner).
            bool Split(double end) => Joint(o.Partition, end, r.At) && !InParts(b, end + (end == r.A ? -Eps : Eps), r.At + r.Out * Eps);
            // Corners: a wall along x runs on over an inside corner's notch; a wall along y stops short of an outside
            // corner's, where the wall along x already stands. So no two overlap, and no corner is left open. Where the inner
            // wall meets it, a run just ends: the next carries on from there.
            if (r.AlongX)
            {
                a -= InParts(b, r.A - Eps, r.At + inward * Eps) && !Split(r.A) ? t : 0;
                z += InParts(b, r.B + Eps, r.At + inward * Eps) && !Split(r.B) ? t : 0;
            }
            else
            {
                a += InParts(b, r.At + inward * Eps, r.A - Eps) ? 0 : t;
                z -= InParts(b, r.At + inward * Eps, r.B + Eps) ? 0 : t;
            }
            double mid = (r.A + r.B) / 2, across = r.At + inward * t / 2;
            IEnumerable<(double, double)> spans = o.Doors.Contains(k) ? [(a, mid - door), (mid + door, z)] : [(a, z)];
            foreach (var (s0, s1) in spans)
                yield return r.AlongX ? ((s0 + s1) / 2, across, (s1 - s0) / 2, t / 2) : (across, (s0 + s1) / 2, t / 2, (s1 - s0) / 2);
        }
        // The inner wall, between the outside walls' inner faces, either side of its doorway.
        if (o.Partition is { } w)
            foreach (var (s0, s1) in new[] { (w.A + t, w.Door - door), (w.Door + door, w.B - t) })
                if (s1 - s0 > 0.01)
                    yield return w.AlongX ? ((s0 + s1) / 2, w.At, (s1 - s0) / 2, t / 2) : (w.At, (s0 + s1) / 2, t / 2, (s1 - s0) / 2);
    }

    /// <summary>
    /// The way through an open house's inner wall, in its own frame: a point a little out from its doorway on either side
    /// (none for a house of one room). Kept clear, as a door is.
    /// </summary>
    public static IEnumerable<((double X, double Y) A, (double X, double Y) B)> InnerDoorways(StopBuilding b)
    {
        if (Partition(b) is not { } w)
            yield break;
        var (x, y) = w.Doorway;
        var (nx, ny) = w.Through;
        yield return ((x - nx * 0.9, y - ny * 0.9), (x + nx * 0.9, y + ny * 0.9));
    }

    /// <summary>
    /// Where something's kept in a composite house, in its own frame, and the way it faces into the room: against the inside
    /// face of a run of wall (none with a door), a run each by the container's index, or out on the floor in front of one.
    /// </summary>
    static (double X, double Y, double FaceX, double FaceY, double FindX, double FindY) KeptComposite(StopBuilding b, ContainerKind kind, int index)
    {
        var o = Outline(b);
        var runs = Enumerable.Range(0, o.Runs.Count).Where(k => !o.Doors.Contains(k) && o.Runs[k].Length >= 1.8).Select(k => o.Runs[k])
            .OrderByDescending(r => r.Length).ThenBy(r => r.AlongX).ThenBy(r => r.At).ThenBy(r => r.A).ToList();
        if (runs.Count == 0)
            return (0, 0, 1, 0, 0, 0);
        var r = runs[index % runs.Count];
        double t = WallThickness, inward = -r.Out;
        // Furniture along the run, clear of the walls at its ends; a hatch or the boards in the middle of it.
        double half = kind == ContainerKind.Cupboard ? CupboardWidth : CabinetWidth;
        double f = kind switch
        {
            ContainerKind.Cupboard => index % 2 == 0 ? 0.3 : 0.7,
            ContainerKind.Cabinet => index % 2 == 0 ? 0.7 : 0.3,
            _ => 0.5,
        };
        double along = Math.Clamp(r.A + r.Length * f, r.A + t + half + 0.1, r.B - t - half - 0.1);
        if (r.A + t + half + 0.1 > r.B - t - half - 0.1)
            along = (r.A + r.B) / 2;
        var (nx, ny) = r.AlongX ? (0.0, inward) : (inward, 0.0);
        var (px, py) = r.AlongX ? (along, r.At) : (r.At, along);
        double depth = kind switch
        {
            ContainerKind.Cupboard => t + CupboardDepth,
            ContainerKind.Cabinet => t + CabinetDepth,
            ContainerKind.Cellar => t + 1.3,
            _ => t + 1.7,
        };
        double find = kind is ContainerKind.Cupboard or ContainerKind.Cabinet ? t + FindOut : depth;
        return (px + nx * depth, py + ny * depth, nx, ny, px + nx * find, py + ny * find);
    }

    /// <summary>
    /// Each unit's way in, in an open house's own frame: just outside its door and just inside it (a plain house has one, in
    /// its <see cref="Front"/>).
    /// </summary>
    public static IEnumerable<((double X, double Y) Out, (double X, double Y) In)> Doorways(StopBuilding b)
    {
        if (!Composite(b))
        {
            var (fx, fy) = Front(b);
            double e = fx != 0 ? b.Length / 2 : b.Width / 2;
            yield return ((fx * (e + 0.8), fy * (e + 0.8)), (fx * (e - 1.2), fy * (e - 1.2)));
            yield break;
        }
        var o = Outline(b);
        foreach (int k in o.Doors)
        {
            var r = o.Runs[k];
            var (mx, my) = r.Point(0.5);
            var (nx, ny) = r.Normal;
            yield return ((mx + nx * 0.8, my + ny * 0.8), (mx - nx * 1.2, my - ny * 1.2));
        }
    }
}
