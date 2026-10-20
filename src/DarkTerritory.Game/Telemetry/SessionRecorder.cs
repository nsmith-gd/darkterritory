using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Telemetry;

/// <summary>Where telemetry lines go: a file (<see cref="FileTelemetrySink"/>), memory (tests), and later #626's spool and upload.</summary>
public interface ITelemetrySink
{
    void Write(string line);
    void Flush();
}

/// <summary>Lines kept in memory (tests, <c>dt telemetry</c>).</summary>
public sealed class MemoryTelemetrySink : ITelemetrySink
{
    public List<string> Lines { get; } = [];
    public void Write(string line) => Lines.Add(line);
    public void Flush() { }
}

/// <summary>A session's lines appended to one JSON-lines file (#626 caps and rotates the spool these go in).</summary>
public sealed class FileTelemetrySink(string path) : ITelemetrySink, IDisposable
{
    StreamWriter? _out;
    public string Path { get; } = path;

    public void Write(string line)
    {
        if (_out is null)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
            _out = new StreamWriter(Path, append: true, new UTF8Encoding(false));
        }
        _out.Write(line);
        _out.Write('\n');
    }

    public void Flush() => _out?.Flush();

    public void Dispose()
    {
        _out?.Dispose();
        _out = null;
    }
}

/// <summary>What the machine and the build are (the <c>session</c> event).</summary>
/// <param name="Cohort">The playtest's cohort code (#628, note 916: typed in Settings or baked into a playtest build); null, none.</param>
public sealed record SessionFacts(string Edition, bool Dev, string Os, string Gpu, bool Vr, string Resolution, double RenderScale, bool VSync,
    double FieldOfView, string? Cohort = null)
{
    /// <summary>The OS as the runtime names it.</summary>
    public static string ThisOs => System.Runtime.InteropServices.RuntimeInformation.OSDescription;
}

/// <summary>
/// The playtest session recorder (queue #624, note 913; the plan, note 907). Each machine records its own session: the host
/// its nights as the host sees them, a client what its player saw and did. It only reads the Sim (its World, its crew's
/// states, the host's incident and encounter logs) and never writes it, so a night runs the same with it or without
/// (determinism, <c>NetcodeTests</c>); it lives here, never in <c>DarkTerritory.Sim</c>, and sends nothing on the wire.
/// <para><b>Consent first:</b> made with <c>enabled: false</c> (the player said no, or hasn't been asked: #625's switch), every
/// call returns at once and nothing is written, not even the session line.</para>
/// <para>Bounded: the night's state is looked at once a sim second, rolled up a minute at a time; events only for what
/// happens a few times a night. A 45-minute night is a few hundred lines (<c>dt telemetry record</c>).</para>
/// <para>The events and their fields are <see cref="TelemetrySchema"/>'s, and no others.</para>
/// </summary>
public sealed class SessionRecorder
{
    readonly ITelemetrySink? _sink;
    readonly ITelemetryGate _gate;
    readonly Func<double> _clock;
    int _night;
    // The crew as last looked at: who was alive.
    readonly Dictionary<int, bool> _alive = [];
    int _incidents;
    uint _nightTick;
    // This machine's player: where they began the night, and what they've done first.
    int _me = -1;
    Ballast.Double3 _startAt;
    int _startParent = int.MinValue;
    readonly HashSet<string> _firsts = new(StringComparer.Ordinal);
    double _startKm;
    // Frames: this minute's and the night's histograms, a millisecond a bin (the last a second or more), and the hitches.
    const int Bins = 1000;
    readonly int[] _minuteFrames = new int[Bins + 1], _nightFrames = new int[Bins + 1];
    int _minuteHitches, _nightHitches, _minute;
    double _median = 1000.0 / 60;
    double? _pingMs, _loss;

    /// <param name="gate">The player's consent (#625), read on every write: not recording, nothing is written.</param>
    /// <param name="clock">Seconds since the session began; a stopwatch by default (tests and <c>dt</c> give their own).</param>
    public SessionRecorder(ITelemetrySink? sink, ITelemetryGate gate, Func<double>? clock = null, string? sessionId = null)
    {
        _sink = sink;
        _gate = gate;
        SessionId = sessionId ?? NewId();
        if (clock is null)
        {
            var watch = Stopwatch.StartNew();
            clock = () => watch.Elapsed.TotalSeconds;
        }
        _clock = clock;
    }

    /// <summary>Recording: consent given now (the gate's asked every time), and somewhere to write.</summary>
    public bool Enabled => _sink is not null && _gate.Recording;
    /// <summary>A random id for this session, made here: nothing about the player or the machine is in it.</summary>
    public string SessionId { get; }

