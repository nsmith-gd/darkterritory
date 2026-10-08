using Ballast;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.LineGen;

/// <summary>Stage 3a (plan §8.1–8.2): each edge's horizontal alignment, and the branches' scripts that depend on it.</summary>
sealed partial class LineBuilder
{
    double _guideA, _guideL1, _guideL2, _guideP1, _guideP2;

    /// <summary>
    /// The main line's slow guide heading: the route's bearing plus a gentle meander, bowing out across a window whose
    /// alternate is shorter than the main line (the main line goes round the hill the alternate goes through).
    /// </summary>
    double Guide(double s)
    {
        if (s <= _gate)
            return 0;
        double u = s - _gate;
        double h = _guideA * (0.65 * DMath.Sin(2 * Math.PI * u / _guideL1 + _guideP1) + 0.35 * DMath.Sin(2 * Math.PI * u / _guideL2 + _guideP2))
            - _guideA * (0.65 * DMath.Sin(_guideP1) + 0.35 * DMath.Sin(_guideP2));
        // Eased in over the threshold: the line leaves the gate on the fortress's bearing.
        h *= Math.Clamp(u / _t.Budget.GraceM, 0, 1);
        foreach (var w in _alts)
            if (w.MainBow != 0 && s > w.T && s < w.J)
            {
                // MainBow is a side, right +1 like the alternate's own; a heading turns left for positive. Added, the main
                // line bowed round toward the alternate and crossed it 300 m past the toe on most nights (note 278).
                double f = (s - w.T) / (w.J - w.T);
                h -= w.MainBow * BowFor(w) * DMath.Sin(2 * Math.PI * f);
            }
        double band = _t.Alignment.BandDeg * Math.PI / 180 * 0.8;
        return Math.Clamp(h, -band, band);
    }

    /// <summary>How far the main line bows across a window: enough that the alternate can be as short as it trades on.</summary>
    double BowFor(AltWindow w) => Math.Clamp(BesselInverse(Math.Min(0.97, w.Ratio / (_t.Alternates.LengthOverChordMin * 1.06))), 0.25, 1.3);

    /// <summary>The β with J0(β) = <paramref name="ratio"/>: a sinusoidal heading deviation of amplitude β shortens a path's
    /// chord to J0(β) of its length.</summary>
    static double BesselInverse(double ratio)
    {
        double lo = 0, hi = 2.4;
        for (int i = 0; i < 50; i++)
        {
            double mid = (lo + hi) / 2;
            if (J0(mid) > ratio)
                lo = mid;
            else
                hi = mid;
        }
        return (lo + hi) / 2;
    }

    static double J0(double x)
    {
        // (1/π) ∫0^π cos(x sin t) dt by Simpson's rule: plenty for a shape.
        const int n = 64;
        double sum = 0;
        for (int i = 0; i <= n; i++)
        {
            double t = Math.PI * i / n, w = i == 0 || i == n ? 1 : i % 2 == 1 ? 4 : 2;
            sum += w * DMath.Cos(x * DMath.Sin(t));
        }
        return sum * (Math.PI / n / 3) / Math.PI;
    }

    /// <summary>Realises the main line's script, item by item, following the guide.</summary>
    bool AlignMain(int attempt)
    {
        var rng = Rng("align", "main", attempt);
        _guideA = _t.Alignment.GuideAmplitudeDeg * Math.PI / 180 * rng.Range(0.5, 1);
        _guideL1 = rng.Range(_t.Alignment.GuideWavelengthKm) * 1000;
        _guideL2 = rng.Range(_t.Alignment.GuideWavelengthKm) * 1000 * 0.4;
        _guideP1 = rng.Range(0, 2 * Math.PI);
        _guideP2 = rng.Range(0, 2 * Math.PI);
        var pose = new Pose(0, 0, 0);
        Main.Prims.Clear();
        _mainPoses = null;
        foreach (var item in Main.Items)
        {
            Realise(item, ref pose, Guide, ref rng, _l.LineSpeed);
            Main.Prims.AddRange(item.Prims);
        }
        return Separated(Main, null) is null;
    }

