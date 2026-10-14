namespace DarkTerritory.Dev.Trends;

/// <summary>
/// How tools/trends/bisect.sh calls each commit it tries (ARCHITECTURE §8 note 523): good or bad by which end's value it's
/// nearer, the line drawn halfway between the two ends. A commit's value is its nights' mean, as the trends line's is; the
/// nights are the same and each is deterministic, so a commit measured twice gives the same value and a bisect is repeatable.
/// </summary>
public static class TrendBisect
{
    public static double Midpoint(double good, double bad) => (good + bad) / 2;

    /// <summary>
    /// Bad when <paramref name="value"/> is on the bad end's side of the midpoint, or on it: a commit that's taken half the
    /// fall has taken its share of it.
    /// </summary>
    public static bool IsBad(double value, double good, double bad)
    {
        if (good == bad || !double.IsFinite(good) || !double.IsFinite(bad))
            throw new ArgumentException($"the two ends measure {good} and {bad}: nothing between them to find");
        double mid = Midpoint(good, bad);
        return bad > good ? value >= mid : value <= mid;
    }
}
