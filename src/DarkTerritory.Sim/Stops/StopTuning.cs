using System.Text.Json;
using System.Text.Json.Serialization;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Stops;

/// <summary>Mirror of content/tuning/stops.json. Field docs live in that file. Two-element arrays are [min, max].</summary>
public sealed record StopTuning
{
    public const string File = "tuning/stops.json";

    public required double CarPitch { get; init; }
    public required double EngineLength { get; init; }
    public required TrackLayoutTuning Track { get; init; }
    public required ShedTuning Shed { get; init; }
    public required HeroTuning Hero { get; init; }
    public required YardCraneTuning Crane { get; init; }
    public required VillageTuning Village { get; init; }
    public required PowerhouseTuning Powerhouse { get; init; }
    public required HoldoutPlacementTuning Holdouts { get; init; }
    public required LairTuning Lairs { get; init; }
    public required DerelictTuning Derelict { get; init; }
    public required DeadTownTuning DeadTown { get; init; }
    public required ScoreTuning Score { get; init; }
    public required int MaxAttempts { get; init; }
    public required StopTierTable Tiers { get; init; }
}

public sealed record TrackLayoutTuning
{
    public required double TurnoutRadius { get; init; }
    public required double Innermost { get; init; }
    public required double Pitch { get; init; }
    public required double FirstToe { get; init; }
    public required double ToeSpacing { get; init; }
    public required double[] FanRadius { get; init; }
    public required double[] FanAngle { get; init; }
    public required double[] SplitToe { get; init; }
}

/// <summary>Derelict cars on a blocked siding (level-design D.1, D.2; ARCHITECTURE §8 note 294). Field docs in stops.json.</summary>
public sealed record DerelictTuning(int[] Cars);
/// <summary>A dead town's railway side (linegen plan §11.3; note 302). Field docs in stops.json.</summary>
public sealed record DeadTownTuning(StationTuning Station, GoodsYardTuning Goods);
public sealed record StationTuning(double[] Size, double Gap, double[] FromLane);
public sealed record GoodsYardTuning(double[] Beyond, double[] Length, double Margin, double Reach, int[] Cars, double[] Car, double CarGap, double[] Shed, double Find);

/// <summary>A yard's powerhouse at its throat (level-design P6, D.2). Field docs in stops.json.</summary>
public sealed record PowerhouseTuning(double[] Size, double[] Throat, double[] Offset);

/// <summary>Where Holdouts go (GDD App. D.4, D.13). Field docs in stops.json.</summary>
public sealed record HoldoutPlacementTuning
{
    public required double[] FacilityDistance { get; init; }
    public required double SecondPadScale { get; init; }
    public required double HaltDistance { get; init; }
    public required double VillageDistance { get; init; }
    public required double VillageLockup { get; init; }
    public required HoldoutApproach Approach { get; init; }
    public required double LoadingClear { get; init; }
    public required double LoadingAngle { get; init; }
    public required double PrisonCar { get; init; }
    public required double Preferred { get; init; }
    public required string[] Prefers { get; init; }
    public required IReadOnlyList<Choice> Shelters { get; init; }
    /// <summary>[length, width] by <see cref="BuildingKind"/> in stops.json's camelCase (looked up, never iterated).</summary>
    public required Dictionary<string, double[]> Sizes { get; init; }
    public required double Siding { get; init; }
    public required double[] ByTrack { get; init; }
    public required double[] LockupBehind { get; init; }
    public required int Candidates { get; init; }
    public required double WalkCell { get; init; }

    public double[] Size(BuildingKind kind) =>
        Sizes.TryGetValue(char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..], out var v) ? v : throw new KeyNotFoundException($"stops.json holdouts.sizes has no {kind}");
}

public sealed record HoldoutApproach(double Facility, double Halt);