    static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// A recorder writing to <c>&lt;the gate's sessions folder&gt;/&lt;session&gt;.jsonl</c>; the file's made on the first line written, so
    /// a session never recorded leaves none.
    /// </summary>
    public static SessionRecorder ToFile(ITelemetryGate gate)
    {
        string id = NewId();
        return new SessionRecorder(new FileTelemetrySink(System.IO.Path.Combine(gate.SessionsDirectory, id + ".jsonl")), gate, sessionId: id);
    }

    /// <summary>The file sink's, to close at the end (null for any other).</summary>
    public IDisposable? Closer => _sink as IDisposable;
    public int Lines { get; private set; }
    public long Bytes { get; private set; }
    /// <summary>In a night (between <see cref="NightBegin"/> and <see cref="NightEnd"/>).</summary>
    public bool InNight { get; private set; }
    bool _closed;

    /// <summary>
    /// The session begins: what the build is, and what it runs on. Its line is written now if the player's said yes, else
    /// before the first line once they do (#625's question may come after the game's up).
    /// </summary>
    public void Begin(SessionFacts f)
    {
        _facts = f;
        if (Enabled)
            _sink!.Flush();
    }

    SessionFacts? _facts;
    bool _sessionWritten;

    void Session()
    {
        _sessionWritten = true;
        var f = _facts!;
        var (build, commit) = Report.Build();
        Emit("session", ("session", SessionId), ("install", _gate.InstallId), ("build", build), ("commit", commit), ("edition", f.Edition), ("dev", f.Dev),
            ("os", f.Os), ("gpu", f.Gpu), ("vr", f.Vr), ("resolution", f.Resolution), ("renderScale", f.RenderScale), ("vsync", f.VSync),
            ("fov", f.FieldOfView), ("cohort", f.Cohort));
    }

    /// <summary>
    /// A night begins. <paramref name="role"/>: "host", "client" or "solo"; <paramref name="me"/>, this machine's player (−1:
    /// none, a bot night). Humans and bots are counted, never named.
    /// </summary>
    public void NightBegin(World world, string role, int humans, int bots, int me)
    {
        if (!Enabled)
            return;
        _night++;
        InNight = true;
        _nightTick = world.Tick;
        _alive.Clear();
        _incidents = world.Attribution.Log.Count;
        _me = me;
        _startParent = int.MinValue;
        _firsts.Clear();
        _startKm = world.Train.Dynamics.Distance / 1000;
        Array.Clear(_minuteFrames);
        Array.Clear(_nightFrames);
        (_minuteHitches, _nightHitches, _minute) = (0, 0, 0);
        var route = world.Route;
        Emit("night", ("night", _night), ("role", role), ("route", route?.Name), ("tier", route?.Tier.ToString()),
            ("cars", world.Train.Dynamics.Consist.Vehicles.Count(v => !v.IsEngine)), ("humans", humans), ("bots", bots));
    }

    /// <summary>
    /// After each sim tick: once a sim second it looks at the crew and the logs (deaths, incidents, firsts), and once a
    /// minute it writes the minute.
    /// </summary>
    public void Tick(World world, IReadOnlyList<(int Id, PlayerState State)> crew)
    {
        if (!Enabled || !InNight || world.Tick % SimConstants.TickRate != 0)
            return;
        double s = Seconds(world), km = world.Train.Dynamics.Distance / 1000;
        foreach (var (id, state) in crew)
        {
            bool alive = state.Alive;
            if (_alive.TryGetValue(id, out bool was) && was && !alive && state.Death is not DeathCause.None and not DeathCause.Waiting)
                Emit("death", ("night", _night), ("s", s), ("km", km), ("cause", state.Death.ToString()),
                    ("post", Census.PostOf(state, world.Train, 60).ToString()), ("me", id == _me));
            _alive[id] = alive;
            if (id == _me && alive)
                Firsts(world, state, s);
        }
        var log = world.Attribution.Log;
        for (; _incidents < log.Count; _incidents++)
        {
            var i = log[_incidents];
            // When it's seen (within the second): the log's own time is the run's clock.
            Emit("incident", ("night", _night), ("s", s), ("kind", i.Kind.ToString()), ("cause", i.Cause == DeathCause.None ? null : i.Cause.ToString()));
        }
        // The first stop: the train at a stand once it's run a little way out of the yard.
        if (world.Train.Dynamics.Speed < 0.3 && km - _startKm > 0.2)
            First("stop", s);
        if (s >= (_minute + 1) * 60)
            Minute(world, crew, km);
    }

