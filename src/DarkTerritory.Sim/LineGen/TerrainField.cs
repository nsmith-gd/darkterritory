using Ballast;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// The land around a generated line (plan §12): a pure, deterministic height function of the Line Plan, shaped around
/// the track rather than routed through. Every machine evaluates it identically: integer-hash noise, no transcendental
/// functions, track positions quantised to the millimetre (§17.3). Tiles of it are the renderer's terrain and what host
/// and clients compare checksums of.
/// <para>
/// Near the track (the formation and shoulder, <c>shoulderM</c>) the ground is at rail height, as it has always been, so
/// every lever, stand and step is where it was. Past that it rises or falls toward what the relief intent asks,
/// limited by the intent's slope. Inside a tunnel the land is the hill over it; on a bridge it's the ravine below.
/// </para>
/// </summary>
public sealed class TerrainField
{
    readonly LinePlan _plan;
    readonly TerrainRules _r;
    readonly RailLine _main;
    readonly EdgeInfo[] _edges;
    readonly Dictionary<long, List<(int Edge, double S, double X, double Z)>> _cells = new();
    readonly ulong _seed;
    readonly IReadOnlyDictionary<string, double> _relief;
    readonly Dictionary<string, Landform> _landforms;
    readonly double _flowCos, _flowSin;
    readonly PlanLake[] _lakes;
    readonly PlanShore[] _shores;
    readonly PlanRoad[] _roads;
    const double Cell = 128;

    sealed class EdgeInfo
    {
        public int Index;
        public string Id = "";
        public EdgeRole Role;
        public RailLine Line = null!;
        public PlanIntent[] Intents = [];
        public (double S0, double S1, StructureType Type, double H)[] Structures = [];
        public double Priority;
    }

    public TerrainField(LinePlan plan, RailLine line, TerrainRules rules)
    {
        _plan = plan;
        _r = rules;
        _main = line;
        _seed = Streams.Mix(Streams.Hash(plan.Seed), "terrain");
        _relief = plan.Rules.BiomeRelief;
        _landforms = plan.Rules.BiomeLandforms.ToDictionary(kv => kv.Key, kv => Landform.From(kv.Value));
        double flow = (_r.Drumlins.FlowDeg) * Math.PI / 180;
        (_flowCos, _flowSin) = (DetCos(flow), DetCos(flow - Math.PI / 2));
        _lakes = [.. plan.Lakes];
        _shores = [.. plan.Shores];
        _roads = [.. plan.Roads];
        var list = new List<EdgeInfo>();
        foreach (var a in plan.Alignment)
        {
            var edge = new EdgeInfo
            {
                Index = list.Count,
                Id = a.Edge,
                Role = a.Role,
                Line = a.Role == EdgeRole.Main ? line : line.Branches[a.Branch].Local,
                Intents = [.. plan.Intents.Where(i => i.Edge == a.Edge).OrderBy(i => i.S0)],
                Structures = [.. plan.Structures.Where(s => s.Edge == a.Edge && s.Type is StructureType.Tunnel or StructureType.Trestle or StructureType.Viaduct or StructureType.Girder or StructureType.Truss)
                    .Select(s => (s.S0, s.S1, s.Type, s.HeightM))],
                // §12.2 step 4: role priority where corridors overlap.
                Priority = a.Role switch { EdgeRole.Main => 4, EdgeRole.Alternate => 3, EdgeRole.Spur => 2, _ => 1 },
            };
            list.Add(edge);
            for (double s = 0; s <= edge.Line.Length + CoarseM - 1e-9; s += CoarseM)
            {
                var p = edge.Line.Sample(Math.Min(s, edge.Line.Length)).Position;
                double x = Q(p.X), z = Q(p.Z);
                long key = Key(x, z);
                if (!_cells.TryGetValue(key, out var cell))
                    _cells[key] = cell = new();
                cell.Add((edge.Index, Math.Min(s, edge.Line.Length), x, z));
            }
        }
        _edges = [.. list];
    }

    public LinePlan Plan => _plan;
    public TerrainRules Rules => _r;

    static double Q(double v) => Math.Round(v * 1000) / 1000;
    static long Key(double x, double z) => ((long)Math.Floor(x / Cell) << 32) ^ ((long)Math.Floor(z / Cell) & 0xFFFFFFFF);

    /// <summary>A track near a point: which edge, how far along, how far out (positive right), and the rail's height there.</summary>
    /// <param name="Past">The point lies off the edge's end, not beside it (past its start or its end along the tangent there):
    /// the projection clamps to the end and finds it "beside" the line, so it stands no formation (note 317).</param>
    public readonly record struct Near(int Edge, double S, double Lateral, double Rail, bool Past = false);

