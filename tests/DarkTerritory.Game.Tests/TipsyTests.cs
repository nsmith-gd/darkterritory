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
}
