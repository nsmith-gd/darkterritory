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
    [InlineData(3, RouteTier.Local, 765)]
    [InlineData(6, RouteTier.Frontier, 2975)]
    [InlineData(10, RouteTier.Frontier, 5355)]
    [InlineData(15, RouteTier.DeadLines, 13090)]
    [InlineData(20, RouteTier.DeepTerritory, 27455)]
    public void NetIncomeByConsistMatchesSpecF1(int cars, RouteTier tier, double net)
    {
        Assert.Equal(tier, Campaign.Campaign.TierFor(C, cars));
        // F.1's table is gross less the average 15% running costs, with no losses.
        Assert.Equal(net, Campaign.Campaign.NetFor(C, R, cars, C.StandardCrew with { LossFraction = 0 }), 0);
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
        Assert.Equal(Tuning.Enemies.Sleepers.LampRevealDistance * 1.25, up.Enemies!.Sleepers.LampRevealDistance, 6);
        // Not modelled yet: bought, saved, and no effect.
        Assert.Equal(base_, Campaign.Campaign.Apply(C, ["lampArmour"], base_));
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

    [Fact]
    public void TheBoardOffersTheConsistsTierAndOneBelow()
    {
        var s = Campaign.Campaign.New(C, 1, "test", 99) with { Cars = 8 };
        var board = Campaign.Campaign.Offers(C, R, s);
        Assert.Equal(C.ContractsOffered, board.Count);
        Assert.Equal(C.ContractsOffered - 1, board.Count(c => c.Tier == RouteTier.Frontier));
        Assert.Single(board, c => c.Tier == RouteTier.Local);
        Assert.Equal(board, Campaign.Campaign.Offers(C, R, s));
        Assert.NotEqual(board.Select(c => c.Seed), Campaign.Campaign.Offers(C, R, s with { Runs = 1 }).Select(c => c.Seed));
        Assert.All(board, c => Assert.Equal(Campaign.Campaign.PerCar(R, c.Tier), c.PerCar));
        // A local crew has nothing below it.
        Assert.All(Campaign.Campaign.Offers(C, R, s with { Cars = 3 }), c => Assert.Equal(RouteTier.Local, c.Tier));
    }
}
