using Ballast;

namespace DarkTerritory.Dev.Trends;

/// <summary>Which way a metric moving is bad news: <see cref="Either"/> for a balance read both ways, <see cref="None"/> tracked only.</summary>
public enum Worse { None, Up, Down, Either }

/// <summary>
/// How far one metric (or every metric its <see cref="Match"/> pattern takes, <c>*</c> for any run of characters) may move from
/// where it's been before the move is a fall: the band's half-width is the larger of <see cref="Abs"/> and <see cref="Rel"/> of
/// that level (an absolute floor keeps a metric near zero, deaths on a quiet night, from tripping on one more).
/// </summary>
public sealed record TrendBand
{
    public string Match { get; init; } = "*";
    public Worse Bad { get; init; }
    public double Abs { get; init; }
    public double Rel { get; init; }
    /// <summary>What it is, for whoever reads the summary.</summary>
    public string? What { get; init; }

    public double Width(double level) => Math.Max(Abs, Rel * Math.Abs(level));

    public bool Matches(string metric) => Glob(Match, metric);

    static bool Glob(string pattern, string text)
    {
        if (!pattern.Contains('*'))
            return pattern == text;
        var parts = pattern.Split('*');
        if (!text.StartsWith(parts[0], StringComparison.Ordinal) || !text.EndsWith(parts[^1], StringComparison.Ordinal)
            || text.Length < parts[0].Length + parts[^1].Length)
            return false;
        int at = parts[0].Length;
        foreach (string middle in parts[1..^1])
        {
            int found = text.IndexOf(middle, at, StringComparison.Ordinal);
            if (found < 0 || found + middle.Length > text.Length - parts[^1].Length)
                return false;
            at = found + middle.Length;
        }
        return true;
    }
}

/// <summary>
/// tools/trends/metrics.json (ARCHITECTURE §8 note 523): the bands, and how much history a step is judged against. These are
/// the tool's own thresholds (how noisy a bot night's numbers are), not design numbers, so they live beside the tool.
/// </summary>
public sealed record TrendBands
{
    /// <summary>A step is judged against the median of up to this many lines before it.</summary>
    public int Window { get; init; } = 5;
    /// <summary>With fewer lines than this before it, a step isn't judged (the file's first lines).</summary>
    public int MinHistory { get; init; } = 3;
    /// <summary>The bands, the first that matches a metric its band.</summary>
    public IReadOnlyList<TrendBand> Metrics { get; init; } = [];

    public TrendBand For(string metric) => Metrics.FirstOrDefault(b => b.Matches(metric)) ?? new TrendBand { Match = metric };

    public static TrendBands Load(string path) => DataFile.Load<TrendBands>(path);

    /// <summary>tools/trends/metrics.json in the repository whose content this is.</summary>
    public static string DefaultPath(string content) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(content).TrimEnd(Path.DirectorySeparatorChar))!, "tools", "trends", "metrics.json");
}

/// <summary>A step judged: a fall (past its band the bad way), or a rise (past it the good way).</summary>
/// <param name="Level">The median of the lines before it (up to <see cref="TrendBands.Window"/>), the level it moved from.</param>
/// <param name="Width">The band's half-width at that level.</param>
/// <param name="Previous">The line before it, and its value: a bisect's good end.</param>
public sealed record TrendStep(string Metric, bool Fall, TrendLine Line, double Value, double Level, double Width, TrendLine Previous, double PreviousValue,
    Worse Bad)
{
    /// <summary>Each seed's own value at this line, in seed order (none for a metric measured once, perf's).</summary>
    public IReadOnlyList<double> Seeds => [.. Line.Seeds.Select(s => s.Metrics.TryGetValue(Metric, out double v) ? v : double.NaN).Where(double.IsFinite)];
}

