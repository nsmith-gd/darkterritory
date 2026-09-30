using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Sim.Route;

/// <summary>Route tiers (GDD §11): you aren't picking a difficulty, you're travelling farther from civilisation.</summary>
public enum RouteTier { Local, Frontier, DeadLines, DeepTerritory }

/// <summary>GDD §18 facility roster.</summary>
public enum FacilityKind { CoalingTower, GrainElevator, Switchyard, Foundry, Slaughterhouse, WreckYard, MineHead, ChemicalWorks, MilitaryDepot }

public enum FeatureKind
{
    /// <summary>Radio dies, voice compresses, no exterior reference (GDD §22).</summary>
    Tunnel,
    /// <summary>A weak bridge has a car limit: your longer, richer train may not clear it.</summary>
    Bridge,
    /// <summary>A freight facility off the main line: the loading set piece (GDD §16–18).</summary>
    Facility,
    /// <summary>A junction with a dead-line branch: what the Switchman throws (App. A.7).</summary>
    Junction,
    /// <summary>Sleepers lying across the rail: placed at generation, not spawned (App. B.2).</summary>
    Sleepers,
    /// <summary>Greased rail: no traction (App. A.2).</summary>
    Grease,
    /// <summary>
    /// A village halt (level-design P1, P20): no freight, no siding; the train stops on the main line and the crew goes
    /// on foot for what the houses hide. Its layout is the feature's <see cref="RouteFeature.Stop"/>.
    /// </summary>
    Village,
    /// <summary>Marsh or contaminated ground under the line: where the Drift is (App. B.4 "terrain region").</summary>
    Marsh,
}

/// <param name="Side">−1 left, +1 right of the direction of travel (facilities, junctions).</param>
/// <param name="MaxCars">For bridges: most cars it will carry, 0 for no limit.</param>
public sealed record RouteFeature(FeatureKind Kind, double Start, double End, int Side = 0, int MaxCars = 0, FacilityKind? Facility = null)
{
    /// <summary>
    /// A facility's own loading modules (T44, by name: "crates", "winch"), set by a designer in <c>dt edit</c>. Unset, it
    /// has what its kind has (facilities.json <c>kinds</c>).
    /// </summary>
    public IReadOnlyList<string>? Modules { get; init; }

    /// <summary>
    /// The stop's layout (docs/design/level-design.md): a facility's yard tracks, sheds and village, or a halt's village,
    /// in the rail frame from <see cref="Start"/>. Null for the coaling tower, and for a hand-laid route without one.
    /// </summary>
    public StopLayout? Stop { get; init; }

    public double Length => End - Start;
    public bool Contains(double s) => s >= Start && s <= End;
}

/// <summary>Tonight's conditions (GDD §22 hazards: they remove tools, they never change enemy rules).</summary>
public sealed record RouteWeather(double FogDensity, bool Wet, double Cold, double Wind);

/// <summary>One night's line: the track, what's on and beside it, the weather, and the dawn deadline.</summary>
public sealed record Route(string Name, RouteTier Tier, ulong Seed, LineDefinition Line, IReadOnlyList<RouteFeature> Features, RouteWeather Weather, double DawnSeconds)
{
    public double Length => Line.Segments.Sum(s => s.Length);
    public IEnumerable<RouteFeature> Of(FeatureKind kind) => Features.Where(f => f.Kind == kind);
    public bool InTunnel(double s) => Features.Any(f => f.Kind == FeatureKind.Tunnel && f.Contains(s));
    public RouteFeature? BridgeAt(double s) => Features.FirstOrDefault(f => f.Kind == FeatureKind.Bridge && f.Contains(s));
    /// <summary>The branches off the main line at its switches, in order along it (GDD §17, App. A.7).</summary>
    public IReadOnlyList<BranchDefinition> Branches { get; init; } = [];
    public RailLine Build()
    {
        var line = Branches.Count == 0 ? new RailLine(Line) : new RailLine(Line, Branches);
        // A generated line brings its land and weather to the rail: the ground, wet rail, brass (linegen plan §12, §14).
        if (Plan is not null)
            line.Conditions = new LineGen.PlanConditions(Plan, line);
        return line;
    }

    /// <summary>
    /// The generated line this route is a projection of (docs/design/linegen-plan.md): terrain, signage, authority and the
    /// route card read it. Null for a hand-laid line. Not saved with a route file: an edited line is hand-laid.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public LineGen.LinePlan? Plan { get; init; }

    /// <summary>Main-line distance of the fortress's outer gate, where the night starts; 0 for route.json's yardLength.</summary>
    public double Gate { get; init; }

    /// <summary>Where the night starts: the outer gate (linegen plan §10.2), or the end of the tuning's yard.</summary>
    public double GateOr(double yardLength) => Gate > 0 ? Gate : yardLength;

    /// <summary>The next feature starting ahead of <paramref name="s"/> (hazards excluded: those you find).</summary>
    public RouteFeature? NextLandmark(double s) =>
        Features.Where(f => f.Start > s && f.Kind is FeatureKind.Tunnel or FeatureKind.Bridge or FeatureKind.Facility or FeatureKind.Junction or FeatureKind.Village)
            .MinBy(f => f.Start);

    /// <summary>Parses "tier:seed" (e.g. "frontier:7").</summary>
    public static (RouteTier Tier, ulong Seed) ParseSpec(string spec)
    {
        var parts = spec.Split(':');
        return (Enum.Parse<RouteTier>(parts[0], ignoreCase: true), parts.Length > 1 ? ulong.Parse(parts[1]) : 1);
    }
}
