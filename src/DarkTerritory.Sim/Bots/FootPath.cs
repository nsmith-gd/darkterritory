using Ballast;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

/// <summary>
/// A way on foot across a stop for a bot (note 326: a crate hand going to search the village's houses): round the stops'
/// walls (<see cref="Run.StopWalls"/>: the houses, their furniture, the sheds) and the train's cars, in at a house's door.
/// A* over a grid of <see cref="Cell"/> squares, each free if a crewmate stands in it clear of every wall and car, over the
/// box round the two ends (and a margin). The bots walk straight at things otherwise, which is fine beside a train and
/// hopeless in a village. Same answer on every run: the grid, the walls and the ties are all fixed.
/// </summary>
public static class FootPath
{
    /// <summary>The grid's square (m): a door's 1.2 m less a crewmate's width either side leaves two of them.</summary>
    public const double Cell = 0.25;
    /// <summary>How far round the two ends the grid reaches, for a way round what's between them (m).</summary>
    const double Margin = 18;
    /// <summary>The wider box looked in when there's no way in the first (m): round a long shed from its far side.</summary>
    const double WideMargin = 60;
    /// <summary>A car's side steps out past its body (m), and half a coupling gap and more (m): the train is one wall.</summary>
    const double StepsOut = 0.8, GapIn = 1.0;
    /// <summary>The longest way it looks for, end to end (m); anything further isn't worth a bot's walk.</summary>
    const double Longest = 400;

    /// <summary>
    /// The way from <paramref name="from"/> to <paramref name="to"/> on foot, as points to walk to in turn (the last is
    /// <paramref name="to"/>), or null if there's none within reach. <paramref name="clearance"/> is a crewmate's half-width
    /// (and a little).
    /// </summary>
    /// <remarks>
    /// Looked for in a box <see cref="Margin"/> round the two ends first, and only if there's no way in that, in one
    /// <see cref="WideMargin"/> round them (note 440): a yard's hero 45 m long, its door away from the line, is a 74 m way
    /// round from a hand 17 m off it, out past the tight box's edge.
    /// </remarks>
    public static List<Double3>? Plan(TrainOnLine train, Double3 from, Double3 to, double clearance = 0.36) =>
        Plan(train, from, to, clearance, Margin) ?? Plan(train, from, to, clearance, WideMargin);

