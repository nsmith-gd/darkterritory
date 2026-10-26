using Ballast;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Campaign;

/// <summary>Mirror of content/tuning/campaign.json. Field docs live in that file.</summary>
public sealed record CampaignTuning(int StartingCars, double StartingScrip, int MaxCars, CarCostTuning CarCost, TierStep[] Tiers,
    int ContractsOffered, int SaveSlots, UpgradeDef[] Upgrades, StandardCrew StandardCrew, SpareKitTuning? SpareKit = null)
{
    public const string File = "tuning/campaign.json";
    /// <summary>What the board's contracts carry (campaign.json <c>contracts</c>; GDD §9, §19, App. B.9; note 182).</summary>
    /// <summary>The departure's stores (campaign.json <c>stores</c>; GDD §9; note 182). Unset, the fortress sells none.</summary>
    public StoresTuning? Stores { get; init; }
    /// <summary>Taking a car off the consist (campaign.json <c>sellCar</c>; GDD §9 "add or remove railcars"). Unset, it can't.</summary>
    public SellCarTuning? SellCar { get; init; }
    /// <summary>
    /// A train taken at dawn ends the campaign (campaign.json <c>dawnEndsCampaign</c>; the director, the inbox, 12 Oct, note
    /// 632: "the campaign ends", roguelike): the save is closed, and the next start is a new crew. Unset, the crew goes on.
    /// </summary>
    public bool DawnEndsCampaign { get; init; }
    /// <summary>Corruption growing on the train (campaign.json <c>growth</c>; GDD Part Eleven q8; note 1039). Unset or off, none.</summary>
    public GrowthTuning? Growth { get; init; }
}

/// <summary>One line of the fortress's stores: its price, how many a night takes at most, and what each holds.</summary>
public sealed record StoreItem(double Cost, int Most, int Each = 1)
{
    /// <summary>Stocked once the consist works this tier (note 871: the flare pistol is a Frontier tool, upgrades.md §4), as an upgrade's.</summary>
    public RouteTier FromTier { get; init; }
}

/// <summary>campaign.json <c>stores</c> (GDD §9 "stock coal, powder and shot, lamps, repair supplies and tools"; note 182).</summary>
public sealed record StoresTuning(StoreItem Powder, StoreItem Lamps, StoreItem Extinguishers)
{
    /// <summary>A flare pistol and <see cref="StoreItem.Each"/> flares (note 871); unset, the fortress sells none.</summary>
    public StoreItem? Flares { get; init; }
    /// <summary>A box of <see cref="StoreItem.Each"/> fusees (note 1052; upgrades.md §7.3); unset, the fortress sells none.</summary>
    public StoreItem? Fusees { get; init; }
}

/// <summary>campaign.json <c>sellCar</c>: what a car taken off brings back (a share of what it cost), and the fewest a consist keeps.</summary>
public sealed record SellCarTuning(double Share, int Fewest);

/// <summary>What the fortress's stores sell for a night (note 182).</summary>
public enum StoreKind : byte { Powder, Lamp, Extinguisher, Flare, Fusee }

/// <summary>
/// The stores bought for the coming night (GDD §9): crates of powder and shot (each <see cref="StoreItem.Each"/> rounds more a
/// gun), spare lamps and spare extinguishers in the guard van. Issued for the night: settling it spends them.
/// </summary>
public sealed record Stores(int Powder = 0, int Lamps = 0, int Extinguishers = 0, int Flares = 0, int Fusees = 0)
{
    public int Of(StoreKind kind) => kind switch
    {
        StoreKind.Powder => Powder,
        StoreKind.Lamp => Lamps,
        StoreKind.Flare => Flares,
        StoreKind.Fusee => Fusees,
        _ => Extinguishers,
    };
    public bool Any => Powder > 0 || Lamps > 0 || Extinguishers > 0 || Flares > 0 || Fusees > 0;
}

public sealed record CarCostTuning(double Base, double Growth, int FromCar);
/// <summary>The fortress's spare repair kits (campaign.json <c>spareKit</c>, GDD v1.4 App. E.12 question 4): the price, and the most a crew keeps.</summary>
public sealed record SpareKitTuning(double Cost, int Most);
public sealed record TierStep(RouteTier Tier, int FromCars);
public enum UpgradeSize : byte { Small, Major }
/// <summary>
/// What an upgrade is for (ARCHITECTURE §8 note 678; docs/design/upgrades.md): a tool that pushes a night further, a bit of
/// fun, something silly bought because it's silly, or a whimsy that works and is funny with it.
/// </summary>
public enum UpgradeKind : byte { Tool, Fun, Silly, Whimsy }

/// <summary>
/// One line of the fortress's catalogue (campaign.json <c>upgrades</c>; spec F.3). Field docs live in that file.
/// <see cref="FromTier"/> holds a tool back until the consist works the tier whose trouble it answers (note 678: the town
/// stocks what the line needs); <see cref="Excludes"/> names the ones it won't go with; <see cref="Line"/> is the shop's
/// card and <see cref="Tally"/> what the yard clerk reads into every night's tally while the crew owns it.
/// </summary>
public sealed record UpgradeDef(string Id, string Name, UpgradeSize Size, double CostShare, Dictionary<string, double> Effect)
{
    public UpgradeKind Kind { get; init; }
    public RouteTier FromTier { get; init; }
    public string[] Excludes { get; init; } = [];
    /// <summary>What has to be fitted first (armour II needs the conversion, III needs II): ranks of one upgrade.</summary>
    public string[] Requires { get; init; } = [];
    public string? Line { get; init; }
    public string? Tally { get; init; }
}
/// <summary>The yardstick crew for <see cref="Campaign.Simulate"/>; <c>Loads</c> is the car-loads a competent delivered night
/// brings home on each tier (note 636: the train leaves empty, note 575, and a night earns what its stops load), and a tier
/// it doesn't name loads every car but the guard.</summary>
public sealed record StandardCrew(double SmallShare, double MajorShare, int MajorEvery, double LossFraction, double RunningCosts,
    Dictionary<string, double>? Loads = null);

