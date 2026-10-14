using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DarkTerritory.Game;

/// <summary>
/// The slow frames written down (note 549; the director, 9 Oct 2026, on main's build: "an extreme borderline unplayable
/// performance drop that kept recurring throughout gameplay"). Each frame of a night is timed in its parts (the sim's
/// ticks, the scene's build, the present); a frame well over the night's run (<see cref="SlowMs"/> and
/// <see cref="OverMedian"/> times the median of the last <see cref="Window"/>) gives one line for the launch log, saying
/// what it spent, what the garbage collector did and allocated, and where the night was. No more than one a second, so a
/// crawl reads as a run of lines, not a flood; <see cref="Summary"/> counts them all at the night's end.
/// <para>The night's performance report (GDD §34.3, note 691; the director, 13 Oct 2026: "a way that I can send you ... a
/// performance report"): the stutters a player feels are shorter than a slow frame, so every <b>hitch</b> (a frame at least
/// <see cref="HitchOverMedian"/> times the median and <see cref="HitchMs"/>) is kept too, with its parts, what the frame
/// counted (<see cref="FrameCounts"/>), how fast the view was turning and where the night was. Every frame goes in a
/// histogram, the night's and its minute's, for the percentiles. <see cref="Report"/> is the whole night;
/// <see cref="Story"/> the last minute, for a note.</para>
/// </summary>
public sealed class FrameWatch
{
    public const double SlowMs = 100, OverMedian = 4, QuietSeconds = 1;
    public const int Window = 120;
    /// <summary>A hitch: a frame the player feels as a stutter, against the night's run.</summary>
    public const double HitchMs = 25, HitchOverMedian = 2;
    /// <summary>The view turning faster than this (degrees a second) is "turning fast" (the director's line 2).</summary>
    public const double TurningFast = 180;
    /// <summary>How much of the night a note carries (s).</summary>
    public const double StorySeconds = 60;
    /// <summary>The hitches kept in full (past it, only counted), and the worst of them a report lists.</summary>
    const int MaxHitches = 4000, WorstListed = 12;
    /// <summary>The histogram's bins are a millisecond each, up to a second (the last bin is a second or more).</summary>
    const int Bins = 1000;
    /// <summary>The report's bands: the frame rates a player knows.</summary>
    static readonly (double Ms, string Name)[] Bands =
        [(8.4, "120 fps"), (11.2, "90 fps"), (16.8, "60 fps"), (22.3, "45 fps"), (33.4, "30 fps"), (50, "20 fps"), (100, "10 fps"), (250, "4 fps"), (double.PositiveInfinity, "under 4 fps")];

    /// <summary>A hitch, as the report lists it.</summary>
    /// <param name="At">Seconds into the night.</param>
    /// <param name="Lead">The part of the frame that took the most of it.</param>
    /// <param name="Turn">How fast the view was turning (degrees a second).</param>
    /// <param name="Counts">What the frame counted (<see cref="FrameCounts"/>), "" for nothing.</param>
    public sealed record Hitch(double At, double Ms, double Median, string Parts, string Lead, string Gc, double Mb, double Turn, string Counts, string Where);

    sealed class Minute(string where)
    {
        public readonly int[] Histogram = new int[Bins + 1];
        public int Frames, Hitches, Slow;
        public string Where = where;
    }

    readonly Stopwatch _frame = new();
    readonly Queue<double> _recent = new();
    readonly List<(string Part, double Ms, long Bytes)> _parts = [];
    /// <summary>The night's allocations by part (its name without its count), and its frames' time in it, for the report.</summary>
    readonly SortedDictionary<string, (long Bytes, double Ms)> _byPart = new(StringComparer.Ordinal);
    long _lapBytes;
    int _timed;
    TimeSpan _jit;
    long _jitMethods;
    readonly int[] _histogram = new int[Bins + 1];
    readonly List<Hitch> _hitches = [];
    readonly Dictionary<string, int> _leads = new(StringComparer.Ordinal);
    readonly List<Minute> _minutes = [];
    readonly Queue<(double At, float Ms)> _last = new();
    double _lap, _lastLine = double.NegativeInfinity, _start = double.NaN, _lastEnd = double.NaN;
    int _gen0, _gen1, _gen2;
    long _allocated;
    readonly int _night0 = GC.CollectionCount(0), _night1 = GC.CollectionCount(1), _night2 = GC.CollectionCount(2);
    readonly long _nightAllocated = GC.GetTotalAllocatedBytes();
    double? _yaw, _pitch, _lastYaw, _lastPitch;
    double _turn;