/// <summary>The falls in a trends file (ARCHITECTURE §8 note 523).</summary>
public static class TrendSeries
{
    /// <summary>
    /// The lines comparable with the newest: measured on the same nights (a change of route, crew or seeds starts a new
    /// series), each commit once (a commit measured again counts by its newest line).
    /// </summary>
    public static IReadOnlyList<TrendLine> Comparable(IReadOnlyList<TrendLine> lines)
    {
        if (lines.Count == 0)
            return [];
        string key = lines[^1].Options.Key;
        var same = lines.Where(l => l.Options.Key == key).ToList();
        var newest = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < same.Count; i++)
            newest[same[i].Commit] = i;
        return [.. same.Where((l, i) => newest[l.Commit] == i)];
    }

    /// <summary>
    /// Line <paramref name="i"/>'s step in <paramref name="metric"/>, if it's one: past its band from the level before it, the
    /// bad way (a fall) or the good way (a rise), where the line before it wasn't already past the band that way. A metric that
    /// fell stays down for the lines after it until the level catches up; only the step that took it there is the fall.
    /// </summary>
    public static TrendStep? Judge(IReadOnlyList<TrendLine> lines, int i, string metric, TrendBands bands)
    {
        var band = bands.For(metric);
        if (band.Bad == Worse.None || !lines[i].Metrics.TryGetValue(metric, out double value))
            return null;
        var history = Enumerable.Range(0, i).Where(j => lines[j].Metrics.ContainsKey(metric)).TakeLast(bands.Window).ToList();
        if (history.Count < Math.Max(1, bands.MinHistory))
            return null;
        double level = Median([.. history.Select(j => lines[j].Metrics[metric])]);
        double width = band.Width(level);
        var previous = lines[history[^1]];
        double before = previous.Metrics[metric];
        // How far past the band the bad way (positive: past it), and the good way.
        double PastBad(double v) => band.Bad switch
        {
            Worse.Up => v - level - width,
            Worse.Down => level - v - width,
            _ => Math.Abs(v - level) - width,
        };
        double PastGood(double v) => band.Bad switch
        {
            Worse.Up => level - v - width,
            Worse.Down => v - level - width,
            _ => double.NegativeInfinity,
        };
        if (PastBad(value) > 0 && PastBad(before) <= 0)
            return new TrendStep(metric, true, lines[i], value, level, width, previous, before, band.Bad);
        if (PastGood(value) > 0 && PastGood(before) <= 0)
            return new TrendStep(metric, false, lines[i], value, level, width, previous, before, band.Bad);
        return null;
    }

    /// <summary>Every step past its band in lines <paramref name="from"/> on, each judged against all the lines before it.</summary>
    public static IReadOnlyList<TrendStep> Steps(IReadOnlyList<TrendLine> lines, TrendBands bands, int from = 0, string? metric = null) =>
        [.. Enumerable.Range(Math.Max(0, from), Math.Max(0, lines.Count - Math.Max(0, from)))
            .SelectMany(i => (metric is null ? lines[i].Metrics.Keys.Order(StringComparer.Ordinal) : (IEnumerable<string>)[metric])
                .Select(m => Judge(lines, i, m, bands)).OfType<TrendStep>())];

    /// <summary>
    /// A metric by the name it's asked for: its whole name, or the end of one ("km" for "balance.km") when only one ends so.
    /// </summary>
    public static string Resolve(string asked, IEnumerable<string> names)
    {
        var all = names.Distinct().ToList();
        if (all.Contains(asked))
            return asked;
        var ending = all.Where(n => n.EndsWith("." + asked, StringComparison.Ordinal)).ToList();
        return ending.Count == 1 ? ending[0]
            : ending.Count > 1 ? throw new ArgumentException($"\"{asked}\" could be any of {string.Join(", ", ending)}")
            : throw new ArgumentException($"no metric called \"{asked}\"");
    }

    public static double Median(IReadOnlyList<double> xs)
    {
        if (xs.Count == 0)
            return double.NaN;
        var sorted = xs.Order().ToList();
        return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    }
}
