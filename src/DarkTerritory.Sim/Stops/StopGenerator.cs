using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Stops;

/// <summary>What a stop's layout is fitted to: its level zone, and the route's switch points (route.json junctions).</summary>
/// <param name="Facility">The facility the stop serves, if any: its Holdout's type depends on it (GDD App. D.4).</param>
/// <param name="ExitGrade">The main line's grade (%, up) out of the stop, which the route lays (level-design D.2).</param>
/// <param name="Side">The side (+1 right) the stop's yard, or a village halt's village, must be on: where the line generator put the
/// facility or the platform. 0 lets the stop roll it.</param>
/// <param name="HaltAt">Where along the zone a village's halt already stands (the line generator's platform), if it does.</param>
/// <param name="DeadTown">A dead town rather than a halt (linegen plan §11.3): it has its railway side, a station building and a
/// goods yard with derelict stock (note 302).</param>
public readonly record struct StopContext(double ZoneLength, double PointsLength, double MaxLateral = 220, FacilityKind? Facility = null, double ExitGrade = 0,
    int Side = 0, double? HaltAt = null, bool DeadTown = false);

/// <summary>
/// Generates a stop's layout (level-design Parts D and Z): rail, then roads, then districts, then buildings, then
/// containers; then measures it (P15) and checks it against the invariants (Z.5). An attempt that fails a check or
/// lands outside its tier's difficulty band is rebuilt from the next attempt seed, keeping the closest. Deterministic:
/// the same seed is the same stop on every machine.
/// </summary>
public static partial class StopGenerator
{
    public static StopLayout Generate(StopTuning t, RouteTier tier, ulong seed, StopKind kind, in StopContext cx)
    {
        StopLayout? best = null;
        double bestCost = double.MaxValue;
        for (int attempt = 0; attempt < t.MaxAttempts; attempt++)
        {
            var layout = Attempt(t, tier, seed, kind, cx, attempt);
            var band = Band(t, layout);
            double off = layout.Moves.Score < band[0] ? band[0] - layout.Moves.Score : layout.Moves.Score > band[1] ? layout.Moves.Score - band[1] : 0;
            double cost = (layout.Valid ? 0 : 1000) + off;
            if (cost < bestCost)
            {
                best = layout;
                bestCost = cost;
            }
            if (cost == 0)
                break;
        }
        return best! with
        {
            InBand = bestCost == 0,
            Checks = [.. best!.Checks, BandCheck(t, best!, bestCost == 0)],
        };
    }

    /// <summary>
    /// The powerhouse (level-design P6: the throat's auxiliary buildings are "a natural home for the yard office or
    /// power"): on the yard's side of the main line, around its first switch, clear of the track. −1 if there's no room.
    /// </summary>
    static int PlacePowerhouse(StopDraft g, Dice R, StopTuning t, int side)
    {
        var p = t.Powerhouse;
        double toe = g.Tracks.Min(tr => tr.Toe);
        for (int tries = 0; tries < 60; tries++)
        {
            var b = new StopBuilding(BuildingKind.Powerhouse, StopZone.Yard, toe + R.Range(p.Throat), side * R.Range(p.Offset), p.Size[0], p.Size[1],
                R.Range(-0.1, 0.1))
            { Variant = R.Int(0, 2) };
            if (g.Fits(b, new Fit(Gap: 3, Rail: 4, Road: 2)))
                return g.Add(b);
        }
        return -1;
    }