    /// <summary>Slow frames this night, and the worst of them (ms).</summary>
    public int Slow { get; private set; }
    public double Worst { get; private set; }
    public int Frames { get; private set; }
    /// <summary>Hitches this night (every one counted; the first few thousand kept in full).</summary>
    public int Hitches { get; private set; }
    /// <summary>The hitches while the view turned fast (<see cref="TurningFast"/>).</summary>
    public int HitchesTurning { get; private set; }
    /// <summary>The longest frame of the night (ms), slow or not.</summary>
    public double Longest { get; private set; }
    /// <summary>The hitches kept, in the night's order.</summary>
    public IReadOnlyList<Hitch> Kept => _hitches;

    /// <summary>At the top of a frame.</summary>
    public void Begin()
    {
        _parts.Clear();
        _lap = 0;
        _lapBytes = GC.GetAllocatedBytesForCurrentThread();
        _gen0 = GC.CollectionCount(0);
        _gen1 = GC.CollectionCount(1);
        _gen2 = GC.CollectionCount(2);
        _allocated = GC.GetTotalAllocatedBytes();
        (_jit, _jitMethods) = (System.Runtime.JitInfo.GetCompilationTime(currentThread: true), System.Runtime.JitInfo.GetCompiledMethodCount(currentThread: true));
        FrameCounts.Take();
        _frame.Restart();
    }

    /// <summary>The frame's time since the last mark, as <paramref name="part"/>.</summary>
    public void Mark(string part)
    {
        double at = _frame.Elapsed.TotalMilliseconds;
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        _parts.Add((part, at - _lap, bytes - _lapBytes));
        (_lap, _lapBytes) = (at, bytes);
    }

    /// <summary>A part of the frame of a known length, and what it allocated (a replayed frame, a test).</summary>
    public void Part(string part, double ms, long bytes = 0)
    {
        _parts.Add((part, ms, bytes));
        _lap += ms;
    }

    /// <summary>Where the view looks this frame (radians), for how fast it's turning.</summary>
    public void View(double yaw, double pitch) => (_yaw, _pitch) = (yaw, pitch);

    /// <summary>
    /// At the end of a frame, at <paramref name="now"/> (s): the line for the log if it was a slow one, or null.
    /// <paramref name="where"/> says where the night was (asked only for a slow frame or a hitch, and once a minute).
    /// </summary>
    public string? End(double now, Func<string> where)
    {
        double total = _frame.Elapsed.TotalMilliseconds;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - _lapBytes;
        if (total - _lap > 0.5 || bytes > 64 * 1024)
            _parts.Add(("other", Math.Max(0, total - _lap), bytes));
        return Frame(total, now, where);
    }

    /// <summary><see cref="End"/> for a frame of <paramref name="total"/> ms, its parts already given.</summary>
    public string? Frame(double total, double now, Func<string> where)
    {
        double median = Median();
        _recent.Enqueue(total);
        if (_recent.Count > Window)
            _recent.Dequeue();
        Frames++;
        Longest = Math.Max(Longest, total);
        Turning(now);
        // The night's run, not its loading: the first frames (no median yet) build everything once.
        if (!double.IsNaN(median))
            _timed++;
        foreach (var (part, ms, bytes) in double.IsNaN(median) ? [] : _parts)
        {
            string name = Unnumbered(part);
            var (b, t) = _byPart.GetValueOrDefault(name);
            _byPart[name] = (b + bytes, t + ms);
        }
        if (double.IsNaN(_start))
            _start = now;
        int minute = (int)((now - _start) / 60);
        while (_minutes.Count <= minute)
            _minutes.Add(new Minute(where()));
        var m = _minutes[minute];
        int bin = Math.Clamp((int)total, 0, Bins);
        _histogram[bin]++;
        m.Histogram[bin]++;
        m.Frames++;
        _last.Enqueue((now, (float)total));
        while (_last.Count > 0 && _last.Peek().At < now - StorySeconds)
            _last.Dequeue();
        // Code run for the first time is compiled on the spot (a build that isn't ReadyToRun): a frame's share of it (note 695).
        double jit = (System.Runtime.JitInfo.GetCompilationTime(currentThread: true) - _jit).TotalMilliseconds;
        if (jit >= 1)
            FrameCounts.Add($"jit {jit:0} ms", (int)(System.Runtime.JitInfo.GetCompiledMethodCount(currentThread: true) - _jitMethods));
        string counts = FrameCounts.Take();
        bool slow = IsSlow(total, median);
        string? place = null;
        if (IsHitch(total, median))
        {
            Hitches++;
            m.Hitches++;
            if (_turn >= TurningFast)
                HitchesTurning++;
            string lead = Lead();
            _leads[lead] = _leads.GetValueOrDefault(lead) + 1;
            if (_hitches.Count < MaxHitches)
            {
                place = where();
                var (gc, mb) = Collected();
                _hitches.Add(new Hitch(now - _start, total, median, Parts(), lead, gc, mb, _turn, counts, place));
            }
        }
        if (!slow)
            return null;
        Slow++;
        m.Slow++;
        Worst = Math.Max(Worst, total);
        if (now - _lastLine < QuietSeconds)
            return null;
        _lastLine = now;
        return Line(total, median, place ?? where(), counts);
    }

