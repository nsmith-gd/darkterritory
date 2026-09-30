using Ballast;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.Stops;

/// <summary>
/// Seeds for everything in a stop (level-design Z.1): each level is a hash of its parent and its own name and index,
/// never the next draw from a shared stream, so stop 3 never depends on stop 2 and a change to the village rules
/// doesn't reshuffle the yards.
/// </summary>
public static class StopSeed
{
    public static ulong Of(ulong parent, ulong key, ulong index = 0) => Mix(Mix(parent ^ Mix(key)) + index);

    /// <summary>SplitMix64's finaliser: fixed by definition, the same on every machine.</summary>
    static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    // Keys for the sub-streams: arbitrary, and fixed forever (changing one reshuffles that stream on every seed).
    public const ulong Stop = 0x53544f50, Halt = 0x48414c54, Attempt = 0x41545450, Layout = 0x4c41594f, Yard = 0x59415244,
        Roads = 0x524f4144, Village = 0x56494c4c, Loot = 0x4c4f4f54, Holdout = 0x484f4c44, Lair = 0x4c414952;
}

/// <summary>A seeded generator for one sub-system of a stop (PCG32, so the same on every machine).</summary>
sealed class Dice(ulong seed)
{
    Pcg32 _rng = new(seed);
    public double F() => _rng.NextDouble();
    public double Range(double min, double max) => _rng.Range(min, max);
    public double Range(double[] r) => _rng.Range(r[0], r[1]);
    public int Int(int min, int max) => _rng.RangeInclusive(min, max);
    public int Int(int[] r) => _rng.RangeInclusive(r[0], r[1]);
    public bool Chance(double p) => _rng.Chance(p);
    public int Sign() => _rng.Chance(0.5) ? 1 : -1;
    public T Pick<T>(IReadOnlyList<T> items) => _rng.Pick(items);
    public T Pick<T>(IReadOnlyList<Choice> choices) where T : struct, Enum => Choice.Pick<T>(choices, F());
}

/// <summary>Plane geometry in the rail frame: footprints as oriented rectangles, polylines, curves.</summary>
static class Plan
{
    public static Pt Axis(double yaw) => new(Math.Cos(yaw), Math.Sin(yaw));

    public static Pt[] Corners(StopBuilding b, double pad = 0)
    {
        var u = Axis(b.Yaw);
        var v = u.Normal;
        double hl = b.Length / 2 + pad, hw = b.Width / 2 + pad;
        var c = b.Centre;
        return [c + u * hl + v * hw, c - u * hl + v * hw, c - u * hl - v * hw, c + u * hl - v * hw];
    }

    /// <summary>A point in a footprint's own frame: along its length, then across.</summary>
    public static (double X, double Y) Local(Pt p, StopBuilding b)
    {
        var u = Axis(b.Yaw);
        var v = u.Normal;
        var r = p - b.Centre;
        return (r.S * u.S + r.D * u.D, r.S * v.S + r.D * v.D);
    }

    public static Pt World(StopBuilding b, double x, double y)
    {
        var u = Axis(b.Yaw);
        return b.Centre + u * x + u.Normal * y;
    }

    public static bool Inside(Pt p, StopBuilding b, double pad = 0)
    {
        var (x, y) = Local(p, b);
        return Math.Abs(x) <= b.Length / 2 + pad && Math.Abs(y) <= b.Width / 2 + pad;
    }

    /// <summary>Distance from a point to a footprint's edge (0 inside).</summary>
    public static double Distance(Pt p, StopBuilding b)
    {
        var (x, y) = Local(p, b);
        return Math.Sqrt(Math.Pow(Math.Max(Math.Abs(x) - b.Length / 2, 0), 2) + Math.Pow(Math.Max(Math.Abs(y) - b.Width / 2, 0), 2));
    }

    /// <summary>Separating-axis test between two footprints, the first grown by <paramref name="pad"/>.</summary>
    public static bool Overlap(StopBuilding a, StopBuilding b, double pad = 0)
    {
        var ca = Corners(a, pad);
        var cb = Corners(b);
        Span<Pt> axes = [Axis(a.Yaw), Axis(a.Yaw).Normal, Axis(b.Yaw), Axis(b.Yaw).Normal];
        foreach (var ax in axes)
        {
            double a0 = double.MaxValue, a1 = double.MinValue, b0 = double.MaxValue, b1 = double.MinValue;
            foreach (var p in ca) { double x = p.S * ax.S + p.D * ax.D; a0 = Math.Min(a0, x); a1 = Math.Max(a1, x); }
            foreach (var p in cb) { double x = p.S * ax.S + p.D * ax.D; b0 = Math.Min(b0, x); b1 = Math.Max(b1, x); }
            if (a1 < b0 || b1 < a0)
                return false;
        }
        return true;
    }

