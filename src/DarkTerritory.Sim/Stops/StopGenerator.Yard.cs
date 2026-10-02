using Ballast;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.Stops;

public static partial class StopGenerator
{
    /// <summary>What the yard came to: its tracks by index, and where its throat is.</summary>
    sealed record YardPlan(int Side, YardForm Form, double FirstToe, double FaceEnd);

    /// <summary>
    /// A yard (P3, P5, P6, P8): tracks nested off the main line, the first switch to the outermost, all out through
    /// S-curves at the spur radius to a common loading face; a shed row between every pair of tracks and between the
    /// innermost and the main line (the outermost's far side is the facility's own ground, where its loading modules
    /// stand); cranes over some faces (P18); the hero at the far end (P7).
    /// </summary>
    static YardPlan BuildYard(StopDraft g, Dice R, StopTuning t, StopTier tt, in StopContext cx, int side, YardForm form, double shift)
    {
        var k = t.Track;
        int n = form == YardForm.Spur ? 1 : Math.Max(2, R.Int(tt.Sidings));
        if (form == YardForm.Split)
            n = Math.Max(1, n - 1);
        int faceCars = R.Int(tt.FaceCars);
        double fanRadius = R.Range(k.FanRadius), fanAngle = R.Range(k.FanAngle);
        double radius = k.TurnoutRadius;

        // Shrink until it fits the zone: fewer cars on the face, then fewer tracks.
        double Offset(int j, int count) => k.Innermost + k.Pitch * (count - 1 - j);
        double Toe(int j) => k.FirstToe + shift + j * k.ToeSpacing;
        double FaceStart(int j, int count) => Toe(j) + Plan.Turnout(radius, Offset(j, count)).Advance;
        // The face holds its cars and the engine up at the buffer stop, with a little room.
        double FaceLength(int cars) => cars * t.CarPitch + t.EngineLength + 4;
        double End(int count, int cars)
        {
            double e = Enumerable.Range(0, count).Max(j => FaceStart(j, count)) + FaceLength(cars);
            return form == YardForm.Fan ? e + fanRadius * DMath.Sin(fanAngle) + t.Hero.Length[1] : e + 12;
        }
        while (End(n, faceCars) > cx.ZoneLength - 20 && (faceCars > tt.FaceCars[0] || n > 1))
        {
            if (faceCars > tt.FaceCars[0])
                faceCars--;
            else
                n--;
        }

        double face0 = Enumerable.Range(0, n).Max(j => FaceStart(j, n)), faceEnd = face0 + FaceLength(faceCars);
        for (int j = 0; j < n; j++)
        {
            double offset = Offset(j, n), toe = Toe(j);
            var (arc, advance) = Plan.Turnout(radius, offset);
            var segments = new List<TrackSegment>
            {
                new(Math.Round(arc, 3), -side * radius),
                new(Math.Round(arc, 3), side * radius),
                new(Math.Round(faceEnd - (toe + advance), 3)),
            };
            if (form == YardForm.Fan)
            {
                // Concentric about a centre beyond the outermost track, so the tracks keep their pitch round the bend.
                double r = fanRadius + (Offset(0, n) - offset);
                segments.Add(new TrackSegment(Math.Round(fanAngle * r, 3), -side * r));
            }
            AddTrack(g, t, cx, j, side, toe, offset, segments, 2 * arc, faceEnd - (toe + advance), (face0 + faceEnd) / 2 - (toe + advance), face0 - (toe + advance),
                depth: j + 1, primary: j == 0, across: false);
        }

        // P5: rows between neighbouring tracks, and between the innermost and the main line.
        var rows = new List<List<int>>();
        for (int j = 0; j < n; j++)
        {
            var track = g.Tracks[j];
            double d, from;
            int[] adj;
            if (j < n - 1)
            {
                var next = g.Tracks[j + 1];
                d = side * (next.Offset + k.Pitch / 2);
                from = Math.Max(track.FaceStart.S, next.FaceStart.S);
                adj = [j, j + 1];
            }
            else
            {
                d = side * (track.Offset - k.Pitch / 2);
                from = track.FaceStart.S;
                adj = [j];
            }
            rows.Add(ShedRow(g, R, t, from, faceEnd, d, adj, adj.Min() + 1));
        }

        if (form == YardForm.Split)
        {
            // P1, P19: a spur across the main line, with its own switch and its own row. The crew splits across the line.
            double toe = Toe(0) + R.Range(k.SplitToe), offset = k.Innermost;
            int carsAcross = R.Int(tt.FaceCars);
            var (arc, advance) = Plan.Turnout(radius, offset);
            double straight = FaceLength(carsAcross);
            if (toe + advance + straight < cx.ZoneLength - 20)
            {
                int j = g.Tracks.Count;
                AddTrack(g, t, cx, j, -side, toe, offset,
                    [new(Math.Round(arc, 3), side * radius), new(Math.Round(arc, 3), -side * radius), new(Math.Round(straight, 3))],
                    2 * arc, straight, straight / 2, 0, depth: 1, primary: false, across: true);
                var tr = g.Tracks[j];
                rows.Add(ShedRow(g, R, t, tr.FaceStart.S, tr.FaceEnd.S, -side * (offset - k.Pitch / 2), [j], 1));
            }
        }

        // The hero (P7, P8): a fan's chain along the outermost track's curve; otherwise the far shed of the outermost row.
        var heroes = new List<int>();
        if (form == YardForm.Fan)
            heroes = HeroChain(g, R, t, g.Tracks[0], -side, t.Hero.Offset, depth: n + 1);
        if (heroes.Count == 0 && rows[0].Count > 0)
        {
            int far = rows[0].MaxBy(i => g.Buildings[i].S);
            g.Buildings[far] = g.Buildings[far] with { Kind = BuildingKind.Hero };
            heroes.Add(far);
        }

        // Cranes over some loading faces (P18), at least one.
        var runways = new Dictionary<int, CraneRunway>();
        for (int j = 0; j < g.Tracks.Count; j++)
            if (R.Chance(tt.Crane))
                runways[j] = Runway(R, t, tt, g.Tracks[j]);
        if (runways.Count == 0)
            runways[0] = Runway(R, t, tt, g.Tracks[0]);

        // Containers (P2, P14): the yard holds the tier's stock, in bays picked at random across every shed. A bay a
        // runway reaches holds a casting for that crane; any other bay a crate stack. The hero's last shed holds the
        // strongroom.
        var slots = new List<(int Building, Pt At, int Track)>();
        foreach (var row in rows)
            foreach (int bi in row)
            {
                var b = g.Buildings[bi];
                if (b.Kind == BuildingKind.Hero)
                    continue;
                int count = Math.Max(1, (int)(b.Length / t.Crane.BayEvery));
                for (int q = 0; q < count; q++)
                {
                    double x = -b.Length / 2 + (q + 0.5) * b.Length / count;
                    (int Building, Pt At, int Track) slot = (bi, Plan.World(b, x, R.Range(-2.5, 2.5)), -1);
                    foreach (int ti in b.Tracks)
                    {
                        if (!runways.TryGetValue(ti, out var rw))
                            continue;
                        var tr = g.Tracks[ti];
                        double towards = Math.Sign(tr.FaceStart.D - b.D);
                        var at = Plan.World(b, x, towards * (b.Width / 2 - t.Crane.BayInset));
                        if (at.S >= rw.From && at.S <= rw.To && Math.Abs(at.D - tr.FaceStart.D) <= rw.Reach)
                        {
                            slot = (bi, at, ti);
                            break;
                        }
                    }
                    slots.Add(slot);
                }
            }
        for (int i = slots.Count - 1; i > 0; i--)
        {
            int j = (int)(R.F() * (i + 1));
            (slots[i], slots[j]) = (slots[j], slots[i]);
        }
        double stock = tt.Empties * R.Range(tt.Stock) - (heroes.Count > 0 ? t.Score.StrongroomLoad : 0);
        var bays = new Dictionary<int, int>();
        foreach (var (bi, at, ti) in slots)
        {
            if (stock <= 1e-6)
                break;
            var b = g.Buildings[bi];
            if (ti >= 0)
            {
                g.Contain(ContainerKind.CraneBay, StopZone.Yard, at, 0, g.Tracks[ti].Depth, bi, ti);
                bays[ti] = bays.GetValueOrDefault(ti) + 1;
                stock -= t.Score.CraneBayLoad;
            }
            else
            {
                g.Contain(ContainerKind.CrateStack, StopZone.Yard, at, 1, b.Tracks.Count > 0 ? b.Tracks.Min() + 1 : 1, bi);
                stock -= t.Score.CrateStackLoad;
            }
        }
        for (int h = 0; h < heroes.Count; h++)
        {
            var b = g.Buildings[heroes[h]];
            g.Contain(h == heroes.Count - 1 ? ContainerKind.Strongroom : ContainerKind.CrateStack, StopZone.Yard, Plan.World(b, R.Range(-0.25, 0.25) * b.Length, 0), 1, n + 1, heroes[h]);
        }
        foreach (var (ti, rw) in runways)
            g.Tracks[ti] = g.Tracks[ti] with { Crane = rw with { Bays = bays.GetValueOrDefault(ti) } };
        return new YardPlan(side, form, Toe(0), faceEnd);
    }

