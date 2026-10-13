using System.Globalization;
using System.Text;
using System.Text.Json;
using Ballast;
using Ballast.Dev;
using Ballast.Render;

/// <summary>
/// `dt review` (ARCHITECTURE §8 note 517): a pull request's review packet, what it changed for the director to look at.
/// <list type="bullet">
/// <item><c>dt review diff &lt;before&gt; &lt;after&gt; [--out out/review] [--tolerance 8] [--title t]</c>: every PNG in both folders compared
/// (by path), the changed ones drawn as strips (before, after, the change in magenta), and what changed listed in
/// <c>review.json</c> and <c>review.md</c> (the CI summary), most changed first. Shots only in one folder are listed as new or
/// gone. JSON files in both (numbers.json) are compared field by field.</item>
/// </list>
/// </summary>
static class ReviewCommands
{
    public static int Run(string[] args) => args switch
    {
        ["review", "diff", var before, var after, ..] => Diff(before, after, Str(args, "--out", "out/review"), (int)Opt(args, "--tolerance", 8), Str(args, "--title", ""),
            Volatile(Str(args, "--volatile", "tools/review/volatile.txt"))),
        _ => Usage(),
    };

    static int Usage()
    {
        Console.Error.WriteLine("usage: dt review diff <before dir> <after dir> [--out dir] [--tolerance 8] [--title text]");
        return 2;
    }

    sealed record Shot(string Path, string Verdict, double ChangedPercent, double Mean, int Max, int[] Box, string? Strip);

