using System.Runtime.CompilerServices;

namespace DarkTerritory.Sim.Towns;

/// <summary>
/// A town type's ground (queue #424, ARCHITECTURE §8 note 692): its plan's terraces, quarry benches, ramps, water and plank
/// decks as a 1 m raster in its rail frame, levels in metres over the yard's. What a crewmate stands on there
/// (<see cref="Level"/>), what the land is under a deck or the water (<see cref="Bed"/>), the water's surface
/// (<see cref="Water"/>), and the walls the steps and the water's edges need (<see cref="Edges"/>): a retaining wall where
/// the ground steps by more than a stride, a rail where it meets water too deep to wade. Made from the plan alone, so the
/// same on every machine; nothing here is a design number but <see cref="FormShape"/>'s levels.
/// </summary>
public sealed class TownGround
{
    /// <summary>The raster's cell (m).</summary>
    public const double Cell = 1;
    /// <summary>The deepest water that's waded (m) without a rail along its edge.</summary>
    public const double Wade = 0.75;
    /// <summary>How high a step in the ground may be and still be walked up (the motor lifts you up to its step and a
    /// half-metre more: PlayerMotor's GroundLiftMargin), so a terrace's edge past it is walled. And the rail's and a
    /// retaining wall's parapet over the higher side.</summary>
    public const double Stride = 0.7, Parapet = 1.1;
    /// <summary>The outer share of a town's margin over which its land eases into the line's (note 692). Not a design number.</summary>
    const double BlendShare = 0.4;

    readonly float[] _level, _bed, _water, _blend;
    readonly byte[] _kind;
    readonly bool[] _inside = [];
    readonly bool[] _ramp = [];

    public double S0 { get; }
    public double D0 { get; }
    public int Ns { get; }
    public int Nd { get; }

    /// <summary>Whether the town's ground is all at the yard's level (the street town): nothing to raise, lower or wall.</summary>
    public bool Flat { get; }

    static readonly ConditionalWeakTable<TownPlan, TownGround> Cache = [];

    /// <summary>The ground of <paramref name="plan"/> (made once a plan).</summary>
    public static TownGround Of(TownPlan plan) => Cache.GetValue(plan, p => new TownGround(p.Bounds, p.Districts, p.Ways, p.Margin));

    /// <summary>The ground a plan's pieces make, before there's a plan (the form's planner asks it, note 692's walks).</summary>
    public static TownGround Build(TownBounds bounds, IReadOnlyList<TownDistrict> districts, IReadOnlyList<TownWay> ways, double margin) =>
        new(bounds, districts, ways, margin);

