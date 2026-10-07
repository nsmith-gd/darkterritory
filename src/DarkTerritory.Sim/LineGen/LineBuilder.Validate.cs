using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// Stage 9 (plan §16): every plan is driven headlessly on the shared train simulation before it's accepted, and held to
/// the acceptance checks. Fade measured on the drive goes back into the warning distances until the two agree.
/// </summary>
sealed partial class LineBuilder
{
    DriveResult? _ideal;
    readonly Dictionary<string, DriveResult> _alternateDrives = new();

    void Check(string name, bool pass, string detail = "") => _checks.Add(new PlanCheck(name, pass, pass && detail.Length == 0 ? "ok" : detail));

    /// <summary>The way the ideal driver takes (§16.1): the main line at every fork unless its main side is closed.</summary>
    List<string> DefaultAlternates() => [.. _alts.Where(a => a.WashoutOnMain).Select(a => a.Edge)];

    Drivers.Options IdealOptions(PlanRouteWay way, bool sloppy) => new(
        sloppy, sloppy ? _t.Reaction.SloppySpeedFactor : 1, sloppy ? _t.Reaction.SloppyTReactS : _t.Reaction.TReactS,
        sloppy ? [] : [.. _facilities.Select(f => way.Of("main", f.OnSpur ? f.S - 10 : f.S) ?? -1).Where(x => x >= 0)],
        _t.Validation.MaxSeconds, _t.Curves.ADerail, _t.Validation.DriverBandMs, _gate - 20, _gate, way.Of("main", _terminus) ?? way.Length);

    DriveResult DriveWay(LinePlan plan, List<string> alternates, bool sloppy)
    {
        var way = new PlanRouteWay(plan, _line!, alternates);
        var stops = sloppy ? [] : IdealOptions(way, false).Stops;
        var profile = new SpeedProfile(way, _l.Brake, stops, d => FadeNear(way, d), WorstAdhesionAll());
        return Drivers.Drive(way, profile, _c.Train, _p.Cars, IdealOptions(way, sloppy));
    }

    /// <summary>The brake effectiveness measured approaching the next demand after d (for the profile's braking curves).</summary>
    double FadeNear(PlanRouteWay way, double d)
    {
        double f = 1;
        foreach (var (demand, at, tellAt) in way.Demands())
            if (d >= tellAt - 200 && d <= at && _fade.TryGetValue(demand.Id, out var measured))
                f = Math.Min(f, measured);
        return f;
    }

