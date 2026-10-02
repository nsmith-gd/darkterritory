using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Sim.Route;

/// <summary>
/// Generates a night's line from a tier and a seed (GDD §22: length, grades, curves, junctions, facility
/// placement, tunnels and bridges). Deterministic: the same (tier, seed) is the same route everywhere,
/// so the host only has to send the seed.
/// </summary>
public static class RouteGenerator
{
    public static Route Generate(RouteTuning t, RouteTier tier, ulong seed)
    {
        var tt = t.Tiers[tier];
        var rng = new Pcg32(seed, (ulong)tier + 1);
        var layoutRng = rng.Fork(1);
        var featureRng = rng.Fork(2);
        var hazardRng = rng.Fork(3);
        var weatherRng = rng.Fork(4);

        double length = Math.Round(layoutRng.Range(tt.LengthKm[0], tt.LengthKm[1]) * 1000);
        var facilities = PlaceFacilities(t, tt, length, ref layoutRng);
        // Village halts between the facilities (level-design P1): their own seed, so they move nothing else.
        var halts = t.Stops is { } stopTuning ? PlaceHalts(t, stopTuning.Tiers[tier].Halts, facilities, length, StopSeed.Of(seed, StopSeed.Halt, (ulong)tier)) : [];

        // Level straight zones: the fortress yard, each facility junction and village halt, and the terminus approach.
        var zones = new List<(double Start, double End)> { (0, t.YardLength) };
        zones.AddRange(facilities.Concat(halts).Order().Select(p => (p - t.PoiZoneHalfLength, p + t.PoiZoneHalfLength)));
        zones.Add((length - t.TerminusApproach, length));

        // The grade out of each facility's yard (level-design D.2 "hard pulls"), up, from its own seed: laid on the stretch
        // the train pulls out onto, so it moves nothing else about the line.
        var exits = t.Stops is { } st2
            ? facilities.Select((p, k) => (End: p + t.PoiZoneHalfLength,
                Grade: Math.Round(new Pcg32(StopSeed.Of(seed, StopSeed.Grade, (ulong)k * 4 + (ulong)tier)).Range(st2.Tiers[tier].ExitGrade[0], st2.Tiers[tier].ExitGrade[1]), 2))).ToList()
            : [];
        var segments = LayOut(tt, length, zones, exits, ref layoutRng);
        var line = new LineDefinition($"{tier}-{seed}", segments);
        var built = new RailLine(line);

        var features = new List<RouteFeature>();
        var weather = new RouteWeather(
            Math.Round(weatherRng.Range(tt.Fog[0], tt.Fog[1]), 4),
            weatherRng.Chance(tt.WetChance),
            Math.Round(weatherRng.Range(tt.Cold[0], tt.Cold[1]), 2),
            Math.Round(weatherRng.Range(tt.Wind[0], tt.Wind[1]), 2));
        double dawn = Math.Round(length / t.DawnAverageSpeed * (1 + t.DawnSlack));

        AddFacilities(t, tier, facilities, dawn, features, ref featureRng);
        if (t.Stops is not null)
            AddStops(t, tier, seed, halts, features, built);
        var blocked = new List<(double Start, double End)>(zones);
        AddSpans(FeatureKind.Tunnel, featureRng.RangeInclusive(tt.Tunnels[0], tt.Tunnels[1]), 200, 1200, 0, length, blocked, features, ref featureRng);
        int bridges = featureRng.RangeInclusive(tt.Bridges[0], tt.Bridges[1]);
        for (int i = 0; i < bridges; i++)
        {
            int maxCars = featureRng.Chance(tt.WeakBridgeChance) ? featureRng.RangeInclusive(6, 16) : 0;
            AddSpans(FeatureKind.Bridge, 1, 60, 400, maxCars, length, blocked, features, ref featureRng);
        }
        var branches = new List<BranchDefinition>();
        AddSpurs(t, features, branches);
        AddJunctions(t.Junctions, featureRng.RangeInclusive(tt.Junctions[0], tt.Junctions[1]), built, length, blocked, features, branches, ref featureRng);

        AddHazards(t, tt, weather, built, length, zones, features, ref hazardRng);
        features.Sort((a, b) => a.Start.CompareTo(b.Start));
        branches.Sort((a, b) => a.Toe.CompareTo(b.Toe));
        return new Route(line.Name, tier, seed, line, features, weather, dawn) { Branches = branches };
    }

