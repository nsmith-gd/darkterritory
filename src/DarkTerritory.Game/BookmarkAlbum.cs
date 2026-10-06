using System.Text;
using Ballast.Render;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game;

/// <summary>
/// The night's stills kept past the run-end screen (GDD v1.4 App. D.12, D.13; ARCHITECTURE note 203): each bookmark on the
/// report that this machine has a still of is written to the player's disk as a PNG, in a folder of its own for the night
/// (when it began, and the line), named by its time in the run, what it is and whom it's of, with the report's lines
/// beside them in <c>night.txt</c>. Presentation only, like the stills themselves: the sim never sees a file.
/// </summary>
public sealed class BookmarkAlbum
{
    /// <summary>Beside the crash reports and saves, in the user's app data.</summary>
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DarkTerritory", "bookmarks");

    readonly Dictionary<int, (Still Still, string File)> _written = [];
    bool _failed;

    /// <param name="directory">Where the nights go.</param>
    /// <param name="began">When the night began, by the player's clock: its folder's name.</param>
    /// <param name="line">The night's line ("frontier-7"), the rest of its name.</param>
    public BookmarkAlbum(string directory, DateTime began, string line)
    {
        Night = Path.Combine(directory, NightName(began, line));
    }

    /// <summary>This night's folder.</summary>
    public string Night { get; }

    /// <summary>How many of the night's stills are on disk.</summary>
    public int Kept => _written.Count;

    /// <summary>"2026-10-05 2130 frontier-7": sorts by date, says which line.</summary>
    public static string NightName(DateTime began, string line) => $"{began:yyyy-MM-dd HHmm} {Safe(line)}".TrimEnd();

    /// <summary>
    /// "03 18m24s grab priya.png": the bookmark's number (the order they were made, so the folder lists in the night's
    /// order), when in the run, what kind, and whom it's of (the dead's own: whom they were following).
    /// </summary>
    public static string FileName(Bookmark b, World world)
    {
        string kind = b.Kind switch
        {
            BookmarkKind.Grab => "grab",
            BookmarkKind.Punish => "punish",
            BookmarkKind.Derail => "derailment",
            BookmarkKind.Stranded => "stranded",
            _ => "bookmark",
        };
        string who = b.Kind == BookmarkKind.Manual ? b.Name : b.Victim >= 0 ? IncidentLog.NameOf(world, b.Victim) : "";
        return $"{b.Id:00} {Stamp(b.Seconds)} {kind} {Safe(who)}".TrimEnd() + ".png";
    }

    /// <summary>Run seconds for a file name: "18m24s", "1h04m12s" (no colons: Windows won't have them).</summary>
    public static string Stamp(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, Math.Floor(seconds)));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h{t.Minutes:00}m{t.Seconds:00}s" : $"{t.Minutes}m{t.Seconds:00}s";
    }

    /// <summary>Lower case, and nothing a file system minds: letters, digits, spaces and hyphens.</summary>
    static string Safe(string s)
    {
        var b = new StringBuilder(s.Length);
        foreach (char c in s.Trim().ToLowerInvariant())
            b.Append(char.IsAsciiLetterOrDigit(c) || c == ' ' ? c : '-');
        return b.ToString();
    }

    /// <summary>
    /// Writes each of the report's bookmarks that has a still and isn't on disk as it is now (a derailment's first-person
    /// stand-in is rewritten when the film's peak replaces it), and <c>night.txt</c> whenever one is. Returns the files
    /// written. Never throws for the disk: a full or read-only one is said once and the night goes on.
    /// </summary>
    public List<string> Save(RunReport report, IReadOnlyDictionary<int, Still> stills, World world)
    {
        var written = new List<string>();
        if (_failed)
            return written;
        try
        {
            foreach (var b in report.Bookmarks)
            {
                if (!stills.TryGetValue(b.Id, out var still) || _written.TryGetValue(b.Id, out var had) && ReferenceEquals(had.Still, still))
                    continue;
                string path = Path.Combine(Night, FileName(b, world));
                PngWriter.Write(path, still.Rgba, still.Width, still.Height);
                _written[b.Id] = (still, Path.GetFileName(path));
                written.Add(path);
            }
            if (written.Count > 0)
                File.WriteAllText(Path.Combine(Night, "night.txt"), Index(report, world));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _failed = true;
            Console.WriteLine($"bookmarks: couldn't keep the night's stills in {Night} ({e.Message})");
        }
        return written;
    }

    /// <summary>The report's lines, each with the stills beside it, then the dead's own: what the pictures are of.</summary>
    string Index(RunReport report, World world)
    {
        var text = new StringBuilder();
        text.AppendLine($"DARK TERRITORY. {Path.GetFileName(Night)}. {report.End}, {Hud.Clock(report.Seconds)}.");
        text.AppendLine();
        foreach (var l in report.Lines)
        {
            string when = l.Seconds >= 0 ? Hud.Clock(l.Seconds) + "  " : "";
            text.AppendLine($"{when}{(l.Who.Length > 0 ? l.Who.ToUpperInvariant() + ": " : "")}{l.Text}");
            foreach (int id in l.Marks)
                if (_written.TryGetValue(id, out var w))
                    text.AppendLine($"    [{w.File}]");
        }
        var manual = report.Bookmarks.Where(b => b.Kind == BookmarkKind.Manual && _written.ContainsKey(b.Id)).ToList();
        if (manual.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("BOOKMARKS");
            foreach (var b in manual)
                text.AppendLine($"{Hud.Clock(b.Seconds)}  {IncidentLog.NameOf(world, b.Taker)}, following {b.Name}: [{_written[b.Id].File}]");
        }
        return text.ToString();
    }
}