    /// <param name="faceAt">Distance along the track to its straight; <paramref name="loading"/> past that, the middle of the shared face.</param>
    /// <param name="before">Straight before the shared face begins: an outer track reaches its straight sooner.</param>
    static void AddTrack(StopDraft g, StopTuning t, in StopContext cx, int index, int side, double toe, double offset, List<TrackSegment> segments,
        double faceAt, double faceLength, double loading, double before, int depth, bool primary, bool across)
    {
        var path = Plan.Lay(toe, segments);
        var walk = new Plan.Walker(path);
        // Cars are worked along the loading face (P16; SpurDrill.Capacity): the engine at the buffer stop, the cars behind
        // it back to where the face begins. Track before that (the S-curve out, and an outer track's straight before the
        // face) is the way in.
        double standing = segments.Sum(s => s.Length) - faceAt - before;
        int capacity = Math.Max(0, (int)((standing - 1 - t.EngineLength) / t.CarPitch));
        g.Tracks.Add(new YardTrack(index, side, toe, offset, segments, walk.At(faceAt).P, walk.At(faceAt + faceLength).P)
        {
            Standing = Math.Round(standing, 3),
            Capacity = capacity,
            Loading = Math.Round(faceAt + loading, 3),
            FaceCars = (int)((faceLength - 4 - t.EngineLength + 1e-6) / t.CarPitch),
            Depth = depth,
            Primary = primary,
            Across = across,
        });
        g.AddRail(path);
    }

