using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkTerritory.Dev.Trends;

/// <summary>
/// What a trends line tracks (ARCHITECTURE §8 note 523), read from what <c>dt harness</c> and <c>dt perf</c> print, not from
/// their types: the bisect reads an older commit's own output with this commit's reader, so a field that older build didn't
/// have is just absent, and an absent metric is skipped (never a zero). Names are dotted by kind, and tools/trends/metrics.json
/// says of each which way is worse and how far a step may move:
/// <list type="bullet">
/// <item><c>balance.*</c>: how the night went (the run's km, deaths, cars, cargo, money; the threats' grabs and fairness).</item>
/// <item><c>pacing.*</c>: how often something happened (<see cref="Sim.Net.PacingReport"/>).</item>
/// <item><c>net.*</c>: what the link carried and how far a client was corrected.</item>
/// <item><c>moments.*</c>: the clip-worthy moments of queue #256 (note 519), every number in the report's <c>moments</c> once it
/// has one. Not yet: the slot is here so they're trended the day they come.</item>
/// <item><c>perf.*</c>: a frame's counts and CPU phases (<c>dt perf</c>), flat (<c>pc</c>) and in a headset (<c>vr</c>).</item>
/// <item><c>sim.*</c>: what the nights cost the CPU, measured by <c>dt trends measure</c> itself.</item>
/// </list>
/// </summary>
public static class TrendMetrics
{
    /// <summary>One <c>dt harness</c> night's numbers.</summary>
    public static SortedDictionary<string, double> FromHarness(JsonNode report)
    {
        var m = New();
        var run = report["run"] as JsonObject;
        Put(m, "balance.km", Num(run?["distanceKm"]) ?? Num(report["trainDistance"]) / 1000);
        Put(m, "balance.deaths", Num(run?["deaths"]) ?? Num(report["deaths"]));
        if (run is not null)
        {
            // Home or not, as a share of the nights once they're averaged.
            Put(m, "balance.delivered", Str(run["end"]) is { } end ? end.Equals("delivered", StringComparison.OrdinalIgnoreCase) ? 1 : 0 : null);
            Put(m, "balance.carsLost", Num(run["carsLost"]));
            Put(m, "balance.carsDelivered", Num(run["carsDelivered"]));
            Put(m, "balance.cargo", Num(run["cargoDelivered"]));
            Put(m, "balance.net", Num(run["net"]));
            Put(m, "balance.crewHome", Num(run["crewHome"]));
        }
        if (report["threats"] is JsonObject threats)
        {
            Put(m, "balance.grabs", Sum(threats["grabs"]));
            Put(m, "balance.rescues", Sum(threats["rescues"]));
            Put(m, "balance.fairness", Num(threats["fairnessViolations"]));
            Put(m, "balance.packFires", Num(threats["packFires"]));
        }
        if (report["upkeep"] is JsonObject upkeep)
        {
            Put(m, "balance.hotBoxesCaught", Num(upkeep["caught"]));
            Put(m, "balance.couplingsParted", Num(upkeep["parted"]));
        }
        if (report["pacing"] is JsonObject pacing)
        {
            Put(m, "pacing.beatsPerMinute", Num(pacing["beatsPerMinute"]));
            Put(m, "pacing.longestQuietSeconds", Num(pacing["longestQuietSeconds"]));
            Put(m, "pacing.quietOver30", Num(pacing["quietOver30"]));
        }
        Put(m, "net.downKbps", Num(report["downKbpsPerClient"]));
        Put(m, "net.upKbps", Num(report["upKbpsPerClient"]));
        Put(m, "net.maxCorrectionM", Num(report["maxCorrectionM"]));
        if (report["moments"] is { } moments)
            Flatten("moments", moments, m);
        return m;
    }