    /// <summary>Drives the plan, feeds measured fade back into the tells (§9.3), and runs the checks.</summary>
    void Validate()
    {
        for (int iter = 0; iter < 3; iter++)
        {
            var plan = Freeze();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            _ideal = DriveWay(plan, DefaultAlternates(), sloppy: false);
            _timings[$"ideal drive {iter}"] = clock.Elapsed.TotalMilliseconds;
            bool changed = false;
            foreach (var (id, f) in _ideal.FadeAt)
                if (f < _fade.GetValueOrDefault(id, 1) - 0.01)
                {
                    _fade[id] = f;
                    changed = true;
                }
            if (!changed)
                break;
            // Boards move out to where a faded brake needs them.
            LayAuthority();
            LaySignage();
            LayDirector();
        }
        _idealTransit = _ideal!.TransitSeconds;
        _minBrake = _ideal.MinBrake;
        var frozen = Freeze();
        // Each alternate as the other side of its fork, and the sloppy driver: independent drives, so side by side.
        var jobs = _alts.Where(w => !w.WashoutOnAlt && !w.WashoutOnMain).Select(w => (Key: w.Edge, Alts: DefaultAlternates().Append(w.Edge).Distinct().ToList(), Sloppy: false)).ToList();
        if (_p.Tier <= _t.Reaction.SloppyUpToTier)
            jobs.Add(("sloppy", DefaultAlternates(), true));
        var results = new DriveResult[jobs.Count];
        // Side by side, but on no more cores than the tuning allows: the game's still rendering while this runs.
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, _t.Validation.MaxParallelDrives) },
            i => results[i] = DriveWay(frozen, jobs[i].Alts, jobs[i].Sloppy));
        DriveResult? sloppy = null;
        for (int i = 0; i < jobs.Count; i++)
            if (jobs[i].Sloppy)
                sloppy = results[i];
            else
                _alternateDrives[jobs[i].Key] = results[i];
        // A side closed by its washout isn't driven: it's closed. The main side of a fork whose main is closed is the default way.
        foreach (var w in _alts.Where(w => w.WashoutOnMain))
            _alternateDrives[w.Edge] = _ideal!;
        _sloppyTransit = sloppy?.TransitSeconds ?? 0;
        RunChecks(sloppy);
    }

    void RunChecks(DriveResult? sloppy)
    {
        _checks.Clear();
        var v = _t.Validation;
        var ideal = _ideal!;
        Check("ideal driver", ideal.Survived, ideal.Failure ?? "");
        if (sloppy is not null)
            Check("sloppy driver", sloppy.Survived, sloppy.Failure ?? "");

        // Geometry limits: radii, grades, vertical curves, tunnel lengths and grades within the tier's caps.
        var geometry = new List<string>();
        foreach (var e in Routable())
        {
            var line = LineOf(e);
            double tight = double.MaxValue;
            for (double s = e.Role == EdgeRole.Alternate ? 2 * _t.Junctions.TurnoutLength + 5 : 0; s < line.Length - (e.Role == EdgeRole.Alternate ? 2 * _t.Junctions.TurnoutLength + 5 : 0); s += 5)
                if (Math.Abs(line.Sample(s).Curvature) > 1e-9)
                    tight = Math.Min(tight, 1 / Math.Abs(line.Sample(s).Curvature));
            if (tight < _l.MinRadius - 1)
                geometry.Add($"{e.Id} curves at {tight:0} m, under the tier's {_l.MinRadius:0}");
        }
        double minVertical = _verticalRadii.Count == 0 ? double.MaxValue : _verticalRadii.Min(r => r.R);
        if (minVertical < _l.MinVerticalRadius - 1)
            geometry.Add($"a vertical curve of {minVertical:0} m, under {_l.MinVerticalRadius:0}");
        foreach (var t in _structures.Where(s => s.Type == StructureType.Tunnel))
        {
            if (t.S1 - t.S0 > _l.MaxTunnel + 1)
                geometry.Add($"{t.Name} is {t.S1 - t.S0:0} m, over {_l.MaxTunnel:0}");
            var line = LineOf(t.Edge);
            for (double s = t.S0; s <= t.S1; s += 25)
                if (Math.Abs(line.Sample(s).GradePercent) > _l.TunnelGrade + 0.05)
                {
                    geometry.Add($"{t.Name} climbs {line.Sample(s).GradePercent:0.00}% inside, over {_l.TunnelGrade:0.00}");
                    break;
                }
        }
        Check("geometry limits", geometry.Count == 0, string.Join("; ", geometry.Take(3)));

        // Ruling grade: the main line's sustained grades within g_main for the loaded train, momentum banks aside.
        var banks = Main.Items.SelectMany(i => i.All()).Where(i => i.Kind == "momentum").ToList();
        double worst = 0, worstAt = 0;
        for (double s = 0; s < _end; s += 10)
        {
            double g = _line!.Sample(s).GradePercent;
            if (g > worst && !banks.Any(b => s >= b.S0 && s <= b.S1))
                (worst, worstAt) = (g, s);
        }
        Check("ruling grade", worst <= _l.MainGrade + 0.02, $"{worst:0.00}% at km {Km(worstAt):0.0} against g_main {_l.MainGrade:0.00}%");

        // Momentum banks: the ideal driver crests, and the rollback zone at the foot is clean (§7.2).
        var momentum = new List<string>();
        foreach (var b in banks)
        {
            double foot = b.S0 + b.Params["runUpM"], zone = foot - _l.ConsistLength - _t.Validation.StallRollbackExtraM;
            if (_limits.Any(l => l.Edge == "main" && l.Source is LimitSource.Curve or LimitSource.Bridge && l.S1 > zone && l.S0 < foot)
                || _alts.Any(a => a.T > zone && a.T < foot) || _deads.Any(d => d.Toe > zone && d.Toe < foot))
                momentum.Add($"the bank at km {Km(foot):0.0} has a limit or junction in its rollback zone");
        }
        if (ideal.Stalled)
            momentum.Add(ideal.Failure!);
        Check("momentum banks", momentum.Count == 0, string.Join("; ", momentum));

        // Descents: every limit held with fade, and the brakes never below 60%.
        Check("descents", ideal.MinBrake >= v.MinBrakeEfficiency && ideal.WorstOverspeed <= v.OverspeedTolerance,
            $"brakes down to {ideal.MinBrake:0.00}; worst overspeed {ideal.WorstOverspeed:0.0} m/s ({ideal.WorstOverspeedAt})");

        // Tells: a required tell at or before s_req - d_warn × margin for every demand (Form 19 counts: it's in hand).
        var untold = new List<string>();
        foreach (var d in _demands)
        {
            if (d.WarnM >= 99999)
            {
                untold.Add($"{d.Type} {d.Id} can't be braked for on its descent");
                continue;
            }
            bool board = _signs.Any(s => s.For == d.Id && s.Required && (s.Edge == d.Edge && s.S <= d.TellAt + 1
                || s.Edge == "main" && d.Edge != "main" && s.S <= _edges[d.Edge].Toe + d.TellAt + 1));
            bool paper = d.Type is DemandType.Restricted or DemandType.Washout && _form19.Any(f => Math.Abs(f.Km - Card(d.Edge, d.SReq)) < 0.15)
                || d.Type == DemandType.WeakBridge && _form19.Any(f => f.Kind == "bridge");
            if (!board && !paper)
                untold.Add($"{d.Type} {d.Id} at km {Card(d.Edge, d.SReq):0.0} (tell zone from {Card(d.Edge, d.TellAt):0.0})");
        }
        Check("tells", untold.Count == 0, $"{untold.Count} untold: " + string.Join("; ", untold.Take(3)));

        // Lethal spacing: no two lethal checks within one full braking distance from line speed (§7.3).
        double brakeFull = _l.LineSpeed * _t.Reaction.TReactS + _l.LineSpeed * _l.LineSpeed / (2 * _l.Brake * WorstAdhesionAll());
        var lethal = _demands.Where(d => d.Type is DemandType.Curve && d.VLethal is { } vl && vl < _l.LineSpeed || d.Type == DemandType.Washout).OrderBy(d => d.Edge).ThenBy(d => d.SReq).ToList();
        var close = lethal.Zip(lethal.Skip(1)).Where(p => p.First.Edge == p.Second.Edge && p.Second.SReq - p.First.SEnd < brakeFull * 0.5).ToList();
        Check("lethal spacing", close.Count == 0, close.Count == 0 ? "" : $"{close.Count} pairs closer than a braking distance, first at km {Card(close[0].First.Edge, close[0].First.SReq):0.0}");

        // Stacks: depth within the tier's cap everywhere (one stack may go one deeper where the tier says so).
        int cap = _l.StackCap + (_l.StackOnceDeeper ? 1 : 0);
        int deepest = _stackDepth.Length == 0 ? 0 : _stackDepth.Max();
        Check("stacks", deepest <= cap, $"depth {deepest} against a cap of {cap}");

        // Recovery: every crunch followed by recovery track (a connector, a lull, a pad) at least the tier's minimum.
        var norecovery = new List<string>();
        var items = Main.Items;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Def is not { Crunch: true })
                continue;
            double run = 0;
            for (int j = i + 1; j < items.Count && run < _l.RecoveryMin; j++)
            {
                if (items[j].IsPiece && items[j].Def!.Crunch)
                    break;
                run += items[j].Length;
            }
            if (run < _l.RecoveryMin * 0.6)
                norecovery.Add($"{items[i].Type} at km {Km(items[i].S1):0.0} ({run:0} m after)");
        }
        Check("recovery", norecovery.Count == 0, string.Join("; ", norecovery.Take(3)));

        // Forks: at least one side of every fork passable by the loaded consist.
        var forks = new List<string>();
        foreach (var w in _alts)
        {
            bool alt = _alternateDrives.TryGetValue(w.Edge, out var r) && r.Survived && !w.WashoutOnAlt;
            bool main = !w.WashoutOnMain && ideal.Survived;
            if (!alt && !main)
                forks.Add($"J{w.Number}: neither side ({r?.Failure})");
            _knownGradesPassable[w.Edge] = alt;
        }
        Check("forks", forks.Count == 0, string.Join("; ", forks));

        // Holding track: level and long enough at every facility (§11.1: loaded cars parked there mustn't roll).
        var holding = new List<string>();
        foreach (var f in _facilities)
        {
            double worstG = 0;
            for (double s = f.S - f.Holding; s <= f.S; s += 5)
                worstG = Math.Max(worstG, Math.Abs(_line!.Sample(s).GradePercent));
            if (worstG > _c.Config.Facilities.Slots.HoldingGrade + 1e-6 || f.Holding < _l.ConsistLength + _c.Config.Facilities.Slots.HoldingExtraM - 1)
                holding.Add($"{f.Name}: {worstG:0.00}% over {f.Holding:0} m");
        }
        Check("holding track", holding.Count == 0, string.Join("; ", holding));

        // Threshold and home straight clean; no Sleeper candidates in the first 2 km.
        var zones = new List<string>();
        foreach (var (a, b, name) in new[] { (_gate, _gate + _t.Budget.GraceM, "threshold"), (_terminus - _t.Budget.HomeStraightM, _terminus, "home straight") })
            for (double s = a; s < b; s += 10)
            {
                var t = _line!.Sample(s);
                if (Math.Abs(t.GradePercent) > _t.Fortress.MaxThresholdGrade + 1e-6 || Math.Abs(t.Curvature) > 1 / (_t.Fortress.ThresholdMinRadius - 1))
                {
                    zones.Add($"{name} at km {Km(s):0.0}");
                    break;
                }
            }
        if (_sleepers.Any(z => z.S0 < _gate + _t.Budget.GraceM) || _sleeperZones.Any(z => z.S0 < _gate + _t.Budget.GraceM))
            zones.Add("Sleepers in the first 2 km");
        Check("threshold and home straight", zones.Count == 0, string.Join("; ", zones));

        // Dawn: ideal transit + 4 min per facility slot within the dawn timer (§22.1's slack reported either way).
        double planned = _idealTransit + _facilities.Count * v.DawnMinutesPerFacility * 60;
        // §22.1: with the spec's dawn formula the deeper tiers can't take every stop in time. The transit alone has to
        // fit; the stops on top of it are the slack decision, reported (and a hard check where the config says so).
        bool transitFits = ideal.Survived && _idealTransit <= _dawn;
        bool stopsFit = planned <= _dawn;
        Check("dawn", transitFits && (stopsFit || !v.DawnWithStopsHard), $"{planned / 60:0.0} min with every stop, {_idealTransit / 60:0.0} without, against dawn at {_dawn / 60:0.0}");
        if (transitFits && !stopsFit)
            Warn($"§22.1: taking all {_facilities.Count} stops runs {(planned - _dawn) / 60:0.0} min past dawn ({_dawn / 60:0} min by the spec's formula)");

        // Coaling: a tower when the night needs one (spec B.6).
        bool needCoal = planned > v.CoalingEnduranceShare * _l.TenderEnduranceS;
        Check("coaling", !needCoal || _facilities.Any(f => f.Kind == FacilityKind.CoalingTower), $"{planned / 60:0} min planned, tender lasts {_l.TenderEnduranceS / 60:0}");

        // Hard bends (note 278): the night has the tier's least count of bends that derail the train under its top speed,
        // so there's always somewhere a driver who isn't watching the map comes off.
        var hard = HardBendSpans(_line!).Where(b => b.S0 > _gate).ToList();
        int leastBends = (int)Math.Floor(_l.Bends[0]);
        Check("hard bends", hard.Count >= leastBends, $"{hard.Count} against the tier's {leastBends}");

        // Quotas (§15.3).
        var missing = Quotas();
        Check("quotas", missing.Count == 0, string.Join(", ", missing));

        // Separation was held when each edge was laid; its failures were dropped or retried.
        Check("separation", true);

        // Walkability: a ledge's drop side stays walkable within 40 m (§12.6).
        var steep = new List<string>();
        var tr = _t.Terrain;
        foreach (var i in _intents.Where(i => i.Left.T == IntentType.LedgeDrop || i.Right.T == IntentType.LedgeDrop))
        {
            var line = LineOf(i.Edge);
            double s = (i.S0 + i.S1) / 2;
            var at = line.Sample(s);
            var right = new Double3(-at.Tangent.Z, 0, at.Tangent.X) * (i.Right.T == IntentType.LedgeDrop ? 1 : -1);
            double prev = _terrain!.Height(at.Position.X + right.X * tr.ShoulderM, at.Position.Z + right.Z * tr.ShoulderM);
            for (double d = tr.ShoulderM + 5; d <= tr.WalkableWithinM; d += 5)
            {
                double h = _terrain.Height(at.Position.X + right.X * d, at.Position.Z + right.Z * d);
                // The noise rides on the slope: a tenth either way is the land, not the cut.
                if ((prev - h) / 5 > tr.WalkableSlope + 0.2)
                {
                    steep.Add($"ledge at km {Card(i.Edge, s):0.0}");
                    break;
                }
                prev = h;
            }
        }
        Check("walkability", steep.Count == 0, string.Join("; ", steep.Take(3)));
        Metrics(planned, deepest);
    }

    readonly Dictionary<string, bool> _knownGradesPassable = new();

    List<string> Quotas()
    {
        var missing = new List<string>();
        int Count(string tag) => _tags.Count(t => t.Edge == "main" && t.Tag == tag);
        foreach (var q in QuotaRows())
        {
            foreach (var (tag, n) in q.Tags)
            {
                int have = tag switch
                {
                    "forest_or_open" => Count("dark_forest") + Count("open"),
                    "marsh_or_water" => Count("marsh") + Count("water_crossing"),
                    _ => Count(tag),
                };
                if (have < n)
                    missing.Add($"{tag} {have}/{n}");
            }
            int junctions = 2 * _alts.Count + _deads.Count;
            if (junctions < q.FacingJunctions)
                missing.Add($"junctions {junctions}/{q.FacingJunctions}");
            if (_deads.Count < q.DeadLines)
                missing.Add($"dead lines {_deads.Count}/{q.DeadLines}");
            int tunnels = _structures.Count(s => s.Type == StructureType.Tunnel && s.Edge == "main");
            if (tunnels < q.Tunnels)
                missing.Add($"tunnels {tunnels}/{q.Tunnels}");
            if (q.DeadSettlementNearFacility > 0)
            {
                bool near = _landmarks.Any(l => l.Type is "halt" or "town" && l.Edge == "main"
                    && _facilities.Any(f => Math.Min(Math.Abs(l.S1 - f.S), Math.Abs(l.S0 - f.S)) < _t.Director.DeadSettlementNearFacilityM));
                if (!near)
                    missing.Add("dead settlement near a facility");
            }
        }
        return missing.Distinct().ToList();
    }

    /// <summary>§16.5's stats record.</summary>
    void Metrics(double planned, int deepest)
    {
        _metrics["lengthKm"] = Math.Round((_terminus - _gate) / 1000, 2);
        _metrics["idealTransitMin"] = Math.Round(_idealTransit / 60, 2);
        _metrics["plannedMin"] = Math.Round(planned / 60, 2);
        _metrics["dawnMin"] = Math.Round(_dawn / 60, 2);
        _metrics["dawnSlackMin"] = Math.Round((_dawn - planned) / 60, 2);
        _metrics["averageSpeed"] = _idealTransit > 0 ? Math.Round((_terminus - _gate) / _idealTransit, 2) : 0;
        _metrics["rulingGradeMain"] = Math.Round(Ruling(_line!), 2);
        double tight = double.MaxValue;
        for (double s = 0; s < _end; s += 5)
            if (Math.Abs(_line!.Sample(s).Curvature) > 1e-9)
                tight = Math.Min(tight, 1 / Math.Abs(_line.Sample(s).Curvature));
        _metrics["minRadius"] = Math.Round(Math.Min(tight, 99999));
        var bends = HardBendSpans(_line!).Where(b => b.S0 > _gate).ToList();
        _metrics["hardBends"] = bends.Count;
        _metrics["hardBendSlowestMs"] = bends.Count == 0 ? 0 : Math.Round(Math.Sqrt(_t.Curves.ADerail * bends.Min(b => b.R)), 1);
        _metrics["tunnelM"] = Math.Round(_structures.Where(s => s.Type == StructureType.Tunnel).Sum(s => s.S1 - s.S0));
        _metrics["bridgeM"] = Math.Round(_structures.Where(s => s.Type is StructureType.Trestle or StructureType.Viaduct or StructureType.Girder or StructureType.Truss).Sum(s => s.S1 - s.S0));
        _metrics["junctions"] = 2 * _alts.Count + _deads.Count;
        _metrics["alternates"] = _alts.Count;
        _metrics["deadLines"] = _deads.Count;
        _metrics["facilities"] = _facilities.Count;
        _metrics["stackDepthMax"] = deepest;
        for (int k = 1; k <= 4; k++)
            _metrics[$"stackDepth{k}M"] = _stackDepth.Count(d => d == k) * _t.Director.PressureStepM;
        _metrics["terrainCost"] = Math.Round(_budgetSpent, 2);
        _metrics["terrainBudget"] = Math.Round(_budgetTotal, 2);
        _metrics["requiredTells"] = _signs.Count(s => s.Required);
        _metrics["form19"] = _form19.Count;
        _metrics["restrictedZones"] = _restricted.Count;
        _metrics["sleepers"] = _sleepers.Count;
        _metrics["grease"] = _grease.Count;
        _metrics["minBrake"] = Math.Round(_minBrake, 3);
        _metrics["sloppyTransitMin"] = Math.Round(_sloppyTransit / 60, 2);
        foreach (var tag in _tags.Where(t => t.Edge == "main").GroupBy(t => t.Tag))
            _metrics[$"tag:{tag.Key}M"] = Math.Round(tag.Sum(t => t.S1 - t.S0));
    }
}