    /// <summary>A frame shown, and how long it took (ms): the frame-time percentiles and hitches (a stutter the player feels,
    /// FrameWatch's: at least <see cref="FrameWatch.HitchMs"/> and <see cref="FrameWatch.HitchOverMedian"/> times the last
    /// minute's median).</summary>
    public void Frame(double ms)
    {
        if (!Enabled || !InNight)
            return;
        int bin = Math.Clamp((int)ms, 0, Bins);
        _minuteFrames[bin]++;
        _nightFrames[bin]++;
        if (ms >= FrameWatch.HitchMs && ms >= FrameWatch.HitchOverMedian * _median)
        {
            _minuteHitches++;
            _nightHitches++;
        }
    }

    /// <summary>The link as it is now (a client's ping and snapshot loss; null for the host and solo).</summary>
    public void Link(double? pingMs, double? loss) => (_pingMs, _loss) = (pingMs, loss);

    public void Prompt(string name, bool acted)
    {
        if (Enabled)
            Emit("prompt", ("night", InNight ? _night : 0), ("name", name), ("acted", acted));
    }

    public void Purchase(string what, double cost, double scripAfter)
    {
        if (Enabled)
            Emit("purchase", ("what", what), ("cost", cost), ("scripAfter", scripAfter));
    }

    public void Rebind(string control)
    {
        if (Enabled)
            Emit("rebind", ("control", control));
    }

    public void Menu(string name, double seconds)
    {
        if (Enabled)
            Emit("menu", ("name", name), ("seconds", seconds));
    }

    /// <summary>The last night begun this session (0: none yet): what a survey after it is about.</summary>
    public int LastNight => _night;

    /// <summary>
    /// The end-of-night survey (#628, note 916), after <see cref="NightEnd"/>: the night just played, its three answers (1-5,
    /// null not said) and the player's comment if they wrote one; skipped, none of it.
    /// </summary>
    public void Survey(SurveyAnswers a)
    {
        if (!Enabled || _night == 0)
            return;
        string? comment = a.Skipped || a.Comment is not { } c || c.Trim().Length == 0 ? null : c.Trim()[..Math.Min(c.Trim().Length, SurveyAnswers.CommentLength)];
        Emit("survey", ("night", _night), ("fun", a.Skipped ? null : Rating(a.Fun)), ("fear", a.Skipped ? null : Rating(a.Fear)),
            ("again", a.Skipped ? null : Rating(a.Again)), ("comment", comment), ("skipped", a.Skipped));
        _sink!.Flush();
    }

    static int? Rating(int? r) => r is >= 1 and <= 5 ? r : null;

    /// <summary>The night over (or left): the host's encounters, then how it ended and how it ran.</summary>
    public void NightEnd(World world)
    {
        if (!Enabled || !InNight)
            return;
        if (world.Encounters is { } encounters)
            foreach (var e in encounters.Encounters)
                Emit("encounter", ("night", _night), ("s", FromRun(world, e.Seconds)), ("kind", e.Kind.ToString()), ("channel", e.Channel.ToString()),
                    ("where", e.Where), ("part", e.Part), ("afoot", e.Afoot), ("engagedAt", Never(world, e.EngagedAt)),
                    ("confrontedAt", Never(world, e.ConfrontedAt)), ("nearAt", Never(world, e.NearAt)), ("grabs", e.Grabs), ("punishes", e.Punishes),
                    ("end", e.End.ToString()), ("endedAt", Never(world, e.EndedAt)));
        var r = world.Run?.Report;
        if (r is { End: RunEnd.Delivered })
            First("delivery", Seconds(world));
        var (p50, p95, p99) = Percentiles(_nightFrames);
        Emit("nightEnd", ("night", _night), ("end", (world.Run?.End ?? RunEnd.None).ToString()), ("seconds", Seconds(world)),
            ("km", world.Train.Dynamics.Distance / 1000), ("carsDelivered", r?.CarsDelivered), ("carsLost", r?.CarsLost),
            ("cargoDelivered", r?.CargoDelivered), ("net", r?.Net), ("crewHome", r?.CrewHome), ("crewLost", r?.CrewLost), ("deaths", r?.Deaths),
            ("encounters", world.Encounters?.Encounters.Count), ("hitches", _nightHitches), ("p50", p50), ("p95", p95), ("p99", p99));
        InNight = false;
        _sink!.Flush();
    }


