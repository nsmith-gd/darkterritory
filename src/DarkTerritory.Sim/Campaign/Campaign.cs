using Ballast;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
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
    public ContractTuning Contracts { get; init; } = new();
    /// <summary>The departure's stores (campaign.json <c>stores</c>; GDD §9; note 182). Unset, the fortress sells none.</summary>
    public StoresTuning? Stores { get; init; }
    /// <summary>Taking a car off the consist (campaign.json <c>sellCar</c>; GDD §9 "add or remove railcars"). Unset, it can't.</summary>
    public SellCarTuning? SellCar { get; init; }
}

/// <summary>
/// campaign.json <c>contracts</c>: the freight the board's ordinary contracts are drawn from, one each by its seed, and whether
/// the board carries a comet contract besides (App. B.9: "the high-risk contract ... the best freight payout").
/// </summary>
public sealed record ContractTuning
{
    public IReadOnlyList<CargoKind> Cargo { get; init; } = [];
    public bool Comet { get; init; }
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
public sealed record StandardCrew(double SmallShare, double MajorShare, int MajorEvery, double LossFraction, double RunningCosts);

/// <summary>
/// A night on the board at the fortress: a route, the freight the train leaves with, and what a car of it pays (spec F.1; GDD
/// §9 "choose freight contracts", §19; note 182). A save from before cargo reads as goods.
/// </summary>
public sealed record Contract(RouteTier Tier, ulong Seed, double PerCar, CargoKind Cargo = CargoKind.Goods)
{
    /// <summary>The route spec the session builds (<c>frontier:7</c>).</summary>
    public string Route => $"{char.ToLowerInvariant(Tier.ToString()[0])}{Tier.ToString()[1..]}:{Seed}";
}

public sealed record RunLog(int Run, string Route, RunEnd End, double Net, int CarsLost, double ScripAfter);

/// <summary>One car's condition, to put it back as it was.</summary>
/// <param name="Cargo">What it's carrying (T68): a save from before cargo types reads as none.</param>
public sealed record CarState(int Id, double Load, double Integrity, double CargoIntegrity, int Ammo, CargoKind Cargo = CargoKind.None);

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
    /// <summary>The custom of the last night's departure town (note 304): the next one won't share it. Null before the first.</summary>
    public string? LastTown { get; init; }
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
    public static CampaignState New(CampaignTuning t, int slot, string name, ulong seed) =>
        new() { Slot = slot, Name = name, Seed = seed, Cars = t.StartingCars, Scrip = t.StartingScrip };

    /// <summary>Spec F.2: what the Nth car costs.</summary>
    public static double CarCost(CampaignTuning t, int n) => Math.Round(t.CarCost.Base * DMath.Pow(t.CarCost.Growth, n - t.CarCost.FromCar));

    public static double NextCarCost(CampaignTuning t, CampaignState s) => CarCost(t, s.Cars + 1);

    public static double UpgradeCost(CampaignTuning t, CampaignState s, UpgradeDef u) => Math.Round(u.CostShare * NextCarCost(t, s));

    /// <summary>Spec F.4: the tier a consist this size works.</summary>
    public static RouteTier TierFor(CampaignTuning t, int cars) => t.Tiers.Where(x => cars >= x.FromCars).OrderBy(x => x.FromCars).Last().Tier;

    /// <summary>
    /// Tonight's board: the consist's own tier, and one from below if there is one, each carrying a freight drawn from
    /// campaign.json's list; then, if the board carries one, a comet contract at the consist's tier, the best-paying freight
    /// there is (App. B.9). The same every time for the same night.
    /// </summary>
    public static IReadOnlyList<Contract> Offers(CampaignTuning t, RunTuning run, CampaignState s)
    {
        var tier = TierFor(t, s.Cars);
        var tiers = Enumerable.Repeat(tier, Math.Max(1, t.ContractsOffered - (tier > RouteTier.Local ? 1 : 0))).ToList();
        if (tier > RouteTier.Local && t.ContractsOffered > 1)
            tiers.Add(tier - 1);
        var board = tiers.Select((tr, i) => Offer(run, tr, SeedFor(s, i), CargoFor(t, s, i))).ToList();
        if (t.Contracts.Comet)
            board.Add(Offer(run, tier, SeedFor(s, board.Count), CargoKind.Comet));
        return board;
    }

