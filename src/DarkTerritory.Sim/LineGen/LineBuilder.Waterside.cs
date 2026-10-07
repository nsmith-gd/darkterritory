using Ballast;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// Stage 4b (docs/design/maritime-rules.md): the water the line runs past. Shores where a biome keeps to the coast (the
/// Atlantic's coves, Fundy's mudflats, the dyked marsh), then the Southern Upland's lakes, beside the line and now and
/// then crossed on a fill. Both are plan data, so the terrain (and the sightlines authority reads off it) is the same
/// on every machine; neither moves the track.
/// </summary>
sealed partial class LineBuilder
{
    readonly List<PlanLake> _lakes = new();
    readonly List<PlanShore> _shores = new();
    readonly List<PlanStructure> _lakeTrestles = new();

    void LayWaterside()
    {
        var rng = Rng("waterside", "main");
        LayShores(ref rng);
        LayRoads(ref rng);
        LayLakes(ref rng);
    }

    /// <summary>
    /// A shore for each coastal biome region that rolls one, on the side no branch leaves by (the sea has no spurs), and
    /// not across a tunnel (the hill over the bore stays whole). The water stands under the lowest rail along it.
    /// </summary>
    void LayShores(ref Pcg32 rng)
    {
        var sr = _t.Terrain.Shore;
        var dr = _t.Terrain.Dykes;
        foreach (var (biome, b0, b1) in _biomes)
        {
            if (!_c.Config.Biomes.Biomes.TryGetValue(biome, out var def) || def.Shore <= 0 || !rng.Chance(def.Shore))
                continue;
            double s0 = Math.Max(b0, _gate + _t.Budget.GraceM * 0.5), s1 = Math.Min(b1, _terminus - _t.Terminus.SkyGlowM * 0.3);
            // Round a tunnel: the shore stops short of it.
            foreach (var t in _structures.Where(x => x.Edge == "main" && x.Type == StructureType.Tunnel))
            {
                if (t.S1 + sr.TaperM > s0 && t.S0 - sr.TaperM < s1)
                {
                    if (t.S0 - s0 > s1 - t.S1)
                        s1 = t.S0 - sr.TaperM;
                    else
                        s0 = t.S1 + sr.TaperM;
                }
            }
            // And round the pads (the fortress, the facilities, the towns): they're level ground, not shore.
            foreach (var pad in _pads)
            {
                double reach = pad.RadiusM + pad.HalfLengthM + sr.TaperM;
                var (at, lat) = NearestMain(pad.X, pad.Z, s0 - reach, s1 + reach);
                if (Math.Abs(lat) > reach + _t.Terrain.CorridorM || at + reach < s0 || at - reach > s1)
                    continue;
                if (at - s0 > s1 - at)
                    s1 = Math.Min(s1, at - reach);
                else
                    s0 = Math.Max(s0, at + reach);
            }
            if (s1 - s0 < sr.MinM)
                continue;
            // The seaward side: the one no branch or facility spur leaves by here.
            var sides = _edges.Values.Where(e => e.Role != EdgeRole.Main && e.Toe > s0 - 400 && e.Toe < s1 + 400).Select(e => e.Side).Distinct().ToList();
            int side = rng.Chance(0.5) ? 1 : -1;
            if (sides.Count == 2)
                continue;
            if (sides.Count == 1)
                side = -sides[0];
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            for (double s = s0; s <= s1; s += 25)
            {
                double z = MainHeight(s);
                (lo, hi) = (Math.Min(lo, z), Math.Max(hi, z));
            }
            var kind = def.ShoreKind switch { "fundy" => ShoreKind.Fundy, "dyke" => ShoreKind.Dyke, "river" => ShoreKind.River, _ => ShoreKind.Sea };
            // A dyked marsh is dead flat: where the line climbs or falls along it, it's a mudflat shore instead.
            if (kind == ShoreKind.Dyke && hi - lo > dr.MaxRailRangeM)
                kind = ShoreKind.Fundy;
            // The Atlantic comes up close under the line, a couple of metres below the rail on its bank (the South Shore
            // line along its coves); Fundy's low water and the dykes' stand far lower.
            double level = lo - (kind == ShoreKind.Sea ? sr.SeaBelowRailM : sr.LevelBelowRailM);
            double near = rng.Range(kind == ShoreKind.Sea ? sr.SeaNearM : sr.NearM), cove = rng.Range(kind == ShoreKind.Sea ? sr.SeaCoveM : sr.CoveM), wl = rng.Range(sr.CoveWavelengthM), phase = rng.Range(0, 1000);
            double flat = kind == ShoreKind.Sea ? 0 : rng.Range(sr.FlatM);
            // A river alongside: its bank close in, the water falling with the rail, its width in place of the mud's.
            if (kind == ShoreKind.River)
            {
                var rv = _t.Terrain.Rivers;
                (near, cove, flat, level) = (rng.Range(rv.BankM), rng.Range(rv.MeanderM), rng.Range(rv.WidthM), rv.BelowRailM);
            }
            double dyke = 0, fields = 0;
            if (kind == ShoreKind.Dyke)
            {
                dyke = rng.Range(dr.OutM);
                fields = dr.FieldsBelowRailM;
                // The dyke stands between the fields and the water: the shore is out past it.
                near = Math.Max(near, dyke + dr.CrestM + dr.HeightM / dr.SideSlope + 12);
                cove *= 0.3;
            }
            _shores.Add(new PlanShore($"shore{_shores.Count + 1}", kind, "main", R(s0), R(s1), side, R(level), R(near), R(cove), R(wl), R(phase), R(flat), R(dyke), R(fields)));
        }
    }

