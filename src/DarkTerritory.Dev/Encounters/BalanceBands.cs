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
    /// <summary>
    /// The late half judged per minute open to spawning (note 930; n1-C2's finding): the terminus approach §1 A keeps (the
    /// final stretch, terminus_safe) takes a third of the late half, the grace a little of the early. The raw count is shown
    /// beside it, unbanded. With no nights to measure the open time from, the raw count is judged.
    /// </summary>
    public bool LatePerOpenMinute { get; init; } = true;
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

    /// <summary>
    /// Each group's encounters per minute open to spawning, early half and late (note 930; n1-C2's reckoning). Measured per
    /// second where the sweep recorded it (<see cref="SweepRow.EarlyOpenSeconds"/>); for older sweeps, estimated: a half's
    /// encounters over its seconds less what the director held there (the grace and its bans in the early half; the final
    /// stretch and every terminus ban in the late), × 60, the nights' mean. The held seconds aren't split by half; the grace
    /// sits in the early by construction, the terminus in the late.
    /// </summary>
    public static Dictionary<string, (double Early, double Late)> OpenRates(IEnumerable<SweepRow> nights) =>
        nights.GroupBy(r => r.Cell.Key).ToDictionary(g => g.Key, g =>
        {
            double early = 0, late = 0;
            foreach (var r in g)
            {
                // Measured a second at a time where the sweep has it (n1-C2's fields, the director's held reason each
                // second); a night with no open time in a half counts as level there.
                if (r.EarlyOpenSeconds is not null)
                {
                    early += r.EarlyPerOpenMinute ?? 0;
                    late += r.LatePerOpenMinute ?? 0;
                    continue;
                }
                double half = r.Minutes * 30;
                double held(Func<string, bool> which) => r.HeldBack.Where(kv => which(kv.Key)).Sum(kv => (double)kv.Value);
                double openEarly = Math.Max(60, half - held(k => k == "grace" || k.StartsWith("banned (grace", StringComparison.Ordinal)));
                double openLate = Math.Max(60, half - held(k => k == "final stretch" || k.StartsWith("banned (terminus", StringComparison.Ordinal)));
                early += r.EarlyHalf / openEarly * 60;
                late += r.LateHalf / openLate * 60;
            }
            return (Math.Round(early / g.Count(), 2), Math.Round(late / g.Count(), 2));
        });

    public static GroupScore Score(BalanceBands b, SweepGroup g, (double Early, double Late)? open = null)
    {
        var scores = new List<BandScore>();
        var session = SessionFor(b, g);
        scores.Add(session is { } s
            ? new("encounters / 20 min", g.EncountersPer20Min, Range(s.Per20Min), In(g.EncountersPer20Min, s.Per20Min))
            : new("encounters / 20 min", g.EncountersPer20Min, "no session", null));
        scores.Add(new("first threat (s)", g.FirstThreat, Range(b.FirstThreatSeconds), g.FirstThreat is { } f ? In(f, b.FirstThreatSeconds) : false));
        if (b.LateAtLeastEarly && b.LatePerOpenMinute && open is { } o)
        {
            scores.Add(new("late − early / open min", Math.Round(o.Late - o.Early, 2), "≥ 0", o.Late >= o.Early));
            scores.Add(new("late half − early (raw)", g.LateHalf - g.EarlyHalf, "—", null));
        }
        else if (b.LateAtLeastEarly)
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
    public static string Table(BalanceBands b, IReadOnlyList<SweepGroup> after, IReadOnlyList<SweepGroup>? before = null,
        IEnumerable<SweepRow>? afterNights = null, IEnumerable<SweepRow>? beforeNights = null)
    {
        var old = before?.ToDictionary(g => g.Key) ?? [];
        // Per open minute only when every sweep shown has its nights (the old and the new judged the same way, row for row).
        bool open = afterNights is not null && (before is null || beforeNights is not null);
        var openNow = open ? OpenRates(afterNights!) : null;
        var openThen = open && beforeNights is not null ? OpenRates(beforeNights) : null;
        (double, double)? Open(Dictionary<string, (double Early, double Late)>? rates, string key) =>
            rates is not null && rates.TryGetValue(key, out var r) ? r : null;
        var sb = new StringBuilder();
        int passed = 0, judged = 0, oldPassed = 0, oldJudged = 0;
        foreach (var g in after)
        {
            var now = Score(b, g, Open(openNow, g.Key));
            var then = old.TryGetValue(g.Key, out var o) ? Score(b, o, Open(openThen, g.Key)) : null;
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
