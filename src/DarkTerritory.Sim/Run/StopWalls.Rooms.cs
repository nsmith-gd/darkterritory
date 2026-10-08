using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Sim.Run;

/// <summary>
/// A straight run of an open house's outside wall, in the house's own frame (x along its axis, y across): along x at
/// y = <see cref="At"/> (or along y at x = <see cref="At"/>) from <see cref="A"/> to <see cref="B"/>, its outside toward
/// <see cref="Out"/> (±1 on the other axis), and which of the house's units it walls (a pair is two cottages).
/// </summary>
public readonly record struct WallRun(bool AlongX, double At, double A, double B, int Out, int Unit)
{
    public double Length => B - A;

    /// <summary>Its outward normal, in the house's frame.</summary>
    public (double X, double Y) Normal => AlongX ? (0, Out) : (Out, 0);

    /// <summary>A point on its line, <paramref name="f"/> of the way from A to B.</summary>
    public (double X, double Y) Point(double f) => AlongX ? (A + (B - A) * f, At) : (At, A + (B - A) * f);
}

/// <summary>
/// The open houses of more than one part (note 326): an L, a cross, a pair of cottages. One floor, the parts open into
/// each other; the walls follow the outline of the parts together, and each unit (a pair has two) has a door in its longest
/// run of wall facing the line. What's kept stands against the walls' inside faces, a run each, or out on the floor in
/// front of one. A plain house keeps its own four walls (<see cref="OpenWalls"/>), unchanged.
/// </summary>
public sealed partial class StopWalls
{
    /// <summary>An open house laid out from its outline: more than one footprint part.</summary>
    public static bool Composite(StopBuilding b) => b.Parts.Count > 1;

    /// <summary>
    /// A composite house's outline: the runs of its outside wall, which run each unit's door is in, and the floor's cells
    /// (the parts cut where their edges cross, each inside them).
    /// </summary>
    public sealed record HouseOutline(IReadOnlyList<WallRun> Runs, IReadOnlyList<int> Doors, IReadOnlyList<(double X0, double Y0, double X1, double Y1)> Cells);

    const double Eps = 1e-3;

    /// <summary>Whether a point in a house's frame is within any of its parts (on a seam between two, it's in).</summary>
    public static bool InParts(StopBuilding b, double x, double y) =>
        (b.Parts.Count > 0 ? b.Parts : [new FootprintPart(0, 0, b.Length, b.Width)])
            .Any(p => Math.Abs(x - p.X) <= p.Length / 2 + 1e-9 && Math.Abs(y - p.Y) <= p.Width / 2 + 1e-9);

    public static HouseOutline Outline(StopBuilding b)
    {
        var parts = b.Parts.Count > 0 ? b.Parts : [new FootprintPart(0, 0, b.Length, b.Width)];
        static List<double> Edges(IEnumerable<double> e) => [.. e.Select(v => Math.Round(v, 6)).Distinct().Order()];
        var xs = Edges(parts.SelectMany(p => new[] { p.X - p.Length / 2, p.X + p.Length / 2 }));
        var ys = Edges(parts.SelectMany(p => new[] { p.Y - p.Width / 2, p.Y + p.Width / 2 }));
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
                if (runs.Count > 0 && runs[^1] is { AlongX: true } last && last.At == run.At && last.Out == run.Out && last.Unit == run.Unit && last.B == run.A)
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
                if (runs.Count > 0 && runs[^1] is { AlongX: false } last && last.At == run.At && last.Out == run.Out && last.Unit == run.Unit && last.B == run.A)
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
        return new HouseOutline(runs, doors, cells);
    }

    /// <summary>A composite house's walls as boxes in its own frame, each unit's door left open (see <see cref="OpenWalls"/>).</summary>
    static IEnumerable<(double X, double Y, double HalfX, double HalfY)> CompositeWalls(StopBuilding b)
    {
        var o = Outline(b);
        double t = WallThickness, door = DoorWidth / 2;
        for (int k = 0; k < o.Runs.Count; k++)
        {
            var r = o.Runs[k];
            double a = r.A, z = r.B, inward = -r.Out;
            // Corners: a wall along x runs on over an inside corner's notch; a wall along y stops short of an outside
            // corner's, where the wall along x already stands. So no two overlap, and no corner is left open.
            if (r.AlongX)
            {
                a -= InParts(b, r.A - Eps, r.At + inward * Eps) ? t : 0;
                z += InParts(b, r.B + Eps, r.At + inward * Eps) ? t : 0;
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
