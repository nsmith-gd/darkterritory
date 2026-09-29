using Ballast;
using DarkTerritory.Sim.Rail;

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

        // Level straight zones: the fortress yard, each facility junction, and the terminus approach.
        var zones = new List<(double Start, double End)> { (0, t.YardLength) };
        zones.AddRange(facilities.Select(p => (p - t.PoiZoneHalfLength, p + t.PoiZoneHalfLength)));
        zones.Add((length - t.TerminusApproach, length));

        var segments = LayOut(tt, length, zones, ref layoutRng);
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
        var blocked = new List<(double Start, double End)>(zones);
        AddSpans(FeatureKind.Tunnel, featureRng.RangeInclusive(tt.Tunnels[0], tt.Tunnels[1]), 200, 1200, 0, length, blocked, features, ref featureRng);
        int bridges = featureRng.RangeInclusive(tt.Bridges[0], tt.Bridges[1]);
        for (int i = 0; i < bridges; i++)
        {
            int maxCars = featureRng.Chance(tt.WeakBridgeChance) ? featureRng.RangeInclusive(6, 16) : 0;
            AddSpans(FeatureKind.Bridge, 1, 60, 400, maxCars, length, blocked, features, ref featureRng);
        }
        int junctions = featureRng.RangeInclusive(tt.Junctions[0], tt.Junctions[1]);
        for (int i = 0; i < junctions; i++)
            AddSpans(FeatureKind.Junction, 1, 30, 30, 0, length, blocked, features, ref featureRng, side: featureRng.Chance(0.5) ? 1 : -1);

        AddHazards(t, tt, weather, built, length, zones, features, ref hazardRng);
        features.Sort((a, b) => a.Start.CompareTo(b.Start));
        return new Route(line.Name, tier, seed, line, features, weather, dawn);
    }

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
    static List<TrackSegment> LayOut(TierTuning tt, double length, List<(double Start, double End)> zones, ref Pcg32 rng)
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
