using System.Globalization;
using System.Text;
using System.Text.Json;
using Ballast;
using DarkTerritory.Dev.Social;

/// <summary>
/// `dt social` (queue #255, ARCHITECTURE §8 note 1038): GDD §20's social test, criteria 2 (better by two than by one), 3 (8–20
/// s for a friend to act) and 5 (someone gets blamed), per creature from staged grabs and bot nights (<see cref="SocialTest"/>),
/// and the crew's own numbers (time split up, calls made, the dead watching). Thresholds in content/tuning/social.json.
/// <list type="bullet">
/// <item><c>dt social [--only gaunt,ribbit] [--seconds 120] [--seeds 2] [--crews 1,2] [--route frontier:7] [--parallel n]
/// [--flags knotter.tautHold,whistler.carry]</c> -> out/social/social.json and out/social/social.md (a creature's pass or fail
/// on each criterion, and why; note 1051: with the creature flags on, and those off shown on, staged).</item>
/// </list>
/// </summary>
static class SocialCommands
{
    public static int Run(string content, string[] args)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        string dir = Str(args, "--out", "out/social");
        Directory.CreateDirectory(dir);
        var t = DataFile.Load<SocialTuning>(Path.Combine(content, SocialTuning.File));
        t = t with
        {
            Creatures = Str(args, "--only", "") is { Length: > 0 } o ? o.Split(',') : t.Creatures,
            Route = Str(args, "--route", t.Route),
            Seconds = Opt(args, "--seconds", t.Seconds),
            Seeds = (int)Opt(args, "--seeds", t.Seeds),
            Cars = (int)Opt(args, "--cars", t.Cars),
            Crews = Str(args, "--crews", "") is { Length: > 0 } cs ? [.. cs.Split(',').Select(x => int.Parse(x, CultureInfo.InvariantCulture))] : t.Crews,
            Flags = Str(args, "--flags", "") is { Length: > 0 } f ? f.Split(',') : t.Flags,
        };
        var report = SocialTest.Run(content, t, (int)Opt(args, "--parallel", Environment.ProcessorCount), Console.Error.WriteLine);
        File.WriteAllText(Path.Combine(dir, "social.json"), JsonSerializer.Serialize(report, DataFile.Options));
        File.WriteAllText(Path.Combine(dir, "social.md"), Markdown(report));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            json = Path.GetFullPath(Path.Combine(dir, "social.json")),
            markdown = Path.GetFullPath(Path.Combine(dir, "social.md")),
            report.Fails,
            report.Crew,
            wallSeconds = Math.Round(clock.Elapsed.TotalSeconds),
        }, DataFile.Options));
        return 0;
    }

    /// <summary>The short summary: a line a creature, a criterion a column, then the reasons and the crew's numbers.</summary>
    public static string Markdown(SocialReport r)
    {
        var md = new StringBuilder();
        md.AppendLine("# The social test (GDD §20), measured");
        md.AppendLine();
        md.AppendLine($"`dt social`: {r.Route}, {r.Cars} cars, {r.Seconds:0} s nights, {r.Seeds} seeds at crews {string.Join(", ", r.Crews)}; thresholds in content/tuning/social.json (note 1038).");
        md.AppendLine();
        md.AppendLine("| Creature | 2: better by two | 3: 8–20 s to act | 5: someone blamed |");
        md.AppendLine("|---|---|---|---|");
        foreach (var row in r.Rows)
            md.AppendLine($"| {row.Creature} | {row.Two.Result} | {row.Act.Result} | {row.Blame.Result} |");
        md.AppendLine();
        if (r.FlagsOn.Count > 0)
        {
            md.AppendLine($"Creature flags on for the run (note 1051): {string.Join(", ", r.FlagsOn)}.");
            md.AppendLine();
        }
        if (r.Flagged.Count > 0)
        {
            md.AppendLine("## With the flags (note 1051)");
            md.AppendLine();
            md.AppendLine("Each creature flag that's off for the run, on, in the staged grab (its nights are the run's above).");
            md.AppendLine();
            md.AppendLine("| Flag | Creature | 2: better by two | 3: 8–20 s to act |");
            md.AppendLine("|---|---|---|---|");
            foreach (var f in r.Flagged)
                md.AppendLine($"| {f.Flag} | {f.Creature} | {f.Two.Result} | {f.Act.Result} |");
            md.AppendLine();
            foreach (var f in r.Flagged)
                md.AppendLine($"- **{f.Flag}**: 2, {f.Two.Why}. 3, {f.Act.Why}.");
            md.AppendLine();
        }
        md.AppendLine("## Why");
        md.AppendLine();
        foreach (var row in r.Rows)
            md.AppendLine($"- **{row.Creature}**: 2, {row.Two.Why}. 3, {row.Act.Why}. 5, {row.Blame.Why}.");
        md.AppendLine();
        md.AppendLine("## The crew's numbers");
        md.AppendLine();
        md.AppendLine("```json");
        md.AppendLine(JsonSerializer.Serialize(r.Crew, DataFile.Options));
        md.AppendLine("```");
        return md.ToString();
    }

    static string Str(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    static double Opt(string[] args, string name, double fallback) =>
        Str(args, name, "") is { Length: > 0 } v ? double.Parse(v, CultureInfo.InvariantCulture) : fallback;
}
