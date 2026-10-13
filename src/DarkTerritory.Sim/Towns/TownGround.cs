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

    readonly float[] _level, _bed, _water;
    readonly byte[] _kind;

    public double S0 { get; }
    public double D0 { get; }
    public int Ns { get; }
    public int Nd { get; }

    /// <summary>Whether the town's ground is all at the yard's level (the street town): nothing to raise, lower or wall.</summary>
    public bool Flat { get; }

    static readonly ConditionalWeakTable<TownPlan, TownGround> Cache = [];

    /// <summary>The ground of <paramref name="plan"/> (made once a plan).</summary>
    public static TownGround Of(TownPlan plan) => Cache.GetValue(plan, p => new TownGround(p));

    TownGround(TownPlan plan)
    {
        var b = plan.Bounds;
        if (b is null || plan.Districts.Count == 0 && plan.Ways.All(w => w.Levels is null && !w.Deck))
        {
            Flat = true;
            _level = _bed = _water = [];
            _kind = [];
            return;
        }
        S0 = b.Rear - 2;
        D0 = -b.Left - 2;
        Ns = (int)Math.Ceiling((b.Gate - b.Rear + 4) / Cell);
        Nd = (int)Math.Ceiling((b.Left + b.Right + 4) / Cell);
        int n = Ns * Nd;
        _level = new float[n];
        _bed = new float[n];
        _water = new float[n];
        _kind = new byte[n];
        Array.Fill(_water, float.NaN);
        // Each district over those before it (a terrace inside a terrace is the deeper bench; a hummock out of the marsh).
        foreach (var d in plan.Districts)
        {
            var (s0, s1, d0, d1) = Box(d.Outline);
            for (int i = Math.Max(0, I(s0)); i <= Math.Min(Ns - 1, I(s1)); i++)
                for (int j = Math.Max(0, J(d0)); j <= Math.Min(Nd - 1, J(d1)); j++)
                {
                    double s = S0 + (i + 0.5) * Cell, dd = D0 + (j + 0.5) * Cell;
                    if (!d.Holds(s, dd))
                        continue;
                    int k = i * Nd + j;
                    switch (d.Kind)
                    {
                        case DistrictKind.Water:
                            _water[k] = (float)d.Level;
                            _bed[k] = _level[k] = (float)(d.Level - d.Depth);
                            _kind[k] = 2;
                            break;
                        case DistrictKind.Deck:
                            // Over whatever's under it: the water and its bed stay, the deck is what's stood on.
                            _level[k] = (float)d.Level;
                            if (_kind[k] != 2)
                                _bed[k] = (float)d.Level;
                            _kind[k] = 3;
                            break;
                        default:
                            _level[k] = _bed[k] = (float)d.Level;
                            _water[k] = float.NaN;
                            _kind[k] = 1;
                            break;
                    }
                }
        }
        // The ways: a ramp's cells at its level along it; a deck's at its deck's; a way on the ground keeps the ground's.
        foreach (var w in plan.Ways)
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
    /// The shaped ground as runs of one level along the line (rail frame: a row of cells from S0 to S1 at D, D + 1, its
    /// level): what the art lays the town's own ground from, the deck cells and the water's bed apart.
    /// </summary>
    public IEnumerable<(double S0, double S1, double D, double Level, bool Deck, bool Wet)> Runs(double apron = 0)
    {
        if (Flat)
            yield break;
        // A cell is drawn where it's shaped, or within the apron of a shaped one (the land's own mesh is left out there).
        var near = Apron(apron);
        for (int j = 0; j < Nd; j++)
        {
            int start = -1;
            for (int i = 0; i <= Ns; i++)
            {
                int k = i * Nd + j;
                bool cut = start >= 0 && (i == Ns || !near[k] || Math.Abs(_level[k] - _level[start * Nd + j]) > 1e-3 || _kind[k] != _kind[start * Nd + j]);
                if (cut)
                {
                    int ks = start * Nd + j;
                    yield return (S0 + start * Cell, S0 + i * Cell, D0 + j * Cell, _kind[ks] == 3 ? _level[ks] : _bed[ks], _kind[ks] == 3, _kind[ks] == 2);
                    start = -1;
                }
                if (i < Ns && near[k] && start < 0)
                    start = i;
            }
        }
    }

    /// <summary>
    /// The town's own ground as few rectangles as it takes (rail frame: S0 to S1 along, D0 to D1 across, its level, and
    /// what it is: 1 land, 2 a basin's bed, 3 a deck): every shaped cell and those within <paramref name="apron"/> of one,
    /// grown greedily along the line then across it while the level and kind hold. What the art lays the ground from.
    /// </summary>
    public IReadOnlyList<(double S0, double S1, double D0, double D1, double Level, byte Kind)> Rects(double apron)
    {
        if (Flat)
            return [];
        var near = Apron(apron);
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