    /// <summary>For each edge within reach of (x, z), its nearest point.</summary>
    public List<Near> Nearby(double x, double z, double reach)
    {
        // The coarse samples (every CoarseM) find each edge's nearest stretch; Newton on the edge refines it.
        Span<double> bestD = stackalloc double[_edges.Length];
        Span<double> bestS = stackalloc double[_edges.Length];
        bestD.Fill(double.MaxValue);
        long cx = (long)Math.Floor(x / Cell), cz = (long)Math.Floor(z / Cell);
        int rings = (int)Math.Ceiling(reach / Cell);
        for (long i = cx - rings; i <= cx + rings; i++)
            for (long j = cz - rings; j <= cz + rings; j++)
            {
                if (!_cells.TryGetValue((i << 32) ^ (j & 0xFFFFFFFF), out var cell))
                    continue;
                foreach (var (edge, s, px, pz) in cell)
                {
                    double d2 = (px - x) * (px - x) + (pz - z) * (pz - z);
                    if (d2 < bestD[edge] || d2 == bestD[edge] && s < bestS[edge])
                        (bestD[edge], bestS[edge]) = (d2, s);
                }
            }
        var list = new List<Near>(2);
        double limit = (reach + CoarseM) * (reach + CoarseM);
        for (int e = 0; e < _edges.Length; e++)
            if (bestD[e] <= limit)
                list.Add(Refine(_edges[e], x, z, bestS[e]));
        return list;
    }

    const double CoarseM = 20;

    /// <summary>Projects (x, z) onto an edge from a guess: Newton on the tangent, then the side and the rail height.</summary>
    Near Refine(EdgeInfo e, double x, double z, double s)
    {
        for (int k = 0; k < 4; k++)
        {
            var t = e.Line.Sample(s);
            double dx = x - Q(t.Position.X), dz = z - Q(t.Position.Z);
            double along = dx * t.Tangent.X + dz * t.Tangent.Z;
            s = Math.Clamp(s + along, 0, e.Line.Length);
        }
        var at = e.Line.Sample(s);
        // Right of travel: (−tz, tx) in the ground plane... for heading h forward (−sin h, −cos h), right is (cos h, −sin h).
        double rx = -at.Tangent.Z, rz = at.Tangent.X;
        double norm = Math.Sqrt(rx * rx + rz * rz);
        double lateral = ((x - Q(at.Position.X)) * rx + (z - Q(at.Position.Z)) * rz) / Math.Max(1e-9, norm);
        // Off the end: a branch's toe 230 m on from a lake on frontier:5 laid its formation back along main's centreline
        // and filled the lake under a trestle there (note 317).
        double past = (x - Q(at.Position.X)) * at.Tangent.X + (z - Q(at.Position.Z)) * at.Tangent.Z;
        bool off = s <= 0 && past < -1 || s >= e.Line.Length && past > 1;
        return new Near(e.Index, s, lateral, Q(at.Position.Y), off);
    }

    /// <summary>The height of the land at (x, z) (plan §12.2).</summary>
    public double Height(double x, double z) => Height(x, z, true);

    /// <summary>The land at (x, z), with or without the roads' beds (a road's surface is the land under its centre).</summary>
    double Height(double x, double z, bool roads)
    {
        var near = Nearby(x, z, _r.CorridorM + 60);
        if (near.Count == 0)
        {
            // Out past every corridor: the land under the fog, near the line's own level where it was last known.
            near = Nearby(x, z, 700);
            if (near.Count == 0)
                return Noise(x, z) * 4 - _r.SkirtDropM;
        }
        double sum = 0, weights = 0, formation = double.NaN, formationW = 0, formationA = double.PositiveInfinity;
        foreach (var n in near)
        {
            var e = _edges[n.Edge];
            double h = EdgeHeight(e, n, x, z);
            double a = Math.Abs(n.Lateral);
            // Inside an edge's formation that edge wins outright, blended over 2 m (§12.2 step 4). Inside two at once (an
            // alternate climbing up alongside main to rejoin it), the nearer rail's: the first edge's won before, and on
            // frontier:3 main's formation stood 2.8 m over the alternate's rail beside it (T107).
            if (a < _r.ShoulderM + 2 && !n.Past && !Disabled(e, n.S))
            {
                double w = a <= _r.ShoulderM ? 1 : 1 - (a - _r.ShoulderM) / 2;
                if (w > formationW || w == formationW && a < formationA)
                    (formation, formationW, formationA) = (h, w, a);
            }
            double weight = e.Priority / ((a + 15) * (a + 15));
            sum += h * weight;
            weights += weight;
        }
        double height = sum / weights;
        if (!double.IsNaN(formation))
            height = formation * formationW + height * (1 - formationW);
        if (_lakes.Length > 0 || _shores.Length > 0)
            height = Waterside(x, z, height, near);
        if (_roads.Length > 0 && roads)
            height = Roads(x, z, height, near);
        return Pads(x, z, height, formation, formationW);
    }

    // ------------------------------------------------------------------ roads (maritime-rules.md §2.2)