    /// <summary>The shots that change on their own (tools/review/volatile.txt: a path, then why), listed apart, never as changed.</summary>
    static Dictionary<string, string> Volatile(string file) => !File.Exists(file) ? []
        : File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)).ToDictionary(p => p[0], p => p.Length > 1 ? p[1].Trim() : "", StringComparer.Ordinal);

    static int Diff(string before, string after, string outDir, int tolerance, string title, Dictionary<string, string> volatileShots)
    {
        static Dictionary<string, string> Pngs(string dir) => !Directory.Exists(dir) ? []
            : Directory.EnumerateFiles(dir, "*.png", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(dir, f).Replace('\\', '/'), f => f);
        var a = Pngs(before);
        var b = Pngs(after);
        string strips = Path.Combine(outDir, "strips");
        if (Directory.Exists(outDir))
            Directory.Delete(outDir, recursive: true);
        Directory.CreateDirectory(strips);
        var shots = new List<Shot>();
        foreach (var path in a.Keys.Intersect(b.Keys).Order(StringComparer.Ordinal))
        {
            var x = ImageFile.Load(a[path]);
            var y = ImageFile.Load(b[path]);
            var r = ImageDiff.Compare(x, y, tolerance);
            string? strip = null;
            string verdict = volatileShots.ContainsKey(path) && r.Verdict != "same" ? "volatile" : r.Verdict;
            if (verdict == "changed")
            {
                strip = Path.Combine("strips", path.Replace('/', '_'));
                var s = ImageDiff.Strip(x, y, r, tolerance);
                PngWriter.Write(Path.Combine(outDir, strip), s.Rgba, s.Width, s.Height, 1);
            }
            shots.Add(new Shot(path, verdict, Math.Round(r.Changed * 100, 3), Math.Round(r.Mean, 2), r.Max, r.Box, strip));
        }
        var added = b.Keys.Except(a.Keys).Order(StringComparer.Ordinal).ToList();
        var gone = a.Keys.Except(b.Keys).Order(StringComparer.Ordinal).ToList();
        var numbers = Numbers(Path.Combine(before, "numbers.json"), Path.Combine(after, "numbers.json"));
        var changed = shots.Where(s => s.Verdict == "changed").OrderByDescending(s => s.ChangedPercent).ToList();
        var report = new
        {
            title,
            before = Path.GetFullPath(before),
            after = Path.GetFullPath(after),
            tolerance,
            compared = shots.Count,
            changed = changed.Count,
            noise = shots.Count(s => s.Verdict == "noise"),
            @volatile = shots.Where(s => s.Verdict == "volatile").Select(s => new { s.Path, why = volatileShots[s.Path] }),
            added,
            gone,
            shots = changed.Concat(shots.Where(s => s.Verdict != "changed")),
            numbers,
        };
        File.WriteAllText(Path.Combine(outDir, "review.json"), JsonSerializer.Serialize(report, DataFile.Options));
        File.WriteAllText(Path.Combine(outDir, "review.md"), Markdown(title, shots, changed, added, gone, numbers, volatileShots));
        Console.WriteLine(JsonSerializer.Serialize(new { outDir = Path.GetFullPath(outDir), report.compared, report.changed, report.noise, added, gone, numberChanges = numbers.Count }, DataFile.Options));
        return 0;
    }

    /// <summary>Two JSON files' numbers, path by path: each that differs, with its before and after.</summary>
    static List<object> Numbers(string before, string after)
    {
        var changes = new List<object>();
        if (!File.Exists(before) || !File.Exists(after))
            return changes;
        var x = Flatten(JsonDocument.Parse(File.ReadAllText(before)).RootElement);
        var y = Flatten(JsonDocument.Parse(File.ReadAllText(after)).RootElement);
        foreach (var key in x.Keys.Union(y.Keys).Order(StringComparer.Ordinal))
        {
            x.TryGetValue(key, out var p);
            y.TryGetValue(key, out var q);
            if (p != q)
                changes.Add(new { key, before = p, after = q });
        }
        return changes;
    }

    static readonly string[] Names = ["piece", "name", "id", "cars", "file", "path"];

    static Dictionary<string, string> Flatten(JsonElement e, string at = "", Dictionary<string, string>? into = null)
    {
        into ??= new(StringComparer.Ordinal);
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                    Flatten(p.Value, at.Length == 0 ? p.Name : $"{at}.{p.Name}", into);
                break;
            case JsonValueKind.Array:
                // A list of named things (the art's pieces, the train table's rows) by name, so one added doesn't shift the rest.
                int i = 0;
                foreach (var v in e.EnumerateArray())
                {
                    string label = Names.Select(n => v.ValueKind == JsonValueKind.Object && v.TryGetProperty(n, out var nv) ? nv.ToString() : null)
                        .FirstOrDefault(l => l is not null) ?? i.ToString(CultureInfo.InvariantCulture);
                    Flatten(v, $"{at}[{label}]", into);
                    i++;
                }
                break;
            default:
                into[at] = e.ToString();
                break;
        }
        return into;
    }

    static string Markdown(string title, List<Shot> shots, List<Shot> changed, List<string> added, List<string> gone, List<object> numbers,
        Dictionary<string, string> volatileShots)
    {
        var md = new StringBuilder();
        md.AppendLine($"## Review packet{(title.Length > 0 ? $": {title}" : "")}");
        md.AppendLine();
        md.AppendLine(changed.Count == 0 && added.Count == 0 && gone.Count == 0
            ? $"Nothing looks different: {shots.Count} shots compared, none changed."
            : $"**{changed.Count} of {shots.Count} shots changed**{(added.Count > 0 ? $", {added.Count} new" : "")}{(gone.Count > 0 ? $", {gone.Count} gone" : "")}. The strips (before, after, the change in magenta) are in the run's `review-packet` artifact.");
        if (changed.Count > 0)
        {
            md.AppendLine();
            md.AppendLine("| Shot | Changed | Mean | Max | Where (x, y, w, h) |");
            md.AppendLine("|---|---:|---:|---:|---|");
            foreach (var s in changed)
                md.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| `{s.Path}` | {s.ChangedPercent:0.###} % | {s.Mean:0.##} | {s.Max} | {string.Join(", ", s.Box)} |"));
        }
        if (shots.Where(s => s.Verdict == "volatile").ToList() is { Count: > 0 } wobbly)
            md.AppendLine($"\nChanged, but they change on their own every run: {string.Join("; ", wobbly.Select(s => $"`{s.Path}` ({volatileShots[s.Path]})"))}.");
        foreach (var (name, list) in new[] { ("New", added), ("Gone", gone) })
            if (list.Count > 0)
                md.AppendLine($"\n{name}: {string.Join(", ", list.Select(p => $"`{p}`"))}");
        if (numbers.Count > 0)
        {
            md.AppendLine();
            md.AppendLine("| Number | Before | After |");
            md.AppendLine("|---|---:|---:|");
            foreach (var n in numbers.Take(60))
            {
                var j = JsonSerializer.SerializeToElement(n);
                md.AppendLine($"| `{j.GetProperty("key")}` | {j.GetProperty("before")} | {j.GetProperty("after")} |");
            }
        }
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
