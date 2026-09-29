using Ballast;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// Stage 4 (plan §9): speed authority and its tells. The fairness guarantee: a train driven at or below the
/// communicated speed, by a driver who reacts within t_react, can meet every demand downstream.
/// </summary>
sealed partial class LineBuilder
{
    TerrainField? _terrain;
    /// <summary>The worst brake effectiveness the validator measured approaching each demand (by id), for §9.3's f_fade.</summary>
    readonly Dictionary<string, double> _fade = new();

    /// <summary>The edges a train is driven over: the main line and the alternates (spurs and dead lines have their own rules).</summary>
    IEnumerable<EdgeDraft> Routable() => _edges.Values.Where(e => e.Role is EdgeRole.Main or EdgeRole.Alternate).OrderBy(e => e.Role == EdgeRole.Main ? -1 : e.Branch);

    void LayAuthority()
    {
        _limits.Clear();
        _restricted.Clear();
        _demands.Clear();
        _terrain ??= new TerrainField(Freeze(), _line!, _t.Terrain);
        var rng = Rng("authority");
        foreach (var e in Routable())
        {
            CurveLimits(e);
            StructureLimits(e);
        }
        _limits.Add(new PlanLimit("main", 0, R(_gate), _t.Fortress.YardSpeed, LimitSource.Yard, "fortress yard"));
        _limits.Add(new PlanLimit("main", R(_terminus), R(_end), _t.Terminus.YardLimitSpeed, LimitSource.Yard, "arrival yard"));
        foreach (var e in _edges.Values.Where(e => e.Role is EdgeRole.Spur))
            _limits.Add(new PlanLimit(e.Id, 0, R(e.Length), _t.Fortress.YardSpeed, LimitSource.Yard, "spur"));
        RestrictedZones(ref rng);
        Demands();
    }

    double Communicated(string edge, double s)
    {
        double v = _l.LineSpeed;
        foreach (var l in _limits)
            if (l.Edge == edge && s >= l.S0 && s <= l.S1)
                v = Math.Min(v, l.VMs);
        foreach (var r in _restricted)
            if (r.Edge == edge && s >= r.S0 && s <= r.S1)
                v = Math.Min(v, r.VMs);
        return v;
    }

    /// <summary>§8.5: a curve gets a board when its posted limit, floor(√(a_post R)), is below the speed approaching it.</summary>
    void CurveLimits(EdgeDraft e)
    {
        var line = LineOf(e);
        var c = _t.Curves;
        double start = -1, minR = double.MaxValue;
        for (double s = 0; s <= line.Length + 5; s += 5)
        {
            double k = s <= line.Length ? Math.Abs(line.Sample(s).Curvature) : 0;
            double posted = k < 1e-9 ? double.MaxValue : Math.Floor(Math.Sqrt(c.APost / k));
            bool limited = posted < _l.LineSpeed;
            if (limited)
            {
                if (start < 0)
                    start = s;
                minR = Math.Min(minR, 1 / k);
            }
            else if (start >= 0)
            {
                double v = Math.Floor(Math.Sqrt(c.APost * minR));
                // The whole train is held to it until its tail is round (the resume board a train's length on).
                _limits.Add(new PlanLimit(e.Id, R(start - 5), R(Math.Min(line.Length, s + _l.ConsistLength + _t.Authority.ResumeAfterExtraM)), v, LimitSource.Curve,
                    $"curve R {minR:0} m, derails at {Math.Sqrt(c.ADerail * minR):0.0} m/s"));
                start = -1;
                minR = double.MaxValue;
            }
        }
    }