/// <summary>
/// A night on the board at the fortress: a route and its tier, and what a car-load pays on it (spec F.1; GDD §9 "choose a
/// route"; note 637: "Just a route", the director, the inbox, 12 Oct). The train leaves empty (note 575), and what the cars
/// carry home is whatever its stops load, each car paid by its freight's rate (run.json economy.cargoRates). A save from
/// when a contract named a freight reads the same: the freight is dropped.
/// </summary>
public sealed record Contract(RouteTier Tier, ulong Seed, double PerCar)
{
    /// <summary>The route spec the session builds (<c>frontier:7</c>).</summary>
    public string Route => $"{char.ToLowerInvariant(Tier.ToString()[0])}{Tier.ToString()[1..]}:{Seed}";
}

public sealed record RunLog(int Run, string Route, RunEnd End, double Net, int CarsLost, double ScripAfter)
{
    /// <summary>
    /// How far the train went that night, km (note 535: the crew's distance is the sum, and a host's record the furthest any
    /// of its crews has gone). A save from before reads as none.
    /// </summary>
    public double Km { get; init; }
}

/// <summary>One car's condition, to put it back as it was.</summary>
/// <param name="Cargo">What it's carrying (T68): a save from before cargo types reads as none.</param>
/// <param name="Eaten">How much of its shell the Car Hugger has eaten (A.3: "gone, not dented"), so a resumed night doesn't
/// give it back (note 481). A save from before reads as none.</param>
public sealed record CarState(int Id, double Load, double Integrity, double CargoIntegrity, int Ammo, CargoKind Cargo = CargoKind.None, double Eaten = 0);

/// <summary>
/// A rake as the save found it (note 481): its vehicles front to back, the track its front is on and how far along. The
/// engine's is the train going on, a switchyard's cars it picked up ahead of it; any other is cars cut off it, or a yard's
/// still standing, where they were left.
/// </summary>
public sealed record RakeSave(int[] Vehicles, int Path, double Distance, bool Handbrake, bool Locked);

/// <summary>
/// A thing aboard at the autosave (note 500): its kind, the car it's in and where its middle is in that car's frame, its size
/// and how it lies, the locker it's on a shelf of and which shelf (−1: none), and what's its own: a crate's cargo, a find's
/// owner (which find), an extinguisher's mount and charge, a toy's noise, a radio broken, a shelved tool's tool (note 649),
/// a crate loaded (in its car's saved load already, note 659).
/// </summary>
public sealed record ThingAboard(BodyKind Kind, int Car, Double3 At, double Radius, double Friction, double Bounce, double Yaw = 0, int Locker = -1,
    int Slot = 0, bool Claimed = true, CargoKind Cargo = CargoKind.None, int Owner = -1, int Home = -1, double Charge = 1,
    ToyNoise Noise = ToyNoise.None, bool Broken = false, ulong Tools = 0, bool Loaded = false, byte Instrument = 0)
{
    /// <summary>A fragile find's knocks (note 719), so it comes back as knocked as it was.</summary>
    public int Knocks { get; init; }
}

/// <summary>
/// Spec E "autosave per POI, on successful departure": enough of a night to start it again from the facility the
/// train last left, if the session is lost.
/// </summary>
public sealed record RunCheckpoint(string Route, int Facility, double Seconds, double Front, double Tender, CarState[] Cars, int[] SpentHoldouts)
{
    /// <summary>
    /// The night's line itself, its plan compressed (linegen plan §17.4: the save keeps the plan and the generator's
    /// version, not just the seed, so an update to the generator never changes a run in progress). Null for a line
    /// that wasn't generated.
    /// </summary>
    public byte[]? Plan { get; init; }

    /// <summary>
    /// The train as it left, rake by rake (spec E "rolls back to last POI autosave"; note 481): a car cut loose or rolled away
    /// before the save stays where it was left, and a switchyard's cars picked up are still ahead of the engine. Null for an
    /// older save, which rebuilds the train from its own cars.
    /// </summary>
    public RakeSave[]? Rakes { get; init; }

    /// <summary>
    /// What the night had taken and spent (note 500): its finds stowed and their pay, the mail, the coal and rounds on the
    /// bill. Null for an older save, whose resumed night counts from the save.
    /// </summary>
    public RunTakings? Takings { get; init; }

    /// <summary>
    /// Everything aboard the train's cars as it left (note 500), put back in place of a fresh stocking: the kit where it was
    /// put, a rescued child, the lamps, radios, toys and finds. Null for an older save, which stocks the train afresh.
    /// </summary>
    public ThingAboard[]? Aboard { get; init; }
}