    /// <summary>Whether a frame of <paramref name="ms"/> is slow against a night running at <paramref name="median"/> ms.</summary>
    public static bool IsSlow(double ms, double median) => ms >= SlowMs && (double.IsNaN(median) || ms >= OverMedian * median);

    /// <summary>Whether a frame of <paramref name="ms"/> is a hitch against a night running at <paramref name="median"/> ms
    /// (the night's first frames, with no run yet, aren't: they're its loading).</summary>
    public static bool IsHitch(double ms, double median) => !double.IsNaN(median) && ms >= HitchMs && ms >= HitchOverMedian * median;

    void Turning(double now)
    {
        _turn = 0;
        if (_yaw is { } yaw && _pitch is { } pitch && _lastYaw is { } ly && _lastPitch is { } lp && now > _lastEnd)
        {
            double dy = Math.IEEERemainder(yaw - ly, 2 * Math.PI), dp = pitch - lp;
            _turn = Math.Sqrt(dy * dy + dp * dp) * (180 / Math.PI) / (now - _lastEnd);
        }
        (_lastYaw, _lastPitch, _lastEnd) = (_yaw, _pitch, now);
    }

    /// <summary>The part that took the most of the frame, without its count ("sim (3 ticks)" is "sim").</summary>
    string Lead()
    {
        if (_parts.Count == 0)
            return "other";
        return Unnumbered(_parts.MaxBy(p => p.Ms).Part);
    }

    static string Unnumbered(string part)
    {
        int paren = part.IndexOf(" (", StringComparison.Ordinal);
        return paren > 0 ? part[..paren] : part;
    }

    /// <summary>The frame's parts: their ms, and what they allocated when it's a megabyte's tenth or more.</summary>
    string Parts() => string.Join(", ", _parts.Where(p => p.Ms >= 1 || p.Bytes >= 104858).Select(p =>
        p.Bytes >= 104858 ? $"{p.Part} {p.Ms:0} ({p.Bytes / 1048576.0:0.0} MB)" : $"{p.Part} {p.Ms:0}"));

    (string Gc, double Mb) Collected()
    {
        int g0 = GC.CollectionCount(0) - _gen0, g1 = GC.CollectionCount(1) - _gen1, g2 = GC.CollectionCount(2) - _gen2;
        double mb = (GC.GetTotalAllocatedBytes() - _allocated) / 1048576.0;
        return (g0 + g1 + g2 == 0 ? "no gc" : $"gc {g0}/{g1}/{g2}", mb);
    }

    string Line(double total, double median, string where, string counts)
    {
        var (gc, mb) = Collected();
        string run = double.IsNaN(median) ? "the night's first frames" : $"median {median:0}";
        return $"slow frame {total:0} ms ({run}): {Parts()}; {gc}, {mb:0.0} MB allocated{(counts.Length > 0 ? "; " + counts : "")}; {where}";
    }

    /// <summary>The night's count, for the log at its end.</summary>
    public string Summary() => $"frames {Frames}, slow {Slow}, worst {Worst:0} ms, hitches {Hitches}, p99 {Ms(Percentile(_histogram, 0.99))} ms";

