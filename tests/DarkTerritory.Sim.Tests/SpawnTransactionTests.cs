using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The spawn transaction (the spawn director overhaul's issue 3, docs/design/spawn-director-overhaul.md §2; ARCHITECTURE §8
/// notes 874 and 885): the director chooses (<see cref="Director.Choose"/>), the world places by the kind's rule, and only a
/// placed attempt is accounted (<see cref="Director.Commit"/>): pressure, budget, cooldown, log, the night's caps. A failed one
/// returns its opportunity with a reason, and a pack is one encounter of several members.
/// </summary>
public class SpawnTransactionTests
{
    static readonly DirectorTuning D = Tuning.Enemies.Director;

    /// <summary>A pressure that spends whenever its cooldown allows (never pressed through it), no first-threat draw, no free spawns.</summary>
    static EnemyTuning Spending() => Tuning.Enemies with
    {
        Director = D with
        {
            Pressure = D.Pressure with { Threshold = 0, PressAt = 1e9 },
            Draw = D.Draw with { Enabled = false },
            Curve = D.Curve with { Free = false },
        },
    };

    /// <summary>The pressure never spends; the free spawns fire without waiting for a first threat.</summary>
    static EnemyTuning FreeOnly() => Tuning.Enemies with
    {
        Director = D with
        {
            Pressure = D.Pressure with { Threshold = 1e9, Max = 1e9, PressAt = 1e9 },
            Curve = D.Curve with { AfterFirst = false },
            NightChance = D.Costs.Keys.Where(k => !D.Curve.Kinds.ContainsKey(k)).ToDictionary(k => k, _ => 0.0),
        },
    };

    /// <summary>A world on a plain 40 km line, the train <paramref name="at"/> m along at <paramref name="speed"/> m/s.</summary>
    static World Plain(EnemyTuning tuning, double at = 10_000, double speed = 0, ulong seed = 1, int crew = 4)
    {
        var line = new Rail.RailLine(new Rail.LineDefinition("t", [new Rail.TrackSegment(40_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, at);
        train.Dynamics.Velocity = speed;
        var world = new World(train, Tuning.Combat);
        world.EnableEnemies(tuning, null, seed, crew, authority: true);
        return world;
    }

    /// <summary>The director's choices, a second at a time from <paramref name="from"/>, until one of <paramref name="channel"/>.</summary>
    static SpawnAttempt First(World world, SpawnChannel channel, ref int second, int within = 1_800)
    {
        var d = world.Director!;
        for (int end = second + within; second < end; second++)
            if (d.Choose(world, 1_000 + second, [], 500) is { } a && a.Channel == channel)
            {
                second++;
                return a;
            }
        throw new Xunit.Sdk.XunitException($"no {channel} attempt in {within} s");
    }

    [Fact]
    public void AFailedPressureSpendConsumesNoPressureBudgetCooldownOrLogLine()
    {
        var world = Plain(Spending());
        var d = world.Director!;
        int second = 0;
        var attempt = First(world, SpawnChannel.Pressure, ref second);
        // Chosen is not spent.
        double pressure = d.Pressure, spent = d.Spent;
        Assert.Empty(d.Log);
        d.Commit(world, attempt, SpawnOutcome.Failed(SpawnFailure.NoSpot), []);
        Assert.Equal(pressure, d.Pressure);
        Assert.Equal(spent, d.Spent);
        Assert.Empty(d.Log);
        Assert.Equal(0, d.SentTonight(attempt.Kind));
        Assert.Null(d.First);
        Assert.Equal("failed (NoSpot)", d.HeldBecause);
        Assert.Equal(new SpawnAttemptResult(attempt.Tick, attempt.Kind, SpawnChannel.Pressure, SpawnFailure.NoSpot, 0, attempt.Distance), d.Attempts.Single());
        // No cooldown started: the next second it chooses again, and not the kind that just failed (it sits out a while).
        var again = d.Choose(world, 1_000 + second++, [], 500);
        Assert.NotEqual("cooldown", d.HeldBecause);
        Assert.NotNull(again);
        Assert.NotEqual(attempt.Kind, again.Kind);
        // Placed, it's accounted: the budget and the pressure charged, the log written, and the cooldown it drew begun.
        double before = d.Pressure;
        d.Commit(world, again, SpawnOutcome.Of([world.NextEnemyId]), []);
        Assert.True(d.Spent > spent);
        Assert.Equal(Math.Max(0, before - D.Pressure.ReliefPerCost * d.Cost(again.Kind)), d.Pressure, 9);
        Assert.Equal(again.Kind, d.Log.Single().Kind);
        Assert.Equal(1, d.Log.Single().Members);
        Assert.NotNull(d.First);
        Assert.Null(d.Choose(world, 1_000 + second, [], 500));
        Assert.Equal("cooldown", d.HeldBecause);
    }

    [Fact]
    public void TheFailedKindSitsOutAWhileAndThenMayBeChosenAgain()
    {
        var world = Plain(Spending());
        var d = world.Director!;
        int second = 0;
        var failed = First(world, SpawnChannel.Pressure, ref second);
        d.Commit(world, failed, SpawnOutcome.Failed(SpawnFailure.NoSpot), []);
        double hold = D.Encounters.Attempts.FailHoldSeconds;
        Assert.True(hold > 0);
        // Chosen and failed again and again: never the held kind within its hold.
        for (int i = 1; i < hold; i++)
            if (d.Choose(world, 1_000 + second++, [], 500) is { } a)
            {
                Assert.NotEqual(failed.Kind, a.Kind);
                d.Commit(world, a, SpawnOutcome.Failed(SpawnFailure.NoSpot), []);
            }
        Assert.Empty(d.Log);
        Assert.All(d.Attempts, r => Assert.False(r.Placed));
    }

    [Fact]
    public void APackIsOneEncounterOfSeveralMembers()
    {
        // Behind a train at speed: the Cinder Hounds' rule puts in a pack.
        var world = Plain(Spending(), speed: 20);
        var d = world.Director!;
        var attempt = d.Attempt(world, EnemyKind.CinderHound, SpawnChannel.Pressure, world.ActiveEnemies);
        var outcome = Spawns.For(EnemyKind.CinderHound)!.Spawn(new SpawnContext(world, world.Enemies!, d));
        Assert.True(outcome.Placed);
        var hounds = world.ActiveEnemies.Where(e => e.Kind == EnemyKind.CinderHound).Select(e => e.Id).ToList();
        Assert.True(hounds.Count >= 2, $"a pack of {hounds.Count}");
        Assert.Equal(hounds, outcome.Members);
        d.Commit(world, attempt, outcome, world.ActiveEnemies);
        // One log line, one toward the night's cap and variety, charged once, however many came.
        var line = Assert.Single(d.Log);
        Assert.Equal(hounds.Count, line.Members);
        Assert.Equal(1, d.SentTonight(EnemyKind.CinderHound));
        Assert.Equal(d.Cost(EnemyKind.CinderHound), d.Spent, 9);
        Assert.Equal(hounds.Count, d.Attempts.Single().Members);
    }

    [Fact]
    public void AFreeSpawnThatFailsStaysOwedAndCommitsNothing()
    {
        var world = Plain(FreeOnly(), at: 19_000);
        var d = world.Director!;
        int second = 0;
        var attempt = First(world, SpawnChannel.Free, ref second);
        Assert.True(attempt.Free);
        d.Commit(world, attempt, SpawnOutcome.Failed(SpawnFailure.NoGap), []);
        Assert.Empty(d.Log);
        Assert.Equal(0, d.SentTonight(attempt.Kind));
        // Owed: the very next second fires again without waiting on the curve's roll (at the end of the night, 18 an hour,
        // that's one in two hundred), and not the kind that failed.
        var again = d.Choose(world, 1_000 + second, [], 500);
        Assert.NotNull(again);
        Assert.Equal(SpawnChannel.Free, again.Channel);
        Assert.NotEqual(attempt.Kind, again.Kind);
        d.Commit(world, again, SpawnOutcome.Of([world.NextEnemyId]), []);
        Assert.True(Assert.Single(d.Log).Free);
        Assert.Equal(0, d.Spent);
    }

    [Fact]
    public void AStopsOwnThatFailsKeepsItsSlot()
    {
        var world = Plain(Spending());
        var d = world.Director!;
        var attempt = d.Attempt(world, EnemyKind.Ribbit, SpawnChannel.Stop, []) with { Stop = (7, false) };
        // The stop as stood at: its seconds counted, nothing put in.
        d.Choose(world, 1_000, [], 500);
        Assert.False(d.StopsStood.ContainsKey(7));
        d.Commit(world, attempt, SpawnOutcome.Failed(SpawnFailure.NoSite), []);
        Assert.Empty(d.Log);
        Assert.False(d.StopsStood.ContainsKey(7));
    }

    /// <summary>
    /// Same draws, same order (note 885): an attempt draws everything its choice needs (the cooldown too) when it's chosen, so a
    /// night's dice stand where they would whether it placed or not, and a client re-simulating the host stays on its stream.
    /// </summary>
    [Fact]
    public void TheDiceRunTheSameWhetherItPlacedOrNot()
    {
        var placed = Plain(Spending());
        var failed = Plain(Spending());
        int a = 0, b = 0;
        var one = First(placed, SpawnChannel.Pressure, ref a);
        var two = First(failed, SpawnChannel.Pressure, ref b);
        Assert.Equal(one, two);
        placed.Director!.Commit(placed, one, SpawnOutcome.Of([placed.NextEnemyId]), []);
        failed.Director!.Commit(failed, two, SpawnOutcome.Failed(SpawnFailure.NoSpot), []);
        Assert.Equal(placed.Director.NextRange(0, 1), failed.Director.NextRange(0, 1));
    }
}