/// <summary>What a host owns between nights (spec E: the host owns the campaign). Saved as text, one file a slot.</summary>
public sealed record CampaignState
{
    public int Slot { get; init; } = 1;
    public string Name { get; init; } = "";
    /// <summary>What the crew named the engine (the nameplate, note 714): on its brass plates, and read in the tally. Null, unnamed.</summary>
    public string? EngineName { get; init; }
    public ulong Seed { get; init; } = 1;
    public int Cars { get; init; }
    public double Scrip { get; init; }
    public IReadOnlyList<string> Upgrades { get; init; } = [];
    public int Runs { get; init; }
    public IReadOnlyList<RunLog> History { get; init; } = [];
    /// <summary>The contract under way, if a night has begun and not been settled.</summary>
    public Contract? Current { get; init; }
    public RunCheckpoint? Checkpoint { get; init; }
    /// <summary>
    /// Spare repair kits (GDD v1.4 App. E.12 question 4): bought at the fortress, or found at a stop and brought home. Each
    /// starts a night in a crew locker beside the train's own (ARCHITECTURE §8 note 173); one lost in the night is gone.
    /// </summary>
    public int SpareKits { get; init; }
    /// <summary>
    /// The derailment's shuffle bag (GDD v1.4 App. E.6 "Rotation": "the host's campaign save keeps a bag of every eligible
    /// track"): what's left to draw and the last track played. Null in a save from before it, which starts a fresh bag.
    /// </summary>
    public Music.MusicBag? Music { get; init; }
    /// <summary>
    /// Who each player is (GDD v1.4 App. D.8: a freed survivor becomes that player's character, "stored in the host's campaign
    /// save against the player's ID"; note 181): their look by name. Someone not here is still who they signed on as.
    /// </summary>
    public IReadOnlyDictionary<string, string> Identities { get; init; } = new Dictionary<string, string>();
    /// <summary>The stores bought for the coming night (GDD §9; note 182): spent by it, whatever comes home.</summary>
    public Stores Stores { get; init; } = new();
    /// <summary>The custom of the last night's departure town (note 281): the next one won't share it. Null before the first.</summary>
    public string? LastTown { get; init; }
    /// <summary>
    /// The town the crew's in (note 591): the last night's terminus, if they got there. The next night departs from it. Null
    /// before the first night, or after one that didn't arrive (the crew's where they were).
    /// </summary>
    public Towns.TownAt? Town { get; init; }
    /// <summary>
    /// The towns the crew's brought creatures into (note 608), by <see cref="Towns.TownAt.Key"/>: how many of each one's houses
    /// burnt. Arrived at again, the town's still missing them.
    /// </summary>
    public IReadOnlyDictionary<string, int> Burnt { get; init; } = new Dictionary<string, int>();
    /// <summary>
    /// How cold each town is to the crew now (note 1024; GDD §3.1 "A town remembers"), by <see cref="Towns.TownAt.Key"/>, in
    /// percent: raised by the creatures they brought in through its gate, easing each night settled (towns.json
    /// <c>standing</c>). A town not here is warm to them; a save from before it has none.
    /// </summary>
    public IReadOnlyDictionary<string, int> Standing { get; init; } = new Dictionary<string, int>();
    /// <summary>
    /// The campaign's over (note 632; the director, the inbox, 12 Oct: a train taken at dawn ends it): the company has no more
    /// trains for this crew. The save is kept to be read, and the slot's next start is a new crew.
    /// </summary>
    public bool Over { get; init; }
    /// <summary>
    /// The crew's logbook (GDD §11; the director, 14 Oct 2026; note 721): a page a night, kept after the campaign's over so the
    /// save can still be read. Every player's profile keeps its own copy (<see cref="CrewLog"/>).
    /// </summary>
    public IReadOnlyList<LogPage> Logbook { get; init; } = [];
    /// <summary>
    /// The crew's own id (GDD §11, note 724): the same on every machine its save has travelled to, so each keeps one copy of it
    /// among the crews it's ridden with. Empty in a save from before it: <see cref="CrewId"/> reads the campaign's seed.
    /// </summary>
    public string Crew { get; init; } = "";
    /// <summary>The crew's id: <see cref="Crew"/>, or for an older save its seed's (random to each campaign, kept in the save).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string CrewId => Crew.Length > 0 ? Crew : $"{Seed:x16}";
    /// <summary>Who last hosted a night of it and settled it (note 724): the front end's "LAST HOSTED BY DAVE". Empty before.</summary>
    public string LastHost { get; init; } = "";
    /// <summary>
    /// When its last night was settled (note 724), Unix milliseconds by the settling host's clock: the tie-break between two
    /// copies with as many nights. 0 before.
    /// </summary>
    public long SettledAt { get; init; }
    /// <summary>
    /// The crew's sigil (GDD §29; note 731): a stencil from look.json maskStencils (from 1; 0, none yet), picked by the host at
    /// the fortress. Painted on the engine's side and small on every crewmate's mask; it travels with the save (note 724).
    /// </summary>
    public int Sigil { get; init; }
    /// <summary>
    /// The corruption grown on the train (note 1039; GDD Part Eleven q8): where creatures got on, a little more each night,
    /// until the blacksmith scrapes it off (<see cref="Campaign.Scrape"/>). Null in a save from before it, or with
    /// campaign.json <c>growth</c> off: a clean train.
    /// </summary>
    public TrainGrowth? Growth { get; init; }
}

/// <summary>What a purchase came to: the new state, or why not.</summary>
public readonly record struct Purchase(CampaignState State, string? Refused)
{
    public bool Ok => Refused is null;
}

/// <summary>The tunings a night is played with, after the crew's upgrades (every machine applies the same list).</summary>
public sealed record Loadout(TrainTuning Train, BoilerTuning Boiler, CombatTuning Combat, EnemyTuning? Enemies);

/// <summary>Spec F.2-F.4: the campaign's rules. Pure: states in, states out.</summary>
public static class Campaign
{
    /// <summary>How far the crew's gone, every night of its campaign together (note 535), km.</summary>
    public static double Distance(CampaignState s) => s.History.Sum(h => h.Km);

    public static CampaignState New(CampaignTuning t, int slot, string name, ulong seed) =>
        new() { Slot = slot, Name = name, Seed = seed, Cars = t.StartingCars, Scrip = t.StartingScrip };

    /// <summary>Spec F.2: what the Nth car costs.</summary>
    public static double CarCost(CampaignTuning t, int n) => Math.Round(t.CarCost.Base * DMath.Pow(t.CarCost.Growth, n - t.CarCost.FromCar));

    public static double NextCarCost(CampaignTuning t, CampaignState s) => CarCost(t, s.Cars + 1);

    /// <param name="standing">Towns.json <c>standing</c> (note 1024): the town the crew's in (its blacksmith's) asks more of a crew
    /// it's cold to. Null, the list price.</param>
    public static double UpgradeCost(CampaignTuning t, CampaignState s, UpgradeDef u, Towns.StandingTuning? standing = null) =>
        Math.Round(u.CostShare * NextCarCost(t, s) * Dearer(s, standing));

    /// <summary>What the blacksmith of the town the crew's in asks over the list price (note 1024): 1, the list price.</summary>
    public static double Dearer(CampaignState s, Towns.StandingTuning? standing) => standing?.Price(s.Town?.Cold ?? 0) ?? 1;

    /// <summary>Spec F.4: the tier a consist this size works.</summary>
    public static RouteTier TierFor(CampaignTuning t, int cars) => t.Tiers.Where(x => cars >= x.FromCars).OrderBy(x => x.FromCars).Last().Tier;

