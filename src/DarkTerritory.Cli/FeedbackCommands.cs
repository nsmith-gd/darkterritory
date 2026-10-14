using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Ballast;
using DarkTerritory.Dev.Feedback;
using DarkTerritory.Dev.Replay;
using DarkTerritory.Game;
using DarkTerritory.Sim;

/// <summary>
/// `dt feedback` (ARCHITECTURE §8 note 516): the director's notes from inside the game.
/// <list type="bullet">
/// <item><c>dt feedback pull [--into out/feedback]</c>: the repository's <c>feedback</c> branch (what the director's dev build sent)
/// fetched and unpacked: <c>notes/&lt;id&gt;/</c> and <c>recordings/</c>. Then as <c>list</c>.</item>
/// <item><c>dt feedback list [--dir d]</c>: the notes, newest first: what was said, where, the tags.</item>
/// <item><c>dt feedback show &lt;id&gt; [--dir d]</c>: one note, and its moment: the night's recording played to the note's tick and
/// drawn through the director's eyes (<c>out/shots/&lt;id&gt;-replay.png</c>) beside the frame they saw.</item>
/// <item><c>dt feedback make --text "…" [--route r] [--bots n] [--at s] [--seconds s] [--dir d]</c>: a note made headless in a
/// recorded bot night, as F8 makes one (the frame drawn from the host player's eyes): the whole path, without a window.</item>
/// </list>
/// </summary>
static class FeedbackCommands
{
    public static int Run(string content, string[] args) => args switch
    {
        ["feedback", "pull", ..] => Pull(Str(args, "--into", "out/feedback")),
        ["feedback", "list", ..] => List(Str(args, "--dir", DefaultDir())),
        ["feedback", "show", var id, ..] => Show(content, Str(args, "--dir", DefaultDir()), id),
        ["feedback", "make", ..] => Make(content, args),
        _ => Usage(),
    };

    /// <summary>The pulled notes when there are some here, else this machine's own (a dev build's).</summary>
    static string DefaultDir() => Directory.Exists("out/feedback/notes") ? "out/feedback/notes" : FeedbackBundle.DefaultDirectory;

    static int Usage()
    {
        Console.Error.WriteLine("usage: dt feedback pull [--into dir] | list [--dir d] | show <id> [--dir d] | make --text \"…\" [--route r] [--bots n] [--at s]");
        return 2;
    }

    static int Pull(string into)
    {
        if (!Git("fetch", "--quiet", "origin", "feedback"))
        {
            Console.Error.WriteLine("no feedback branch to fetch (no notes sent yet, or no access)");
            return 1;
        }
        Directory.CreateDirectory(into);
        // The branch's files, unpacked as they are (it's its own history: notes/ and recordings/).
        var archive = Process.Start(new ProcessStartInfo("git", ["archive", "--format=tar", "FETCH_HEAD"]) { RedirectStandardOutput = true })!;
        var tar = Process.Start(new ProcessStartInfo("tar", ["-x", "-C", into]) { RedirectStandardInput = true })!;
        archive.StandardOutput.BaseStream.CopyTo(tar.StandardInput.BaseStream);
        tar.StandardInput.Close();
        archive.WaitForExit();
        tar.WaitForExit();
        if (archive.ExitCode != 0 || tar.ExitCode != 0)
            return 1;
        return List(Path.Combine(into, "notes"));
    }

    static bool Git(params string[] args)
    {
        using var git = Process.Start(new ProcessStartInfo("git", args) { RedirectStandardError = true })!;
        git.WaitForExit();
        return git.ExitCode == 0;
    }

    static int List(string dir)
    {
        var notes = FeedbackBundle.List(dir);
        Print(new
        {
            dir = Path.GetFullPath(dir),
            count = notes.Count,
            notes = notes.Select(n => new
            {
                n.Note.Id,
                n.Note.At,
                n.Note.Kind,
                text = n.Note.Text ?? (n.Note.Kind == "voice" ? $"(spoken, {n.Note.VoiceSeconds} s: voice.wav)" : null),
                n.Note.Night,
                into = $"{(int)n.Note.Seconds / 60}:{(int)n.Note.Seconds % 60:00}, km {n.Note.Km}",
                n.Note.Tags,
                n.Note.Commit,
            }),
        });
        return 0;
    }