    /// <summary>Lays one item's horizontal primitives from <paramref name="pose"/>, filling its length exactly.</summary>
    void Realise(Item item, ref Pose pose, Func<double, double> guide, ref Pcg32 rng, double speed)
    {
        var c = _t.Curves;
        double len = item.Length;
        item.Prims.Clear();
        var band = _t.Alignment.BandDeg * Math.PI / 180;
        List<HPrim> turn = [];
        double before = 0;
        double toward = Math.Sign(Geometry.Wrap(guide(item.S1) - pose.Heading));
        if (toward == 0)
            toward = rng.Chance(0.5) ? 1 : -1;
        switch (item.H)
        {
            case HShape.Straight:
                break;
            case HShape.Free:
            case HShape.Gentle:
                {
                    double want = Geometry.Wrap(guide(item.S1) - pose.Heading);
                    // A little wander either way keeps a connector from looking ruled (§8.1).
                    if (Math.Abs(want) < item.Wander * Math.PI / 180 && item.Wander > 0)
                        want += (rng.Chance(0.5) ? 1 : -1) * item.Wander * Math.PI / 180 * rng.Range(0.3, 1);
                    // Wander at its gentle radius; steering back to the guide may curve as tight as a connector does (§8.1: "when
                    // drift approaches the limit, the next connector curves back").
                    double steer = Math.Max(_l.MinRadius * _t.Alignment.ConnectorRadiusFactor, 500);
                    double minR = item.H == HShape.Gentle ? item.MinRadius
                        : Math.Abs(want) > 2 * item.Wander * Math.PI / 180 ? Math.Min(item.MinRadius, steer) : item.MinRadius;
                    minR = Math.Max(minR, _l.MinRadius);
                    // A recovery connector keeps a long straight for its sightline (§7.1) and turns in what's left.
                    double straight = item.Type == "recovery" && len > 1100 ? Math.Min(800, len * 0.6) : 0;
                    double avail = len - straight - 20;
                    if (avail < 60 || Math.Abs(want) < 0.002)
                        break;
                    double maxD = Geometry.MaxDeflection(c, avail, minR, speed);
                    double d = Math.Clamp(want, -maxD, maxD);
                    // §8.1 world band: the heading keeps within ±bandDeg of the route's bearing.
                    d = Math.Clamp(pose.Heading + d, -band, band) - pose.Heading;
                    if (Math.Abs(d) < 0.002)
                        break;
                    // As big a radius as fits: the gentlest curve that makes the turn.
                    double r = minR;
                    for (double tryR = minR * 8; tryR > minR; tryR *= 0.8)
                        if (Geometry.TurnLength(c, d, tryR, speed) <= avail)
                        {
                            r = tryR;
                            break;
                        }
                    turn = Geometry.Turn(c, d, r, speed);
                    before = straight + (avail - turn.Sum(p => p.Length)) / 2;
                    break;
                }
            case HShape.Turn:
                {
                    // A hard bend by a branch turns away from it (note 278), as far as the world band lets it.
                    bool away = item.Params.TryGetValue("turn", out var forced) && forced != 0;
                    double d = item.Deflection * (away ? Math.Sign(forced) : toward);
                    double maxD = Geometry.MaxDeflection(c, len - 20, item.Radius, speed);
                    d = Math.Sign(d) * Math.Min(Math.Abs(d), maxD);
                    if (away)
                        d = Math.Clamp(pose.Heading + d, -band, band) - pose.Heading;
                    else if (Math.Abs(pose.Heading + d) > band)
                        d = -d;
                    turn = Geometry.Turn(c, d, item.Radius, speed);
                    before = (len - turn.Sum(p => p.Length)) / 2;
                    break;
                }
            case HShape.Reverse:
                {
                    // A hard S-bend (note 359): its first turn away from a branch where one's near, as a hard bend's is, and the
                    // way the world band has room for; its straight between as the piece says.
                    bool hard = item.Params.TryGetValue("gapM", out var between);
                    bool away = hard && item.Params.TryGetValue("turn", out var forced) && forced != 0;
                    double d = item.Deflection * (away ? Math.Sign(item.Params["turn"]) : toward);
                    if (hard)
                    {
                        d = Math.Sign(d) * Math.Min(Math.Abs(d), Geometry.MaxDeflection(c, (len - between - 20) / 2, item.Radius, speed));
                        if (away)
                            d = Math.Clamp(pose.Heading + d, -band, band) - pose.Heading;
                        else if (Math.Abs(pose.Heading + d) > band)
                            d = -d;
                    }
                    var out1 = Geometry.Turn(c, d, item.Radius, speed);
                    var back = Geometry.Turn(c, -d, item.Radius, speed);
                    double gap = Math.Max(0, len - out1.Sum(p => p.Length) - back.Sum(p => p.Length) - 20);
                    turn = [.. out1, HPrim.Tangent(hard ? between : Math.Min(gap, 60)), .. back];
                    before = Math.Max(0, (len - turn.Sum(p => p.Length)) / 2);
                    break;
                }
            case HShape.EndCurve:
                {
                    double d = item.Deflection * toward;
                    if (Math.Abs(pose.Heading + d) > band)
                        d = -d;
                    double limit = item.Params.GetValueOrDefault("limitMs", speed);
                    turn = Geometry.Turn(c, d, item.Radius, limit);
                    before = Math.Max(0, len - turn.Sum(p => p.Length) - 10);
                    break;
                }
        }
        double used = before + turn.Sum(p => p.Length);
        if (used > len + 1e-6)
        {
            // Didn't fit after all: straight track, as a safe fallback.
            turn = [];
            before = 0;
            used = 0;
        }
        if (before > 1e-6)
            item.Prims.Add(HPrim.Tangent(before));
        item.Prims.AddRange(turn);
        if (len - used > 1e-6)
            item.Prims.Add(HPrim.Tangent(len - used));
        pose = Geometry.Advance(pose, item.Prims);
    }

