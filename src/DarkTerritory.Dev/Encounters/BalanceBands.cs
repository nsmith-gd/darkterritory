using System.Globalization;
using System.Text;
using Ballast;

namespace DarkTerritory.Dev.Encounters;

/// <summary>One of the brief's §7 sessions: a crew, the trains it's judged on, and its encounters in a 20-minute night.</summary>
public sealed record SessionBand
{
    public int Crew { get; init; }
    public double[] Cars { get; init; } = [0, 99];
    public double[] Per20Min { get; init; } = [0, 0];

    public bool Holds(SweepGroup g) => g.Crew == Crew && g.Cars >= Cars[0] && g.Cars <= Cars[1];
}

/// <summary>
/// tools/director/bands.json (ARCHITECTURE §8 note 930): the brief's §7 balance targets, and the few of ours where §7 names what
/// to watch but gives no number. Benchmarks a sweep is scored against, never spawn quotas; nothing in the game reads them.
/// </summary>
public sealed record BalanceBands
{
    public IReadOnlyList<SessionBand> Encounters { get; init; } = [];
    public double[] FirstThreatSeconds { get; init; } = [90, 150];
    public bool LateAtLeastEarly { get; init; } = true;
    /// <summary>Droughts over 90 s a night, at most, on the mean (ours: §7 says to investigate them).</summary>
    public double DroughtsOver90 { get; init; } = 2;
    /// <summary>The share of a group's nights that get home, at least (ours: §7's "the ability to finish runs").</summary>
    public double DeliveredAtLeast { get; init; } = 0.75;

    public static BalanceBands Load(string path) => DataFile.Load<BalanceBands>(path);

    /// <summary>tools/director/bands.json in the repository whose content this is.</summary>
    public static string DefaultPath(string content) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(content).TrimEnd(Path.DirectorySeparatorChar))!, "tools", "director", "bands.json");
}

/// <summary>A measure of a group against its band: the value (null, not measured), the band said plainly, and whether it's in it.</summary>
public sealed record BandScore(string Measure, double? Value, string Band, bool? Pass);

/// <summary>A group's scores (note 930).</summary>
public sealed record GroupScore(string Key, IReadOnlyList<BandScore> Scores)
{
    public int Passed => Scores.Count(s => s.Pass == true);
    public int Judged => Scores.Count(s => s.Pass is not null);
}

/// <summary>`dt director report`: a sweep's groups scored against the brief's §7 bands, and old against new side by side.</summary>
public static class SweepScoring
{
    /// <summary>The session band that judges <paramref name="g"/>'s encounters, if any holds it.</summary>
    public static SessionBand? SessionFor(BalanceBands b, SweepGroup g) => b.Encounters.FirstOrDefault(s => s.Holds(g));

    public static GroupScore Score(BalanceBands b, SweepGroup g)
    {
        var scores = new List<BandScore>();
        var session = SessionFor(b, g);
        scores.Add(session is { } s
            ? new("encounters / 20 min", g.EncountersPer20Min, Range(s.Per20Min), In(g.EncountersPer20Min, s.Per20Min))
            : new("encounters / 20 min", g.EncountersPer20Min, "no session", null));
        scores.Add(new("first threat (s)", g.FirstThreat, Range(b.FirstThreatSeconds), g.FirstThreat is { } f ? In(f, b.FirstThreatSeconds) : false));
        if (b.LateAtLeastEarly)
            scores.Add(new("late half − early", g.LateHalf - g.EarlyHalf, "≥ 0", g.LateHalf >= g.EarlyHalf));
        scores.Add(new("droughts > 90 s", g.DroughtsOver90, $"≤ {Num(b.DroughtsOver90)}", g.DroughtsOver90 <= b.DroughtsOver90));
        scores.Add(new("delivered", g.Delivered, $"≥ {Num(b.DeliveredAtLeast)}", g.Delivered >= b.DeliveredAtLeast));
        // Watched, not banded (§7, §8: overlap, deaths, cargo): shown beside the scores.
        scores.Add(new("max overlap", g.MaxOverlap, "—", null));
        scores.Add(new("deaths", g.Deaths, "—", null));
        scores.Add(new("cargo kept", g.CargoKept, "—", null));
        scores.Add(new("kinds", g.Kinds, "—", null));
        return new GroupScore(g.Key, scores);
    }

    static bool In(double v, double[] band) => v >= band[0] && v <= band[1];
    static string Range(double[] band) => $"{Num(band[0])}–{Num(band[1])}";
    static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    static string Cell(double? v) => v is { } x ? x.ToString("0.##", CultureInfo.InvariantCulture) : "—";
    static string Mark(bool? pass) => pass switch { true => "🟢", false => "🔴", null => "" };

    /// <summary>
    /// The groups as a markdown table, one row a measure: the band, then each sweep's value and mark, the baseline's (old)
    /// before the sweep's (new) where there is one. A group only one of them has shows its own columns.
    /// </summary>
    public static string Table(BalanceBands b, IReadOnlyList<SweepGroup> after, IReadOnlyList<SweepGroup>? before = null)
    {
        var old = before?.ToDictionary(g => g.Key) ?? [];
        var sb = new StringBuilder();
        int passed = 0, judged = 0, oldPassed = 0, oldJudged = 0;
        foreach (var g in after)
        {
            var now = Score(b, g);
            var then = old.TryGetValue(g.Key, out var o) ? Score(b, o) : null;
            passed += now.Passed;
            judged += now.Judged;
            oldPassed += then?.Passed ?? 0;
            oldJudged += then?.Judged ?? 0;
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"**{g.Key}** ({g.Nights} nights{(then is null ? "" : $"; old {o!.Nights}")})"));
            sb.AppendLine();
            sb.AppendLine(then is null ? "| measure | band | value |" : "| measure | band | old | new |");
            sb.AppendLine(then is null ? "|---|---|---:|" : "|---|---|---:|---:|");
            for (int i = 0; i < now.Scores.Count; i++)
            {
                var n = now.Scores[i];
                string value = $"{Cell(n.Value)} {Mark(n.Pass)}".TrimEnd();
                sb.AppendLine(then is null
                    ? $"| {n.Measure} | {n.Band} | {value} |"
                    : $"| {n.Measure} | {n.Band} | {$"{Cell(then.Scores[i].Value)} {Mark(then.Scores[i].Pass)}".TrimEnd()} | {value} |");
            }
            sb.AppendLine();
        }
        sb.AppendLine(before is null
            ? $"In band: {passed} of {judged}."
            : $"In band: new {passed} of {judged}; old {oldPassed} of {oldJudged} (the groups both have).");
        return sb.ToString();
    }
}
