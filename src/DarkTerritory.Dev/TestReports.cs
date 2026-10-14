using System.Text;
using DarkTerritory.Game;

namespace DarkTerritory.Dev;

/// <summary>
/// The director's test reports (GDD §34.2, ARCHITECTURE §8 note 677; the director, 13 Oct: "all bundled together into a single
/// .txt file ... so I don't have to manually drag and drop each of them"): every report a developer build writes (a note typed
/// with F8 or spoken with F9, REPORT A PROBLEM, a crash) goes on the end of one text file, each as its report reads, under a
/// numbered rule. A session's notes are sent as that one file. Each night's performance goes on it too (GDD §34.3, note 691).
/// </summary>
public sealed class TestReports(string path)
{
    public const string FileName = "test-reports.txt";

    /// <summary>What starts each report in the file: "===== TEST REPORT 3 · ...".</summary>
    public const string Rule = "===== TEST REPORT ";

    readonly object _lock = new();

    /// <summary>The file.</summary>
    public string Path => path;

    /// <summary>
    /// The game's own folder's <c>logs</c> (beside the executable, or <paramref name="gameFolder"/>), made if it isn't there;
    /// null when it can't be written (a game installed where its user can't write), and the app data's is used as before.
    /// </summary>
    public static string? GameLogs(string? gameFolder = null)
    {
        string logs = System.IO.Path.Combine(gameFolder ?? AppContext.BaseDirectory, "logs");
        try
        {
            Directory.CreateDirectory(logs);
            // A folder can exist and still refuse a file (Program Files): try one.
            string probe = System.IO.Path.Combine(logs, $".write-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return logs;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>How many reports the file holds.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
                return Counted();
        }
    }

    int Counted()
    {
        try
        {
            return File.Exists(path) ? File.ReadLines(path).Count(l => l.StartsWith(Rule, StringComparison.Ordinal)) : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>
    /// <paramref name="report"/> on the end of the file, under its rule: its number, its time and <paramref name="what"/> it is
    /// ("a typed note (F8)"), and the folder that holds the rest of it (a note's frame and voice) when there is one;
    /// <paramref name="more"/> after it (a note's frames, note 691). Returns its number, or 0 if the file couldn't be written:
    /// the report's own file is still there.
    /// </summary>
    public int Add(Report report, string what, string? folder = null, string? more = null) =>
        Append(report.Time, what, folder, more is null ? report.ToText() : report.ToText().TrimEnd() + Environment.NewLine + Environment.NewLine + more);

    /// <summary>A report that isn't a crash report's (the night's performance, GDD §34.3, note 691), on the end of the file.</summary>
    public int Add(DateTime time, string what, string text) => Append(time, what, null, text);

    int Append(DateTime time, string what, string? folder, string text)
    {
        lock (_lock)
        {
            try
            {
                int n = Counted() + 1;
                var t = new StringBuilder();
                if (n == 1)
                    t.AppendLine("DARK TERRITORY TEST REPORTS").AppendLine("Every note and report this build wrote, oldest first (GDD §34.2). Send this file as it is.").AppendLine();
                t.AppendLine($"{Rule}{n} · {time:yyyy-MM-dd HH:mm:ss} · {what} =====");
                if (folder is not null)
                    t.AppendLine($"folder    {folder}");
                t.AppendLine(text.TrimEnd()).AppendLine();
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
                File.AppendAllText(path, t.ToString());
                return n;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return 0;
            }
        }
    }

    /// <summary>What a report written by <see cref="CrashReports"/> is called in the file.</summary>
    public static string Called(Report report) => report.Kind == Report.Crash ? "a crash" : "a problem reported (REPORT A PROBLEM)";
}