    double Median()
    {
        if (_recent.Count < 10)
            return double.NaN;
        var sorted = _recent.Order().ToArray();
        return sorted[sorted.Length / 2];
    }

    /// <summary>The frame time (ms) that <paramref name="p"/> of the frames are at or under: the top of its bin (infinite in
    /// the last, a second or more: <see cref="Ms"/> says so).</summary>
    static double Percentile(int[] histogram, double p)
    {
        long n = 0;
        foreach (int c in histogram)
            n += c;
        if (n == 0)
            return 0;
        long want = (long)Math.Ceiling(p * n), seen = 0;
        for (int i = 0; i < histogram.Length; i++)
            if ((seen += histogram[i]) >= want)
                return i < Bins ? i + 1 : double.PositiveInfinity;
        return double.PositiveInfinity;
    }

    static string Ms(double ms) => double.IsInfinity(ms) ? $"≥{Bins}" : ms.ToString("0", CultureInfo.InvariantCulture);

    static string Clock(double seconds) => $"{(int)(seconds / 60):00}:{(int)(seconds % 60):00}";

    /// <summary>
    /// The whole night's performance (GDD §34.3, note 691): its frames' percentiles and histogram, its hitches by the part
    /// that led them, the night minute by minute, and the worst hitches in full. <paramref name="title"/> heads it (the
    /// route, how it ended); <paramref name="more"/> are lines the app adds (the audio's gaps).
    /// </summary>
    public string Report(string title, IEnumerable<string>? more = null)
    {
        var t = new StringBuilder();
        var ci = CultureInfo.InvariantCulture;
        double seconds = _last.Count > 0 && !double.IsNaN(_start) ? _last.Last().At - _start : 0;
        t.AppendLine($"PERFORMANCE · {title}");
        t.AppendLine(ci, $"night     {Clock(seconds)} long, {Frames} frames ({(seconds > 0 ? Frames / seconds : 0):0} fps on average)");
        t.AppendLine(ci, $"frames    p50 {Ms(Percentile(_histogram, 0.5))} ms · p90 {Ms(Percentile(_histogram, 0.9))} · p99 {Ms(Percentile(_histogram, 0.99))} · p99.9 {Ms(Percentile(_histogram, 0.999))} · longest {Longest:0}");
        t.AppendLine(ci, $"hitches   {Hitches} (a frame ≥ {HitchOverMedian:0}× the median and ≥ {HitchMs:0} ms){(seconds >= 60 ? $", {Hitches / (seconds / 60):0.0} a minute" : "")}; {Slow} of them slow (≥ {SlowMs:0} ms and {OverMedian:0}× the median); {HitchesTurning} while the view turned faster than {TurningFast:0}°/s");
        if (_leads.Count > 0)
            t.AppendLine($"led by    {string.Join(", ", _leads.OrderByDescending(l => l.Value).ThenBy(l => l.Key, StringComparer.Ordinal).Select(l => $"{l.Key} {l.Value}"))}");
        int g0 = GC.CollectionCount(0) - _night0, g1 = GC.CollectionCount(1) - _night1, g2 = GC.CollectionCount(2) - _night2;
        double mb = (GC.GetTotalAllocatedBytes() - _nightAllocated) / 1048576.0;
        t.AppendLine(ci, $"memory    {mb:0} MB allocated ({(seconds > 0 ? mb / seconds : 0):0.0} MB/s), collections {g0}/{g1}/{g2} (gen 0/1/2)");
        foreach (var line in more ?? [])
            t.AppendLine(line);
        if (_byPart.Count > 0 && _timed > 0)
        {
            // A frame's parts on average, past the night's first frames: a part that allocates every frame is what keeps
            // the collector busy (note 695).
            t.AppendLine();
            t.AppendLine("part of the frame (a frame's average)      ms       MB");
            foreach (var (part, (bytes, ms)) in _byPart.OrderByDescending(p => p.Value.Ms))
                t.AppendLine(ci, $"  {part,-36} {ms / _timed,7:0.00}  {bytes / 1048576.0 / _timed,7:0.00}");
        }
        t.AppendLine();
        t.AppendLine("frame time        share of frames");
        long all = Math.Max(1, Frames);
        double from = 0;
        foreach (var (ms, name) in Bands)
        {
            int n = 0;
            for (int i = (int)Math.Ceiling(from); i <= Math.Min(Bins, double.IsInfinity(ms) ? Bins : (int)Math.Ceiling(ms) - 1); i++)
                n += _histogram[i];
            double share = n / (double)all;
            string label = double.IsInfinity(ms) ? $"> {from:0} ms" : $"≤ {ms:0.#} ms";
            t.AppendLine(ci, $"  {label,-9} {name,-11} {share * 100,5:0.0}% {new string('#', (int)Math.Round(share * 40))}");
            from = ms;
        }
        t.AppendLine();
        t.AppendLine("minute  frames  p50  p99  hitches  slow  where (at the minute's start)");
        for (int i = 0; i < _minutes.Count; i++)
        {
            var m = _minutes[i];
            t.AppendLine(ci, $"  {i,4}  {m.Frames,6}  {Ms(Percentile(m.Histogram, 0.5)),3}  {Ms(Percentile(m.Histogram, 0.99)),3}  {m.Hitches,7}  {m.Slow,4}  {m.Where}");
        }
        Listed(t, _hitches, "the worst hitches");
        return t.ToString();
    }