    TownGround(TownBounds? b, IReadOnlyList<TownDistrict> districts, IReadOnlyList<TownWay> ways, double margin)
    {
        if (b is null || districts.Count == 0 && ways.All(w => w.Levels is null && !w.Deck))
        {
            Flat = true;
            _level = _bed = _water = _blend = [];
            _kind = [];
            return;
        }
        Bounds = b;
        Margin = margin;
        double m = margin + 2;
        S0 = b.Rear - m;
        D0 = -b.Left - m;
        Ns = (int)Math.Ceiling((b.Gate - b.Rear + 2 * m) / Cell);
        Nd = (int)Math.Ceiling((b.Left + b.Right + 2 * m) / Cell);
        int n = Ns * Nd;
        _level = new float[n];
        _bed = new float[n];
        _water = new float[n];
        _blend = new float[n];
        _kind = new byte[n];
        _inside = new bool[n];
        _ramp = new bool[n];
        Array.Fill(_water, float.NaN);
        // How much the town's ground is the land's (note 692's landforms): all of it inside the wall and for half its margin
        // past it, then less to nothing at the margin's edge, where the line plan's land takes over.
        for (int i = 0; i < Ns; i++)
            for (int j = 0; j < Nd; j++)
            {
                double s = S0 + (i + 0.5) * Cell, dd = D0 + (j + 0.5) * Cell;
                _inside[i * Nd + j] = b.Holds(s, dd);
                double out_ = _inside[i * Nd + j] ? 0 : OutsideBy(b, s, dd);
                _blend[i * Nd + j] = out_ <= 0 ? 1 : margin <= 0 ? 0 : (float)Smooth(Math.Clamp((margin - out_) / (BlendShare * margin), 0, 1));
            }
        // Each district over those before it (a terrace inside a terrace is the deeper bench; a hummock out of the marsh). A
        // landform's (its falloff) eases from what's under it to its own level over its falloff past its outline.
        foreach (var d in districts)
        {
            var (s0, s1, d0, d1) = Box(d.Outline);
            double f = d.Falloff;
            for (int i = Math.Max(0, I(s0 - f)); i <= Math.Min(Ns - 1, I(s1 + f)); i++)
                for (int j = Math.Max(0, J(d0 - f)); j <= Math.Min(Nd - 1, J(d1 + f)); j++)
                {
                    double s = S0 + (i + 0.5) * Cell, dd = D0 + (j + 0.5) * Cell;
                    int k = i * Nd + j;
                    // The land outside the wall stays outside it (note 692): the town's ground inside is its own.
                    if (d.Outside && b.Holds(s, dd))
                        continue;
                    if (!d.Holds(s, dd))
                    {
                        if (f <= 0 || d.Kind is DistrictKind.Water or DistrictKind.Deck or DistrictKind.Stage)
                            continue;
                        double away = d.Distance(s, dd);
                        if (away >= f)
                            continue;
                        double t = Smooth(1 - away / f);
                        _level[k] = _bed[k] = (float)(_level[k] + (d.Level + Swell(d, s, dd) - _level[k]) * t);
                        continue;
                    }
                    switch (d.Kind)
                    {
                        case DistrictKind.Water:
                            _water[k] = (float)d.Level;
                            _bed[k] = _level[k] = (float)(d.Level - d.Depth);
                            _kind[k] = 2;
                            break;
                        case DistrictKind.Deck or DistrictKind.Stage:
                            // Over whatever's under it: the water and its bed stay, the deck is what's stood on.
                            _level[k] = (float)d.Level;
                            if (_kind[k] != 2)
                                _bed[k] = (float)d.Level;
                            _kind[k] = 3;
                            break;
                        default:
                            _level[k] = _bed[k] = (float)(d.Level + Swell(d, s, dd));
                            _water[k] = float.NaN;
                            _kind[k] = 1;
                            break;
                    }
                }
        }
        // The ways: a ramp's cells at its level along it; a deck's at its deck's; a way on the ground keeps the ground's.
        foreach (var w in ways)
        {
            if (w.Levels is null && !w.Deck)
                continue;
            for (int p = 1; p < w.Points.Count; p++)
            {
                var (a, z) = (w.Points[p - 1], w.Points[p]);
                double la = w.Levels?[p - 1] ?? w.Level, lz = w.Levels?[p] ?? w.Level;
                double es = z.S - a.S, ed = z.D - a.D, len2 = es * es + ed * ed, half = w.Width / 2;
                if (len2 < 1e-9)
                    continue;
                var (s0, s1, d0, d1) = (Math.Min(a.S, z.S) - half, Math.Max(a.S, z.S) + half, Math.Min(a.D, z.D) - half, Math.Max(a.D, z.D) + half);
                for (int i = Math.Max(0, I(s0)); i <= Math.Min(Ns - 1, I(s1)); i++)
                    for (int j = Math.Max(0, J(d0)); j <= Math.Min(Nd - 1, J(d1)); j++)
                    {
                        double s = S0 + (i + 0.5) * Cell, dd = D0 + (j + 0.5) * Cell;
                        double t = Math.Clamp(((s - a.S) * es + (dd - a.D) * ed) / len2, 0, 1);
                        double x = a.S + es * t - s, y = a.D + ed * t - dd;
                        if (x * x + y * y > half * half)
                            continue;
                        int k = i * Nd + j;
                        _level[k] = (float)(la + (lz - la) * t);
                        _ramp[k] = w.Levels is not null;
                        if (w.Deck)
                        {
                            if (_kind[k] != 2)
                                _bed[k] = _level[k];
                            _kind[k] = 3;
                        }
                        else
                        {
                            _bed[k] = _level[k];
                            _water[k] = float.NaN;
                            _kind[k] = 1;
                        }
                    }
            }
        }
        Flat = _level.All(x => x == 0) && _water.All(float.IsNaN);
    }

    /// <summary>The wall the ground was laid inside, and how far past it the town shapes the land (m; note 692).</summary>
    public TownBounds? Bounds { get; }
    public double Margin { get; }

    static double Smooth(double x) => x * x * (3 - 2 * x);

    /// <summary>A landform's roll at (s, d) (note 692): two octaves of value noise on a 64 m and a 23 m lattice from its
    /// seed, in [−Roll, Roll]. Pure and integer-hashed, so every machine lays the same land.</summary>
    static double Swell(TownDistrict d, double s, double dd) =>
        d.Roll <= 0 ? 0 : d.Roll * (0.7 * Noise(d.RollSeed, s / 64, dd / 64) + 0.3 * Noise(d.RollSeed ^ 0x9E3779B9u, s / 23, dd / 23));

