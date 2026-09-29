using System.Text;

namespace DarkTerritory.Game;

/// <summary>
/// Crash reports (roadmap M6): what the game printed lately, and the exception that stopped it, written to a text
/// file in the user's app data where a player can find it and send it. The console is teed through a ring of recent
/// lines so the report carries the story, not just the stack.
/// </summary>
public sealed class CrashReports
{
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DarkTerritory", "crashes");

    readonly Queue<string> _recent = new();
    readonly int _keep;
    readonly object _lock = new();

    public CrashReports(string directory, int keepLines = 200)
    {
        Directory = directory;
        _keep = keepLines;
    }

    public string Directory { get; }

    /// <summary>Tees the console through the report's memory, and writes a report if the process dies of an exception.</summary>
    public static CrashReports Install(string? directory = null)
    {
        var reports = new CrashReports(directory ?? DefaultDirectory);
        Console.SetOut(new Tee(Console.Out, reports));
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex && reports.Write(ex) is { } path)
                Console.Error.WriteLine($"Dark Territory stopped: {ex.Message}. A report is at {path}");
        };
        return reports;
    }

    public void Remember(string line)
    {
        lock (_lock)
        {
            _recent.Enqueue(line);
            while (_recent.Count > _keep)
                _recent.Dequeue();
        }
    }

    public IReadOnlyList<string> Recent
    {
        get
        {
            lock (_lock)
                return [.. _recent];
        }
    }

    /// <summary>Writes a report for <paramref name="ex"/>; returns its path, or null if it couldn't be written.</summary>
    public string? Write(Exception ex, DateTime? at = null)
    {
        var when = at ?? DateTime.Now;
        var text = new StringBuilder()
            .AppendLine($"Dark Territory crash report, {when:yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"version {typeof(CrashReports).Assembly.GetName().Version}, .NET {Environment.Version}, {System.Runtime.InteropServices.RuntimeInformation.OSDescription} ({System.Runtime.InteropServices.RuntimeInformation.OSArchitecture})")
            .AppendLine()
            .AppendLine(ex.ToString())
            .AppendLine()
            .AppendLine("The last things it said:");
        foreach (var line in Recent)
            text.AppendLine("  " + line);
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = Path.Combine(Directory, $"crash-{when:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, text.ToString());
            return path;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Writes through to the real console and keeps each finished line.</summary>
    sealed class Tee(TextWriter inner, CrashReports reports) : TextWriter
    {
        readonly StringBuilder _line = new();
        public override Encoding Encoding => inner.Encoding;

        public override void Write(char value)
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