    /// <summary>Samples an edge's centre line on the ground plane every <paramref name="step"/> metres, from its start pose.</summary>
    static List<(double S, double X, double Z)> Trace(Pose start, IReadOnlyList<HPrim> prims, double step = 25)
    {
        var list = new List<(double, double, double)> { (0, start.X, start.Z) };
        double s = 0, next = step;
        var pose = start;
        foreach (var p in prims)
        {
            double done = 0;
            while (done < p.Length - 1e-9)
            {
                double take = Math.Min(p.Length - done, next - s);
                double k0 = p.K0 + (p.K1 - p.K0) * (done / p.Length), k1 = p.K0 + (p.K1 - p.K0) * ((done + take) / p.Length);
                pose = Geometry.Advance(pose, new HPrim(take, k0, k1));
                done += take;
                s += take;
                if (s >= next - 1e-9)
                {
                    list.Add((s, pose.X, pose.Z));
                    next += step;
                }
            }
        }
        list.Add((s, pose.X, pose.Z));
        return list;
    }

    readonly Dictionary<string, (Pose Start, List<(double S, double X, double Z)> Trace)> _traces = new();

    Pose[]? _mainPoses;

    /// <summary>The main line's pose at <paramref name="s"/>, from its primitives (before the rail model builds it).</summary>
    Pose PoseOnMain(double s)
    {
        if (_mainPoses is null)
        {
            // Every metre, integrated once: poses are asked for all through the branches' building.
            double length = Main.Prims.Sum(p => p.Length);
            _mainPoses = new Pose[(int)Math.Ceiling(length) + 2];
            var pose = new Pose(0, 0, 0);
            int i = 0;
            double at = 0;
            _mainPoses[0] = pose;
            foreach (var p in Main.Prims)
            {
                double done = 0;
                while (done < p.Length - 1e-9)
                {
                    double next = Math.Min(p.Length, Math.Floor(at + done + 1 + 1e-9) - at);
                    double take = next - done;
                    if (take <= 1e-9)
                        take = Math.Min(1, p.Length - done);
                    double k0 = p.K0 + (p.K1 - p.K0) * (done / p.Length), k1 = p.K0 + (p.K1 - p.K0) * ((done + take) / p.Length);
                    pose = Geometry.Advance(pose, new HPrim(take, k0, k1));
                    done += take;
                    int idx = (int)Math.Round(at + done);
                    if (Math.Abs(at + done - idx) < 1e-6 && idx < _mainPoses.Length)
                        _mainPoses[i = idx] = pose;
                }
                at += p.Length;
            }
            for (int k = i + 1; k < _mainPoses.Length; k++)
                _mainPoses[k] = pose;
        }
        double f = Math.Clamp(s, 0, _mainPoses.Length - 2);
        int a = (int)f;
        double t = f - a;
        var p0 = _mainPoses[a];
        var p1 = _mainPoses[a + 1];
        return new Pose(p0.X + (p1.X - p0.X) * t, p0.Z + (p1.Z - p0.Z) * t, p0.Heading + (p1.Heading - p0.Heading) * t);
    }

