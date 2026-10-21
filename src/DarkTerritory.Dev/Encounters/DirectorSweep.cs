using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using DarkTerritory.Sim.Net;

namespace DarkTerritory.Dev.Encounters;

/// <summary>
/// One night of `dt director sweep` (the director's brief §8, note 888): a route (a tier alone takes the night's seed as the
/// route's, as `dt balance` does; "frontier:7" keeps route 7 for every seed), a crew of bots, a train, and the harness seed
/// (the bots' and the director's).
/// </summary>
public sealed record SweepCell(string Route, int Crew, int Cars, int Seed)
{
    /// <summary>The route this night runs: "frontier:7" as given, or "frontier" with the night's seed.</summary>
    public string RouteSpec => Route.Contains(':') ? Route : $"{Route}:{Seed}";
    public string Tier => Route.Split(':')[0];
    /// <summary>The group the summary puts it in: every seed of one route, crew and train.</summary>
    public string Key => $"{Route} crew {Crew} cars {Cars}";

    /// <summary>Its <c>dt harness</c> command line: hosted as the app hosts it, with the enemies and the upkeep, as trends' nights are.</summary>
    public string[] Args(double seconds) =>
        ["harness", "--as-app", "--route", RouteSpec, "--bots", Num(Crew), "--cars", Num(Cars), "--enemies", "--upkeep",
            "--seconds", seconds.ToString(CultureInfo.InvariantCulture), "--seed", Num(Seed)];

    static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// How one night went, in the brief's terms (§7, §8). Distinct encounters and droughts are the <see cref="EncounterWatch"/>'s,
/// measured from the enemies' spines; the rest is the harness's report. <see cref="Telemetry"/> is #600's encounter block, as
/// its harness report gives it, once it lands (null before).
/// </summary>
public sealed record SweepRow(SweepCell Cell, string End, bool Delivered, double Minutes, double Km, int Deaths, double CargoKept,
    int CarsDelivered, int CarsLost, int Sent, IReadOnlyDictionary<string, int> SentByKind, int Encounters,
    IReadOnlyDictionary<string, int> EncountersByKind, int Kinds, int EarlyHalf, int LateHalf, double? FirstThreat,
    double LongestDrought, double LongestDangerDrought, int DroughtsOver90, int MaxOverlap, int OverlapSeconds,
    int CappedIdleSeconds, int PackFires, IReadOnlyDictionary<string, int> HeldBack, double WallSeconds)
{
    /// <summary>#600's `encounters` block, copied whole when the harness report has one (attempts, failures and their reasons).</summary>
    public JsonNode? Telemetry { get; init; }
    /// <summary>From <see cref="Telemetry"/>'s attempts, every channel's together: spawns tried, placed, and charged though
    /// nothing was placed (§2 issue 3); null with no telemetry (the old director's baseline).</summary>
    public int? Tried { get; init; }
    public int? Placed { get; init; }
    public int? ChargedUnplaced { get; init; }
    /// <summary>
    /// Each half's seconds open to the director's encounters, not under a quiet the brief keeps (grace, the final stretch and the
    /// terminus's, the facility lull; <see cref="EncounterWatch.Protects"/>), and each half's encounters per open minute (#613,
    /// note 930): the terminus approach falls in the late half, so its raw count understates the escalation.
    /// </summary>
    public double? EarlyOpenSeconds { get; init; }
    public double? LateOpenSeconds { get; init; }
    public double? EarlyPerOpenMinute { get; init; }
    public double? LatePerOpenMinute { get; init; }
}

/// <summary>A route, crew and train's nights together: the means the brief's §7 table is judged by, and their spread.</summary>
public sealed record SweepGroup(string Key, string Route, int Crew, int Cars, int Nights, double Encounters, int EncountersMin, int EncountersMax,
    double EncountersPer20Min, double Kinds, double EarlyHalf, double LateHalf, double? FirstThreat, double LongestDrought, double WorstDrought,
    double DroughtsOver90, double MaxOverlap, double OverlapShare, double Deaths, double CargoKept, double Delivered, double Minutes,
    double Sent, double PackFires)
{
    /// <summary>Of the spawns tried, the share placed (#600's telemetry); null when no night had it.</summary>
    public double? Placement { get; init; }
    /// <summary>The nights' mean encounters per open minute in each half (<see cref="SweepRow.EarlyPerOpenMinute"/>); null when no night had it.</summary>
    public double? EarlyPerOpenMinute { get; init; }
    public double? LatePerOpenMinute { get; init; }
}

public sealed record SweepReport(string Commit, double Seconds, IReadOnlyList<SweepGroup> Groups, IReadOnlyList<SweepRow> Nights);

/// <summary>`dt director sweep`: the matrix, the rows, the groups, the table, and a sweep against a baseline.</summary>
public static class DirectorSweep
{
    /// <summary>The brief's matrix (§8): crews 1, 2, 4, 8 × trains 2, 4, 8, 12, 18 × the four tiers.</summary>
    public static readonly int[] Crews = [1, 2, 4, 8];
    public static readonly int[] Trains = [2, 4, 8, 12, 18];
    public static readonly string[] Tiers = ["local", "frontier", "deadLines", "deepTerritory"];

