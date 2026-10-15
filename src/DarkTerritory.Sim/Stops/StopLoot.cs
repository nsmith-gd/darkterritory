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
    /// <summary>The finds that lose worth when knocked (note 719). Unset, none do.</summary>
    public FragileTuning? Fragile { get; init; }
    /// <summary>The two-person finds (note 719). Unset, none.</summary>
    public JackpotTuning? Jackpots { get; init; }
    /// <summary>A stop's locked house of better things (note 719). Unset, none.</summary>
    public JackpotHouseTuning? JackpotHouse { get; init; }
    /// <summary>Loose things riding in the cars (note 719). Unset or off, they lie as they lay.</summary>
    public RideTuning? Ride { get; init; }

    /// <summary>
    /// What a find of <paramref name="item"/> knocked <paramref name="knocks"/> times pays (note 719): its whole worth times
    /// <c>fragile.keep</c> once a knock, rounded to fives, never less than <see cref="LeastWorth"/>. Not fragile, its worth.
    /// </summary>
    public double Knocked(string item, double whole, int knocks) =>
        knocks <= 0 || Fragile is not { } f || !f.Items.ContainsKey(item) ? whole
            : Math.Max(LeastWorth, Math.Round(whole * Math.Pow(f.Keep, knocks) / LeastWorth) * LeastWorth);

    /// <summary>A find's name as the clerk reads it in the tally (note 719): a knocked fragile one with its damage ("Mantel clock, dented").</summary>
    public string Named(string item, int knocks) =>
        knocks > 0 && Fragile is { } f && f.Items.TryGetValue(item, out var words) && words.Length > 0
            ? $"{Name(item)}, {words[Math.Min(knocks, words.Length) - 1]}" : Name(item);

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
    /// <summary>Note 723: a find put on someone else heals this many times as much (capped at their full health).</summary>
    public double OnAFriend { get; init; } = 1.5;

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

/// <summary>loot.json <c>fragile</c> (note 719): which finds lose worth knocked, and the words for it; how hard a knock; what's kept.</summary>
public sealed record FragileTuning
{
    /// <summary>The damage's words by knocks, by item key (looked up, never iterated).</summary>
    public Dictionary<string, string[]> Items { get; init; } = new();
    public double KnockSpeed { get; init; } = 5.5;
    /// <summary>A slide across a car's floor (note 719's riding) struck into something faster than this (m/s) is a knock too.</summary>
    public double SlideKnock { get; init; } = 3;
    public double KnockSeconds { get; init; } = 0.3;
    public double Keep { get; init; } = 0.5;
}

/// <summary>loot.json <c>jackpots.items</c>' one (note 719): its worth in the tier's car-loads, and its ball's radius (m).</summary>
public sealed record JackpotItem(double Worth, double Radius);

/// <summary>loot.json <c>jackpots</c> (note 719): the two-person finds, each stop's chance of one by tier, the jackpot house's, and the most at a stop.</summary>
public sealed record JackpotTuning
{
    /// <summary>By item key. Iterated only sorted by key (<see cref="Keys"/>), so every machine draws the same.</summary>
    public Dictionary<string, JackpotItem> Items { get; init; } = new();
    /// <summary>By tier key, looked up, never iterated.</summary>
    public Dictionary<string, double> Chance { get; init; } = new();
    public double House { get; init; }
    public int Most { get; init; } = 2;

    /// <summary>The items' keys in a fixed order (ordinal), for drawing one.</summary>
    public IReadOnlyList<string> Keys => [.. Items.Keys.Order(StringComparer.Ordinal)];
}

/// <summary>loot.json <c>jackpotHouse</c> (note 719). Field docs live in that file.</summary>
public sealed record JackpotHouseTuning
{
    /// <summary>By tier key, looked up, never iterated.</summary>
    public Dictionary<string, double> Chance { get; init; } = new();
    public double UnlockSeconds { get; init; } = 6;
    public int Draws { get; init; } = 1;
    public int[] Extra { get; init; } = [0, 0];
    public string[] Items { get; init; } = [];
}