    /// <summary>
    /// A road's centre at s, metres out from the main line (positive right): its offset on its side, wandering, and
    /// swinging across the line at each of its crossings (level with the rail at the crossing itself).
    /// </summary>
    public static double RoadLateral(PlanRoad road, IReadOnlyList<PlanCrossing> crossings, double s, double rampM = 70)
    {
        double off = road.OffsetM + road.WanderM * Value(s / road.WavelengthM + road.Phase, 0.37, 0x0AD5EED);
        double side = road.FirstSide, lateral = double.NaN;
        foreach (var c in crossings)
        {
            if (c.Road != road.Id)
                continue;
            if (s >= c.S - rampM && s <= c.S + rampM)
            {
                // Across the line: straight from one side's offset through the rail to the other's.
                double u = (s - c.S) / rampM;
                lateral = side * off * -u;
            }
            if (s > c.S)
                side = -side;
        }
        return double.IsNaN(lateral) ? side * off : lateral;
    }

    /// <summary>
    /// The road beds: flat across the road at the height of the land under its centre (it follows the country), banks
    /// at bankSlope out to the land, a little crown; faded in over its first and last 30 m. The formation keeps its own.
    /// </summary>
    double Roads(double x, double z, double height, List<Near> near)
    {
        if (MainOf(near) is not { } n)
            return height;
        var rr = _r.Roads;
        foreach (var road in _roads)
        {
            if (n.S < road.S0 || n.S > road.S1)
                continue;
            double lat = RoadLateral(road, _plan.Crossings, n.S, rr.RampM);
            double d = Math.Abs(n.Lateral - lat);
            if (d > rr.HalfWidthM + 25)
                continue;
            if (Math.Abs(n.Lateral) < _r.ShoulderM + 1 && Math.Abs(lat) > _r.ShoulderM)
                continue;
            var t = _main.Sample(n.S);
            double cx = t.Position.X - t.Tangent.Z * lat, cz = t.Position.Z + t.Tangent.X * lat;
            double surface = Math.Abs(lat) <= _r.ShoulderM ? n.Rail : Height(cx, cz, false);
            surface -= 0.06 * Smooth(0, rr.HalfWidthM, d);
            double w = Smooth(road.S0, road.S0 + 30, n.S) * Smooth(road.S1, road.S1 - 30, n.S);
            double shaped;
            if (d <= rr.HalfWidthM)
                shaped = surface;
            else
            {
                double bank = (d - rr.HalfWidthM) * rr.BankSlope;
                shaped = Math.Abs(height - surface) <= bank ? height : surface + Math.Sign(height - surface) * bank;
            }
            height += (shaped - height) * w;
        }
        return height;
    }

    // ------------------------------------------------------------------ waterside (docs/design/maritime-rules.md)

    /// <summary>
    /// The lakes and shores (plan data): the land cut down to a lake's basin and its shore, or a shore's beach, mud and
    /// sea, or a dyked marsh's fields and dyke set outright. Never under the formation's fill slope down from any track
    /// it's near (a crossed lake is crossed on a fill), except where a bridge span leaves the ground open.
    /// </summary>
    double Waterside(double x, double z, double height, List<Near> near)
    {
        double h = height;
        foreach (var lake in _lakes)
        {
            double reach = lake.RadiusM * lake.Stretch * (1 + lake.Wobble) + 40;
            if (Math.Abs(x - lake.X) > reach || Math.Abs(z - lake.Z) > reach)
                continue;
            // Metres past the shore, near enough (the metric is in radii).
            double past = (LakeMetric(lake, x, z) - 1) * lake.RadiusM;
            double target = past >= 0
                ? lake.LevelM + 0.35 + past * _r.Lakes.ShoreSlope
                : lake.LevelM + 0.35 - (lake.DepthM + 0.35) * Smooth(0, 14, -past);
            h = Math.Min(h, target);
        }
        if (_shores.Length > 0 && MainOf(near) is { } n)
        {
            foreach (var sh in _shores)
            {
                double taper = _r.Shore.TaperM;
                if (n.S < sh.S0 - taper || n.S > sh.S1 + taper)
                    continue;
                double w = Smooth(sh.S0 - taper, sh.S0, n.S) * Smooth(sh.S1 + taper, sh.S1, n.S);
                // A dyke sets the ground outright (its fields, its bank); a shore only ever cuts the land down to it.
                double shore = Shore(sh, n.S, n.Lateral * sh.Side, x, z, n.Rail, h);
                double shaped = sh.Kind == ShoreKind.Dyke ? shore : Math.Min(h, shore);
                h += (shaped - h) * w;
            }
        }
        if (h >= height)
            return h;
        double fill = double.NegativeInfinity;
        foreach (var q in near)
        {
            if (q.Past || Disabled(_edges[q.Edge], q.S))
                continue;
            double a = Math.Abs(q.Lateral);
            fill = Math.Max(fill, a <= _r.ShoulderM ? q.Rail : q.Rail - (a - _r.ShoulderM) * _r.Lakes.FillSlope);
        }
        return Math.Max(h, Math.Min(height, fill));
    }

    Near? MainOf(List<Near> near)
    {
        foreach (var q in near)
            if (_edges[q.Edge].Role == EdgeRole.Main)
                return q;
        return null;
    }

    /// <summary>How far out a shore's water's edge is at s: its near distance, and more in the coves.</summary>
    public double ShoreEdge(PlanShore sh, double s) => sh.NearM + sh.CoveM * (0.5 + 0.5 * Value(s / sh.CoveWavelengthM + sh.Phase, 0.5, _seed ^ 0xC0FEE5EA));

