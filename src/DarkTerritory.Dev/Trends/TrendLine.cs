using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Ballast;

namespace DarkTerritory.Dev.Trends;

/// <summary>
/// The nights a trends line is measured on (ARCHITECTURE §8 note 523): <c>dt harness</c> nights on one route with the enemies
/// and the upkeep, one a seed, the nights D1 measured by hand ("4-bot full nights with upkeep, frontier:7, 2700 s").
/// <see cref="Harness"/> is that command line less its seed, recorded as it was run: a series compares only lines measured on
/// the same nights, and the bisect runs them on each commit it tries with that commit's own <c>dt harness</c> (an older commit
/// has no <c>dt trends</c>, but every one has the harness).
/// </summary>
public sealed record TrendNights
{
    public string Route { get; init; } = "frontier:7";
    public int Bots { get; init; } = 4;
    public int Cars { get; init; } = 10;
    public double Seconds { get; init; } = 2700;
    /// <summary>Eight: two rounds of four on CI's four cores. "A bot night's distance varies a lot from seed to seed" (note 399),
    /// and the mean of eight moves about half as much as the mean of two.</summary>
    public IReadOnlyList<int> Seeds { get; init; } = [1, 2, 3, 4, 5, 6, 7, 8];
    /// <summary>`dt perf`'s counts and CPU phases as well as the nights.</summary>
    public bool Perf { get; init; }
    /// <summary>The <c>dt harness</c> arguments each night is, less <c>--seed</c>.</summary>
    public string Harness { get; init; } = Line("frontier:7", 4, 10, 2700);

    public static TrendNights Of(string route, int bots, int cars, double seconds, IReadOnlyList<int> seeds, bool perf) => new()
    {
        Route = route,
        Bots = bots,
        Cars = cars,
        Seconds = seconds,
        Seeds = seeds,
        Perf = perf,
        Harness = Line(route, bots, cars, seconds),
    };

    static string Line(string route, int bots, int cars, double seconds) =>
        $"--route {route} --bots {bots} --cars {cars} --enemies --upkeep --seconds {seconds.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>One night's <c>dt harness</c> command line (the arguments after <c>dt</c>).</summary>
    public string[] Args(int seed) => ["harness", .. Harness.Split(' ', StringSplitOptions.RemoveEmptyEntries), "--seed", seed.ToString(CultureInfo.InvariantCulture)];

    /// <summary>What a line's numbers are comparable by: the same nights, the same seeds.</summary>
    [JsonIgnore]
    public string Key => $"{Harness}, seeds {Range(Seeds)}";

    /// <summary>"1-8", "2", or "1,4,7" (and any mix: "1-3,9").</summary>
    public static IReadOnlyList<int> ParseSeeds(string spec)
    {
        var seeds = new List<int>();
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Split('-') is [var a, var b])
                seeds.AddRange(Enumerable.Range(int.Parse(a, CultureInfo.InvariantCulture), int.Parse(b, CultureInfo.InvariantCulture) - int.Parse(a, CultureInfo.InvariantCulture) + 1));
            else
                seeds.Add(int.Parse(part, CultureInfo.InvariantCulture));
        }
        return seeds.Count > 0 ? [.. seeds.Distinct().Order()] : throw new ArgumentException($"no seeds in \"{spec}\"");
    }

    /// <summary>The seeds as people write them: a run of them as "1-8".</summary>
    public static string Range(IReadOnlyList<int> seeds) =>
        seeds.Count > 1 && seeds.Zip(seeds.Skip(1)).All(p => p.Second == p.First + 1)
            ? $"{seeds[0]}-{seeds[^1]}"
            : string.Join(',', seeds);
}

/// <summary>One seed's night on a trends line: its own numbers (D1's "21.7/14.3/21.5 km" are these), and what it took.</summary>
public sealed record TrendSeed(int Seed, double WallSeconds, IReadOnlyDictionary<string, double> Metrics);