    static CraneRunway Runway(Dice R, StopTuning t, StopTier tt, YardTrack track)
    {
        double face = track.FaceEnd.S - track.FaceStart.S;
        double length = Math.Min(R.Range(tt.Runway), face - 8);
        double from = track.FaceStart.S + R.Range(4, Math.Max(4, face - length - 4));
        return new CraneRunway(Math.Round(from, 2), Math.Round(from + length, 2), t.Crane.Reach, 0);
    }

    /// <summary>A row of sheds along the line from <paramref name="from"/> to <paramref name="to"/>, centred <paramref name="d"/> out.</summary>
    static List<int> ShedRow(StopDraft g, Dice R, StopTuning t, double from, double to, double d, int[] tracks, int depth)
    {
        var sh = t.Shed;
        double a = from + sh.Trim[0], b = to - sh.Trim[1], length = b - a;
        var placed = new List<int>();
        if (length < sh.MinLength)
            return placed;
        int count = Math.Max(1, Math.Min(R.Int(sh.PerRow), (int)(length / sh.MinLength)));
        var weights = Enumerable.Range(0, count).Select(_ => R.Range(0.8, 1.2)).ToArray();
        double sum = weights.Sum(), x = a;
        foreach (double w in weights)
        {
            double len = w / sum * (length - sh.Gap * (count - 1));
            var shed = new StopBuilding(BuildingKind.Shed, StopZone.Yard, x + len / 2, d, len, sh.Depth, 0) { Tracks = tracks, Variant = R.Int(0, 3) };
            if (g.Fits(shed, new Fit(Gap: 0.5, Rail: 3.5, Road: -1)))
                placed.Add(g.Add(shed));
            x += len + sh.Gap;
        }
        return placed;
    }

    /// <summary>
    /// Sheds laid tangent to a fan track's curve on <paramref name="side"/>, until the curve runs out: the outermost
    /// track's inside, between it and the next track round the bend (its outside is the facility's own ground).
    /// </summary>
    static List<int> HeroChain(StopDraft g, Dice R, StopTuning t, YardTrack track, int side, double offset, int depth)
    {
        // The curve is everything past the loading face.
        int start = track.Path.Select((p, i) => (p, i)).First(x => x.p.S >= track.FaceEnd.S - 0.5).i;
        var curve = track.Path.Skip(start).ToList();
        var placed = new List<int>();
        if (curve.Count < 4)
            return placed;
        var walk = new Plan.Walker(curve);
        int want = R.Int(t.Hero.Count);
        for (double u = 2; u < walk.Total && placed.Count < want;)
        {
            double w = R.Range(t.Hero.Length);
            var (p, tan) = walk.At(u + w / 2);
            var n = tan.Normal * side;
            var c = p + n * offset;
            var b = new StopBuilding(BuildingKind.Hero, StopZone.Yard, c.S, c.D, w, t.Shed.Depth, DMath.Atan2(tan.D, tan.S)) { Tracks = [track.Index], Variant = R.Int(0, 3) };
            if (g.Fits(b, new Fit(Gap: 0.4, Rail: 3.5, Road: -1)))
            {
                placed.Add(g.Add(b));
                u += w + 1.5;
            }
            else
                u += 4;
        }
        return placed;
    }
}
