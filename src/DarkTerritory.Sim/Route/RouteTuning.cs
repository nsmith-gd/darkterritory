using System.Text.Json.Serialization;

namespace DarkTerritory.Sim.Route;

/// <summary>Mirror of content/tuning/route.json. Two-element arrays are [min, max] ranges.</summary>
public sealed record RouteTuning(
    double DawnAverageSpeed, double DawnSlack, double NightLengthKm, double YardLength, double TerminusApproach, double NoSpawnFinalApproach,
    double NoSleepersFirst, double PoiZoneHalfLength, double PoiMinSpacing, double PoiMinFromEnds,
    TierTable Tiers, IReadOnlyList<FacilityEntry> Facilities)
{
    public const string File = "tuning/route.json";
    public JunctionTuning Junctions { get; init; } = new();

    /// <summary>
    /// The stops' yards and villages (content/tuning/stops.json), laid out with the route. Without it (a tuning built by
    /// hand) every facility gets route.json's single spur and there are no village halts.
    /// </summary>
    [JsonIgnore]
    public Stops.StopTuning? Stops { get; init; }

    /// <summary>route.json with stops.json alongside it: what every caller that generates routes uses.</summary>
    public static RouteTuning Load(string content) =>
        Ballast.DataFile.Load<RouteTuning>(Path.Combine(content, File)) with
        {
            Stops = Ballast.DataFile.Load<Stops.StopTuning>(Path.Combine(content, DarkTerritory.Sim.Stops.StopTuning.File)),
        };

    /// <summary>What a stop's layout is fitted to: its level zone and this route's switch points.</summary>
    public Stops.StopContext StopContext => new(2 * PoiZoneHalfLength, Junctions.PointsLength);
}

/// <summary>
/// Switches and the branches off them (GDD §17: "switches are thrown by hand"; App. A.7's dead lines). Not in the
/// spec: first-pass numbers for a hand-thrown switch stand and a disused line.
/// </summary>
public sealed record JunctionTuning
{
    /// <summary>The diverging route: a turnout curve off the main line, then straight away from it.</summary>
    public double DivergeRadius { get; init; } = 190;
    public double DivergeLength { get; init; } = 40;
    /// <summary>[min, max] length of a dead line to its buffer stop.</summary>
    public double[] DeadLineLength { get; init; } = [450, 700];
    /// <summary>The switch stand: this far off the centre line at the points, on the branch's side.</summary>
    public double LeverOffset { get; init; } = 2.6;
    public double LeverReach { get; init; } = 2.2;
    /// <summary>Seconds of holding Use to throw it over.</summary>
    public double ThrowSeconds { get; init; } = 1.5;
    /// <summary>A cannonball passing this close to a stand's lever throws it over (note 595); 0, a ball never does.</summary>
    public double ShotReach { get; init; } = 0.6;
    /// <summary>The points won't move with a wheel within this of the toe.</summary>
    public double PointsLength { get; init; } = 12;

    /// <summary>
    /// A facility's spur (GDD §17: "most cannot accommodate a full armoured freight train"): where its points are from
    /// the start of the facility's level zone, a tighter turnout than a dead line's, and its length to the buffer stop.
    /// </summary>
    public double SpurToe { get; init; } = 200;
    public double SpurRadius { get; init; } = 150;
    public double SpurDiverge { get; init; } = 30;
    public double SpurLength { get; init; } = 100;
}

public sealed record TierTable(TierTuning Local, TierTuning Frontier, TierTuning DeadLines, TierTuning DeepTerritory)
{
    public TierTuning this[RouteTier tier] => tier switch
    {
        RouteTier.Local => Local,
        RouteTier.Frontier => Frontier,
        RouteTier.DeadLines => DeadLines,
        _ => DeepTerritory,
    };

    /// <summary>The tightest main-line curve any tier lays: a hand-laid line (the editor, T44) goes no tighter.</summary>
    public double TightestRadius() => new[] { Local, Frontier, DeadLines, DeepTerritory }.Min(t => t.MinRadius);
}

public sealed record TierTuning(
    double[] LengthKm, double MaxGrade, double GradeChance, double MinRadius, double CurveChance,
    int[] Pois, int[] Tunnels, int[] Bridges, double WeakBridgeChance, int[] Junctions,
    double SleepersPerKm, double GreasePerKm, double[] Fog, double WetChance, double[] Cold, double[] Wind);

public sealed record FacilityEntry(FacilityKind Kind, RouteTier FromTier);
