using System.Diagnostics;

namespace DarkTerritory.Game;

/// <summary>
/// The slow frames written down (note 549; the director, 9 Oct 2026, on main's build: "an extreme borderline unplayable
/// performance drop that kept recurring throughout gameplay"). Each frame of a night is timed in its parts (the sim's
/// ticks, the scene's build, the present); a frame well over the night's run (<see cref="SlowMs"/> and
/// <see cref="OverMedian"/> times the median of the last <see cref="Window"/>) gives one line for the launch log, saying
/// what it spent, what the garbage collector did and allocated, and where the night was. No more than one a second, so a
/// crawl reads as a run of lines, not a flood; <see cref="Summary"/> counts them all at the night's end.
/// </summary>
public sealed class FrameWatch
{
    public const double SlowMs = 100, OverMedian = 4, QuietSeconds = 1;
    public const int Window = 120;

    readonly Stopwatch _frame = new();
    readonly Queue<double> _recent = new();
    readonly List<(string Part, double Ms)> _parts = [];
    double _lap, _lastLine = double.NegativeInfinity;
    int _gen0, _gen1, _gen2;
    long _allocated;

    /// <summary>Slow frames this night, and the worst of them (ms).</summary>
    public int Slow { get; private set; }
    public double Worst { get; private set; }
    public int Frames { get; private set; }

    /// <summary>At the top of a frame.</summary>
    public void Begin()
    {
        _parts.Clear();
        _lap = 0;
        _gen0 = GC.CollectionCount(0);
        _gen1 = GC.CollectionCount(1);
        _gen2 = GC.CollectionCount(2);
        _allocated = GC.GetTotalAllocatedBytes();
        _frame.Restart();
    }

    /// <summary>The frame's time since the last mark, as <paramref name="part"/>.</summary>
    public void Mark(string part)
    {
        double at = _frame.Elapsed.TotalMilliseconds;
        _parts.Add((part, at - _lap));
        _lap = at;
    }

    /// <summary>
    /// At the end of a frame, at <paramref name="now"/> (s): the line for the log if it was a slow one, or null.
    /// <paramref name="where"/> says where the night was (asked only for a slow frame).
    /// </summary>
    public string? End(double now, Func<string> where)
    {
        double total = _frame.Elapsed.TotalMilliseconds;
        if (total - _lap > 0.5)
            _parts.Add(("other", total - _lap));
        double median = Median();
        _recent.Enqueue(total);
        if (_recent.Count > Window)
            _recent.Dequeue();
        Frames++;
        if (!IsSlow(total, median))
            return null;
        Slow++;
        Worst = Math.Max(Worst, total);
        if (now - _lastLine < QuietSeconds)
            return null;
        _lastLine = now;
        return Line(total, median, where());
    }

    /// <summary>Whether a frame of <paramref name="ms"/> is slow against a night running at <paramref name="median"/> ms.</summary>
    public static bool IsSlow(double ms, double median) => ms >= SlowMs && (double.IsNaN(median) || ms >= OverMedian * median);

    string Line(double total, double median, string where)
    {
        var parts = string.Join(", ", _parts.Where(p => p.Ms >= 1).Select(p => $"{p.Part} {p.Ms:0}"));
        int g0 = GC.CollectionCount(0) - _gen0, g1 = GC.CollectionCount(1) - _gen1, g2 = GC.CollectionCount(2) - _gen2;
        double mb = (GC.GetTotalAllocatedBytes() - _allocated) / 1048576.0;
        string gc = g0 + g1 + g2 == 0 ? "no gc" : $"gc {g0}/{g1}/{g2}";
        string run = double.IsNaN(median) ? "the night's first frames" : $"median {median:0}";
        return $"slow frame {total:0} ms ({run}): {parts}; {gc}, {mb:0.0} MB allocated; {where}";
    }

    /// <summary>The night's count, for the log at its end.</summary>
    public string Summary() => $"frames {Frames}, slow {Slow}, worst {Worst:0} ms";

    double Median()
    {
        if (_recent.Count < 10)
            return double.NaN;
        var sorted = _recent.Order().ToArray();
        return sorted[sorted.Length / 2];
    }
}