/// <summary>
/// One commit's numbers (ARCHITECTURE §8 note 523), a line of <c>trends/main.jsonl</c> on the <c>trends</c> branch:
/// the commit, when, the nights it was measured on, each metric's mean over the seeds (and perf's, once), and each seed's own.
/// </summary>
public sealed partial record TrendLine
{
    public string Commit { get; init; } = "";
    /// <summary>The commit's first line; a pull request's merge names it ("… (#684)", "Merge pull request #688 from …").</summary>
    public string? Subject { get; init; }
    public int? Pr { get; init; }
    /// <summary>The checkout had changes of its own: the numbers aren't the commit's (a local run's, never CI's).</summary>
    public bool Dirty { get; init; }
    /// <summary>The commit the measuring build was built from, when it isn't <see cref="Commit"/> (a stale build).</summary>
    public string? Build { get; init; }
    public DateTime Time { get; init; }
    public TrendNights Options { get; init; } = new();
    public IReadOnlyDictionary<string, double> Metrics { get; init; } = new Dictionary<string, double>();
    public IReadOnlyList<TrendSeed> Seeds { get; init; } = [];
    public double WallSeconds { get; init; }

    [JsonIgnore]
    public string Short => Commit.Length > 8 ? Commit[..8] : Commit;

    public string ToJson() => JsonSerializer.Serialize(this, Compact);

    public static TrendLine FromJson(string json) =>
        JsonSerializer.Deserialize<TrendLine>(json, Compact) is { Commit.Length: > 0 } line ? line
            : throw new InvalidDataException("not a trends line (no commit)");

    /// <summary>A trends file, a line a commit (blank lines skipped); a line that won't read names its number.</summary>
    public static IReadOnlyList<TrendLine> Read(string path)
    {
        var lines = new List<TrendLine>();
        int n = 0;
        foreach (string text in File.ReadLines(path))
        {
            n++;
            if (string.IsNullOrWhiteSpace(text))
                continue;
            try
            {
                lines.Add(FromJson(text));
            }
            catch (JsonException e)
            {
                throw new InvalidDataException($"{path}:{n}: {e.Message}", e);
            }
        }
        return lines;
    }

    /// <summary>The pull request a main commit came from, by its subject: a squash's "(#NNN)" at the end, or a merge's.</summary>
    public static int? PrOf(string? subject) =>
        subject is null ? null
        : PrAtEnd().Match(subject) is { Success: true } squash ? int.Parse(squash.Groups[1].Value, CultureInfo.InvariantCulture)
        : PrMerged().Match(subject) is { Success: true } merge ? int.Parse(merge.Groups[1].Value, CultureInfo.InvariantCulture)
        : null;

    [GeneratedRegex(@"\(#(\d+)\)\s*$")]
    private static partial Regex PrAtEnd();

    [GeneratedRegex(@"^Merge pull request #(\d+)\b")]
    private static partial Regex PrMerged();

    internal static readonly JsonSerializerOptions Compact = new(DataFile.Options) { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
}

/// <summary>The commit a measure is run on: its sha and subject from git, and whether the checkout has changes of its own.</summary>
public sealed record TrendCommit(string Sha, string? Subject, bool Dirty)
{
    /// <summary>From git in <paramref name="repo"/>; with no git there (a copied tree), <paramref name="fallback"/> (the build's commit).</summary>
    public static TrendCommit Read(string repo, string fallback)
    {
        string? Git(params string[] args)
        {
            try
            {
                var start = new ProcessStartInfo("git") { WorkingDirectory = repo, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (string a in args)
                    start.ArgumentList.Add(a);
                using var git = Process.Start(start)!;
                string output = git.StandardOutput.ReadToEnd();
                git.WaitForExit();
                return git.ExitCode == 0 ? output.Trim() : null;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return null;
            }
        }
        return Git("rev-parse", "HEAD") is { Length: > 0 } sha
            ? new(sha, Git("log", "-1", "--format=%s"), Git("status", "--porcelain", "--untracked-files=no") is { Length: > 0 })
            : new(fallback, null, false);
    }
}
