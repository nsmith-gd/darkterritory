using Ballast;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.Net;

/// <summary>
/// One of the combination audit's hazard sets (GDD §34 "every pair and triple in the roster against every hazard set";
/// note 186): what tonight's line takes away (§22 "hazards remove your tools. They never change enemy rules"), laid over
/// whatever line the night runs. Mirror of an entry in content/tuning/balance.json <c>combinations.hazardSets</c>.
/// </summary>
/// <param name="Adhesion">Wet rail (§22): the rail's adhesion everywhere is at most this (1 dry). Also fouls the guns faster (note 183).</param>
/// <param name="ColdStep">Deep cold (§22): added to the line's cold step everywhere (note 183).</param>
/// <param name="Wind">Wind (§22): the night's wind is at least this (gunfire carries further, note 183).</param>
/// <param name="LampsOut">Lights fail (§23): the forward lamp smashed and every car's lamp out from the start.</param>
public sealed record HazardSet(string Name, double Adhesion = 1, int ColdStep = 0, double Wind = 0, bool LampsOut = false)
{
    public static readonly HazardSet Clear = new("clear");
    public bool Any => Adhesion < 1 || ColdStep > 0 || Wind > 0 || LampsOut;
}

/// <summary>A line's own conditions with a <see cref="HazardSet"/> over them: the rail model asks this instead.</summary>
public sealed class HazardConditions(RailLine line, ITrackConditions? inner, HazardSet set) : ITrackConditions
{
    double _hint;

    public ITrackConditions? Inner { get; } = inner;
    public HazardSet Set { get; } = set;

    /// <summary>The line's own ground, or (a hand-laid line has none) the nearest track's height, as the motor would take it.</summary>
    public double Ground(Double3 world)
    {
        if (Inner is { } c)
            return c.Ground(world);
        var (path, along) = line.Nearest(world, ref _hint);
        return line.Sample(path, along).Position.Y;
    }
    public double Adhesion(int path, double distance) => Math.Min(Inner?.Adhesion(path, distance) ?? 1, Set.Adhesion);
    public double Drag(int path, double distance, double speed) => Inner?.Drag(path, distance, speed) ?? 0;
    public int ColdStep(int path, double distance) => (Inner?.ColdStep(path, distance) ?? 0) + Set.ColdStep;
    public double Wind(int path, double distance) => Math.Max(Inner?.Wind(path, distance) ?? 0, Set.Wind);

    /// <summary>Lays the set over the line (once: the host and every client share the harness's line).</summary>
    public static void Apply(RailLine line, HazardSet set)
    {
        if (set.Any && line.Conditions is not HazardConditions)
            line.Conditions = new HazardConditions(line, line.Conditions, set);
    }
}
