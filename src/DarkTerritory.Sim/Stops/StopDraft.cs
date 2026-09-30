namespace DarkTerritory.Sim.Stops;

/// <summary>Where a footprint may go: clear of other buildings, track and roads, out of the rail buffer, inside the stop.</summary>
readonly record struct Fit(double Gap = 2, double Rail = 4, double Road = 2.5, double Buffer = 0, double SMin = double.MinValue, double SMax = double.MaxValue);

/// <summary>A stop's layout while it's being built: everything placed so far, and what's in the way.</summary>
sealed class StopDraft(double zoneLength, double maxLateral)
{
    public readonly double ZoneLength = zoneLength;
    public readonly double MaxLateral = maxLateral;
    public readonly List<StopBuilding> Buildings = [];
    public readonly List<StopRoad> Roads = [];
    public readonly List<YardTrack> Tracks = [];
    public readonly List<StopContainer> Containers = [];
    // Yard track and roads as points every couple of metres, bucketed for the placement tests. The main line is the
    // line D = 0 and needs no points.
    readonly PointGrid _rail = new(), _road = new();
    public IReadOnlyList<Pt> RailPoints => _rail.All;
    public IReadOnlyList<Pt> RoadPoints => _road.All;
    /// <summary>False once something it needed couldn't be placed (fewer than three houses): an attempt to reroll.</summary>
    public bool Valid = true;

    public void AddRail(IReadOnlyList<Pt> pts) => _rail.AddRange(Plan.Samples(pts));

    public StopRoad AddRoad(RoadKind kind, IReadOnlyList<Pt> pts)
    {
        var road = new StopRoad(kind, pts);
        Roads.Add(road);
        _road.AddRange(Plan.Samples(pts));
        return road;
    }

    public int Add(StopBuilding b)
    {
        Buildings.Add(b);
        return Buildings.Count - 1;
    }

    public void Contain(ContainerKind kind, StopZone zone, Pt at, int band, int depth, int building, int track = -1, bool outlier = false, bool floor = false) =>
        Containers.Add(new StopContainer(Containers.Count, kind, zone, at, band, depth, building, track, outlier, floor));

    public double RoadDistance(Pt p)
    {
        double m = double.MaxValue;
        foreach (var r in Roads)
            m = Math.Min(m, Plan.PolylineDistance(p, r.Points));
        return m;
    }

    public bool InStop(StopBuilding b) =>
        Plan.Corners(b).All(p => p.S >= 2 && p.S <= ZoneLength - 2 && Math.Abs(p.D) <= MaxLateral);

    public bool Fits(StopBuilding b, in Fit f)
    {
        if (!InStop(b))
            return false;
        foreach (var p in Plan.Corners(b))
            if (Math.Abs(p.D) < f.Buffer || p.S > f.SMax || p.S < f.SMin)
                return false;
        foreach (var o in Buildings)
            if (Plan.Overlap(b, o, f.Gap))
                return false;
        if (NearMain(b, f.Rail) || _rail.Any(b, f.Rail))
            return false;
        return f.Road < 0 || !_road.Any(b, f.Road);
    }

    /// <summary>A footprint within <paramref name="pad"/> of the main line (the line D = 0).</summary>
    public static bool NearMain(StopBuilding b, double pad)
    {
        var c = Plan.Corners(b);
        return c.Min(p => p.D) <= pad && c.Max(p => p.D) >= -pad;
    }

    public bool TouchesRail(StopBuilding b, double pad) => NearMain(b, pad) || _rail.Any(b, pad);
    public bool TouchesRoad(StopBuilding b, double pad) => _road.Any(b, pad);
    /// <summary>Yard track (not the main line) within <paramref name="pad"/> of a footprint.</summary>
    public bool NearYardTrack(StopBuilding b, double pad) => _rail.Any(b, pad);

    /// <summary>A polyline that runs clear of every building placed so far.</summary>
    public bool Clear(IReadOnlyList<Pt> pts, double pad) =>
        !Plan.Samples(pts, 1.5).Any(p => Buildings.Any(b => Plan.Inside(p, b, pad)));

    public Pt Clamp(Pt p, double sMin, double sMax) =>
        new(Math.Clamp(p.S, sMin, sMax), Math.Clamp(p.D, -MaxLateral + 8, MaxLateral - 8));
}

/// <summary>Points bucketed in 8 m cells, for "is anything inside this footprint" (lookups only, so order never matters).</summary>
sealed class PointGrid
{
    const double Cell = 8;
    readonly Dictionary<long, List<Pt>> _cells = [];
    readonly List<Pt> _all = [];
    public IReadOnlyList<Pt> All => _all;

    static long Key(int i, int j) => ((long)i << 32) ^ (uint)j;

    public void AddRange(IEnumerable<Pt> pts)
    {
        foreach (var p in pts)
        {
            _all.Add(p);
            long k = Key((int)Math.Floor(p.S / Cell), (int)Math.Floor(p.D / Cell));
            if (!_cells.TryGetValue(k, out var list))
                _cells[k] = list = [];
            list.Add(p);
        }
    }

    /// <summary>Any point inside the footprint grown by <paramref name="pad"/>.</summary>
    public bool Any(StopBuilding b, double pad)
    {
        var c = Plan.Corners(b, pad);
        int i0 = (int)Math.Floor(c.Min(p => p.S) / Cell), i1 = (int)Math.Floor(c.Max(p => p.S) / Cell);
        int j0 = (int)Math.Floor(c.Min(p => p.D) / Cell), j1 = (int)Math.Floor(c.Max(p => p.D) / Cell);
        for (int i = i0; i <= i1; i++)
            for (int j = j0; j <= j1; j++)
                if (_cells.TryGetValue(Key(i, j), out var list))
                    foreach (var p in list)
                        if (Plan.Inside(p, b, pad))
                            return true;
        return false;
    }
}
