using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// The geometry of GDD App. D.4's Holdout rules, shared by the line generator that places them
/// (<c>LineBuilder.LayHoldouts</c>) and the validator that holds every plan to them (<see cref="Check"/>): where the
/// consist stands at a site, where its loading modules are, what a lamp can be seen from, and what's walkable.
/// </summary>
public static class HoldoutSites
{
    /// <summary>The facility's zone start: its far board (the 2 km one, or farther for a heavy train).</summary>
    public static double FarBoard(LinePlan plan, PlanPoi poi) =>
        plan.Signage.Where(s => s.Type == "facility" && s.Edge == "main" && s.Text.StartsWith(poi.Name + " ", StringComparison.Ordinal)).Select(s => s.S)
            .DefaultIfEmpty(poi.Approach.S0).Min();

    /// <summary>The facility's near board: the 1 km board its Holdout lamps are seen from.</summary>
    public static double NearBoard(LinePlan plan, PlanPoi poi) =>
        plan.Signage.Where(s => s.Type == "facility" && s.Edge == "main" && s.Text.StartsWith(poi.Name + " ", StringComparison.Ordinal)).Select(s => s.S)
            .DefaultIfEmpty(poi.S - 1000).Max();

    /// <summary>A halt's or dead town's whistle board (edge, distance along it), where it has one.</summary>
    public static (string Edge, double S)? WhistleBoard(LinePlan plan, PlanLandmark station)
    {
        var board = plan.Signage.FirstOrDefault(s => s.Type == "whistle" && s.For == station.Name);
        return board is null ? null : (board.Edge, board.S);
    }

    /// <summary>
    /// The consist stopped at a facility, as points every few metres from the engine's front back: on a spur, with the
    /// engine at the buffer stop (spec D's layout, `facilities.json` spurLayout) and the rest back through the points
    /// onto the main line; on the main line (the coaling tower), with the tender under the chute at the zone's middle.
    /// </summary>
    public static List<Double3> StoppedConsist(LinePlan plan, RailLine line, PlanPoi poi, Train.TrainTuning train, double step = 5)
    {
        double length = plan.Consist.LengthM;
        var points = new List<Double3>();
        if (poi.SpurEdge is { } spur && plan.Edge(spur).Branch is var b && b >= 0 && b < line.Branches.Count)
        {
            var local = line.Branches[b].Local;
            double back = 0;
            for (; back <= length && back <= local.Length; back += step)
                points.Add(local.Sample(local.Length - back).Position);
            for (double onMain = back - local.Length; onMain <= length - local.Length; onMain += step)
                points.Add(line.Sample(Math.Max(0, poi.S - onMain)).Position);
            return points;
        }
        var g = train.Geometry;
        double front = poi.S + g.EngineLength - g.Engine.TenderLength / 2;
        for (double back = 0; back <= length; back += step)
            points.Add(line.Sample(Math.Clamp(front - back, 0, line.Length)).Position);
        return points;
    }

    /// <summary>
    /// Where the facility's loading modules stand (spec D, as `Run.EnableSites` lays them out): every crate, the capstan,
    /// its handles and sleds, the crane's corners, controls and castings; and the coaling tower's chute lever. The walk to
    /// a Holdout keeps clear of all of them (D.4 "must not share a walking route with the nearest loading module").
    /// </summary>
    public static List<Double3> ModulePoints(PlanPoi poi, RailLine line, FacilityTuning facilities, LinePlan plan)
    {
        var points = new List<Double3>();
        if (poi.SpurEdge is { } spur && plan.Edge(spur).Branch is var b && b >= 0 && b < line.Branches.Count)
        {
            var branch = line.Branches[b];
            var modules = facilities.ModulesOf(poi.Type);
            if (modules.Count == 0)
                return points;
            double mid = branch.Local.Length - facilities.SpurLayout;
            var feature = new RouteFeature(FeatureKind.Facility, poi.S, poi.S + 100, poi.Side, Facility: poi.Type);
            var site = new Site(0, feature, modules, facilities, branch.Local, mid, branch.Side, branch.Toe + mid,
                facilities.Crates.Count[^1], branch.Index, facilities.Crates.Heavy.Count[^1]);
            points.AddRange(site.CrateStack);
            points.AddRange(site.HeavyStack);
            points.AddRange(site.Handles);
            if (site.Has(ModuleKind.Winch))
                points.AddRange([site.Capstan, site.SledFrom, site.SledTo]);
            if (site.Crane is { } crane)
            {
                points.AddRange([crane.Controls, crane.Corner(0, 0), crane.Corner(0, 1), crane.Corner(1, 0), crane.Corner(1, 1)]);
                points.AddRange(crane.Castings.Select(c => c.At));
            }
            return points;
        }
        // The coaling tower's chute: its lever on the facility's side (Run.ChuteAt).
        points.Add(Run.Run.ChuteLever(poi.S, poi.Side, line));
        return points;
    }