    /// <summary>§9.2: weak bridges at their crossing speed, brass fields at cutting speed, the whole train's length over.</summary>
    void StructureLimits(EdgeDraft e)
    {
        foreach (var st in _structures.Where(s => s.Edge == e.Id))
        {
            if (st.Weak is { } weak)
                _limits.Add(new PlanLimit(e.Id, st.S0, R(st.S1 + _l.ConsistLength), weak.SpeedMs, LimitSource.Bridge, $"{st.Name}: max {weak.MaxCars} cars"));
            if (st.Type == StructureType.BrassField)
                _limits.Add(new PlanLimit(e.Id, st.S0, R(st.S1 + _l.ConsistLength), _t.Hazards.BrassCuttingSpeed, LimitSource.Brass, "brass across the rail"));
        }
    }

    /// <summary>
    /// §9.4: sight(s) = min(fog, lamp, geometric sightline). The geometric sightline raymarches from the lamp along the
    /// track over the height field: a cutting on a curve shortens it.
    /// </summary>
    double Sight(EdgeDraft e, double s)
    {
        var line = LineOf(e);
        var a = _t.Authority;
        var lamp = line.Sample(s).Position + Double3.Up * a.LampHeightM;
        double fog = _weather!.FogMinM, reach = Math.Min(fog, _c.Enemies.Sleepers.LampRevealDistance);
        for (double d = 15; d <= reach; d += 15)
        {
            if (s + d > line.Length)
                return d;
            var target = line.Sample(s + d).Position + Double3.Up * a.TargetHeightM;
            // Three points along the ray: is the land above it anywhere? (On straight level track it never is.)
            if (Math.Abs(line.Sample(s + d / 2).Curvature) < 1e-5 && Math.Abs(line.Sample(s + d / 2).GradePercent - line.Sample(s).GradePercent) < 0.2)
                continue;
            for (int k = 1; k <= 3; k++)
            {
                var p = Double3.Lerp(lamp, target, k / 4.0);
                if (_terrain!.Height(p.X, p.Z) > p.Y + 0.2)
                    return d - 15;
            }
        }
        return reach;
    }

    /// <summary>
    /// §9.4 restricted speed: the fastest a train can go and still react and brake to the Sleepers' safe speed within
    /// its sight over the tier's margin.
    /// </summary>
    double RestrictedSpeed(double sight, double brake)
    {
        double vSafe = _c.Enemies.Sleepers.DerailAbove - _t.Authority.SleeperSafeMargin;
        double avail = sight / _l.TellMargin;
        double t = _t.Reaction.TReactS;
        // v t + (v² − vs²) / (2b) = avail  →  v² + 2 b t v − (vs² + 2 b avail) = 0
        double v = -brake * t + Math.Sqrt(brake * brake * t * t + vSafe * vSafe + 2 * brake * avail);
        return Math.Max(_t.Authority.MinRestrictedSpeed, Math.Floor(v));
    }

