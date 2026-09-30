using System.Globalization;
using Ballast;
using DarkTerritory.Sim.Route;
using HoldoutPlacement = DarkTerritory.Sim.Run.HoldoutPlacement;
using HoldoutSiteKind = DarkTerritory.Sim.Run.HoldoutSiteKind;
using HoldoutTuning = DarkTerritory.Sim.Run.HoldoutTuning;
using HoldoutType = DarkTerritory.Sim.Run.HoldoutType;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// GDD App. D.4, the Holdouts: at every facility pad (a second on big pads and switchyards), every halt and every dead
/// town. Each from its site's sub-seed, so the same seed gives the same Holdouts: where it stands, what it is, where its
/// lamp is. What doesn't fit leaves the site short, the plan fails its "holdouts" check, and it's regenerated (§16.4).
/// </summary>
sealed partial class LineBuilder
{
    readonly List<PlanHoldout> _holdouts = new();

    void LayHoldouts()
    {
        _holdouts.Clear();
        if (_c.Holdouts is not { } t || _terrain is null)
            return;
        var p = t.Placement;
        var plan = Freeze();
        var line = _line!;
        _terrainEdges = [.. plan.Alignment.Select(a => a.Edge)];
        foreach (var poi in _pois)
        {
            var slot = _facilities.First(f => $"poi{f.Index + 1}" == poi.Id);
            bool second = poi.Pad.RadiusM >= p.SecondPadScaleM || p.SecondAtKinds.Contains(HoldoutSites.Key(poi.Type));
            ulong sub = ulong.Parse(poi.SubSeed.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var consist = HoldoutSites.StoppedConsist(plan, line, poi, _c.Train);
            var modules = _c.Facilities is { } ft ? HoldoutSites.ModulePoints(poi, line, ft, plan) : [];
            var board = line.Sample(HoldoutSites.NearBoard(plan, poi)).Position + Double3.Up * p.BoardEyeM;
            var zone = new PlanRange("main", R(HoldoutSites.FarBoard(plan, poi)), R(slot.LullEnd));
            for (int k = 0; k < (second ? 2 : 1); k++)
            {
                var rng = Streams.Rng(sub, "holdout", "", k);
                var type = FacilityType(poi.Type, p, ref rng);
                var size = p.Size[type];
                var first = k > 0 ? _holdouts.LastOrDefault(h => h.Site == poi.Id) : null;
                PlanHoldout? placed = null;
                for (int attempt = 0; attempt < p.Tries && placed is null; attempt++)
                {
                    // A third of the tries beside the line behind the consist (down the track it came in on, where the approach
                    // board sees along the corridor); a third out past its front, beyond the buffer stop, clear of the loading
                    // modules laid out behind it; a third anywhere round it. Fit keeps the walk clear of the modules either way.
                    Double3 c;
                    if (attempt % 3 == 1)
                    {
                        var fwd = ((consist[0] - consist[1]) with { Y = 0 }).Normalized;
                        double angle = rng.Range(-1.1, 1.1);
                        var dir = fwd * Math.Cos(angle) + new Double3(-fwd.Z, 0, fwd.X) * Math.Sin(angle);
                        c = consist[0] + dir * rng.Range(p.FacilityFromConsistM[0] + 4, p.FacilityFromConsistM[1] - 4);
                    }
                    else if (attempt % 3 == 0)
                    {
                        double back = rng.Range(p.FacilityFromConsistM[0] + 4, p.FacilityFromConsistM[1] - 4);
                        double s = Math.Max(0, Math.Min(slot.S, RearOnMain(plan, poi)) - back);
                        var at = line.Sample(s);
                        var fwd = (at.Tangent with { Y = 0 }).Normalized;
                        var side = new Double3(-fwd.Z, 0, fwd.X) * (rng.Chance(0.5) ? 1 : -1);
                        c = at.Position + side * rng.Range(p.TrackClearanceM + size[0] + 2, 40) + fwd * rng.Range(-20, 20);
                    }
                    else
                    {
                        int i = rng.RangeInclusive(0, consist.Count - 2);
                        double angle = rng.Range(0, 2 * Math.PI);
                        double d = rng.Range(p.FacilityFromConsistM[0] + 4, p.FacilityFromConsistM[1] - 4);
                        c = consist[i] + new Double3(Math.Cos(angle), 0, Math.Sin(angle)) * d;
                    }
                    var ahead = new Double3(0, 0, -1);
                    // Its long side along the track nearest it.
                    var (from, dist) = HoldoutSites.Nearest(consist, c.X, c.Z);
                    int k0 = NearestIndex(consist, from);
                    ahead = (consist[Math.Max(0, k0 - 1)] - consist[Math.Min(consist.Count - 1, Math.Max(1, k0))]) with { Y = 0 };
                    ahead = ahead.Length < 1e-6 ? new Double3(0, 0, -1) : ahead.Normalized;
                    if (dist < p.FacilityFromConsistM[0] || dist > p.FacilityFromConsistM[1])
                        continue;
                    if (first is not null && HoldoutSites.Horizontal(c, new Double3(first.X, first.Y, first.Z)) < p.SecondApartM)
                        continue;
                    placed = Fit($"h{_holdouts.Count + 1}", poi.Id, poi.Name, HoldoutSiteKind.Facility, type, size, c, ahead, from, board, zone, poi.SpurEdge,
                        k > 0, $"0x{Streams.Mix(sub, "holdout", "", k):X16}", modules, t);
                }
                if (placed is null)
                    Warn($"no room for a Holdout at {poi.Name}: " + string.Join(", ", HoldoutRejects.Select(kv => $"{kv.Key} {kv.Value}")));
                else
                    _holdouts.Add(placed);
                HoldoutRejects.Clear();
            }
        }

        // Halts and dead towns (D.4): one each, beside the platform within 40 m of the line, or in the town's footprint
        // within 80 m. Their sub-seed is the station's own, as a facility's is its POI's.
        var stations = _landmarks.Where(l => l.Type is "halt" or "town").ToList();
        for (int n = 0; n < stations.Count; n++)
        {
            var station = stations[n];
            bool town = station.Type == "town";
            var type = town ? HoldoutType.BarricadedShelter : HoldoutType.HaltLockup;
            var size = p.Size[type];
            ulong sub = Streams.Mix(_seed, "station", station.Name, n);
            var rng = Streams.Rng(sub, "holdout");
            var edgeLine = LineOf(station.Edge);
            double reach = town ? p.TownFromMainM : p.HaltFromMainM;
            var platforms = _structures.Where(st => st.Type == StructureType.Platform && st.Edge == station.Edge && st.S1 >= station.S0 && st.S0 <= station.S1)
                .Select(st => st.Side).ToHashSet();
            var whistle = HoldoutSites.WhistleBoard(plan, station);
            var boardAt = whistle is { } w && w.Edge == station.Edge ? w : (station.Edge, Math.Max(0, station.S0 - _c.Config.Signage.WhistleBeforeM));
            PlanHoldout? Try((string Edge, double S) from, double zoneFrom, ref Pcg32 rng)
            {
                var board = HoldoutSites.HoldoutBoardAt(plan, line, from, p.BoardEyeM)!.Value;
                var zone = new PlanRange(station.Edge, R(zoneFrom), R(station.S1));
                for (int attempt = 0; attempt < p.Tries; attempt++)
                {
                    double s = rng.Range(station.S0 + 5, station.S1 - 5);
                    int side = !town && platforms.Count > 0 ? platforms.First() : rng.Chance(0.5) ? 1 : -1;
                    // On the platform (it runs 3.6-10 m out, StructureKit.PlatformBay) half the time: close to the line, where the
                    // approach's cutting walls can't hide its lamp. Beside it (or clear of the track where there's none) otherwise.
                    bool onPlatform = platforms.Contains(side) && attempt % 2 == 0;
                    double inner = (onPlatform ? p.TrackClearanceM : platforms.Contains(side) ? 10 + 1 : p.TrackClearanceM) + size[0];
                    double outer = onPlatform ? 10 - size[0] : reach - size[0];
                    if (inner > outer)
                        continue;
                    double lateral = rng.Range(inner, outer);
                    var at = edgeLine.Sample(Math.Clamp(s, 0, edgeLine.Length));
                    var ahead = (at.Tangent with { Y = 0 }).Normalized;
                    var right = new Double3(-ahead.Z, 0, ahead.X);
                    var c = at.Position + right * (side * lateral);
                    if (Fit($"h{_holdouts.Count + 1}", HoldoutSites.StationId(n), station.Name, town ? HoldoutSiteKind.DeadTown : HoldoutSiteKind.Halt, type, size,
                        c, ahead, at.Position, board, zone, null, false, $"0x{sub:X16}", [], t) is { } fits)
                        return fits;
                }
                return null;
            }
            var placed = Try(whistle ?? boardAt, boardAt.Item2, ref rng);
            // Where the line comes round a hill or up out of a cutting at the station, nothing there can be seen from 400 m out.
            // D.4 wants the lamp seen from the whistle board, so the board comes in (not in D: 50 m at a time, no nearer than
            // placement.whistleNearestM) until it's where the lamp can be. The board's what warns the driver; it still does.
            if (placed is null && whistle is { } sign && sign.Edge == station.Edge)
                for (double s = sign.S + 50; placed is null && s <= station.S0 - p.WhistleNearestM; s += 50)
                    if ((placed = Try((sign.Edge, s), s, ref rng)) is not null)
                        MoveWhistle(station.Name, sign.S, s);
            if (placed is null)
                Warn($"no room for a Holdout at {station.Name}: " + string.Join(", ", HoldoutRejects.Select(kv => $"{kv.Key} {kv.Value}")));
            else
                _holdouts.Add(placed);
            HoldoutRejects.Clear();
        }
    }

    /// <summary>
    /// D.4 "type is chosen deterministically from the site's sub-seed": always a shelter at a mine head (the portal lamp
    /// room); where there's a spare siding, a prison car by chance (likelier at the preferred kinds); a shelter otherwise.
    /// </summary>
    static HoldoutType FacilityType(FacilityKind kind, HoldoutPlacement p, ref Pcg32 rng)
    {
        string key = HoldoutSites.Key(kind);
        // Drawn every time, so a type rule changing doesn't reshuffle where the Holdout goes.
        double roll = rng.NextDouble();
        if (p.ShelterAlwaysAt.Contains(key) || !p.SpareSidingAt.Contains(key))
            return HoldoutType.BarricadedShelter;
        double chance = p.PrisonCarPreferredAt.Contains(key) ? p.PrisonCarChance.Preferred : p.PrisonCarChance.Other;
        return roll < chance ? HoldoutType.PrisonCar : HoldoutType.BarricadedShelter;
    }

    /// <summary>
    /// A Holdout at <paramref name="centre"/>, long side along <paramref name="along"/>, its door facing where the walk comes
    /// from, if it fits there: off the track, on dry and walkable ground, reachable on foot with its walk clear of the
    /// loading modules, and its lamp (as low as it can be) in sight of the approach board. Null if not.
    /// </summary>
    PlanHoldout? Fit(string id, string site, string name, HoldoutSiteKind kind, HoldoutType type, double[] size, Double3 centre, Double3 along, Double3 from,
        Double3 board, PlanRange zone, string? spur, bool second, string subSeed, List<Double3> modules, HoldoutTuning t)
    {
        var p = t.Placement;
        var terrain = _terrain!;
        centre = centre with { Y = terrain.Height(centre.X, centre.Z) };
        double heading = Math.Round(Math.Atan2(-along.X, -along.Z) * 180 / Math.PI, 3);
        var (door, _) = HoldoutSites.DoorFacing(centre, heading, size[0], from);
        door = door with { Y = terrain.Height(door.X, door.Z) };
        var probe = new PlanHoldout(id, site, name, kind, type, R(centre.X), R(centre.Y), R(centre.Z), heading, size, [R(door.X), R(door.Y), R(door.Z)], [], [],
            zone, spur, [R(from.X), R(from.Y), R(from.Z)], second, subSeed);
        // Off every track, and not standing in water or on a slope.
        foreach (var near in terrain.Nearby(centre.X, centre.Z, p.TrackClearanceM + size[1] + size[0]))
        {
            var track = LineOf(_terrainEdges![near.Edge]).Sample(near.S).Position;
            if (HoldoutSites.Horizontal(track, centre) < p.TrackClearanceM + size[0])
                return Reject("track");
        }
        var corners = HoldoutSites.Footprint(probe).ToList();
        double rise = corners.Max(c => terrain.Height(c.X, c.Z)) - corners.Min(c => terrain.Height(c.X, c.Z));
        // Its corners no further apart in height than a walkable slope across half its diagonal.
        if (rise > terrain.Rules.WalkableSlope * Math.Sqrt(size[0] * size[0] + size[1] * size[1]))
            return Reject("rise");
        if (corners.Any(c => terrain.WaterAt(c.X, c.Z) is not null))
            return Reject("water");
        if (HoldoutSites.Walk(terrain, from, door, p.WalkStepM) is { } why)
            return Reject("walk " + why.Split(' ')[1]);
        foreach (var m in modules)
            if (HoldoutSites.SegmentDistance(from, door, m) < p.RouteClearanceM)
                return Reject("modules");
        // The lowest lamp that's seen from the board: a candidate that the highest can't see is out at once.
        if (!HoldoutSites.Sees(terrain, board, centre + Double3.Up * p.LampHeightM[1], clear: 0.6))
            return Reject("lamp");
        double height = p.LampHeightM[1];
        for (double h = p.LampHeightM[0]; h < p.LampHeightM[1]; h += 1)
            if (HoldoutSites.Sees(terrain, board, centre + Double3.Up * h, clear: 0.6))
            {
                height = h;
                break;
            }
        var lit = centre + Double3.Up * height;
        return probe with { Lamp = [R(lit.X), R(lit.Y), R(lit.Z)], Board = [R(board.X), R(board.Y), R(board.Z)] };
    }

    // Why the spots tried for a Holdout didn't fit, for the warning when none did (`dt linegen debug`).
    readonly Dictionary<string, int> HoldoutRejects = new();

    PlanHoldout? Reject(string why)
    {
        HoldoutRejects[why] = HoldoutRejects.GetValueOrDefault(why) + 1;
        return null;
    }

    string[]? _terrainEdges;

    /// <summary>A station's whistle board, in to where its Holdout's lamp can be seen from it (<see cref="LayHoldouts"/>).</summary>
    void MoveWhistle(string station, double from, double to)
    {
        int i = _signs.FindIndex(x => x.Type == "whistle" && x.For == station && Math.Abs(x.S - from) < 0.01);
        if (i < 0)
            return;
        _signs[i] = _signs[i] with { S = R(to) };
        _signs.Sort((a, b) => a.Edge != b.Edge ? string.CompareOrdinal(a.Edge, b.Edge) : a.S != b.S ? a.S.CompareTo(b.S) : string.CompareOrdinal(a.Type, b.Type));
        Warn($"{station}'s whistle board is {to - from:0} m nearer than usual, where its Holdout's lamp can be seen");
    }

    /// <summary>Where the stopped consist's rear is on the main line (its main-line distance), or the facility's junction.</summary>
    double RearOnMain(LinePlan plan, PlanPoi poi)
    {
        double length = plan.Consist.LengthM;
        if (poi.SpurEdge is { } spur && plan.Edge(spur).Branch is var b && b >= 0)
            return poi.S - Math.Max(0, length - _line!.Branches[b].Local.Length);
        var g = _c.Train.Geometry;
        return poi.S + g.EngineLength - g.Engine.TenderLength / 2 - length;
    }

    static int NearestIndex(List<Double3> points, Double3 p)
    {
        int best = 0;
        double bestD = double.MaxValue;
        for (int i = 0; i < points.Count; i++)
            if (HoldoutSites.Horizontal(points[i], p) < bestD)
                (best, bestD) = (i, HoldoutSites.Horizontal(points[i], p));
        return best;
    }
}