    /// <summary>The brief's §7 sessions, each crew on a train its size might run: solo 3, two 5, four 8, eight 12.</summary>
    public static readonly (int Crew, int Cars)[] Sessions = [(1, 3), (2, 5), (4, 8), (8, 12)];

    /// <summary>Note 874's quick sweep, for every item's PR that changes the nights: frontier:7, the four sessions, seeds 1–4.</summary>
    public static IReadOnlyList<SweepCell> Quick() => Grid(["frontier:7"], Sessions, [1, 2, 3, 4]);

    public static IReadOnlyList<SweepCell> Grid(IEnumerable<string> routes, IEnumerable<(int Crew, int Cars)> sessions, IEnumerable<int> seeds) =>
        [.. from r in routes from s in sessions from seed in seeds select new SweepCell(r, s.Crew, s.Cars, seed)];

    /// <summary>"1x3,2x5" → its sessions; or every crew with every train.</summary>
    public static IReadOnlyList<(int Crew, int Cars)> ParseSessions(string? cells, IEnumerable<int> crews, IEnumerable<int> cars) =>
        cells is { Length: > 0 }
            ? [.. cells.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(c => c.Split('x') is [var a, var b]
                ? (int.Parse(a, CultureInfo.InvariantCulture), int.Parse(b, CultureInfo.InvariantCulture))
                : throw new ArgumentException($"a cell is crew x cars (\"2x5\"), not \"{c}\""))]
            : [.. from c in crews from n in cars select (c, n)];

    /// <summary>A night's row from its harness report and its watch.</summary>
    public static SweepRow Row(SweepCell cell, HarnessReport r, EncounterWatch w, double wallSeconds, JsonNode? printed = null)
    {
        var run = r.Run;
        var t = r.Threats;
        var enc = w.Encounters;
        double half = r.Seconds / 2;
        var droughts = w.Droughts;
        var (earlyOpen, lateOpen) = w.OpenSeconds(half, r.Seconds);
        int early = enc.Count(e => e.Start < half), late = enc.Count(e => e.Start >= half);
        return new SweepRow(cell, run?.End.ToString() ?? "none", run?.End == Sim.Run.RunEnd.Delivered, Math.Round((run?.Seconds ?? r.Seconds) / 60, 1),
            Math.Round(run?.DistanceKm ?? r.TrainDistance / 1000, 2), run?.CrewLost ?? r.Deaths, t?.MeanCargoIntegrity ?? 1,
            run?.CarsDelivered ?? 0, run?.CarsLost ?? 0, t?.Spawned.Values.Sum() ?? 0, t?.Spawned ?? new Dictionary<string, int>(), enc.Count,
            Sorted(enc.GroupBy(e => e.Kind.ToString()).ToDictionary(g => g.Key, g => g.Count())), enc.Select(e => e.Kind).Distinct().Count(),
            enc.Count(e => e.Start < half), enc.Count(e => e.Start >= half),
            enc.FirstOrDefault(e => e.Start >= w.Grace) is { } first ? Math.Round(first.Start - w.Grace, 1) : null,
            Math.Round(droughts.DefaultIfEmpty(0).Max(), 1), Math.Round(w.DangerDroughts.DefaultIfEmpty(0).Max(), 1), droughts.Count(d => d > 90),
            w.MaxOverlap, w.OverlapSeconds.Skip(2).Sum(), w.CappedIdleSeconds, t?.PackFires ?? 0,
            Sorted(t?.Director.Where(kv => kv.Key != "sent").ToDictionary() ?? []), Math.Round(wallSeconds, 1))
        {
            Telemetry = printed?["threats"]?["encounters"]?.DeepClone() ?? printed?["encounters"]?.DeepClone(),
            EarlyOpenSeconds = Math.Round(earlyOpen),
            LateOpenSeconds = Math.Round(lateOpen),
            EarlyPerOpenMinute = earlyOpen > 0 ? Math.Round(early / earlyOpen * 60, 3) : null,
            LatePerOpenMinute = lateOpen > 0 ? Math.Round(late / lateOpen * 60, 3) : null,
        } is var row && row.Telemetry?["attempts"] is JsonObject attempts
            ? row with
            {
                Tried = attempts.Sum(a => Int(a.Value?["tried"])),
                Placed = attempts.Sum(a => Int(a.Value?["placed"])),
                ChargedUnplaced = attempts.Sum(a => Int(a.Value?["chargedUnplaced"])),
            }
            : row;
    }

    static int Int(JsonNode? n) => n is JsonValue v && v.TryGetValue(out int i) ? i : 0;

    static SortedDictionary<string, int> Sorted(IDictionary<string, int> d) => new(d, StringComparer.Ordinal);