    /// <summary>
    /// Junctions with a dead line off each (App. A.7: what the Switchman sets you onto). The turnout is on straight
    /// main line, and the dead line runs alongside, so it lies on the main line's ground and never crosses it.
    /// </summary>
    static void AddJunctions(JunctionTuning j, int count, RailLine line, double length, List<(double Start, double End)> blocked,
        List<RouteFeature> features, List<BranchDefinition> branches, ref Pcg32 rng)
    {
        const double Span = 30;
        double turnout = Turnout(j).Advance;
        for (int placed = 0, tries = 0; placed < count && tries < 400; tries++)
        {
            int side = rng.Chance(0.5) ? 1 : -1;
            double deadLine = Math.Round(rng.Range(j.DeadLineLength[0], j.DeadLineLength[1]));
            double start = Math.Round(rng.Range(0, length - deadLine));
            // Clear of every other feature for its whole length: a tunnel or a bridge alongside would swallow it.
            if (blocked.Any(b => start < b.End + 150 && start + deadLine > b.Start - 150) || !Straight(line, start - 10, start + turnout + 10))
                continue;
            blocked.Add((start, start + deadLine));
            features.Add(new RouteFeature(FeatureKind.Junction, start, start + Span, side));
            branches.Add(DeadLine(j, line, start, side, deadLine));
            placed++;
        }
    }

    /// <summary>The turnout's out-and-back curves: how far along the main line they take, and how far beside it they end.</summary>
    static (double Advance, double Offset) Turnout(JunctionTuning j) => Turnout(j.DivergeRadius, j.DivergeLength);

    static (double Advance, double Offset) Turnout(double radius, double length)
    {
        double theta = length / radius;
        return (2 * radius * DMath.Sin(theta), 2 * radius * (1 - DMath.Cos(theta)));
    }

    /// <summary>
    /// A spur for every facility with machinery to load at (GDD §17-18): out through a turnout from the level zone's
    /// straight and alongside to a buffer stop, on the facility's side. The coaling tower stands over the main line:
    /// the tender goes under its chute where it is.
    /// </summary>
    static void AddSpurs(RouteTuning t, List<RouteFeature> features, List<BranchDefinition> branches)
    {
        var j = t.Junctions;
        foreach (var f in features.Where(f => f.Kind == FeatureKind.Facility && f.Facility != FacilityKind.CoalingTower))
        {
            // A generated yard lays its own tracks: every one a spur off the main line at its own switch.
            if (f.Stop is { } stop)
            {
                foreach (var track in stop.Tracks)
                    branches.Add(new BranchDefinition(BranchKind.Spur, f.Start + track.Toe, track.Side, track.Segments) { Standing = track.Standing });
                continue;
            }
            // Level straight track (the zone), so no grade to follow: out, back to parallel, and on.
            int side = f.Side == 0 ? 1 : f.Side;
            branches.Add(new BranchDefinition(BranchKind.Spur, f.Start + j.SpurToe, side,
            [
                new TrackSegment(j.SpurDiverge, -side * j.SpurRadius),
                new TrackSegment(j.SpurDiverge, side * j.SpurRadius),
                new TrackSegment(j.SpurLength - 2 * j.SpurDiverge),
            ]));
        }
    }

    /// <summary>How far a facility's spur runs beside the main line.</summary>
    public static double SpurOffset(JunctionTuning j) => Turnout(j.SpurRadius, j.SpurDiverge).Offset;