    /// <summary>
    /// Lakes at the biome's rate per km (Poisson-ish spacing), each an ellipse along the ice's flow, off to one side or
    /// (sometimes, where the line runs level on open ground) straddling it on a fill. None where another track, a pad, a
    /// structure or a cutting is, none on a shore's seaward side, and none over another lake.
    /// </summary>
    void LayLakes(ref Pcg32 rng)
    {
        var lr = _t.Terrain.Lakes;
        var line = _line!;
        double s = _gate + _t.Budget.GraceM;
        double end = _terminus - _t.Terminus.SkyGlowM * 0.3;
        while (s < end)
        {
            string biome = BiomeAt(s);
            double rate = _c.Config.Biomes.Biomes.TryGetValue(biome, out var def) ? def.LakesPerKm : 0;
            if (rate <= 0)
            {
                s += 500;
                continue;
            }
            s += 1000 / rate * rng.Range(0.4, 1.6);
            if (s >= end)
                break;
            bool cross = rng.Chance(lr.CrossChance);
            double radius = cross ? rng.Range(lr.CrossRadiusM) : rng.Range(lr.RadiusM);
            double stretch = rng.Range(lr.Stretch);
            double heading = _t.Terrain.Drumlins.FlowDeg + rng.Range(-lr.TurnDeg, lr.TurnDeg);
            double wobble = lr.Wobble * rng.Range(0.5, 1.0);
            int side = rng.Chance(0.5) ? 1 : -1;
            // On an Atlantic shore, a barachois: a pond close in on the landward side behind the line's bank, so there's
            // water both sides of the train (maritime-rules.md §3).
            bool barachois = false;
            if (_shores.FirstOrDefault(x => x.Kind == ShoreKind.Sea && s > x.S0 + 100 && s < x.S1 - 100) is { } sea)
                (side, cross, barachois) = (-sea.Side, false, true);
            var t = line.Sample(s);
            var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            // A crossed lake lies across the line (its long axis near square to it), a side lake off it.
            double off = cross ? rng.Range(-0.3, 0.3) * radius : side * (radius * (1 + rng.Range(lr.OffM)));
            if (cross)
                heading = DMath.Atan2(right.Z, right.X) * 180 / Math.PI + rng.Range(-lr.TurnDeg, lr.TurnDeg);
            if (barachois)
            {
                // Long and narrow, lying along the line behind its bank, the near shore just past the ballast's slope.
                radius = rng.Range(22, 50);
                stretch = rng.Range(2.2, 3.6);
                heading = DMath.Atan2(t.Tangent.Z, t.Tangent.X) * 180 / Math.PI + rng.Range(-8, 8);
                off = side * (radius + _t.Terrain.ShoulderM + 10 + rng.Range(0, 12));
                wobble = rng.Range(0.04, 0.1);
            }
            // A side lake slides out from the line until it's clear of it (its long axis lies along the ice, not the
            // track); a crossed one stays where it is or goes.
            double? found = null;
            PlanLake lake = null!;
            for (int k = 0; k < (cross ? 1 : 8) && found is null; k++)
            {
                var c = t.Position + right * (off + side * k * 15);
                double hr = heading * Math.PI / 180;
                lake = new PlanLake($"lake{_lakes.Count + 1}", R(c.X), R(c.Z), R(radius), R(stretch), Math.Round(DMath.Cos(hr), 6), Math.Round(DMath.Sin(hr), 6),
                    R(wobble), 0, lr.DepthM, cross);
                found = LakeLevel(lake, cross);
            }
            if (found is not { } level)
                continue;
            _lakes.Add(lake with { LevelM = R(level) });
            if (cross)
                TrestleAcross(_lakes[^1]);
        }
    }

