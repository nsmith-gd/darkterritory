using Ballast;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.LineGen;

/// <summary>A horizontal primitive (plan §8.1): curvature from <see cref="K0"/> to <see cref="K1"/> linearly over its length.</summary>
/// <remarks>A tangent has both zero, an arc both equal, a clothoid (Euler spiral) one of each.</remarks>
public readonly record struct HPrim(double Length, double K0, double K1)
{
    public static HPrim Tangent(double length) => new(length, 0, 0);
    public double Deflection => (K0 + K1) / 2 * Length;
}

/// <summary>A position and heading on the ground plane. Heading 0 runs along −Z; positive turns left (as <see cref="RailLine"/>).</summary>
public readonly record struct Pose(double X, double Z, double Heading)
{
    public Double3 Position => new(X, 0, Z);
    public Double3 Forward => new(-Math.Sin(Heading), 0, -Math.Cos(Heading));
    /// <summary>Unit vector to the right of travel.</summary>
    public Double3 Right => new(Math.Cos(Heading), 0, -Math.Sin(Heading));
}

/// <summary>Plane geometry for laying track: turns with transitions, integration, and closing a gap between two poses.</summary>
public static class Geometry
{
    /// <summary>
    /// §8.1: the transition length for a curve of radius <paramref name="radius"/> at <paramref name="speed"/>:
    /// max(40 m, v² / (0.5 R)), clamped to what the kit bends to.
    /// </summary>
    public static double TransitionLength(CurveRules c, double radius, double speed) =>
        Math.Clamp(Math.Max(c.TransitionMinM, speed * speed / (c.TransitionSpeedFactor * Math.Abs(radius))), c.TransitionMinM, c.TransitionMaxM);

    /// <summary>
    /// A turn of <paramref name="deflection"/> radians (positive left) at <paramref name="radius"/>: clothoid in, arc, clothoid
    /// out. Too small a turn for two full transitions is two shorter ones meeting at the same peak curvature.
    /// </summary>
    public static List<HPrim> Turn(CurveRules c, double deflection, double radius, double speed)
    {
        var list = new List<HPrim>();
        if (Math.Abs(deflection) < 1e-9)
            return list;
        double k = Math.Sign(deflection) / Math.Abs(radius);
        double lc = TransitionLength(c, radius, speed);
        double spirals = lc * Math.Abs(k); // deflection of the two transitions together
        if (spirals >= Math.Abs(deflection))
        {
            double l = Math.Abs(deflection) / Math.Abs(k);
            list.Add(new HPrim(l, 0, k));
            list.Add(new HPrim(l, k, 0));
            return list;
        }
        list.Add(new HPrim(lc, 0, k));
        list.Add(new HPrim((Math.Abs(deflection) - spirals) / Math.Abs(k), k, k));
        list.Add(new HPrim(lc, k, 0));
        return list;
    }

    public static double TurnLength(CurveRules c, double deflection, double radius, double speed) => Turn(c, deflection, radius, speed).Sum(p => p.Length);

    /// <summary>The largest turn at <paramref name="radius"/> that fits in <paramref name="length"/>.</summary>
    public static double MaxDeflection(CurveRules c, double length, double radius, double speed)
    {
        double lc = TransitionLength(c, radius, speed);
        // Full form: length = R|δ| + Lc; spirals only: length = 2 R |δ|.
        double full = (length - lc) / Math.Abs(radius);
        if (full >= lc / Math.Abs(radius))
            return full;
        return Math.Max(0, length / (2 * Math.Abs(radius)));
    }

    /// <summary>Steps along a primitive the way the rail model does: exact heading, midpoint position.</summary>
    public static Pose Advance(Pose p, HPrim prim, double step = 1)
    {
        double x = p.X, z = p.Z, h = p.Heading;
        int n = Math.Max(1, (int)Math.Ceiling(prim.Length / step));
        double ds = prim.Length / n;
        for (int i = 0; i < n; i++)
        {
            double u0 = i * ds, u1 = u0 + ds, um = u0 + ds / 2;
            double k0 = K(prim, u0), k1 = K(prim, u1), km = K(prim, um);
            double mid = h + (k0 + km) / 2 * ds / 2;
            x += -Math.Sin(mid) * ds;
            z += -Math.Cos(mid) * ds;
            h += (k0 + k1) / 2 * ds;
        }
        return new Pose(x, z, h);
    }

    static double K(HPrim p, double u) => p.K0 + (p.K1 - p.K0) * (p.Length <= 0 ? 0 : u / p.Length);

    public static Pose Advance(Pose p, IEnumerable<HPrim> prims)
    {
        foreach (var prim in prims)
            p = Advance(p, prim);
        return p;
    }

    public static double Wrap(double angle)
    {
        while (angle > Math.PI)
            angle -= 2 * Math.PI;
        while (angle < -Math.PI)
            angle += 2 * Math.PI;
        return angle;
    }

