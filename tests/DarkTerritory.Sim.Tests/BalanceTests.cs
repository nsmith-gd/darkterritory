using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Tests;

/// <summary>GDD §34's balance sweep, judged (T55): survivable at 2, non-trivial at 8, fair throughout.</summary>
public class BalanceTests
{
    static readonly BalanceTuning T = new(SurvivableCrew: 2, SurvivableDelivered: 0.5, NonTrivialCrew: 8, NonTrivialPunishes: 5);

    static BalanceRow Night(int crew, bool delivered, int punishes, int unfair = 0, int cars = 10, int lost = 0) =>
        new(RouteTier.Frontier, 1, crew, cars, delivered ? "Delivered" : "DawnMissed", delivered, delivered ? 3000 : -500, lost,
            new Dictionary<string, int>(), punishes, unfair, 3000, 26);

    [Fact]
    public void TheGridIsEveryCombination()
    {
        var grid = Balance.Grid([RouteTier.Frontier, RouteTier.DeadLines], [1, 2, 3], [2, 8], [6, 10]);
        Assert.Equal(2 * 3 * 2 * 2, grid.Count);
        Assert.Equal(grid.Count, grid.Distinct().Count());
    }

    [Fact]
    public void ASweepThatsSurvivableSmallAndBusyLargePasses()
    {
        var report = Balance.Judge([Night(2, true, 1), Night(2, false, 2), Night(8, true, 9), Night(8, true, 3)], T);
        Assert.True(report.Pass, string.Join("; ", report.Checks.Select(c => $"{c.Name}: {c.Detail}")));
        var two = Assert.Single(report.ByCrew, g => g.Value == 2);
        Assert.Equal(0.5, two.DeliveredRate);
        Assert.Equal(6, Assert.Single(report.ByCrew, g => g.Value == 8).MeanPunishes);
    }

    [Fact]
    public void EachTargetFailsOnItsOwn()
    {
        // Two can't get home.
        Assert.False(Check(Balance.Judge([Night(2, false, 1), Night(8, true, 9)], T), "survivable at 2").Pass);
        // Eight get home with nothing happening to them.
        Assert.False(Check(Balance.Judge([Night(2, true, 1), Night(8, true, 0)], T), "non-trivial at 8").Pass);
        // A commit without the reaction window, anywhere, fails the lot.
        var unfair = Balance.Judge([Night(2, true, 1), Night(8, true, 9, unfair: 1)], T);
        Assert.False(unfair.Pass);
        Assert.False(Check(unfair, "fair (App. A.1)").Pass);
    }

    [Fact]
    public void TrainLengthIsReportedNotJudged()
    {
        var report = Balance.Judge([Night(8, true, 9, cars: 6), Night(8, false, 12, cars: 20, lost: 3)], T);
        Assert.Equal([6, 20], report.ByCars.Select(g => g.Value));
        Assert.Equal(3, Assert.Single(report.ByCars, g => g.Value == 20).MeanCrewLost);
        Assert.DoesNotContain(report.Checks, c => c.Name.Contains("cars"));
    }

    static BalanceCheck Check(BalanceReport r, string name) => Assert.Single(r.Checks, c => c.Name == name);
}
