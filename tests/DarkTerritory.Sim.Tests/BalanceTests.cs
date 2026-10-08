using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Tests;

/// <summary>GDD §34's balance sweep, judged (T55): survivable at 2, non-trivial at 8, fair throughout.</summary>
public class BalanceTests
{
    static readonly BalanceTuning T = new(SurvivableCrew: 2, SurvivableDelivered: 0.5, NonTrivialCrew: 8, NonTrivialPunishes: 5);

    static BalanceRow Night(int crew, bool delivered, int punishes, int unfair = 0, int cars = 10, int lost = 0, RouteTier tier = RouteTier.Frontier) =>
        new(tier, 1, crew, cars, delivered ? "Delivered" : "DawnMissed", delivered, delivered ? 3000 : -500, lost,
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

    [Fact]
    public void SoloFinishesItsFirstRunsAndNoMore()
    {
        // App. F.1 (note 300): alone, the first runs' line and train get home; where a crew advances to, they mostly don't.
        BalanceRow Solo(RouteTier tier, int cars, bool delivered) => Night(1, delivered, 0, cars: cars, tier: tier);
        var ok = Balance.Judge([Solo(RouteTier.Local, 3, true), Solo(RouteTier.Local, 3, true), Solo(RouteTier.Local, 3, false),
            Solo(RouteTier.Frontier, 6, false), Solo(RouteTier.Frontier, 6, false), Solo(RouteTier.Frontier, 6, true)], T);
        Assert.True(Check(ok, "solo finishes local at 3 cars").Pass, Check(ok, "solo finishes local at 3 cars").Detail);
        Assert.True(Check(ok, "solo is hard on frontier at 6 cars").Pass, Check(ok, "solo is hard on frontier at 6 cars").Detail);
        // Can't finish a first run alone; or can go anywhere alone.
        Assert.False(Check(Balance.Judge([Solo(RouteTier.Local, 3, false), Solo(RouteTier.Local, 3, true)], T), "solo finishes local at 3 cars").Pass);
        Assert.False(Check(Balance.Judge([Solo(RouteTier.Frontier, 6, true), Solo(RouteTier.Frontier, 6, false)], T), "solo is hard on frontier at 6 cars").Pass);
        // Not judged without solo nights in the cell (a crew of 2 there, or solo on another train).
        Assert.DoesNotContain(Balance.Judge([Night(2, true, 1, cars: 3, tier: RouteTier.Local), Solo(RouteTier.Frontier, 10, true)], T).Checks,
            c => c.Name.StartsWith("solo"));
    }

    static BalanceCheck Check(BalanceReport r, string name) => Assert.Single(r.Checks, c => c.Name == name);

    [Fact]
    public void ThePacingTargetsAreReportedAndAdvisoryTheyNeverFailTheSweep()
    {
        // Note 379 (orchestrator.md §4): P1, nobody slack past the press for long; P3, something engaged for 45-60 % of the night.
        var busy = Night(2, true, 1) with { SlackOver = 10, QuietShare = 0.5 };
        var idle = Night(8, true, 9) with { SlackOver = 120, QuietShare = 0.8 };
        var report = Balance.Judge([busy, idle], T);
        var p1 = Assert.Single(report.Checks, c => c.Name.StartsWith("P1"));
        var p3 = Assert.Single(report.Checks, c => c.Name.StartsWith("P3"));
        Assert.False(p1.Pass);
        Assert.Contains("120", p1.Detail);
        Assert.False(p3.Pass); // 35 % engaged on average
        Assert.True(p1.Advisory && p3.Advisory);
        Assert.True(report.Pass, "advisory checks don't fail the sweep");
        // Within the targets, both pass; set them in earnest (advisory off) and a miss fails it.
        Assert.All(Balance.Judge([busy, busy], T).Checks.Where(c => c.Name.StartsWith('P')), c => Assert.True(c.Pass, c.Detail));
        var strict = T with { Pacing = new PacingTargets { Advisory = false } };
        Assert.False(Balance.Judge([busy, idle], strict).Pass);
    }

    [Fact]
    public void ACrewOfOneHasNoPacingTargets()
    {
        // The census doesn't steer a crew of one (note 345): its nights aren't judged by P1 and P3.
        var report = Balance.Judge([Night(1, true, 1) with { SlackOver = 500, QuietShare = 0.9 }], T);
        Assert.DoesNotContain(report.Checks, c => c.Name.StartsWith('P'));
    }
}
