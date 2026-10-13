using System.Numerics;
using System.Text.Json.Nodes;
using Ballast.Platform;
using Ballast.Render;
using DarkTerritory.Dev.Replay;
using DarkTerritory.Game;
using DarkTerritory.Sim.Net;

namespace DarkTerritory.Dev.Feedback;

/// <summary>
/// The director's notes from inside the night (ARCHITECTURE §8 note 516), in a developer build: <b>F8</b> types one (Enter
/// keeps it, Escape drops it), <b>F9</b> held says one (let go, it's kept). The moment is fixed at the press: the frame on the
/// screen, the tick (a mark in the night's recording, note 515), where they stood and what was near. Each note's a bundle in
/// the app data's <c>feedback/</c> (<see cref="FeedbackBundle"/>), sent to the repository's <c>feedback</c> branch when a
/// token's set (<see cref="FeedbackUpload"/>), and its night's recording with it when the night's over.
/// <para>While a note's being typed the night goes on, but the keys are the note's; a spoken note goes to the studio, not
/// the crew (the mic isn't sent, and it's not loud to the Choir, while F9 is held).</para>
/// </summary>
public sealed class FeedbackNotes(string directory, NightRecorder? recorder, CrashReports? reports, FeedbackUpload? upload, int sampleRate = 48000)
{
    public const Key TypeKey = Key.F8, SayKey = Key.F9;

    /// <summary>A spoken note's longest (s): past it, it's kept as it is.</summary>
    public const double MaxSeconds = 90;

    readonly List<float> _voice = new();
    readonly System.Text.StringBuilder _text = new();
    FeedbackNote? _pending;
    (byte[] Rgba, int Width, int Height)? _frame;
    bool _wantFrame;
    int _made;
    bool _wasCaptured;
    string? _toast;
    double _toastUntil;
    readonly List<Task> _sending = new();
    readonly HashSet<string> _nightsNoted = new(StringComparer.Ordinal);

    public string Directory => directory;
    public bool Typing { get; private set; }
    public bool Speaking { get; private set; }
    /// <summary>Notes kept this run.</summary>
    public int Made => _made;
    public FeedbackUpload? Upload => upload;

    /// <summary>The last note kept: its bundle's folder.</summary>
    public string? Last { get; private set; }

    /// <summary>
    /// The keys, before the night reads them: F8 or F9 starts a note; while one's typed, this frame's presses and typing are
    /// the note's, and are taken (<see cref="InputState.Swallow"/>) so nothing after sees them.
    /// </summary>
    public void Keys(Window window, InputState input, IPlaySession session, HostSession? host, double now)
    {
        if (Typing)
        {
            foreach (char c in input.Text)
                if (!char.IsControl(c) && _text.Length < 600)
                    _text.Append(c);
            if (input.Pressed(Key.Backspace) && _text.Length > 0)
                _text.Length--;
            if (input.Pressed(Key.Enter))
            {
                Keep(_text.ToString().Trim(), null, now);
                Close(window);
            }
            else if (input.Pressed(Key.Escape))
            {
                _pending = null;
                _frame = null;
                Close(window);
                Toast("NOTE DROPPED", now);
            }
            input.Swallow();
            return;
        }
        if (Speaking)
        {
            double said = _voice.Count / (double)sampleRate;
            if (!input.Down(SayKey) || said >= MaxSeconds)
            {
                Speaking = false;
                if (_voice.Count == 0)
                    Toast("NO MICROPHONE: F8 TO TYPE A NOTE", now);
                else
                    Keep(null, [.. _voice], now);
                _voice.Clear();
            }
            return;
        }
        if (input.Pressed(TypeKey))
        {
            Begin(session, host);
            Typing = true;
            _text.Clear();
            _wasCaptured = window.MouseCaptured;
            window.TextInput = true;
            input.Swallow();
        }
        else if (input.Pressed(SayKey))
        {
            Begin(session, host);
            Speaking = true;
            _voice.Clear();
        }
    }

    void Close(Window window)
    {
        Typing = false;
        window.TextInput = false;
        window.MouseCaptured = _wasCaptured;
    }

    /// <summary>The moment, fixed at the press: the note's context, a mark in the recording, and the frame wanted.</summary>
    void Begin(IPlaySession session, HostSession? host)
    {
        var at = DateTime.UtcNow;
        string id = $"fb-{at:yyyyMMdd-HHmmss}-{_made + 1}";
        string? recording = host is not null ? recorder?.Current : null;
        _pending = FeedbackNote.Of(id, at, session, host, recording);
        _wantFrame = true;
        _frame = null;
    }

