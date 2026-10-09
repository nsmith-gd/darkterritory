using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkTerritory.Dev.Trends;

/// <summary>
/// <c>dt trends measure</c> (ARCHITECTURE §8 note 523): one commit's trends line. Each seed's night is a <c>dt harness</c> night
/// (the CLI hands in its own harness, so these are the nights <c>dt harness</c> prints, not a copy of them), read back from its
/// JSON as the bisect reads an older build's; then <c>dt perf</c> once, if asked. The nights run side by side (each is its own
/// host and bots over its own loopback, deterministic by its seed, so running them together changes no number) and what the
/// process spent on them, over the ticks they ran, is <c>sim.cpuMsPerTick</c>: the sim's cost, the host's and every bot's.
/// </summary>
public static class TrendMeasure
{
    /// <param name="night">A seed's night: what <c>dt harness</c> prints for it, parsed.</param>
    /// <param name="perf">What <c>dt perf</c> prints, parsed; null to leave perf out.</param>
    /// <param name="parallel">How many nights at once (a night is one thread's work).</param>
    /// <param name="build">The commit the measuring build was built from (the SDK's stamp), kept when it isn't the checkout's.</param>
    public static TrendLine Run(TrendNights nights, TrendCommit commit, string build, Func<int, JsonNode> night, Func<JsonNode>? perf, int parallel,
        TextWriter? log, DateTime now)
    {
        var watch = Stopwatch.StartNew();
        var cpuBefore = CpuTime();
        var seeds = new TrendSeed[nights.Seeds.Count];
        long ticks = 0;
        var said = new object();
        Parallel.For(0, seeds.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallel) }, i =>
        {
            int seed = nights.Seeds[i];
            var clock = Stopwatch.StartNew();
            var report = night(seed);
            var metrics = TrendMetrics.FromHarness(report);
            Interlocked.Add(ref ticks, report["ticks"] is JsonValue t && t.TryGetValue(out long n) ? n : 0);
            seeds[i] = new TrendSeed(seed, Math.Round(clock.Elapsed.TotalSeconds, 1), metrics);
            lock (said)
                log?.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"seed {seed}: {Get(metrics, "balance.km")} km, {Get(metrics, "balance.deaths")} deaths, {Get(metrics, "balance.carsLost")} cars lost ({clock.Elapsed.TotalSeconds:0} s)"));
        });
        var cpu = CpuTime() - cpuBefore;
        var mean = TrendMetrics.Mean(seeds.Select(s => s.Metrics));
        if (ticks > 0)
            mean["sim.cpuMsPerTick"] = TrendMetrics.Round(cpu.TotalMilliseconds / ticks);
        if (perf is not null)
        {
            var clock = Stopwatch.StartNew();
            foreach (var (name, value) in TrendMetrics.FromPerf(perf()))
                mean[name] = value;
            log?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"perf: {clock.Elapsed.TotalSeconds:0} s"));
        }
        return new TrendLine
        {
            Commit = commit.Sha,
            Subject = commit.Subject,
            Pr = TrendLine.PrOf(commit.Subject),
            Dirty = commit.Dirty,
            Build = build.Length > 0 && build != "unknown" && build != commit.Sha ? build : null,
            Time = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc),
            Options = nights,
            Metrics = mean,
            Seeds = seeds,
            WallSeconds = Math.Round(watch.Elapsed.TotalSeconds, 1),
        };
    }

    /// <summary>A report object as <c>dt</c> prints it, read back: what <see cref="Run"/>'s nights hand it.</summary>
    public static JsonNode AsPrinted(object report, JsonSerializerOptions options) =>
        JsonNode.Parse(JsonSerializer.Serialize(report, options)) ?? throw new InvalidDataException("the report printed as null");

    static string Get(IReadOnlyDictionary<string, double> m, string name) =>
        m.TryGetValue(name, out double v) ? v.ToString(CultureInfo.InvariantCulture) : "?";

    static TimeSpan CpuTime()
    {
        using var self = Process.GetCurrentProcess();
        return self.TotalProcessorTime;
    }
}