    /// <summary>
    /// Restricted zones (§9.4): where the geometry is blinder than a train can stop in, and where the generator wants
    /// Sleeper candidates. Not every zone holds Sleepers, so crews can't learn to ignore the board.
    /// </summary>
    void RestrictedZones(ref Pcg32 rng)
    {
        var a = _t.Authority;
        double brake = _l.Brake * WorstAdhesionAll();
        foreach (var e in Routable())
        {
            var line = LineOf(e);
            // Where the land blinds the lamp inside the fog: its own zone, whatever the Sleepers.
            double start = -1, least = double.MaxValue;
            for (double s = e.Role == EdgeRole.Main ? _gate + _t.Budget.GraceM : 200; s < line.Length - 200; s += a.SightStepM)
            {
                double sight = Sight(e, s);
                if (sight < a.BlindSightM)
                {
                    if (start < 0)
                        start = s;
                    least = Math.Min(least, sight);
                }
                else if (start >= 0)
                {
                    AddRestricted(e.Id, start - 60, s + 60, least, brake, sleepers: false, "blind");
                    start = -1;
                    least = double.MaxValue;
                }
            }
        }
        // Sleeper country (§15.2): zones on the main line where Sleepers would lie, ×1 / ×2 / ×3.5 by tier.
        double km = (_terminus - _gate) / 1000;
        int zones = rng.Round(a.RestrictedPerKm * _l.SleeperDensity * km);
        var main = Main;
        var blocked = new List<(double, double)> { (0, _gate + _t.Budget.GraceM), (_terminus - _t.Terminus.SpawnBanM - 800, _end) };
        foreach (var f in _facilities)
            blocked.Add((f.S - f.Holding - 300, f.LullEnd));
        foreach (var w in _alts)
        {
            blocked.Add((w.T - 400, w.T + 200));
            blocked.Add((w.J - 200, w.J + 200));
        }
        foreach (var d in _deads)
            blocked.Add((d.Toe - 400, d.Toe + 200));
        int placedEmpty = 0;
        for (int i = 0, tries = 0; i < zones && tries < 300; tries++)
        {
            double len = rng.Range(a.RestrictedLengthM);
            // Straights and blind curve exits (App. B.2): start from a tagged spot where there is one.
            var spots = _tags.Where(t => t.Edge == "main" && t.Tag is "straight_long" or "blind_curve_exit").ToList();
            double s0 = spots.Count > 0 && rng.Chance(0.7) ? rng.Pick(spots).S0 - rng.Range(0, 300) : rng.Range(_gate + _t.Budget.GraceM, _terminus - len);
            s0 = Math.Round(s0 / 10) * 10;
            double s1 = s0 + len;
            if (blocked.Any(b => s1 > b.Item1 && s0 < b.Item2) || _restricted.Any(r => r.Edge == "main" && s1 + 400 > r.S0 && s0 - 400 < r.S1))
                continue;
            double sight = double.MaxValue;
            for (double s = s0; s <= s1; s += a.SightStepM * 2)
                sight = Math.Min(sight, Sight(main, s));
            bool empty = placedEmpty < Math.Round(zones * a.RestrictedEmptyShare) && rng.Chance(a.RestrictedEmptyShare * 1.5);
            if (empty)
                placedEmpty++;
            AddRestricted("main", s0, s1, sight, brake, sleepers: !empty, empty ? "restricted (no Sleepers reported)" : "Sleeper country");
            blocked.Add((s0 - 400, s1 + 400));
            i++;
        }
        _restricted.Sort((x, y) => x.Edge != y.Edge ? string.CompareOrdinal(x.Edge, y.Edge) : x.S0.CompareTo(y.S0));
    }

    void AddRestricted(string edge, double s0, double s1, double sight, double brake, bool sleepers, string reason)
    {
        double v = Math.Min(_l.LineSpeed, RestrictedSpeed(sight, brake));
        _restricted.Add(new PlanRestricted(edge, R(Math.Max(0, s0)), R(s1), v, Math.Round(sight), sleepers, reason));
    }

    double WorstAdhesionAll() => _l.RainChance > 0 ? Math.Min(_t.Weather.WetAdhesion, _t.Weather.WetBiasAdhesion) : 1;