/// <summary>Where the outside creatures live (GDD B.6, B.8). Field docs in stops.json.</summary>
public sealed record LairTuning
{
    public required WarrenTuning Warren { get; init; }
    public required FollowerGroundTuning Followers { get; init; }
    public required double[] SootCall { get; init; }
    public required double[] WhistlerNest { get; init; }
    /// <summary>How far along the line from where the train stands the nest may be; 0 anywhere in the zone (note 314).</summary>
    public double WhistlerNestAlong { get; init; }
}

public sealed record WarrenTuning(double Radius, double Clear, double FromTrain, double FromMain);
public sealed record FollowerGroundTuning(double Radius, int Most);

public sealed record ShedTuning
{
    public required double Depth { get; init; }
    public required double Gap { get; init; }
    public required int[] PerRow { get; init; }
    public required double MinLength { get; init; }
    public required double[] Trim { get; init; }
}

public sealed record HeroTuning
{
    public required int[] Count { get; init; }
    public required double[] Length { get; init; }
    public required double Offset { get; init; }
}

public sealed record YardCraneTuning
{
    public required double Reach { get; init; }
    public required double BayEvery { get; init; }
    public required double BayInset { get; init; }
}

public sealed record VillageTuning
{
    public required double YawJitter { get; init; }
    public required double RoadReach { get; init; }
    public required double HouseGap { get; init; }
    public required int[] Outliers { get; init; }
    public required double OutlierFindBonus { get; init; }
    public required double SecondFindChance { get; init; }
    public required int[] Outbuildings { get; init; }
    public required double WellChance { get; init; }
    public required double SetbackExtra { get; init; }
    public required double[] SetbackRange { get; init; }
    public required BlocksTuning Blocks { get; init; }
    public required StreetTuning Street { get; init; }
    public required CrossroadsTuning Crossroads { get; init; }
    public required FarmsteadsTuning Farmsteads { get; init; }
    public required double[] Extent { get; init; }
    public required HaltTuning Halt { get; init; }
    /// <summary>Whether the plain houses (a rectangle or a square) stand open to walk into and search (note 326).</summary>
    public bool OpenHouses { get; init; }
    /// <summary>Whether the barns, outbuildings and a dead town's goods shed stand open to walk into, their finds inside to search (note 417).</summary>
    public bool OpenSheds { get; init; }
}

public sealed record BlocksTuning
{
    public required double[] Depth { get; init; }
    public required double[] Length { get; init; }
    public required double TwoColumns { get; init; }
    public required double KeepFrontage { get; init; }
    public required double KeepEdge { get; init; }
    public required double KeepInner { get; init; }
}

public sealed record StreetTuning
{
    public required double[] Length { get; init; }
    public required double Bend { get; init; }
    public required double[] Every { get; init; }
    public required double[] Offset { get; init; }
    public required double Skip { get; init; }
    public required double LaneChance { get; init; }
    public required double[] Lane { get; init; }
}

public sealed record CrossroadsTuning
{
    public required double[] Out { get; init; }
    public required double[] Arm { get; init; }
    public required double[] Beyond { get; init; }
    public required int[] Houses { get; init; }
    public required double[] Radius { get; init; }
}

public sealed record FarmsteadsTuning
{
    public required double[] Length { get; init; }
    public required int[] Farms { get; init; }
    public required double[] Drive { get; init; }
    public required double Wiggle { get; init; }
}

public sealed record HaltTuning
{
    public required double Length { get; init; }
    public required double Offset { get; init; }
}

public sealed record ScoreTuning
{
    public required double Throw { get; init; }
    public required double Coupling { get; init; }
    public required double Reversal { get; init; }
    public required double BlindMove { get; init; }
    public required double Respot { get; init; }
    public required double HandCar { get; init; }
    public required double MetresPerPoint { get; init; }
    public required double BlockedCrossing { get; init; }
    public required double Clearance { get; init; }
    public required double VillageMetresPerPoint { get; init; }
    public required double FindOdds { get; init; }
    public required double PerHouse { get; init; }
    public required double OffsetPerPoint { get; init; }
    public required double VillageWeight { get; init; }
    public required double WalkFactor { get; init; }
    public required double CraneBayLoad { get; init; }
    public required double CrateStackLoad { get; init; }
    public required double StrongroomLoad { get; init; }
    public required double CarryMetresPerPoint { get; init; }
    public required double HeavyCarry { get; init; }
    public required double LowPower { get; init; }
    public required double DeadPower { get; init; }
    public required double HardPull { get; init; }
    public required double HardPullFrom { get; init; }
}

