using System.Numerics;
using Ballast.Render;
using DarkTerritory.Game;

namespace DarkTerritory.Dev;

/// <summary>
/// The developer tools a developer build of the app carries (ARCHITECTURE §8 note 514), started once at launch. A player's
/// build has none of this: the app reaches it only under <c>#if DEVTOOLS</c>, which only a DevTools build defines, and
/// tools/package.sh builds players' builds without it (<c>dt build check</c> proves it of the package).
/// </summary>
public sealed class DevTools
{
    /// <param name="args">The app's command line: <c>--no-dev-mark</c> hides the corner's mark (for footage); <c>--no-record</c>
    /// records no nights; <c>--record-voice</c> keeps the crew's voices in them; <c>--recordings dir</c> keeps them there.</param>
    /// <param name="crashes">The app's crash reports: a crash names the night's recording, to replay up to it; a note carries
    /// the report's header (note 516). <c>--feedback dir</c> keeps the notes there; <c>--no-notes</c> turns F8 and F9 off.</param>
    /// <param name="logs">The game's own folder's logs (<see cref="TestReports.GameLogs"/>, GDD §34.2, note 677), when the app
    /// keeps them there: the notes go in it too. Null, they're in the app data as before.</param>
    public static DevTools Start(string[] args, CrashReports? crashes = null, string? logs = null) => new(args, crashes, logs);

    DevTools(string[] args, CrashReports? crashes, string? logs)
    {
        Marked = !args.Contains("--no-dev-mark");
        var (_, commit) = Report.Build();
        Label = $"DEV {(commit.Length > 7 ? commit[..7] : commit).ToUpperInvariant()}";
        // Every night this machine hosts is recorded (note 515), for `dt replay` to play again tick for tick.
        if (!args.Contains("--no-record"))
        {
            int at = Array.IndexOf(args, "--recordings");
            Recorder = new Replay.NightRecorder(at >= 0 && at + 1 < args.Length ? args[at + 1] : Replay.NightRecorder.DefaultDirectory,
                voice: args.Contains("--record-voice"));
            NetPlaySession.Tap = Recorder;
            Recorder.Started = path => crashes?.Context("recording", path);
            // A window closed mid-night ends the process without the night's end: the recording's closed then.
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Recorder.Finish();
        }
        // Every report this build writes, on the end of one file beside this launch's log (GDD §34.2, note 677): the
        // director sends that, not a report at a time.
        if (crashes is not null)
        {
            string beside = Path.GetDirectoryName(crashes.LogPath) ?? logs ?? CrashReports.LogsBeside(crashes.Directory);
            Tests = new TestReports(Path.Combine(beside, TestReports.FileName));
            crashes.Saved = (report, _) => Tests.Add(report, TestReports.Called(report));
            Console.WriteLine($"test reports: {Tests.Path}");
        }
        // The director's notes from inside the night (note 516), sent on when this machine has a token for it.
        if (!args.Contains("--no-notes"))
        {
            int at = Array.IndexOf(args, "--feedback");
            string notes = at >= 0 && at + 1 < args.Length ? args[at + 1] : logs is not null ? Path.Combine(logs, "notes") : Feedback.FeedbackBundle.DefaultDirectory;
            Notes = new Feedback.FeedbackNotes(notes, Recorder, crashes, Feedback.FeedbackUpload.FromEnvironment()) { Tests = Tests };
            if (Recorder is not null)
                Recorder.Closed += Notes.NightOver;
            // What's still being written or sent gets a moment at the exit (after the recording's closed, above).
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Notes.Settle();
        }
    }

    /// <summary>Every report this build writes, in one file (GDD §34.2, note 677); null without the app's crash reports.</summary>
    public TestReports? Tests { get; }

    /// <summary>The director's notes from inside the night (note 516), or null with <c>--no-notes</c>.</summary>
    public Feedback.FeedbackNotes? Notes { get; }

    readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>Records every night this machine hosts (note 515), or null with <c>--no-record</c>.</summary>
    public Replay.NightRecorder? Recorder { get; }

    /// <summary>The corner's mark is drawn: whoever's looking (the director's test builds, a screenshot) knows it's a dev build.</summary>
    public bool Marked { get; }

    /// <summary>"DEV" and the build's commit.</summary>
    public string Label { get; }

    static readonly Vector4 MarkColour = new(0.75f, 0.75f, 0.75f, 0.45f);

    /// <summary>The mark, small and dim in the top right, over whatever the frame shows (the HUD or a menu).</summary>
    public void Draw(Overlay overlay, int width, int height)
    {
        if (Marked)
            overlay.TextRight(width - 3, 3, Label, MarkColour);
        Notes?.Draw(overlay, width, height, _clock.Elapsed.TotalSeconds);
    }
}
