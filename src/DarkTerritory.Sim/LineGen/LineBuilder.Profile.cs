using Ballast;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.LineGen;

/// <summary>Stage 3b (plan §8.3–8.4): every edge's vertical profile, then the rail model built from it all.</summary>
sealed partial class LineBuilder
{
    /// <summary>A stretch of constant grade before the vertical curves go in.</summary>
    sealed record GradeRun(double S0, double S1, double G, bool Hard, bool Soft, double Cap);

    RailLine? _line;
    readonly List<(string Edge, double S, double R)> _verticalRadii = new();

    /// <summary>The runs of grade an edge's script asks for: pieces' own, and drift (soft) where they leave it free.</summary>
    List<GradeRun> Runs(EdgeDraft e)
    {
        var runs = new List<GradeRun>();
        foreach (var item in e.Items)
        {
            if (item.Length < 1e-6)
                continue;
            double cap = Math.Min(item.DriftCap, _t.Profile.DriftMaxGrade);
            if (item.Grades is null)
            {
                // Into a facility the approach climbs or is level, never falls: a long stop on a descent fades the brakes
                // (spec B.5), and the loaded cars wait at the foot of it.
                runs.Add(new GradeRun(item.S0, item.S1, 0, false, true, item.Type == "approach" ? -cap : cap));
                continue;
            }
            foreach (var (f0, f1, g) in item.Grades)
                if (f1 > f0)
                    runs.Add(new GradeRun(item.S0 + f0 * item.Length, item.S0 + f1 * item.Length, g, item.HardLevel, false, cap));
        }
        return runs;
    }

    /// <summary>
    /// Fills the soft runs' grades: the main line toward the regional drift, a dead line gently, an alternate so that it
    /// arrives at the main line's height where it rejoins. Returns false when an alternate can't within its cap.
    /// </summary>
    bool Solve(EdgeDraft e, List<GradeRun> runs, double z0, double? zEnd, double softBias = 0)
    {
        if (e.Role == EdgeRole.Main || e.Role == EdgeRole.DeadLine || e.Role == EdgeRole.Spur)
        {
            double z = z0;
            for (int i = 0; i < runs.Count; i++)
            {
                var r = runs[i];
                if (r.Soft)
                {
                    double target = e.Role == EdgeRole.Main ? Drift(r.S1) : z0 + (Drift(e.Toe + r.S1) - Drift(e.Toe)) * 0.5;
                    double g = (target - z) / Math.Max(1, r.S1 - r.S0) * 100;
                    // A negative cap marks a run that may climb but not fall (a facility's approach).
                    bool noFall = r.Cap < 0;
                    double cap = Math.Min(Math.Abs(r.Cap), e.Role == EdgeRole.Main ? _l.MainGrade : _t.DeadLines.MaxGrade);
                    g = Math.Clamp(g, noFall ? 0 : -Math.Min(cap, _l.DescentGrade), cap);
                    runs[i] = r with { G = Math.Round(g, 3) };
                }
                z += (runs[i].S1 - runs[i].S0) * runs[i].G / 100;
            }
            return true;
        }
        // An alternate: one grade on every soft run, so the fixed pieces plus it come out at the rejoin's height.
        double fixedRise = runs.Where(r => !r.Soft).Sum(r => (r.S1 - r.S0) * r.G / 100);
        double softLength = runs.Where(r => r.Soft).Sum(r => r.S1 - r.S0);
        double need = (zEnd ?? z0) - z0 - fixedRise + softBias;
        double grade = softLength > 1 ? need / softLength * 100 : 0;
        var w = _alts.First(a => a.Edge == e.Id);
        double limit = w.WashoutOnMain ? _l.MainGrade : _l.BranchGrade;
        if (Math.Abs(grade) > limit || softLength <= 1 && Math.Abs(need) > 0.05)
            return false;
        for (int i = 0; i < runs.Count; i++)
            if (runs[i].Soft)
                runs[i] = runs[i] with { G = Math.Round(grade, 4) };
        return true;
    }

