using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Stops;

/// <summary>Mirror of content/tuning/loot.json. Field docs live in that file.</summary>
public sealed record LootTuning
{
    public const string File = "tuning/loot.json";

    public required YardLootTuning Yard { get; init; }
    /// <summary>
    /// What each kind of find is worth, in the tier's value per car (run.json economy.perCar), by item key: always the same
    /// for a kind (note 718: the director, 14 Oct 2026, "as they play the game more often, they learn what those objects
    /// are"). Looked up, never iterated. A kind not listed is worth the least a find is (<see cref="LeastWorth"/>).
    /// </summary>
    public Dictionary<string, double> Worth { get; init; } = new();
    /// <summary>
    /// The village finds' worth by tier key, times <see cref="Worth"/> (note 718: the deep tiers' fewer houses keep worthier
    /// things); 1 where unlisted. Not the comet fragment's, nor the trophies'. Looked up, never iterated.
    /// </summary>
    public Dictionary<string, double> FindsByTier { get; init; } = new();
    /// <summary>An outlier house's container (level-design P11) draws its find this many times and keeps the worthiest (note 718).</summary>
    public int OutlierDraws { get; init; } = 1;
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
    /// <summary>
    /// How far out along the main line (m) a stop's loot, and its facility's crates, come out (the director, 8 Oct 2026:
    /// he watched it appear after he'd stopped; note 352). 0: when the train first stops there.
    /// </summary>
    public double StockAhead { get; init; }
    /// <summary>Toys found at the stops (note 264: none ride from the fortress now). Unset, none.</summary>
    public ToyLootTuning? Toys { get; init; }
    /// <summary>The finds that heal when used (GDD App. F.1, the damage model; note 272). Unset, none do.</summary>
    public HealingTuning? Healing { get; init; }
    /// <summary>The held search of an open house's hiding spots (GDD App. F.3; note 326). Unset, the finds lie out.</summary>
    public SearchTuning? Search { get; init; }
    /// <summary>A creature's trophy when it's killed (the Gannet's head, note 340), by the kind's tuning name. Unset, none.</summary>
    public Dictionary<string, TrophyTuning> Trophies { get; init; } = new();
    /// <summary>Comet fragments at the stops (note 718). Unset, none.</summary>
    public CometLootTuning? Comet { get; init; }
    /// <summary>What the clerk's tally says of the night's finds (note 718).</summary>
    public LootTallyTuning Tally { get; init; } = new();

    /// <summary>The least a find is worth, in scrip: finds are rounded to fives, and none is worth nothing.</summary>
    public const double LeastWorth = 5;

    /// <summary>
    /// What a find of <paramref name="item"/> pays at the tier's <paramref name="perCar"/> (note 718): its kind's worth, rounded
    /// to fives, the same every time.
    /// </summary>
    public double WorthOf(string item, double perCar) =>
        Math.Max(LeastWorth, Math.Round(Worth.GetValueOrDefault(item) * perCar / LeastWorth) * LeastWorth);

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

/// <summary>loot.json <c>toys</c> (note 264): a toy's chance in each village container of <see cref="Kinds"/>, and what it sounds like.</summary>
public sealed record ToyLootTuning
{
    public double Chance { get; init; }
    /// <summary>The container kinds a toy turns up in, in loot.json's camelCase.</summary>
    public string[] Kinds { get; init; } = [];
    /// <summary>Each toy's noise drawn evenly from these (App. C item 4: most quiet, some not).</summary>
    public Physics.ToyNoise[] Noises { get; init; } = [];
}

/// <summary>loot.json <c>healing</c> (GDD App. F.1: "healing items are rare loot"; note 272). Field docs live in that file.</summary>
public sealed record HealingTuning
{
    public double UseSeconds { get; init; } = 2;
    /// <summary>Health each healing find gives back, by item key (looked up, never iterated).</summary>
    public Dictionary<string, int> Heals { get; init; } = [];
    public int BotBelow { get; init; } = 50;

    public int Of(string item) => Heals.GetValueOrDefault(item);
}

/// <summary>loot.json <c>search</c> (GDD App. F.3; note 326): how near a hiding spot, and how long held, to search it.</summary>
public sealed record SearchTuning
{
    public double Reach { get; init; } = 1;
    /// <summary>Seconds held to search a spot, by container kind in loot.json's camelCase (looked up, never iterated).</summary>
    public Dictionary<string, double> Seconds { get; init; } = [];

    /// <summary>The seconds a kind of spot takes, or null if it's never searched (it lies out).</summary>
    public double? Of(ContainerKind kind) =>
        Seconds.TryGetValue(char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..], out var s) ? s : null;
}

public sealed record LootKindTuning
{
    public required string[] Items { get; init; }
}

/// <summary>
/// loot.json <c>comet</c> (note 718; the director, 14 Oct 2026: comet cargo "needs to be at actual stops"): each stop's chance
/// of a comet fragment or few, by tier key; how many; the find it is (a body of a find's <see cref="LootTuning.Radius"/>).
/// </summary>
public sealed record CometLootTuning
{
    public string Item { get; init; } = "cometFragment";
    /// <summary>By tier key (run.json's), looked up, never iterated.</summary>
    public Dictionary<string, double> Chance { get; init; } = new();
    public int[] Count { get; init; } = [1, 1];
}

/// <summary>loot.json <c>tally</c> (note 718): how many kinds of the night's finds the clerk names, and whether the finder's line is read.</summary>
public sealed record LootTallyTuning(int Best = 3, bool Finder = true);

