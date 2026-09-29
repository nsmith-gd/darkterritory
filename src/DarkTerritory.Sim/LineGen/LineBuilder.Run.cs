using Ballast;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.LineGen;

/// <summary>The stages in order (plan §4), and the Line Plan they come to.</summary>
sealed partial class LineBuilder
{
    // What the later stages lay, frozen into the plan.
    readonly List<PlanStructure> _structures = new();
    readonly List<PlanIntent> _intents = new();
    readonly List<PlanWater> _water = new();
    readonly List<PlanLimit> _limits = new();
    readonly List<PlanRestricted> _restricted = new();
    readonly List<PlanDemand> _demands = new();
    readonly List<PlanSign> _signs = new();
    readonly List<PlanPoi> _pois = new();
    readonly List<PlanLandmark> _landmarks = new();
    readonly List<PlanPad> _pads = new();
    readonly List<PlanExposure> _exposure = new();
    readonly List<PlanTag> _tags = new();
    readonly List<PlanZone> _sleeperZones = new(), _greaseZones = new(), _sleepers = new(), _grease = new();
    readonly List<PlanMarker> _markers = new();
    readonly List<CardLine> _timetable = new(), _form19 = new();
    readonly List<KnownGrade> _knownGrades = new();
    readonly List<PlanCheck> _checks = new();
    readonly SortedDictionary<string, double> _metrics = new();
    readonly List<PlanNode> _nodes = new();
    readonly List<PlanEdge> _graphEdges = new();
    PlanWeather? _weather;
    PlanFortress? _fortress;
    PlanTerminus? _terminusPlan;
    double[] _pressure = [];
    double _dawn, _idealTransit, _sloppyTransit, _minBrake = 1;
    string _routeName = "";

    public (LinePlan? Plan, string? Why) Run()
    {
        BuildGraph();
        ScriptMain();
        bool aligned = false;
        for (int k = 0; k < _t.Alignment.PieceRetries && !aligned; k++)
            aligned = AlignMain(k);
        if (!aligned)
            return (null, Separated(Main, null) ?? "the main line comes back on itself");
        _traces["main"] = (new Pose(0, 0, 0), Trace(new Pose(0, 0, 0), Main.Prims));
        foreach (var slot in _facilities.Where(f => f.OnSpur))
            BuildSpur(slot);
        foreach (var d in _deads.ToList())
            if (!Retry(k => BuildDeadLine(d, k), d.Edge))
            {
                _deads.Remove(d);
                _edges.Remove(d.Edge);
                _traces.Remove(d.Edge);
            }
        foreach (var w in _alts.ToList())
            if (!Retry(k => BuildAlternate(w, k), w.Edge))
            {
                _alts.Remove(w);
                _edges.Remove(w.Edge);
                _traces.Remove(w.Edge);
            }
        Reindex();
        if (ProfileAndBuild() is { } why)
            return (null, why);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        void Lap(string stage)
        {
            _timings[stage] = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
        }
        Lap("geometry");
        LayStructuresAndIntents();
        LayStations();
        RollWeather();
        LayTags();
        LayExposure();
        Lap("stations+tags");
        // §22.1: dawn by the spec's formula (route / 11 m/s + 18%); the validator reports the slack against ideal transit.
        var cf = _t.Conflicts;
        _dawn = (_terminus - _gate) / cf.DawnAverageSpeed * (1 + cf.DawnSlack);
        LayAuthority();
        Lap("authority");
        LaySignage();
        LayDirector();
        LayRouteCard();
        Lap("signage+director");
        Validate();
        Lap("validate");
        LayDirector();
        LayRouteCard();
        return (Freeze(), null);
    }

    /// <summary>The branches in order along the main line: a rake's path onto one is its index (RailLine).</summary>
    void Reindex()
    {
        int index = 0;
        foreach (var e in _edges.Values.Where(e => e.Role != EdgeRole.Main).OrderBy(e => e.Toe).ThenBy(e => e.Id, StringComparer.Ordinal))
            e.Branch = index++;
    }

    /// <summary>Takes an alternate out of the line altogether (it couldn't be laid), keeping the rest.</summary>
    void DropAlternate(EdgeDraft e, string why)
    {
        Warn($"dropped {e.Id}: {why}");
        _alts.RemoveAll(a => a.Edge == e.Id);
        _edges.Remove(e.Id);
        _traces.Remove(e.Id);
        Reindex();
    }

    /// <summary>Smallest scope first (§16.4): an edge that won't lay is tried again on its own before the run is.</summary>
    bool Retry(Func<int, string?> build, string edge)
    {
        string? why = null;
        for (int k = 0; k < _t.Alignment.PieceRetries * 2; k++)
        {
            why = build(k);
            if (why is null)
                return true;
            _traces.Remove(edge);
        }
        Warn($"dropped {edge}: {why}");
        return false;
    }