    /// <summary>
    /// Derelict cars on some of the yard's sidings (level-design D.1, D.2, P18; note 294): the tier's <c>blocked</c> count
    /// of tracks, never the facility's own (its loading modules are there), so never all. Each holds <c>derelict.cars</c>,
    /// at most what its face takes. A switchyard's sidings already have its standing cars (note 187), and none of these.
    /// </summary>
    static void BlockSidings(StopDraft g, Dice R, StopTuning t, StopTier tt)
    {
        var open = g.Tracks.Where(tr => !tr.Primary && tr.Capacity > 0).Select(tr => tr.Index).ToList();
        int n = Math.Min(R.Int(tt.Blocked), open.Count);
        for (int k = 0; k < n; k++)
        {
            int pick = open[R.Int(0, open.Count - 1)];
            open.Remove(pick);
            int at = g.Tracks.FindIndex(tr => tr.Index == pick);
            g.Tracks[at] = g.Tracks[at] with { Derelicts = Math.Clamp(R.Int(t.Derelict.Cars), 1, g.Tracks[at].Capacity) };
        }
    }

    /// <summary>
    /// A dead town's railway side (linegen plan §11.3: "platforms, station building, goods shed, sidings with derelict stock";
    /// note 302). The station building stands behind the halt's platform, clear of the lane up to the village. The goods
    /// yard is out past the tier's rail buffer (P13: its find is loot), on whichever side has room, the far side from the
    /// village first: a siding with its points long lifted, the derelicts standing on it from its buffer stop, and the goods
    /// shed beyond it, its workbench a find at <c>deadTown.goods.find</c>. Anything that doesn't fit is left out, and the
    /// check says so (the stop's rerolled for it, P15). With <c>deadTown.station.open</c> the station stands open (note
    /// 493), with no dice of its own, so the stop's laid as before; nothing's kept in it (it's inside the rail buffer, P13).
    /// </summary>
    static void RailwaySide(StopDraft g, Dice R, StopTuning t, StopTier tt, Pt halt, int side, List<IReadOnlyList<Pt>> sidings)
    {
        var dt = t.DeadTown;
        // The station: behind the platform's back edge (WorldArt.Halt: 2.2 m behind the halt's line), along from the lane.
        double back = Math.Abs(halt.D) + 2.2 + dt.Station.Gap + dt.Station.Size[1] / 2;
        foreach (double along in R.Chance(0.5) ? new[] { 1.0, -1.0 } : [-1.0, 1.0])
        {
            var station = new StopBuilding(BuildingKind.Station, StopZone.Village, halt.S + along * (dt.Station.Size[0] / 2 + R.Range(dt.Station.FromLane)),
                side * back, dt.Station.Size[0], dt.Station.Size[1], 0)
            { Variant = R.Int(0, 2), Open = dt.Station.Open };
            if (g.Fits(station, new Fit(Gap: 1.5, Rail: 2, Road: 1)))
            {
                g.Add(station);
                break;
            }
        }
        // The goods yard.
        var gd = dt.Goods;
        int cars = R.Int(gd.Cars);
        double length = R.Range(gd.Length), beyond = R.Range(gd.Beyond);
        foreach (int gs in new[] { -side, side })
        {
            double d = gs * (tt.Buffer + beyond);
            for (int tries = 0; tries < 12; tries++)
            {
                // Beside the station: the siding's middle within `reach` of the halt along the line.
                double lo = Math.Max(gd.Margin, halt.S - gd.Reach - length / 2), hi = Math.Min(g.ZoneLength - gd.Margin - length, halt.S + gd.Reach - length / 2);
                if (hi < lo)
                    break;
                double s0 = R.Range(lo, hi), s1 = s0 + length;
                // The derelicts from the siding's buffer stop (its far end, up the line), a gap between each.
                var stock = Enumerable.Range(0, cars).Select(c => new StopBuilding(BuildingKind.Derelict, StopZone.Village,
                    s1 - 1 - gd.Car[0] / 2 - c * (gd.Car[0] + gd.CarGap), d, gd.Car[0], gd.Car[1], 0)
                { Variant = R.Int(0, 3) }).ToList();
                var shed = new StopBuilding(BuildingKind.GoodsShed, StopZone.Village, s0 + gd.Shed[0] / 2 + R.Range(4, Math.Max(4, length - gd.Shed[0] - cars * (gd.Car[0] + gd.CarGap))),
                    d + gs * (gd.Car[1] / 2 + 3 + gd.Shed[1] / 2), gd.Shed[0], gd.Shed[1], 0)
                { Variant = R.Int(0, 2), Open = t.Village.OpenSheds };
                var siding = new List<Pt> { new(s0, d), new(s1, d) };
                if (!stock.All(c => g.Fits(c, new Fit(Gap: 0.5, Rail: 4, Road: 1))) || !g.Fits(shed, new Fit(Gap: 1, Rail: 4, Road: 1)) || !g.Clear(siding, 3))
                    continue;
                foreach (var c in stock)
                    g.Add(c);
                int si = g.Add(shed);
                g.AddRail(siding);
                sidings.Add(siding);
                if (R.Chance(gd.Find))
                    g.Contain(ContainerKind.Bench, StopZone.Village, shed.Centre, Math.Abs(shed.D) > 40 ? 2 : 1, 0, si);
                return;
            }
        }
    }