    /// <summary>
    /// §8.2's G1 connector: a turn, a tangent and a turn (each turn clothoid, arc, clothoid) from <paramref name="from"/>
    /// to <paramref name="to"/>, at <paramref name="radius"/>. Solved by Newton on the first turn's deflection and the
    /// tangent's length, the second turn taking the rest of the heading change. Null when there's no such connector
    /// (the poses too close together for the radius, or pointing apart).
    /// </summary>
    public static List<HPrim>? Connect(CurveRules c, Pose from, Pose to, double radius, double speed, double tolerance = 0.01)
    {
        double dh = Wrap(to.Heading - from.Heading);
        // Seeds: the tangent along the chord between the two, in either turning sense.
        double chord = Math.Atan2(-(to.X - from.X), -(to.Z - from.Z));
        double distance = Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Z - from.Z) * (to.Z - from.Z));
        foreach (double seed in new[] { Wrap(chord - from.Heading), 0.0, dh / 2, Wrap(chord - from.Heading) * 1.5 })
        {
            var solved = Solve(c, from, to, radius, speed, seed, Math.Max(1, distance - 2 * radius * Math.Abs(seed)), dh, tolerance);
            if (solved is not null)
                return solved;
        }
        return null;
    }

    static List<HPrim> Build(CurveRules c, double d1, double tangent, double d2, double radius, double speed)
    {
        var list = Turn(c, d1, radius, speed);
        list.Add(HPrim.Tangent(tangent));
        list.AddRange(Turn(c, d2, radius, speed));
        return list;
    }

    static (double Along, double Across) Error(CurveRules c, Pose from, Pose to, double d1, double t, double dh, double radius, double speed)
    {
        var end = Advance(from, Build(c, d1, t, Wrap(dh - d1), radius, speed));
        // In the target's frame: along its heading, and across to its right.
        double ex = end.X - to.X, ez = end.Z - to.Z;
        var f = to.Forward;
        var r = to.Right;
        return (ex * f.X + ez * f.Z, ex * r.X + ez * r.Z);
    }

    static List<HPrim>? Solve(CurveRules c, Pose from, Pose to, double radius, double speed, double d1, double t, double dh, double tolerance)
    {
        for (int iter = 0; iter < 40; iter++)
        {
            var (ea, er) = Error(c, from, to, d1, t, dh, radius, speed);
            if (Math.Abs(ea) < tolerance && Math.Abs(er) < tolerance)
                return t >= 0 ? Build(c, d1, t, Wrap(dh - d1), radius, speed) : null;
            const double h1 = 1e-5, h2 = 1e-2;
            var (a1, r1) = Error(c, from, to, d1 + h1, t, dh, radius, speed);
            var (a2, r2) = Error(c, from, to, d1, t + h2, dh, radius, speed);
            double j11 = (a1 - ea) / h1, j12 = (a2 - ea) / h2, j21 = (r1 - er) / h1, j22 = (r2 - er) / h2;
            double det = j11 * j22 - j12 * j21;
            if (Math.Abs(det) < 1e-12)
                return null;
            double dd = (-ea * j22 + er * j12) / det, dt = (-er * j11 + ea * j21) / det;
            // Damped: a big step through a turn's worth of heading jumps to another family of solutions.
            double scale = Math.Min(1, 0.4 / Math.Max(1e-9, Math.Abs(dd)));
            d1 += dd * scale;
            t += dt * scale;
            if (t < -200 || Math.Abs(d1) > Math.PI)
                return null;
        }
        return null;
    }

    /// <summary>Merges horizontal primitives and a grade profile into rail segments, splitting at every break of either.</summary>
    /// <param name="grades">(length, grade at start, grade at end) runs covering the same length.</param>
    public static List<TrackSegment> Segments(IReadOnlyList<HPrim> prims, IReadOnlyList<(double Length, double G0, double G1)> grades)
    {
        // Breaks on a millimetre grid, so lengths print short and add up exactly.
        var cuts = new SortedSet<long>();
        double s = 0;
        foreach (var p in prims)
            cuts.Add((long)Math.Round((s += p.Length) * 1000));
        s = 0;
        foreach (var g in grades)
            cuts.Add((long)Math.Round((s += g.Length) * 1000));
        long total = (long)Math.Round(Math.Min(prims.Sum(p => p.Length), grades.Sum(g => g.Length)) * 1000);
        var list = new List<TrackSegment>();
        int hi = 0, vi = 0;
        double hStart = 0, vStart = 0;
        long atMm = 0;
        foreach (long cutMm in cuts)
        {
            if (cutMm <= atMm)
                continue;
            if (cutMm > total)
                break;
            double at = atMm / 1000.0, cut = cutMm / 1000.0;
            while (hi < prims.Count - 1 && at >= hStart + prims[hi].Length - 1e-6)
                hStart += prims[hi++].Length;
            while (vi < grades.Count - 1 && at >= vStart + grades[vi].Length - 1e-6)
                vStart += grades[vi++].Length;
            var hp = prims[hi];
            var vp = grades[vi];
            double k0 = K(hp, at - hStart), k1 = K(hp, cut - hStart);
            double g0 = G(vp, at - vStart), g1 = G(vp, cut - vStart);
            var seg = new TrackSegment((cutMm - atMm) / 1000.0, R(k0), Math.Round(g0, 5));
            if (Math.Abs(k1 - k0) > 1e-12)
                seg = seg with { EndRadius = R(k1) };
            if (Math.Abs(g1 - g0) > 1e-9)
                seg = seg with { EndGradePercent = Math.Round(g1, 5) };
            list.Add(seg);
            atMm = cutMm;
        }
        return list;
    }

    static double G((double Length, double G0, double G1) g, double u) => g.G0 + (g.G1 - g.G0) * (g.Length <= 0 ? 0 : Math.Clamp(u / g.Length, 0, 1));
    static double R(double k) => Math.Abs(k) < 1e-12 ? 0 : Math.Round(1 / k, 4);
}