    /// <summary>
    /// A dead line: out through the turnout and back to parallel (radius positive curving left; side −1 is left), then
    /// alongside the main line at that offset to a buffer stop. Alongside is the main line's own segments, each as the
    /// curve parallel to it: the same angle at the main's radius plus or minus the offset, climbing the same height.
    /// </summary>
    static BranchDefinition DeadLine(JunctionTuning j, RailLine main, double toe, int side, double length)
    {
        var (advance, offset) = Turnout(j);
        double L = j.DivergeLength, R = j.DivergeRadius;
        double Grade(double from, double to) => Math.Round((main.Sample(to).Position.Y - main.Sample(from).Position.Y) / L * 100, 4);
        var segments = new List<TrackSegment>
        {
            new(L, -side * R, Grade(toe, toe + advance / 2)),
            new(L, side * R, Grade(toe + advance / 2, toe + advance)),
        };
        double left = length - 2 * L, s = 0;
        foreach (var m in main.Segments)
        {
            double from = Math.Max(s, toe + advance), to = s + m.Length;
            s = to;
            if (to <= from)
                continue;
            double along = to - from;
            // A parallel curve's radius: the main's, less the offset on the inside of the bend, more on the outside.
            double radius = m.Radius == 0 ? 0 : m.Radius + side * offset;
            double scale = m.Radius == 0 ? 1 : radius / m.Radius;
            double piece = Math.Min(along * scale, left);
            segments.Add(new TrackSegment(Math.Round(piece, 3), radius, Math.Round(m.GradePercent / scale, 4)));
            left -= piece;
            if (left <= 0.01)
                break;
        }
        return new BranchDefinition(BranchKind.DeadLine, toe, side, segments);
    }

    /// <summary>No curve and no change of grade over a stretch: where a turnout can be laid.</summary>
    static bool Straight(RailLine line, double from, double to)
    {
        double grade = line.Sample(Math.Max(0, from)).GradePercent;
        for (double s = Math.Max(0, from); s <= to; s += 2)
            if (line.Sample(s) is var t && (t.Curvature != 0 || t.GradePercent != grade))
                return false;
        return true;
    }

    /// <summary>
    /// Village halts in the longest gaps between facilities, each centred in its gap and only where there's room for
    /// its zone well clear of the facilities' (and of the line's ends).
    /// </summary>
    static List<double> PlaceHalts(RouteTuning t, int[] count, List<double> facilities, double length, ulong seed)
    {
        var rng = new Pcg32(seed);
        var edges = new List<double> { t.PoiMinFromEnds / 2 };
        edges.AddRange(facilities.Order());
        edges.Add(length - t.PoiMinFromEnds / 2);
        double clear = 2 * t.PoiZoneHalfLength + 300;
        var gaps = edges.Zip(edges.Skip(1), (a, b) => (Mid: (a + b) / 2, Width: b - a)).Where(g => g.Width >= 2 * clear).OrderByDescending(g => g.Width).ToList();
        int n = Math.Min(gaps.Count, rng.RangeInclusive(count[0], count[1]));
        return [.. gaps.Take(n).Select(g => Math.Round(g.Mid)).Order()];
    }

    /// <summary>
    /// Each stop's layout (level-design Parts D and Z), seeded by its own hash (Z.1): a yard for every facility with
    /// machinery (the coaling tower stands over the main line), with a village at the tier's chance, and a village for
    /// each halt. The yard's side is the facility's.
    /// </summary>
    static void AddStops(RouteTuning t, RouteTier tier, ulong seed, List<double> halts, List<RouteFeature> features, RailLine built)
    {
        var st = t.Stops!;
        var cx = t.StopContext;
        for (int i = 0; i < features.Count; i++)
        {
            var f = features[i];
            if (f.Kind != FeatureKind.Facility || f.Facility == FacilityKind.CoalingTower)
                continue;
            ulong stopSeed = StopSeed.Of(seed, StopSeed.Stop, (ulong)i * 4 + (ulong)tier);
            var kind = new Pcg32(stopSeed).Chance(st.Tiers[tier].VillageChance) ? StopKind.YardAndVillage : StopKind.Yard;
            var stop = StopGenerator.Generate(st, tier, stopSeed, kind, cx with { Facility = f.Facility, ExitGrade = ExitGradeOf(built, f) });
            features[i] = f with { Side = stop.YardSide, Stop = stop };
        }
        for (int h = 0; h < halts.Count; h++)
        {
            ulong stopSeed = StopSeed.Of(seed, StopSeed.Halt, (ulong)h * 4 + (ulong)tier + 1000);
            var stop = StopGenerator.Generate(st, tier, stopSeed, StopKind.Village, cx);
            features.Add(new RouteFeature(FeatureKind.Village, halts[h] - t.PoiZoneHalfLength, halts[h] + t.PoiZoneHalfLength, stop.VillageSide) { Stop = stop });
        }
    }

