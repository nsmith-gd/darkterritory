using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.LineGen;

/// <summary>How a script item bends (plan §8.1).</summary>
enum HShape : byte
{
    /// <summary>Follows the guide heading, with low wander.</summary>
    Free,
    /// <summary>Tangent track: junctions, holding track, bridges, platforms.</summary>
    Straight,
    /// <summary>Only gentle curves (threshold, home straight, causeway): R at least the item's MinRadius.</summary>
    Gentle,
    /// <summary>One curve of a given radius and deflection, turned toward the guide.</summary>
    Turn,
    /// <summary>Reverse curves (the Blind Throat): out and back by the same deflection.</summary>
    Reverse,
    /// <summary>Straight, then one limited curve at the far end (the Drop).</summary>
    EndCurve,
}

/// <summary>A stretch of an edge's script (plan §7): a set piece, a connector, or one of the fixed zones.</summary>
sealed class Item
{
    public string Id = "";
    /// <summary>A set piece's id, or a zone: fortress, threshold, approach, holding, stand, departure, pad, home, arrival,
    /// recovery, connector, turnout, closure.</summary>
    public string Type = "";
    public string Kind = "";
    public double S0, S1;
    public PieceDef? Def;
    public Dictionary<string, double> Params = new();
    public List<string> Tags = new();
    public double Cost;
    public int Depth = 1;
    public string? Signature;
    public HShape H = HShape.Free;
    public double Radius, Deflection, MinRadius, Wander;
    /// <summary>Grades as (from, to, grade) over fractions of the item; null: the regional drift, within <see cref="DriftCap"/>.</summary>
    public List<(double F0, double F1, double G)>? Grades;
    public double DriftCap = double.PositiveInfinity;
    /// <summary>Must stay level (junction pads, holding track, yards): vertical curves keep out of it.</summary>
    public bool HardLevel;
    /// <summary>Stacked pieces sharing this range (the "b" of a signature's "a+b").</summary>
    public List<Item> Overlays = new();
    /// <summary>Realised horizontal primitives, and the relief intents it asks for.</summary>
    public List<HPrim> Prims = new();
    public double Length => S1 - S0;
    public bool IsPiece => Def is not null && Def.Kind is not ("straight" or "sweep");
    public IEnumerable<Item> All() => Overlays.Prepend(this);
    public bool Has(string kind) => All().Any(i => i.Kind == kind);
    public override string ToString() => $"{Type} {S0:0}-{S1:0}";
}

/// <summary>An edge being built: its script, then its geometry.</summary>
sealed class EdgeDraft
{
    public string Id = "";
    public EdgeRole Role;
    public int Branch = -1;
    public double Toe;
    public int Side;
    public double? Rejoin;
    public List<Item> Items = new();
    public List<TrackSegment> Segments = new();
    public List<HPrim> Prims = new();
    public List<(double Length, double G0, double G1)> Grades = new();
    /// <summary>Rail height at the start (a branch starts on the main line's).</summary>
    public double Z0;
    public double Length => Items.Count == 0 ? 0 : Items[^1].S1;
}

sealed class FacilitySlot
{
    public int Index;
    public double S;
    public FacilityKind Kind;
    public FacilityDef Def = null!;
    public int Side;
    public double SpurLength, SpurGrade, Holding, LullStart, LullEnd, PadRadius;
    public string Name = "";
    public string? SpurEdge;
    public bool OnSpur => Def.Spur;
}

sealed class AltWindow
{
    public int Index;
    public double T, J;
    public string TradeOffId = "";
    public TradeOff TradeOff = null!;
    public int Side;
    public double Ratio;
    public bool WashoutOnMain, WashoutOnAlt;
    public WeakLimit? Weak;
    /// <summary>The main line bows away across the window (for an alternate that's shorter than it).</summary>
    public int MainBow;
    public string Edge = "";
    public int Number;
}

sealed class DeadLinePlan
{
    public int Index;
    public double Toe;
    public int Side;
    public double Length;
    public bool WreckYard;
    public string Edge = "";
    public int Number;
}

