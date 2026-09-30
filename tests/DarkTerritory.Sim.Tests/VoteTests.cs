using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// GDD App. D.11, the creature vote, and D.14's "vote bounds": dead players only, once a run, carried over if unused; the
/// options derived from how each creature comes; ×1.2 a vote to a cap of ×1.5, applied only as a weight inside the
/// director's roll and within the creature's want tag, so the tags' shares hold and no gate, cap or once-a-run limit is
/// bypassed.
/// </summary>
[Collection(nameof(LineGenTests))]
public class VoteTests
{
    static readonly Route.Route Deep = LineGen.Routes.Generate(Ballast.DataFile.FindContentRoot(), "deadLines:3", 10);
    static readonly Route.Route Local = LineGen.Routes.Generate(Ballast.DataFile.FindContentRoot(), "local:1", 10);

    static HoldoutTests.Night Night(Route.Route? route = null)
    {
        var n = new HoldoutTests.Night(front: 6000, route: route ?? Deep, cars: 10, enemies: true);
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, Tuning.Player);
        n.Crew[5] = PlayerMotor.SpawnOnRoof(n.Train, 3, 0, Tuning.Player);
        n.Step(0.1);
        return n;
    }

    [Fact]
    public void TheOptionsAreTheDirectorsWeightedTaggedEligibleCreatures()
    {
        var n = Night();
        var options = n.World.VoteOptions;
        Assert.NotEmpty(options);
        var d = n.World.Director!;
        Assert.All(options, k => Assert.Equal("weighted", d.SpawnMode(k)));
        Assert.All(options, k => Assert.NotNull(d.WantTag(k)));
        // D.11 "excluded: condition-triggered (the Stoker) and loudness-triggered (the Choir)", and the line's hazards.
        Assert.DoesNotContain(EnemyKind.Stoker, options);
        Assert.DoesNotContain(EnemyKind.Choir, options);
        Assert.DoesNotContain(EnemyKind.Sleepers, options);
        Assert.DoesNotContain(EnemyKind.Drift, options);
        Assert.Equal("condition", d.SpawnMode(EnemyKind.Stoker));
        Assert.Equal("loudness", d.SpawnMode(EnemyKind.Choir));
        Assert.Contains(EnemyKind.CinderHound, options);
        // Gated out on a Local line (the Gaunt and the Switchman are Frontier and beyond, the Passenger Dead lines): not offered.
        var local = Night(Local).World.VoteOptions;
        Assert.DoesNotContain(EnemyKind.Gaunt, local);
        Assert.DoesNotContain(EnemyKind.Passenger, local);
        Assert.DoesNotContain(EnemyKind.Switchman, local);
        Assert.Contains(EnemyKind.CinderHound, local);
    }

    [Fact]
    public void OnlyTheDeadVoteOnceARunAndAnUnusedVoteCarriesOver()
    {
        var n = Night();
        n.DeadAtTheFortress(2);
        var kind = n.World.VoteOptions[0];
        // The living can't.
        Assert.False(n.World.Vote(1, n.Crew[1], kind));
        // A lobbied player can't.
        n.Crew[3] = Net.HostSession.Lobbied(n.Train);
        Assert.False(n.World.Vote(3, n.Crew[3], kind));
        // Not something that isn't an option.
        Assert.False(n.World.Vote(2, n.Crew[2], EnemyKind.Stoker));
        // The dead can, once.
        Assert.True(n.World.Vote(2, n.Crew[2], kind));
        Assert.False(n.World.Vote(2, n.Crew[2], kind));
        Assert.Equal(1, n.World.Votes[kind]);
        // Someone who died without voting, was freed, and died again still has theirs.
        n.DeadAtTheFortress(4);
        n.Crew[4] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, Tuning.Player);
        n.Step(0.1);
        n.DeadAtTheFortress(4);
        Assert.True(n.World.Vote(4, n.Crew[4], kind));
        Assert.Equal(2, n.World.Votes[kind]);
    }

    [Fact]
    public void NoCreaturesWeightGoesPastTheCapHoweverManyVote()
    {
        var n = Night();
        var kind = n.World.VoteOptions.First();
        for (int id = 10; id < 22; id++)
        {
            n.DeadAtTheFortress(id);
            n.World.Vote(id, n.Crew[id], kind);
        }
        Assert.Equal(12, n.World.Votes[kind]);
        Assert.Equal(Tuning.Holdouts.Vote.Cap, n.World.Director!.VoteMultipliers[kind], 9);
        Assert.True(n.World.Director.VoteMultipliers.Values.All(m => m <= 1.5 + 1e-12));
    }

    /// <summary>
    /// Every decision the director makes over a stretch of night: with votes in, each want tag's options weigh exactly what
    /// they did without them (the shares hold), the voted creature's own weight rises by at most ×1.5 against its tag, and
    /// nothing is an option that wasn't (no gate bypassed).
    /// </summary>
    [Fact]
    public void WantTagSharesHoldAndNothingIsAnOptionThatWasntOne()
    {
        var n = Night();
        var d = n.World.Director!;
        // A vote for each creature on offer, several for one.
        int id = 20;
        foreach (var k in n.World.VoteOptions)
        {
            n.DeadAtTheFortress(id);
            n.World.Vote(id++, n.Crew[id - 1], k);
        }
        var favourite = n.World.VoteOptions[0];
        for (int i = 0; i < 3; i++)
        {
            n.DeadAtTheFortress(id);
            n.World.Vote(id++, n.Crew[id - 1], favourite);
        }
        int decisions = 0, votedIn = 0, seen = d.Log.Count;
        for (int t = 0; t < 400 * SimConstants.TickRate; t++)
        {
            n.Step(holdSpeed: 14);
            if (d.Log.Count == seen || d.LastOptions.Count == 0)
                continue;
            seen = d.Log.Count;
            var before = d.LastOptionsBeforeVotes;
            var after = d.LastOptions;
            Assert.Equal(before.Select(o => o.Kind), after.Select(o => o.Kind));
            foreach (var tag in before.Select(o => d.WantTag(o.Kind)).Distinct())
            {
                double b = before.Where(o => d.WantTag(o.Kind) == tag).Sum(o => o.Weight);
                double a = after.Where(o => d.WantTag(o.Kind) == tag).Sum(o => o.Weight);
                Assert.Equal(b, a, 9);
            }
            // Untagged options aren't touched at all.
            for (int i = 0; i < before.Count; i++)
                if (d.WantTag(before[i].Kind) is null)
                    Assert.Equal(before[i].Weight, after[i].Weight);
                else if (before[i].Weight > 0)
                {
                    double gain = after[i].Weight / before[i].Weight;
                    Assert.True(gain <= 1.5 + 1e-9, $"{before[i].Kind} x{gain}");
                    if (d.VoteMultipliers.ContainsKey(before[i].Kind) && before.Count(o => d.WantTag(o.Kind) == d.WantTag(before[i].Kind)) > 1)
                        votedIn++;
                }
            decisions++;
        }
        Assert.True(decisions > 5, $"{decisions} decisions");
        Assert.True(votedIn > 0, "no decision had a voted creature with company in its tag");
    }

    [Fact]
    public void AVoteBypassesNoGateCapOrOnceARunLimit()
    {
        var n = Night();
        var d = n.World.Director!;
        // The Gaunt's once a run: sent once, it's no option again, voted for or not.
        d.Charge(n.World, EnemyKind.Gaunt, n.World.ActiveEnemies);
        Assert.DoesNotContain(EnemyKind.Gaunt, n.World.VoteOptions);
        n.DeadAtTheFortress(2);
        Assert.False(n.World.Vote(2, n.Crew[2], EnemyKind.Gaunt));
        // A gated-out creature (the Passenger on a Local line) can't be made one.
        var local = Night(Local);
        local.DeadAtTheFortress(2);
        Assert.False(local.World.Vote(2, local.Crew[2], EnemyKind.Passenger));
        // Votes change no budget, spend, or cost.
        double budget = d.Budget, spent = d.Spent, cost = d.Cost(EnemyKind.CinderHound);
        n.World.Vote(2, n.Crew[2], EnemyKind.CinderHound);
        Assert.Equal(budget, d.Budget);
        Assert.Equal(spent, d.Spent);
        Assert.Equal(cost, d.Cost(EnemyKind.CinderHound));
    }

    [Fact]
    public void WithoutVotesTheDirectorIsExactlyAsItWas()
    {
        // The vote's weighting is the identity until someone votes: the same night decides the same things.
        var a = Night();
        var b = Night();
        for (int t = 0; t < 200 * SimConstants.TickRate; t++)
        {
            a.Step(holdSpeed: 14);
            b.Step(holdSpeed: 14);
        }
        Assert.Equal(a.World.Director!.Log, b.World.Director!.Log);
        Assert.Equal(a.World.Director.LastOptionsBeforeVotes, a.World.Director.LastOptions);
    }

    [Fact]
    public void TheSootChildrensRealChildRollIsUntouched()
    {
        // GDD v1.1 App. B.6: a true 50/50 with a real child, drawn from the director's dice when they come (and a host's
        // first call always real). A vote spends none of the dice and touches neither the chance nor that first-call rule, so
        // the roll comes out the same with the vote in or not.
        var n = Night();
        var d = n.World.Director!;
        var dice = d.Rng;
        bool nextReal = n.World.NextChildReal;
        n.DeadAtTheFortress(2);
        if (n.World.VoteOptions.Contains(EnemyKind.SootChildren))
            Assert.True(n.World.Vote(2, n.Crew[2], EnemyKind.SootChildren));
        var now = d.Rng;
        Assert.Equal(dice.NextUInt(), now.NextUInt());
        Assert.Equal(nextReal, n.World.NextChildReal);
        Assert.Equal(0.5, Tuning.Enemies.SootChildren.RealChance);
    }
}
