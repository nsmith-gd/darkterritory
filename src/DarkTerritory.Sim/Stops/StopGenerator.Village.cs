namespace DarkTerritory.Sim.Stops;

public static partial class StopGenerator
{
    /// <summary>How far along the stop a village may reach (a village further along keeps below the yard's throat).</summary>
    readonly record struct Reach(double SMin, double SMax);

    sealed record VillagePlan(int Side, double Offset, VillageForm Form, Pt Entry, int Houses, int Outliers);

    static readonly ContainerKind[] HouseKinds = [ContainerKind.Cupboard, ContainerKind.Cabinet, ContainerKind.Cellar, ContainerKind.UnderFloor];

    /// <summary>
    /// A village (P4, P11, P12, P20) of the given form, from the road in at <paramref name="entry"/>, <paramref name="offset"/>
    /// metres out on <paramref name="side"/>. Houses front their roads, turned off them; outliers sit at the ends of stub
    /// roads; timber outbuildings stand on the side nearest the line. Then containers: a share of the houses, never none.
    /// </summary>
    static VillagePlan BuildVillage(StopDraft g, Dice R, StopTuning t, StopTier tt, VillageForm form, int side, double offset, Pt entry, Reach reach,
        double s0, double s1, bool oneColumn)
    {
        var v = t.Village;
        var houses = new List<int>();
        var outliers = new List<int>();
        var sheds = new List<int>();
        var ends = new List<(Pt V, Pt Dir)>();
        var fit = new Fit(Gap: v.HouseGap, Rail: 6, Road: 2.5, Buffer: tt.Buffer, SMin: reach.SMin, SMax: reach.SMax);

        bool TryHouse(StopBuilding h, double maxRoad)
        {
            if (g.RoadDistance(h.Centre) > maxRoad || !g.Fits(h, fit))
                return false;
            houses.Add(g.Add(h));
            return true;
        }

        switch (form)
        {
            case VillageForm.Blocks:
                Blocks(g, R, t, side, offset, entry, s0, s1, oneColumn, h => TryHouse(h, v.RoadReach), ends);
                break;
            case VillageForm.Street:
            {
                var u = Outward(R, side, entry, reach, g.ZoneLength);
                var n = u.Normal;
                var st = v.Street;
                double len = R.Range(st.Length), bend = R.Range(-st.Bend, st.Bend);
                var pts = Plan.Bezier(entry, entry + u * (len * 0.35), entry + u * (len * 0.7) + n * (bend * 0.6), entry + u * len + n * bend, 24)
                    .Select(p => g.Clamp(p, reach.SMin + 4, reach.SMax - 4)).ToList();
                g.AddRoad(RoadKind.Street, pts);
                HousesAlong(g, R, t, pts, st, 12, h => TryHouse(h, v.RoadReach));
                ends.Add((pts[^1], (pts[^1] - pts[^2]).Unit));
                if (R.Chance(st.LaneChance))
                {
                    var walk = new Plan.Walker(pts);
                    var (p, tan) = walk.At(walk.Total * R.Range(0.35, 0.6));
                    int sgn = R.Sign();
                    var lane = new List<Pt> { p, g.Clamp(p + tan.Normal * (sgn * R.Range(st.Lane)), reach.SMin + 4, reach.SMax - 4) };
                    if (g.Clear(lane, 2))
                    {
                        g.AddRoad(RoadKind.Street, lane);
                        HousesAlong(g, R, t, lane, st, 14, h => TryHouse(h, v.RoadReach));
                        ends.Add((lane[1], (lane[1] - lane[0]).Unit));
                    }
                }
                break;
            }
            case VillageForm.Crossroads:
            {
                var c = v.Crossroads;
                var u = Outward(R, side, entry, reach, g.ZoneLength);
                var n = u.Normal;
                var centre = g.Clamp(entry + u * R.Range(c.Out), reach.SMin + 30, reach.SMax - 30);
                var beyond = g.Clamp(centre + u * R.Range(c.Beyond), reach.SMin + 4, reach.SMax - 4);
                var a0 = g.Clamp(centre - n * R.Range(c.Arm), reach.SMin + 4, reach.SMax - 4);
                var a1 = g.Clamp(centre + n * R.Range(c.Arm), reach.SMin + 4, reach.SMax - 4);
                g.AddRoad(RoadKind.Street, [entry, centre, beyond]);
                g.AddRoad(RoadKind.Street, [a0, centre, a1]);
                int want = R.Int(c.Houses);
                double baseYaw = Math.Atan2(u.D, u.S);
                for (int tries = 0, placed = 0; tries < 120 && placed < want; tries++)
                {
                    double ang = R.Range(0, Math.PI * 2), r = R.Range(c.Radius);
                    var at = centre + new Pt(Math.Cos(ang), Math.Sin(ang)) * r;
                    var h = House(R, t, at) with { Yaw = baseYaw + (R.Chance(0.5) ? 0 : Math.PI / 2) + R.Range(-v.YawJitter, v.YawJitter) };
                    if (TryHouse(h, 14))
                        placed++;
                }
                ends.Add((beyond, u));
                ends.Add((a0, n * -1));
                ends.Add((a1, n));
                break;
            }
            default:
            {
                // Farmsteads: a winding track out, a few farms off it, each a house and a barn. Long walks, few houses.
                var f = v.Farmsteads;
                var u = Outward(R, side, entry, reach, g.ZoneLength);
                var n = u.Normal;
                double len = R.Range(f.Length);
                var pts = Plan.Bezier(entry, entry + u * (len * 0.33) + n * R.Range(-f.Wiggle, f.Wiggle), entry + u * (len * 0.66) + n * R.Range(-f.Wiggle, f.Wiggle),
                    entry + u * len + n * R.Range(-f.Wiggle, f.Wiggle), 30).Select(p => g.Clamp(p, reach.SMin + 4, reach.SMax - 4)).ToList();
                g.AddRoad(RoadKind.Street, pts);
                var walk = new Plan.Walker(pts);
                int farms = R.Int(f.Farms);
                int last = -1;
                for (int k = 0; k < farms; k++)
                {
                    var (p, tan) = walk.At(walk.Total * (0.2 + 0.78 * k / Math.Max(1, farms - 1)));
                    for (int tries = 0; tries < 8; tries++)
                    {
                        int sgn = R.Sign();
                        var nn = tan.Normal * sgn;
                        var end = p + nn * R.Range(f.Drive);
                        var c = end + nn * 8;
                        double yaw = Math.Atan2(tan.D, tan.S) + R.Range(-0.3, 0.3);
                        var h = House(R, t, c) with { Yaw = yaw };
                        var bc = end + nn * 6 + tan * (R.Range(14, 18) * R.Sign());
                        var barn = new StopBuilding(BuildingKind.Barn, StopZone.Village, bc.S, bc.D, 14, 9, yaw + R.Range(-0.2, 0.2)) { Variant = R.Int(0, 3) };
                        var drive = new List<Pt> { p, end };
                        if (!g.Clear(drive, 2) || !g.Fits(h, fit) || !g.Fits(barn, fit with { Road = 2 }) || Plan.Overlap(h, barn, 3))
                            continue;
                        g.AddRoad(RoadKind.Lane, drive);
                        houses.Add(last = g.Add(h));
                        sheds.Add(g.Add(barn));
                        break;
                    }
                }
                if (last >= 0)
                {
                    g.Buildings[last] = g.Buildings[last] with { Outlier = true };
                    outliers.Add(last);
                }
                break;
            }
        }

        // Outliers at the ends of stub roads (P11).
        for (int i = ends.Count - 1; i > 0; i--)
        {
            int j = (int)(R.F() * (i + 1));
            (ends[i], ends[j]) = (ends[j], ends[i]);
        }
        int wantOut = R.Int(v.Outliers);
        foreach (var (at, dir) in ends)
        {
            if (outliers.Count >= wantOut)
                break;
            var end = g.Clamp(at + dir.Unit * R.Range(tt.Stub), reach.SMin + 6, reach.SMax - 6);
            var h = House(R, t, end + dir.Unit * 8, R.Chance(0.4) ? HouseShape.Square : null) with { Outlier = true };
            var stub = new List<Pt> { at, end };
            if (Plan.Samples(stub, 1.5).Any(p => Math.Abs(p.D) < tt.Buffer) || !g.Clear(stub, 1.5) || !g.Fits(h, fit with { Gap = 3 })
                || Plan.Samples(stub, 1.5).Any(p => Plan.Inside(p, h, 0.5)))
                continue;
            g.AddRoad(RoadKind.Stub, stub);
            int hi = g.Add(h);
            houses.Add(hi);
            outliers.Add(hi);
        }

        // Timber outbuildings on the side nearest the line, and sometimes a well.
        for (int tries = 0, want = R.Int(v.Outbuildings), got = 0; tries < 30 && got < want; tries++)
        {
            var c = new Pt(entry.S + R.Range(-40, 40), side * R.Range(tt.Buffer + 4, Math.Max(tt.Buffer + 5, offset - 6)));
            var b = new StopBuilding(BuildingKind.Outbuilding, StopZone.Village, c.S, c.D, 8, 6, R.Range(-0.2, 0.2)) { Variant = R.Int(0, 3) };
            if (g.Fits(b, fit with { Gap = 2, Road = 2 }))
            {
                sheds.Add(g.Add(b));
                got++;
            }
        }
        if (houses.Count > 0 && R.Chance(v.WellChance))
        {
            var h = g.Buildings[R.Pick(houses)];
            for (int tries = 0; tries < 12; tries++)
            {
                var w = new StopBuilding(BuildingKind.Well, StopZone.Village, h.S + R.Range(-18, 18), h.D + R.Range(-18, 18), 5, 5, 0);
                if (g.Fits(w, fit with { Gap = 2, Road = 2 }))
                {
                    g.Add(w);
                    break;
                }
            }
        }

        // P12 + P14: containers only, in a share of the houses that falls with tier; the economy fills them later.
        int before = g.Containers.Count;
        foreach (int hi in houses)
        {
            var h = g.Buildings[hi];
            if (!R.Chance(tt.Find + (h.Outlier ? v.OutlierFindBonus : 0)))
                continue;
            int count = R.Chance(v.SecondFindChance) ? 2 : 1;
            for (int q = 0; q < count; q++)
                g.Contain(R.Pick(HouseKinds), StopZone.Village, Plan.World(h, R.Range(-0.28, 0.28) * h.Length, R.Range(-0.28, 0.28) * h.Width), Band(h.D), 0, hi, outlier: h.Outlier);
        }
        foreach (int si in sheds)
        {
            var b = g.Buildings[si];
            if (R.Chance(0.5))
                g.Contain(b.Kind == BuildingKind.Barn ? ContainerKind.Hayloft : ContainerKind.Bench, StopZone.Village, b.Centre, Band(b.D), 0, si);
        }
        if (g.Containers.Count == before && houses.Count > 0)
        {
            int hi = outliers.Count > 0 ? outliers[0] : houses[0];
            var h = g.Buildings[hi];
            g.Contain(ContainerKind.Cellar, StopZone.Village, h.Centre, Band(h.D), 0, hi, outlier: h.Outlier, floor: true);
        }
        if (houses.Count < 3)
            g.Valid = false;
        return new VillagePlan(side, offset, form, entry, houses.Count, outliers.Count);
    }

