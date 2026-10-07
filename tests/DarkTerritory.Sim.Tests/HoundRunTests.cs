using Ballast;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The hound run (ARCHITECTURE §8 note 328; docs/design/orchestrator.md §5.3, §6.1; GDD App. F.3, the director, 7 Oct 2026:
/// "more threats that can board the train at speed ... tower defense style that gives our gunners things to do"): a train
/// run fast draws a stream of Cinder Hounds faster than it, sized to the crew, that the guns answer one hound at a time.
/// </summary>
public class HoundRunTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly HoundRunTuning R = Tuning.Enemies.Director.Run;
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>A night at <paramref name="speed"/> with <paramref name="crew"/> in the cab, run until its first hound run (or <paramref name="seconds"/>).</summary>
    static Night FirstRun(int crew, double speed = 21, double seconds = 300)
    {
        var n = new Night(4, speed);
        for (int i = 0; i < crew; i++)
            n.Crew[i] = PlayerMotor.SpawnInCab(n.Train, P);
        for (int s = 0; s < seconds && n.World.Director!.HoundRuns.Count == 0; s++)
            n.Run(1);
        return n;
    }

    static List<CinderHound> Runners(Night n) => [.. n.World.ActiveEnemies.OfType<CinderHound>().Where(h => h.Runner)];

    [Fact]
    public void AFastTrainDrawsARunThatOutrunsItAndBoardsWhatTheGunsLetThrough()
    {
        // The director's playtest: kept hot and never stopped. 21 m/s is past the hounds' 19 (the director's packs can't
        // board it); the run's runners can.
        var n = FirstRun(4);
        var d = n.World.Director!;
        var run = Assert.Single(d.HoundRuns);
        Assert.True(n.World.ElapsedSeconds >= d.Grace);
        Assert.Equal(4, run.Size); // round(1.4 + 0.6 × 4)
        Assert.True(n.Train.Dynamics.Speed > E.CinderHounds.MaxSpeed);
        // In pairs, spacing apart: all of them out once the last pair's due.
        n.Run(R.Spacing * (run.Size / 2) + 1);
        var runners = Runners(n);
        Assert.Equal(run.Size, runners.Count);
        Assert.All(runners, h => Assert.Equal(run.Pack, h.Pack));
        // Both flanks.
        Assert.Contains(runners, h => h.Lateral > 0);
        Assert.Contains(runners, h => h.Lateral < 0);
        // Nobody shoots: every one of them reaches the rear car and boards it at 21 m/s.
        int rear = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        n.Run((R.SpawnBehind + 10) / R.Closing + E.CinderHounds.HowlSeconds + 2);
        Assert.All(runners, h => Assert.Equal(rear, h.Attached));
        Assert.Equal((0, 0, run.Size), d.RunOutcome(run.Pack));
        n.AssertFair();
        // One run at a time: with its runners still aboard, no second run, however far the train goes.
        n.Run(R.AfterMetres / 21 + 10);
        Assert.Single(d.HoundRuns);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(4, 4)]
    [InlineData(8, 6)]
    public void TheRunIsSizedToTheCrewAlive(int crew, int size)
    {
        var n = FirstRun(crew);
        Assert.Equal(size, Assert.Single(n.World.Director!.HoundRuns).Size);
    }

    [Fact]
    public void ABallLandingBetweenAPairScattersBothAndARoundFiredAnywhereElseDoesNot()
    {
        var n = FirstRun(1);
        n.Run(E.CinderHounds.HowlSeconds + 2); // the first pair running in, past its howl
        var pair = Runners(n);
        Assert.Equal(2, pair.Count);
        Assert.All(pair, h => Assert.Equal(SpinePhase.Commit, h.Phase));
        // A round fired from the train that comes down far from them: the director's packs would go (suppressRounds 1); a
        // runner doesn't.
        var far = n.Train.Frames[0].ToWorld(new Double3(0, 0, -150));
        Land(n, far);
        Assert.All(pair, h => Assert.False(h.Gone));
        // One good shot between them takes both: neither boards.
        var between = (pair[0].WorldPosition(n.Train) + pair[1].WorldPosition(n.Train)) * 0.5;
        Assert.All(pair, h => Assert.True((h.WorldPosition(n.Train) - between).Length <= R.Scatter));
        Land(n, between);
        Assert.All(pair, h => Assert.True(h.Gone));
        Assert.Contains(n.Events, e => e.EnemyId == pair[0].Id && e.To == SpinePhase.BreakOff);
        Assert.All(pair, h => Assert.True(h.Attached < 0));
        Assert.Equal((2, 0, 0), n.World.Director!.RunOutcome(pair[0].Pack));
    }

    /// <summary>A ball from the rear of the train that comes down at <paramref name="at"/> this tick.</summary>
    static void Land(Night n, Double3 at)
    {
        var muzzle = n.Train.Frames[n.Train.Dynamics.Consist.Vehicles[^1].Id].ToWorld(Double3.Zero);
        bool fired = false;
        n.Run(SimConstants.TickSeconds, _ =>
        {
            if (!fired)
                n.World.Shots.Add(new GunShot(0, 0, muzzle, (at - muzzle).Normalized, (at - muzzle).Length, -1, false, at, ImpactSurface.Ground));
            fired = true;
            return default;
        });
    }

    [Theory]
    [InlineData(12)] // under fromSpeed: hauling, not running
    [InlineData(1)] // at a stand
    public void ASlowTrainDrawsNoRun(double speed)
    {
        var n = FirstRun(4, speed, seconds: E.Director.GraceMaxSeconds + R.AfterMetres / 12 + 60);
        Assert.Empty(n.World.Director!.HoundRuns);
    }

    [Fact]
    public void SlowingStartsTheCountAgain()
    {
        // Run most of the way to a run, slow under stopSpeed for a second, and the count's gone: it takes the whole
        // afterMetres again.
        var n = new Night(4, 21);
        n.Crew[0] = PlayerMotor.SpawnInCab(n.Train, P);
        var d = n.World.Director!;
        n.Run(d.Grace + 1);
        while (d.RunMetres < R.AfterMetres * 0.8)
            n.Run(1);
        n.Train.Dynamics.Velocity = 1;
        n.Run(2);
        Assert.Equal(0, d.RunMetres);
        Assert.Empty(d.HoundRuns);
        n.Train.Dynamics.Velocity = 21;
        n.Run(R.AfterMetres * 0.8 / 21);
        Assert.Empty(d.HoundRuns);
        n.Run(R.AfterMetres * 0.2 / 21 + 3);
        Assert.Single(d.HoundRuns);
    }
}
