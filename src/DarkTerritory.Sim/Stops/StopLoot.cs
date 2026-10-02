using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Stops;

/// <summary>Mirror of content/tuning/loot.json. Field docs live in that file.</summary>
public sealed record LootTuning
{
    public const string File = "tuning/loot.json";

    public required YardLootTuning Yard { get; init; }
    public required double[] VillageBudget { get; init; }
    public required double OutlierWeight { get; init; }
    public required double SettleSeconds { get; init; }
    public required double Radius { get; init; }
    /// <summary>By container kind, in loot.json's camelCase (looked up, never iterated).</summary>
    public required Dictionary<string, LootKindTuning> Kinds { get; init; }
    /// <summary>Item names for the HUD, by item key.</summary>
    public required Dictionary<string, string> Items { get; init; }
    /// <summary>
    /// The chance a container at a stop has a repair kit in it too (GDD v1.4 App. E.12 question 4: kits are found as well
    /// as bought): every village container and yard crate stack and strongroom, each rolled on its own stream.
    /// </summary>
    public double RepairKitChance { get; init; }

    public LootKindTuning Of(ContainerKind kind) =>
        Kinds.TryGetValue(char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..], out var k) ? k : throw new KeyNotFoundException($"loot.json has no kind {kind}");

    public string Name(string item) => Items.TryGetValue(item, out var n) ? n : item;
}

public sealed record YardLootTuning
{
    public required CrateStackLoot CrateStack { get; init; }
    public required CraneBayLoot CraneBay { get; init; }
    public required StrongroomLoot Strongroom { get; init; }
}

public sealed record CrateStackLoot { public required int[] Crates { get; init; } }
public sealed record CraneBayLoot { public required int Castings { get; init; } }
public sealed record StrongroomLoot { public required int Heavy { get; init; } }

public sealed record LootKindTuning
{
    public required double Weight { get; init; }
    public required string[] Items { get; init; }
}

/// <summary>A village find (P12, P14): which container it's in, what it is, and what it pays in scrip.</summary>
public readonly record struct LootFind(int Container, string Item, double Value);

/// <summary>
/// The economy's half of P14: turns a stop's village containers into finds. The layout decides where; this decides what
/// and how much, from the run's seed, so every machine agrees and the economy can be tuned without touching a layout.
/// </summary>
public static class StopLoot
{
    /// <param name="perCar">The tier's contract value per delivered car (run.json economy.perCar): the budget's unit.</param>
    public static IReadOnlyList<LootFind> Village(LootTuning t, StopLayout stop, ulong routeSeed, int feature, double perCar)
    {
        var rng = new Ballast.Pcg32(StopSeed.Of(StopSeed.Of(routeSeed, StopSeed.Loot, (ulong)feature), stop.Seed));
        var containers = stop.Containers.Where(c => c.Zone == StopZone.Village).ToList();
        if (containers.Count == 0)
            return [];
        double budget = perCar * rng.Range(t.VillageBudget[0], t.VillageBudget[1]);
        double Weight(StopContainer c) => t.Of(c.Kind).Weight * (c.Outlier ? t.OutlierWeight : 1);
        double sum = containers.Sum(Weight);
        return [.. containers.Select(c =>
        {
            var kind = t.Of(c.Kind);
            string item = kind.Items[(int)(rng.NextDouble() * kind.Items.Length)];
            return new LootFind(c.Index, item, Math.Max(5, Math.Round(budget * Weight(c) / sum / 5) * 5));
        })];
    }

    /// <summary>
    /// The containers at a stop with a repair kit in them besides what they hold (E.12 question 4), from the run's seed on
    /// a stream of its own, so the finds and crates the economy already deals out are the same with or without them.
    /// </summary>
    public static IReadOnlyList<int> Kits(LootTuning t, StopLayout stop, ulong routeSeed, int feature)
    {
        if (t.RepairKitChance <= 0)
            return [];
        var rng = new Ballast.Pcg32(StopSeed.Of(StopSeed.Of(routeSeed, StopSeed.Kit, (ulong)feature), stop.Seed));
        return [.. stop.Containers.Where(c => c.Kind != ContainerKind.CraneBay).OrderBy(c => c.Index)
            .Where(c => rng.NextDouble() < t.RepairKitChance).Select(c => c.Index)];
    }

    /// <summary>How many cargo crates a yard's crate stack holds (P14: the layout says a stack, the run says how many).</summary>
    public static int CratesIn(LootTuning t, StopLayout stop, ulong routeSeed, int feature, StopContainer c)
    {
        var rng = new Ballast.Pcg32(StopSeed.Of(StopSeed.Of(routeSeed, StopSeed.Loot, (ulong)feature), stop.Seed ^ (ulong)c.Index * 0x9E3779B9UL));
        return rng.RangeInclusive(t.Yard.CrateStack.Crates[0], t.Yard.CrateStack.Crates[1]);
    }

    /// <summary>The tier key run.json's economy prices by.</summary>
    public static string TierKey(RouteTier tier) => char.ToLowerInvariant(tier.ToString()[0]) + tier.ToString()[1..];
}