    /// <summary>The tier's band for this kind of stop: a yard's, or a village halt's (P15).</summary>
    public static double[] Band(StopTuning t, StopLayout layout) =>
        layout.HasYard ? t.Tiers[layout.Tier].Band.Yard : t.Tiers[layout.Tier].Band.Village;

    static StopCheck BandCheck(StopTuning t, StopLayout l, bool inBand)
    {
        var b = Band(t, l);
        return new StopCheck("Difficulty inside the tier's band", inBand,
            $"{l.Moves.Score:0} against {l.Tier}'s {b[0]:0}–{b[1]:0}, kept on attempt {l.Attempt + 1} (P15)");
    }

    /// <summary>One attempt, unmeasured against the band.</summary>
    public static StopLayout Attempt(StopTuning t, RouteTier tier, ulong seed, StopKind kind, in StopContext cx, int attempt)
    {
        var tt = t.Tiers[tier];
        ulong s = StopSeed.Of(seed, StopSeed.Attempt, (ulong)attempt);
        var R = new Dice(StopSeed.Of(s, StopSeed.Layout));
        var RR = new Dice(StopSeed.Of(s, StopSeed.Roads));
        var g = new StopDraft(cx.ZoneLength, cx.MaxLateral);
        bool hasYard = kind != StopKind.Village, hasVillage = kind != StopKind.Yard;
        int sY = R.Sign();
        if (cx.Side != 0)
            sY = Math.Sign(cx.Side);
        var form = hasYard ? R.Pick<YardForm>(tt.Forms) : (YardForm?)null;
        var arr = Arrangement.Single;
        if (hasYard && hasVillage)
        {
            arr = R.Pick<Arrangement>(tt.Arrangements);
            // A split yard has track on both sides: its village goes further out, or further along.
            if (form == YardForm.Split && arr == Arrangement.Opposite)
                arr = Arrangement.Setback;
        }
        var vform = hasVillage ? R.Pick<VillageForm>(tt.Villages) : (VillageForm?)null;
        double cutLength = 10 + tt.Empties * t.CarPitch;
        double edge = cx.MaxLateral + 12, zone = cx.ZoneLength;

        YardPlan? yard = null;
        if (hasYard)
            yard = BuildYard(g, new Dice(StopSeed.Of(s, StopSeed.Yard)), t, tt, cx, sY, form!.Value, arr == Arrangement.Along ? 150 : 0);
        double cutFront = yard is null ? 0 : yard.FirstToe - 10;

        // Where the road crosses the line (P10, P19): within the tier's reach of the throat; local keeps it clear of the
        // waiting cars.
        double CrossAt() => yard!.FirstToe - (tt.CrossingClear ? 10 + cutLength + RR.Range(6, 18) : RR.Range(15, tt.CrossingReach - 10));

        Pt? crossing = null, halt = null;
        VillagePlan? village = null;
        var vr = new Dice(StopSeed.Of(s, StopSeed.Village));
        var everywhere = new Reach(4, zone - 4);
        if (hasYard && hasVillage && arr != Arrangement.Along)
        {
            double sc = CrossAt();
            crossing = new Pt(sc, 0);
            int side = -sY;
            double f = RR.Range(tt.VillageOffset);
            if (arr == Arrangement.Setback)
                f = Math.Clamp(f + t.Village.SetbackExtra, t.Village.SetbackRange[0], t.Village.SetbackRange[1]);
            // The through road: over the crossing and away from the yard (P9).
            g.AddRoad(RoadKind.Through, [crossing.Value, new Pt(sc - RR.Range(20, 40), sY * RR.Range(40, 70)), new Pt(sc - RR.Range(60, 140), sY * edge)]);
            Pt entry;
            if (vform == VillageForm.Blocks)
            {
                double vs0 = Math.Max(15, sc - RR.Range(5, 25)), vs1 = Math.Min(zone - 15, vs0 + RR.Range(t.Village.Extent));
                entry = new Pt(vs0, side * f);
                g.AddRoad(RoadKind.Through, [new Pt(vs0 - RR.Range(12, 40), side * edge), new Pt(vs0 - RR.Range(6, 14), side * (f + RR.Range(20, 50))), entry]);
                g.AddRoad(RoadKind.Through, [entry, crossing.Value]);
                village = BuildVillage(g, vr, t, tt, vform!.Value, side, f, entry, everywhere, vs0, vs1, oneColumn: arr == Arrangement.Setback || f > 70);
            }
            else
            {
                entry = new Pt(Math.Clamp(sc + RR.Range(0, 30), 14, zone - 20), side * f);
                g.AddRoad(RoadKind.Through, [crossing.Value, new Pt((sc + entry.S) / 2 + RR.Range(-6, 6), side * f * 0.5), entry]);
                village = BuildVillage(g, vr, t, tt, vform!.Value, side, f, entry, everywhere, 0, 0, false);
            }
        }
        else if (hasYard && hasVillage)
        {
            // Further along the line on the yard's own side, short of its throat.
            int side = sY;
            double f = RR.Range(tt.VillageOffset[0], Math.Min(tt.VillageOffset[1], 70));
            double vs0 = RR.Range(18, 30), vs1 = yard!.FirstToe - 30;
            var reach = new Reach(4, yard.FirstToe - 24);
            Pt entry;
            if (RR.Chance(0.6))
            {
                double sc = RR.Range(vs0 + 25, vs1 - 25);
                crossing = new Pt(sc, 0);
                entry = new Pt(sc + RR.Range(-4, 4), side * f);
                g.AddRoad(RoadKind.Through, [new Pt(sc + RR.Range(-90, 90), -side * edge), new Pt(sc + RR.Range(-15, 15), -side * RR.Range(40, 80)), crossing.Value]);
                g.AddRoad(RoadKind.Through, [crossing.Value, entry]);
            }
            else
            {
                entry = new Pt(vs0 + 8, side * f);
                g.AddRoad(RoadKind.Through, [new Pt(-12, side * RR.Range(70, 150)), entry]);
            }
            village = BuildVillage(g, vr, t, tt, vform!.Value, side, f, entry, reach, vs0, vs1, false);
        }
        else if (hasVillage)
        {
            // A halt on the main line, the village off to one side.
            int side = sY;
            double f = RR.Range(tt.VillageOffset);
            double vs0 = RR.Range(zone * 0.15, zone * 0.45), vs1 = Math.Min(zone - 15, vs0 + RR.Range(t.Village.Extent));
            double sh = vs0 + RR.Range(15, 45);
            if (cx.HaltAt is { } at)
                sh = Math.Clamp(at, 20, zone - 20);
            halt = new Pt(sh, side * t.Village.Halt.Offset);
            Pt entry;
            if (RR.Chance(0.6))
            {
                double sc = RR.Range(vs0 + 50, vs1 - 20);
                crossing = new Pt(sc, 0);
                entry = new Pt(sc + RR.Range(-4, 4), side * f);
                g.AddRoad(RoadKind.Through, [new Pt(sc + RR.Range(-90, 90), -side * edge), new Pt(sc + RR.Range(-15, 15), -side * RR.Range(40, 80)), crossing.Value]);
                g.AddRoad(RoadKind.Through, [crossing.Value, entry]);
            }
            else
            {
                entry = new Pt(vs0, side * f);
                g.AddRoad(RoadKind.Through, [new Pt(vs0 - RR.Range(20, 80), side * edge), new Pt(vs0 - 8, side * (f + RR.Range(10, 30))), entry]);
            }
            g.AddRoad(RoadKind.Lane, [new Pt(sh, side * 7), new Pt(sh + RR.Range(-6, 6), side * f)]);
            village = BuildVillage(g, vr, t, tt, vform!.Value, side, f, entry, everywhere, vs0, vs1, false);
        }
        else if (RR.Chance(0.5))
        {
            double sc = CrossAt();
            crossing = new Pt(sc, 0);
            g.AddRoad(RoadKind.Through, [new Pt(sc + RR.Range(-60, 60), -sY * edge), new Pt(sc + RR.Range(-10, 10), -sY * RR.Range(30, 60)), crossing.Value,
                new Pt(sc - RR.Range(20, 40), sY * RR.Range(40, 70)), new Pt(sc - RR.Range(60, 140), sY * edge)]);
        }
        else
        {
            g.AddRoad(RoadKind.Through, [new Pt(RR.Range(150, 320), -sY * edge), new Pt(RR.Range(60, 120), -sY * RR.Range(50, 90)), new Pt(-12, -sY * RR.Range(30, 60))]);
        }

        // A dead town's railway side (note 302), from its own seed, before the Holdouts and lairs so they're placed round it.
        var sidings = new List<IReadOnlyList<Pt>>();
        if (cx.DeadTown && halt is { } stationAt && village is not null)
            RailwaySide(g, new Dice(StopSeed.Of(s, StopSeed.DeadTown)), t, tt, stationAt, village.Side, sidings);

        // The yard's power and its powerhouse at the throat (level-design D.2), from their own seed.
        var power = PowerState.Live;
        int powerhouse = -1;
        if (hasYard)
        {
            var RP = new Dice(StopSeed.Of(s, StopSeed.Power));
            power = RP.Pick<PowerState>(tt.Power);
            powerhouse = PlacePowerhouse(g, RP, t, sY);
            // Blocked sidings (D.2), from their own seed, so a stop's track and buildings are what they were without them.
            if (cx.Facility != FacilityKind.Switchyard)
                BlockSidings(g, new Dice(StopSeed.Of(s, StopSeed.Blocked)), t, tt);
            // Something to load beside every yard track, on the tiers that say so (the director, 8 Oct; note 352).
            if (tt.EveryTrack)
                StockEveryTrack(g, t);
        }

        // Last, from their own seeds: the Holdouts (App. D.4) and where the outside creatures live (B.6, B.8).
        var stopPoint = StopPointOf(g, halt, zone);
        var walk = new StopWalk(g.Buildings, zone, cx.MaxLateral, t.Holdouts.WalkCell).From(stopPoint);
        var holdouts = PlaceHoldouts(g, new Dice(StopSeed.Of(s, StopSeed.Holdout)), t, cx, stopPoint, halt, walk);
        var lairs = PlaceLairs(g, new Dice(StopSeed.Of(s, StopSeed.Lair)), t, tt, stopPoint, walk);

        var layout = new StopLayout
        {
            Seed = seed,
            Attempt = attempt,
            Tier = tier,
            Kind = kind,
            Form = form,
            VillageForm = vform,
            Arrangement = arr,
            YardSide = hasYard ? sY : 0,
            VillageSide = village?.Side ?? 0,
            VillageOffset = village?.Offset ?? 0,
            ZoneLength = zone,
            Tracks = [.. g.Tracks],
            Buildings = [.. g.Buildings],
            Roads = [.. g.Roads],
            Containers = [.. g.Containers],
            Crossing = crossing,
            Halt = halt,
            HaltLength = halt is null ? 0 : t.Village.Halt.Length,
            StopPoint = stopPoint,
            Power = power,
            Powerhouse = powerhouse,
            ExitGrade = hasYard ? cx.ExitGrade : 0,
            Holdouts = holdouts,
            Sidings = sidings,
            Lairs = lairs,
            CutFront = cutFront,
            CutLength = hasYard ? cutLength : 0,
            Moves = new StopMoves(),
        };
        layout = layout with { Moves = Measure(t, tt, layout) };
        return layout with { Checks = StopChecks.Run(t, tt, cx, layout, g) };
    }