    /// <summary>
    /// §8.3: grades joined only by parabolic vertical curves of at least the tier's radius. A curve keeps out of level
    /// track that must stay level (junction pads, holding track); otherwise it's centred on the break.
    /// </summary>
    List<(double Length, double G0, double G1)> Smooth(EdgeDraft e, List<GradeRun> runs)
    {
        // Merge equal neighbours first.
        var merged = new List<GradeRun>();
        foreach (var r in runs)
        {
            if (merged.Count > 0 && Math.Abs(merged[^1].G - r.G) < 1e-6 && merged[^1].Hard == r.Hard)
                merged[^1] = merged[^1] with { S1 = r.S1 };
            else
                merged.Add(r);
        }
        double rv = _l.MinVerticalRadius * 1.15;
        // A drift run too short for the vertical curves at both its ends takes its neighbour's grade instead: fewer,
        // gentler changes rather than curves tighter than the tier allows (§8.3).
        for (int pass = 0; pass < 4; pass++)
        {
            bool changed = false;
            for (int i = 0; i < merged.Count; i++)
            {
                var r = merged[i];
                if (!r.Soft)
                    continue;
                double need = 0;
                if (i > 0)
                    need += Math.Abs(r.G - merged[i - 1].G) / 100 * rv / (merged[i - 1].Hard ? 1 : 2);
                if (i + 1 < merged.Count)
                    need += Math.Abs(merged[i + 1].G - r.G) / 100 * rv / (merged[i + 1].Hard ? 1 : 2);
                if (need <= (r.S1 - r.S0) * 0.9)
                    continue;
                double to = i > 0 && !(i + 1 < merged.Count && merged[i + 1].S1 - merged[i + 1].S0 > merged[i - 1].S1 - merged[i - 1].S0) ? merged[i - 1].G
                    : i + 1 < merged.Count ? merged[i + 1].G : r.G;
                if (Math.Abs(to - r.G) > 1e-6)
                {
                    merged[i] = r with { G = to };
                    changed = true;
                }
            }
            if (!changed)
                break;
            var again = new List<GradeRun>();
            foreach (var r in merged)
            {
                if (again.Count > 0 && Math.Abs(again[^1].G - r.G) < 1e-6 && again[^1].Hard == r.Hard)
                    again[^1] = again[^1] with { S1 = r.S1, Soft = again[^1].Soft && r.Soft };
                else
                    again.Add(r);
            }
            merged = again;
        }
        int n = merged.Count;
        var into = new double[n]; // vertical curve length taken from the end of run i
        var outOf = new double[n]; // and from the start of run i+1... indexed by the later run
        // What each change of grade wants of the runs either side: all of it from a soft run next to a hard one.
        var wantInto = new double[n];
        var wantOut = new double[n];
        for (int i = 0; i + 1 < n; i++)
        {
            var (a, b) = (merged[i], merged[i + 1]);
            double lv = Math.Abs(b.G - a.G) / 100 * rv;
            wantInto[i] = a.Hard ? 0 : b.Hard ? lv : lv / 2;
            wantOut[i + 1] = b.Hard ? 0 : lv - wantInto[i];
        }
        // Room: a run gives at most 90% of itself to the curves at its two ends, shared as they want it, so a short
        // run between two long curves isn't pinched at one end while the other has room over.
        for (int i = 0; i < n; i++)
        {
            double room = (merged[i].S1 - merged[i].S0) * 0.9, want = wantInto[i] + wantOut[i];
            if (want > room && want > 0)
            {
                wantInto[i] *= room / want;
                wantOut[i] *= room / want;
            }
        }
        for (int i = 0; i + 1 < n; i++)
        {
            double dg = Math.Abs(merged[i + 1].G - merged[i].G) / 100;
            if (dg < 1e-7)
                continue;
            // A curve squeezed on one side is squeezed on both, so its two halves stay one radius.
            double lv = dg * rv;
            double full = (merged[i].Hard ? 0 : merged[i + 1].Hard ? lv : lv / 2);
            double scale = Math.Min(full > 0 ? wantInto[i] / full : 1, lv - full > 0 ? wantOut[i + 1] / (lv - full) : 1);
            into[i] = full * scale;
            outOf[i + 1] = (lv - full) * scale;
            if (into[i] + outOf[i + 1] > 1e-6)
                _verticalRadii.Add((e.Id, merged[i].S1, (into[i] + outOf[i + 1]) / dg));
        }
        var list = new List<(double, double, double)>();
        for (int i = 0; i < n; i++)
        {
            var r = merged[i];
            double body = r.S1 - r.S0 - outOf[i] - into[i];
            if (body > 1e-6)
                list.Add((body, r.G, r.G));
            if (i + 1 < n && into[i] + outOf[i + 1] > 1e-6)
                list.Add((into[i] + outOf[i + 1], r.G, merged[i + 1].G));
        }
        return list;
    }