/// <summary>
/// Builds one attempt at a Line Plan (plan §4): each stage in order on its own random stream. The stages are split
/// across files by the plan's numbering (Graph, Script, Align, Profile, Terrain, Authority, Stations, Dressing,
/// Director). What fails a check comes back as a reason; the generator retries with the next attempt's seed.
/// </summary>
sealed partial class LineBuilder
{
    readonly LineGenContent _c;
    readonly TiersFile _t;
    readonly RunParameters _p;
    readonly Limits _l;
    readonly ulong _seed;
    readonly List<string> _warnings = new();

    // Stage 1: the main line's layout, in main-line distance.
    double _gate, _terminus, _end, _departureRoad, _innerGate;
    readonly List<FacilitySlot> _facilities = new();
    readonly List<AltWindow> _alts = new();
    readonly List<DeadLinePlan> _deads = new();
    readonly Dictionary<string, EdgeDraft> _edges = new();
    readonly List<(string Biome, double S0, double S1)> _biomes = new();
    string _region = "";

    public LineBuilder(LineGenContent content, RunParameters p, ulong seed)
    {
        _c = content;
        _t = content.Config.Tiers;
        _p = p;
        _l = new Limits(content, p);
        _seed = seed;
    }

    public Limits Limits => _l;
    public IReadOnlyList<string> Warnings => _warnings;
    Pcg32 Rng(string stage, string edge = "", long index = 0) => Streams.Rng(_seed, stage, edge, index);
    EdgeDraft Main => _edges["main"];

    void Warn(string text)
    {
        if (!_warnings.Contains(text))
            _warnings.Add(text);
    }

    /// <summary>Stage 1 (plan §6): the main line's length, its reserved zones, facility slots, alternates, washouts,
    /// weak bridges and dead lines, and the junction count.</summary>
    void BuildGraph()
    {
        var f = _t.Fortress;
        _departureRoad = _l.LongestConsist + f.DepartureRoadExtraM;
        _innerGate = _departureRoad + f.ThroatM;
        _gate = _innerGate + f.InnerGateBeforeM;
        double length = Math.Round(_l.LengthKm * 100) * 10;
        _terminus = _gate + length;
        _end = _terminus + _t.Terminus.ArrivalYardM;
        _edges["main"] = new EdgeDraft { Id = "main", Role = EdgeRole.Main };
        LayBiomes();
        PlaceFacilities();
        PlaceAlternates();
        PlaceWashoutsAndWeakBridges();
        PlaceDeadLines();
        CheckJunctionCount();
    }

    /// <summary>§13.1: biome regions of 2–6 km along the main line from the tier's weights; the route's region is its commonest.</summary>
    void LayBiomes()
    {
        var rng = Rng("biomes", "main");
        var weights = _c.Config.Biomes.TierWeights[Key(_p.Tier)].Select(kv => (kv.Key, kv.Value)).ToList();
        // The route's own region leans the whole line its way (a mountain route is mostly mountain).
        _region = rng.Weighted(weights)!;
        var leaned = weights.Select(w => (w.Key, w.Key == _region ? w.Value * 2.5 : w.Value)).ToList();
        double s = 0;
        string? last = null;
        while (s < _end)
        {
            double len = rng.Range(_c.Config.Biomes.RegionKm) * 1000;
            string pick = rng.Weighted(leaned.Where(w => w.Key != last || leaned.Count == 1).ToList())!;
            _biomes.Add((pick, s, Math.Min(_end, s + len)));
            last = pick;
            s += len;
        }
    }

    public static string Key(RouteTier tier) => char.ToLowerInvariant(tier.ToString()[0]) + tier.ToString()[1..];

    string BiomeAt(double s) => _biomes.FirstOrDefault(b => s >= b.S0 && s < b.S1).Biome ?? _biomes[^1].Biome;

    double Km(double s) => (s - _gate) / 1000;