    /// <summary>P2's bands: crates close in, pocketable out past 40 m.</summary>
    static int Band(double d) => Math.Abs(d) > 40 ? 2 : 1;

    /// <summary>Road-bounded blocks along a frontage road, densest where the road comes in (the sketch's village).</summary>
    static void Blocks(StopDraft g, Dice R, StopTuning t, int side, double offset, Pt entry, double s0, double s1, bool oneColumn,
        Func<StopBuilding, bool> tryHouse, List<(Pt V, Pt Dir)> ends)
    {
        var bt = t.Village.Blocks;
        double len = s1 - s0;
        int cols = oneColumn ? 1 : R.Chance(bt.TwoColumns) ? 2 : 1;
        int rows = Math.Max(2, Math.Min(cols == 2 ? 3 : 4, (int)Math.Round(len / R.Range(bt.Length))));
        var dl = new List<double> { offset };
        for (int c = 0; c < cols; c++)
            dl.Add(dl[c] + R.Range(bt.Depth));
        var sl = new List<double> { s0 };
        for (int r = 1; r < rows; r++)
            sl.Add(s0 + len * r / rows + R.Range(-5, 5));
        sl.Add(s1);
        var grid = new Pt[cols + 1, rows + 1];
        for (int c = 0; c <= cols; c++)
            for (int r = 0; r <= rows; r++)
                grid[c, r] = g.Clamp(new Pt(sl[r] + (r == 0 && c == 0 ? 0 : R.Range(-5, 5)), side * (dl[c] + (c == 0 ? R.Range(-1, 1) : R.Range(-6, 6)))), 4, g.ZoneLength - 4);
        grid[0, 0] = new Pt(s0, side * offset);

        // Streets: the frontage always; the rest mostly, and none left hanging off the network.
        var edges = new List<((int C, int R) A, (int C, int R) B)>();
        for (int c = 0; c <= cols; c++)
            for (int r = 0; r < rows; r++)
                if (c == 0 || R.Chance(c == cols ? bt.KeepEdge : bt.KeepInner))
                    edges.Add(((c, r), (c, r + 1)));
        for (int r = 0; r <= rows; r++)
            for (int c = 0; c < cols; c++)
                if (R.Chance(r == 0 || r == rows ? bt.KeepEdge : bt.KeepInner))
                    edges.Add(((c, r), (c + 1, r)));
        var reached = new bool[cols + 1, rows + 1];
        for (int r = 0; r <= rows; r++)
            reached[0, r] = true;
        for (bool grew = true; grew;)
        {
            grew = false;
            foreach (var (a, b) in edges)
                if (reached[a.C, a.R] != reached[b.C, b.R])
                {
                    reached[a.C, a.R] = reached[b.C, b.R] = true;
                    grew = true;
                }
        }
        var degree = new int[cols + 1, rows + 1];
        foreach (var (a, b) in edges.Where(e => reached[e.A.C, e.A.R]))
        {
            g.AddRoad(RoadKind.Street, [grid[a.C, a.R], grid[b.C, b.R]]);
            degree[a.C, a.R]++;
            degree[b.C, b.R]++;
        }

        // Houses, block by block, the most nearest the road in.
        var cells = new List<(double D0, double D1, double S0, double S1)>();
        for (int c = 0; c < cols; c++)
            for (int r = 0; r < rows; r++)
                cells.Add((dl[c], dl[c + 1], sl[r], sl[r + 1]));
        double Dist((double D0, double D1, double S0, double S1) cell) => Pt.Distance(new Pt((cell.S0 + cell.S1) / 2, side * (cell.D0 + cell.D1) / 2), entry);
        var ranked = cells.Select((cell, i) => (cell, i)).OrderBy(x => Dist(x.cell)).ThenBy(x => x.i).Select(x => x.cell).ToList();
        for (int rank = 0; rank < ranked.Count; rank++)
        {
            var cell = ranked[rank];
            int want = Math.Max(2, 5 - (int)Math.Round(rank * 3.0 / Math.Max(1, ranked.Count - 1)));
            for (int tries = 0, placed = 0; tries < 60 && placed < want; tries++)
                if (tryHouse(House(R, t, new Pt(R.Range(cell.S0 + 7, cell.S1 - 7), side * R.Range(cell.D0 + 7, cell.D1 - 7)))))
                    placed++;
        }
        for (int c = 1; c <= cols; c++)
            for (int r = 0; r <= rows; r++)
            {
                if (!reached[c, r] || degree[c, r] == 0)
                    continue;
                if (c == cols)
                    ends.Add((grid[c, r], new Pt(R.Range(-0.35, 0.35), side).Unit));
                else if (r == 0)
                    ends.Add((grid[c, r], new Pt(-1, side * 0.3).Unit));
                else if (r == rows)
                    ends.Add((grid[c, r], new Pt(1, side * 0.3).Unit));
            }
        ends.Add((grid[0, 0], new Pt(-1, side * 0.3).Unit));
        ends.Add((grid[0, rows], new Pt(1, side * 0.3).Unit));
    }