    static double Rise(List<(double Length, double G0, double G1)> grades) => grades.Sum(g => g.Length * (g.G0 + g.G1) / 2 / 100);

    static double HeightAt(List<(double Length, double G0, double G1)> grades, double z0, double s)
    {
        double z = z0, at = 0;
        foreach (var (len, g0, g1) in grades)
        {
            if (at + len >= s)
            {
                double u = s - at, g = g0 + (g1 - g0) * (len > 0 ? u / len : 0);
                return z + u * (g0 + g) / 2 / 100;
            }
            z += len * (g0 + g1) / 2 / 100;
            at += len;
        }
        return z;
    }

    double MainHeight(double s) => HeightAt(Main.Grades, 0, s);

    /// <summary>Profiles every edge and builds the rail model; an alternate's closure is corrected on the built line.</summary>
    string? ProfileAndBuild()
    {
        var mainRuns = Runs(Main);
        Solve(Main, mainRuns, 0, null);
        Main.Grades = Smooth(Main, mainRuns);
        Main.Segments = Geometry.Segments(Main.Prims, Main.Grades);
        foreach (var e in Branches())
        {
            e.Z0 = MainHeight(e.Toe);
            if (e.Role == EdgeRole.Alternate)
                continue;
            var runs = Runs(e);
            Solve(e, runs, e.Z0, null);
            e.Grades = Smooth(e, runs);
            e.Segments = Geometry.Segments(e.Prims, e.Grades);
        }
        foreach (var e in Branches().Where(b => b.Role == EdgeRole.Alternate).ToList())
            if (ProfileAlternate(e, 0) is { } why)
            {
                // Its pieces' own grades on the drift instead, and if it still can't meet the main line, no alternate here.
                foreach (var item in e.Items.Where(i => i.IsPiece && i.Kind is "climb" or "descent" or "summit" or "roller"))
                    item.Grades = null;
                if (ProfileAlternate(e, 0) is { } still)
                    DropAlternate(e, still);
            }
        // Build, then correct each alternate's closure on the line the rail model actually lays.
        for (int iter = 0; iter <= _t.Alignment.ClosureIterations; iter++)
        {
            _line = BuildLine();
            bool closed = true;
            foreach (var e in Branches().Where(b => b.Role == EdgeRole.Alternate))
            {
                var b = _line.Branches[e.Branch];
                var end = b.Local.Sample(b.Local.Length);
                var main = _line.Sample(b.Rejoin);
                var err = end.Position - main.Position;
                double flat = Math.Sqrt(err.X * err.X + err.Z * err.Z);
                double angle = Math.Abs(DMath.Atan2(Double3.Cross(end.Tangent, main.Tangent).Y, Double3.Dot(end.Tangent, main.Tangent)));
                bool ok = flat < _t.Alignment.ClosureTolM * 0.1 && Math.Abs(err.Y) < 0.05 && angle < _t.Alignment.ClosureTolDeg * Math.PI / 180 * 0.5;
                if (ok)
                    continue;
                closed = false;
                if (iter == _t.Alignment.ClosureIterations)
                {
                    if (flat > _t.Alignment.ClosureTolM || angle > _t.Alignment.ClosureTolDeg * Math.PI / 180)
                    {
                        DropAlternate(e, $"closes {flat:0.00} m and {angle * 180 / Math.PI:0.00}° off the main line");
                        _line = BuildLine();
                        break;
                    }
                    continue;
                }
                if (Reclose(e, err, b) is { } why)
                {
                    DropAlternate(e, why);
                    _line = BuildLine();
                    break;
                }
            }
            if (closed)
                break;
        }
        return null;
    }

