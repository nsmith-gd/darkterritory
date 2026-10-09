using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ballast;
using DarkTerritory.Dev.Trends;
using DarkTerritory.Game;

/// <summary>
/// `dt trends` (ARCHITECTURE §8 note 523): main's numbers, commit by commit, and the falls among them.
/// <list type="bullet">
/// <item><c>dt trends measure [--route frontier:7] [--bots 4] [--cars 10] [--seeds 1-8] [--seconds 2700] [--perf] [--parallel n]
/// [--append file.jsonl]</c>: this checkout's line, one JSON object on one line: the commit, the time, the nights, each
/// metric's mean and each seed's own. The Trends workflow appends it to <c>trends/main.jsonl</c> on the <c>trends</c> branch.</item>
/// <item><c>dt trends show &lt;file.jsonl&gt; [--metric m] [--last 10] [--bands f] [--markdown | --comment [--run url]]</c>: each
/// metric over the last commits and the steps past their bands (tools/trends/metrics.json); <c>--markdown</c> as the job
/// summary has it, <c>--comment</c> the pull request comment for the newest commit's falls (nothing when there are none).</item>
/// <item><c>dt trends nights [measure's options]</c>: the nights as <c>dt harness</c> runs them, for tools/trends/bisect.sh.</item>
/// <item><c>dt trends judge --metric m [--good g --bad b] &lt;report.json&gt;…</c>: a metric from <c>dt harness</c> and
/// <c>dt perf</c> reports (any build's), and with the two ends, the bisect's call: exit 0 good, 1 bad, 125 not measured.</item>
/// <item><c>dt trends value &lt;file.jsonl&gt; --commit sha --metric m</c>: a measured commit's value (the bisect's ends).</item>
/// </list>
/// </summary>
static class TrendsCommands
{
    /// <param name="harness">The CLI's own <c>dt harness</c>: given its arguments, the report it prints.</param>
    /// <param name="perf">The CLI's own <c>dt perf</c>, likewise.</param>
    public static int Run(string[] args, Func<string[], object> harness, Func<string[], object> perf) => args switch
    {
        ["trends", "measure", ..] => Measure(args, harness, perf),
        ["trends", "nights", ..] => PrintNights(Nights(args)),
        ["trends", "show", var file, ..] when File.Exists(file) => Show(file, args),
        ["trends", "judge", ..] => Judge(args),
        ["trends", "value", var file, ..] when File.Exists(file) => Value(file, args),
        _ => Usage(),
    };

    static int Usage()
    {
        Console.Error.WriteLine("usage: dt trends measure [--route r] [--bots n] [--cars n] [--seeds 1-8] [--seconds s] [--perf] [--append f.jsonl]"
            + " | dt trends show <f.jsonl> [--metric m] [--last n] [--markdown | --comment] | dt trends nights | dt trends judge --metric m [--good g --bad b] <reports>"
            + " | dt trends value <f.jsonl> --commit sha --metric m");
        return 2;
    }

    static TrendNights Nights(string[] args) => TrendNights.Of(Str(args, "--route", "frontier:7"), (int)Opt(args, "--bots", 4), (int)Opt(args, "--cars", 10),
        Opt(args, "--seconds", 2700), TrendNights.ParseSeeds(Str(args, "--seeds", "1-8")), args.Contains("--perf"));

    static int PrintNights(TrendNights n) => Print(new { n.Harness, n.Seeds, n.Perf });

    static int Measure(string[] args, Func<string[], object> harness, Func<string[], object> perf)
    {
        var nights = Nights(args);
        string repo = Path.GetDirectoryName(DataFile.FindContentRoot(Environment.CurrentDirectory))!;
        var (_, build) = Report.Build();
        var commit = TrendCommit.Read(repo, build);
        if (build != "unknown" && build != commit.Sha)
            Console.Error.WriteLine($"dt was built from {build}, the checkout is at {commit.Sha}: build it again to measure this commit");
        if (commit.Dirty)
            Console.Error.WriteLine("the checkout has changes of its own: the line is marked dirty");
        // Four nights at once on CI's four cores: a night is one thread's work, and memory's no limit (about 300 MB each).
        int parallel = (int)Opt(args, "--parallel", Math.Min(Environment.ProcessorCount, nights.Seeds.Count));
        var line = TrendMeasure.Run(nights, commit, build,
            seed => TrendMeasure.AsPrinted(harness(nights.Args(seed)), DataFile.Options),
            // Six frames a view, not perf's twelve: the counts are the same, and lavapipe takes most of a second a frame.
            nights.Perf ? () => TrendMeasure.AsPrinted(perf(["perf", "--frames", Str(args, "--perf-frames", "6")]), DataFile.Options) : null,
            parallel, Console.Error, DateTime.UtcNow);
        string json = line.ToJson();
        Console.WriteLine(json);
        if (Str(args, "--append", "") is { Length: > 0 } append)
            File.AppendAllText(append, json + "\n");
        return 0;
    }