    /// <summary>The main line's grade (%, up) just past a stop's zone: what a loaded train pulls out onto.</summary>
    public static double ExitGradeOf(RailLine line, RouteFeature f) =>
        f.End + 5 < line.Length ? Math.Max(0, line.Sample(f.End + 5).GradePercent) : 0;

    static List<double> PlaceFacilities(RouteTuning t, TierTuning tt, double length, ref Pcg32 rng)
    {
        int count = rng.RangeInclusive(tt.Pois[0], tt.Pois[1]);
        double usable = length - 2 * t.PoiMinFromEnds;
        while (count > 1 && usable / count < t.PoiMinSpacing)
            count--;
        double slot = usable / count;
        double jitter = Math.Max(0, (slot - t.PoiMinSpacing) / 2);
        var list = new List<double>();
        for (int i = 0; i < count; i++)
            list.Add(Math.Round(t.PoiMinFromEnds + slot * (i + 0.5) + rng.Range(-jitter, jitter)));
        return list;
    }

    /// <summary>Straights, curves and grades between the level zones, keeping elevation from wandering off.</summary>
    static List<TrackSegment> LayOut(TierTuning tt, double length, List<(double Start, double End)> zones, List<(double End, double Grade)> exits, ref Pcg32 rng)
    {
        var segments = new List<TrackSegment>();
        double s = 0, elevation = 0;
        while (s < length - 1e-6)
        {
            var zone = zones.FirstOrDefault(z => s >= z.Start - 1e-6 && s < z.End - 1e-6);
            if (zone != default)
            {
                double len = zone.End - s;
                segments.Add(new TrackSegment(Math.Round(len, 1)));
                s += Math.Round(len, 1);
                continue;
            }
            double next = zones.Where(z => z.Start > s + 1e-6).Select(z => z.Start).DefaultIfEmpty(length).Min();
            double available = next - s;

            double segLength, radius = 0;
            if (rng.Chance(tt.CurveChance))
            {
                segLength = rng.Range(200, 900);
                radius = Math.Round(rng.Range(tt.MinRadius, tt.MinRadius * 4)) * (rng.Chance(0.5) ? 1 : -1);
            }
            else
            {
                segLength = rng.Range(300, 1500);
            }
            if (available - segLength < 150)
            {
                // Too short a sliver would be left before the zone: take it all in this segment.
                segLength = available;
                if (segLength < 150)
                    radius = 0;
            }

            double grade;
            if (rng.Chance(tt.GradeChance))
            {
                // Bias back towards the start elevation so a route doesn't climb forever.
                double upChance = Math.Clamp(0.5 - elevation / 600, 0.15, 0.85);
                grade = rng.Range(0.6, tt.MaxGrade) * (rng.Chance(upChance) ? 1 : -1);
            }
            else
            {
                grade = rng.Range(-0.4, 0.4);
            }
            grade = Math.Round(grade, 2);
            // Pulling out of a yard: its exit grade, over a real stretch of climb (still drawing as ever, so nothing else moves).
            foreach (var exit in exits)
                if (Math.Abs(exit.End - s) < 0.5)
                {
                    grade = exit.Grade;
                    segLength = Math.Min(available, Math.Max(segLength, 400));
                }
            segLength = Math.Round(segLength, 1);
            segments.Add(new TrackSegment(segLength, radius, grade));
            elevation += segLength * grade / 100;
            s += segLength;
        }
        return segments;
    }

    static void AddFacilities(RouteTuning t, RouteTier tier, List<double> at, double dawn, List<RouteFeature> features, ref Pcg32 rng)
    {
        var available = t.Facilities.Where(f => f.FromTier <= tier).Select(f => f.Kind).ToList();
        // A long night needs coal on the way: at twenty cars the tender lasts 53 minutes (spec B.6).
        bool needCoal = dawn > 40 * 60;
        for (int i = 0; i < at.Count; i++)
        {
            var kind = needCoal && i == at.Count / 2 ? FacilityKind.CoalingTower : rng.Pick(available);
            features.Add(new RouteFeature(FeatureKind.Facility, at[i] - t.PoiZoneHalfLength, at[i] + t.PoiZoneHalfLength, rng.Chance(0.5) ? 1 : -1, Facility: kind));
        }
    }

