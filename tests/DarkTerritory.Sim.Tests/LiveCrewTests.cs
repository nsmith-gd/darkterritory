using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The orchestrator's live crew (ARCHITECTURE §8 note 336; docs/design/orchestrator.md §3.2 1, 3, 5; GDD App. F.3, the
/// director, 7 Oct 2026: "orchestrates threats based on the number of active players in the game currently"): the budget
/// spends like the crew alive now, and the engaged cap goes by it: a crew of one meets one thing at a time.
/// </summary>
public class LiveCrewTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly DirectorTuning D = Tuning.Enemies.Director;
    static readonly PlayerTuning P = Tuning.Player;

    static Night Crewed(int crew, double speed = 14, EnemyTuning? enemies = null)
    {
        var n = new Night(4, speed, enemies: enemies);
        for (int i = 0; i < crew; i++)
            n.Crew[i] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Run(1.1);
        return n;
    }

    static double Multiplier(int crew) => Math.Min(D.CrewCap, D.CrewBase + D.CrewPerPlayer * crew);

    [Fact]
    public void TheBudgetSpendsLikeTheCrewAliveNow()
    {
        var n = Crewed(4);
        var d = n.World.Director!;
        Assert.Equal(4, d.Active);
        double four = d.Budget;
        // Two of them die: from the next second the budget is a crew of two's.
        foreach (int id in new[] { 2, 3 })
            n.Crew[id] = n.Crew[id] with { Health = 0, Death = DeathCause.Mauled };
        n.Run(1.1);
        Assert.Equal(2, d.Active);
        Assert.Equal(four * Multiplier(2) / Multiplier(4), d.Budget, 9);
    }

    [Fact]
    public void WithLiveCrewOffTheBudgetIsTheCrewTheNightStartedWith()
    {
        var off = E with { Director = D with { Orchestrator = D.Orchestrator with { LiveCrew = false } } };
        var n = Crewed(4, enemies: off);
        var d = n.World.Director!;
        double four = d.Budget;
        foreach (int id in new[] { 2, 3 })
            n.Crew[id] = n.Crew[id] with { Health = 0, Death = DeathCause.Mauled };
        n.Run(1.1);
        Assert.Equal(2, d.Active);
        Assert.Equal(four, d.Budget, 9);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(5, 4)]
    [InlineData(8, 6)]
    public void TheEngagedCapGoesByTheCrewActive(int crew, int cap)
    {
        Assert.Equal(cap, Crewed(crew).World.Director!.EngagedCap);
    }

    [Fact]
    public void WithTheOrchestratorOffTheCapsAreAppBsFlatOnes()
    {
        var off = E with { Director = D with { Orchestrator = D.Orchestrator with { On = false } } };
        Assert.Equal(D.MaxConcurrentSmallCrew, Crewed(1, enemies: off).World.Director!.EngagedCap);
    }

    [Fact]
    public void ACrewOfOneIsNeverSentASecondThreatWhileOneIsOnThem()
    {
        // A long solo night on the line: every spawn the director made, it made with nothing else engaged.
        var n = Crewed(1);
        n.Run(900);
        var d = n.World.Director!;
        Assert.NotEmpty(d.Log);
        // ActiveTotal counts the spawn itself.
        Assert.All(d.Log, l => Assert.True(l.ActiveTotal <= 1, $"{l}"));
        n.AssertFair();
    }

    [Fact]
    public void AtTheCapTheHoundRunWaitsAndComesOnceTheCrewIsFree()
    {
        // Solo at 21 m/s, a hound on the rear car the whole way (it stays aboard, note 269): the run's count banks past
        // afterMetres, but nothing's sent until it's dealt with.
        var n = Crewed(1, speed: 21, enemies: HoundRunTests.Quiet);
        var d = n.World.Director!;
        int rear = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        var shape = n.Train.Frames[rear].Shape;
        var aboard = n.World.AddEnemy(id => new CinderHound(id, id) { Health = E.CinderHounds.Health });
        aboard.Restore(SpinePhase.Commit, 0, E.CinderHounds.Health, rear, new Ballast.Double3(0.6, shape.RoofHeight, shape.HalfLength - 2.5), 0, 0, 0, 0, 0);
        n.Run(d.Grace + D.Run.AfterMetres / 21 + 20);
        Assert.Empty(d.HoundRuns);
        Assert.True(d.RunMetres >= D.Run.AfterMetres);
        // Dealt with: the hound, and the fire it set in the car while it was left alone (that's engaged with the crew too).
        Assert.Contains(n.World.ActiveEnemies, e => e is CarFire { Gone: false });
        foreach (var e in n.World.ActiveEnemies.Where(e => !e.Gone).ToList())
            e.Dismiss();
        n.Run(2);
        Assert.Single(d.HoundRuns);
    }
}