    /// <summary>The mic's samples this frame (the app's mic loop): true while they're a spoken note's, not the crew's.</summary>
    public bool Hears(ReadOnlySpan<float> samples)
    {
        if (!Speaking)
            return false;
        foreach (float s in samples)
            _voice.Add(s);
        return true;
    }

    /// <summary>Each frame, before it's shown: the frame of the moment, if a note's just begun (one extra render, once).</summary>
    public void Frame(Func<byte[]> render, int width, int height)
    {
        if (!_wantFrame)
            return;
        _wantFrame = false;
        _frame = (render(), width, height);
    }

    void Keep(string? text, float[]? voice, double now)
    {
        if (_pending is not { } note)
            return;
        _pending = null;
        if (text is { Length: 0 } && voice is null)
        {
            Toast("NOTE DROPPED (NOTHING IN IT)", now);
            return;
        }
        note = note with
        {
            Kind = voice is null ? "typed" : "voice",
            Text = text,
            VoiceSeconds = voice is null ? null : Math.Round(voice.Length / (double)sampleRate, 1),
        };
        var frame = _frame;
        _frame = null;
        _made++;
        // Its mark in the night's recording, at the moment it was begun (a dropped note leaves none).
        if (note.Recording is not null && recorder?.Current is { } recording && Path.GetFileName(recording) == note.Recording)
        {
            recorder.Mark("feedback", new JsonObject { ["id"] = note.Id, ["tick"] = note.Tick });
            _nightsNoted.Add(recording);
        }
        // Written and sent off the frame: a PNG and a commit take longer than a frame has.
        _sending.RemoveAll(t => t.IsCompleted);
        _sending.Add(Task.Run(async () =>
        {
            string dir = FeedbackBundle.Write(directory, note, frame, voice, sampleRate, reports);
            Last = dir;
            if (upload is not null)
                await upload.SendAsync(dir, null).ConfigureAwait(false);
        }));
        Toast(upload is null ? $"NOTE {_made} KEPT" : $"NOTE {_made} KEPT AND SENT", now);
    }

    /// <summary>
    /// A night recorded with notes in it is over (or the app's closing): its recording goes up beside them, whole, so the
    /// notes' replays play.
    /// </summary>
    public void NightOver(string recording)
    {
        if (upload is null || !_nightsNoted.Remove(recording))
            return;
        _sending.Add(Task.Run(() => upload.CommitAsync([($"recordings/{Path.GetFileName(recording)}", recording)], $"Recording {Path.GetFileName(recording)}")));
    }

    /// <summary>Waits for what's being written and sent (at the app's exit), up to <paramref name="seconds"/>.</summary>
    public void Settle(double seconds = 20) => Task.WaitAll([.. _sending], TimeSpan.FromSeconds(seconds));

    void Toast(string text, double now)
    {
        _toast = text;
        _toastUntil = now + 3;
    }

    static readonly Vector4 Ink = new(1, 0.85f, 0.55f, 1), Dim = new(0.75f, 0.75f, 0.75f, 0.6f), Red = new(1, 0.3f, 0.25f, 1);

    /// <summary>The note being made, or the word that it's kept; the keys under the corner's mark otherwise.</summary>
    public void Draw(Overlay overlay, int width, int height, double now)
    {
        if (Typing)
        {
            string line = $"NOTE FOR THE STUDIO: {_text}{((int)(now * 2) % 2 == 0 ? "_" : " ")}";
            overlay.Rect(8, height - 40, width - 16, 30, new Vector4(0, 0, 0, 0.7f));
            overlay.Text(14, height - 36, line.Length > 110 ? "…" + line[^109..] : line, Ink);
            overlay.TextRight(width - 14, height - 22, "ENTER KEEPS IT · ESC DROPS IT", Dim);
        }
        else if (Speaking)
        {
            double said = _voice.Count / (double)sampleRate;
            overlay.TextCentred(width / 2f, height - 30, $"● A NOTE FOR THE STUDIO {(int)said / 60}:{(int)said % 60:00} (LET GO OF F9 TO KEEP IT)", Red);
        }
        else if (_toast is { } t && now < _toastUntil)
            overlay.TextCentred(width / 2f, height - 30, t, Ink);
        overlay.TextRight(width - 3, 12, "F8 NOTE · F9 SAY", Dim);
    }
}