    /// <summary>
    /// The land at <paramref name="l"/> metres seaward of the line (negative: landward) along a shore. The Atlantic's: a
    /// beach up from the water, a cliff where the rail stands high over it, the sea deepening with drowned drumlins for
    /// islands. Fundy's: a red mudflat at low water between. A dyke: flat fields both sides of the line, the dyke, salt
    /// marsh, then the mud.
    /// </summary>
    double Shore(PlanShore sh, double s, double l, double x, double z, double rail, double land)
    {
        var sr = _r.Shore;
        double d = ShoreEdge(sh, s);
        if (sh.Kind == ShoreKind.River)
        {
            // The near bank down from the line, the bed, the far bank climbing into the valley side; the water follows the rail.
            var rv = _r.Rivers;
            double water = rail - sh.LevelM;
            if (l < d)
                return water + 0.4 + (d - l) * 0.35;
            if (l < d + sh.FlatM)
                return water - 0.2 - rv.DepthM * Smooth(0, Math.Min(8, sh.FlatM / 2), Math.Min(l - d, d + sh.FlatM - l))
                    + 0.5 * Smooth(0.8, 1, Value(x / 14, z / 14, _seed ^ 0x2A4D));
            return water + 0.4 + (l - d - sh.FlatM) * rv.FarSlope;
        }
        if (sh.Kind == ShoreKind.Dyke)
        {
            var dr = _r.Dykes;
            // The fields lie the depth of the low bank under the rail, following it (a dykeland line is all but level).
            double fields = rail - sh.FieldsM + 0.08 * Value(x / 9, z / 9, _seed ^ 0xD1CE);
            if (l < 0)
                return land + (fields - land) * Smooth(dr.LandwardM, dr.LandwardM * 0.6, -l);
            double run = dr.HeightM / dr.SideSlope, half = dr.CrestM / 2 + run;
            if (l < sh.DykeM - half)
                return fields;
            if (l <= sh.DykeM + half)
                return fields + dr.HeightM * Math.Clamp((half - Math.Abs(l - sh.DykeM)) / run, 0, 1);
            if (l < d)
                return sh.LevelM + dr.MarshAboveWaterM + 0.1 * Value(x / 6, z / 6, _seed ^ 0x5A17);
        }
        else if (l < d)
            return sh.LevelM + 0.6 + (d - l) * (rail - sh.LevelM > sr.CliffAboveM ? sr.CliffSlope : sr.BeachSlope);
        double flatEnd = d + sh.FlatM;
        if (l < flatEnd)
            return sh.LevelM + 0.3 - 0.55 * Smooth(d, flatEnd, l) - 0.4 * Smooth(0.75, 1, Math.Abs(Value(x / 45, z / 45, _seed ^ 0x7A1D)));
        double deep = sh.LevelM - 0.3 - sr.DepthM * Smooth(flatEnd, flatEnd + 50, l);
        if (sh.Kind == ShoreKind.Sea)
        {
            // The ria coast's islands: drumlins the sea came in round, humped up out of it.
            double n = 0.5 + 0.5 * Value(x / sr.IslandWavelengthM, z / sr.IslandWavelengthM, _seed ^ 0x1514ED);
            deep += Smooth(1 - sr.IslandShare, 1 - sr.IslandShare * 0.4, n) * (sr.DepthM + 7) * Smooth(flatEnd + 30, flatEnd + 110, l);
        }
        return deep;
    }

    /// <summary>A lake's shape: under 1 inside its shore; the ellipse along its heading, its shore pushed in and out by a slow wobble.</summary>
    public static double LakeMetric(PlanLake lake, double x, double z)
    {
        double dx = x - lake.X, dz = z - lake.Z;
        double u = dx * lake.Cos + dz * lake.Sin, v = -dx * lake.Sin + dz * lake.Cos;
        double ru = lake.RadiusM * lake.Stretch, rv = lake.RadiusM;
        double m = Math.Sqrt(u * u / (ru * ru) + v * v / (rv * rv));
        ulong seed = (ulong)(long)Math.Round(lake.X * 10) * 0x9E3779B97F4A7C15UL ^ (ulong)(long)Math.Round(lake.Z * 10);
        double wob = Value(dx / (rv * 0.8), dz / (rv * 0.8), seed);
        return m / (1 + lake.Wobble * wob);
    }

