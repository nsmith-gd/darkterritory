using Ballast.Render;
using DarkTerritory.Game;

namespace DarkTerritory.Dev.Feedback;

/// <summary>
/// A note's folder (note 516): <c>note.json</c>, <c>frame.png</c>, <c>voice.wav</c> when it was spoken, and the report
/// (<c>report.txt</c>, <c>report.json</c>: the build, the machine, the settings, what the game was doing and its last lines,
/// as a crash report has them; note 452). The report goes on the end of the build's test reports too (<see cref="TestReports"/>,
/// note 677), with the last minute's frames (<c>frames.txt</c>, GDD §34.3, note 691) when the night's being timed.
/// </summary>
public static class FeedbackBundle
{
    public const string NoteFile = "note.json", FrameFile = "frame.png", VoiceFile = "voice.wav", ReportFile = "report.txt", FramesFile = "frames.txt";

    /// <summary>Where a developer build keeps its notes: the user's app data, beside the recordings.</summary>
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DarkTerritory", "feedback");

    /// <summary>Writes the bundle and returns its folder.</summary>
    public static string Write(string directory, FeedbackNote note, (byte[] Rgba, int Width, int Height)? frame, float[]? voice, int sampleRate,
        CrashReports? reports, TestReports? tests = null, string? frames = null)
    {
        string dir = Path.Combine(directory, note.Id);
        Directory.CreateDirectory(dir);
        if (frame is { } f)
            PngWriter.Write(Path.Combine(dir, FrameFile), f.Rgba, f.Width, f.Height, 1);
        if (voice is { Length: > 0 })
            Wav.Write(Path.Combine(dir, VoiceFile), voice, sampleRate);
        if (frames is not null)
            File.WriteAllText(Path.Combine(dir, FramesFile), frames);
        if (reports is not null)
        {
            var report = reports.Take(null, note.At.ToLocalTime(), note.Text ?? "(a spoken note)");
            File.WriteAllText(Path.Combine(dir, ReportFile), report.ToText());
            File.WriteAllText(Path.Combine(dir, "report.json"), report.ToJson());
            tests?.Add(report, note.Kind == "voice" ? $"a spoken note (F9), {note.VoiceSeconds?.ToString(System.Globalization.CultureInfo.InvariantCulture)} s: {VoiceFile}" : "a typed note (F8)", Path.GetFullPath(dir), frames);
        }
        // The note last: a folder with its note.json is whole.
        File.WriteAllText(Path.Combine(dir, NoteFile), note.ToJson());
        return dir;
    }

    /// <summary>The notes in a folder of bundles, newest first.</summary>
    public static IReadOnlyList<(string Dir, FeedbackNote Note)> List(string directory) =>
        !Directory.Exists(directory) ? []
        : [.. Directory.EnumerateDirectories(directory).Where(d => File.Exists(Path.Combine(d, NoteFile)))
            .Select(d => (d, FeedbackNote.FromJson(File.ReadAllText(Path.Combine(d, NoteFile))))).OrderByDescending(n => n.Item2.At)];
}

/// <summary>16-bit PCM mono WAV, for a spoken note.</summary>
public static class Wav
{
    public static void Write(string path, ReadOnlySpan<float> samples, int sampleRate)
    {
        using var w = new BinaryWriter(File.Create(path));
        int bytes = samples.Length * 2;
        w.Write("RIFF"u8);
        w.Write(36 + bytes);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(sampleRate);
        w.Write(sampleRate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(bytes);
        foreach (float s in samples)
            w.Write((short)Math.Round(Math.Clamp(s, -1f, 1f) * short.MaxValue));
    }
}