    /// <summary>
    /// The last <see cref="StorySeconds"/> before <paramref name="now"/>, for a note (F8, F9): the frames' percentiles and
    /// the hitches in them in full, so "it's stuttering here" comes with the frames it means.
    /// </summary>
    public string Story(double now)
    {
        var t = new StringBuilder();
        var ci = CultureInfo.InvariantCulture;
        var frames = _last.Where(f => f.At >= now - StorySeconds).Select(f => (double)f.Ms).Order().ToArray();
        if (frames.Length == 0)
            return "FRAMES · none timed yet";
        double At(double p) => frames[Math.Clamp((int)Math.Ceiling(p * frames.Length) - 1, 0, frames.Length - 1)];
        double since = double.IsNaN(_start) ? now : now - _start;
        var hitches = _hitches.Where(h => h.At >= since - StorySeconds && h.At <= since).ToList();
        t.AppendLine(ci, $"FRAMES · the last {Math.Min(StorySeconds, since):0} s ({frames.Length} frames): p50 {At(0.5):0.0} ms · p90 {At(0.9):0.0} · p99 {At(0.99):0.0} · longest {frames[^1]:0.0}; {hitches.Count} hitches");
        Listed(t, hitches, "its hitches");
        return t.ToString().TrimEnd();
    }

    static void Listed(StringBuilder t, IReadOnlyList<Hitch> hitches, string called)
    {
        if (hitches.Count == 0)
            return;
        var ci = CultureInfo.InvariantCulture;
        var worst = hitches.OrderByDescending(h => h.Ms).Take(WorstListed).OrderBy(h => h.At).ToList();
        t.AppendLine();
        t.AppendLine(worst.Count < hitches.Count ? $"{called}, the worst {worst.Count} of {hitches.Count}:" : $"{called}:");
        foreach (var h in worst)
        {
            string turn = h.Turn >= 1 ? $"; turning {h.Turn:0}°/s" : "";
            string counts = h.Counts.Length > 0 ? $"; {h.Counts}" : "";
            t.AppendLine(ci, $"  {Clock(h.At)}  {h.Ms:0} ms (median {h.Median:0}), led by {h.Lead}: {h.Parts}; {h.Gc}, {h.Mb:0.0} MB{counts}{turn}; {h.Where}");
        }
    }
}

/// <summary>
/// What a frame did that can cost it (note 691), counted where it's done (a scenery cell built on the frame instead of a
/// worker, a mesh uploaded) and read by <see cref="FrameWatch"/> at the frame's end, for its hitches. Per thread: the
/// frame's own.
/// </summary>
public static class FrameCounts
{
    [ThreadStatic]
    static SortedDictionary<string, int>? t_counts;

    /// <summary><paramref name="n"/> more of <paramref name="what"/> this frame.</summary>
    public static void Add(string what, int n = 1)
    {
        if (n == 0)
            return;
        var counts = t_counts ??= new(StringComparer.Ordinal);
        counts[what] = counts.GetValueOrDefault(what) + n;
    }

    /// <summary>This frame's counts ("cells cooked here 1, meshes uploaded 3"), and a fresh count.</summary>
    public static string Take()
    {
        if (t_counts is not { Count: > 0 } counts)
            return "";
        string s = string.Join(", ", counts.Select(c => $"{c.Key} {c.Value}"));
        counts.Clear();
        return s;
    }
}