    static double Noise(uint seed, double x, double y)
    {
        double fx = Math.Floor(x), fy = Math.Floor(y), tx = Smooth(x - fx), ty = Smooth(y - fy);
        int ix = (int)fx, iy = (int)fy;
        double a = Lattice(seed, ix, iy), b = Lattice(seed, ix + 1, iy), c = Lattice(seed, ix, iy + 1), e = Lattice(seed, ix + 1, iy + 1);
        return a + (b - a) * tx + (c - a) * ty + (a - b - c + e) * tx * ty;
    }

    static double Lattice(uint seed, int x, int y)
    {
        uint h = seed ^ (uint)x * 0x27D4EB2Du ^ (uint)y * 0x165667B1u;
        h = (h ^ (h >> 15)) * 0x85EBCA77u;
        h = (h ^ (h >> 13)) * 0xC2B2AE3Du;
        h ^= h >> 16;
        return h * (2.0 / uint.MaxValue) - 1;
    }

    /// <summary>How far outside the wall's outline (or rectangle) a point is (m).</summary>
    static double OutsideBy(TownBounds b, double s, double d)
    {
        IReadOnlyList<(double S, double D)> o = b.Outline ?? [(b.Rear, -b.Left), (b.Gate, -b.Left), (b.Gate, b.Right), (b.Rear, b.Right)];
        double best = double.MaxValue;
        for (int i = 0, j = o.Count - 1; i < o.Count; j = i++)
            best = Math.Min(best, TownDistrict.SegDist(s, d, o[j], o[i]));
        return best;
    }

    /// <summary>Whether a point is inside the town's wall.</summary>
    public bool Inside(double s, double d) => !Flat && Index(s, d) is var k and >= 0 && _inside[k];

    /// <summary>How much of a point's ground is the town's (1 inside the wall; easing to 0 at the margin's edge).</summary>
    public double Blend(double s, double d) => !Flat && Index(s, d) is var k and >= 0 ? _blend[k] : 0;

    /// <summary>
    /// Where a crewmate can walk to from (<paramref name="s"/>, <paramref name="d"/>) inside the town (note 692; the
    /// director: "make sure that there's always walkable paths for the player"): every cell reached by steps no taller
    /// than <see cref="Stride"/>, never into water too deep to wade, never through <paramref name="blocked"/> (the
    /// buildings' footprints, the walls).
    /// </summary>
    public Func<double, double, bool> Walk(double s, double d, Func<double, double, bool>? blocked = null)
    {
        if (Flat)
            return (_, _) => true;
        var seen = new bool[Ns * Nd];
        bool Open(int k)
        {
            if (_blend[k] < 1 || _kind[k] == 2 && _water[k] - _bed[k] > Wade)
                return false;
            return blocked is null || !blocked(S0 + (k / Nd + 0.5) * Cell, D0 + (k % Nd + 0.5) * Cell);
        }
        int start = Index(s, d);
        if (start >= 0)
        {
            var queue = new Queue<int>();
            seen[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int k = queue.Dequeue(), i = k / Nd, j = k % Nd;
                foreach (var (a, b) in new[] { (i + 1, j), (i - 1, j), (i, j + 1), (i, j - 1) })
                {
                    if (a < 0 || b < 0 || a >= Ns || b >= Nd)
                        continue;
                    int n = a * Nd + b;
                    if (seen[n] || !Open(n) || Math.Abs(_level[n] - _level[k]) > Stride)
                        continue;
                    seen[n] = true;
                    queue.Enqueue(n);
                }
            }
        }
        return (ps, pd) => Index(ps, pd) is var k and >= 0 && seen[k];
    }

    int I(double s) => (int)Math.Floor((s - S0) / Cell);
    int J(double d) => (int)Math.Floor((d - D0) / Cell);

    static (double S0, double S1, double D0, double D1) Box(IReadOnlyList<(double S, double D)> o) =>
        (o.Min(p => p.S), o.Max(p => p.S), o.Min(p => p.D), o.Max(p => p.D));

    int Index(double s, double d)
    {
        int i = I(s), j = J(d);
        return i < 0 || j < 0 || i >= Ns || j >= Nd ? -1 : i * Nd + j;
    }

    /// <summary>Whether a point is inside the raster and its ground isn't the yard's level: somewhere the town has shaped.</summary>
    public bool Shaped(double s, double d) => !Flat && Index(s, d) is var k and >= 0 && (_level[k] != 0 || _bed[k] != 0 || !float.IsNaN(_water[k]));