    /// <summary>
    /// Some crossed lakes are taken on a low timber trestle instead of a fill (maritime-rules §4; note 317): the span is
    /// where the main line is over the water, and <c>trestleAbutmentM</c> past it each end, so the water runs on under it
    /// (the terrain leaves a span's ground open). Named, a landmark, and a bridge to the tags, authority and signage after.
    /// Its own dice (<c>lakeTrestle</c>, by lake), so a line whose lakes all stay fills is laid exactly as before.
    /// </summary>
    void TrestleAcross(PlanLake lake)
    {
        var lr = _t.Terrain.Lakes;
        var rng = Rng("lakeTrestle", lake.Id);
        if (!rng.Chance(lr.TrestleChance))
            return;
        var line = _line!;
        var (at, _) = NearestMain(lake.X, lake.Z, 0, line.Length);
        double reach = lake.RadiusM * lake.Stretch * (1 + lake.Wobble) + 20;
        double? wet0 = null, wet1 = null;
        for (double s = Math.Max(0, at - reach); s <= Math.Min(line.Length, at + reach); s += 2)
        {
            var p = line.Sample(s).Position;
            if (TerrainField.LakeMetric(lake, p.X, p.Z) < 1)
                (wet0, wet1) = (wet0 ?? s, s);
        }
        if (wet0 is not { } w0 || wet1 is not { } w1)
            return;
        double a = w0 - lr.TrestleAbutmentM, b = w1 + lr.TrestleAbutmentM;
        if (b - a > lr.TrestleMaxM || _structures.Any(st => st.Edge == "main" && st.S1 > a - lr.TrestleClearM && st.S0 < b + lr.TrestleClearM))
            return;
        double rail = line.Sample((a + b) / 2).Position.Y;
        string name = Name("bridge", ref rng);
        name = string.Join(' ', name.Split(' ')[..^1].Append("Trestle"));
        int n = _structures.Count(st => st.Id.StartsWith("bridge", StringComparison.Ordinal)) + 1;
        var trestle = new PlanStructure($"bridge{n}", StructureType.Trestle, "main", R(a), R(b), R(rail - lake.LevelM + lake.DepthM), null, name, "timber");
        // In order along the main line, as the structures were laid.
        int i = _structures.FindIndex(st => st.Edge == "main" && st.S0 > a);
        _structures.Insert(i < 0 ? _structures.Count : i, trestle);
        _lakeTrestles.Add(trestle);
        _landmarks.Add(new PlanLandmark("bridge", name, "main", trestle.S0, trestle.S1));
    }