public sealed record StopTierTable(StopTier Local, StopTier Frontier, StopTier DeadLines, StopTier DeepTerritory)
{
    public StopTier this[RouteTier tier] => tier switch
    {
        RouteTier.Local => Local,
        RouteTier.Frontier => Frontier,
        RouteTier.DeadLines => DeadLines,
        _ => DeepTerritory,
    };
}

/// <summary>One tier's difficulty levers (level-design D.2).</summary>
public sealed record StopTier
{
    /// <summary>Ribbit warrens per yard or village (GDD B.6).</summary>
    public required int[] Warrens { get; init; }
    /// <summary>The yard's power, weighted (level-design D.2), and the grade out of it (%), up.</summary>
    public required IReadOnlyList<Choice> Power { get; init; }
    public required double[] ExitGrade { get; init; }
    public required int Empties { get; init; }
    public required double VillageChance { get; init; }
    public required int[] Halts { get; init; }
    public required IReadOnlyList<Choice> Forms { get; init; }
    public required int[] Sidings { get; init; }
    /// <summary>Sidings with derelict cars standing on them, [min, max] (level-design D.2).</summary>
    public required int[] Blocked { get; init; }
    public required int[] FaceCars { get; init; }
    public required double Crane { get; init; }
    public required double[] Runway { get; init; }
    public required double[] Stock { get; init; }
    /// <summary>
    /// Note 352 (the director, 8 Oct 2026: "loot should spawn in all yard lines in early game and mid game"): every yard
    /// track has something to load beside its loading face. Unset, the stock lies where the dice put it, and a siding can be bare.
    /// </summary>
    public bool EveryTrack { get; init; }
    public required IReadOnlyList<Choice> Villages { get; init; }
    public required IReadOnlyList<Choice> Arrangements { get; init; }
    public required double[] VillageOffset { get; init; }
    public required double CrossingReach { get; init; }
    public required bool CrossingClear { get; init; }
    public required double Buffer { get; init; }
    public required double Find { get; init; }
    public required double[] Stub { get; init; }
    public required DifficultyBand Band { get; init; }
}

public sealed record DifficultyBand(double[] Yard, double[] Village);

/// <summary>A weighted choice, written in JSON as <c>["name", weight]</c>.</summary>
[JsonConverter(typeof(ChoiceConverter))]
public sealed record Choice(string Name, double Weight)
{
    /// <summary>Picks from weighted choices (the weights sum to 1; the last takes any remainder).</summary>
    public static T Pick<T>(IReadOnlyList<Choice> choices, double r) where T : struct, Enum
    {
        foreach (var c in choices)
        {
            if (r < c.Weight)
                return Enum.Parse<T>(c.Name, ignoreCase: true);
            r -= c.Weight;
        }
        return Enum.Parse<T>(choices[^1].Name, ignoreCase: true);
    }
}

sealed class ChoiceConverter : JsonConverter<Choice>
{
    public override Choice Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("a choice is [\"name\", weight]");
        reader.Read();
        string name = reader.GetString() ?? throw new JsonException("a choice needs a name");
        reader.Read();
        double weight = reader.GetDouble();
        reader.Read();
        if (reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException("a choice is [\"name\", weight]");
        return new Choice(name, weight);
    }

    public override void Write(Utf8JsonWriter writer, Choice value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteStringValue(value.Name);
        writer.WriteNumberValue(value.Weight);
        writer.WriteEndArray();
    }
}