    /// <summary>Which side of the stopped consist the modules are on (+1 right of the direction of travel).</summary>
    public static int ModuleSide(LinePlan plan, PlanPoi poi) => poi.SpurEdge is { } spur ? plan.Edge(spur).Side : poi.Side;

    /// <summary>The nearest point of a polyline to (x, z), on the ground plane.</summary>
    public static (Double3 Point, double Distance) Nearest(IReadOnlyList<Double3> points, double x, double z)
    {
        var best = points[0];
        double bestD = double.MaxValue;
        for (int i = 0; i + 1 < points.Count; i++)
        {
            var a = points[i];
            var ab = points[i + 1] - a;
            double len2 = ab.X * ab.X + ab.Z * ab.Z;
            double f = len2 < 1e-9 ? 0 : Math.Clamp(((x - a.X) * ab.X + (z - a.Z) * ab.Z) / len2, 0, 1);
            var p = a + ab * f;
            double d = Math.Sqrt((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z));
            if (d < bestD)
                (best, bestD) = (p, d);
        }
        if (points.Count == 1)
            bestD = Math.Sqrt((best.X - x) * (best.X - x) + (best.Z - z) * (best.Z - z));
        return (best, bestD);
    }

    /// <summary>How close a ground-plane segment comes to a point.</summary>
    public static double SegmentDistance(Double3 a, Double3 b, Double3 p)
    {
        double abx = b.X - a.X, abz = b.Z - a.Z;
        double len2 = abx * abx + abz * abz;
        double f = len2 < 1e-9 ? 0 : Math.Clamp(((p.X - a.X) * abx + (p.Z - a.Z) * abz) / len2, 0, 1);
        double dx = a.X + abx * f - p.X, dz = a.Z + abz * f - p.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Whether the land lets <paramref name="eye"/> see <paramref name="lamp"/>: the ray stays over the ground all the way.</summary>
    /// <param name="clear">How far over the ground it has to pass (the generator asks for more than the validator does, so a
    /// plan's rounding never turns a lamp it placed into one the check refuses).</param>
    public static bool Sees(TerrainField terrain, Double3 eye, Double3 lamp, double step = 5, double clear = 0.25)
    {
        var d = lamp - eye;
        double run = Math.Sqrt(d.X * d.X + d.Z * d.Z);
        int n = Math.Max(2, (int)Math.Ceiling(run / step));
        // The last few metres are the Holdout's own ground: the lamp's on it.
        for (int i = 1; i < n; i++)
        {
            double f = (double)i / n;
            var p = eye + d * f;
            if (run * (1 - f) < 4)
                break;
            if (terrain.Height(p.X, p.Z) > p.Y - clear)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Reachable on foot (D.14 "recoverability"): every step of the straight walk from <paramref name="start"/> to
    /// <paramref name="to"/> is no steeper than walkable, and none is under water. Returns why not, or null.
    /// </summary>
    public static string? Walk(TerrainField terrain, Double3 start, Double3 to, double step)
    {
        var d = to - start;
        double run = Math.Sqrt(d.X * d.X + d.Z * d.Z);
        int n = Math.Max(1, (int)Math.Ceiling(run / step));
        double prev = terrain.Ground(start with { Y = start.Y + 1 });
        double slope = terrain.Rules.WalkableSlope;
        for (int i = 1; i <= n; i++)
        {
            var p = start + d * ((double)i / n);
            double h = terrain.Height(p.X, p.Z);
            if (terrain.WaterAt(p.X, p.Z) is not null)
                return $"water {i * run / n:0} m out";
            // The formation's own shoulder is a step up or down, not a slope: the first few metres off the track are allowed it.
            if (i * run / n > 8 && Math.Abs(h - prev) / (run / n) > slope + 0.05)
                return $"a {Math.Abs(h - prev) / (run / n) * 100:0}% slope {i * run / n:0} m out";
            prev = h;
        }
        return null;
    }

    /// <summary>A Holdout's footprint corners and middle (world): where its ground must be walkable and dry.</summary>
    public static IEnumerable<Double3> Footprint(PlanHoldout h)
    {
        double yaw = h.HeadingDeg * Math.PI / 180;
        var along = new Double3(-Math.Sin(yaw), 0, -Math.Cos(yaw));
        var across = new Double3(Math.Cos(yaw), 0, -Math.Sin(yaw));
        var c = new Double3(h.X, h.Y, h.Z);
        yield return c;
        foreach (int i in new[] { -1, 1 })
            foreach (int j in new[] { -1, 1 })
                yield return c + along * (i * h.Size[1]) + across * (j * h.Size[0]);
    }

    /// <summary>Where the door is, and which way it faces (unit, out of the Holdout towards <paramref name="toward"/>).</summary>
    public static (Double3 Door, Double3 Out) DoorFacing(Double3 centre, double headingDeg, double halfWidth, Double3 toward)
    {
        double yaw = headingDeg * Math.PI / 180;
        var across = new Double3(Math.Cos(yaw), 0, -Math.Sin(yaw));
        double sign = Double3.Dot(toward - centre, across) >= 0 ? 1 : -1;
        return (centre + across * (sign * halfWidth), across * sign);
    }

    public static double Horizontal(Double3 a, Double3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    static Double3 V(double[] a) => new(a[0], a[1], a[2]);

    /// <summary>
    /// The validator's check (plan §16.3 "holdouts", D.4): every site has its Holdouts, each where D.4 says, its lamp in
    /// sight of the approach, its walk clear of the loading modules, and reachable on foot. Returns what's wrong, one line a
    /// fault; empty is a pass. Public so tests can hold a mutated plan to it.
    /// </summary>
    public static List<string> Check(LinePlan plan, RailLine line, TerrainField terrain, HoldoutTuning t, FacilityTuning? facilities, Train.TrainTuning train)
    {
        var faults = new List<string>();
        var p = t.Placement;
        var holdouts = plan.Holdouts;
        foreach (var poi in plan.Pois)
        {
            var mine = holdouts.Where(h => h.Site == poi.Id).ToList();
            bool second = poi.Pad.RadiusM >= p.SecondPadScaleM || p.SecondAtKinds.Contains(Key(poi.Type));
            int want = second ? 2 : 1;
            if (mine.Count != want || mine.Count(h => h.Second) != want - 1)
                faults.Add($"{poi.Name} has {mine.Count} Holdouts, wants {want}");
            var consist = StoppedConsist(plan, line, poi, train);
            var modules = facilities is null ? [] : ModulePoints(poi, line, facilities, plan);
            double board = NearBoard(plan, poi);
            foreach (var h in mine)
            {
                var c = new Double3(h.X, h.Y, h.Z);
                double d = Nearest(consist, h.X, h.Z).Distance;
                if (d < p.FacilityFromConsistM[0] - 0.5 || d > p.FacilityFromConsistM[1] + 0.5)
                    faults.Add($"{h.Id} at {poi.Name} is {d:0} m from the consist, outside {p.FacilityFromConsistM[0]:0}-{p.FacilityFromConsistM[1]:0}");
                foreach (var m in modules)
                    if (SegmentDistance(V(h.From), V(h.Door), m) < p.RouteClearanceM - 0.5)
                    {
                        faults.Add($"{h.Id} at {poi.Name}: the walk to it passes {SegmentDistance(V(h.From), V(h.Door), m):0} m from a loading module");
                        break;
                    }
                if (Math.Abs(h.Board[0] - line.Sample(board).Position.X) > 0.5 || Math.Abs(h.Board[2] - line.Sample(board).Position.Z) > 0.5)
                    faults.Add($"{h.Id} at {poi.Name} isn't seen from the 1 km board");
                if (h.Type == HoldoutType.PrisonCar && !p.SpareSidingAt.Contains(Key(poi.Type)))
                    faults.Add($"{h.Id} is a prison car at {poi.Name}, which has no spare siding");
                if (h.Type == HoldoutType.HaltLockup || p.ShelterAlwaysAt.Contains(Key(poi.Type)) && h.Type != HoldoutType.BarricadedShelter)
                    faults.Add($"{h.Id} at {poi.Name} is a {h.Type}");
                if (h.Zone.Edge != "main" || h.Zone.S1 < poi.S || h.Spur != poi.SpurEdge)
                    faults.Add($"{h.Id} at {poi.Name}: its zone isn't the facility's");
            }
            if (mine.Count == 2 && Horizontal(V([mine[0].X, mine[0].Y, mine[0].Z]), V([mine[1].X, mine[1].Y, mine[1].Z])) < p.SecondApartM - 0.5)
                faults.Add($"{poi.Name}'s two Holdouts are on top of each other");
        }
        var stations = plan.Landmarks.Where(l => l.Type is "halt" or "town").ToList();
        foreach (var (station, i) in stations.Select((s, i) => (s, i)))
        {
            string id = StationId(i);
            var mine = holdouts.Where(h => h.Site == id).ToList();
            if (mine.Count != 1)
            {
                faults.Add($"{station.Name} has {mine.Count} Holdouts, wants 1");
                continue;
            }
            var h = mine[0];
            bool town = station.Type == "town";
            var edgeLine = EdgeLine(plan, line, station.Edge);
            double reach = town ? p.TownFromMainM : p.HaltFromMainM;
            double lateral = terrain.Nearby(h.X, h.Z, reach + 60).Where(n => n.Edge == EdgeIndex(plan, station.Edge)).Select(n => Math.Abs(n.Lateral)).DefaultIfEmpty(double.MaxValue).Min();
            if (lateral > reach + 0.5)
                faults.Add($"{h.Id} at {station.Name} is {lateral:0} m from the line, over {reach:0}");
            if (h.Type != (town ? HoldoutType.BarricadedShelter : HoldoutType.HaltLockup))
                faults.Add($"{h.Id} at {station.Name} is a {h.Type}");
            if (h.SiteKind != (town ? HoldoutSiteKind.DeadTown : HoldoutSiteKind.Halt))
                faults.Add($"{h.Id} at {station.Name} is at a {h.SiteKind}");
            if (WhistleBoard(plan, station) is { } wb && HoldoutBoardAt(plan, line, wb) is { } at
                && (Math.Abs(h.Board[0] - at.X) > 0.5 || Math.Abs(h.Board[2] - at.Z) > 0.5))
                faults.Add($"{h.Id} at {station.Name} isn't seen from the whistle board");
            _ = edgeLine;
        }
        foreach (var h in holdouts)
        {
            var c = new Double3(h.X, h.Y, h.Z);
            if (!Sees(terrain, V(h.Board), V(h.Lamp)))
                faults.Add($"{h.Id} at {h.Name}: its lamp can't be seen from the approach");
            if (Walk(terrain, V(h.From), V(h.Door), p.WalkStepM) is { } why)
                faults.Add($"{h.Id} at {h.Name} isn't reachable on foot: {why}");
            foreach (var corner in Footprint(h))
                if (terrain.WaterAt(corner.X, corner.Z) is not null)
                {
                    faults.Add($"{h.Id} at {h.Name} stands in water");
                    break;
                }
            if (terrain.Nearby(h.X, h.Z, p.TrackClearanceM + h.Size[1] + h.Size[0])
                .Any(n => Horizontal(EdgeLine(plan, line, plan.Alignment[n.Edge].Edge).Sample(n.S).Position, c) < p.TrackClearanceM + h.Size[0] - 0.5))
                faults.Add($"{h.Id} at {h.Name} is on the track");
            if (!p.Size.TryGetValue(h.Type, out var size) || !size.SequenceEqual(h.Size))
                faults.Add($"{h.Id} at {h.Name} isn't a {h.Type}'s size");
            _ = c;
        }
        if (holdouts.Select(h => h.Id).Distinct().Count() != holdouts.Count)
            faults.Add("two Holdouts share an id");
        return faults;
    }

    /// <summary>A halt's or dead town's site id: its order among the line's stations.</summary>
    public static string StationId(int index) => $"stn{index + 1}";

    public static string Key(FacilityKind kind) => char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..];

    /// <summary>An edge's rail line by its id (the main line, or a branch's own).</summary>
    public static RailLine EdgeLine(LinePlan plan, RailLine line, string edge) =>
        edge == "main" ? line : line.Branches[plan.Edge(edge).Branch].Local;

    /// <summary>An edge's index as the terrain numbers them: its place in the plan's alignment (the main line first).</summary>
    public static int EdgeIndex(LinePlan plan, string edge)
    {
        for (int i = 0; i < plan.Alignment.Count; i++)
            if (plan.Alignment[i].Edge == edge)
                return i;
        return -1;
    }

    /// <summary>Where a driver's eye is at a board: on the track at its distance, a cab's height up (holdouts.json boardEyeM).</summary>
    public static Double3? HoldoutBoardAt(LinePlan plan, RailLine line, (string Edge, double S) board, double eye = 0)
    {
        var edgeLine = EdgeLine(plan, line, board.Edge);
        var p = edgeLine.Sample(Math.Clamp(board.S, 0, edgeLine.Length)).Position;
        return p + Double3.Up * eye;
    }
}