    /// <summary>
    /// §8.1 self-intersection: track keeps <c>separationM</c> from other track (other edges, or the same edge more than a
    /// kilometre away along it), except within 2 km of a junction the two share. Checked on a grid. Returns what's too
    /// close, or null.
    /// </summary>
    string? Separated(EdgeDraft edge, IEnumerable<EdgeDraft>? others)
    {
        var a = _t.Alignment;
        var start = edge.Role == EdgeRole.Main ? new Pose(0, 0, 0) : PoseOnMain(edge.Toe);
        var trace = Trace(start, edge.Prims);
        _traces[edge.Id] = (start, trace);
        double sep2 = a.SeparationM * a.SeparationM;
        var grid = new Dictionary<(long, long), List<(string Edge, double S, double X, double Z)>>();
        void Put(string id, List<(double S, double X, double Z)> t)
        {
            foreach (var (s, x, z) in t)
            {
                var key = ((long)Math.Floor(x / a.SeparationM), (long)Math.Floor(z / a.SeparationM));
                if (!grid.TryGetValue(key, out var list))
                    grid[key] = list = new();
                list.Add((id, s, x, z));
            }
        }
        Put(edge.Id, trace);
        foreach (var o in others ?? [])
            if (_traces.TryGetValue(o.Id, out var ot))
                Put(o.Id, ot.Trace);
        foreach (var (s, x, z) in trace)
        {
            long cx = (long)Math.Floor(x / a.SeparationM), cz = (long)Math.Floor(z / a.SeparationM);
            for (long i = cx - 1; i <= cx + 1; i++)
                for (long j = cz - 1; j <= cz + 1; j++)
                {
                    if (!grid.TryGetValue((i, j), out var list))
                        continue;
                    foreach (var (id, os, ox, oz) in list)
                    {
                        if ((ox - x) * (ox - x) + (oz - z) * (oz - z) >= sep2)
                            continue;
                        if (id == edge.Id)
                        {
                            if (Math.Abs(os - s) > a.SameEdgeGapM)
                                return $"{edge.Id} comes back within {a.SeparationM} m of itself at {s:0} m";
                            continue;
                        }
                        if (NearSharedJunction(edge, s, id, os))
                            continue;
                        return $"{edge.Id} at {s:0} m is within {a.SeparationM} m of {id} at {os:0} m";
                    }
                }
        }
        return null;
    }

    /// <summary>Within 2 km of a junction two edges share: their corridors meet there by design.</summary>
    bool NearSharedJunction(EdgeDraft edge, double s, string other, double os)
    {
        double exempt = _t.Alignment.JunctionExemptM;
        var o = _edges[other];
        var p = Where(edge, s);
        var q = Where(o, os);
        bool Near(Pose j) => Dist(p, j) < exempt || Dist(q, j) < exempt;
        foreach (var branch in new[] { edge, o }.Where(e => e.Role != EdgeRole.Main))
        {
            if (Near(PoseOnMain(branch.Toe)))
                return true;
            if (branch.Rejoin is { } rj && Near(PoseOnMain(rj)))
                return true;
        }
        return false;
    }

    static double Dist(Pose a, Pose b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    /// <summary>A traced edge's point at <paramref name="s"/> (to the trace's 25 m).</summary>
    Pose Where(EdgeDraft e, double s)
    {
        var t = _traces[e.Id].Trace;
        int i = Math.Clamp((int)Math.Round(s / 25), 0, t.Count - 1);
        return new Pose(t[i].X, t[i].Z, 0);
    }
}

static class EdgeRoleExtensions
{
    public static EdgeRole ToEdgeRole(this BranchKind kind) => kind switch
    {
        BranchKind.Alternate => EdgeRole.Alternate,
        BranchKind.Spur => EdgeRole.Spur,
        _ => EdgeRole.DeadLine,
    };
}