    /// <summary>
    /// P15 (level-design D.1): what the crew does to fill the tier's typical empties from what the yard holds. Each trip
    /// takes the cut into the track with the most left to load (then fewest switches): its switch set and restored, the
    /// cut uncoupled and coupled back, one reversal and one blind move backing out, and the switchman's walk from the
    /// waiting cars to the points and back. At the track, castings go up by crane (a re-spot for every runway's worth of
    /// cars past the first) and crates are carried from their sheds (metres there and back; a heavy crate by two). A
    /// trip fills no more cars than the track holds, or than there is to load beside it. A village adds its round trip
    /// and search.
    /// </summary>
    public static StopMoves Measure(StopTuning t, StopTier tt, StopLayout l)
    {
        var w = t.Score;
        int trips = 0, throws = 0, couplings = 0, reversals = 0, blind = 0, respots = 0, hand = 0, clearances = 0;
        double left = tt.Empties, walk = 0, carry = 0;
        bool blocked = false;
        if (l.HasYard)
        {
            var used = new bool[l.Containers.Count];
            // What's left to load at a track: castings its crane reaches, crates in the sheds beside it.
            IEnumerable<StopContainer> At(YardTrack tr) => l.Containers.Where(c => !used[c.Index] && c.Zone == StopZone.Yard
                && (c.Kind == ContainerKind.CraneBay ? c.Track == tr.Index : c.Building >= 0 && l.Buildings[c.Building].Tracks.Contains(tr.Index)));
            double Load(StopContainer c) => c.Kind switch
            {
                ContainerKind.CraneBay => w.CraneBayLoad,
                ContainerKind.Strongroom => w.StrongroomLoad,
                _ => w.CrateStackLoad,
            };
            var tried = new HashSet<int>();
            while (left > 1e-6)
            {
                var next = l.Tracks.Where(tr => !tried.Contains(tr.Index) && tr.Capacity > 0)
                    .Select(tr => (tr, supply: At(tr).Sum(Load)))
                    .Where(x => x.supply > 1e-6)
                    .OrderByDescending(x => Math.Min(x.supply, x.tr.Capacity)).ThenBy(x => x.tr.Depth).ThenBy(x => x.tr.Index)
                    .Select(x => x.tr).FirstOrDefault();
                if (next is null)
                    break;
                tried.Add(next.Index);
                trips++;
                throws += 2;
                couplings += 2;
                reversals += 1;
                blind += 1;
                // A blocked siding is cleared first (D.1): its derelicts pulled out and put away on a track already worked,
                // two more throws for that one's switch and a reversal.
                if (next.Blocked)
                {
                    clearances++;
                    throws += 2;
                    reversals += 1;
                }
                // The switchman walks from the waiting cars to the points and back; across the line for a split yard's far spur.
                walk += 2 * (Math.Abs(next.Toe - l.CutFront) + (next.Across ? 8 : 0)) * w.WalkFactor;
                double room = Math.Min(left, next.Capacity), craned = 0, filled = 0;
                foreach (var c in At(next).OrderBy(c => c.Kind == ContainerKind.CraneBay ? 0 : 1).ThenBy(c => c.Index).ToList())
                {
                    if (room <= 1e-6)
                        break;
                    used[c.Index] = true;
                    double load = Math.Min(Load(c), room);
                    room -= load;
                    left -= load;
                    filled += load;
                    if (c.Kind == ContainerKind.CraneBay)
                        craned += load;
                    else
                    {
                        // Carried from the shed to the nearest point on the loading face, there and back.
                        double metres = 2 * Plan.PolylineDistance(c.At, next.Path) * w.WalkFactor;
                        carry += metres * (c.Kind == ContainerKind.Strongroom ? w.HeavyCarry : 1);
                    }
                }
                if (next.Crane is { } rw && craned > 0)
                    respots += Math.Max(0, (int)Math.Ceiling(craned / Math.Max(1, (int)(rw.Length / t.CarPitch))) - 1);
                hand += (int)Math.Ceiling(filled - craned - 1e-6);
            }
            if (l.Crossing is { } c0 && c0.S <= l.CutFront && c0.S >= l.CutFront - l.CutLength)
                blocked = true;
        }
        // Power (D.1): low slows the cranes, dead stops them until the switchman's walked to the powerhouse and restarted it.
        double powerWalk = l.Power != PowerState.Live && l.Powerhouse >= 0
            ? 2 * Pt.Distance(new Pt(l.CutFront, 0), l.Buildings[l.Powerhouse].Centre) * w.WalkFactor : 0;
        double power = l.Power switch { PowerState.Low => w.LowPower, PowerState.Dead => w.DeadPower, _ => 0 } + powerWalk / w.MetresPerPoint;
        // A hard pull: the loaded train dragged up a steep grade out of the yard.
        bool hardPull = l.ExitGrade >= w.HardPullFrom;
        double yard = !l.HasYard ? 0 : throws * w.Throw + couplings * w.Coupling + reversals * w.Reversal + blind * w.BlindMove + respots * w.Respot
            + hand * w.HandCar + clearances * w.Clearance + carry / w.CarryMetresPerPoint + walk / w.MetresPerPoint + (blocked ? w.BlockedCrossing : 0)
            + power + (hardPull ? l.ExitGrade * w.HardPull : 0);

        double villageWalk = 0, village = 0;
        int houses = l.Buildings.Count(b => b.Kind == BuildingKind.House);
        if (l.HasVillage)
        {
            var from = l.Halt is { } h ? h : new Pt(l.HasYard ? l.CutFront : l.ZoneLength / 2, 0);
            double far = l.Containers.Where(c => c.Zone == StopZone.Village).Select(c => Pt.Distance(from, c.At)).DefaultIfEmpty(0).Max();
            villageWalk = 2 * far * w.WalkFactor;
            village = villageWalk / w.VillageMetresPerPoint + w.FindOdds / tt.Find + houses * w.PerHouse + l.VillageOffset / w.OffsetPerPoint;
        }
        return new StopMoves
        {
            Empties = tt.Empties,
            Trips = trips,
            Throws = throws,
            Couplings = couplings,
            Reversals = reversals,
            Clearances = clearances,
            BlindMoves = blind,
            Respots = respots,
            HandCars = Math.Max(0, hand),
            CarryWalk = Math.Round(carry, 1),
            Unfilled = l.HasYard ? (int)Math.Ceiling(left - 1e-6) : 0,
            SwitchWalk = Math.Round(walk, 1),
            PowerWalk = Math.Round(powerWalk, 1),
            HardPull = l.HasYard && hardPull,
            CrossingBlocked = blocked,
            VillageWalk = Math.Round(villageWalk, 1),
            Houses = houses,
            Yard = Math.Round(yard, 2),
            Village = Math.Round(village, 2),
            Score = Math.Round(yard + village * (l.HasYard ? w.VillageWeight : 1), 1),
        };
    }
}
