namespace DarkTerritory.Sim.Stops;

/// <summary>
/// The ground of a stop as someone on foot sees it (GDD App. D.14 "recoverability"): cells of open ground, buildings
/// solid, track and roads walkable. <see cref="From"/> is every cell's walking distance from one place (8-connected
/// Dijkstra; the distances don't depend on the order ties are taken in), so "can the crew get there from the train, and
/// how far is it" is a lookup.
/// </summary>
sealed class StopWalk
{
    readonly double _cell, _s0, _d0;
    readonly int _ns, _nd;
    readonly bool[] _solid;

    public StopWalk(IReadOnlyList<StopBuilding> buildings, double zoneLength, double maxLateral, double cell)
    {
        _cell = cell;
        _s0 = -40;
        _d0 = -(maxLateral + 20);
        _ns = (int)Math.Ceiling((zoneLength + 80) / cell);
        _nd = (int)Math.Ceiling((2 * maxLateral + 40) / cell);
        _solid = new bool[_ns * _nd];
        foreach (var b in buildings)
        {
            var c = Plan.Corners(b);
            int i0 = Math.Max(0, I(c.Min(p => p.S))), i1 = Math.Min(_ns - 1, I(c.Max(p => p.S)));
            int j0 = Math.Max(0, J(c.Min(p => p.D))), j1 = Math.Min(_nd - 1, J(c.Max(p => p.D)));
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                    if (Plan.Inside(Centre(i, j), b, cell * 0.2))
                        _solid[i * _nd + j] = true;
        }
    }

    int I(double s) => (int)Math.Floor((s - _s0) / _cell);
    int J(double d) => (int)Math.Floor((d - _d0) / _cell);
    Pt Centre(int i, int j) => new(_s0 + (i + 0.5) * _cell, _d0 + (j + 0.5) * _cell);
    bool Open(int i, int j) => i >= 0 && j >= 0 && i < _ns && j < _nd && !_solid[i * _nd + j];

    /// <summary>
    /// Walking distances from <paramref name="start"/> (or the nearest open cell to it): Dijkstra over the cells on a
    /// bucket queue, a step across a cell costing 5 and a diagonal 7 (√2 near enough), so it's linear in the cells.
    /// </summary>
    public Field From(Pt start)
    {
        var dist = new int[_ns * _nd];
        Array.Fill(dist, int.MaxValue);
        if (Nearest(start) is not var (si, sj))
            return new Field(this, dist);
        var buckets = new Queue<int>[8];
        for (int b = 0; b < buckets.Length; b++)
            buckets[b] = new Queue<int>();
        dist[si * _nd + sj] = 0;
        buckets[0].Enqueue(si * _nd + sj);
        int pending = 1;
        for (int d = 0; pending > 0; d++)
        {
            var q = buckets[d & 7];
            while (q.Count > 0)
            {
                int k = q.Dequeue();
                pending--;
                if (dist[k] != d)
                    continue;
                int i = k / _nd, j = k % _nd;
                for (int di = -1; di <= 1; di++)
                    for (int dj = -1; dj <= 1; dj++)
                    {
                        if (di == 0 && dj == 0 || !Open(i + di, j + dj))
                            continue;
                        // No cutting a building's corner diagonally.
                        bool diagonal = di != 0 && dj != 0;
                        if (diagonal && (!Open(i + di, j) || !Open(i, j + dj)))
                            continue;
                        int n = (i + di) * _nd + j + dj, nd = d + (diagonal ? 7 : 5);
                        if (nd < dist[n])
                        {
                            dist[n] = nd;
                            buckets[nd & 7].Enqueue(n);
                            pending++;
                        }
                    }
            }
        }
        return new Field(this, dist);
    }

    /// <summary>The open cell nearest a point, within a couple of cells of it (a door is just outside its wall).</summary>
    (int I, int J)? Nearest(Pt p)
    {
        int ci = I(p.S), cj = J(p.D);
        (int, int)? best = null;
        double bestD = double.MaxValue;
        for (int i = ci - 2; i <= ci + 2; i++)
            for (int j = cj - 2; j <= cj + 2; j++)
                if (Open(i, j) && Pt.Distance(Centre(i, j), p) is var d && d < bestD)
                    (best, bestD) = ((i, j), d);
        return best;
    }

    public sealed class Field(StopWalk walk, int[] dist)
    {
        /// <summary>How far it is on foot to <paramref name="p"/>; null if it can't be reached.</summary>
        public double? To(Pt p) => walk.Nearest(p) is var (i, j) && dist[i * walk._nd + j] is var d && d != int.MaxValue ? d * walk._cell / 5 : null;
    }

    /// <summary>
    /// A clear sight line from <paramref name="from"/> to <paramref name="to"/> past every footprint but
    /// <paramref name="except"/> (in plan: a lamp is seen past sheds, not over them).
    /// </summary>
    public static bool Seen(Pt from, Pt to, IReadOnlyList<StopBuilding> buildings, int except, double zoneLength)
    {
        for (int i = 0; i < buildings.Count; i++)
            if (i != except && Crosses(from, to, buildings[i]))
                return false;
        return true;
    }

    /// <summary>Does the segment pass through the footprint? Clipped in the footprint's own frame (Liang–Barsky).</summary>
    static bool Crosses(Pt a, Pt b, StopBuilding r)
    {
        var (x0, y0) = Plan.Local(a, r);
        var (x1, y1) = Plan.Local(b, r);
        double hl = r.Length / 2, hw = r.Width / 2, t0 = 0, t1 = 1, dx = x1 - x0, dy = y1 - y0;
        bool Clip(double p, double q)
        {
            if (Math.Abs(p) < 1e-12)
                return q >= 0;
            double u = q / p;
            if (p < 0)
            {
                if (u > t1) return false;
                if (u > t0) t0 = u;
            }
            else
            {
                if (u < t0) return false;
                if (u < t1) t1 = u;
            }
            return true;
        }
        return Clip(-dx, x0 + hl) && Clip(dx, hl - x0) && Clip(-dy, y0 + hw) && Clip(dy, hw - y0) && t0 <= t1;
    }
}