    static Contract Offer(RunTuning run, RouteTier tier, ulong seed, CargoKind cargo) => new(tier, seed, PerCar(run, tier, cargo), cargo);

    /// <summary>An ordinary contract's freight: one of campaign.json's list, by the night and the slot on the board.</summary>
    static CargoKind CargoFor(CampaignTuning t, CampaignState s, int i)
    {
        var pool = t.Contracts.Cargo;
        if (pool.Count == 0)
            return CargoKind.Goods;
        ulong h = s.Seed * 0xD6E8FEB86659FD93UL ^ (ulong)(s.Runs + 1) * 0xA0761D6478BD642FUL ^ (ulong)(i + 1) * 0xE7037ED1A0B428DBUL;
        h ^= h >> 31;
        return pool[(int)(h % (ulong)pool.Count)];
    }

    /// <summary>What a car of <paramref name="cargo"/> pays on <paramref name="tier"/>: the tier's value by the cargo's rate (run.json economy.cargoRates).</summary>
    public static double PerCar(RunTuning run, RouteTier tier, CargoKind cargo) => Math.Round(PerCar(run, tier) * run.Economy.Rate(cargo));

    static ulong SeedFor(CampaignState s, int i)
    {
        // Small, readable route seeds, different each night and each slot on the board.
        ulong h = s.Seed * 0x9E3779B97F4A7C15UL ^ (ulong)(s.Runs + 1) * 0xBF58476D1CE4E5B9UL ^ (ulong)(i + 1) * 0x94D049BB133111EBUL;
        return 1 + h % 9999;
    }

    public static double PerCar(RunTuning run, RouteTier tier)
    {
        string key = char.ToLowerInvariant(tier.ToString()[0]) + tier.ToString()[1..];
        return run.Economy.PerCar.GetValueOrDefault(key, 700);
    }

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

    /// <summary>Starts a night on a contract from the board.</summary>
    public static CampaignState Begin(CampaignState s, Contract c) => s with { Current = c, Checkpoint = null };

    /// <summary>
    /// GDD §9 arrival: "everything still attached to the locomotive counts". The night's net goes to (or comes out of)
    /// the scrip, and cars left behind are gone from the consist. The spare repair kits are the ones that came home beyond
    /// the train's own (the fortress always issues that one): spares lost in the night are gone, and kits found at a stop
    /// and brought in are kept (E.12 question 4).
    /// </summary>
    public static CampaignState Settle(CampaignState s, RunReport report)
    {
        int cars = Math.Max(2, s.Cars - report.CarsLost);
        double scrip = s.Scrip + report.Net;
        var log = new RunLog(s.Runs + 1, s.Current?.Route ?? "?", report.End, report.Net, report.CarsLost, scrip);
        int spares = report.SpareKitsHome >= 0 ? report.SpareKitsHome : s.SpareKits;
        // D.8: whoever was freed tonight is that survivor from now on; everyone else stays who they were.
        var identities = new Dictionary<string, string>(s.Identities);
        foreach (var (name, look) in report.Identities)
            identities[name] = look;
        // The departure's stores were the night's issue (note 182): spent with it.
        return s with { Cars = cars, Scrip = scrip, Runs = s.Runs + 1, History = [.. s.History, log], Current = null, Checkpoint = null, SpareKits = spares, Identities = identities, Stores = new() };
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
                    "lamp" when l.Enemies is { } e => l with { Enemies = e with { Sleepers = e.Sleepers with { LampRevealDistance = e.Sleepers.LampRevealDistance * k } } },
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

    /// <summary>Spec F.1: gross on the loaded cars (all but the guard), less running costs and the expected loss.</summary>
    public static double NetFor(CampaignTuning t, RunTuning run, int cars, StandardCrew crew) =>
        PerCar(run, TierFor(t, cars)) * (cars - 1) * (1 - crew.RunningCosts) * (1 - crew.LossFraction);
}