    static int Show(string file, string[] args)
    {
        var lines = TrendLine.Read(file);
        var series = TrendSeries.Comparable(lines);
        var bands = TrendBands.Load(Str(args, "--bands", TrendBands.DefaultPath(DataFile.FindContentRoot(Environment.CurrentDirectory))));
        string? metric = Str(args, "--metric", "") is { Length: > 0 } asked ? TrendSeries.Resolve(asked, series.SelectMany(l => l.Metrics.Keys)) : null;
        int last = (int)Opt(args, "--last", 10);
        if (args.Contains("--markdown"))
        {
            Console.Write(TrendMarkdown.Summary(series, bands, last, metric));
            return 0;
        }
        if (args.Contains("--comment"))
        {
            if (TrendMarkdown.Comment(series, bands, Str(args, "--run", "")) is { } comment)
                Console.Write(comment);
            return 0;
        }
        var shown = series.TakeLast(Math.Max(1, last)).ToList();
        var steps = TrendSeries.Steps(series, bands, series.Count - shown.Count, metric);
        var names = shown.SelectMany(l => l.Metrics.Keys).Distinct().Where(n => metric is null || n == metric).Order(StringComparer.Ordinal);
        var newest = series.LastOrDefault();
        return Print(new
        {
            file = Path.GetFullPath(file),
            lines = lines.Count,
            // Lines measured on other nights (an older route or seeds) start their own series and are left out of this one.
            series = series.Count,
            nights = newest?.Options.Key,
            newest = newest is null ? null : new
            {
                commit = newest.Commit,
                newest.Pr,
                newest.Subject,
                newest.Time,
                falls = steps.Where(s => s.Fall && s.Line == newest).Select(Step),
                rises = steps.Where(s => !s.Fall && s.Line == newest).Select(Step),
            },
            metrics = names.Select(n =>
            {
                var band = bands.For(n);
                return new
                {
                    name = n,
                    bad = band.Bad.ToString().ToLowerInvariant(),
                    band = new { band.Abs, band.Rel },
                    values = shown.Where(l => l.Metrics.ContainsKey(n)).Select(l => new { commit = l.Short, value = l.Metrics[n] }),
                    falls = steps.Count(s => s.Fall && s.Metric == n),
                    rises = steps.Count(s => !s.Fall && s.Metric == n),
                };
            }),
            falls = steps.Where(s => s.Fall).Select(Step),
            rises = steps.Where(s => !s.Fall).Select(Step),
        });
    }

    static object Step(TrendStep s) => new
    {
        s.Metric,
        commit = s.Line.Short,
        s.Line.Pr,
        s.Value,
        level = Math.Round(s.Level, 4),
        width = Math.Round(s.Width, 4),
        bad = s.Bad.ToString().ToLowerInvariant(),
        previous = new { commit = s.Previous.Short, value = s.PreviousValue },
        s.Seeds,
    };

    /// <summary>A commit's metric from the reports its own build printed: the bisect's step (tools/trends/bisect.sh).</summary>
    static int Judge(string[] args)
    {
        string asked = Str(args, "--metric", "");
        // The reports are every argument that's a file, less the options' values.
        string[] valued = ["--metric", "--good", "--bad"];
        var files = args.Select((a, i) => (a, i)).Skip(2).Where(p => !valued.Contains(args[p.i - 1]) && File.Exists(p.a)).Select(p => p.a).ToList();
        var nights = new List<IReadOnlyDictionary<string, double>>();
        var perf = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (string f in files)
        {
            var node = JsonNode.Parse(File.ReadAllText(f));
            if (node?["ticks"] is not null)
                nights.Add(TrendMetrics.FromHarness(node));
            else if (node?["pc"] is not null || node?["vr"] is not null)
                foreach (var (k, v) in TrendMetrics.FromPerf(node))
                    perf[k] = v;
        }
        var mean = TrendMetrics.Mean(nights);
        foreach (var (k, v) in perf)
            mean[k] = v;
        string metric;
        try
        {
            metric = TrendSeries.Resolve(asked, mean.Keys);
        }
        catch (ArgumentException e)
        {
            // Not measured by this commit's build (older than the metric): the bisect skips it.
            Print(new { metric = asked, value = (double?)null, verdict = "skip", why = e.Message, files = files.Count });
            return 125;
        }
        double value = mean[metric];
        if (!args.Contains("--good") || !args.Contains("--bad"))
        {
            Print(new { metric, value, nights = nights.Select(n => n.TryGetValue(metric, out double v) ? v : (double?)null) });
            return 0;
        }
        double good = Opt(args, "--good", 0), bad = Opt(args, "--bad", 0);
        bool isBad = TrendBisect.IsBad(value, good, bad);
        Print(new { metric, value, good, bad, midpoint = TrendBisect.Midpoint(good, bad), verdict = isBad ? "bad" : "good" });
        return isBad ? 1 : 0;
    }

    static int Value(string file, string[] args)
    {
        string sha = Str(args, "--commit", "");
        var line = sha.Length < 4 ? null : TrendLine.Read(file).LastOrDefault(l => l.Commit.StartsWith(sha, StringComparison.OrdinalIgnoreCase));
        if (line is null)
        {
            Console.Error.WriteLine($"no line for commit {sha} in {file}");
            return 1;
        }
        string metric = TrendSeries.Resolve(Str(args, "--metric", ""), line.Metrics.Keys);
        return Print(new { line.Commit, metric, value = line.Metrics[metric], line.Options.Harness, line.Options.Seeds });
    }

    static int Print(object value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, DataFile.Options));
        return 0;
    }

    static string Str(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    static double Opt(string[] args, string name, double fallback) =>
        Str(args, name, "") is { Length: > 0 } v ? double.Parse(v, CultureInfo.InvariantCulture) : fallback;
}