    /// <summary>
    /// Tonight's board: routes at the consist's own tier, and one from the tier below if there is one (note 637: a contract
    /// is just a route). The same every time for the same night. None for a campaign that's over (note 632).
    /// </summary>
    public static IReadOnlyList<Contract> Offers(CampaignTuning t, RunTuning run, CampaignState s)
    {
        if (s.Over)
            return [];
        var tier = TierFor(t, s.Cars);
        var tiers = Enumerable.Repeat(tier, Math.Max(1, t.ContractsOffered - (tier > RouteTier.Local ? 1 : 0))).ToList();
        if (tier > RouteTier.Local && t.ContractsOffered > 1)
            tiers.Add(tier - 1);
        return [.. tiers.Select((tr, i) => new Contract(tr, SeedFor(s, i), PerCar(run, tr)))];
    }

    static ulong SeedFor(CampaignState s, int i)
    {
        // Small, readable route seeds, different each night and each slot on the board.
        ulong h = s.Seed * 0x9E3779B97F4A7C15UL ^ (ulong)(s.Runs + 1) * 0xBF58476D1CE4E5B9UL ^ (ulong)(i + 1) * 0x94D049BB133111EBUL;
        return 1 + h % 9999;
    }

    public static double PerCar(RunTuning run, RouteTier tier) => run.Economy.PerCar.GetValueOrDefault(Key(tier), 700);

    /// <summary>A tier's key in the tuning files ("deadLines").</summary>
    static string Key(RouteTier tier) => char.ToLowerInvariant(tier.ToString()[0]) + tier.ToString()[1..];

    public static Purchase BuyCar(CampaignTuning t, CampaignState s)
    {
        if (s.Cars >= t.MaxCars)
            return new(s, $"the consist is at its limit ({t.MaxCars} cars)");
        double cost = NextCarCost(t, s);
        return s.Scrip < cost ? new(s, $"a car costs {cost:0} scrip; you have {s.Scrip:0}") : new(s with { Cars = s.Cars + 1, Scrip = s.Scrip - cost }, null);
    }

    public static Purchase BuyUpgrade(CampaignTuning t, CampaignState s, string id, Towns.StandingTuning? standing = null)
    {
        if (t.Upgrades.FirstOrDefault(u => u.Id == id) is not { } u)
            return new(s, $"no upgrade called '{id}'");
        // A livery owned (note 714: "bought once, picked in the yard"): chosen again, the train's painted in it. The one on the
        // train is the last livery in the list, as Apply takes it.
        if (s.Upgrades.Contains(id) && u.Effect.ContainsKey("livery"))
            return LiveryOf(t, s) == id ? new(s, $"the train's in {u.Name.ToLowerInvariant()} already")
                : new(s with { Upgrades = [.. s.Upgrades.Where(x => x != id), id] }, null);
        if (s.Upgrades.Contains(id))
            return new(s, $"you already have {u.Name}");
        if (Unavailable(t, s, u) is { } why)
            return new(s, why);
        double cost = UpgradeCost(t, s, u, standing);
        return s.Scrip < cost ? new(s, $"{u.Name} costs {cost:0} scrip; you have {s.Scrip:0}") : new(s with { Upgrades = [.. s.Upgrades, id], Scrip = s.Scrip - cost }, null);
    }

    /// <summary>The livery the train's painted in (note 714): the last livery upgrade owned, or null for the works' paint.</summary>
    public static string? LiveryOf(CampaignTuning t, CampaignState s) =>
        s.Upgrades.LastOrDefault(id => t.Upgrades.FirstOrDefault(u => u.Id == id)?.Effect.ContainsKey("livery") == true);

    /// <summary>
    /// Why the fortress won't sell <paramref name="u"/> to this crew yet (note 678), or null: a tool held back until the
    /// consist works the tier it answers (spec F.4: the cars set the tier), or one that won't go with something already fitted
    /// (either side naming the other).
    /// </summary>
    public static string? Unavailable(CampaignTuning t, CampaignState s, UpgradeDef u)
    {
        if (TierFor(t, s.Cars) < u.FromTier)
            return $"{u.Name} is stocked for {TierWords(u.FromTier)} work: a train of {t.Tiers.Where(x => x.Tier == u.FromTier).Select(x => x.FromCars).DefaultIfEmpty(0).First()} cars";
        foreach (var need in u.Requires)
            if (!s.Upgrades.Contains(need))
                return $"{u.Name} needs the {t.Upgrades.FirstOrDefault(o => o.Id == need)?.Name.ToLowerInvariant() ?? need} first";
        foreach (var other in s.Upgrades)
            if (other != u.Id && t.Upgrades.FirstOrDefault(o => o.Id == other) is { } o && (u.Excludes.Contains(other) || o.Excludes.Contains(u.Id)))
                return $"{u.Name} won't go with the {o.Name.ToLowerInvariant()}";
        return null;
    }

    /// <summary>A tier as the fortress says it ("dead lines").</summary>
    public static string TierWords(RouteTier tier) => tier switch
    {
        RouteTier.Local => "local",
        RouteTier.Frontier => "frontier",
        RouteTier.DeadLines => "dead lines",
        _ => "deep territory",
    };

    /// <summary>
    /// What the yard clerk adds to the tally for what the crew owns (note 678): each owned upgrade's <see cref="UpgradeDef.Tally"/>,
    /// in the catalogue's order, read as flat as the coal. "The settlement does not mourn" (GDD §9), and it isn't impressed.
    /// </summary>
    public static IReadOnlyList<string> TallyLines(CampaignTuning t, IEnumerable<string> upgrades)
    {
        var owned = upgrades.ToHashSet();
        return [.. t.Upgrades.Where(u => u.Tally is { Length: > 0 } && owned.Contains(u.Id)).Select(u => u.Tally!)];
    }