    public static double SegmentDistance(Pt p, Pt a, Pt b)
    {
        var ab = b - a;
        double l2 = ab.S * ab.S + ab.D * ab.D;
        double t = l2 > 0 ? Math.Clamp(((p.S - a.S) * ab.S + (p.D - a.D) * ab.D) / l2, 0, 1) : 0;
        return Pt.Distance(p, a + ab * t);
    }

    public static double PolylineDistance(Pt p, IReadOnlyList<Pt> pts)
    {
        double m = double.MaxValue;
        for (int i = 1; i < pts.Count; i++)
            m = Math.Min(m, SegmentDistance(p, pts[i - 1], pts[i]));
        return m;
    }

    /// <summary>Points every <paramref name="step"/> metres along a polyline, both ends included.</summary>
    public static List<Pt> Samples(IReadOnlyList<Pt> pts, double step = 2)
    {
        var o = new List<Pt>();
        for (int i = 0; i < pts.Count - 1; i++)
        {
            var a = pts[i];
            var b = pts[i + 1];
            int n = Math.Max(1, (int)Math.Ceiling(Pt.Distance(a, b) / step));
            for (int j = 0; j < n; j++)
                o.Add(a + (b - a) * ((double)j / n));
        }
        if (pts.Count > 0)
            o.Add(pts[^1]);
        return o;
    }

    public static List<Pt> Bezier(Pt a, Pt b, Pt c, Pt e, int n = 28)
    {
        var o = new List<Pt>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            double t = (double)i / n, u = 1 - t;
            o.Add(a * (u * u * u) + b * (3 * u * u * t) + c * (3 * u * t * t) + e * (t * t * t));
        }
        return o;
    }

    /// <summary>A polyline by arc length: the point and unit tangent <paramref name="u"/> metres along.</summary>
    public sealed class Walker
    {
        readonly IReadOnlyList<Pt> _pts;
        readonly double[] _cum;
        public Walker(IReadOnlyList<Pt> pts)
        {
            _pts = pts;
            _cum = new double[pts.Count];
            for (int i = 1; i < pts.Count; i++)
                _cum[i] = _cum[i - 1] + Pt.Distance(pts[i], pts[i - 1]);
        }
        public double Total => _cum[^1];
        public (Pt P, Pt T) At(double u)
        {
            u = Math.Clamp(u, 0, Total);
            int i = 1;
            while (i < _cum.Length - 1 && _cum[i] < u)
                i++;
            var a = _pts[i - 1];
            var b = _pts[i];
            double seg = _cum[i] - _cum[i - 1];
            double t = seg > 0 ? (u - _cum[i - 1]) / seg : 0;
            return (a + (b - a) * t, (b - a).Unit);
        }
    }

    /// <summary>
    /// A branch's segments laid out in the rail frame from its switch at (<paramref name="toe"/>, 0), heading along the
    /// main line: the same integration RailLine does, flat (a facility zone is level and straight).
    /// </summary>
    public static List<Pt> Lay(double toe, IReadOnlyList<TrackSegment> segments, double step = 1)
    {
        var pts = new List<Pt> { new(toe, 0) };
        double s = toe, d = 0, heading = 0;
        foreach (var seg in segments)
        {
            int n = Math.Max(1, (int)Math.Ceiling(seg.Length / step));
            double ds = seg.Length / n;
            // Positive radius curves left (towards −D): the heading from the main line's direction towards +D falls.
            double turn = seg.Radius == 0 ? 0 : -ds / seg.Radius;
            for (int i = 0; i < n; i++)
            {
                double mid = heading + turn / 2;
                s += Math.Cos(mid) * ds;
                d += Math.Sin(mid) * ds;
                heading += turn;
                pts.Add(new(s, d));
            }
        }
        return pts;
    }

    /// <summary>An S-curve's out-and-back arcs to reach <paramref name="offset"/> beside the main line: each arc's length and how far along it takes.</summary>
    public static (double Arc, double Advance) Turnout(double radius, double offset)
    {
        double theta = Math.Acos(1 - offset / (2 * radius));
        return (radius * theta, 2 * radius * Math.Sin(theta));
    }
}
