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
    /// <param name="crashes">The app's crash reports: a crash names the night's recording, to replay up to it.</param>
    public static DevTools Start(string[] args, CrashReports? crashes = null) => new(args, crashes);

    DevTools(string[] args, CrashReports? crashes)
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
    }

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
    }
}