    /// <summary>
    /// <c>dt perf</c>'s numbers for each target it measured: each view's triangles and draw calls (the same on any machine),
    /// and the CPU's share of the frame: the worst view's, and the scene build, prepare and record phases averaged over the
    /// views. Its GPU pass times are left out: on CI's lavapipe they rank the passes, they aren't a GPU's.
    /// </summary>
    public static SortedDictionary<string, double> FromPerf(JsonNode perf)
    {
        var m = New();
        foreach (string target in new[] { "pc", "vr" })
        {
            if (perf[target]?["views"] is not JsonArray views || views.Count == 0)
                continue;
            string t = $"perf.{target}";
            var rows = views.OfType<JsonObject>().ToList();
            foreach (var v in rows)
                if (Str(v["view"]) is { } view)
                {
                    Put(m, $"{t}.{view}.triangles", Num(v["triangles"]));
                    Put(m, $"{t}.{view}.draws", Num(v["drawsPerFrame"]));
                }
            Put(m, $"{t}.triangles", Max(rows, "triangles"));
            Put(m, $"{t}.draws", Max(rows, "drawsPerFrame"));
            Put(m, $"{t}.cpuMs", Num(perf[target]?["worstCpuMs"]) ?? Max(rows, "cpuMs"));
            foreach (string phase in new[] { "buildMs", "prepareMs", "recordMs" })
                Put(m, $"{t}.{phase}", Mean(rows, phase));
        }
        return m;
    }

    /// <summary>Each metric's mean over the nights that have it: a night without one is left out of its mean, not counted as a zero.</summary>
    public static SortedDictionary<string, double> Mean(IEnumerable<IReadOnlyDictionary<string, double>> nights)
    {
        var sums = new SortedDictionary<string, (double Sum, int N)>(StringComparer.Ordinal);
        foreach (var night in nights)
            foreach (var (name, value) in night)
                sums[name] = sums.TryGetValue(name, out var s) ? (s.Sum + value, s.N + 1) : (value, 1);
        var mean = New();
        foreach (var (name, (sum, n)) in sums)
            mean[name] = Round(sum / n);
        return mean;
    }

    /// <summary>Four significant figures, kept to the integer for big counts: enough for a trend, and short lines.</summary>
    public static double Round(double value)
    {
        if (value == 0 || double.IsNaN(value) || double.IsInfinity(value))
            return value;
        int digits = Math.Clamp(3 - (int)Math.Floor(Math.Log10(Math.Abs(value))), 0, 6);
        return Math.Round(value, digits);
    }

    static SortedDictionary<string, double> New() => new(StringComparer.Ordinal);

    static void Put(SortedDictionary<string, double> m, string name, double? value)
    {
        if (value is { } v && double.IsFinite(v))
            m[name] = Round(v);
    }

    static double? Num(JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.Number ? double.Parse(v.ToJsonString(), CultureInfo.InvariantCulture) : null;

    static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    /// <summary>A by-kind count's total ("grabs": {"Ribbit": 1, "Gaunt": 2}: 3).</summary>
    static double? Sum(JsonNode? node) =>
        node is JsonObject o ? o.Sum(kv => Num(kv.Value) ?? 0) : null;

    static double? Max(List<JsonObject> rows, string field) =>
        rows.Select(r => Num(r[field])).OfType<double>().DefaultIfEmpty(double.NaN).Max() is var x && double.IsFinite(x) ? x : null;

    static double? Mean(List<JsonObject> rows, string field) =>
        rows.Select(r => Num(r[field])).OfType<double>().ToList() is { Count: > 0 } xs ? xs.Average() : null;

    /// <summary>Every number in a node, by its dotted path ("moments.count", "moments.byKind.grabBrokenLate").</summary>
    static void Flatten(string prefix, JsonNode node, SortedDictionary<string, double> m)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (key, child) in o)
                    if (child is not null)
                        Flatten($"{prefix}.{key}", child, m);
                break;
            case JsonValue:
                Put(m, prefix, Num(node));
                break;
        }
    }
}
