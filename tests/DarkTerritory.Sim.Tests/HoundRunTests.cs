using Ballast;
using DarkTerritory.Sim.Bots;
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

    /// <summary>
    /// The director spending nothing of its own (its pressure never reaches its threshold, no first draw): the run alone, so
    /// the live crew's cap (note 336) doesn't hold it behind the director's threats.
    /// </summary>
    internal static readonly EnemyTuning Quiet = E with
    {
        Director = E.Director with
        {
            Pressure = E.Director.Pressure with { Threshold = 1e9, PressAt = 2e9, Max = 3e9 },
            Draw = E.Director.Draw with { Enabled = false },
        },
    };

    /// <summary>A night at <paramref name="speed"/> with <paramref name="crew"/> in the cab, run until its first hound run (or <paramref name="seconds"/>).</summary>
    static Night FirstRun(int crew, double speed = 21, double seconds = 300)
    {
        var n = new Night(4, speed, enemies: Quiet);
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
        // Nobody shoots: every one of them reaches the train and boards it at 21 m/s: the first pair the rear car from behind,
        // the second (the lane ahead, note 405) the first car behind the engine from in front.
        int rear = n.Train.Dynamics.Consist.Vehicles[^1].Id, first = n.Train.Dynamics.Consist.Vehicles[1].Id;
        Assert.Equal(2, runners.Count(h => h.Ahead));
        n.Run((R.SpawnBehind + 10) / R.Closing + E.CinderHounds.HowlSeconds + 2);
        Assert.All(runners, h => Assert.Equal(h.Ahead ? first : rear, h.Attached));
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
    [InlineData(18)] // under fromSpeed, the pack's own 19: the director's packs can still board it, and outrunning them is the counter
    [InlineData(12)] // hauling
    [InlineData(1)] // at a stand
    public void ASlowTrainDrawsNoRun(double speed)
    {
        var n = FirstRun(4, speed, seconds: E.Director.GraceMaxSeconds + R.AfterMetres / 12 + 60);
        Assert.Empty(n.World.Director!.HoundRuns);
    }

    [Fact]
    public void TheGunnerAtTheGuardGunAnswersTheRun()
    {
        // The gunners' wave (App. F.3): the gunner bot at the guard van's gun, a run of two coming up behind a 21 m/s train.
        var e = Quiet with { Director = Quiet.Director with { Run = R with { AfterMetres = 200 } } };
        var n = new Night(4, 21, enemies: e);
        int rear = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        var mount = Guns.Mount(n.Train, rear)!.Value;
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, rear, mount.Position.Z + 0.7, P) with { Yaw = Math.PI };
        var gunner = new GunnerBot(Tuning.Combat.Guns);
        var d = n.World.Director!;
        for (int s = 0; s < 300 && d.HoundRuns.Count == 0; s++)
            n.Run(1, id => gunner.Decide(n.Crew[id], n.World, n.World.Tick, out _));
        var run = Assert.Single(d.HoundRuns);
        n.Run((R.SpawnBehind + 10) / R.Closing + E.CinderHounds.HowlSeconds + 2, id => gunner.Decide(n.Crew[id], n.World, n.World.Tick, out _));
        var (scattered, killed, boarded) = d.RunOutcome(run.Pack);
        Assert.True(n.Shots.Count > 0);
        // One gunner, a run of two: 7 rounds, both killed on the line, neither aboard (first pass).
        Assert.True(scattered + killed == run.Size && boarded == 0, $"rounds {n.Shots.Count}: scattered {scattered}, killed {killed}, aboard {boarded}");
        n.AssertFair();
    }

    /// <summary>Every pair of a run from ahead (note 405), to test the lane on its own.</summary>
    static readonly EnemyTuning AllAhead = Quiet with { Director = Quiet.Director with { Run = R with { AheadEvery = 1 } } };

    [Fact]
    public void TheLaneAheadComesAcrossTheLineInTheLampAndLeapsOntoTheFirstCar()
    {
        // Note 405 (orchestrator.md §5.3 6): put down in front of the engine off to a flank, they howl, run in to meet the
        // train, cross the line in front of it and are aboard the first car behind the engine as it comes by.
        var n = new Night(4, 21, enemies: AllAhead);
        n.Crew[0] = PlayerMotor.SpawnInCab(n.Train, P);
        var d = n.World.Director!;
        for (int s = 0; s < 300 && d.HoundRuns.Count == 0; s++)
            n.Run(1);
        var pair = Runners(n);
        Assert.Equal(2, pair.Count);
        Assert.All(pair, h => Assert.True(h.Ahead));
        double front = n.Train.Dynamics.Distance;
        Assert.All(pair, h => Assert.InRange(h.LineDistance - front, R.AheadMetres - 30, R.AheadMetres + 10));
        int flank = Math.Sign(pair[0].Lateral);
        Assert.All(pair, h => Assert.Equal(flank, Math.Sign(h.Lateral)));
        Assert.All(pair, h => Assert.True(Math.Abs(h.Lateral) >= R.AheadLateral[0] - 1));
        // They hold in the lamp for the howl: the gun's mark doesn't move along the line.
        double held = pair[0].LineDistance;
        n.Run(E.CinderHounds.HowlSeconds - 1.5); // drawn up to a second ago
        Assert.Equal(held, pair[0].LineDistance, 6);
        Assert.Equal(SpinePhase.Telegraph, pair[0].Phase);
        // Running in: across the line before the train's on them.
        int first = n.Train.Dynamics.Consist.Vehicles[1].Id;
        for (int i = 0; i < 40 * SimConstants.TickRate && pair.Any(h => h.Attached < 0); i++)
            n.Run(SimConstants.TickSeconds);
        Assert.All(pair, h => Assert.Equal(first, h.Attached));
        Assert.All(pair, h => Assert.True(h.Local.Z < 0, "at the car's front end"));
        Assert.All(pair, h => Assert.Equal(-flank, Math.Sign(h.Local.X))); // crossed: aboard on the far side
        Assert.Equal((0, 0, 2), d.RunOutcome(pair[0].Pack));
        Assert.Equal(1, d.AheadPairs);
        n.AssertFair();
    }

    [Fact]
    public void WithNoGunLaidForwardTheRunAllComesFromBehind()
    {
        var n = new Night(4, 21, enemies: AllAhead);
        n.Crew[0] = PlayerMotor.SpawnInCab(n.Train, P);
        foreach (var v in n.Train.Vehicles)
            if (v.Gun.Facing < 0)
                v.Gun = default;
        var d = n.World.Director!;
        for (int s = 0; s < 300 && d.HoundRuns.Count == 0; s++)
            n.Run(1);
        Assert.Single(d.HoundRuns);
        Assert.All(Runners(n), h => Assert.False(h.Ahead));
        Assert.Equal(0, d.AheadPairs);
    }

    [Fact]
    public void TheGunnerAtTheForwardGunAnswersTheLaneAhead()
    {
        // The forward gun's wave (note 405): the gunner bot at the engine's gun, a pair coming in ahead of a 21 m/s train.
        var e = AllAhead with { Director = AllAhead.Director with { Run = AllAhead.Director.Run with { AfterMetres = 200 } } };
        var n = new Night(4, 21, enemies: e);
        int engine = n.Train.Dynamics.Consist.Vehicles.First(v => n.Train.Vehicles[v.Id].Gun is { Mounted: true, Facing: < 0 }).Id;
        var mount = Guns.Mount(n.Train, engine)!.Value;
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, engine, mount.Position.Z + 0.7, P) with { Yaw = 0 };
        var gunner = new GunnerBot(Tuning.Combat.Guns);
        var d = n.World.Director!;
        for (int s = 0; s < 300 && d.HoundRuns.Count == 0; s++)
            n.Run(1, id => gunner.Decide(n.Crew[id], n.World, n.World.Tick, out _));
        var run = Assert.Single(d.HoundRuns);
        n.Run(R.AheadMetres / 21 + E.CinderHounds.HowlSeconds + 4, id => gunner.Decide(n.Crew[id], n.World, n.World.Tick, out _));
        var (scattered, killed, boarded) = d.RunOutcome(run.Pack);
        Assert.True(n.Shots.Count > 0);
        Assert.True(scattered + killed == run.Size && boarded == 0, $"rounds {n.Shots.Count}: scattered {scattered}, killed {killed}, aboard {boarded}");
        n.AssertFair();
    }

    [Fact]
    public void TheForwardGunnerGoesForwardOntoTheEngineAndAnswersTheLaneAhead()
    {
        // Note 414: a bot crew's second gunner, put down on car 2's roof, walks forward, jumps onto the engine's hood, goes
        // round the stack to the gun's seat and answers the pair coming in ahead.
        var e = AllAhead with { Director = AllAhead.Director with { Run = AllAhead.Director.Run with { AfterMetres = 2000 } } };
        var n = new Night(4, 21, enemies: e);
        int engine = n.Train.Dynamics.Consist.Vehicles[0].Id, car2 = n.Train.Dynamics.Consist.Vehicles[2].Id;
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, car2, 0, P);
        var gunner = new GunnerBot(Tuning.Combat.Guns) { Forward = true };
        Assert.Equal("forward-gunner", gunner.Name);
        var guns = Tuning.Combat.Guns;
        var d = n.World.Director!;
        double seated = -1;
        for (int s = 0; s < 300 && d.HoundRuns.Count == 0; s++)
        {
            n.Run(1, id => gunner.Decide(n.Crew[id], n.World, n.World.Tick, out _));
            if (seated < 0 && Guns.MannedGun(n.Crew[1], n.Train, guns) == engine && n.Crew[1].Has(PlayerFlags.Seated))
                seated = n.World.ElapsedSeconds;
        }
        Assert.True(seated >= 0, $"never seated at the engine's gun: on {n.Crew[1].Parent} {n.Crew[1].Surface} at {n.Crew[1].Position}");
        Assert.True(seated < 60, $"seated after {seated} s");
        var run = Assert.Single(d.HoundRuns);
        n.Run(R.AheadMetres / 21 + E.CinderHounds.HowlSeconds + 4, id => gunner.Decide(n.Crew[id], n.World, n.World.Tick, out _));
        var (scattered, killed, boarded) = d.RunOutcome(run.Pack);
        Assert.True(scattered + killed == run.Size && boarded == 0, $"rounds {n.Shots.Count}: scattered {scattered}, killed {killed}, aboard {boarded}");
        n.AssertFair();
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(6, true)]
    [InlineData(8, true)]
    public void ABotCrewOfSixOrMoreHasAForwardGunnerInItsLastPlace(int count, bool forward)
    {
        var crew = Enumerable.Range(0, count).Select(i => BotCrew.Make(i, count, null, Tuning.Combat, P, 1)).ToList();
        Assert.Equal(forward ? 1 : 0, crew.Count(b => b is GunnerBot { Forward: true }));
        Assert.Equal(1, crew.Count(b => b is GunnerBot { Forward: false }));
        if (forward)
            Assert.True(crew[^1] is GunnerBot { Forward: true });
    }

    [Fact]
    public void SlowingStartsTheCountAgain()
    {
        // Run most of the way to a run, slow under stopSpeed for a second, and the count's gone: it takes the whole
        // afterMetres again.
        var n = new Night(4, 21, enemies: Quiet);
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