    /// <summary>
    /// Leaving: "menu" (to the menu mid-night), "app" (the game closed), "kicked" or "disconnected"; with the night and how far
    /// into it, if one was on.
    /// </summary>
    public void Quit(string how, World? world = null)
    {
        // The game closed mid-night is said then, with the night; not again as the app ends.
        if (!Enabled || how == "app" && _closed)
            return;
        _closed |= how == "app";
        Emit("quit", ("how", how), ("night", InNight ? _night : 0), ("s", InNight && world is not null ? Seconds(world) : null));
        _sink!.Flush();
    }

    void Firsts(World world, in PlayerState me, double s)
    {
        if (_startParent == int.MinValue)
            (_startParent, _startAt) = (me.Parent, me.Position);
        else if (me.Parent != _startParent || (me.Position - _startAt).Length > 1)
            First("move", s);
        var a = world.Attribution;
        if (a.Driver == _me || a.Fireman == _me || me.Has(PlayerFlags.Seated))
            First("job", s);
    }

    void First(string what, double s)
    {
        if (_firsts.Add(what))
            Emit("first", ("night", _night), ("what", what), ("s", s));
    }

    void Minute(World world, IReadOnlyList<(int Id, PlayerState State)> crew, double km)
    {
        _minute++;
        int frames = _minuteFrames.Sum();
        var (p50, p95, p99) = Percentiles(_minuteFrames);
        if (p50 is { } median && median > 0)
            _median = median;
        Emit("minute", ("night", _night), ("minute", _minute), ("km", km), ("alive", crew.Count(c => c.State.Alive)), ("crew", crew.Count),
            ("frames", frames), ("hitches", _minuteHitches), ("p50", p50), ("p95", p95), ("p99", p99), ("pingMs", _pingMs), ("loss", _loss));
        Array.Clear(_minuteFrames);
        _minuteHitches = 0;
    }

    static (double? P50, double? P95, double? P99) Percentiles(int[] histogram)
    {
        int n = histogram.Sum();
        if (n == 0)
            return (null, null, null);
        double At(double q)
        {
            int want = (int)Math.Ceiling(q * n), seen = 0;
            for (int i = 0; i < histogram.Length; i++)
                if ((seen += histogram[i]) >= want)
                    return i + 0.5;
            return histogram.Length;
        }
        return (At(0.5), At(0.95), At(0.99));
    }

    /// <summary>The night's clock: seconds since <see cref="NightBegin"/>, the yard included (every <c>s</c> is on it).</summary>
    double Seconds(World world) => (world.Tick - _nightTick) * SimConstants.TickSeconds;
    /// <summary>A time on the encounter log's clock (note 880's: the run's own, which starts at the yard's gate, where the yard is
    /// safe; else the world's), put on the night's.</summary>
    double FromRun(World world, double logSeconds) =>
        logSeconds + Seconds(world) - (world.Run is { Tuning.YardIsSafe: true } run ? run.Seconds : world.ElapsedSeconds);
    double? Never(World world, double at) => at < 0 ? null : FromRun(world, at);

    /// <summary>One line: the common fields, then the type's, each of which must be in <see cref="TelemetrySchema.Events"/>.</summary>
    void Emit(string type, params (string Key, object? Value)[] fields)
    {
        // Consent's asked on every line (#625): switched off mid-night, nothing more is written.
        if (!Enabled)
            return;
        if (!_sessionWritten && _facts is not null && type != "session")
            Session();
        if (!TelemetrySchema.Events.TryGetValue(type, out var allowed))
            throw new InvalidOperationException($"telemetry: no event \"{type}\" in the schema");
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("schema", TelemetrySchema.Version);
            w.WriteString("type", type);
            w.WriteNumber("t", Math.Round(_clock(), 3));
            foreach (var (key, value) in fields)
            {
                if (Array.IndexOf(allowed, key) < 0)
                    throw new InvalidOperationException($"telemetry: \"{type}\" has no field \"{key}\" in the schema");
                w.WritePropertyName(key);
                switch (value)
                {
                    case null:
                        w.WriteNullValue();
                        break;
                    case string text:
                        w.WriteStringValue(text);
                        break;
                    case bool b:
                        w.WriteBooleanValue(b);
                        break;
                    case int i:
                        w.WriteNumberValue(i);
                        break;
                    case double d when double.IsFinite(d):
                        w.WriteNumberValue(Math.Round(d, 3));
                        break;
                    case double:
                        w.WriteNullValue();
                        break;
                    default:
                        throw new InvalidOperationException($"telemetry: \"{type}.{key}\" is a {value.GetType().Name}");
                }
            }
            w.WriteEndObject();
        }
        string line = Encoding.UTF8.GetString(stream.ToArray());
        _sink!.Write(line);
        Lines++;
        Bytes += line.Length + 1;
    }
}