    /// <summary>What a crewmate stands on at a point (m over the yard's level): a terrace, a ramp, a deck, a basin's bed.</summary>
    public double Level(double s, double d) => !Flat && Index(s, d) is var k and >= 0 ? _level[k] : 0;

    /// <summary>The land itself at a point (m over the yard's): under a deck or the water, the bed.</summary>
    public double Bed(double s, double d) => !Flat && Index(s, d) is var k and >= 0 ? _bed[k] : 0;

    /// <summary>The water's surface at a point (m over the yard's), or null where there's none.</summary>
    public double? Water(double s, double d) => !Flat && Index(s, d) is var k and >= 0 && !float.IsNaN(_water[k]) ? _water[k] : null;

    /// <summary>Whether a point is on a deck.</summary>
    public bool Deck(double s, double d) => !Flat && Index(s, d) is var k and >= 0 && _kind[k] == 3;

    /// <summary>
    /// The walls the ground needs (rail frame: from (S, D) to (S, D), its bottom and top in m over the yard's level): a
    /// retaining wall along every step higher than <see cref="Stride"/>, from the low side up to a parapet over the high
    /// side; a rail along every edge of walkable ground (land or deck) on water deeper than <see cref="Wade"/>. Laid
    /// along the raster's cell edges, runs of the same wall merged.
    /// </summary>
    public IReadOnlyList<(double S0, double D0, double S1, double D1, double Bottom, double Top)> Edges()
    {
        if (Flat)
            return [];
        if (_edges is not null)
            return _edges;
        var edges = new List<(double, double, double, double, double, double)>();
        // Across each cell edge (along d between i and i+1; along s between j and j+1), what wall there is, if any.
        (double Bottom, double Top)? Between(int a, int b)
        {
            // Only inside the wall (outside, the land eases where it falls: note 692's landforms; its blend is whole for the
            // margin's first part too, so it's the wall that's asked).
            if (!_inside[a] || !_inside[b])
                return null;
            bool wetA = !float.IsNaN(_water[a]) && _kind[a] == 2, wetB = !float.IsNaN(_water[b]) && _kind[b] == 2;
            if (wetA != wetB)
            {
                int dry = wetA ? b : a, wet = wetA ? a : b;
                if (_water[wet] - _bed[wet] > Wade)
                    return (_bed[wet], _level[dry] + Parapet);
                return null;
            }
            if (wetA)
                return null;
            double la = _level[a], lb = _level[b];
            if (Math.Abs(la - lb) <= Stride)
                return null;
            return (Math.Min(la, lb), Math.Max(la, lb) + Parapet);
        }
        // Edges between rows i and i+1 (a wall across the line's way, running along d), merged along j.
        for (int i = 0; i + 1 < Ns; i++)
        {
            (int J, double B, double T)? open = null;
            for (int j = 0; j <= Nd; j++)
            {
                var w = j < Nd ? Between(i * Nd + j, (i + 1) * Nd + j) : null;
                if (open is { } o && (w is null || Math.Abs(w.Value.Bottom - o.B) > 0.05 || Math.Abs(w.Value.Top - o.T) > 0.05))
                {
                    double s = S0 + (i + 1) * Cell;
                    edges.Add((s, D0 + o.J * Cell, s, D0 + j * Cell, o.B, o.T));
                    open = null;
                }
                if (w is { } x && open is null)
                    open = (j, x.Bottom, x.Top);
            }
        }
        // Edges between columns j and j+1 (a wall along the line's way), merged along i.
        for (int j = 0; j + 1 < Nd; j++)
        {
            (int I, double B, double T)? open = null;
            for (int i = 0; i <= Ns; i++)
            {
                var w = i < Ns ? Between(i * Nd + j, i * Nd + j + 1) : null;
                if (open is { } o && (w is null || Math.Abs(w.Value.Bottom - o.B) > 0.05 || Math.Abs(w.Value.Top - o.T) > 0.05))
                {
                    double d = D0 + (j + 1) * Cell;
                    edges.Add((S0 + o.I * Cell, d, S0 + i * Cell, d, o.B, o.T));
                    open = null;
                }
                if (w is { } x && open is null)
                    open = (i, x.Bottom, x.Top);
            }
        }
        return _edges = edges;
    }

    List<(double, double, double, double, double, double)>? _edges;