    /// <summary>§6.2 step 3 and §11.1: facility slots, their types, and the land each reserves.</summary>
    void PlaceFacilities()
    {
        var rng = Rng("facilities", "main");
        var r = _c.Config.Facilities.Slots;
        double length = _terminus - _gate;
        double first = _gate + Math.Max(r.FirstMinM, r.FirstMinShare * length);
        double last = _terminus - r.LastBeforeEndM;
        int wanted = rng.Round(_l.Facilities), n = wanted;
        while (n > 1 && (last - first) / (n - 1) < r.SpacingM)
            n--;
        if (n < wanted)
            Warn($"only {n} of {wanted} facility slots fit a {length / 1000:0.0} km line");
        double span = n > 1 ? (last - first) / (n - 1) : 0;
        double jitter = n > 1 ? Math.Max(0, (span - r.SpacingM) / 2) : Math.Max(0, (last - first) / 2);
        for (int i = 0; i < n; i++)
        {
            double s = n > 1 ? first + span * i : (first + last) / 2;
            s += rng.Range(-jitter, jitter) * (i == 0 || i == n - 1 ? 0.5 : 1);
            s = Math.Clamp(Math.Round(s / 10) * 10, first, last);
            var slot = new FacilitySlot { Index = i, S = s, Side = rng.Chance(0.5) ? 1 : -1 };
            slot.Holding = _l.ConsistLength + r.HoldingExtraM;
            slot.SpurLength = Math.Round(rng.Range(r.SpurLengthM));
            slot.SpurGrade = Math.Round(rng.Range(r.SpurGrade), 2);
            _facilities.Add(slot);
        }
        ChooseFacilityTypes(ref rng);
        foreach (var slot in _facilities)
        {
            slot.LullStart = slot.S - _t.Budget.LullBeforeM;
            // The coaling tower is a stop on the main line (a zone either side of the chute); a spur runs out alongside.
            double after = slot.OnSpur ? Math.Max(_t.Budget.LullAfterM, slot.SpurLength + 100) : _t.Budget.LullAfterM + _c.Route.PoiZoneHalfLength;
            slot.LullEnd = slot.S + after;
            slot.PadRadius = Math.Round(rng.Range(_c.Config.Facilities.Scale.PadRadiusM));
        }
    }

    /// <summary>§11.1 type selection: contracts first, a coaling tower if the night needs one, then what suits the land.</summary>
    void ChooseFacilityTypes(ref Pcg32 rng)
    {
        var types = _c.Config.Facilities.Types.Values.Where(t => t.FromTier <= _p.Tier).ToList();
        var chosen = new FacilityKind?[_facilities.Count];
        int next = 0;
        foreach (var kind in _p.Contracts.Distinct())
            if (next < chosen.Length && types.Any(t => t.Kind == kind))
                chosen[next++] = kind;
        // Spec B.6 / plan §11.1: coal when the expected run approaches the tender's endurance. The validator checks the
        // real figure; this is its estimate from line speed and the stops.
        // A heavy train averages well under line speed (it's slow to accelerate out of every stop and limit).
        double average = _l.LineSpeed * Math.Clamp(0.45 + _l.Accel, 0.5, 0.8);
        double expected = (_terminus - _gate) / average + _facilities.Count * _t.Validation.DawnMinutesPerFacility * 60;
        if (expected > _t.Validation.CoalingEnduranceShare * _l.TenderEnduranceS && !chosen.Contains(FacilityKind.CoalingTower))
        {
            // The middle slot, so the tender's halves either side of it are both short.
            int mid = _facilities.Count / 2;
            if (chosen[mid] is not null && next < chosen.Length)
                chosen[next] = chosen[mid];
            chosen[mid] = FacilityKind.CoalingTower;
        }
        for (int i = 0; i < chosen.Length; i++)
        {
            if (chosen[i] is not null)
                continue;
            string biome = BiomeAt(_facilities[i].S);
            // Any kind the tier has may be drawn, the coaling tower too (§11.1's table: "Any" land), though one is enough.
            var options = types.Where(t => t.Kind != FacilityKind.CoalingTower || !chosen.Contains(FacilityKind.CoalingTower))
                .Select(t => (t.Kind, (t.Biomes.TryGetValue(biome, out var w) ? w : t.Biomes.GetValueOrDefault("*", 0))
                    // Variety: a kind already on the line is less likely again.
                    / (1 + 2 * chosen.Count(c => c == t.Kind)))).ToList();
            chosen[i] = rng.Weighted(options);
        }
        for (int i = 0; i < chosen.Length; i++)
        {
            _facilities[i].Kind = chosen[i] ?? FacilityKind.CoalingTower;
            _facilities[i].Def = types.First(t => t.Kind == _facilities[i].Kind);
        }
    }

