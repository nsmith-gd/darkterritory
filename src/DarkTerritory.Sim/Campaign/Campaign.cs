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
}

/// <summary>One line of the fortress's stores: its price, how many a night takes at most, and what each holds.</summary>
public sealed record StoreItem(double Cost, int Most, int Each = 1);

/// <summary>campaign.json <c>stores</c> (GDD §9 "stock coal, powder and shot, lamps, repair supplies and tools"; note 182).</summary>
public sealed record StoresTuning(StoreItem Powder, StoreItem Lamps, StoreItem Extinguishers);

/// <summary>campaign.json <c>sellCar</c>: what a car taken off brings back (a share of what it cost), and the fewest a consist keeps.</summary>
public sealed record SellCarTuning(double Share, int Fewest);

/// <summary>What the fortress's stores sell for a night (note 182).</summary>
public enum StoreKind : byte { Powder, Lamp, Extinguisher }

/// <summary>
/// The stores bought for the coming night (GDD §9): crates of powder and shot (each <see cref="StoreItem.Each"/> rounds more a
/// gun), spare lamps and spare extinguishers in the guard van. Issued for the night: settling it spends them.
/// </summary>
public sealed record Stores(int Powder = 0, int Lamps = 0, int Extinguishers = 0)
{
    public int Of(StoreKind kind) => kind switch { StoreKind.Powder => Powder, StoreKind.Lamp => Lamps, _ => Extinguishers };
    public bool Any => Powder > 0 || Lamps > 0 || Extinguishers > 0;
}

public sealed record CarCostTuning(double Base, double Growth, int FromCar);
/// <summary>The fortress's spare repair kits (campaign.json <c>spareKit</c>, GDD v1.4 App. E.12 question 4): the price, and the most a crew keeps.</summary>
public sealed record SpareKitTuning(double Cost, int Most);
public sealed record TierStep(RouteTier Tier, int FromCars);
public enum UpgradeSize : byte { Small, Major }
public sealed record UpgradeDef(string Id, string Name, UpgradeSize Size, double CostShare, Dictionary<string, double> Effect);
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
    ToyNoise Noise = ToyNoise.None, bool Broken = false, ulong Tools = 0, bool Loaded = false);

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

    public static double UpgradeCost(CampaignTuning t, CampaignState s, UpgradeDef u) => Math.Round(u.CostShare * NextCarCost(t, s));

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

    public static Purchase BuyUpgrade(CampaignTuning t, CampaignState s, string id)
    {
        if (t.Upgrades.FirstOrDefault(u => u.Id == id) is not { } u)
            return new(s, $"no upgrade called '{id}'");
        if (s.Upgrades.Contains(id))
            return new(s, $"you already have {u.Name}");
        double cost = UpgradeCost(t, s, u);
        return s.Scrip < cost ? new(s, $"{u.Name} costs {cost:0} scrip; you have {s.Scrip:0}") : new(s with { Upgrades = [.. s.Upgrades, id], Scrip = s.Scrip - cost }, null);
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
            _ => (st.Extinguishers, "spare extinguisher"),
        };
        if (s.Stores.Of(kind) >= item.Most)
            return new(s, $"a night takes {item.Most} at most, and you have them");
        if (s.Scrip < item.Cost)
            return new(s, $"a {name} costs {item.Cost:0} scrip; you have {s.Scrip:0}");
        var stores = kind switch
        {
            StoreKind.Powder => s.Stores with { Powder = s.Stores.Powder + 1 },
            StoreKind.Lamp => s.Stores with { Lamps = s.Stores.Lamps + 1 },
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
    public static CampaignState Arrived(CampaignState s, RunReport report, Towns.TownAt? terminus)
    {
        if (report.End != RunEnd.Delivered || terminus is null)
            return s;
        int burnt = s.Burnt.GetValueOrDefault(terminus.Key) + report.TownBurnt;
        if (burnt <= 0)
            return s with { Town = terminus };
        var all = new SortedDictionary<string, int>(s.Burnt.ToDictionary(), StringComparer.Ordinal) { [terminus.Key] = burnt };
        return s with { Town = terminus with { Burnt = burnt }, Burnt = all };
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
    public static CampaignState Settle(CampaignState s, RunReport report, CampaignTuning? t = null)
    {
        int cars = Math.Max(2, s.Cars - report.CarsLost);
        double scrip = s.Scrip + report.Net;
        var log = new RunLog(s.Runs + 1, s.Current?.Route ?? "?", report.End, report.Net, report.CarsLost, scrip) { Km = report.DistanceKm };
        int spares = report.SpareKitsHome >= 0 ? report.SpareKitsHome : s.SpareKits;
        // D.8: whoever was freed tonight is that survivor from now on; everyone else stays who they were.
        var identities = new Dictionary<string, string>(s.Identities);
        foreach (var (name, look) in report.Identities)
            identities[name] = look;
        // The departure's stores were the night's issue (note 182): spent with it.
        return s with { Cars = cars, Scrip = scrip, Runs = s.Runs + 1, History = [.. s.History, log], Current = null, Checkpoint = null, SpareKits = spares, Identities = identities, Stores = new(), Over = s.Over || Ends(t, report.End) };
    }

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
                    "handrails" => Fit(l, c => c with { Handrails = c.Handrails || k > 0 }),
                    "insulation" => Fit(l, c => c with { Insulation = c.Insulation * k }),
                    "contactSafe" => l with { Train = l.Train with { Couplings = l.Train.Couplings with { SafeContactSpeed = l.Train.Couplings.SafeContactSpeed * k } } },
                    "underLoad" => l with { Train = l.Train with { Couplings = l.Train.Couplings with { UncoupleUnderLoadSeconds = l.Train.Couplings.UncoupleUnderLoadSeconds * k } } },
                    "unhook" when l.Enemies is { } e => l with { Enemies = e with { Passenger = e.Passenger with { UncoupleSeconds = e.Passenger.UncoupleSeconds * k } } },
                    // The last of F.3's small ones and its switch thrower (note 196).
                    "lampOut" when l.Enemies is { } e => l with { Enemies = e with { Climbers = e.Climbers with { LampOutSeconds = e.Climbers.LampOutSeconds * k } } },
                    "foul" => l with { Combat = l.Combat with { Guns = l.Combat.Guns with { FoulChance = l.Combat.Guns.FoulChance * k } } },
                    "repair" => l with { Boiler = l.Boiler with { RepairSeconds = l.Boiler.RepairSeconds * k } },
                    "radioReach" => l with { Train = l.Train with { Kit = l.Train.Kit with { RadioReach = l.Train.Kit.RadioReach + k } } },
                    "switchThrower" => Fit(l, c => c with { SwitchThrower = c.SwitchThrower || k > 0 }),
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
