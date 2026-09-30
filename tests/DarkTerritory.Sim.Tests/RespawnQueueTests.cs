using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>GDD App. D.6, row by row: the respawn queue.</summary>
public class RespawnQueueTests
{
    static RespawnQueue Of(params (int Player, QueueKind Kind, double At)[] entries)
    {
        var q = new RespawnQueue();
        foreach (var (p, k, at) in entries)
            if (k == QueueKind.Dead)
                q.Died(p, at);
            else
                q.Lobbied(p, at);
        return q;
    }

    [Fact]
    public void OrderIsTheDeadByTimeOfDeathAndTheLobbiedByJoiningInOneQueue()
    {
        var q = Of((3, QueueKind.Dead, 10), (7, QueueKind.Lobbied, 12), (1, QueueKind.Dead, 40), (9, QueueKind.Lobbied, 41));
        Assert.Equal([3, 7, 1, 9], q.Order);
        Assert.Equal([QueueKind.Dead, QueueKind.Lobbied, QueueKind.Dead, QueueKind.Lobbied], q.Entries.Select(e => e.Kind));
        Assert.Equal(0, q.PositionOf(3));
        Assert.Equal(3, q.PositionOf(9));
        Assert.Equal(-1, q.PositionOf(5));
        Assert.Equal(0, q.Violations);
    }

    [Fact]
    public void DeferMovesDownToAnyPositionBehind()
    {
        var q = Of((1, QueueKind.Dead, 1), (2, QueueKind.Dead, 2), (3, QueueKind.Dead, 3), (4, QueueKind.Dead, 4));
        Assert.True(q.Defer(1, 2));
        Assert.Equal([2, 3, 1, 4], q.Order);
        Assert.True(q.Defer(2, 3));
        Assert.Equal([3, 1, 4, 2], q.Order);
        Assert.Equal(0, q.Violations);
    }

    [Fact]
    public void NobodyCanMoveThemselvesUp()
    {
        var q = Of((1, QueueKind.Dead, 1), (2, QueueKind.Dead, 2), (3, QueueKind.Dead, 3));
        Assert.False(q.Defer(3, 0));
        Assert.False(q.Defer(3, 1));
        Assert.False(q.Defer(2, 1)); // where they are already
        Assert.False(q.Defer(1, 3)); // off the end
        Assert.False(q.Defer(5, 1)); // not in it
        Assert.Equal([1, 2, 3], q.Order);
        Assert.DoesNotContain(q.History, h => h.Op == QueueOp.Deferred);
    }

    [Fact]
    public void AssignmentTakesTheFirstEligibleAndSkippedEntriesKeepTheirPlace()
    {
        var q = new RespawnQueue();
        q.Died(1, 1, ["poi1"]);
        q.Died(2, 2, ["poi1"]);
        q.Died(3, 3, []);
        q.Died(4, 4, []);
        bool NotDiedHere(QueueEntry e) => !e.DiedIn.Contains("poi1");
        var a = q.Assign("h1", NotDiedHere);
        Assert.Equal(3, a!.Player);
        // The second Holdout at the same site takes the next.
        var b = q.Assign("h2", NotDiedHere);
        Assert.Equal(4, b!.Player);
        // Nobody else is eligible here.
        Assert.Null(q.Assign("h3", NotDiedHere));
        // Nobody moved: the skipped are still at the front.
        Assert.Equal([1, 2, 3, 4], q.Order);
        Assert.Equal("h1", q.Of(3)!.Holdout);
        // Asked again, a Holdout keeps who it has.
        Assert.Equal(3, q.Assign("h1", _ => true)!.Player);
        // Elsewhere, the skipped are first.
        Assert.Equal(1, q.Assign("h9", e => !e.DiedIn.Contains("poi7"))!.Player);
        Assert.Equal(0, q.Violations);
    }

    [Fact]
    public void AMissedRescueGoesBackToThePositionItHeld()
    {
        var q = Of((1, QueueKind.Dead, 1), (2, QueueKind.Dead, 2));
        Assert.Equal(1, q.Assign("h1", _ => true)!.Player);
        // More die while the crew are deciding whether to stop.
        q.Died(3, 50);
        q.Lobbied(4, 51);
        var released = q.Release("h1");
        Assert.Equal(1, released!.Player);
        Assert.Null(released.Holdout);
        Assert.Equal(0, q.PositionOf(1));
        Assert.Equal([1, 2, 3, 4], q.Order);
        // The crew come back: it can assign again, to the same player.
        Assert.Equal(1, q.Assign("h1", _ => true)!.Player);
        Assert.Equal(0, q.Violations);
    }

    [Fact]
    public void FreedLeavesTheQueueAndEveryoneBehindMovesUp()
    {
        var q = Of((1, QueueKind.Dead, 1), (2, QueueKind.Dead, 2), (3, QueueKind.Lobbied, 3));
        q.Assign("h1", _ => true);
        Assert.Equal(1, q.Freed("h1")!.Player);
        Assert.Equal([2, 3], q.Order);
        Assert.Equal(0, q.PositionOf(2));
        Assert.Null(q.Freed("h1"));
        Assert.Equal(0, q.Violations);
    }