    /// <summary>
    /// §9.2–9.3: every point the track needs the train at or below a speed by a place, with its warning distance
    /// d_warn = v t_react + (v² − v_req²) / (2 (a_brake f_fade − g_down)), and the tell zone that starts d_warn × margin
    /// before it.
    /// </summary>
    void Demands()
    {
        int n = 0;
        string Id() => $"d{++n}";
        foreach (var e in Routable())
        {
            var line = LineOf(e);
            // Every drop in the communicated speed is a demand: limit zones' starts, restricted entries.
            var starts = _limits.Where(l => l.Edge == e.Id && l.Source != LimitSource.Yard).Select(l => (l.S0, l.VMs, l.Source, (string?)l.Why))
                .Concat(_restricted.Where(r => r.Edge == e.Id).Select(r => (r.S0, r.VMs, LimitSource.Restricted, (string?)r.Reason)))
                .OrderBy(x => x.S0).ToList();
            foreach (var (s, v, source, why) in starts)
            {
                double vin = Communicated(e.Id, s - 5);
                if (vin <= v + 1e-6)
                    continue;
                double? lethal = source == LimitSource.Curve ? Math.Sqrt(_t.Curves.ADerail * MinRadiusFrom(line, s)) : null;
                var type = source switch
                {
                    LimitSource.Curve => DemandType.Curve,
                    LimitSource.Bridge => DemandType.WeakBridge,
                    LimitSource.Brass => DemandType.Brass,
                    _ => DemandType.Restricted,
                };
                AddDemand(Id(), type, e, s, v, vin, lethal, s + 1);
            }
        }
        foreach (var slot in _facilities)
        {
            double stop = slot.OnSpur ? slot.S - 10 : slot.S;
            AddDemand(Id(), DemandType.FacilityStop, Main, stop, 0, Communicated("main", stop - 50), null, stop);
        }
        foreach (var w in _alts)
            AddDemand(Id(), DemandType.FacingJunction, Main, w.T, 0, Communicated("main", w.T - 50), null, w.T);
        foreach (var d in _deads)
            AddDemand(Id(), DemandType.FacingJunction, Main, d.Toe, 0, Communicated("main", d.Toe - 50), null, d.Toe);
        foreach (var st in _structures.Where(s => s.Type == StructureType.Washout))
        {
            var e = _edges[st.Edge];
            AddDemand(Id(), DemandType.Washout, e, st.S0, 0, Communicated(st.Edge, st.S0 - 50), 0, st.S1);
        }
        AddDemand(Id(), DemandType.YardLimit, Main, _terminus, _t.Terminus.YardLimitSpeed, Communicated("main", _terminus - 50), null, _end);
    }

    static double MinRadiusFrom(RailLine line, double s)
    {
        double k = 0;
        for (double x = s; x < Math.Min(line.Length, s + 2000); x += 5)
        {
            double c = Math.Abs(line.Sample(x).Curvature);
            if (c < 1e-6 && k > 0)
                break;
            k = Math.Max(k, c);
        }
        return k > 0 ? 1 / k : double.MaxValue;
    }

    /// <summary>
    /// §9.3's warning distance over the approach: the real consist's braking (from the sim) on the worst wet rail, the
    /// worst brake effectiveness the validator found on that approach, and gravity on a descent.
    /// </summary>
    double WarnDistance(EdgeDraft e, double s, double vin, double vreq, string id)
    {
        var line = LineOf(e);
        double adhesion = WorstAdhesion(e.Id, s - 1500, s);
        double fade = _fade.GetValueOrDefault(id, 1);
        // Braking distance first on the flat, then the grade averaged over it.
        double brake = _l.Brake * adhesion * fade;
        double guess = vin * _t.Reaction.TReactS + (vin * vin - vreq * vreq) / (2 * Math.Max(0.05, brake));
        double g = 0;
        int k = 0;
        for (double x = Math.Max(0, s - guess); x <= s; x += 20, k++)
            g += line.Sample(x).GradePercent;
        g = k > 0 ? g / k : 0;
        double down = g < 0 ? _c.Train.Gravity * Math.Sin(Math.Atan(-g / 100)) : 0;
        double net = brake - down;
        if (net <= 0.02)
            return double.PositiveInfinity;
        return vin * _t.Reaction.TReactS + (vin * vin - vreq * vreq) / (2 * net);
    }

    void AddDemand(string id, DemandType type, EdgeDraft e, double s, double vreq, double vin, double? lethal, double end, string? piece = null)
    {
        double warn = WarnDistance(e, s, vin, vreq, id);
        double tell = s - warn * _l.TellMargin;
        _demands.Add(new PlanDemand(id, type, e.Id, R(s), vreq, vin, double.IsInfinity(warn) ? 99999 : R(warn), double.IsInfinity(warn) ? -99999 : R(tell), lethal is { } l ? Math.Round(l, 2) : null, R(end), piece));
    }
}