    static void HousesAlong(StopDraft g, Dice R, StopTuning t, IReadOnlyList<Pt> road, StreetTuning st, double start, Func<StopBuilding, bool> tryHouse)
    {
        var walk = new Plan.Walker(road);
        for (double u = start; u < walk.Total - 6; u += R.Range(st.Every))
        {
            var (p, tan) = walk.At(u);
            foreach (int side in (ReadOnlySpan<int>)[-1, 1])
            {
                if (R.Chance(st.Skip))
                    continue;
                var c = p + tan.Normal * (side * R.Range(st.Offset));
                tryHouse(House(R, t, c) with { Yaw = Math.Atan2(tan.D, tan.S) + R.Range(-0.26, 0.26) });
            }
        }
    }

    /// <summary>Away from the line, leaning along it towards the middle of where the village may go.</summary>
    static Pt Outward(Dice R, int side, Pt entry, Reach reach, double zone)
    {
        double lo = Math.Max(reach.SMin, 0), hi = Math.Min(reach.SMax, zone);
        return new Pt(Math.Clamp(((lo + hi) / 2 - entry.S) / 160, -0.7, 0.7) + R.Range(-0.25, 0.25), side).Unit;
    }

    /// <summary>A house footprint (P4): a rectangle, an L, a cross or a pair, turned off the line.</summary>
    static StopBuilding House(Dice R, StopTuning t, Pt at, HouseShape? shape = null)
    {
        var s = shape ?? R.Pick<HouseShape>([HouseShape.Rect, HouseShape.Rect, HouseShape.L, HouseShape.L, HouseShape.Cross, HouseShape.Pair, HouseShape.Rect]);
        double l, w;
        FootprintPart[] parts;
        switch (s)
        {
            case HouseShape.L:
                l = R.Range(10, 13); w = R.Range(9, 11);
                parts = [new(0, -w / 4, l, w / 2), new(-l / 2 + l * 0.2, w / 4, l * 0.4, w / 2)];
                break;
            case HouseShape.Cross:
                l = w = R.Range(9, 11);
                parts = [new(0, 0, l, w * 0.42), new(0, 0, l * 0.42, w)];
                break;
            case HouseShape.Pair:
            {
                l = R.Range(13, 16);
                double hw = R.Range(6, 7.5);
                w = hw + 1.2;
                parts = [new(-l / 4, -0.6, l / 2 - 0.4, hw), new(l / 4, 0.6, l / 2 - 0.4, hw)];
                break;
            }
            case HouseShape.Square:
                l = w = R.Range(8, 9.5);
                parts = [new(0, 0, l, w)];
                break;
            default:
                l = R.Range(8, 11); w = R.Range(6.5, 8.5);
                parts = [new(0, 0, l, w)];
                break;
        }
        return new StopBuilding(BuildingKind.House, StopZone.Village, at.S, at.D, l, w, R.Range(-t.Village.YawJitter, t.Village.YawJitter))
        {
            Shape = s,
            Parts = parts,
            Variant = R.Int(0, 8),
        };
    }
}