    /// <summary>A spare repair kit from the fortress's stores (E.12 question 4, answered: it sells them).</summary>
    public static Purchase BuySpareKit(CampaignTuning t, CampaignState s)
    {
        if (t.SpareKit is not { } k)
            return new(s, "the fortress has no spare kits to sell");
        if (s.SpareKits >= k.Most)
            return new(s, $"the lockers hold {k.Most} spare kits, and you have them");
        return s.Scrip < k.Cost ? new(s, $"a spare kit costs {k.Cost:0} scrip; you have {s.Scrip:0}")
            : new(s with { SpareKits = s.SpareKits + 1, Scrip = s.Scrip - k.Cost }, null);
    }

    /// <summary>
    /// The departure's stores (GDD §9 "stock ... powder and shot, lamps"; note 182): a crate of powder and shot, a spare lamp
    /// or a spare extinguisher for the coming night, up to campaign.json's most of each. Not mid-night.
    /// </summary>
    public static Purchase BuyStores(CampaignTuning t, CampaignState s, StoreKind kind)
    {
        if (t.Stores is not { } st)
            return new(s, "the fortress has no stores to sell");
        if (s.Current is not null)
            return new(s, "the stores are bought before the gates open");
        var (item, name) = kind switch
        {
            StoreKind.Powder => (st.Powder, "crate of powder and shot"),
            StoreKind.Lamp => (st.Lamps, "spare lamp"),
            StoreKind.Flare => (st.Flares, "flare pistol"),
            StoreKind.Fusee => (st.Fusees, "box of fusees"),
            _ => (st.Extinguishers, "spare extinguisher"),
        };
        if (item is null)
            return new(s, "the fortress has none to sell");
        // Note 871: a tool's stocked for the tier whose trouble it answers, as an upgrade is (UpgradeDef.FromTier).
        if (TierFor(t, s.Cars) < item.FromTier)
            return new(s, $"the {name} is stocked for {TierWords(item.FromTier)} work");
        if (s.Stores.Of(kind) >= item.Most)
            return new(s, $"a night takes {item.Most} at most, and you have them");
        if (s.Scrip < item.Cost)
            return new(s, $"a {name} costs {item.Cost:0} scrip; you have {s.Scrip:0}");
        var stores = kind switch
        {
            StoreKind.Powder => s.Stores with { Powder = s.Stores.Powder + 1 },
            StoreKind.Lamp => s.Stores with { Lamps = s.Stores.Lamps + 1 },
            StoreKind.Flare => s.Stores with { Flares = s.Stores.Flares + 1 },
            StoreKind.Fusee => s.Stores with { Fusees = s.Stores.Fusees + 1 },
            _ => s.Stores with { Extinguishers = s.Stores.Extinguishers + 1 },
        };
        return new(s with { Stores = stores, Scrip = s.Scrip - item.Cost }, null);
    }

    /// <summary>What taking the last car off the consist brings back: a share of what that car cost (GDD §9 "add or remove railcars").</summary>
    public static double SellBack(CampaignTuning t, CampaignState s) => t.SellCar is { } k ? Math.Round(k.Share * CarCost(t, s.Cars)) : 0;

    /// <summary>
    /// A car off the consist (GDD §9): it brings back a share of its price, and a shorter train works a lower tier when it drops
    /// under one (spec F.4). Never below campaign.json's fewest, and not mid-night.
    /// </summary>
    public static Purchase SellCar(CampaignTuning t, CampaignState s)
    {
        if (t.SellCar is not { } k)
            return new(s, "the yard isn't taking cars back");
        if (s.Current is not null)
            return new(s, "cars come off before the gates open");
        if (s.Cars <= k.Fewest)
            return new(s, $"a consist keeps {k.Fewest} cars at least");
        return new(s with { Cars = s.Cars - 1, Scrip = s.Scrip + SellBack(t, s) }, null);
    }

    /// <summary>
    /// The night's tunings with the departure's stores aboard (note 182): each crate of powder and shot is
    /// <see cref="StoreItem.Each"/> rounds more a gun, and the spare lamps and extinguishers go in the guard van.
    /// </summary>
    public static Loadout WithStores(CampaignTuning t, Loadout l, Stores stores)
    {
        if (!stores.Any || t.Stores is not { } st)
            return l;
        return l with
        {
            Combat = l.Combat with { Guns = l.Combat.Guns with { Ammo = l.Combat.Guns.Ammo + stores.Powder * st.Powder.Each } },
            Train = l.Train with
            {
                Kit = l.Train.Kit with
                {
                    SpareLamps = l.Train.Kit.SpareLamps + stores.Lamps * st.Lamps.Each,
                    SpareExtinguishers = l.Train.Kit.SpareExtinguishers + stores.Extinguishers * st.Extinguishers.Each,
                    // Note 871: each flare pistol bought comes loaded with its flares.
                    FlarePistols = l.Train.Kit.FlarePistols + stores.Flares,
                    FlareShots = st.Flares?.Each ?? l.Train.Kit.FlareShots,
                    // Note 1052: each box of fusees with its fusees.
                    Fusees = l.Train.Kit.Fusees + stores.Fusees,
                    FuseesEach = st.Fusees?.Each ?? l.Train.Kit.FuseesEach,
                },
            },
        };
    }

    /// <summary>
    /// Where a night arrived (note 591): delivered to a town (not a silent settlement), the crew's in it now, and the next
    /// night departs from it. Anything else, they're where they were.
    /// </summary>
    /// <para>The houses its creatures burnt there (note 608) are remembered against the town, with any from an earlier visit,
    /// and the town the crew's in carries them, so the next night's departure town has them burnt out.</para>
    /// <para>And it's colder to the crew for each creature they brought in (note 1024; towns.json <c>standing</c>, null or off:
    /// nothing more), on top of what it still holds against them.</para>
    public static CampaignState Arrived(CampaignState s, RunReport report, Towns.TownAt? terminus, Towns.StandingTuning? standing = null)
    {
        if (report.End != RunEnd.Delivered || terminus is null)
            return s;
        int was = s.Standing.GetValueOrDefault(terminus.Key), cold = standing?.Brought(was, report.BroughtIn) ?? was;
        if (cold != was)
            s = s with { Standing = With(s.Standing, terminus.Key, cold) };
        terminus = terminus with { Cold = standing?.On == true ? cold : 0 };
        int burnt = s.Burnt.GetValueOrDefault(terminus.Key) + report.TownBurnt;
        if (burnt <= 0)
            return s with { Town = terminus };
        return s with { Town = terminus with { Burnt = burnt }, Burnt = With(s.Burnt, terminus.Key, burnt) };
    }

