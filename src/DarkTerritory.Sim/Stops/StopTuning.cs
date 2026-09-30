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
    public required int Empties { get; init; }
    public required double VillageChance { get; init; }
    public required int[] Halts { get; init; }
    public required IReadOnlyList<Choice> Forms { get; init; }
    public required int[] Sidings { get; init; }
    public required int[] FaceCars { get; init; }
    public required double Crane { get; init; }
    public required double[] Runway { get; init; }
    public required double[] Stock { get; init; }
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
