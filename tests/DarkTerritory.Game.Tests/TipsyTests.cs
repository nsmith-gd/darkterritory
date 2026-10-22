using Ballast;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The dining car's drinks on the drinker's view (upgrades.md §8.5, note 959): blurred and leaning for a minute, a little
/// more and longer with each, easing off at the end, and sober again after.
/// </summary>
public class TipsyTests
{
    static readonly TipsyTuning T = DataFile.Load<LookTuning>(Path.Combine(DataFile.FindContentRoot(), LookTuning.File)).Tipsy;

    static double MostLean(Tipsy t, double from, double to)
    {
        double most = 0;
        for (double s = from; s < to; s += 0.05)
            most = Math.Max(most, Math.Abs(t.Roll(s, T)));
        return most;
    }

    [Fact]
    public void ADrinkBlursAndLeansTheViewAMinuteAndMoreWithEachThenItsGone()
    {
        var t = new Tipsy();
        Assert.Equal(0, t.Blur(10, T));
        Assert.Equal(0, t.Roll(10, T));
        t.Drank(10, T);
        float one = t.Blur(20, T);
        Assert.True(one > 0.2f, $"one drink's blur {one}");
        double lean = MostLean(t, 12, 12 + T.LeanPeriod * 2);
        Assert.InRange(lean * 180 / Math.PI, T.LeanDegrees * 0.5, T.LeanDegrees);
        // It leans both ways.
        var swing = Enumerable.Range(0, 400).Select(i => t.Roll(12 + i * 0.05, T)).ToList();
        Assert.Contains(swing, r => r < 0);
        Assert.Contains(swing, r => r > 0);
        // A second while the first's in: more of both, and longer.
        t.Drank(30, T);
        Assert.True(t.Blur(40, T) > one);
        Assert.True(MostLean(t, 32, 32 + T.LeanPeriod * 2) > lean);
        double end = 30 + T.Seconds + T.MoreEach;
        Assert.True(t.Blur((float)(end - T.FadeSeconds - 1), T) > 0);
        // Easing off in its last seconds, and gone after.
        Assert.True(t.Blur(end - T.FadeSeconds / 2, T) < t.Blur(end - T.FadeSeconds - 1, T));
        Assert.Equal(0, t.Blur(end + 0.1, T));
        Assert.Equal(0, t.Roll(end + 0.1, T));
        Assert.Equal(0, t.Drinks(end + 0.1));
        // A drink after it's worn off starts from one again.
        t.Drank(end + 5, T);
        Assert.Equal(1, t.Drinks(end + 6));
    }

    static Sim.Physics.Body Glass(int id, int carrier) =>
        new(id, Sim.Physics.BodyKind.Drink, Sim.Player.PlayerState.World,
            new Ballast.Physics.PbdBody([new Ballast.Physics.Particle(new Double3(0, 1, 0), 1, 0.05)]))
        { Carrier = carrier };

    [Fact]
    public void TheCrewSeeADrinkersHeadLollAndNotWhoPutTheirsDown()
    {
        // Note 974 (§8.5: "the crew see the drinker's head loll"): a drink gone from a crewmate's hands and from the train was
        // drunk; one put down is still about, in nobody's hands.
        var drinkers = new Drinkers();
        drinkers.Observe([Glass(7, 3), Glass(8, 4)], [3, 4, 5], 10, T);
        Assert.Equal(0, drinkers.Loll(3, 10, T));
        drinkers.Observe([Glass(8, -1)], [3, 4, 5], 11, T);
        double loll = drinkers.Loll(3, 12, T);
        Assert.InRange(loll * 180 / Math.PI, T.LollDegrees * 0.99, T.LollDegrees * 1.01);
        Assert.Equal(0, drinkers.Loll(4, 12, T));
        Assert.Equal(0, drinkers.Loll(5, 12, T));
        // The head rolls over to both sides and drops forward as it goes; sober, it's still.
        var poses = Enumerable.Range(0, 200).Select(i => Tipsy.LollPose(loll, i * 0.05, T)).ToList();
        Assert.Contains(poses, p => p.Roll < -0.5 * loll);
        Assert.Contains(poses, p => p.Roll > 0.5 * loll);
        Assert.All(poses, p => Assert.True(p.Nod > 0));
        Assert.Equal((0f, 0f), Tipsy.LollPose(0, 1, T));
        // A second lolls it further; it's gone when the drinks have worn off.
        drinkers.Observe([Glass(9, 3)], [3], 20, T);
        drinkers.Observe([], [3], 21, T);
        Assert.True(drinkers.Loll(3, 22, T) > loll);
        Assert.Equal(0, drinkers.Loll(3, 21 + T.Seconds + T.MoreEach + 1, T));
    }
}
