using Ballast;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// A generated line's conditions as the rail model asks for them: the terrain field's ground, wet rail's adhesion where
/// the night rains (worse where the track is wet-biased, §14), and the drag of brass growth across the rail above cutting
/// speed (§7.2, GDD §22 "cut through slowly or ram it and pay"). Host and clients build it from the same plan.
/// </summary>
public sealed class PlanConditions : ITrackConditions
{
    readonly LinePlan _plan;
    readonly RailLine _line;
    readonly (int Path, double S0, double S1, double Adhesion)[] _wet;
    readonly (string Edge, double S0, double S1)[] _brass;
    // Each edge's fog factors in order along it, for Fog's binary search (it's asked a few hundred times a frame).
    readonly Dictionary<string, (double S0, double S1, double Fog)[]> _fog;

    public PlanConditions(LinePlan plan, RailLine line)
    {
        _plan = plan;
        _line = line;
        Terrain = new TerrainField(plan, line, plan.Rules.Terrain);
        var r = plan.Rules;
        _wet = plan.Weather.Rain
            ? [.. plan.Exposure.Select(x => (PathOf(x.Edge), x.S0, x.S1, x.Why.Contains("wet") ? r.WetBiasAdhesion : r.WetAdhesion))]
            : [];
        _brass = [.. plan.Structures.Where(s => s.Type == StructureType.BrassField).Select(s => (s.Edge, s.S0, s.S1))];
        _fog = plan.Exposure.GroupBy(x => x.Edge, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.S0).Select(x => (x.S0, x.S1, x.Fog)).ToArray(), StringComparer.Ordinal);
    }

    public TerrainField Terrain { get; }

    int PathOf(string edge) => edge == "main" ? RailLine.MainPath : _plan.Edge(edge).Branch;

    (string Edge, double S) Locate(int path, double distance) => TrackRules.Locate(_plan, _line, path, distance);

    public double Ground(Double3 world) => Terrain.Ground(world);

    public double Adhesion(int path, double distance)
    {
        if (_wet.Length == 0)
            return 1;
        var (edge, s) = Locate(path, distance);
        int p = PathOf(edge);
        foreach (var w in _wet)
            if (w.Path == p && s >= w.S0 && s < w.S1)
                return w.Adhesion;
        return _plan.Rules.WetAdhesion;
    }

    /// <summary>The plan's exposure along the line (§14): its cold step and wind where the track is, or none off it.</summary>
    PlanExposure? ExposureAt(int path, double distance)
    {
        var (edge, s) = Locate(path, distance);
        foreach (var x in _plan.Exposure)
            if (x.Edge == edge && s >= x.S0 && s < x.S1)
                return x;
        return null;
    }

    /// <summary>The night's cold step (§14's temperature) plus this stretch's own (climbed high, or exposed).</summary>
    public int ColdStep(int path, double distance) => _plan.Weather.TempStep + (ExposureAt(path, distance)?.ColdStep ?? 0);

    /// <summary>The night's wind (0 to 1) times this stretch's exposure (×1.5 where exposed).</summary>
    public double Wind(int path, double distance) => _plan.Weather.Wind * (ExposureAt(path, distance)?.Wind ?? 1);

    /// <summary>
    /// §14's fog factor there, averaged over the weather's fogBlendM either side (note 313): low ground's fog comes up
    /// round the train over a few hundred metres and drains away again. Off the plan's track, the night's own (1).
    /// </summary>
    public double Fog(int path, double distance)
    {
        double blend = _plan.Rules.FogBlendM;
        var (edge, s) = Locate(path, distance);
        if (!_plan.Rules.FogAlongLine || !_fog.TryGetValue(edge, out var runs))
            return 1;
        if (blend <= 0)
            return FogOn(runs, s);
        const int Samples = 9;
        double sum = 0;
        for (int i = 0; i < Samples; i++)
            sum += FogOn(runs, s + blend * (2.0 * i / (Samples - 1) - 1));
        return sum / Samples;
    }

    static double FogOn((double S0, double S1, double Fog)[] runs, double s)
    {
        int lo = 0, hi = runs.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (s < runs[mid].S0)
                hi = mid - 1;
            else if (s >= runs[mid].S1)
                lo = mid + 1;
            else
                return runs[mid].Fog;
        }
        return 1;
    }

    public double Drag(int path, double distance, double speed)
    {
        if (_brass.Length == 0 || speed <= _plan.Rules.BrassCuttingSpeed)
            return 0;
        var (edge, s) = Locate(path, distance);
        foreach (var b in _brass)
            if (b.Edge == edge && s >= b.S0 && s <= b.S1)
                return _plan.Rules.BrassDrag;
        return 0;
    }
}