    [Fact]
    public void ADisconnectRemovesTheEntryAndRejoiningIsANewLobbiedEntryAtTheBack()
    {
        var q = Of((1, QueueKind.Dead, 1), (2, QueueKind.Dead, 2), (3, QueueKind.Dead, 3));
        q.Assign("h1", e => e.Player == 2);
        // They held a Holdout: it's told, to reassign.
        Assert.Equal("h1", q.Remove(2));
        Assert.Equal([1, 3], q.Order);
        Assert.Null(q.AssignedTo("h1"));
        q.Lobbied(2, 90);
        Assert.Equal([1, 3, 2], q.Order);
        Assert.Equal(QueueKind.Lobbied, q.Of(2)!.Kind);
        Assert.Null(q.Remove(8));
        Assert.Equal(0, q.Violations);
    }

    [Fact]
    public void DeferringOutOfAHoldoutGivesItUpUnlessTheBreachHasStarted()
    {
        var q = Of((1, QueueKind.Dead, 1), (2, QueueKind.Dead, 2), (3, QueueKind.Dead, 3));
        q.Assign("h1", _ => true);
        Assert.True(q.Defer(1, 1));
        Assert.Null(q.Of(1)!.Holdout);
        // Reassigned to the next eligible entry.
        Assert.Equal(2, q.Assign("h1", _ => true)!.Player);
        // The breach starts: locked.
        q.Lock("h1", true);
        Assert.False(q.Defer(2, 2));
        Assert.Equal("h1", q.Of(2)!.Holdout);
        // Interrupted: unlocked again.
        q.Lock("h1", false);
        Assert.True(q.Defer(2, 2));
        Assert.Equal(0, q.Violations);
    }

    [Fact]
    public void TheIntegrityRuleCatchesSomeoneMovingUp()
    {
        Assert.True(RespawnQueue.Holds([1, 2, 3], [2, 3, 1], deferrer: 1));
        Assert.False(RespawnQueue.Holds([1, 2, 3], [2, 1, 3], deferrer: null));
        // A "deferrer" who went up is still someone moving up.
        Assert.False(RespawnQueue.Holds([1, 2, 3], [1, 3, 2], deferrer: 3));
        Assert.True(RespawnQueue.Holds([1, 2, 3], [1, 3], deferrer: null));
        Assert.True(RespawnQueue.Holds([1, 2], [1, 2, 9], deferrer: null));
        Assert.False(RespawnQueue.Holds([1, 2], [9, 1, 2], deferrer: null));
    }

    /// <summary>D.14 "queue integrity", over randomised sequences of everything that can happen to the queue.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RandomisedSequencesNeverMoveAnyoneUpExceptByThoseAheadLeaving(int seed)
    {
        var rng = new Random(seed);
        var q = new RespawnQueue();
        int next = 1;
        double t = 0;
        var sites = new[] { "poi1", "poi2", "stn1" };
        for (int step = 0; step < 2000; step++)
        {
            t += rng.NextDouble();
            int n = q.Count;
            int pick = n == 0 ? 0 : q.Entries[rng.Next(n)].Player;
            switch (rng.Next(8))
            {
                case 0 when n < 10:
                    q.Died(next++, t, [sites[rng.Next(sites.Length)]]);
                    break;
                case 1 when n < 10:
                    q.Lobbied(next++, t);
                    break;
                case 2 when n > 0:
                    q.Defer(pick, rng.Next(n + 1));
                    break;
                case 3:
                    string site = sites[rng.Next(sites.Length)];
                    q.Assign($"h-{site}-{rng.Next(2)}", e => !e.DiedIn.Contains(site));
                    break;
                case 4:
                    q.Release($"h-{sites[rng.Next(sites.Length)]}-{rng.Next(2)}");
                    break;
                case 5:
                    q.Freed($"h-{sites[rng.Next(sites.Length)]}-{rng.Next(2)}");
                    break;
                case 6 when n > 0:
                    q.Remove(pick);
                    break;
                case 7:
                    q.Lock($"h-{sites[rng.Next(sites.Length)]}-{rng.Next(2)}", rng.Next(2) == 0);
                    break;
            }
            // One Holdout, one player; one player, one Holdout.
            var held = q.Entries.Where(e => e.Holdout is not null).ToList();
            Assert.Equal(held.Count, held.Select(e => e.Holdout).Distinct().Count());
            Assert.Equal(q.Count, q.Order.Distinct().Count());
        }
        Assert.True(q.History.Count > 500);
        Assert.Equal(0, q.Violations);
        // And every change, re-checked from the history itself.
        foreach (var c in q.History)
            Assert.True(RespawnQueue.Holds(c.Before, c.After, c.Op is QueueOp.Deferred or QueueOp.Joined ? c.Player : null), $"{c.Op} {c.Player}");
    }
}
