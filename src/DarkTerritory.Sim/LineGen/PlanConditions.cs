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
