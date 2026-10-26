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

    static readonly Dictionary<string, double> Shares = new() { ["kill"] = 0.40, ["split"] = 0.25, ["trust"] = 0.20, ["cargo"] = 0.15 };

    static BalanceRow Spent(double kill, double split, double trust, double cargo) => Night(4, true, 3) with
    {
        SpentByWant = new Dictionary<string, double> { ["kill"] = kill, ["split"] = split, ["trust"] = trust, ["cargo"] = cargo },
    };

    [Fact]
    public void WantBalanceIsTheSweepsSpendAgainstEachWantsShare()
    {
        // App. B.10 "Want balance: generated runs land near the target shares" (note 1032), over the whole sweep's spend.
        var near = Balance.Judge([Spent(30, 30, 20, 20), Spent(50, 20, 20, 10)], T, Shares);
        var check = Assert.Single(near.Checks, c => c.Name.StartsWith("want balance"));
        Assert.True(check.Pass, check.Detail);
        Assert.Matches(@"kill 40\s?% \(of 40\s?%\)", check.Detail);
        // Kill taking two-thirds of the spend is off its share by far more than the tolerance.
        var kills = Balance.Judge([Spent(70, 10, 10, 10), Spent(60, 10, 10, 20)], T, Shares);
        Assert.False(Assert.Single(kills.Checks, c => c.Name.StartsWith("want balance")).Pass);
        Assert.False(kills.Pass);
        // Too little spent to have shares, or no targets: not judged.
        Assert.DoesNotContain(Balance.Judge([Spent(10, 0, 0, 0)], T, Shares).Checks, c => c.Name.StartsWith("want balance"));
        Assert.DoesNotContain(Balance.Judge([Spent(70, 10, 10, 10)], T).Checks, c => c.Name.StartsWith("want balance"));
    }

    [Fact]
    public void DeepIsJudgedAgainstLocalOnlyAtMatchedCrewAndCars()
    {
        // App. B.10 "Tier progression: Deep territory is meaningfully harder than Local at matched crew and length" (note 1032).
        BalanceRow At(RouteTier tier, bool delivered, int punishes, int crew = 4, int cars = 6) => Night(crew, delivered, punishes, cars: cars, tier: tier);
        var harder = Balance.Judge([At(RouteTier.Local, true, 2), At(RouteTier.Local, true, 4), At(RouteTier.DeepTerritory, true, 6),
            At(RouteTier.DeepTerritory, false, 9)], T);
        var check = Assert.Single(harder.Checks, c => c.Name.StartsWith("deepTerritory harder"));
        Assert.True(check.Pass, check.Detail);
        Assert.Equal("deepTerritory harder than local (crew 4, 6 cars)", check.Name);
        // Deep getting home more often, or its threats landing little more than Local's, isn't harder.
        Assert.False(Assert.Single(Balance.Judge([At(RouteTier.Local, false, 2), At(RouteTier.DeepTerritory, true, 9)], T).Checks,
            c => c.Name.StartsWith("deepTerritory")).Pass);
        Assert.False(Assert.Single(Balance.Judge([At(RouteTier.Local, true, 4), At(RouteTier.DeepTerritory, true, 4)], T).Checks,
            c => c.Name.StartsWith("deepTerritory")).Pass);
        // Unmatched (another crew size or train) isn't compared at all.
        Assert.DoesNotContain(Balance.Judge([At(RouteTier.Local, true, 2, crew: 2), At(RouteTier.DeepTerritory, true, 9, crew: 8)], T).Checks,
            c => c.Name.StartsWith("deepTerritory"));
    }
}