    /// <summary>What's reserved on the main line: the yard, grace, home straight and spike, facility lulls, junction pads.</summary>
    List<(double S0, double S1)> Blocked(bool withSpike = true)
    {
        var list = new List<(double, double)> { (0, _gate + _t.Budget.GraceM), (_terminus - _t.Budget.HomeStraightM, _end) };
        if (withSpike)
            list.Add((_terminus - _t.Budget.SpikeWindowM[0], _terminus - _t.Budget.SpikeWindowM[1]));
        foreach (var f in _facilities)
            list.Add((f.LullStart, f.LullEnd));
        double pad = _t.Junctions.PadM, spacing = _t.Junctions.MinSpacingM;
        foreach (var a in _alts)
            list.Add((a.T - Math.Max(pad, spacing), a.J + Math.Max(pad, spacing)));
        foreach (var d in _deads)
            list.Add((d.Toe - Math.Max(pad, spacing), d.Toe + Math.Max(pad, spacing)));
        return list;
    }

    static bool Clear(List<(double S0, double S1)> blocked, double a, double b) => blocked.All(x => b <= x.S0 || a >= x.S1);

    /// <summary>§6.2 step 4: windows of main line an alternate bypasses, each with its trade-off.</summary>
    void PlaceAlternates()
    {
        var rng = Rng("alternates", "main");
        int want = rng.Count(_l.Alternates[0], _l.Alternates[1]);
        var cfg = _t.Alternates;
        for (int tries = 0; _alts.Count < want && tries < 300; tries++)
        {
            var blocked = Blocked();
            double w = Math.Round(rng.Range(cfg.WindowKm[0], cfg.WindowKm[1]) * 1000 * (tries > 150 ? 0.7 : 1));
            double t = Math.Round(rng.Range(_gate, _terminus - w) / 10) * 10;
            double pad = _t.Junctions.PadM;
            if (!Clear(blocked, t - pad, t + w + pad))
                continue;
            var options = cfg.TradeOffs.Where(kv => kv.Value.MinD <= _p.D)
                // A line with two alternates offers two different choices.
                .Select(kv => (kv.Key, kv.Value.Weight / (1 + 3 * _alts.Count(a => a.TradeOffId == kv.Key)))).ToList();
            string id = rng.Weighted(options)!;
            var a = new AltWindow
            {
                Index = _alts.Count,
                T = t,
                J = t + w,
                TradeOffId = id,
                TradeOff = cfg.TradeOffs[id],
                Side = rng.Chance(0.5) ? 1 : -1,
            };
            a.Ratio = rng.Range(a.TradeOff.LengthRatio);
            // An alternate shorter than the main line needs the main line to bow away from it across the window.
            a.MainBow = a.Ratio < 1 ? -a.Side : 0;
            _alts.Add(a);
        }
        if (_alts.Count < want)
            Warn($"placed {_alts.Count} of {want} alternates: no more room between the facilities");
        _alts.Sort((x, y) => x.T.CompareTo(y.T));
        for (int i = 0; i < _alts.Count; i++)
        {
            _alts[i].Index = i;
            _alts[i].Edge = $"alt{i + 1}";
        }
    }