    /// <summary>
    /// The water nearest (x, z) and whether it's near enough to shape the shore's ground: a lake's or a shore's level
    /// and kind (lake, sea, fundy, dyke), within <paramref name="margin"/> metres of its edge. For the art's shore materials.
    /// </summary>
    public (double Level, string Kind)? WaterNear(double x, double z, double margin)
    {
        foreach (var lake in _lakes)
        {
            double reach = lake.RadiusM * lake.Stretch * (1 + lake.Wobble) + margin;
            if (Math.Abs(x - lake.X) > reach || Math.Abs(z - lake.Z) > reach)
                continue;
            if ((LakeMetric(lake, x, z) - 1) * lake.RadiusM < margin)
                return (lake.LevelM, "lake");
        }
        bool tidal = false;
        foreach (var w in _plan.Water)
            tidal |= w.Type == "tidal";
        if ((_shores.Length == 0 && !tidal) || MainOf(Nearby(x, z, _r.CorridorM + 60)) is not { } n)
            return null;
        foreach (var sh in _shores)
        {
            if (n.S < sh.S0 - _r.Shore.TaperM || n.S > sh.S1 + _r.Shore.TaperM)
                continue;
            double l = n.Lateral * sh.Side;
            if (sh.Kind == ShoreKind.River)
            {
                double d = ShoreEdge(sh, n.S);
                if (l > d - margin && l < d + sh.FlatM + margin)
                    return (n.Rail - sh.LevelM, "river");
            }
            else if (l > (sh.Kind == ShoreKind.Dyke ? sh.DykeM : ShoreEdge(sh, n.S) - margin))
                return (sh.LevelM, sh.Kind.ToString().ToLowerInvariant());
        }
        // A tidal river's red mud banks, up and down its reach from the span.
        foreach (var w in _plan.Water)
            if (w.Type == "tidal" && w.Edge == _edges[n.Edge].Id && n.S > w.S0 - w.WidthM - margin && n.S < w.S1 + w.WidthM + margin)
                return (w.LevelM, "tidal");
        return null;
    }

    /// <summary>The formation's rail-height ground doesn't apply inside a tunnel or over a bridge span.</summary>
    bool Disabled(EdgeInfo e, double s)
    {
        foreach (var st in e.Structures)
            if (s >= st.S0 && s <= st.S1)
                return true;
        return false;
    }

    double EdgeHeight(EdgeInfo e, Near n, double x, double z)
    {
        double rail = n.Rail, a = Math.Abs(n.Lateral), s = n.S;
        // Structures: the hill over a bore, the ravine or river under a span (formation disabled, §12.3).
        foreach (var st in e.Structures)
        {
            if (s < st.S0 - 1 || s > st.S1 + 1)
                continue;
            if (st.Type == StructureType.Tunnel)
                return rail + _r.TunnelCoverM + Noise(x, z) * 3 + Math.Min(a, 60) * 0.25;
            double depth = st.H;
            // The valley's floor, rising at the abutments (fill slopes to the ends).
            double toEnd = Math.Min(s - st.S0, st.S1 - s);
            double floor = Math.Min(depth, 4 + toEnd * 0.9);
            return rail - floor + Noise(x, z) * 1.5;
        }
        if (a <= _r.ShoulderM)
            return rail;
        var (type, h, up, down, land) = IntentAt(e, s, n.Lateral >= 0);
        double noise = Noise(x, z) * _r.NoiseAmplitudeM * Smooth(_r.ShoulderM, 30, a) * BiomeNoise(type);
        double target = h + noise;
        double run = a - _r.ShoulderM;
        double slope = type switch
        {
            IntentType.Cutting => h > 6 ? 2.0 : 1 / 1.5,
            IntentType.LedgeUp => 2.0,
            IntentType.LedgeDrop => _r.WalkableSlope,
            IntentType.Embankment or IntentType.Marsh => 0.5,
            IntentType.Mountain => 1.2,
            IntentType.River => 0.6,
            IntentType.Ravine => 0.9,
            IntentType.Pad => 0.3,
            _ => 0.35,
        };
        double delta = Math.Sign(target) * Math.Min(Math.Abs(target), run * slope);
        // A ledge's drop stays walkable near the track (§12.6), then falls away.
        if (type == IntentType.LedgeDrop && run > _r.WalkableWithinM)
            delta = Math.Max(target, -_r.WalkableWithinM * _r.WalkableSlope - (run - _r.WalkableWithinM) * 1.4);
        // Past the corridor, down under the fog.
        double skirt = _r.SkirtDropM * Smooth(_r.CorridorM, _r.CorridorM + 70, a);
        return rail + delta + Relief(x, z, a, up, down, land) - skirt;
    }

    /// <summary>
    /// The land's own shape out past the formation: hills and ridges at landform scale, as rough as the biome is, none
    /// near the track (it stays walkable, §12.6), and shaped by what the intent says the land does there: a cutting's
    /// walls go on up into the hill, a marsh or a river stays low, a ledge's drop side only falls.
    /// </summary>
    double ReliefOf(string? biome) => biome is not null && _relief.TryGetValue(biome, out var r) ? r : 1;

    /// <summary>How much of the relief an intent takes, rising and falling: a cutting's walls go on up into the hill, a
    /// marsh or a river stays low, a ledge's drop side only falls.</summary>
    static (double Up, double Down) Takes(IntentType t) => t switch
    {
        IntentType.Cutting or IntentType.LedgeUp or IntentType.Mountain => (1.3, 1.0),
        IntentType.Embankment => (0.6, 0.6),
        IntentType.Ravine => (0.4, 0.4),
        IntentType.River or IntentType.Marsh => (0.12, 0.12),
        IntentType.Pad => (0.2, 0.2),
        IntentType.LedgeDrop => (0, 1),
        _ => (1, 1),
    };

