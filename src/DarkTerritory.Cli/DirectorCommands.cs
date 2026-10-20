using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ballast;
using DarkTerritory.Dev.Encounters;
using DarkTerritory.Dev.Trends;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Net;

/// <summary>
/// `dt director sweep` (the spawn director overhaul's #601, the director's brief §8, note 888): `dt harness` nights over a
/// matrix of routes, crews, trains and seeds, hosted as the app hosts them, each watched for its encounters (distinct, not
/// bodies), droughts and overlaps, and summed up by group.
/// <list type="bullet">
/// <item><c>--quick</c>: note 874's quick sweep, frontier:7 with crews of 1, 2, 4 and 8 on 3, 5, 8 and 12 cars, seeds 1–4,
/// which every item's PR that changes the nights runs and quotes.</item>
/// <item><c>--routes local,frontier,deadLines,deepTerritory</c> (a tier alone: the route of the night's seed; or "frontier:7");
/// <c>--crews 1,2,4,8 --cars 2,4,8,12,18</c> every crew on every train, or <c>--cells 1x3,2x5,4x8,8x12</c> just those;
/// <c>--seeds 1-10</c>; <c>--seconds 2700</c>; <c>--parallel n</c>.</item>
/// <item><c>--out f.json</c> the report (the groups and every night); <c>--compare base.json</c> its groups against an
/// earlier sweep's (tools/director/baseline-old.json, the old director). The table goes to stderr, the JSON to stdout.</item>
/// </list>
/// </summary>
static class DirectorCommands
{
    public static int Sweep(string[] args, Func<string[], Action<World>, object> harness)
    {
        var cells = Cells(args);
        double seconds = Opt(args, "--seconds", 2700);
        int parallel = (int)Opt(args, "--parallel", Math.Min(Environment.ProcessorCount, cells.Count));
        string repo = Path.GetDirectoryName(DataFile.FindContentRoot(Environment.CurrentDirectory))!;
        var commit = TrendCommit.Read(repo, Report.Build().Commit);
        Console.Error.WriteLine($"dt director sweep: {cells.Count} nights, {parallel} at a time, at {commit.Sha[..Math.Min(9, commit.Sha.Length)]}{(commit.Dirty ? " (dirty)" : "")}");
        var rows = new SweepRow[cells.Count];
        // Each night is its own host and bots over their own loopback; nothing's shared, so they run side by side.
        Parallel.For(0, cells.Count, new ParallelOptions { MaxDegreeOfParallelism = parallel }, i =>
        {
            var cell = cells[i];
            var watch = new EncounterWatch(Opt(args, "--group", EncounterWatch.DefaultGroupSeconds));
            var clock = Stopwatch.StartNew();
            var report = (HarnessReport)harness(cell.Args(seconds), watch.Observe);
            watch.Grace = report.Threats?.Pressure?.Grace ?? 0;
            var printed = JsonSerializer.SerializeToNode(report, DataFile.Options);
            rows[i] = DirectorSweep.Row(cell, report, watch, clock.Elapsed.TotalSeconds, printed);
            var r = rows[i];
            Console.Error.WriteLine($"  {cell.RouteSpec} crew {cell.Crew} cars {cell.Cars} seed {cell.Seed}: {r.End} at {r.Minutes} min, "
                + $"{r.Encounters} encounters of {r.Kinds} kinds, worst drought {r.LongestDrought:0} s, overlap {r.MaxOverlap} ({r.WallSeconds:0} s)");
        });
        var report = new SweepReport(commit.Dirty ? commit.Sha + "+dirty" : commit.Sha, seconds, DirectorSweep.Groups(rows), rows);
        Console.Error.WriteLine();
        Console.Error.Write(DirectorSweep.Table(report.Groups));
        if (Str(args, "--compare", "") is { Length: > 0 } baseline)
        {
            var before = JsonSerializer.Deserialize<SweepReport>(File.ReadAllText(baseline), DataFile.Options)!;
            Console.Error.WriteLine();
            Console.Error.WriteLine($"against {baseline} ({before.Commit[..Math.Min(9, before.Commit.Length)]}):");
            Console.Error.Write(DirectorSweep.Compare(before.Groups, report.Groups));
        }
        string json = JsonSerializer.Serialize(report, DataFile.Options);
        if (Str(args, "--out", "") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            File.WriteAllText(output, json + "\n");
        }
        Console.WriteLine(json);
        return 0;
    }

    static IReadOnlyList<SweepCell> Cells(string[] args)
    {
        if (args.Contains("--quick"))
            return DirectorSweep.Quick();
        var routes = Str(args, "--routes", "frontier").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sessions = DirectorSweep.ParseSessions(Str(args, "--cells", ""), Ints(Str(args, "--crews", "1,2,4,8")), Ints(Str(args, "--cars", "2,4,8,12,18")));
        return DirectorSweep.Grid(routes, sessions, TrendNights.ParseSeeds(Str(args, "--seeds", "1-4")));
    }

    static int[] Ints(string spec) => [.. spec.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => int.Parse(s, CultureInfo.InvariantCulture))];

    static string Str(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    static double Opt(string[] args, string name, double fallback) =>
        Str(args, name, "") is { Length: > 0 } v ? double.Parse(v, CultureInfo.InvariantCulture) : fallback;
}
