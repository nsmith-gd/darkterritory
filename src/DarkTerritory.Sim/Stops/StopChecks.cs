namespace DarkTerritory.Sim.Stops;

/// <summary>
/// The invariants every stop is held to (level-design Z.5). A layout that fails one is rerolled (P15); the tests sweep
/// thousands of seeds against the same list.
/// </summary>
static class StopChecks
{
    public static List<StopCheck> Run(StopTuning t, StopTier tt, StopContext cx, StopLayout l, StopDraft g)
    {
        var o = new List<StopCheck>();
        void Add(string name, bool pass, string detail) => o.Add(new StopCheck(name, pass, detail));
        void Skip(string name, string why) => o.Add(new StopCheck(name, true, why, Applies: false));

        if (l.HasYard)
        {
            var storage = l.Buildings.Where(b => b.Kind is BuildingKind.Shed or BuildingKind.Hero).ToList();
            int far = storage.Count(b => !g.NearYardTrack(b, t.Crane.Reach));
            Add("Every storage shed is within crane reach of a track", far == 0, $"{storage.Count - far}/{storage.Count} sheds within {t.Crane.Reach:0} m (P3, P5)");

            // The shortest face is the one the yard was laid out to; tracks that reach it sooner have more straight.
            int shortest = l.Tracks.Min(tr => tr.FaceCars);
            Add("Loading faces suit the tier", shortest >= tt.FaceCars[0] && shortest <= tt.FaceCars[1],
                $"the shortest face holds {shortest} cars; {l.Tier} rolls {tt.FaceCars[0]}–{tt.FaceCars[1]} (P16)");

            var loose = l.Containers.Where(c => c.Kind == ContainerKind.CraneBay && !l.Tracks.Any(tr => tr.Crane is { } rw
                && c.At.S >= rw.From - 0.01 && c.At.S <= rw.To + 0.01 && Math.Abs(c.At.D - tr.FaceStart.D) <= rw.Reach + 0.01)).Count();
            Add("Nothing uncarryable beyond a crane", loose == 0, $"{l.Containers.Count(c => c.Kind == ContainerKind.CraneBay)} crane bays, all under a runway (P2)");

            // Nested spurs never cross: past its own points, every track keeps its distance from every other.
            double closest = double.MaxValue;
            for (int i = 0; i < l.Tracks.Count; i++)
                for (int j = i + 1; j < l.Tracks.Count; j++)
                {
                    var a = l.Tracks[i];
                    var b = l.Tracks[j];
                    if (a.Side != b.Side)
                        continue;
                    foreach (var p in Plan.Samples(b.Path, 2).Where(p => p.S > b.Toe + 30))
                        closest = Math.Min(closest, Near(p, a.Path));
                }
            Add("Tracks never cross", closest >= 4.5, closest == double.MaxValue ? "one track a side" : $"closest {closest:0.0} m apart past the points (P6)");

            bool inZone = l.Tracks.All(tr => tr.Toe > 0 && tr.Path.All(p => p.S >= 0 && p.S <= cx.ZoneLength));
            Add("The yard is inside the level zone", inZone, "switches and track on the zone's straight, level main line");
        }
        else
            Skip("Yard checks", "no yard at this stop");

        int inBuffer = l.Containers.Count(c => c.Zone == StopZone.Village && Math.Abs(c.At.D) < tt.Buffer);
        Add("No loot in the rail buffer", inBuffer == 0, $"nothing within {tt.Buffer:0} m of the main line outside the yard (P13, P19)");

        if (l.HasYard && l.HasVillage && l.VillageSide != l.YardSide)
        {
            double d = l.Crossing is { } c ? l.Tracks.Where(tr => !tr.Across).Min(tr => Math.Abs(tr.Toe - c.S)) : double.MaxValue;
            Add("One crossing, within reach of the throat", d <= tt.CrossingReach,
                l.Crossing is null ? "missing (P10)" : $"{d:0} m from the throat; {l.Tier} allows {tt.CrossingReach:0} (P10)");
        }
        else
            Skip("One crossing, within reach of the throat", "one-sided stop: a crossing is optional");

        var storageCorners = l.Buildings.Where(b => b.Kind is BuildingKind.Shed or BuildingKind.Hero).SelectMany(b => Plan.Corners(b)).ToList();
        if (storageCorners.Count > 0)
        {
            double s0 = storageCorners.Min(p => p.S), s1 = storageCorners.Max(p => p.S), d0 = storageCorners.Min(p => p.D), d1 = storageCorners.Max(p => p.D);
            int inYard = g.RoadPoints.Count(p => p.S >= s0 && p.S <= s1 && p.D >= d0 && p.D <= d1 && Math.Abs(p.D) > 8);
            Add("Roads never enter a yard", inYard == 0, "rail serves the sheds; roads serve the houses (P9)");
        }

        if (l.HasVillage)
        {
            var houses = l.Buildings.Where(b => b.Kind == BuildingKind.House).ToList();
            int lost = houses.Count(h => l.Roads.Min(r => Plan.PolylineDistance(h.Centre, r.Points)) > 25);
            Add("Every house is on a road", lost == 0 && houses.Count >= 3, $"{houses.Count - lost}/{houses.Count} houses within 25 m of a road (P11)");
            int finds = l.Containers.Count(c => c.Zone == StopZone.Village), outliers = houses.Count(h => h.Outlier);
            Add("At least one find, one or two outliers", finds >= 1 && outliers is >= 1 and <= 2, $"{finds} containers, {outliers} outlier{(outliers == 1 ? "" : "s")} (P11, P12)");
        }
        else
            Skip("Village checks", "no village at this stop");

        int clash = 0;
        for (int i = 0; i < l.Buildings.Count; i++)
            for (int j = i + 1; j < l.Buildings.Count; j++)
                if (Plan.Overlap(l.Buildings[i], l.Buildings[j]))
                    clash++;
        foreach (var b in l.Buildings)
        {
            if (g.TouchesRail(b, 1))
                clash++;
            if (b.Zone == StopZone.Village && g.TouchesRoad(b, 0.5))
                clash++;
        }
        Add("Nothing overlaps", clash == 0, $"{l.Buildings.Count} buildings against each other, the track and the roads");

        // GDD App. D.4, D.14: a Holdout for every site, where D.4 puts it, reachable, lit to the approach, off the loading.
        var h = t.Holdouts;
        int want = l.HasYard ? (l.Holdouts.Count(x => x.Second) > 0 ? 2 : 1) : l.Halt is not null ? 1 : 0;
        Add("A Holdout for the site", l.Holdouts.Count == want && want > 0,
            $"{l.Holdouts.Count} ({string.Join(", ", l.Holdouts.Select(x => x.Kind))}) at a {(l.HasYard ? "facility" : "halt")} (App. D.4)");
        var walk = new StopWalk(l.Buildings, l.ZoneLength, cx.MaxLateral, h.WalkCell).From(l.StopPoint);
        int unreached = 0, unsited = 0, unseen = 0, onWalk = 0;
        var loading = l.Containers.Where(c => c.Zone == StopZone.Yard).Select(c => c.At).ToList();
        Pt? nearest = loading.Count > 0 ? loading.MinBy(p => Pt.Distance(p, l.StopPoint)) : null;
        foreach (var ho in l.Holdouts)
        {
            var b = l.Buildings[ho.Building];
            if (walk.To(ho.Door) is null)
                unreached++;
            if (!StopGenerator.Sited(h, b, ho.Site, l.StopPoint))
                unsited++;
            var board = new Pt(-(ho.Site == HoldoutSite.Facility ? h.Approach.Facility : h.Approach.Halt), 0);
            if (!StopWalk.Seen(board, ho.Lamp, l.Buildings, ho.Building, l.ZoneLength))
                unseen++;
            if (ho.Site == HoldoutSite.Facility && !StopGenerator.OffTheLoadingWalk(h, b.Centre, l.StopPoint, nearest, loading))
                onWalk++;
        }
        Add("Every Holdout is where App. D.4 puts it", unsited == 0,
            $"a facility's {h.FacilityDistance[0]:0}–{h.FacilityDistance[1]:0} m from the consist; a halt's within {h.HaltDistance:0} m, a dead town's {h.VillageDistance:0} m, of the line");
        Add("Every Holdout can be walked to from the consist", unreached == 0,
            l.Holdouts.Count == 0 ? "none" : $"{string.Join(", ", l.Holdouts.Select(x => $"{x.Walk:0} m"))} on foot (App. D.14)");
        Add("Every Holdout lamp is seen from the approach", unseen == 0, $"from the {(l.HasYard ? "1 km" : "whistle")} board, past every building (App. D.4)");
        Add("No Holdout on the walk to the loading", onWalk == 0, $"{h.LoadingClear:0} m clear of it, {h.LoadingAngle:0}° off the nearest (App. D.4)");

        // GDD B.6: the outside creatures have somewhere to be.
        int warrens = l.Lairs.Count(x => x.Kind == LairKind.Warren), roosts = l.Lairs.Count(x => x.Kind == LairKind.GauntRoost),
            calls = l.Lairs.Count(x => x.Kind == LairKind.SootCall);
        Add("The outside creatures have somewhere to be", warrens >= 1 && roosts == 1 && calls == 1,
            $"{warrens} warren{(warrens == 1 ? "" : "s")}, {roosts} Gaunt roost, {calls} Soot Child call (B.6)");
        return o;
    }

    /// <summary>
    /// Distance from a point to a track, looking only where the track is level with it along the line: yard track
    /// always runs onwards along the main line, so its points come in order of S.
    /// </summary>
    static double Near(Pt p, IReadOnlyList<Pt> path)
    {
        int lo = 0, hi = path.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (path[mid].S < p.S - 30)
                lo = mid + 1;
            else
                hi = mid;
        }
        double m = double.MaxValue;
        for (int i = Math.Max(1, lo); i < path.Count && path[i - 1].S <= p.S + 30; i++)
            m = Math.Min(m, Plan.SegmentDistance(p, path[i - 1], path[i]));
        return m;
    }
}