    /// <summary><paramref name="by"/> with <paramref name="key"/> at <paramref name="n"/> (gone at 0), in key order for the save.</summary>
    static SortedDictionary<string, int> With(IReadOnlyDictionary<string, int> by, string key, int n)
    {
        var all = new SortedDictionary<string, int>(by.ToDictionary(), StringComparer.Ordinal) { [key] = n };
        if (n <= 0)
            all.Remove(key);
        return all;
    }

    /// <summary>
    /// A night gone by (note 1024): every town that's cold to the crew is <c>standing.easePerNight</c> warmer, wherever the
    /// crew is (the town they're in too). Null or off, as it is.
    /// </summary>
    public static CampaignState Eased(CampaignState s, Towns.StandingTuning? standing)
    {
        if (standing is null || s.Standing.Count == 0)
            return s;
        var eased = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, cold) in s.Standing)
            if (standing.Eased(cold) is var now and > 0)
                eased[key] = now;
        var town = s.Town is { } t ? t with { Cold = standing.On ? eased.GetValueOrDefault(t.Key) : 0 } : null;
        return s with { Standing = eased, Town = town };
    }

    /// <summary>The route spec a contract's night is played on: from the town the crew's in, if it's in one (note 591).</summary>
    public static string RouteOf(CampaignState s, Contract c) => Towns.TownAt.With(c.Route, s.Town);

    /// <summary>Starts a night on a contract from the board. A campaign that's over (note 632) has no more nights.</summary>
    public static CampaignState Begin(CampaignState s, Contract c) =>
        s.Over ? throw new InvalidOperationException($"{s.Name}'s campaign is over: the company has no more trains for them") : s with { Current = c, Checkpoint = null };

    /// <summary>
    /// Whether a night that ended this way ends the campaign (note 632; the director, the inbox, 12 Oct): a train taken at dawn
    /// (<see cref="RunEnd.DawnMissed"/>), with campaign.json <c>dawnEndsCampaign</c>. A derailment, a crew lost or a night
    /// stranded is settled and the crew goes on.
    /// </summary>
    public static bool Ends(CampaignTuning? t, RunEnd end) => end == RunEnd.DawnMissed && t?.DawnEndsCampaign == true;

    /// <summary>
    /// GDD §9 arrival: "everything still attached to the locomotive counts". The night's net goes to (or comes out of)
    /// the scrip, and cars left behind are gone from the consist. The spare repair kits are the ones that came home beyond
    /// the train's own (the fortress always issues that one): spares lost in the night are gone, and kits found at a stop
    /// and brought in are kept (E.12 question 4). A train taken at dawn ends the campaign (<see cref="Ends"/>, note 632): settled
    /// all the same, so its last night is in the history, and closed.
    /// </summary>
    /// <para>And the night gone by eases every town cold to the crew (<see cref="Eased"/>, note 1024).</para>
    public static CampaignState Settle(CampaignState s, RunReport report, CampaignTuning? t = null, Towns.StandingTuning? standing = null)
    {
        s = Eased(s, standing);
        int cars = Math.Max(2, s.Cars - report.CarsLost);
        double scrip = s.Scrip + report.Net;
        var log = new RunLog(s.Runs + 1, s.Current?.Route ?? "?", report.End, report.Net, report.CarsLost, scrip) { Km = report.DistanceKm };
        int spares = report.SpareKitsHome >= 0 ? report.SpareKitsHome : s.SpareKits;
        // D.8: whoever was freed tonight is that survivor from now on; everyone else stays who they were.
        var identities = new Dictionary<string, string>(s.Identities);
        foreach (var (name, look) in report.Identities)
            identities[name] = look;
        // The departure's stores were the night's issue (note 182): spent with it.
        // Note 1039: the night's boarders grow on the train where they got on, and what's grown spreads.
        var growth = t?.Growth is { On: true } g ? (s.Growth ?? TrainGrowth.None).Night(g, report.Boarded, cars + 1) : s.Growth;
        return s with { Cars = cars, Scrip = scrip, Runs = s.Runs + 1, History = [.. s.History, log], Current = null, Checkpoint = null, SpareKits = spares, Identities = identities, Stores = new(), Over = s.Over || Ends(t, report.End), Growth = growth };
    }

    /// <summary>
    /// The blacksmith scrapes the train clean (note 1039; campaign.json <c>growth.scrapeCost</c>): the couplings, the tender and
    /// under every car, for a small fee. Not mid-night, and not a clean train.
    /// </summary>
    public static Purchase Scrape(CampaignTuning t, CampaignState s, Towns.StandingTuning? standing = null)
    {
        if (t.Growth is not { On: true } g)
            return new(s, "the blacksmith doesn't do that");
        if (s.Current is not null)
            return new(s, "the train's scraped before the gates open");
        if (s.Growth is not { Any: true })
            return new(s, "there's nothing grown on the train to scrape");
        double cost = ScrapeCost(t, s, standing);
        return s.Scrip < cost ? new(s, $"scraping the train costs {cost:0} scrip; you have {s.Scrip:0}")
            : new(s with { Growth = null, Scrip = s.Scrip - cost }, null);
    }

    /// <summary>What the blacksmith asks to scrape the train (note 1039), dearer in a town cold to the crew as their upgrades are.</summary>
    public static double ScrapeCost(CampaignTuning t, CampaignState s, Towns.StandingTuning? standing = null) =>
        Math.Round((t.Growth?.ScrapeCost ?? 0) * Dearer(s, standing));

    /// <summary>
    /// Two copies of one crew's save (GDD §11, note 724): the one with more nights settled wins; with as many, the newer
    /// settlement; with that too, the one already held (<paramref name="held"/>). A campaign over (note 632) closes every copy:
    /// whichever wins is over if either is. Deterministic: no clock read here, only what each copy says.
    /// </summary>
    public static CampaignState Prefer(CampaignState held, CampaignState incoming)
    {
        var win = incoming.Runs > held.Runs || incoming.Runs == held.Runs && incoming.SettledAt > held.SettledAt ? incoming : held;
        return win.Over || !(held.Over || incoming.Over) ? win : win with { Over = true };
    }

    /// <summary>The night's tunings with the crew's spare repair kits aboard (E.12 question 4): stocked in the lockers beside the train's own.</summary>
    public static Loadout WithSpareKits(Loadout l, int spares) =>
        spares <= 0 ? l : l with { Train = l.Train with { Kit = l.Train.Kit with { SpareKits = spares } } };

    /// <summary>
    /// The night's tunings after the crew's upgrades. Most effects multiply a tuning; <c>radioReach</c> adds metres to the
    /// radio's (note 196); the consist's (note 184) add cars to train.json's <c>composition</c> (<c>utilityCars</c>,
    /// <c>guardCars</c>, <c>armouredCars</c>) or fit something (<c>handrails</c>, <c>switchThrower</c>), and every machine
    /// builds the same train from them. An effect name nobody models changes nothing.
    /// </summary>
    public static Loadout Apply(CampaignTuning t, IEnumerable<string> upgrades, Loadout base_)
    {
        var l = base_;
        foreach (var id in upgrades)
        {
            if (t.Upgrades.FirstOrDefault(u => u.Id == id) is not { } u)
                continue;
            foreach (var (effect, k) in u.Effect)
                l = effect switch
                {
                    "tender" => l with { Boiler = l.Boiler with { TenderCapacity = (int)Math.Round(l.Boiler.TenderCapacity * k) } },
                    "burn" => l with { Boiler = l.Boiler with { FireTimeConstant = l.Boiler.FireTimeConstant / k, SteamPerUnit = l.Boiler.SteamPerUnit / k } },
                    "ammo" => l with { Combat = l.Combat with { Guns = l.Combat.Guns with { Ammo = (int)Math.Round(l.Combat.Guns.Ammo * k) } } },
                    "brakes" => l with { Train = l.Train with { Performance = [.. l.Train.Performance.Select(r => r with { Brake = r.Brake * k })] } },
                    // Spec F.3's lamp brightness (queue #243, note 506): everything the headlamp reaches, not the retired Sleepers alone.
                    "lamp" => l with { Train = l.Train with { HeadlampReach = l.Train.HeadlampReach * k } },
                    // The consist (GDD §10, §26, spec F.3; note 184).
                    "utilityCars" => Fit(l, c => c with { UtilityCars = c.UtilityCars + (int)Math.Round(k) }),
                    "guardCars" => Fit(l, c => c with { GuardCars = c.GuardCars + (int)Math.Round(k) }),
                    "armouredCars" => Fit(l, c => c with { ArmouredCars = c.ArmouredCars + (int)Math.Round(k) }),
                    "cabArmour" => Fit(l, c => c with { CabArmour = c.CabArmour || k > 0 }),
                    "handrails" => Fit(l, c => c with { Handrails = c.Handrails || k > 0 }),
                    "contactSafe" => l with { Train = l.Train with { Couplings = l.Train.Couplings with { SafeContactSpeed = l.Train.Couplings.SafeContactSpeed * k } } },
                    "underLoad" => l with { Train = l.Train with { Couplings = l.Train.Couplings with { UncoupleUnderLoadSeconds = l.Train.Couplings.UncoupleUnderLoadSeconds * k } } },
                    "unhook" when l.Enemies is { } e => l with { Enemies = e with { Passenger = e.Passenger with { UncoupleSeconds = e.Passenger.UncoupleSeconds * k } } },
                    // The last of F.3's small ones and its switch thrower (note 196).
                    "lampOut" when l.Enemies is { } e => l with { Enemies = e with { Climbers = e.Climbers with { LampOutSeconds = e.Climbers.LampOutSeconds * k } } },
                    "foul" => l with { Combat = l.Combat with { Guns = l.Combat.Guns with { FoulChance = l.Combat.Guns.FoulChance * k } } },
                    // The lucky horseshoe (note 678): foul's, and its share kept, so a foul it saved can glint (note 775).
                    "horseshoe" => l with { Combat = l.Combat with { Guns = l.Combat.Guns with { FoulChance = l.Combat.Guns.FoulChance * k, HorseshoeFoul = l.Combat.Guns.HorseshoeFoul * k } } },
                    "repair" => l with { Boiler = l.Boiler with { RepairSeconds = l.Boiler.RepairSeconds * k } },
                    "radioReach" => l with { Train = l.Train with { Kit = l.Train.Kit with { RadioReach = l.Train.Kit.RadioReach + k } } },
                    "switchThrower" => Fit(l, c => c with { SwitchThrower = c.SwitchThrower || k > 0 }),
                    // The handcar (note 729), hung on the guard van's back.
                    "handcar" => Fit(l, c => c with { Handcar = c.Handcar || k > 0 }),
                    // The BBQ deck (note 698): the engine's front, its chairs, cooler and grill.
                    "bbqDeck" => Fit(l, c => c with { BbqDeck = c.BbqDeck || k > 0 }),
                    "diningCar" => Fit(l, c => c with { DiningCar = c.DiningCar || k > 0 }),
                    // The livery (note 714): the train's paint, the last owned's (BuyUpgrade puts the one picked last).
                    "livery" => Fit(l, c => c with { Livery = (int)Math.Round(k) }),
                    // The train's dressing (note 701): drawn on the train; the art reads which.
                    _ when effect.StartsWith("dress:", StringComparison.Ordinal) && k > 0 =>
                        Fit(l, c => c.Dressed(effect["dress:".Length..]) ? c : c with { Dress = [.. c.Dress, effect["dress:".Length..]] }),
                    // The stuffed moose head (note 701): a Moose takes it personally, its temper rising k times as fast as the train passes.
                    "mooseTemper" when l.Enemies is { } e => l with { Enemies = e with { Moose = e.Moose with { TrainPass = e.Moose.TrainPass * k } } },
                    // The disco ball (note 701): the Fire Flies come to dance, k times as often at a stop.
                    "fireFlies" when l.Enemies is { } e => l with { Enemies = e with { FireFlies = e.FireFlies with { StoppedWeight = e.FireFlies.StoppedWeight * k } } },
                    // The band (note 700): an instrument of band.json's in the guard van, one per upgrade.
                    _ when effect.StartsWith("instrument:", StringComparison.Ordinal) && k > 0 =>
                        l with { Train = l.Train with { Kit = l.Train.Kit with { Instruments = [.. l.Train.Kit.Instruments, effect["instrument:".Length..]] } } },
                    // The catalogue's (note 678; docs/design/upgrades.md): tools that keep pace with the tiers, and the silly.
                    "overspeedLead" => l with { Train = l.Train with { Overspeed = l.Train.Overspeed with { LeadSeconds = l.Train.Overspeed.LeadSeconds * k } } },
                    "brakeFade" => l with { Train = l.Train with { BrakeFade = l.Train.BrakeFade with { FadePerSecond = l.Train.BrakeFade.FadePerSecond * k } } },
                    "fragile" when l.Train.Fragile is { } f => l with { Train = l.Train with { Fragile = f with { ShockShare = f.ShockShare * k, BrakeShockPerSecond = f.BrakeShockPerSecond * k } } },
                    // The cowcatcher: what striking debris on the line costs the front car, heavy or minor, k times.
                    "debrisDamage" when l.Enemies is { } e => l with { Enemies = e with { Sleepers = e.Sleepers with { HeavyDamage = e.Sleepers.HeavyDamage * k, MinorDamage = e.Sleepers.MinorDamage * k } } },
                    // The grab irons: a ladder caught from the ground at k times the speed (the player's own tuning stays the base).
                    "grabIrons" => l with { Train = l.Train with { Kit = l.Train.Kit with { GrabIrons = l.Train.Kit.GrabIrons * k } } },
                    "boardUp" => l with { Train = l.Train with { Breach = l.Train.Breach with { BoardSeconds = l.Train.Breach.BoardSeconds * k } } },
                    // The speaking tubes (note 978): a horn in every car, joined to its neighbours'.
                    "speakingTubes" => l with { Train = l.Train with { Kit = l.Train.Kit with { SpeakingTubes = l.Train.Kit.SpeakingTubes || k > 0 } } },
                    // The crew's tools (note 1052; upgrades.md §7.3): the communication cord and the end-of-train telltale.
                    "cord" => l with { Train = l.Train with { Kit = l.Train.Kit with { Cord = l.Train.Kit.Cord || k > 0 } } },
                    "telltale" => l with { Train = l.Train with { Kit = l.Train.Kit with { Telltale = l.Train.Kit.Telltale || k > 0 } } },
                    "radios" => l with { Train = l.Train with { Kit = l.Train.Kit with { Radios = l.Train.Kit.Radios + (int)Math.Round(k) } } },
                    "accel" => l with { Train = l.Train with { Performance = [.. l.Train.Performance.Select(r => r with { Accel = r.Accel * k })] } },
                    "reload" => l with { Combat = l.Combat with { Guns = l.Combat.Guns with { ReloadStepSeconds = l.Combat.Guns.ReloadStepSeconds * k } } },
                    "whistle" => l with { Combat = l.Combat with { Choir = l.Combat.Choir with { WhistleLoudness = l.Combat.Choir.WhistleLoudness * k } } },
                    "hotboxKnock" when l.Enemies is { } e => l with { Enemies = e with { Hotbox = e.Hotbox with { KnockSeconds = e.Hotbox.KnockSeconds * k, GlowSeconds = e.Hotbox.GlowSeconds * k } } },
                    "hotboxMend" when l.Enemies is { } e => l with { Enemies = e with { Hotbox = e.Hotbox with { RepairSeconds = e.Hotbox.RepairSeconds * k } } },
                    // A bigger extinguisher: k times the spray in it, and back to full on its bracket in 1/k the time.
                    "extinguisher" when l.Enemies is { } e => l with { Enemies = e with { CarFire = e.CarFire with { ChargeSeconds = e.CarFire.ChargeSeconds * k, RechargeSeconds = e.CarFire.RechargeSeconds / k } } },
                    _ => l,
                };
        }
        return l;
    }

    static Loadout Fit(Loadout l, Func<CompositionTuning, CompositionTuning> f) => l with { Train = l.Train with { Composition = f(l.Train.Composition) } };

    /// <summary>
    /// The standard crew's campaign (see campaign.json): a night's net by spec F.1, and a car bought, with its share of
    /// upgrades, as soon as it's affordable. Returns the run on which the consist reached each size.
    /// </summary>
    public static IReadOnlyDictionary<int, int> Simulate(CampaignTuning t, RunTuning run, int maxRuns = 500)
    {
        var crew = t.StandardCrew;
        int cars = t.StartingCars, runs = 0, bought = 0;
        double scrip = t.StartingScrip;
        var reached = new Dictionary<int, int> { [cars] = 0 };
        while (cars < t.MaxCars && runs < maxRuns)
        {
            runs++;
            scrip += NetFor(t, run, cars, crew);
            while (cars < t.MaxCars)
            {
                double share = crew.SmallShare + ((bought + 1) % crew.MajorEvery == 0 ? crew.MajorShare : 0);
                double cost = CarCost(t, cars + 1) * (1 + share);
                if (scrip < cost)
                    break;
                scrip -= cost;
                cars++;
                bought++;
                reached[cars] = runs;
            }
        }
        return reached;
    }

    /// <summary>Spec F.1: gross on the car-loads a night brings home (note 636: what its stops load, never more than all
    /// but the guard), less running costs and the expected loss.</summary>
    public static double NetFor(CampaignTuning t, RunTuning run, int cars, StandardCrew crew)
    {
        var tier = TierFor(t, cars);
        double loads = cars - 1;
        if (crew.Loads is { } l && l.TryGetValue(Key(tier), out double home))
            loads = Math.Min(loads, home);
        return PerCar(run, tier) * loads * (1 - crew.RunningCosts) * (1 - crew.LossFraction);
    }
}
