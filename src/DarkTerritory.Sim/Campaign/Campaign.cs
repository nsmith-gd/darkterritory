using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Campaign;

/// <summary>Mirror of content/tuning/campaign.json. Field docs live in that file.</summary>
public sealed record CampaignTuning(int StartingCars, double StartingScrip, int MaxCars, CarCostTuning CarCost, TierStep[] Tiers,
    int ContractsOffered, int SaveSlots, UpgradeDef[] Upgrades, StandardCrew StandardCrew)
{
    public const string File = "tuning/campaign.json";
}

public sealed record CarCostTuning(double Base, double Growth, int FromCar);
public sealed record TierStep(RouteTier Tier, int FromCars);
public enum UpgradeSize : byte { Small, Major }
public sealed record UpgradeDef(string Id, string Name, UpgradeSize Size, double CostShare, Dictionary<string, double> Effect);
public sealed record StandardCrew(double SmallShare, double MajorShare, int MajorEvery, double LossFraction, double RunningCosts);

/// <summary>A night on the board at the fortress: a route and what it pays a car (spec F.1).</summary>
public sealed record Contract(RouteTier Tier, ulong Seed, double PerCar)
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
    public static double CarCost(CampaignTuning t, int n) => Math.Round(t.CarCost.Base * Math.Pow(t.CarCost.Growth, n - t.CarCost.FromCar));

    public static double NextCarCost(CampaignTuning t, CampaignState s) => CarCost(t, s.Cars + 1);

    public static double UpgradeCost(CampaignTuning t, CampaignState s, UpgradeDef u) => Math.Round(u.CostShare * NextCarCost(t, s));

    /// <summary>Spec F.4: the tier a consist this size works.</summary>
    public static RouteTier TierFor(CampaignTuning t, int cars) => t.Tiers.Where(x => cars >= x.FromCars).OrderBy(x => x.FromCars).Last().Tier;

    /// <summary>Tonight's board: the consist's own tier, and one from below if there is one. The same every time for the same night.</summary>
    public static IReadOnlyList<Contract> Offers(CampaignTuning t, RunTuning run, CampaignState s)
    {
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

    /// <summary>Starts a night on a contract from the board.</summary>
    public static CampaignState Begin(CampaignState s, Contract c) => s with { Current = c, Checkpoint = null };

    /// <summary>
    /// GDD §9 arrival: "everything still attached to the locomotive counts". The night's net goes to (or comes out of)
    /// the scrip, and cars left behind are gone from the consist.
    /// </summary>
    public static CampaignState Settle(CampaignState s, RunReport report)
    {
        int cars = Math.Max(2, s.Cars - report.CarsLost);
        double scrip = s.Scrip + report.Net;
        var log = new RunLog(s.Runs + 1, s.Current?.Route ?? "?", report.End, report.Net, report.CarsLost, scrip);
        return s with { Cars = cars, Scrip = scrip, Runs = s.Runs + 1, History = [.. s.History, log], Current = null, Checkpoint = null };
    }

    /// <summary>The night's tunings after the crew's upgrades. Upgrades with no modelled effect change nothing.</summary>
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
                    _ => l,
                };
        }
        return l;
    }

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