    public static IReadOnlyList<SweepGroup> Groups(IEnumerable<SweepRow> rows) =>
        [.. rows.GroupBy(r => r.Cell.Key).Select(g =>
        {
            var c = g.First().Cell;
            static double M(IEnumerable<double> v) => Math.Round(v.DefaultIfEmpty(0).Average(), 2);
            var firsts = g.Where(r => r.FirstThreat is not null).Select(r => r.FirstThreat!.Value).ToList();
            return new SweepGroup(g.Key, c.Route, c.Crew, c.Cars, g.Count(), M(g.Select(r => (double)r.Encounters)), g.Min(r => r.Encounters),
                g.Max(r => r.Encounters), M(g.Select(r => r.Minutes > 0 ? r.Encounters * 20 / r.Minutes : 0)), M(g.Select(r => (double)r.Kinds)),
                M(g.Select(r => (double)r.EarlyHalf)), M(g.Select(r => (double)r.LateHalf)), firsts.Count > 0 ? M(firsts) : null,
                M(g.Select(r => r.LongestDrought)), g.Max(r => r.LongestDrought), M(g.Select(r => (double)r.DroughtsOver90)),
                M(g.Select(r => (double)r.MaxOverlap)), M(g.Select(r => r.Minutes > 0 ? r.OverlapSeconds / (r.Minutes * 60) : 0)),
                M(g.Select(r => (double)r.Deaths)), M(g.Select(r => r.CargoKept)), M(g.Select(r => r.Delivered ? 1.0 : 0)), M(g.Select(r => r.Minutes)),
                M(g.Select(r => (double)r.Sent)), M(g.Select(r => (double)r.PackFires)))
            {
                Placement = g.Sum(r => r.Tried ?? 0) is > 0 and var tried ? Math.Round((double)g.Sum(r => r.Placed ?? 0) / tried, 3) : null,
                EarlyPerOpenMinute = g.Any(r => r.EarlyPerOpenMinute is not null) ? M(g.Where(r => r.EarlyPerOpenMinute is not null).Select(r => r.EarlyPerOpenMinute!.Value)) : null,
                LatePerOpenMinute = g.Any(r => r.LatePerOpenMinute is not null) ? M(g.Where(r => r.LatePerOpenMinute is not null).Select(r => r.LatePerOpenMinute!.Value)) : null,
            };
        }).OrderBy(g => Array.IndexOf(Tiers, g.Route.Split(':')[0])).ThenBy(g => g.Route, StringComparer.Ordinal).ThenBy(g => g.Crew).ThenBy(g => g.Cars)];

    /// <summary>The groups as a markdown table, as a note or a PR quotes them.</summary>
    public static string Table(IEnumerable<SweepGroup> groups)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| route | crew | cars | nights | encounters (min–max) | per 20 min | kinds | early / late | first (s) | drought mean / worst (s) | >90 s | overlap max / share | deaths | cargo | delivered | minutes | sent | placed |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var g in groups)
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {g.Route} | {g.Crew} | {g.Cars} | {g.Nights} | {g.Encounters:0.0} ({g.EncountersMin}–{g.EncountersMax}) | {g.EncountersPer20Min:0.0} | {g.Kinds:0.0} | {g.EarlyHalf:0.0} / {g.LateHalf:0.0} | {(g.FirstThreat is { } f ? f.ToString("0", CultureInfo.InvariantCulture) : "—")} | {g.LongestDrought:0} / {g.WorstDrought:0} | {g.DroughtsOver90:0.0} | {g.MaxOverlap:0.0} / {g.OverlapShare:P0} | {g.Deaths:0.00} | {g.CargoKept:0.00} | {g.Delivered:P0} | {g.Minutes:0.0} | {g.Sent:0.0} | {(g.Placement is { } p ? p.ToString("P0", CultureInfo.InvariantCulture) : "—")} |"));
        return sb.ToString();
    }

    /// <summary>
    /// A sweep against a baseline's (an older commit's, or the old director's in tools/director/baseline): each group both have,
    /// the after's mean less the before's, for the measures an item says moved.
    /// </summary>
    public static string Compare(IEnumerable<SweepGroup> before, IEnumerable<SweepGroup> after)
    {
        var old = before.ToDictionary(g => g.Key);
        var sb = new StringBuilder();
        sb.AppendLine("| route | crew | cars | encounters | per 20 min | kinds | late half | worst drought (s) | overlap max | deaths | delivered | minutes |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var a in after)
        {
            if (!old.TryGetValue(a.Key, out var b))
                continue;
            static string D(double x, double y, string f = "0.0") =>
                $"{x.ToString(f, CultureInfo.InvariantCulture)} → {y.ToString(f, CultureInfo.InvariantCulture)}";
            sb.AppendLine($"| {a.Route} | {a.Crew} | {a.Cars} | {D(b.Encounters, a.Encounters)} | {D(b.EncountersPer20Min, a.EncountersPer20Min)} | {D(b.Kinds, a.Kinds)} | {D(b.LateHalf, a.LateHalf)} | {D(b.WorstDrought, a.WorstDrought, "0")} | {D(b.MaxOverlap, a.MaxOverlap)} | {D(b.Deaths, a.Deaths, "0.00")} | {D(b.Delivered, a.Delivered, "0%")} | {D(b.Minutes, a.Minutes)} |");
        }
        return sb.ToString();
    }
}