    /// <summary>
    /// §6.2 steps 5–6: washouts on one side of a fork, never both; weak bridges on alternates (a limit the departing
    /// train may not meet) or on the main line (one it does). At least one side of every fork stays passable.
    /// </summary>
    void PlaceWashoutsAndWeakBridges()
    {
        var rng = Rng("hazards", "main");
        int washouts = rng.Count(_l.Washouts[0], _l.Washouts[1]);
        if (washouts > _alts.Count)
        {
            Warn($"{washouts} washouts wanted, but only {_alts.Count} forks to put them on");
            washouts = _alts.Count;
        }
        foreach (var a in _alts.OrderBy(_ => rng.NextUInt()).Take(washouts))
        {
            // Closing the main line forces the alternate (GDD §22 "forces the bad junction").
            if (rng.Chance(0.5))
                a.WashoutOnMain = true;
            else
                a.WashoutOnAlt = true;
        }
        int weak = rng.Count(_l.WeakBridges[0], _l.WeakBridges[1]);
        var h = _t.Hazards;
        for (int i = 0; i < weak; i++)
        {
            // On an alternate whose main side is open, the limit may be below the train that left; else the main line
            // gets one the train satisfies.
            var alt = _alts.Where(a => a.Weak is null && !a.WashoutOnMain && !a.WashoutOnAlt).OrderBy(_ => rng.NextUInt()).FirstOrDefault();
            double speed = Math.Round(rng.Range(h.WeakBridgeSpeed));
            if (alt is not null)
                alt.Weak = new WeakLimit(Math.Max(1, _p.Cars - rng.RangeInclusive(h.WeakBridgeCarsBelowPlan[0], h.WeakBridgeCarsBelowPlan[1])), speed);
            else
                _mainWeakBridges.Add(new WeakLimit(_p.Cars + rng.RangeInclusive(0, 3), speed));
        }
    }

    readonly List<WeakLimit> _mainWeakBridges = new();

    /// <summary>§6.2 step 7: dead lines off facing junctions, curving away to a buffer stop.</summary>
    void PlaceDeadLines()
    {
        var rng = Rng("deadlines", "main");
        int want = rng.Count(_l.DeadLines[0], _l.DeadLines[1]);
        for (int i = 0; i < want; i++)
            TryAddDeadLine(ref rng);
        if (_deads.Count < want)
            Warn($"placed {_deads.Count} of {want} dead lines");
    }

    bool TryAddDeadLine(ref Pcg32 rng)
    {
        var d = _t.DeadLines;
        for (int tries = 0; tries < 200; tries++)
        {
            double toe = Math.Round(rng.Range(_gate + _t.Budget.GraceM, _terminus - _t.Budget.HomeStraightM) / 10) * 10;
            if (!Clear(Blocked(withSpike: false), toe - _t.Junctions.PadM, toe + _t.Junctions.PadM))
                continue;
            _deads.Add(new DeadLinePlan
            {
                Toe = toe,
                Side = rng.Chance(0.5) ? 1 : -1,
                Length = Math.Round(rng.Range(d.LengthKm) * 1000),
                WreckYard = rng.Chance(d.WreckYardChance),
            });
            _deads.Sort((x, y) => x.Toe.CompareTo(y.Toe));
            for (int i = 0; i < _deads.Count; i++)
            {
                _deads[i].Index = i;
                _deads[i].Edge = $"dead{i + 1}";
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// §6.2 step 8: the main line's junctions (both ends of each alternate, and each dead line) meet the tier's count,
    /// topped up with dead lines. The Switchman needs three (GDD B.7).
    /// </summary>
    void CheckJunctionCount()
    {
        var rng = Rng("junctions", "main");
        int target = rng.Count(_l.Junctions[0], _l.Junctions[1]);
        int Have() => 2 * _alts.Count + _deads.Count;
        while (Have() < target)
        {
            if (!TryAddDeadLine(ref rng))
            {
                Warn($"only {Have()} of {target} junctions fit");
                break;
            }
            if (_deads.Count > Math.Round(_l.DeadLines[1]))
                Warn($"junction count {target} needs {_deads.Count} dead lines, past the tier's {_l.DeadLines[1]:0.#}");
        }
        NumberJunctions();
    }

    /// <summary>Numbers the facing junctions along the line: J1, J2, ...; an alternate's trailing end shares its number.</summary>
    void NumberJunctions()
    {
        int n = 1;
        foreach (var (s, set) in _alts.Select(a => (a.T, (Action<int>)(k => a.Number = k))).Concat(_deads.Select(d => (d.Toe, (Action<int>)(k => d.Number = k)))).OrderBy(x => x.Item1))
            set(n++);
    }
}