    /// <summary>
    /// Where a lake would go, its water level (under the lowest rail near it), or null where it can't: over another
    /// track, a pad, a structure, a cutting, a shore's sea or another lake; or, for a crossed one, where the crossing
    /// isn't open level ground (a fill across the narrows, never a bridge).
    /// </summary>
    double? LakeLevel(PlanLake lake, bool cross)
    {
        var lr = _t.Terrain.Lakes;
        double reach = lake.RadiusM * lake.Stretch * (1 + lake.Wobble) + lr.ClearM;
        foreach (var o in _lakes)
            if (Math.Sqrt((o.X - lake.X) * (o.X - lake.X) + (o.Z - lake.Z) * (o.Z - lake.Z)) < reach + o.RadiusM * o.Stretch)
                return null;
        foreach (var p in _pads)
            if (Math.Sqrt((p.X - lake.X) * (p.X - lake.X) + (p.Z - lake.Z) * (p.Z - lake.Z)) < reach + p.RadiusM + p.HalfLengthM)
                return null;
        var line = _line!;
        double low = double.PositiveInfinity, inside = 0;
        // Every edge near it: a branch mustn't pass it at all; the main line only as far as a crossing allows.
        foreach (var a in _edges.Values)
        {
            var track = a.Role == EdgeRole.Main ? line : line.Branches[a.Branch].Local;
            for (double d = 0; d <= track.Length; d += 20)
            {
                var p = track.Sample(d).Position;
                double dx = p.X - lake.X, dz = p.Z - lake.Z;
                // Its level is under the lowest rail anywhere near (the land's there at rail height, give or take).
                if (a.Role == EdgeRole.Main && dx * dx + dz * dz < (reach + 300) * (reach + 300))
                    low = Math.Min(low, p.Y);
                if (dx * dx + dz * dz > reach * reach)
                    continue;
                double m = TerrainField.LakeMetric(lake, p.X, p.Z);
                double clear = 1 + (_t.Terrain.ShoulderM + 6) / lake.RadiusM;
                if (a.Role != EdgeRole.Main && m < clear + lr.ClearM / lake.RadiusM)
                    return null;
                if (a.Role == EdgeRole.Main)
                {
                    if (m < clear)
                    {
                        if (!cross)
                            return null;
                        inside += 20;
                        if (!OpenGround(d))
                            return null;
                    }
                }
            }
        }
        if (double.IsInfinity(low) || (cross && (inside < 40 || inside > 420)))
            return null;
        // Not out on a shore's sea side.
        // Clear of the roads (a lake doesn't drown one).
        foreach (var road in _roads)
            for (double d = Math.Max(road.S0, 0); d <= road.S1; d += 25)
            {
                var t = line.Sample(Math.Min(d, line.Length));
                var r = Double3.Cross(t.Tangent, Double3.Up).Normalized;
                var p = t.Position + r * TerrainField.RoadLateral(road, _crossings, d, _t.Terrain.Roads.RampM);
                if ((p.X - lake.X) * (p.X - lake.X) + (p.Z - lake.Z) * (p.Z - lake.Z) < reach * reach
                    && TerrainField.LakeMetric(lake, p.X, p.Z) < 1 + (_t.Terrain.Roads.HalfWidthM + 12) / lake.RadiusM)
                    return null;
            }
        // Out in the corridor the terrain models, where it can be seen, not under the fog past it.
        var (at, lat) = NearestMain(lake.X, lake.Z, 0, line.Length);
        if (Math.Abs(lat) > _t.Terrain.CorridorM - lake.RadiusM * 0.5)
            return null;
        if (_shores.Count > 0)
        {
            foreach (var sh in _shores)
                if (at >= sh.S0 - reach && at <= sh.S1 + reach && Math.Sign(lat) == sh.Side)
                    return null;
        }
        return low - lr.LevelBelowRailM;
    }

    /// <summary>Whether the main line at s is somewhere a fill can cross water: no structure, and nothing but plain ground or embankment beside it.</summary>
    bool OpenGround(double s)
    {
        foreach (var st in _structures)
            if (st.Edge == "main" && s > st.S0 - 60 && s < st.S1 + 60)
                return false;
        foreach (var i in _intents)
            if (i.Edge == "main" && s >= i.S0 && s <= i.S1 && (!Open(i.Left.T) || !Open(i.Right.T)))
                return false;
        return true;
        static bool Open(IntentType t) => t is IntentType.Plain or IntentType.Embankment or IntentType.Marsh or IntentType.River;
    }

    /// <summary>The main line's nearest point to (x, z) between s0 and s1 (a 20 m search, then 2 m), and the side it's on.</summary>
    (double S, double Lateral) NearestMain(double x, double z, double s0, double s1)
    {
        var line = _line!;
        s0 = Math.Max(0, s0);
        s1 = Math.Min(line.Length, s1);
        double best = double.PositiveInfinity, at = s0;
        for (double d = s0; d <= s1; d += 20)
        {
            var p = line.Sample(d).Position;
            double q = (p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z);
            if (q < best)
                (best, at) = (q, d);
        }
        for (double d = Math.Max(s0, at - 20); d <= Math.Min(s1, at + 20); d += 2)
        {
            var p = line.Sample(d).Position;
            double q = (p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z);
            if (q < best)
                (best, at) = (q, d);
        }
        var t = line.Sample(at);
        var r = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        return (at, (x - t.Position.X) * r.X + (z - t.Position.Z) * r.Z);
    }
}
