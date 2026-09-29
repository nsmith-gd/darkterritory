using System.Text.Json.Serialization;

namespace DarkTerritory.Sim.Route;

/// <summary>Mirror of content/tuning/route.json. Two-element arrays are [min, max] ranges.</summary>
public sealed record RouteTuning(
    double DawnAverageSpeed, double DawnSlack, double YardLength, double TerminusApproach, double NoSpawnFinalApproach,
    double NoSleepersFirst, double PoiZoneHalfLength, double PoiMinSpacing, double PoiMinFromEnds,
    TierTable Tiers, IReadOnlyList<FacilityEntry> Facilities)
{
    public const string File = "tuning/route.json";
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
}

public sealed record TierTuning(
    double[] LengthKm, double MaxGrade, double GradeChance, double MinRadius, double CurveChance,
    int[] Pois, int[] Tunnels, int[] Bridges, double WeakBridgeChance, int[] Junctions,
    double SleepersPerKm, double GreasePerKm, double[] Fog, double WetChance, double[] Cold, double[] Wind);

public sealed record FacilityEntry(FacilityKind Kind, RouteTier FromTier);