    LinePlan Freeze()
    {
        var line = _line!;
        var edges = _edges.Values.OrderBy(e => e.Role == EdgeRole.Main ? -1 : e.Branch).ToList();
        return new LinePlan
        {
            Version = _t.Version,
            Seed = $"0x{_seed:X16}",
            Route = new PlanRoute(_p.RouteId, _p.Tier, Math.Round(_p.Severity, 4), Math.Round(_p.D, 4), _region, _routeName),
            Consist = new PlanConsist(_p.Cars, Math.Round(_l.MassTonnes, 1), Math.Round(_l.ConsistLength, 1), Math.Round(_l.ClimbMax, 3), Math.Round(_l.Brake, 4), Math.Round(_l.MainGrade, 3)),
            Weather = _weather ?? new PlanWeather(_l.Fog[0], _l.Fog[1], false, 0, 0, 0, 0.016),
            GateM = _gate,
            TerminusM = _terminus,
            LengthM = Math.Round(line.Length, 3),
            Graph = new PlanGraph(_nodes, _graphEdges),
            Alignment = [.. edges.Select(e => new PlanAlignment(e.Id, e.Role, e.Branch, e.Toe, e.Side, e.Rejoin, e.Segments))],
            Pieces = [.. PlanPieces()],
            Intents = _intents,
            Structures = _structures,
            Water = _water,
            Authority = new PlanAuthority(_l.LineSpeed, _t.Fortress.YardSpeed, _limits, _restricted, _demands, Math.Round(_l.TellMargin, 3), _t.Reaction.TReactS, Math.Round(_l.Brake, 4)),
            Signage = _signs,
            Pois = _pois,
            Landmarks = _landmarks,
            Pads = _pads,
            Biomes = [.. _biomes.Select(b => new PlanBiome("main", Math.Round(b.S0, 1), Math.Round(Math.Min(b.S1, line.Length), 1), b.Biome))],
            Exposure = _exposure,
            Director = new PlanDirector(_tags, _sleeperZones, _greaseZones, new PlanPressure(_t.Director.PressureStepM, _pressure), _sleepers, _grease),
            RouteCard = new PlanRouteCard(_routeName, _timetable, _form19, _knownGrades, Math.Round(_dawn), _l.LineSpeed),
            Markers = _markers,
            Fortress = _fortress ?? new PlanFortress("", "", _departureRoad, _innerGate, _gate, 0, []),
            Terminus = _terminusPlan ?? new PlanTerminus("", false, true, _terminus, 0, 0, []),
            Validation = new PlanValidation
            {
                IdealTransitS = Math.Round(_idealTransit, 1),
                SloppyTransitS = Math.Round(_sloppyTransit, 1),
                DawnSlackS = Math.Round(_dawn - _idealTransit, 1),
                MinBrakeEfficiency = Math.Round(_minBrake, 4),
                Checks = _checks,
                Warnings = _warnings,
                Metrics = _metrics,
            },
        };
    }

    IEnumerable<PlanPiece> PlanPieces()
    {
        foreach (var e in _edges.Values.OrderBy(e => e.Role == EdgeRole.Main ? -1 : e.Branch))
            foreach (var item in e.Items)
                foreach (var i in item.All())
                    if (i.IsPiece)
                        yield return new PlanPiece(i.Id, i.Type, i.Kind, e.Id, Math.Round(i.S0, 2), Math.Round(i.S1, 2),
                            new SortedDictionary<string, double>(i.Params.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 4))), [.. i.Tags.Distinct()],
                            Math.Round(i.Cost, 3), i.Depth, i.Signature);
        foreach (var r in _regionItems)
            yield return new PlanPiece(r.Id, r.Type, r.Kind, "main", Math.Round(r.S0, 2), Math.Round(r.S1, 2), new SortedDictionary<string, double>(), [.. r.Tags], r.Cost);
    }

    /// <summary>For `dt linegen debug`: each main-line item with its heading at the end, and each branch's planned end against the built one.</summary>
    readonly Dictionary<string, double> _timings = new();

    public IEnumerable<string> Debug()
    {
        foreach (var (k, v) in _timings)
            yield return $"time {k}: {v:0} ms";
        if (_ideal is { } ideal)
        {
            yield return $"ideal: {ideal.Survived} {ideal.Failure} transit {ideal.TransitSeconds / 60:0.0} min, brake {ideal.MinBrake:0.00}, over {ideal.WorstOverspeed:0.0} {ideal.WorstOverspeedAt}, trace {ideal.Trace.Count}";
            for (int i = 0; i < ideal.Trace.Count; i += Math.Max(1, ideal.Trace.Count / 40))
                yield return $"  t {i / 2.0:0}s  d {ideal.Trace[i].D}  v {ideal.Trace[i].V}";
        }
        foreach (var (e, r) in _alternateDrives)
            yield return $"{e}: {r.Survived} {r.Failure} transit {r.TransitSeconds / 60:0.0} min, trace {r.Trace.Count}";
        foreach (var c in _checks.Where(c => !c.Pass))
            yield return $"FAIL {c.Name}: {c.Detail}";
        var pose = new Pose(0, 0, 0);
        foreach (var i in Main.Items)
        {
            pose = Geometry.Advance(pose, i.Prims);
            var at = _line?.Sample(i.S1);
            string built = at is { } a ? $" built {a.Position.X:0.0},{a.Position.Z:0.0} h {Math.Atan2(-a.Tangent.X, -a.Tangent.Z) * 180 / Math.PI:0.0}" : "";
            yield return $"{i.Type,-14} {i.H,-8} {i.S0,8:0}-{i.S1,8:0}  heading {pose.Heading * 180 / Math.PI,7:0.0}  guide {Guide(i.S1) * 180 / Math.PI,7:0.0}  prims {i.Prims.Count} len {i.Prims.Sum(p => p.Length):0.0} defl {i.Prims.Sum(p => p.Deflection) * 180 / Math.PI:0.0} at {pose.X:0.0},{pose.Z:0.0}{built}";
        }
        if (_line is null)
            yield break;
        foreach (var e in Branches())
        {
            var b = _line.Branches[e.Branch];
            var planned = Geometry.Advance(PoseOnMain(e.Toe), e.Prims);
            var built = b.Local.Sample(b.Local.Length).Position;
            yield return $"{e.Id}: planned end {planned.X:0.0},{planned.Z:0.0} built {built.X:0.0},{built.Z:0.0} len {b.Local.Length:0} prims {e.Prims.Sum(p => p.Length):0}";
        }
    }

    /// <summary>The rail model built from this attempt (after stage 3).</summary>
    internal RailLine Line => _line!;
}
