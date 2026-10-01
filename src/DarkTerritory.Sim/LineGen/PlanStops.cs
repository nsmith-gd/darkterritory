using System.Globalization;
using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// The line generator hands each facility and settlement to the stop generator (linegen plan §11.1: "the POI generator
/// builds its interior"; level-design Part Z): a facility on a spur gets its yard, and a halt or dead town its village,
/// each with its loot, its Holdouts and where the outside creatures live (GDD App. D, B.6).
/// <list type="bullet">
/// <item>A facility's yard is laid from its junction along the main line, where the generator keeps it straight and
/// level (the holding track before it, the departure lull after, <see cref="LineBuilder"/>). Its first track is the
/// facility's spur, at the plan's own junction; the yard's other tracks join the branches after the plan's.</item>
/// <item>A settlement's village takes the settlement's straight, level stretch as its zone, its halt at the plan's
/// platform, on the platform's side.</item>
/// <item>The ground under each stop is flattened to it by a long pad (id <c>stop:</c>), added to the route's copy of
/// the plan so every machine's terrain has it. Idempotent: a plan that already has them (saved, or sent to a joiner)
/// is given them afresh.</item>
/// </list>
/// Deterministic: every stop is seeded from the plan's own sub-seeds and positions.
/// </summary>
public static class PlanStops
{
    public const string PadPrefix = "stop:";

    /// <summary>A village takes a stretch this nearly straight (radius 100 km and up: under 12 cm of bow over 300 m).</summary>
    const double VillageCurvature = 1e-5;

    /// <summary>How many layouts a facility's stop is drawn from before the facility goes without one.</summary>
    const int Redraws = 4;

