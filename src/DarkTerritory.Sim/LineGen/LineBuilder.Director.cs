using Ballast;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// Stage 8 (plan §15): what the director spends its budget on. Sleepers and Grease are level content, placed now from
/// candidate zones (App. B.2); the rest spawn at night by the tags. The terrain pressure curve lets the director keep its
/// own spikes off the terrain's crunches, or seed a conflict pair there on purpose.
/// </summary>
sealed partial class LineBuilder
{
    void LayDirector()
    {
        var rng = Rng("director");
        var dr = _t.Director;
        _sleeperZones.Clear();
        _greaseZones.Clear();
        _sleepers.Clear();
        _grease.Clear();
        // §15.2 Sleeper zones: only inside restricted zones, never in the first 2 km.
        foreach (var z in _restricted.Where(r => r.Edge == "main" && r.Sleepers))
            if (z.S1 > _gate + _t.Budget.GraceM)
                _sleeperZones.Add(new PlanZone("main", R(Math.Max(z.S0, _gate + _t.Budget.GraceM)), z.S1));
        double km = (_terminus - _gate) / 1000;
        int sleepers = _sleeperZones.Count == 0 ? 0 : (int)Math.Round(dr.SleeperZonesPerKm * _l.SleeperDensity * km * rng.Range(0.8, 1.2));
        double zoneLength = _sleeperZones.Sum(z => z.S1 - z.S0);
        for (int i = 0, tries = 0; i < sleepers && tries < sleepers * 30; tries++)
        {
            // A spot in the zones, weighted by their length.
            double pick = rng.NextDouble() * zoneLength, s = -1;
            foreach (var z in _sleeperZones)
            {
                if (pick < z.S1 - z.S0)
                {
                    s = z.S0 + pick;
                    break;
                }
                pick -= z.S1 - z.S0;
            }
            s = Math.Round(s);
            var zone = _sleeperZones.FirstOrDefault(z => s >= z.S0 && s + dr.SleeperLengthM <= z.S1);
            if (zone is null || _sleepers.Any(x => Math.Abs(x.S0 - s) < 200))
                continue;
            _sleepers.Add(new PlanZone("main", s, s + dr.SleeperLengthM));
            i++;
        }
        _sleepers.Sort((a, b) => a.S0.CompareTo(b.S0));

        // §15.2 Grease zones: grades and the approaches to limited curves, doubled in wet or cold (App. B.2).
        var main = LineOf("main");
        bool Clear(double s) => s > _gate + 400 && s < _terminus - _t.Terminus.SpawnBanM - 200 && !_facilities.Any(f => s > f.LullStart && s < f.LullEnd)
            && !_alts.Any(w => Math.Abs(s - w.T) < 300 || Math.Abs(s - w.J) < 300) && !_deads.Any(d => Math.Abs(s - d.Toe) < 300);
        for (double s = _gate; s < _terminus; s += 100)
            if (Math.Abs(main.Sample(s).GradePercent) >= 1 && Clear(s))
                _greaseZones.Add(new PlanZone("main", R(s), R(s + 100)));
        foreach (var l in _limits.Where(l => l.Edge == "main" && l.Source == LimitSource.Curve))
            if (Clear(l.S0 - 150))
                _greaseZones.Add(new PlanZone("main", R(l.S0 - 250), R(l.S0 - 20)));
        _greaseZones.Sort((a, b) => a.S0.CompareTo(b.S0));
        double rate = dr.GreasePerKm * (_weather!.Rain || _weather.Cold > 0.5 ? dr.GreaseWetCold : 1);
        int grease = _greaseZones.Count == 0 ? 0 : (int)Math.Round(rate * km * rng.Range(0.8, 1.2));
        for (int i = 0, tries = 0; i < grease && tries < grease * 30; tries++)
        {
            var z = rng.Pick(_greaseZones);
            double len = Math.Round(rng.Range(dr.GreaseLengthM));
            double s = Math.Round(rng.Range(z.S0, z.S1));
            if (_grease.Any(x => Math.Abs(x.S0 - s) < 300) || !Clear(s + len))
                continue;
            _grease.Add(new PlanZone("main", s, s + len));
            i++;
        }
        _grease.Sort((a, b) => a.S0.CompareTo(b.S0));
        LayPressure();
    }

    /// <summary>Each piece's demand window (§7.3): from the start of its tell zone to its end.</summary>
    IEnumerable<(Item Piece, string Edge, double From, double To)> DemandWindows()
    {
        foreach (var e in Routable())
            foreach (var p in e.Items.SelectMany(i => i.All()).Where(i => i.IsPiece && i.Kind != "settlement"))
            {
                double from = p.S0;
                foreach (var d in _demands)
                    if (d.Edge == e.Id && d.SReq >= p.S0 - 5 && d.SReq <= p.S1 + 5 && d.TellAt > -99999)
                        from = Math.Min(from, d.TellAt);
                yield return (p, e.Id, from, p.S1);
            }
    }

    /// <summary>§15.4: the stacked cost of the demand windows active at each chainage of the main line; and the stack depth.</summary>
    void LayPressure()
    {
        double step = _t.Director.PressureStepM;
        int n = (int)Math.Ceiling(_end / step) + 1;
        _pressure = new double[n];
        var depth = new int[n];
        foreach (var (p, edge, from, to) in DemandWindows().Where(w => w.Edge == "main"))
            for (int i = Math.Max(0, (int)(from / step)); i < n && i * step <= to; i++)
            {
                _pressure[i] += p.Cost;
                depth[i]++;
            }
        for (int i = 0; i < n; i++)
            _pressure[i] = Math.Round(_pressure[i], 2);
        _stackDepth = depth;
    }

    int[] _stackDepth = [];
}