/// <summary>
/// A village find (P12, P14): which container it's in, what it is, and what it pays in scrip (its kind's worth: note 718).
/// </summary>
public readonly record struct LootFind(int Container, string Item, double Value);

/// <summary>
/// The economy's half of P14: turns a stop's village containers into finds. The layout decides where; this decides what
/// and how much, from the run's seed, so every machine agrees and the economy can be tuned without touching a layout.
/// </summary>
public static class StopLoot
{
    /// <param name="perCar">The tier's contract value per delivered car (run.json economy.perCar): the unit of a find's worth.</param>
    public static IReadOnlyList<LootFind> Village(LootTuning t, StopLayout stop, ulong routeSeed, int feature, double perCar)
    {
        var rng = new Ballast.Pcg32(StopSeed.Of(StopSeed.Of(routeSeed, StopSeed.Loot, (ulong)feature), stop.Seed));
        var containers = stop.Containers.Where(c => c.Zone == StopZone.Village).ToList();
        if (containers.Count == 0)
            return [];
        // The stream's first draw was the stop's budget, which a find's worth no longer comes from (note 718): kept, so the
        // finds a seed deals are the ones it always dealt.
        rng.NextDouble();
        // An outlier's other draws (P11), on a stream of their own for the same reason.
        var outlier = new Ballast.Pcg32(StopSeed.Of(StopSeed.Of(routeSeed, StopSeed.Outlier, (ulong)feature), stop.Seed));
        return [.. containers.Select(c =>
        {
            var kind = t.Of(c.Kind);
            string item = kind.Items[(int)(rng.NextDouble() * kind.Items.Length)];
            for (int draw = 1; c.Outlier && draw < t.OutlierDraws; draw++)
            {
                string other = kind.Items[(int)(outlier.NextDouble() * kind.Items.Length)];
                if (t.WorthOf(other, perCar) > t.WorthOf(item, perCar))
                    item = other;
            }
            return new LootFind(c.Index, item, t.WorthOf(item, perCar));
        })];
    }

    /// <summary>
    /// The containers at a stop a comet fragment lies beside (note 718), one per fragment: on some nights' stops (loot.json
    /// <c>comet</c>'s chance by tier), from the run's seed on a stream of its own, so every machine agrees and nothing else
    /// a seed deals changes. Only beside a container (not a crane's bay) with somewhere in the open by it to be seen from the
    /// line (<paramref name="lyingOut"/>; the run's <c>CometAt</c>).
    /// </summary>
    public static IReadOnlyList<int> Comets(LootTuning t, StopLayout stop, ulong routeSeed, int feature, RouteTier tier, Func<StopContainer, bool> lyingOut)
    {
        if (t.Comet is not { } comet || comet.Chance.GetValueOrDefault(TierKey(tier)) is not (> 0 and var chance))
            return [];
        var rng = new Ballast.Pcg32(StopSeed.Of(StopSeed.Of(routeSeed, StopSeed.Comet, (ulong)feature), stop.Seed));
        if (rng.NextDouble() >= chance)
            return [];
        var places = stop.Containers.OrderBy(c => c.Index).Where(c => c.Kind != ContainerKind.CraneBay && lyingOut(c)).Select(c => c.Index).ToList();
        int n = Math.Min(places.Count, rng.RangeInclusive(comet.Count[0], comet.Count[^1]));
        var at = new List<int>();
        for (int i = 0; i < n; i++)
        {
            int pick = Math.Min(places.Count - 1, (int)(rng.NextDouble() * places.Count));
            at.Add(places[pick]);
            places.RemoveAt(pick);
        }
        return at;
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

    /// <summary>
    /// The village containers at a stop with a toy in them besides what they hold (note 264; GDD §19 hand-carried loot), and
    /// each toy's noise: from the run's seed on a stream of its own, so every machine agrees and the finds are unchanged.
    /// </summary>
    public static IReadOnlyList<(int Container, Physics.ToyNoise Noise)> Toys(LootTuning t, StopLayout stop, ulong routeSeed, int feature)
    {
        if (t.Toys is not { Chance: > 0 } toys)
            return [];
        var rng = new Ballast.Pcg32(StopSeed.Of(StopSeed.Of(routeSeed, StopSeed.Toy, (ulong)feature), stop.Seed));
        var list = new List<(int, Physics.ToyNoise)>();
        foreach (var c in stop.Containers.Where(c => c.Zone == StopZone.Village).OrderBy(c => c.Index))
        {
            string kind = char.ToLowerInvariant(c.Kind.ToString()[0]) + c.Kind.ToString()[1..];
            double roll = rng.NextDouble(), pick = rng.NextDouble();
            if (toys.Kinds.Contains(kind) && roll < toys.Chance)
                list.Add((c.Index, toys.Noises.Length == 0 ? Physics.ToyNoise.None : toys.Noises[Math.Min(toys.Noises.Length - 1, (int)(pick * toys.Noises.Length))]));
        }
        return list;
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

/// <summary>
/// A creature's trophy (loot.json <c>trophies</c>; note 340): the find it is, what it pays in the tier's car-loads, and the
/// chance a kill leaves it (note 718: "can sometimes leave trophies"; 1, every time, as the Gannet's head is).
/// </summary>
public sealed record TrophyTuning(string Item, double PerCar, double Chance = 1);