    /// <summary>
    /// The town's own ground as few rectangles as it takes (rail frame: S0 to S1 along, D0 to D1 across, its level, and
    /// what it is: 1 land, 2 a basin's bed, 3 a deck): every shaped cell and those within <paramref name="apron"/> of one,
    /// grown greedily along the line then across it while the level and kind hold. What the art lays the ground from.
    /// </summary>
    public IReadOnlyList<(double S0, double S1, double D0, double D1, double Level, byte Kind)> Rects(double apron, bool insideOnly = false)
    {
        if (Flat)
            return [];
        var near = Apron(apron);
        // (A ramp's cells are its own to draw, along it: Rects would lay one box a cell up its slope.)
        near = [.. near.Select((x, k) => x && !_ramp[k] && (!insideOnly || _inside[k]))];
        var used = new bool[Ns * Nd];
        var rects = new List<(double, double, double, double, double, byte)>();
        float LevelOf(int k) => _kind[k] == 3 ? _level[k] : _bed[k];
        byte KindOf(int k) => _kind[k] == 0 ? (byte)1 : _kind[k];
        for (int i = 0; i < Ns; i++)
            for (int j = 0; j < Nd; j++)
            {
                int k = i * Nd + j;
                if (used[k] || !near[k])
                    continue;
                float lv = LevelOf(k);
                byte kd = KindOf(k);
                bool Same(int x) => !used[x] && near[x] && Math.Abs(LevelOf(x) - lv) < 1e-3 && KindOf(x) == kd;
                int i1 = i;
                while (i1 + 1 < Ns && Same((i1 + 1) * Nd + j))
                    i1++;
                int j1 = j;
                while (j1 + 1 < Nd)
                {
                    bool all = true;
                    for (int a = i; a <= i1 && all; a++)
                        all = Same(a * Nd + j1 + 1);
                    if (!all)
                        break;
                    j1++;
                }
                for (int a = i; a <= i1; a++)
                    for (int b = j; b <= j1; b++)
                        used[a * Nd + b] = true;
                rects.Add((S0 + i * Cell, S0 + (i1 + 1) * Cell, D0 + j * Cell, D0 + (j1 + 1) * Cell, lv, kd));
            }
        return rects;
    }

    /// <summary>The town's water as rectangles of one surface (rail frame), the basins and channels under the decks too.</summary>
    public IReadOnlyList<(double S0, double S1, double D0, double D1, double Level)> WaterRects()
    {
        if (Flat)
            return [];
        var used = new bool[Ns * Nd];
        var rects = new List<(double, double, double, double, double)>();
        for (int i = 0; i < Ns; i++)
            for (int j = 0; j < Nd; j++)
            {
                int k = i * Nd + j;
                if (used[k] || float.IsNaN(_water[k]))
                    continue;
                float w = _water[k];
                bool Same(int x) => !used[x] && !float.IsNaN(_water[x]) && Math.Abs(_water[x] - w) < 1e-3;
                int i1 = i;
                while (i1 + 1 < Ns && Same((i1 + 1) * Nd + j))
                    i1++;
                int j1 = j;
                while (j1 + 1 < Nd)
                {
                    bool all = true;
                    for (int a = i; a <= i1 && all; a++)
                        all = Same(a * Nd + j1 + 1);
                    if (!all)
                        break;
                    j1++;
                }
                for (int a = i; a <= i1; a++)
                    for (int b = j; b <= j1; b++)
                        used[a * Nd + b] = true;
                rects.Add((S0 + i * Cell, S0 + (i1 + 1) * Cell, D0 + j * Cell, D0 + (j1 + 1) * Cell, w));
            }
        return rects;
    }

    /// <summary>Whether a point is where the town draws its own ground (shaped, or within the apron of shaped ground): the
    /// land's own mesh is left out there.</summary>
    public bool Drawn(double s, double d, double apron)
    {
        if (Flat || Index(s, d) is not (var k and >= 0))
            return false;
        return Apron(apron)[k];
    }

    bool[]? _apron;
    double _apronFor = -1;

    bool[] Apron(double apron)
    {
        if (_apron is not null && _apronFor == apron)
            return _apron;
        var near = new bool[Ns * Nd];
        int r = (int)Math.Ceiling(apron / Cell);
        for (int i = 0; i < Ns; i++)
            for (int j = 0; j < Nd; j++)
            {
                int k = i * Nd + j;
                if (_level[k] == 0 && _bed[k] == 0 && float.IsNaN(_water[k]))
                    continue;
                for (int a = Math.Max(0, i - r); a <= Math.Min(Ns - 1, i + r); a++)
                    for (int b = Math.Max(0, j - r); b <= Math.Min(Nd - 1, j + r); b++)
                        near[a * Nd + b] = true;
            }
        _apronFor = apron;
        return _apron = near;
    }
}