    static int Show(string content, string dir, string id)
    {
        string bundle = Path.Combine(dir, id);
        if (!File.Exists(Path.Combine(bundle, FeedbackBundle.NoteFile)))
        {
            Console.Error.WriteLine($"no note {id} in {Path.GetFullPath(dir)}");
            return 1;
        }
        var note = FeedbackNote.FromJson(File.ReadAllText(Path.Combine(bundle, FeedbackBundle.NoteFile)));
        object? moment = null;
        // The recording beside the notes (a pull's recordings/, or this machine's own).
        string? recording = note.Recording is not { } file ? null
            : new[] { Path.Combine(dir, "..", "recordings", file), Path.Combine(NightRecorder.DefaultDirectory, file) }.FirstOrDefault(File.Exists);
        if (recording is not null)
        {
            using var replay = NightReplay.Open(recording, content);
            replay.Run(note.Tick);
            var world = replay.Host.World;
            var crew = replay.Host.Players.ToList();
            string shot = Path.Combine("out", "shots", $"{id}-replay.png");
            using (var shooter = new WorldShot(content, world.Route))
                shooter.Save(shot, shooter.Render(world, crew, WorldShot.Camera($"eye:{note.Player?.Id ?? 1}", world, crew), (byte)(note.Player?.Id ?? 1)));
            moment = new
            {
                recording = Path.GetFullPath(recording),
                tick = replay.Host.Tick,
                divergedAt = replay.DivergedAt,
                sameBuild = replay.Header.Commit == Report.Build().Commit,
                shot = Path.GetFullPath(shot),
            };
        }
        Print(new
        {
            note,
            files = Directory.EnumerateFiles(bundle).Select(Path.GetFullPath).Order(StringComparer.Ordinal),
            moment,
            why = moment is null ? (note.Recording is null ? "the note was made on a night this machine didn't host: no recording" : $"{note.Recording} isn't here (dt feedback pull)") : null,
        });
        return 0;
    }

    static int Make(string content, string[] args)
    {
        string route = Str(args, "--route", "frontier:7"), dir = Str(args, "--dir", "out/feedback/notes");
        int bots = (int)Opt(args, "--bots", 3);
        double at = Opt(args, "--at", 10), seconds = Math.Max(at, Opt(args, "--seconds", at + 2));
        var recorder = new NightRecorder(Str(args, "--recordings", Path.Combine(dir, "..", "recordings")));
        string bundle;
        using (var night = NetPlaySession.HostGame(content, new SessionSetup(Route: route, Cars: 6, Enemies: true), port: 0, bots: bots, tap: recorder))
        {
            for (int t = 0; t < at * SimConstants.TickRate; t++)
                night.Step(default);
            var host = night.Host!;
            var now = DateTime.UtcNow;
            string id = $"fb-{now:yyyyMMdd-HHmmss}-1";
            var note = FeedbackNote.Of(id, now, night, host, recorder.Current) with { Text = Str(args, "--text", "a note made headless") };
            recorder.Mark("feedback", new System.Text.Json.Nodes.JsonObject { ["id"] = id });
            var crew = host.Players.ToList();
            using (var shooter = new WorldShot(content, night.Route))
            {
                var frame = shooter.Render(host.World, crew, WorldShot.Camera($"eye:{night.PlayerId}", host.World, crew), (byte)night.PlayerId);
                bundle = FeedbackBundle.Write(dir, note, (frame, shooter.Width, shooter.Height), null, 48000, null);
            }
            for (double t = at * SimConstants.TickRate; t < seconds * SimConstants.TickRate; t++)
                night.Step(default);
        }
        Print(new { bundle = Path.GetFullPath(bundle), recording = Path.GetFullPath(recorder.Current!) });
        return 0;
    }

    static void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value, DataFile.Options));

    static string Str(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    static double Opt(string[] args, string name, double fallback) =>
        Str(args, name, "") is { Length: > 0 } v ? double.Parse(v, CultureInfo.InvariantCulture) : fallback;
}