    /// <summary>Fills in the stops: features' layouts and zones, the yard tracks as branches, the pads in the plan.</summary>
    public static (List<RouteFeature> Features, List<BranchDefinition> Branches, LinePlan Plan) Add(LinePlan plan, RouteTuning tuning,
        List<RouteFeature> features, List<BranchDefinition> branches, LineDefinition mainLine, ulong routeSeed)
    {
        plan = plan with { Pads = [.. plan.Pads.Where(p => !p.Id.StartsWith(PadPrefix, StringComparison.Ordinal))] };
        if (tuning.Stops is not { } st)
            return (features, branches, plan);
        var main = new RailLine(mainLine);
        var cx = tuning.StopContext;
        var tier = plan.Route.Tier;
        var pads = new List<PlanPad>();
        var yards = new List<(double Start, double End)>();
        var extra = new List<BranchDefinition>();

        for (int i = 0; i < features.Count; i++)
        {
            var f = features[i];
            if (f.Kind != FeatureKind.Facility || f.Facility is null or FacilityKind.CoalingTower or FacilityKind.MineHead)
                continue;
            // The facility's plan: on a spur, with its alignment one of the branches.
            var poi = plan.Pois.FirstOrDefault(p => p.Type == f.Facility && Math.Abs(p.S - (f.Start + tuning.Junctions.SpurToe)) < 0.5);
            if (poi?.SpurEdge is not { } spurEdge || plan.Alignment.FirstOrDefault(a => a.Edge == spurEdge) is not { } spur)
                continue;
            int index = branches.FindIndex(b => b.Kind == BranchKind.Spur && Math.Abs(b.Toe - spur.Toe) < 0.01 && b.Side == spur.Side);
            if (index < 0)
                continue;
            // The yard is laid so its first switch is the plan's junction; its zone has to lie on the straight, level main line
            // (and the cut waiting before it on the straight approach, which may climb a little, §11.1). A layout that reaches
            // back past the level holding track (a village along the line before the yard) is drawn again, a few times, from
            // seeds of its own.
            StopLayout? stop = null;
            double start = 0, end = 0;
            ulong baseSeed = StopSeed.Of(SubSeed(poi.SubSeed), StopSeed.Stop);
            for (int attempt = 0; attempt < Redraws && stop is null; attempt++)
            {
                ulong stopSeed = attempt == 0 ? baseSeed : StopSeed.Of(baseSeed, (ulong)attempt);
                var kind = new Pcg32(stopSeed).Chance(st.Tiers[tier].VillageChance) ? StopKind.YardAndVillage : StopKind.Yard;
                var drawn = StopGenerator.Generate(st, tier, stopSeed, kind, cx with { Facility = f.Facility, Side = poi.Side });
                if (!drawn.Valid || drawn.Tracks.FirstOrDefault(t => t.Primary) is not { } primary)
                    continue;
                start = poi.S - primary.Toe;
                end = start + drawn.ZoneLength;
                if (StraightAndLevel(main, start, end) && StraightAndLevel(main, start - drawn.CutLength, start, level: false))
                    stop = drawn;
            }
            if (stop is null)
                continue;
            var at = f with { Start = start, End = end, Side = stop.YardSide };
            stop = stop with { ExitGrade = RouteGenerator.ExitGradeOf(main, at) };
            features[i] = at with { Stop = stop };
            yards.Add((start, end));
            // Its first track is the plan's spur (the same branch index: every edge the plan names keeps its branch); the
            // rest join after.
            foreach (var t in stop.Tracks)
            {
                var def = new BranchDefinition(BranchKind.Spur, start + t.Toe, t.Side, t.Segments) { Standing = t.Standing };
                if (t.Primary)
                    branches[index] = def;
                else
                    extra.Add(def);
            }
            pads.Add(PadOf(main, stop, start));
        }

        // Halts and dead towns on the main line (§11.3) that aren't in a yard's zone: a village each.
        var platforms = plan.Structures.Where(s => s.Type == StructureType.Platform && s.Edge == "main").ToList();
        foreach (var town in plan.Landmarks.Where(l => l.Type is "halt" or "town" && l.Edge == "main").OrderBy(l => l.S0))
        {
            double start = town.S0, end = town.S1;
            // Straight to a village's eye (T114 playtest: "a stop with nothing there"): the generator's straights carry a
            // residual bow of a few hundred km radius, a few cm over a halt, and the yard's exact test turned a halt in
            // ten away from its village, leaving its boards and platform with nothing at them.
            if (yards.Any(y => y.Start < end + 100 && start < y.End + 100) || !StraightAndLevel(main, start, end, maxCurvature: VillageCurvature))
                continue;
            var platform = platforms.FirstOrDefault(p => p.S0 < end && start < p.S1);
            ulong stopSeed = StopSeed.Of(StopSeed.Of(routeSeed, StopSeed.Halt), (ulong)Math.Round(start));
            var stop = StopGenerator.Generate(st, tier, stopSeed, StopKind.Village, cx with
            {
                ZoneLength = end - start,
                Side = platform?.Side ?? 0,
                HaltAt = platform is null ? null : (platform.S0 + platform.S1) / 2 - start,
            });
            if (!stop.Valid)
                continue;
            features.Add(new RouteFeature(FeatureKind.Village, start, end, stop.VillageSide) { Stop = stop });
            pads.Add(PadOf(main, stop, start));
        }

        branches.AddRange(extra);
        features.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Kind.CompareTo(b.Kind));
        return (features, branches, plan with { Pads = [.. plan.Pads, .. pads] });
    }

    /// <summary>A plan's sub-seed ("0x…" hex, linegen plan §17.2) as a number.</summary>
    static ulong SubSeed(string s) =>
        ulong.TryParse(s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : 0;

    /// <summary>The main line has no curve and no change of grade over a stretch: a stop's rail frame is the line there.</summary>
    static bool StraightAndLevel(RailLine main, double from, double to, bool level = true, double maxCurvature = 1e-6)
    {
        if (from < 0 || to > main.Length)
            return false;
        double y0 = main.Sample(from).Position.Y;
        for (double s = from; s <= to; s += 10)
        {
            var t = main.Sample(s);
            if (Math.Abs(t.Curvature) > maxCurvature || level && Math.Abs(t.Position.Y - y0) > 0.05)
                return false;
        }
        return true;
    }

    /// <summary>
    /// The ground flattened to the stop: a box along the main line over its zone, out on each side as far as the stop has
    /// built there (a yard's side never flattens the sea on the other, WatersideTests).
    /// </summary>
    static PlanPad PadOf(RailLine main, StopLayout stop, double start)
    {
        var mid = main.Sample(start + stop.ZoneLength / 2);
        double right = 20, left = 20;
        void Reach(double d, double half)
        {
            if (d + half > 0)
                right = Math.Max(right, d + half + 12);
            if (d - half < 0)
                left = Math.Max(left, -(d - half) + 12);
        }
        foreach (var b in stop.Buildings)
            Reach(b.D, Math.Max(b.Length, b.Width) / 2);
        foreach (var t in stop.Tracks)
            foreach (var p in t.Path)
                Reach(p.D, 3);
        // Centred between its two edges, so one radius reaches both.
        var across = Double3.Cross(mid.Tangent, Double3.Up).Normalized * ((right - left) / 2);
        var at = mid.Position + across;
        return new PlanPad($"{PadPrefix}{Math.Round(start)}", Math.Round(at.X, 3), Math.Round(at.Z, 3), Math.Round(mid.Position.Y, 3),
            Math.Round((right + left) / 2, 1), stop.ZoneLength / 2, Math.Round(Math.Atan2(-mid.Tangent.X, -mid.Tangent.Z) * 180 / Math.PI, 3), Box: true);
    }
}
