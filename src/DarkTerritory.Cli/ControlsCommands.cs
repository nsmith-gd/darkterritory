using System.Text.Json;
using Ballast;
using DarkTerritory.Game;

/// <summary>
/// `dt controls [--write | --check]` (note 898): the controls map from <see cref="Controls"/>. Bare, it prints the map as
/// JSON; <c>--write</c> rewrites README.md's table and tools/package/PLAYING.txt's keys; <c>--check</c> exits 1 if either
/// says a key the defaults don't (ControlsMapTests holds the same on every PR).
/// </summary>
static class ControlsCommands
{
    public static int Run(string content, string[] args)
    {
        string root = Path.GetDirectoryName(Path.GetFullPath(content))!;
        var files = new (string Path, Func<string, string?> Splice)[]
        {
            (Path.Combine(root, ControlsMap.Readme), ControlsMap.SpliceReadme),
            (Path.Combine(root, ControlsMap.Playing), ControlsMap.SplicePlaying),
        };
        if (args.Contains("--write") || args.Contains("--check"))
        {
            var stale = new List<string>();
            foreach (var (path, splice) in files)
            {
                string now = File.ReadAllText(path);
                string? then = splice(now);
                if (then is null)
                {
                    Console.Error.WriteLine($"{path}: the controls map's markers aren't there");
                    return 1;
                }
                if (then == now)
                    continue;
                stale.Add(path);
                if (args.Contains("--write"))
                    File.WriteAllText(path, then);
            }
            Console.WriteLine(JsonSerializer.Serialize(new { stale, written = args.Contains("--write") }, DataFile.Options));
            return args.Contains("--check") && stale.Count > 0 ? 1 : 0;
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            controls = Enum.GetValues<Control>().Select(c => new { control = c.ToString(), key = Controls.Defaults[c], label = Controls.Label(c), does = Controls.Help(c) }),
            @fixed = Controls.Fixed.Select(f => new { key = f.Key, does = f.Does }),
        }, DataFile.Options));
        return 0;
    }
}