    /// <summary>Two intents <paramref name="f"/> of the way from one to the other: height, relief and biome all blended, so the land never steps.</summary>
    (IntentType, double, double, double, Landform) Blend(SideIntent a, string? biomeA, SideIntent b, string? biomeB, double f)
    {
        var (ua, da) = Takes(a.T);
        var (ub, db) = Takes(b.T);
        double ra = ReliefOf(biomeA), rb = ReliefOf(biomeB);
        return (f < 0.5 ? a.T : b.T, a.H + (b.H - a.H) * f, (ua * ra) * (1 - f) + (ub * rb) * f, (da * ra) * (1 - f) + (db * rb) * f,
            Landform.Lerp(LandformOf(biomeA), LandformOf(biomeB), f));
    }

    Landform LandformOf(string? biome) => biome is not null && _landforms.TryGetValue(biome, out var l) ? l : Landform.Plain;

    /// <summary>
    /// The Maritimes' landforms, as weights a biome mixes (biomes.json "landform"): rolling ground; drumlins, the ice's
    /// long whaleback hills all lying one way (the flow, <c>terrain.drumlins</c>); knobs, the granite barrens' bare
    /// rounded humps with bogs in the hollows between; the highland plateau, flat-topped and cut by steep river gorges.
    /// </summary>
    public readonly record struct Landform(double Rolling, double Drumlins, double Knobs, double Plateau)
    {
        public static readonly Landform Plain = new(1, 0, 0, 0);

        public static Landform Lerp(Landform a, Landform b, double f) => new(a.Rolling + (b.Rolling - a.Rolling) * f, a.Drumlins + (b.Drumlins - a.Drumlins) * f,
            a.Knobs + (b.Knobs - a.Knobs) * f, a.Plateau + (b.Plateau - a.Plateau) * f);

        public static Landform From(IReadOnlyDictionary<string, double> w)
        {
            double r = w.GetValueOrDefault("rolling"), d = w.GetValueOrDefault("drumlins"), k = w.GetValueOrDefault("knobs"), p = w.GetValueOrDefault("plateau");
            double sum = r + d + k + p;
            return sum <= 0 ? Plain : new(r / sum, d / sum, k / sum, p / sum);
        }
    }

    /// <summary>
    /// The land's own shape out past the formation: hills and ridges at landform scale, as rough as the biome is (its
    /// noiseScale), and none near the track, which stays walkable (§12.6).
    /// </summary>
    double Relief(double x, double z, double a, double up, double down, Landform land)
    {
        double ramp = Smooth(_r.ReliefFromM, _r.ReliefFullM, a);
        if (ramp <= 0 || _r.ReliefM <= 0)
            return 0;
        double shape = 0;
        if (land.Rolling > 0)
        {
            double w0 = _r.ReliefWavelengthM[0], w1 = _r.ReliefWavelengthM[^1];
            double n = 0.65 * Value(x / w1, z / w1, _seed ^ 0x2545F491) + 0.35 * Value(x / w0, z / w0, _seed ^ 0x9E3779B9);
            double ridged = 1 - 2 * Math.Abs(Value(x / (w1 * 0.7), z / (w1 * 0.7), _seed ^ 0x68E31DA4));
            shape += land.Rolling * ((1 - _r.ReliefRidged) * n + _r.ReliefRidged * ridged + _r.ReliefUp);
        }
        if (land.Drumlins > 0)
        {
            // Stretched along the ice's flow: an isolated whaleback wherever the noise stands high, the ground between low.
            var d = _r.Drumlins;
            double u = x * _flowCos + z * _flowSin, v = -x * _flowSin + z * _flowCos;
            double n = 0.5 + 0.5 * Value(u / (d.WavelengthM * d.Stretch), v / d.WavelengthM, _seed ^ 0x51ED270B);
            double hump = Smooth(0.45, 0.85, n);
            shape += land.Drumlins * (Math.Sqrt(hump) * d.Height + 0.15 * Value(x / 60, z / 60, _seed ^ 0x3C6EF372));
        }
        if (land.Knobs > 0)
        {
            // Bare granite humps, rounded by the ice, close together, and the hollows between them holding the bogs.
            var k = _r.Knobs;
            double n = Value(x / k.WavelengthM, z / k.WavelengthM, _seed ^ 0x7F4A7C15), m = Value(x / (k.WavelengthM * 3.1), z / (k.WavelengthM * 3.1), _seed ^ 0x2C1B3C6D);
            double k0 = Math.Max(0, n + 0.2), knob = k0 * Math.Sqrt(k0);
            shape += land.Knobs * (knob * k.Height + m * 0.35 - 0.1);
        }
        if (land.Plateau > 0)
        {
            // The highland: a flat top at a height (the noise clamped, with a scarp where it falls away), and gorges cut
            // down through it where a river runs (a thin valley along a noise's zero line).
            var p = _r.Plateau;
            double n = 0.5 + 0.5 * Value(x / p.WavelengthM, z / p.WavelengthM, _seed ^ 0x6A09E667);
            double top = Smooth(0.35, 0.5, n) * p.Height + 0.12 * Value(x / 90, z / 90, _seed ^ 0x1F83D9AB);
            double river = Math.Abs(Value(x / p.GorgeWavelengthM, z / p.GorgeWavelengthM, _seed ^ 0x5BE0CD19));
            double gorge = Smooth(p.GorgeWidth, 0, river);
            shape += land.Plateau * (top - gorge * (top + p.GorgeDepth));
        }
        return _r.ReliefM * ramp * shape * (shape > 0 ? up : down);
    }