/// <summary>loot.json <c>ride</c> (note 719): loose things sliding in the cars. Field docs live in that file.</summary>
public sealed record RideTuning
{
    public bool Enabled { get; init; }
    public double Grip { get; init; } = 0.05;
    public double StackGrip { get; init; } = 0.3;
    public double Slide { get; init; } = 0.03;
    public double HeavyGrip { get; init; } = 2;
    public int LiveCap { get; init; } = 12;
    public double MaxFelt { get; init; } = 12;
}

/// <summary>
/// A village find (P12, P14): which container it's in, what it is, and what it pays in scrip (its kind's worth: note 718).
/// <paramref name="Knocks"/>: how many times a fragile one's been knocked (note 719), its <paramref name="Value"/> less for it.
/// </summary>
public readonly record struct LootFind(int Container, string Item, double Value, int Knocks = 0);

/// <summary>
/// The economy's half of P14: turns a stop's village containers into finds. The layout decides where; this decides what
/// and how much, from the run's seed, so every machine agrees and the economy can be tuned without touching a layout.
/// </summary>
public static class StopLoot
{
    /// <param name="perCar">The tier's contract value per delivered car (run.json economy.perCar): the unit of a find's worth.</param>
    /// <param name="locked">The stop's jackpot house (<see cref="JackpotHouse"/>), or −1: its kept finds are drawn
    /// <c>jackpotHouse.draws</c> times, the worthiest kept (note 719), on a stream of their own.</param>
    public static IReadOnlyList<LootFind> Village(LootTuning t, StopLayout stop, ulong routeSeed, int feature, double perCar, int locked = -1)
    {
        var rng = new Ballast.Pcg32(StopSeed.Of(StopSeed.Of(routeSeed, StopSeed.Loot, (ulong)feature), stop.Seed));
        var containers = stop.Containers.Where(c => c.Zone == StopZone.Village && c.Kind != ContainerKind.Loose).ToList();
        var finds = new List<LootFind>();
        if (containers.Count > 0)
        {
            // The stream's first draw was the stop's budget, which a find's worth no longer comes from (note 718): kept, so
            // the finds a seed deals are the ones it always dealt.
            rng.NextDouble();
            // An outlier's other draws (P11), on a stream of their own for the same reason.
            var outlier = new Ballast.Pcg32(StopSeed.Of(StopSeed.Of(routeSeed, StopSeed.Outlier, (ulong)feature), stop.Seed));
            // The jackpot house's better draws (note 719), likewise.
            var better = new Ballast.Pcg32(StopSeed.Of(StopSeed.Of(routeSeed, StopSeed.Better, (ulong)feature), stop.Seed));
            finds.AddRange(containers.Select(c =>
            {
                var kind = t.Of(c.Kind);
                string item = kind.Items[(int)(rng.NextDouble() * kind.Items.Length)];
                for (int draw = 1; c.Outlier && draw < t.OutlierDraws; draw++)
                {
                    string other = kind.Items[(int)(outlier.NextDouble() * kind.Items.Length)];
                    if (t.WorthOf(other, perCar) > t.WorthOf(item, perCar))
                        item = other;
                }
                for (int draw = 1; locked >= 0 && c.Building == locked && draw < (t.JackpotHouse?.Draws ?? 1); draw++)
                {
                    string other = kind.Items[(int)(better.NextDouble() * kind.Items.Length)];
                    if (t.WorthOf(other, perCar) > t.WorthOf(item, perCar))
                        item = other;
                }
                return new LootFind(c.Index, item, t.WorthOf(item, perCar));
            }));
        }
        // The small things lying on the floors (note 738), on a stream of their own so the village's other finds are what
        // they were: each worth what its kind is (note 718), on top of them.
        var loose = stop.Containers.Where(c => c.Zone == StopZone.Village && c.Kind == ContainerKind.Loose).ToList();
        if (loose.Count > 0)
        {
            var lr = new Ballast.Pcg32(StopSeed.Of(StopSeed.Of(routeSeed, StopSeed.Loose, (ulong)feature), stop.Seed));
            var kind = t.Of(ContainerKind.Loose);
            foreach (var c in loose)
            {
                string item = kind.Items[(int)(lr.NextDouble() * kind.Items.Length)];
                finds.Add(new LootFind(c.Index, item, t.WorthOf(item, perCar)));
            }
        }
        return finds;
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
    /// Note 719 (queue #453, the big finds): the stop's jackpot house, its building's index, or −1 for none: at loot.json
    /// <c>jackpotHouse.chance</c> by tier, from the run's seed on a stream of its own, the village's biggest open house of one
    /// door (not a pair of cottages, a Holdout or the Gaunt's roost). Alike on every machine, so each locks the same door.
    /// </summary>
    public static int JackpotHouse(LootTuning t, StopLayout stop, ulong routeSeed, int feature, RouteTier tier)
    {
        if (t.JackpotHouse is not { } jh || jh.Chance.GetValueOrDefault(TierKey(tier)) is not (> 0 and var chance))
            return -1;
        var rng = new Ballast.Pcg32(StopSeed.Of(StopSeed.Of(routeSeed, StopSeed.Locked, (ulong)feature), stop.Seed));
        if (rng.NextDouble() >= chance)
            return -1;
        int best = -1;
        double most = 0;
        for (int i = 0; i < stop.Buildings.Count; i++)
        {
            var b = stop.Buildings[i];
            if (b.Kind != BuildingKind.House || !b.Open || b.Zone != StopZone.Village || b.Shape == HouseShape.Pair || !Run.StopWalls.Walled(stop, i)
                || Run.StopWalls.Doorways(b).Count() != 1 || stop.Lairs.Any(l => l.Kind == LairKind.GauntRoost && l.Building == i))
                continue;
            double area = b.Parts.Count > 0 ? b.Parts.Sum(p => p.Length * p.Width) : b.Length * b.Width;
            if (area > most + 1e-9)
                (best, most) = (i, area);
        }
        return best;
    }

    /// <summary>
    /// Note 719: what lies on the jackpot house's floor besides what it keeps: loot.json <c>jackpotHouse.extra</c> of its
    /// <c>items</c>, from the run's seed on a stream of its own. None without a jackpot house.
    /// </summary>
    public static IReadOnlyList<string> Extras(LootTuning t, StopLayout stop, ulong routeSeed, int feature, int locked)
    {
        if (locked < 0 || t.JackpotHouse is not { Items.Length: > 0 } jh)
            return [];
        var rng = new Ballast.Pcg32(StopSeed.Of(StopSeed.Of(routeSeed, StopSeed.Extra, (ulong)feature), stop.Seed));
        int n = rng.RangeInclusive(jh.Extra[0], jh.Extra[^1]);
        return [.. Enumerable.Range(0, n).Select(_ => jh.Items[Math.Min(jh.Items.Length - 1, (int)(rng.NextDouble() * jh.Items.Length))])];
    }

    /// <summary>
    /// Note 719 (the director, 14 Oct 2026: "Very big value things should take two to carry"): a stop's two-person jackpots, each
    /// its item and the container it lies beside (−1: on the jackpot house's floor). From the run's seed on a stream of its own:
    /// the jackpot house's at <c>jackpots.house</c>, and one more at the tier's <c>jackpots.chance</c>, beside a yard's crate
    /// stack or strongroom or in an open house (<paramref name="placeable"/>); never more than <c>jackpots.most</c>.
    /// </summary>
    public static IReadOnlyList<(string Item, int Beside)> Jackpots(LootTuning t, StopLayout stop, ulong routeSeed, int feature, RouteTier tier, int locked,
        Func<StopContainer, bool> placeable)
    {
        if (t.Jackpots is not { Items.Count: > 0 } jp || jp.Most <= 0)
            return [];
        var keys = jp.Keys;
        var rng = new Ballast.Pcg32(StopSeed.Of(StopSeed.Of(routeSeed, StopSeed.Jackpot, (ulong)feature), stop.Seed));
        string Pick() => keys[Math.Min(keys.Count - 1, (int)(rng.NextDouble() * keys.Count))];
        var list = new List<(string, int)>();
        double house = rng.NextDouble(), roll = rng.NextDouble();
        if (locked >= 0 && house < jp.House)
            list.Add((Pick(), -1));
        if (roll < jp.Chance.GetValueOrDefault(TierKey(tier)) && list.Count < jp.Most)
        {
            var places = stop.Containers.OrderBy(c => c.Index).Where(c => c.Building != locked || locked < 0).Where(placeable).ToList();
            if (places.Count > 0)
            {
                var at = places[Math.Min(places.Count - 1, (int)(rng.NextDouble() * places.Count))];
                list.Add((Pick(), at.Index));
            }
        }
        return list;
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
