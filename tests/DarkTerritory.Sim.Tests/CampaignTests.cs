using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>Spec F: the campaign's economy, pinned to its tables.</summary>
public class CampaignTests
{
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(DataFile.FindContentRoot(), CampaignTuning.File));
    static readonly RunTuning R = Tuning.Run;

    [Theory]
    [InlineData(4, 1300)]
    [InlineData(5, 1610)]
    [InlineData(6, 2000)]
    [InlineData(8, 3070)]
    [InlineData(10, 4720)]
    [InlineData(12, 7260)]
    [InlineData(15, 13830)]
    [InlineData(18, 26340)]
    [InlineData(20, 40470)]
    public void CarsCostWhatSpecF2Says(int car, double cost) =>
        Assert.InRange(Campaign.Campaign.CarCost(C, car), cost * 0.99, cost * 1.01);

    [Theory]
    [InlineData(3, RouteTier.Local, 855)]
    [InlineData(6, RouteTier.Frontier, 2556.45)]
    [InlineData(10, RouteTier.Frontier, 2556.45)]
    [InlineData(15, RouteTier.DeadLines, 7889.75)]
    [InlineData(20, RouteTier.DeepTerritory, 19446.5)]
    public void NetIncomeByConsistMatchesSpecF1(int cars, RouteTier tier, double net)
    {
        Assert.Equal(tier, Campaign.Campaign.TierFor(C, cars));
        // F.1's table (note 636): what a night's stop loads on the tier, never more than all but the guard, less the 5%
        // its costs and fees come to.
        Assert.Equal(net, Campaign.Campaign.NetFor(C, R, cars, C.StandardCrew), 0);
    }

    [Fact]
    public void TheStandardCrewReachesTwentyCarsInFiftyFiveToSixtyRuns()
    {
        // Spec F.4: "roughly 55-60 successful runs to reach 20 cars".
        var reached = Campaign.Campaign.Simulate(C, R);
        Assert.InRange(reached[20], 55, 60);
        // And the phases come in order at sensible points (F.4's table, within a few nights).
        Assert.InRange(reached[10], 18, 26);
        Assert.InRange(reached[15], 34, 42);
    }

    [Fact]
    public void ACarIsBoughtWithScripOrNotAtAll()
    {
        var s = Campaign.Campaign.New(C, 1, "test", 1);
        Assert.Equal(3, s.Cars);
        var refused = Campaign.Campaign.BuyCar(C, s);
        Assert.False(refused.Ok);
        Assert.Contains("1300", refused.Refused);
        var bought = Campaign.Campaign.BuyCar(C, s with { Scrip = 2000 });
        Assert.True(bought.Ok);
        Assert.Equal(4, bought.State.Cars);
        Assert.Equal(700, bought.State.Scrip);
        Assert.False(Campaign.Campaign.BuyCar(C, s with { Cars = C.MaxCars, Scrip = 1e9 }).Ok);
    }

    [Fact]
    public void UpgradesCostAShareOfTheNextCarAndChangeTheNightsTuning()
    {
        var s = Campaign.Campaign.New(C, 1, "test", 1) with { Scrip = 10_000 };
        var tender = C.Upgrades.Single(u => u.Id == "tenderCapacity");
        var p = Campaign.Campaign.BuyUpgrade(C, s, "tenderCapacity");
        Assert.True(p.Ok);
        Assert.Equal(10_000 - Math.Round(tender.CostShare * 1300), p.State.Scrip);
        Assert.False(Campaign.Campaign.BuyUpgrade(C, p.State, "tenderCapacity").Ok);
        Assert.False(Campaign.Campaign.BuyUpgrade(C, p.State, "warpDrive").Ok);

        var base_ = new Loadout(Tuning.Train, Tuning.Boiler, Tuning.Combat, Tuning.Enemies);
        var up = Campaign.Campaign.Apply(C, ["tenderCapacity", "improvedBrakes", "boilerUpgrade", "ammunitionCapacity", "lampBrightness", "roofHandrails"], base_);
        Assert.Equal(Tuning.Boiler.TenderCapacity * 1.25, up.Boiler.TenderCapacity, 0);
        Assert.All(up.Train.Performance.Zip(Tuning.Train.Performance), x => Assert.Equal(x.Second.Brake * 1.2, x.First.Brake, 6));
        // Spec F.3 "boiler upgrade (-15% burn)": the same steam from 15% less coal.
        Assert.Equal(Tuning.Boiler.FireTimeConstant / 0.85, up.Boiler.FireTimeConstant, 6);
        Assert.Equal(Tuning.Boiler.SteamPerUnit / Tuning.Boiler.FireTimeConstant, up.Boiler.SteamPerUnit / up.Boiler.FireTimeConstant, 6);
        Assert.Equal((int)(Tuning.Combat.Guns.Ammo * 1.5), up.Combat.Guns.Ammo);
        // Lamp brightness (note 506): the headlamp's reach, everything it shows (UpgradeTests).
        Assert.Equal(1.25, up.Train.HeadlampReach, 6);
        // An upgrade the campaign doesn't sell changes nothing (every one it does sell does something: UpgradeTests, note 196).
        Assert.Equal(base_, Campaign.Campaign.Apply(C, ["warpDrive"], base_));
        // The consist's (note 184): fitted to the train, the numbers they bring in train.json composition.
        Assert.True(up.Train.Composition.Handrails);
    }

    [Fact]
    public void ANightIsSettledIntoTheCampaign()
    {
        var s = Campaign.Campaign.Begin(Campaign.Campaign.New(C, 1, "test", 1) with { Cars = 6 }, new Contract(RouteTier.Frontier, 7, 700));
        var report = new RunReport(RunEnd.Delivered, 1800, 27, 4, 1, 4, 2800, 60, 20, 40, 2680, 5, 1);
        var after = Campaign.Campaign.Settle(s, report);
        Assert.Equal(2680, after.Scrip);
        Assert.Equal(5, after.Cars); // the car left behind is gone
        Assert.Equal(1, after.Runs);
        Assert.Null(after.Current);
        var log = Assert.Single(after.History);
        Assert.Equal("frontier:7", log.Route);
        Assert.Equal(RunEnd.Delivered, log.End);
    }

    /// <summary>
    /// Note 632 (the director, the inbox, 12 Oct: "c, The campaign ends", roguelike): a train taken at dawn is settled, so its
    /// night is in the history, and the campaign's over: no board, and no night begun on it. Any other lost night, the crew
    /// goes on. With campaign.json dawnEndsCampaign off (a mod's), a dawn loss is just a lost night again.
    /// </summary>
    [Fact]
    public void ATrainTakenAtDawnEndsTheCampaign()
    {
        Assert.True(C.DawnEndsCampaign);
        var s = Campaign.Campaign.Begin(Campaign.Campaign.New(C, 1, "test", 1), new Contract(RouteTier.Local, 5, 450));
        static RunReport Lost(RunEnd end) => new(end, 1200, 0, 0, 0, 0, 0, 40, 20, 0, -60, 3, 0);

        var taken = Campaign.Campaign.Settle(s, Lost(RunEnd.DawnMissed), C);
        Assert.True(taken.Over);
        Assert.Equal(RunEnd.DawnMissed, Assert.Single(taken.History).End);
        Assert.Empty(Campaign.Campaign.Offers(C, R, taken));
        Assert.Throws<InvalidOperationException>(() => Campaign.Campaign.Begin(taken, new Contract(RouteTier.Local, 6, 450)));

        foreach (var end in new[] { RunEnd.Derailed, RunEnd.CrewLost, RunEnd.Stranded, RunEnd.Delivered })
        {
            var on = Campaign.Campaign.Settle(s, Lost(end), C);
            Assert.False(on.Over, end.ToString());
            Assert.NotEmpty(Campaign.Campaign.Offers(C, R, on));
        }
        Assert.False(Campaign.Campaign.Settle(s, Lost(RunEnd.DawnMissed), C with { DawnEndsCampaign = false }).Over);
        // A new crew in the slot starts afresh.
        Assert.False(Campaign.Campaign.New(C, 1, "next", 2).Over);
    }

    [Fact]
    public void TheBoardOffersTheConsistsTierAndOneBelow()
    {
        var s = Campaign.Campaign.New(C, 1, "test", 99) with { Cars = 8 };
        var board = Campaign.Campaign.Offers(C, R, s);
        var all = board;
        Assert.Equal(C.ContractsOffered, board.Count);
        Assert.Equal(C.ContractsOffered - 1, board.Count(c => c.Tier == RouteTier.Frontier));
        Assert.Single(board, c => c.Tier == RouteTier.Local);
        Assert.Equal(all, Campaign.Campaign.Offers(C, R, s));
        Assert.NotEqual(all.Select(c => c.Seed), Campaign.Campaign.Offers(C, R, s with { Runs = 1 }).Select(c => c.Seed));
        Assert.All(all, c => Assert.Equal(Campaign.Campaign.PerCar(R, c.Tier), c.PerCar));
        // A local crew has nothing below it.
        Assert.All(Campaign.Campaign.Offers(C, R, s with { Cars = 3 }), c => Assert.Equal(RouteTier.Local, c.Tier));
    }
    // ---- WP13: the departure's purchases (GDD §9, App. B.9; note 182); a contract is just a route (note 637) ----

    /// <summary>
    /// Note 637 ("Just a route", the director, the inbox, 12 Oct): a contract is the route and its tier, and what a car-load
    /// pays there. The train leaves empty (note 575); the freight is whatever its stops load. A save from when a contract
    /// named a freight (note 182) reads as the same route, the freight dropped.
    /// </summary>
    [Fact]
    public void AContractIsJustARoute()
    {
        var s = Campaign.Campaign.New(C, 1, "test", 99) with { Cars = 8 };
        var board = Campaign.Campaign.Offers(C, R, s);
        Assert.All(board, c => Assert.Equal(Campaign.Campaign.PerCar(R, c.Tier), c.PerCar));
        // Nothing more on the board than the routes: no comet contract alongside them.
        Assert.Equal(C.ContractsOffered, board.Count);
        var begun = Campaign.Campaign.Begin(s, board[0]);
        Assert.Equal(board[0], begun.Current);
        var old = System.Text.Json.JsonSerializer.Deserialize<Contract>("{\"tier\":\"frontier\",\"seed\":7,\"perCar\":1260,\"cargo\":\"comet\"}", DataFile.Options)!;
        Assert.Equal(new Contract(RouteTier.Frontier, 7, 1260), old);
        Assert.Equal("frontier:7", old.Route);
    }

    [Fact]
    public void TheFortressSellsPowderLampsAndExtinguishersForTheNight()
    {
        var st = C.Stores!;
        var s = Campaign.Campaign.New(C, 1, "test", 1) with { Scrip = 1000 };
        var p = Campaign.Campaign.BuyStores(C, s, StoreKind.Powder);
        Assert.True(p.Ok);
        Assert.Equal(1, p.State.Stores.Powder);
        Assert.Equal(1000 - st.Powder.Cost, p.State.Scrip);
        p = Campaign.Campaign.BuyStores(C, p.State, StoreKind.Lamp);
        p = Campaign.Campaign.BuyStores(C, p.State, StoreKind.Extinguisher);
        Assert.Equal(new Stores(1, 1, 1), p.State.Stores);
        // Up to a night's most, and only with the scrip.
        var full = p.State with { Stores = new Stores(st.Powder.Most, 0, 0) };
        Assert.False(Campaign.Campaign.BuyStores(C, full, StoreKind.Powder).Ok);
        Assert.False(Campaign.Campaign.BuyStores(C, p.State with { Scrip = 0 }, StoreKind.Lamp).Ok);
        // Not once the gates are open.
        Assert.False(Campaign.Campaign.BuyStores(C, Campaign.Campaign.Begin(p.State, Campaign.Campaign.Offers(C, R, p.State)[0]), StoreKind.Lamp).Ok);

        // Aboard: each crate of powder is more rounds for every gun; the lamps and extinguishers ride in the guard van.
        var base_ = new Loadout(Tuning.Train, Tuning.Boiler, Tuning.Combat, Tuning.Enemies);
        var l = Campaign.Campaign.WithStores(C, base_, new Stores(2, 1, 2));
        Assert.Equal(Tuning.Combat.Guns.Ammo + 2 * st.Powder.Each, l.Combat.Guns.Ammo);
        Assert.Equal(1, l.Train.Kit.SpareLamps);
        Assert.Equal(2, l.Train.Kit.SpareExtinguishers);
        Assert.Equal(base_, Campaign.Campaign.WithStores(C, base_, new Stores()));
        var train = new Train.TrainOnLine(new Train.TrainDynamics(Train.Consist.Uniform(l.Train, 4, 1)),
            new Rail.RailLine(new Rail.LineDefinition("t", [new Rail.TrackSegment(5_000)])), 600);
        var world = new World(train, Tuning.Combat);
        world.Stock();
        int guard = train.Dynamics.Consist.Vehicles[^1].Id;
        Assert.Equal(2, world.Bodies.All.Count(b => b.Kind == Physics.BodyKind.Lamp && b.Parent == guard));
        Assert.Equal(2, world.Bodies.All.Count(b => b.Kind == Physics.BodyKind.Extinguisher && b.Parent == guard && b.Home < 0));

        // Spent with the night.
        var settled = Campaign.Campaign.Settle(Campaign.Campaign.Begin(p.State, Campaign.Campaign.Offers(C, R, p.State)[0]),
            new RunReport(RunEnd.Delivered, 1800, 27, 2, 0, 2, 900, 10, 0, 0, 890, 1, 0));
        Assert.False(settled.Stores.Any);
    }

    [Fact]
    public void ACarTakenOffBringsBackHalfItsPriceAndCanDropTheTier()
    {
        var k = C.SellCar!;
        var s = Campaign.Campaign.New(C, 1, "test", 1) with { Cars = 6, Scrip = 0 };
        Assert.Equal(RouteTier.Frontier, Campaign.Campaign.TierFor(C, s.Cars));
        var p = Campaign.Campaign.SellCar(C, s);
        Assert.True(p.Ok);
        Assert.Equal(5, p.State.Cars);
        Assert.Equal(Math.Round(k.Share * Campaign.Campaign.CarCost(C, 6)), p.State.Scrip);
        Assert.Equal(RouteTier.Local, Campaign.Campaign.TierFor(C, p.State.Cars));
        // Buying it back costs the full price: selling is a loss.
        Assert.True(Campaign.Campaign.NextCarCost(C, p.State) > p.State.Scrip);
        Assert.False(Campaign.Campaign.SellCar(C, s with { Cars = k.Fewest }).Ok);
        Assert.False(Campaign.Campaign.SellCar(C, Campaign.Campaign.Begin(s, Campaign.Campaign.Offers(C, R, s)[0])).Ok);
    }
}
