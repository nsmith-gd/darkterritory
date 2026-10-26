using System.Text.Json;
using Ballast;
using DarkTerritory.Dev.Social;
using DarkTerritory.Sim.Audit;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Dev.Tests;

/// <summary>
/// `dt social` (queue #255, ARCHITECTURE §8 note 1038): GDD §20's social test as numbers. A small run produces every
/// criterion's verdict and the crew's numbers, the same twice over (and at any parallelism); the verdicts follow the
/// thresholds in content/tuning/social.json.
/// </summary>
public class SocialTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void ASmallRunMeasuresEveryCriterionAndIsTheSameTwice()
    {
        var t = DataFile.Load<SocialTuning>(Path.Combine(Content, SocialTuning.File)) with { Creatures = ["CarHugger"], Seconds = 20, Seeds = 1, Crews = [1, 2] };
        var first = SocialTest.Run(Content, t, parallel: 1);
        var row = Assert.Single(first.Rows);
        Assert.Equal("CarHugger", row.Creature);
        Assert.Equal(2, first.Nights.Count);
        // The staged grab alone and with a friend, and a verdict (with its reasons) on each criterion.
        Assert.True(row.Alone?.Grabbed);
        Assert.True(row.Paired?.Grabbed);
        foreach (var v in new[] { row.Two, row.Act, row.Blame })
        {
            Assert.Contains(v.Result, new[] { "pass", "fail", "unmeasured" });
            Assert.NotEmpty(v.Why);
        }
        Assert.All(first.Nights, n => Assert.Equal(n.Crew, n.Bots.Count));
        var again = SocialTest.Run(Content, t, parallel: 2);
        Assert.Equal(JsonSerializer.Serialize(first, DataFile.Options), JsonSerializer.Serialize(again, DataFile.Options));
    }

    [Fact]
    public void TheVerdictsFollowTheThresholds()
    {
        var t = DataFile.Load<SocialTuning>(Path.Combine(Content, SocialTuning.File));
        Assert.NotEmpty(t.Kinds());
        SocialNight Night(int crew, int failures, int blamed, params SocialGrab[] grabs) =>
            new("Gaunt", crew, 1, [.. Enumerable.Repeat("bot", crew)], 0, failures, blamed, grabs, 0, 0, 0, 0, true, 60);
        GrabCheck Check(int crew, bool freed, double held, double window) => new("Gaunt", crew, true, freed, held, window, freed ? "broken off" : "", "");

        // Two free what one can't; the window inside 8-20 s; three of four failures name someone.
        var good = SocialTest.Judge(EnemyKind.Gaunt, Check(1, false, 12, 12), Check(2, true, 3, 12), [Night(1, 2, 1), Night(2, 2, 2)], t);
        Assert.Equal(("pass", "pass", "pass"), (good.Two.Result, good.Act.Result, good.Blame.Result));

        // Two held as long as one; a 30 s window; nobody named.
        var bad = SocialTest.Judge(EnemyKind.Gaunt, Check(1, true, 4, 30), Check(2, true, 4, 30), [Night(1, 1, 0), Night(2, 1, 0)], t);
        Assert.Equal(("fail", "fail", "fail"), (bad.Two.Result, bad.Act.Result, bad.Blame.Result));

        // No staged grab: two by the nights' failures, three by the grabs that killed, and nothing failed is unmeasured.
        var nights = SocialTest.Judge(EnemyKind.Gaunt, null, null, [Night(1, 0, 0, new SocialGrab(9, null, "killed")), Night(2, 0, 0)], t);
        Assert.Equal(("unmeasured", "pass", "unmeasured"), (nights.Two.Result, nights.Act.Result, nights.Blame.Result));
    }
}