    IEnumerable<EdgeDraft> Branches() => _edges.Values.Where(e => e.Role != EdgeRole.Main).OrderBy(e => e.Branch);

    string? ProfileAlternate(EdgeDraft e, double bias)
    {
        var runs = Runs(e);
        double zEnd = MainHeight(e.Rejoin!.Value);
        if (!Solve(e, runs, e.Z0, zEnd, bias))
            return $"{e.Id} can't climb {zEnd - e.Z0:0} m within its grade cap";
        e.Grades = Smooth(e, runs);
        // Vertical curves shift the height a little: take it out of the soft runs.
        double miss = zEnd - (e.Z0 + Rise(e.Grades));
        if (Math.Abs(miss) > 0.01)
        {
            runs = Runs(e);
            if (!Solve(e, runs, e.Z0, zEnd, bias + miss))
                return $"{e.Id} can't meet the main line's height";
            e.Grades = Smooth(e, runs);
        }
        e.Segments = Geometry.Segments(e.Prims, e.Grades);
        return null;
    }

    /// <summary>
    /// The built alternate missed the main line by <paramref name="err"/> (a grade shortens the ground-plane distance a
    /// little, and the vertical curves add their bit): the closure connector is solved again for a target shifted back by
    /// the miss, and the profile redone. A fixed-point iteration: the miss hardly depends on the connector.
    /// </summary>
    string? Reclose(EdgeDraft e, Double3 err, Branch built)
    {
        int ci = e.Items.FindIndex(i => i.Kind == "closure");
        var closure = e.Items[ci];
        var (from, target) = _closures[e.Id];
        target = new Pose(target.X - err.X, target.Z - err.Z, target.Heading);
        _closures[e.Id] = (from, target);
        var prims = Close(from, target);
        if (prims is null)
            return $"{e.Id} won't re-close";
        double newLength = prims.Sum(p => p.Length);
        double shift = newLength - closure.Length;
        closure.Prims = prims;
        closure.S1 = closure.S0 + newLength;
        for (int i = ci + 1; i < e.Items.Count; i++)
        {
            e.Items[i].S0 += shift;
            e.Items[i].S1 += shift;
        }
        e.Prims = [.. e.Items.SelectMany(i => i.Prims)];
        return ProfileAlternate(e, 0);
    }

    readonly Dictionary<string, (Pose From, Pose Target)> _closures = new();

    static double Heading(Double3 t) => DMath.Atan2(-t.X, -t.Z);

    RailLine BuildLine()
    {
        foreach (var e in _edges.Values)
            foreach (var seg in e.Segments)
                if (!double.IsFinite(seg.Length) || !double.IsFinite(seg.Radius) || !double.IsFinite(seg.GradePercent) || seg.EndGradePercent is { } eg && !double.IsFinite(eg) || seg.EndRadius is { } er && !double.IsFinite(er))
                    throw new InvalidOperationException($"{e.Id}: a segment isn't finite: {seg}; items {string.Join(", ", e.Items.Where(i => i.Grades?.Any(g => !double.IsFinite(g.G)) == true).Select(i => i.ToString()))}");
        var main = new LineDefinition(_p.RouteId, Main.Segments);
        var defs = Branches().Select(e => new BranchDefinition(e.Role switch
        {
            EdgeRole.Alternate => BranchKind.Alternate,
            EdgeRole.Spur => BranchKind.Spur,
            _ => BranchKind.DeadLine,
        }, e.Toe, e.Side, e.Segments)
        { Rejoin = e.Rejoin }).ToList();
        return new RailLine(main, defs);
    }
}