    static List<Double3>? Plan(TrainOnLine train, Double3 from, Double3 to, double clearance, double margin)
    {
        if ((Flat(to) - Flat(from)).Length > Longest)
            return null;
        double x0 = Math.Min(from.X, to.X) - margin, z0 = Math.Min(from.Z, to.Z) - margin;
        int nx = (int)Math.Ceiling((Math.Max(from.X, to.X) + margin - x0) / Cell) + 1;
        int nz = (int)Math.Ceiling((Math.Max(from.Z, to.Z) + margin - z0) / Cell) + 1;
        Double3 At(int i, int k) => new(x0 + i * Cell, 0, z0 + k * Cell);
        // The cars near the box, as their footprints in their own frames.
        var cars = train.Frames.Where(f => Math.Abs(f.Origin.X - (x0 + nx * Cell / 2)) < nx * Cell / 2 + 20
            && Math.Abs(f.Origin.Z - (z0 + nz * Cell / 2)) < nz * Cell / 2 + 20).ToList();
        var walls = train.Walls;
        var free = new sbyte[nx * nz]; // 0 unknown, 1 free, −1 blocked
        bool Free(int i, int k)
        {
            ref sbyte f = ref free[i * nz + k];
            if (f == 0)
            {
                var p = At(i, k);
                bool blocked = false;
                if (walls is not null)
                    foreach (var w in walls.Near(p))
                    {
                        var l = w.ToLocal(p);
                        double dx = Math.Max(0, Math.Abs(l.X) - w.HalfLength), dz = Math.Max(0, Math.Abs(l.Z) - w.HalfWidth);
                        if (dx * dx + dz * dz < clearance * clearance)
                        {
                            blocked = true;
                            break;
                        }
                    }
                if (!blocked)
                    foreach (var c in cars)
                    {
                        var l = c.ToLocal(p with { Y = c.Origin.Y });
                        var b = c.Shape.Bounds;
                        // Its steps stand out past its sides, and the coupling gaps between cars aren't a way through.
                        if (l.X > b.Min.X - clearance - StepsOut && l.X < b.Max.X + clearance + StepsOut
                            && l.Z > b.Min.Z - clearance - GapIn && l.Z < b.Max.Z + clearance + GapIn)
                        {
                            blocked = true;
                            break;
                        }
                    }
                f = blocked ? (sbyte)-1 : (sbyte)1;
            }
            return f > 0;
        }
        (int, int) CellOf(Double3 p) => ((int)Math.Round((p.X - x0) / Cell), (int)Math.Round((p.Z - z0) / Cell));
        // The ends: their own squares, or the nearest free one (standing against a car's side, a find by a cupboard).
        (int, int)? Nearest((int I, int K) c)
        {
            for (int r = 0; r <= 4; r++)
                for (int di = -r; di <= r; di++)
                    for (int dk = -r; dk <= r; dk++)
                        if (Math.Max(Math.Abs(di), Math.Abs(dk)) == r && c.I + di >= 0 && c.K + dk >= 0 && c.I + di < nx && c.K + dk < nz
                            && Free(c.I + di, c.K + dk))
                            return (c.I + di, c.K + dk);
            return null;
        }
        if (Nearest(CellOf(from)) is not var (si, sk) || Nearest(CellOf(to)) is not var (gi, gk))
            return null;
        int start = si * nz + sk, goal = gi * nz + gk;
        var g = new Dictionary<int, double> { [start] = 0 };
        var came = new Dictionary<int, int>();
        var open = new PriorityQueue<int, (double F, int Index)>();
        double H(int i, int k)
        {
            double dx = Math.Abs(i - gi), dz = Math.Abs(k - gk);
            return (Math.Max(dx, dz) + (Math.Sqrt(2) - 1) * Math.Min(dx, dz)) * Cell;
        }
        open.Enqueue(start, (H(si, sk), start));
        var closed = new HashSet<int>();
        int[] di8 = [1, -1, 0, 0, 1, 1, -1, -1], dk8 = [0, 0, 1, -1, 1, -1, 1, -1];
        while (open.TryDequeue(out int cur, out _))
        {
            if (cur == goal)
                break;
            if (!closed.Add(cur))
                continue;
            int ci = cur / nz, ck = cur % nz;
            double gc = g[cur];
            if (gc > Longest * 1.5)
                return null;
            for (int d = 0; d < 8; d++)
            {
                int ni = ci + di8[d], nk = ck + dk8[d];
                if (ni < 0 || nk < 0 || ni >= nx || nk >= nz || !Free(ni, nk))
                    continue;
                // Diagonally only where both squares beside it are free too: no cutting a wall's corner.
                if (d >= 4 && (!Free(ci + di8[d], ck) || !Free(ci, ck + dk8[d])))
                    continue;
                int n = ni * nz + nk;
                double step = (d >= 4 ? Math.Sqrt(2) : 1) * Cell, ng = gc + step;
                if (g.TryGetValue(n, out double was) && was <= ng)
                    continue;
                g[n] = ng;
                came[n] = cur;
                open.Enqueue(n, (ng + H(ni, nk), n));
            }
        }
        if (!came.ContainsKey(goal) && goal != start)
            return null;
        // Back from the goal; then only the corners, each the furthest point still in a straight free line of the last.
        var cells = new List<(int I, int K)>();
        for (int c = goal; ; c = came[c])
        {
            cells.Add((c / nz, c % nz));
            if (c == start)
                break;
        }
        cells.Reverse();
        var points = new List<Double3>();
        int from0 = 0;
        while (from0 < cells.Count - 1)
        {
            int far = from0 + 1;
            for (int j = cells.Count - 1; j > from0 + 1; j--)
                if (Clear(cells[from0], cells[j], (i, k) => i >= 0 && k >= 0 && i < nx && k < nz && Free(i, k)))
                {
                    far = j;
                    break;
                }
            points.Add(At(cells[far].I, cells[far].K) with { Y = to.Y });
            from0 = far;
        }
        if (points.Count == 0 || (Flat(points[^1]) - Flat(to)).Length > Cell * 1.5)
            points.Add(to);
        else
            points[^1] = to;
        return points;
    }

    /// <summary>Its length, from a start through each point.</summary>
    public static double Length(Double3 from, IReadOnlyList<Double3> path)
    {
        double total = 0;
        var at = from;
        foreach (var p in path)
        {
            total += (Flat(p) - Flat(at)).Length;
            at = p;
        }
        return total;
    }

    /// <summary>Every square on the straight line between two is free (sampled at a third of a square).</summary>
    static bool Clear((int I, int K) a, (int I, int K) b, Func<int, int, bool> free)
    {
        int steps = (int)Math.Ceiling(Math.Max(Math.Abs(b.I - a.I), Math.Abs(b.K - a.K)) * 3.0);
        for (int s = 0; s <= steps; s++)
        {
            double t = steps == 0 ? 0 : (double)s / steps;
            double fi = a.I + (b.I - a.I) * t, fk = a.K + (b.K - a.K) * t;
            // Each square the line passes near.
            int i0 = (int)Math.Floor(fi), k0 = (int)Math.Floor(fk);
            for (int i = i0; i <= i0 + 1; i++)
                for (int k = k0; k <= k0 + 1; k++)
                    if (Math.Abs(i - fi) < 0.75 && Math.Abs(k - fk) < 0.75 && !free(i, k))
                        return false;
        }
        return true;
    }

    static Double3 Flat(Double3 v) => new(v.X, 0, v.Z);
}
