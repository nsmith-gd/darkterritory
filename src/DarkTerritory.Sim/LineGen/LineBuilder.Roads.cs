using Ballast;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// Stage 4b, with the waterside (docs/design/maritime-rules.md §2.2): the country road. Where the biome is settled, a
/// gravel road runs beside the main line and crosses it at grade now and then, as the Maritime roads and railways
/// share the same valleys. Plan data, so its bed is in the terrain every machine walks on; it never moves the track.
/// </summary>
sealed partial class LineBuilder
{
    readonly List<PlanRoad> _roads = new();
    readonly List<PlanCrossing> _crossings = new();

    /// <summary>
    /// Roads over the biome regions that roll one, merged where they meet, cut round structures, cuttings and
    /// junctions; each piece on the landward side of any shore it runs by, crossing the line where both sides are open.
    /// </summary>
    void LayRoads(ref Pcg32 rng)
    {
        var rr = _t.Terrain.Roads;
        double first = _gate + _t.Budget.GraceM * 0.5, last = _terminus - _t.Terminus.SkyGlowM * 0.3;
        var spans = new List<(double S0, double S1)>();
        foreach (var (biome, b0, b1) in _biomes)
        {
            if (!_c.Config.Biomes.Biomes.TryGetValue(biome, out var def) || def.Roads <= 0 || !rng.Chance(def.Roads))
                continue;
            double s0 = Math.Max(b0, first), s1 = Math.Min(b1, last);
            if (s1 <= s0)
                continue;
            if (spans.Count > 0 && s0 - spans[^1].S1 < 1)
                spans[^1] = (spans[^1].S0, s1);
            else
                spans.Add((s0, s1));
        }
        var toes = _edges.Values.Where(e => e.Role != EdgeRole.Main).Select(e => e.Toe).ToList();
        bool Blocked(double s) => !OpenGround(s) || toes.Any(t => Math.Abs(t - s) < rr.ClearM + 150)
            || _pads.Any(p => NearestMainCached(p) is var (at, lat) && Math.Abs(at - s) < p.RadiusM + p.HalfLengthM + 40 && Math.Abs(lat) < p.RadiusM + 60);
        foreach (var (s0, s1) in spans)
        {
            double? start = null;
            for (double s = s0; s <= s1 + 20; s += 20)
            {
                bool ok = s <= s1 && !Blocked(s) && !Blocked(s + rr.ClearM) && !Blocked(s - rr.ClearM);
                if (ok && start is null)
                    start = s;
                else if (!ok && start is { } a)
                {
                    if (s - 20 - a >= rr.MinM)
                        LayRoad(a, s - 20, ref rng);
                    start = null;
                }
            }
        }
    }

    readonly Dictionary<PlanPad, (double, double)> _padNear = new();
    (double S, double Lateral) NearestMainCached(PlanPad p)
    {
        if (!_padNear.TryGetValue(p, out var v))
            _padNear[p] = v = NearestMain(p.X, p.Z, 0, _line!.Length);
        return v;
    }

    void LayRoad(double s0, double s1, ref Pcg32 rng)
    {
        var rr = _t.Terrain.Roads;
        // Landward of any shore it runs by; and it doesn't cross where a shore holds either side.
        int landward = 0;
        foreach (var sh in _shores)
            if (sh.S1 > s0 && sh.S0 < s1)
            {
                // Along the Atlantic the land between the line and the sea is the barachois's; the road keeps inland of
                // it, out past what the corridor shows, so none is laid there.
                if (sh.Kind == ShoreKind.Sea)
                    return;
                landward = -sh.Side;
            }
        int side = landward != 0 ? landward : rng.Chance(0.5) ? 1 : -1;
        var road = new PlanRoad($"road{_roads.Count + 1}", "main", R(s0), R(s1), side, R(rng.Range(rr.OffsetM)), R(rng.Range(rr.WanderM)),
            R(rng.Range(rr.WanderWavelengthM)), R(rng.Range(0, 1000)));
        _roads.Add(road);
        if (landward != 0)
            return;
        for (double s = s0 + rng.Range(rr.CrossEveryM) * 0.6; s < s1 - rr.RampM * 2; s += rng.Range(rr.CrossEveryM))
            if (OpenGround(s) && OpenGround(s - rr.RampM) && OpenGround(s + rr.RampM))
                _crossings.Add(new PlanCrossing(road.Id, R(s)));
    }
}
