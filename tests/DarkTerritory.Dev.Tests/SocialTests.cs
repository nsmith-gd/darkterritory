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

    [Fact]
    public void ThePassengerIsJudgedByTheCarLost()
    {
        // Note 1051 (note 1050 Q3's default): its grab is the uncoupling, held till the car's gone (a 600 s window), so
        // criterion 3 asks whether alone the car goes no sooner than actMinSeconds and with a friend it's kept.
        var t = DataFile.Load<SocialTuning>(Path.Combine(Content, SocialTuning.File));
        Assert.Contains("Passenger", t.CarLost);
        GrabCheck Check(int crew, bool freed, double held) => new("Passenger", crew, true, freed, held, 600, freed ? "broken off" : "", "");
        var lostLate = SocialTest.Judge(EnemyKind.Passenger, Check(1, false, 43), Check(2, true, 1.5), [], t);
        Assert.Equal("pass", lostLate.Act.Result);
        Assert.Contains("car lost", lostLate.Act.Why);
        Assert.Equal("fail", SocialTest.Judge(EnemyKind.Passenger, Check(1, false, 4), Check(2, true, 1.5), [], t).Act.Result);
        Assert.Equal("fail", SocialTest.Judge(EnemyKind.Passenger, Check(1, false, 43), Check(2, false, 43), [], t).Act.Result);
        // Anyone else is still judged by the window.
        Assert.Equal("fail", SocialTest.Judge(EnemyKind.Gaunt, Check(1, false, 43) with { Kind = "Gaunt" }, Check(2, true, 1.5) with { Kind = "Gaunt" }, [], t).Act.Result);
    }

    [Fact]
    public void TheChoirsStagedCrewSingInTime()
    {
        // Note 1051 (Q3's default): the staged crew sing at the Choir, everyone on the same song, which counts as singing.
        var t = DataFile.Load<SocialTuning>(Path.Combine(Content, SocialTuning.File));
        Assert.Contains("Choir", t.Sung);
        var p = DataFile.Load<Sim.Player.PlayerTuning>(Path.Combine(Content, Sim.Player.PlayerTuning.File)).Voices.Singing;
        int window = (int)(p.WindowSeconds * Sim.SimConstants.TickRate);
        var song = Enumerable.Range(0, window).Select(i => t.Song.At(i * Sim.SimConstants.TickSeconds)).ToArray();
        Assert.True(Sim.Player.Singing.InTime(song, song, p));
        var (alone, paired) = SocialTest.Staged(SocialTest.Content(Content), EnemyKind.Choir, t);
        Assert.True(alone?.Grabbed);
        Assert.True(paired?.Grabbed);
    }

    [Fact]
    public void TheFlagsAreShownBothWays()
    {
        // Note 1051: each creature flag (off in the game) on for a run, or shown on beside a run with it off.
        Assert.Equal(8, SocialTest.Content(Content, ["knotter.tautHold"]).Enemies.Knotter.HoldSeconds);
        Assert.True(SocialTest.Content(Content, ["whistler.carry"]).Enemies.Whistler.Carry.On);
        Assert.Throws<ArgumentException>(() => SocialTest.Content(Content, ["knotter.nope"]));
        var t = DataFile.Load<SocialTuning>(Path.Combine(Content, SocialTuning.File)) with { Creatures = ["Knotter"], Seconds = 5, Seeds = 1, Crews = [1] };
        var off = SocialTest.Run(Content, t, parallel: 1);
        Assert.Equal("fail", Assert.Single(off.Rows).Act.Result);
        var shown = Assert.Single(off.Flagged);
        Assert.Equal(("knotter.tautHold", "pass"), (shown.Flag, shown.Act.Result));
        var on = SocialTest.Run(Content, t with { Flags = ["knotter.tautHold"] }, parallel: 1);
        Assert.Equal("pass", Assert.Single(on.Rows).Act.Result);
        Assert.Empty(on.Flagged);
        Assert.Equal(["knotter.tautHold"], on.FlagsOn);
    }
}