    static double BiomeNoise(IntentType t) => t switch { IntentType.Pad => 0.1, IntentType.Marsh => 0.3, IntentType.Mountain => 2, _ => 1 };

    /// <summary>The intent at s on one side, blended over <c>blendM</c> at span boundaries so the land never steps.</summary>
    (IntentType Type, double H, double Up, double Down, Landform Land) IntentAt(EdgeInfo e, double s, bool right)
    {
        var spans = e.Intents;
        if (spans.Length == 0)
            return (IntentType.Plain, 0, 1, 1, Landform.Plain);
        int lo = 0, hi = spans.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (spans[mid].S0 <= s)
                lo = mid;
            else
                hi = mid - 1;
        }
        var span = spans[lo];
        var here = right ? span.Right : span.Left;
        double half = _r.BlendM / 2;
        if (lo + 1 < spans.Length && spans[lo + 1].S0 - s < half)
        {
            var next = right ? spans[lo + 1].Right : spans[lo + 1].Left;
            double f = Smooth(-half, half, s - spans[lo + 1].S0);
            return Blend(here, span.Biome, next, spans[lo + 1].Biome, f);
        }
        if (lo > 0 && s - span.S0 < half)
        {
            var prev = right ? spans[lo - 1].Right : spans[lo - 1].Left;
            double f = Smooth(-half, half, s - span.S0);
            return Blend(prev, spans[lo - 1].Biome, here, span.Biome, f);
        }
        return Blend(here, span.Biome, here, span.Biome, 0);
    }

    /// <summary>
    /// Flattened pads (§12.1 "pad"): the fortress, the facilities, the settlements, blended in over 30 m. A box pad (a stop's
    /// ground) ends square at its zone's ends and leaves the rail's formation its own, since the line past a stop may climb.
    /// </summary>
    double Pads(double x, double z, double height, double formation, double formationW)
    {
        foreach (var p in _plan.Pads)
        {
            double dx = x - p.X, dz = z - p.Z;
            double d;
            if (p.Box)
            {
                double hx = -DMath.Sin(p.HeadingDeg * Math.PI / 180), hz = -DMath.Cos(p.HeadingDeg * Math.PI / 180);
                double along = dx * hx + dz * hz, across = Math.Abs(dx * hz - dz * hx);
                d = Math.Max(across, p.RadiusM + Math.Max(0, Math.Abs(along) - p.HalfLengthM));
            }
            else if (p.HalfLengthM > 0)
            {
                // A long pad along a heading: distance to its centre line segment.
                double hx = -DMath.Sin(p.HeadingDeg * Math.PI / 180), hz = -DMath.Cos(p.HeadingDeg * Math.PI / 180);
                double along = Math.Clamp(dx * hx + dz * hz, -p.HalfLengthM, p.HalfLengthM);
                double ex = dx - along * hx, ez = dz - along * hz;
                d = Math.Sqrt(ex * ex + ez * ez);
            }
            else
                d = Math.Sqrt(dx * dx + dz * dz);
            if (d >= p.RadiusM + 30)
                continue;
            double w = 1 - Smooth(p.RadiusM, p.RadiusM + 30, d);
            if (p.Box && !double.IsNaN(formation))
                w *= 1 - formationW;
            height = height * (1 - w) + p.ElevM * w;
        }
        return height;
    }

    static double Smooth(double a, double b, double x)
    {
        double t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Two octaves of integer-hash value noise at the terrain's wavelengths, in [−1, 1].</summary>
    double Noise(double x, double z)
    {
        double w0 = _r.NoiseWavelengthM[0], w1 = _r.NoiseWavelengthM[^1];
        return 0.4 * Value(x / w0, z / w0, _seed) + 0.6 * Value(x / w1, z / w1, _seed ^ 0x5bd1e995);
    }

    /// <summary>A cosine from arithmetic alone (Taylor series after range reduction): the same bits on every machine (§17.3).</summary>
    static double DetCos(double x)
    {
        const double tau = 2 * Math.PI;
        x -= tau * Math.Floor(x / tau + 0.5);
        double x2 = x * x, term = 1, sum = 1;
        for (int i = 1; i < 14; i++)
        {
            term *= -x2 / ((2 * i - 1) * (2 * i));
            sum += term;
        }
        return sum;
    }

    static double Value(double x, double z, ulong seed)
    {
        double fx = Math.Floor(x), fz = Math.Floor(z);
        long ix = (long)fx, iz = (long)fz;
        double tx = x - fx, tz = z - fz;
        tx = tx * tx * (3 - 2 * tx);
        tz = tz * tz * (3 - 2 * tz);
        double a = Streams.Lattice(seed, ix, iz), b = Streams.Lattice(seed, ix + 1, iz);
        double c = Streams.Lattice(seed, ix, iz + 1), d = Streams.Lattice(seed, ix + 1, iz + 1);
        return (a + (b - a) * tx) * (1 - tz) + (c + (d - c) * tx) * tz;
    }

    // ------------------------------------------------------------------ ground for people and bodies

    /// <summary>
    /// Where someone standing at <paramref name="p"/> is held up: the land, or the floor of a tunnel or deck of a bridge
    /// when they're down on it rather than up on the hill (plan §12.3).
    /// </summary>
    public double Ground(Double3 p)
    {
        var near = Nearby(p.X, p.Z, 12);
        foreach (var n in near)
        {
            var e = _edges[n.Edge];
            foreach (var st in e.Structures)
                if (n.S >= st.S0 && n.S <= st.S1)
                {
                    double width = st.Type == StructureType.Tunnel ? 4.5 : 2.6;
                    // Inside the bore or on the deck (not up on the hill above it).
                    if (Math.Abs(n.Lateral) <= width && p.Y < n.Rail + 6)
                        return n.Rail;
                }
        }
        return Height(p.X, p.Z);
    }

    // ------------------------------------------------------------------ tiles

    /// <summary>Tile coordinates holding (x, z).</summary>
    public (int X, int Z) TileOf(double x, double z) => ((int)Math.Floor(x / _r.TileM), (int)Math.Floor(z / _r.TileM));

    /// <summary>§12.2: a tile exists only where track is within the corridor.</summary>
    public bool TileExists(int tx, int tz)
    {
        double cx = (tx + 0.5) * _r.TileM, cz = (tz + 0.5) * _r.TileM;
        return Nearby(cx, cz, _r.CorridorM + _r.TileM * 0.71).Count > 0;
    }

    /// <summary>Vertices per side of a tile: 256 m at 2 m is 129.</summary>
    public int TileVertices => (int)Math.Round(_r.TileM / _r.GridM) + 1;

    /// <summary>A tile's heights, row by row from its (−x, −z) corner (plan §12.2: 129 × 129 at 2 m).</summary>
    public float[] TileHeights(int tx, int tz)
    {
        int n = TileVertices;
        var h = new float[n * n];
        double x0 = tx * _r.TileM, z0 = tz * _r.TileM;
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                h[j * n + i] = (float)(Math.Round(Height(x0 + i * _r.GridM, z0 + j * _r.GridM) * 1000) / 1000);
        return h;
    }

    /// <summary>§17.3: a tile's checksum, FNV-1a over its heights to the millimetre. Host and clients compare these.</summary>
    public static ulong Checksum(float[] heights)
    {
        ulong h = 0xCBF29CE484222325UL;
        foreach (float v in heights)
        {
            long mm = (long)Math.Round(v * 1000.0);
            for (int b = 0; b < 8; b++)
            {
                h ^= (byte)(mm >> (b * 8));
                h *= 0x100000001B3UL;
            }
        }
        return h;
    }

    public ulong TileChecksum(int tx, int tz) => Checksum(TileHeights(tx, tz));

    /// <summary>
    /// §17.3's safety net, cheaply: the land's heights at points spread down the main line and out to either side of it,
    /// to the millimetre. Host and joiner compare it; if they differ, the joiner's ground (and so its players'
    /// prediction) would, and it's refused rather than left to desync.
    /// </summary>
    public string Print(int points = 256)
    {
        var heights = new float[points];
        for (int i = 0; i < points; i++)
        {
            var t = _main.Sample(_main.Length * (i + 0.5) / points);
            double lateral = (i % 2 == 0 ? 1 : -1) * (5 + (i * 37 % 240));
            double x = t.Position.X - t.Tangent.Z * lateral, z = t.Position.Z + t.Tangent.X * lateral;
            heights[i] = (float)(Math.Round(Height(x, z) * 1000) / 1000);
        }
        return Checksum(heights).ToString("x16");
    }

    /// <summary>The water planes (§12.4) near a point: the level of the one it's in, or null.</summary>
    public double? WaterAt(double x, double z)
    {
        foreach (var w in _plan.Water)
        {
            var edge = Array.Find(_edges, e => e.Id == w.Edge);
            if (edge is null)
                continue;
            var near = Nearby(x, z, w.WidthM + 20).FirstOrDefault(n => n.Edge == edge.Index);
            if (near == default || near.S < w.S0 - (w.Type is "river" or "tidal" ? w.WidthM : 0) || near.S > w.S1 + (w.Type is "river" or "tidal" ? w.WidthM : 0))
                continue;
            if (w.Type is "river" or "tidal" && Math.Abs(near.S - (w.S0 + w.S1) / 2) > w.WidthM / 2 + (w.S1 - w.S0) / 2)
                continue;
            if (Height(x, z) < w.LevelM)
                return w.LevelM;
        }
        if (WaterNear(x, z, 0) is { } water && Height(x, z) < water.Level)
            return water.Level;
        return null;
    }
}
