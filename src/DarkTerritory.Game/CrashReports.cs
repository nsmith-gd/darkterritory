using System.Text;

namespace DarkTerritory.Game;

/// <summary>
/// Crash reports (roadmap M6), and a player's own reports of a problem (note 452): written to the user's app data where a
/// player can find them and send them to the studio (<see cref="ReportsTuning"/>), in the form a developer reads
/// (<see cref="Report"/>): the build, the machine, the settings, what the game was doing, the exception and the lines it
/// printed last. The console is teed through a ring of recent lines, so a report carries the story and not just the stack,
/// and (note 452) into a log of each launch (<c>logs/latest.log</c>, the last few kept).
/// </summary>
public sealed class CrashReports
{
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DarkTerritory", "crashes");

    /// <summary>The launches' logs, beside the reports' folder (<c>DarkTerritory/logs</c>).</summary>
    public static string LogsBeside(string directory) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(directory)) ?? directory, "logs");

    /// <summary>This launch's log, and the earlier ones renamed by when they started.</summary>
    public const string LatestLog = "latest.log";

    readonly Queue<(DateTime At, string Line)> _recent = new();
    readonly int _keep;
    readonly object _lock = new();
    readonly List<ReportField> _context = [];
    readonly DateTime _started = DateTime.Now;
    StreamWriter? _log;

    public CrashReports(string directory, int keepLines = 200)
    {
        Directory = directory;
        _keep = keepLines;
    }

    public string Directory { get; }

    /// <summary>This launch's log file, while there is one (<see cref="StartLog"/>).</summary>
    public string? LogPath { get; private set; }

    /// <summary>
    /// What's going on when a report's written, asked then (the night's route, how far in, who's aboard): the app's, and
    /// read inside a catch, so a report's still written if the game's state is what broke.
    /// </summary>
    public Func<IEnumerable<ReportField>>? Live { get; set; }

    /// <summary>Tees the console through the report's memory and this launch's log, and writes a report if the process dies of an exception.</summary>
    public static CrashReports Install(string? directory = null, int keepLogs = 5)
    {
        var reports = new CrashReports(directory ?? DefaultDirectory);
        reports.StartLog(LogsBeside(reports.Directory), keepLogs);
        Console.SetOut(new Tee(Console.Out, reports));
        Console.SetError(new Tee(Console.Error, reports));
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex && reports.Write(ex) is { } path)
                Console.Error.WriteLine($"Dark Territory stopped: {ex.Message}. A report is at {path}");
        };
        return reports;
    }

    /// <summary>
    /// Starts this launch's log in <paramref name="logs"/>: the last launch's <see cref="LatestLog"/> kept under the time it
    /// started (log-yyyyMMdd-HHmmss.txt), only the newest <paramref name="keep"/> of those kept. A folder that can't be
    /// written means no log, as before.
    /// </summary>
    public void StartLog(string logs, int keep = 5)
    {
        try
        {
            System.IO.Directory.CreateDirectory(logs);
            var latest = Path.Combine(logs, LatestLog);
            if (File.Exists(latest))
            {
                var was = Path.Combine(logs, $"log-{File.GetCreationTime(latest):yyyyMMdd-HHmmss}.txt");
                File.Move(latest, was, overwrite: true);
            }
            foreach (var old in System.IO.Directory.GetFiles(logs, "log-*.txt").Order(StringComparer.Ordinal).SkipLast(Math.Max(0, keep)))
                File.Delete(old);
            _log = new StreamWriter(latest, append: false) { AutoFlush = true };
            File.SetCreationTime(latest, DateTime.Now);
            LogPath = latest;
            Context("log", latest);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log = null;
        }
    }

    /// <summary>Closes this launch's log (the process ending does it otherwise).</summary>
    public void StopLog()
    {
        lock (_lock)
        {
            _log?.Dispose();
            _log = null;
        }
    }

    /// <summary>A line for every report's header from now (<paramref name="value"/> null takes it off): "gpu", "settings", "doing".</summary>
    public void Context(string name, string? value)
    {
        lock (_lock)
        {
            int at = _context.FindIndex(f => f.Name == name);
            if (value is null)
            {
                if (at >= 0)
                    _context.RemoveAt(at);
            }
            else if (at >= 0)
                _context[at] = new(name, value);
            else
                _context.Add(new(name, value));
        }
    }

    public void Remember(string line)
    {
        lock (_lock)
        {
            var now = DateTime.Now;
            _recent.Enqueue((now, line));
            while (_recent.Count > _keep)
                _recent.Dequeue();
            try
            {
                _log?.WriteLine(Stamped(now, line));
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
                _log = null;
            }
        }
    }

    public IReadOnlyList<string> Recent
    {
        get
        {
            lock (_lock)
                return [.. _recent.Select(r => r.Line)];
        }
    }

    /// <summary>The recent lines as the log has them, each with the time it was printed.</summary>
    public IReadOnlyList<string> RecentStamped
    {
        get
        {
            lock (_lock)
                return [.. _recent.Select(r => Stamped(r.At, r.Line))];
        }
    }

    static string Stamped(DateTime at, string line) => $"[{at:HH:mm:ss.fff}] {line}";

    /// <summary>The report as of now (<paramref name="ex"/> null: a player's problem, with what they said).</summary>
    public Report Take(Exception? ex, DateTime when, string? note = null)
    {
        var (version, commit) = Report.Build();
        var fields = new List<ReportField>
        {
            new("os", $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription} ({System.Runtime.InteropServices.RuntimeInformation.OSArchitecture})"),
            new("runtime", $".NET {Environment.Version}"),
        };
        lock (_lock)
            fields.AddRange(_context);
        try
        {
            if (Live?.Invoke() is { } live)
                fields.AddRange(live);
        }
        catch (Exception e)
        {
            fields.Add(new("live", $"couldn't be read: {e.GetType().Name}: {e.Message}"));
        }
        var up = when > _started ? when - _started : TimeSpan.Zero;
        fields.Add(new("uptime", $"{(int)up.TotalHours:00}:{up.Minutes:00}:{up.Seconds:00}"));
        string kind = ex is null ? Report.Problem : Report.Crash;
        return new Report($"{kind}-{when:yyyyMMdd-HHmmss}", kind, when, version, commit, fields, ex?.GetType().FullName, ex?.Message, ex?.ToString(), RecentStamped, note);
    }

    /// <summary>Writes a report for <paramref name="ex"/>, as text and its JSON twin; returns the text's path, or null if it couldn't be written.</summary>
    public string? Write(Exception ex, DateTime? at = null) => Save(Take(ex, at ?? DateTime.Now));

    /// <summary>
    /// A player's report of a problem (note 452; REPORT A PROBLEM): what's going on and what the game printed lately, as a
    /// crash's is, without an exception. Returns the text's path, or null.
    /// </summary>
    public string? WriteProblem(string? note = null, DateTime? at = null) => Save(Take(null, at ?? DateTime.Now, note));

    string? Save(Report r)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = Path.Combine(Directory, r.Id + ".txt");
            File.WriteAllText(path, r.ToText());
            File.WriteAllText(Path.ChangeExtension(path, ".json"), r.ToJson());
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The reports written since the player last put the notice away (note 411): a crash says so on the console, which a
    /// player launched from Steam never sees, so the next launch's title says it instead. Null when there's none new, or
    /// the folder can't be read.
    /// </summary>
    public static CrashNotice? Unseen(string directory)
    {
        try
        {
            if (!System.IO.Directory.Exists(directory))
                return null;
            // The names carry their time (crash-yyyyMMdd-HHmmss), so ordinal order is the order they were written in.
            string seen = File.Exists(Path.Combine(directory, SeenFile)) ? File.ReadAllText(Path.Combine(directory, SeenFile)).Trim() : "";
            var fresh = System.IO.Directory.GetFiles(directory, "crash-*.txt").Select(Path.GetFileName)
                .Where(n => string.CompareOrdinal(n, seen) > 0).Order(StringComparer.Ordinal).ToList();
            return fresh.Count == 0 ? null : new CrashNotice(directory, Path.Combine(directory, fresh[^1]!), fresh.Count);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The notice put away (note 411): the reports up to <paramref name="notice"/>'s newest aren't said again.</summary>
    public static void MarkSeen(CrashNotice notice)
    {
        try
        {
            File.WriteAllText(Path.Combine(notice.Directory, SeenFile), Path.GetFileName(notice.Newest) + "\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Said again next time: no worse than before.
        }
    }

    /// <summary>The newest report the title's notice was put away at.</summary>
    public const string SeenFile = "seen.txt";

    /// <summary>Writes through to the real console and keeps each finished line.</summary>
    sealed class Tee(TextWriter inner, CrashReports reports) : TextWriter
    {
        readonly StringBuilder _line = new();
        public override Encoding Encoding => inner.Encoding;

        public override void Write(char value)
        {
            // The net's threads print too: one line at a time.
            lock (_line)
            {
                inner.Write(value);
                if (value == '\n')
                {
                    reports.Remember(_line.ToString().TrimEnd('\r'));
                    _line.Clear();
                }
                else
                    _line.Append(value);
            }
        }

        public override void Write(string? value)
        {
            if (value is null)
                return;
            foreach (char c in value)
                Write(c);
        }

        public override void WriteLine(string? value)
        {
            Write(value);
            Write('\n');
        }

        public override void Flush() => inner.Flush();
    }
}

/// <summary>Crash reports the player hasn't been told of (note 411): where they are, the newest, and how many.</summary>
public sealed record CrashNotice(string Directory, string Newest, int Count);