    /// <summary>Places <paramref name="count"/> spans that don't overlap anything already placed.</summary>
    static void AddSpans(FeatureKind kind, int count, double minLen, double maxLen, int maxCars, double length,
        List<(double Start, double End)> blocked, List<RouteFeature> features, ref Pcg32 rng, int side = 0)
    {
        for (int placed = 0, tries = 0; placed < count && tries < 200; tries++)
        {
            double len = Math.Round(rng.Range(minLen, maxLen));
            double start = Math.Round(rng.Range(0, length - len));
            double end = start + len;
            // Keep a little clear track between features so they read as separate places.
            if (blocked.Any(b => start < b.End + 150 && end > b.Start - 150))
                continue;
            blocked.Add((start, end));
            features.Add(new RouteFeature(kind, start, end, side, maxCars));
            placed++;
        }
    }

    /// <summary>
    /// Level content hazards (App. B.2): Sleepers on straights and blind curve exits, never in the first
    /// 2 km; Grease on grades and curve approaches, doubled in wet or deep cold. Neither in the final approach.
    /// </summary>
    static void AddHazards(RouteTuning t, TierTuning tt, RouteWeather weather, RailLine line, double length,
        List<(double Start, double End)> zones, List<RouteFeature> features, ref Pcg32 rng)
    {
        double km = length / 1000;
        double last = length - t.NoSpawnFinalApproach;
        bool inZone(double s) => zones.Any(z => s >= z.Start && s <= z.End);

        // Candidate points: straights, and just after each curve ends (the blind exit).
        var straights = new List<double>();
        var curveExits = new List<double>();
        var approaches = new List<double>();
        double s0 = 0;
        foreach (var seg in line.Segments)
        {
            if (seg.Radius == 0)
                straights.Add(s0 + seg.Length / 2);
            else
            {
                curveExits.Add(s0 + seg.Length + 40);
                approaches.Add(s0 - 120);
            }
            if (Math.Abs(seg.GradePercent) >= 1)
                approaches.Add(s0 + seg.Length / 2);
            s0 += seg.Length;
        }

        int sleepers = (int)Math.Round(tt.SleepersPerKm * km * rng.Range(0.8, 1.2));
        var sleeperSpots = straights.Concat(curveExits).Where(s => s >= t.NoSleepersFirst && s <= last && !inZone(s)).ToList();
        bool sleeperOk(double s) => s >= t.NoSleepersFirst && s + 20 <= last && !inZone(s);
        Scatter(FeatureKind.Sleepers, sleepers, sleeperSpots, 20, sleeperOk, features, ref rng);

        double greaseRate = tt.GreasePerKm * (weather.Wet || weather.Cold > 0.5 ? 2 : 1);
        int grease = (int)Math.Round(greaseRate * km * rng.Range(0.8, 1.2));
        var greaseSpots = approaches.Where(s => s > t.YardLength && s <= last && !inZone(s)).ToList();
        bool greaseOk(double s) => s > t.YardLength && s + 150 <= last && !inZone(s);
        Scatter(FeatureKind.Grease, grease, greaseSpots, rng.Range(40, 150), greaseOk, features, ref rng);
    }

    static void Scatter(FeatureKind kind, int count, List<double> spots, double len, Func<double, bool> ok, List<RouteFeature> features, ref Pcg32 rng)
    {
        if (spots.Count == 0)
            return;
        for (int placed = 0, tries = 0; placed < count && tries < count * 20; tries++)
        {
            double at = Math.Round(rng.Pick(spots) + rng.Range(-60, 60));
            if (!ok(at) || features.Any(f => f.Kind == kind && Math.Abs(f.Start - at) < 200))
                continue;
            placed++;
            features.Add(new RouteFeature(kind, at, at + Math.Round(len)));
        }
    }
}
