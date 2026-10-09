using System.Text.Json;
using System.Text.Json.Nodes;
using Ballast;
using Ballast.Dev;
using Ballast.Net;
using DarkTerritory.Game;
using DarkTerritory.Sim.Net;

namespace DarkTerritory.Dev.Replay;

/// <summary>
/// What a recording says of the night it holds (ARCHITECTURE §8 note 515): everything <see cref="NetPlaySession.BuildHost"/>
/// needs to build the host's world again, and the build that recorded it. A recording carries the host-only parts of the
/// setup that a joiner's Welcome leaves out (the line plan of a resumed night, the music bag, the crew's looks).
/// </summary>
public sealed record NightHeader
{
    public const string FormatName = "dark-territory-night";
    public string Format { get; init; } = FormatName;
    public int Version { get; init; } = 1;
    /// <summary>The build that recorded it: its version, and its commit (a replay on other code may part from it).</summary>
    public string Build { get; init; } = "";
    public string Commit { get; init; } = "";
    /// <summary>When it began, on the recording machine's clock: for people, never for the sim.</summary>
    public DateTime RecordedAt { get; init; }
    /// <summary>The setup as a joiner gets it (<see cref="SessionSetup.Encode"/>), its content's hashes and mods with it.</summary>
    public string Setup { get; init; } = "";
    public bool FirstChildReal { get; init; }
    /// <summary>A resumed night's line plan, compressed, or null when it was generated from the route.</summary>
    public byte[]? Plan { get; init; }
    public Sim.Music.MusicBag? MusicBag { get; init; }
    public Dictionary<string, string>? Identities { get; init; }
    public Sim.Campaign.RunCheckpoint? Resume { get; init; }
    /// <summary>The crew the night's threats were planned for.</summary>
    public int Crew { get; init; }
    public byte[]? PasswordKey { get; init; }
    /// <summary>The host's loss window (note 540), in ticks: what it was told, not what this machine's HUD would say.</summary>
    public int? LossWindowTicks { get; init; }
    /// <summary>Whether the crew's voices are in it; by default they're blanked (their lengths kept: the sim's the same).</summary>
    public bool Voice { get; init; }
    public int TickRate { get; init; } = Sim.SimConstants.TickRate;

    public static NightHeader For(HostedNight night, bool voice, DateTime at)
    {
        var (version, commit) = Report.Build();
        var s = night.Setup;
        return new NightHeader
        {
            Build = version,
            Commit = commit,
            RecordedAt = at,
            Setup = s.Encode(),
            FirstChildReal = s.FirstChildReal,
            Plan = s.Plan?.Compress(),
            MusicBag = s.MusicBag,
            Identities = s.Identities?.ToDictionary(kv => kv.Key, kv => kv.Value),
            Resume = night.Resume,
            Crew = night.Crew,
            PasswordKey = night.PasswordKey,
            LossWindowTicks = night.LossWindowTicks,
            Voice = voice,
        };
    }

    /// <summary>The night as <see cref="NetPlaySession.BuildHost"/> takes it.</summary>
    public HostedNight Night() => new(SessionSetup.Decode(Setup) with
    {
        FirstChildReal = FirstChildReal,
        Plan = Plan is { } plan ? Sim.LineGen.LinePlan.Decompress(plan) : null,
        MusicBag = MusicBag,
        Identities = Identities,
    }, Resume, Crew, PasswordKey)
    {
        LossWindowTicks = LossWindowTicks ?? new HudTuning().LossWindowTicks,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Compact);
    public static NightHeader FromJson(string json) =>
        JsonSerializer.Deserialize<NightHeader>(json, Compact) is { Format: FormatName } h ? h
            : throw new InvalidDataException("not a recorded night of Dark Territory");

    internal static readonly JsonSerializerOptions Compact = new(DataFile.Options) { WriteIndented = false };
}

/// <summary>
/// Records every night this machine hosts (note 515), one file each in <see cref="Directory"/>: a developer build sets it as
/// <see cref="NetPlaySession.Tap"/> at launch. It wraps the host's transport (<see cref="RecordingTransport"/>), notes what
/// changes the host between its steps (whose machine it is: the host player), and closes the file when the night's over.
/// Its cost on the frame is copying what arrived and hashing what's sent, a few microseconds a tick (note 515 measures it).
/// </summary>
public sealed class NightRecorder(string directory, bool voice = false, int keep = 30) : IHostTap
{
    /// <summary>Where a developer build keeps its recordings: the user's app data, beside the bookmarks and the crashes.</summary>
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DarkTerritory", "recordings");

    public string Directory => directory;

    /// <summary>Told each recording's path as it starts (the crash reports name it: a crash in a night can be replayed to).</summary>
    public Action<string>? Started { get; set; }

    /// <summary>Told each recording's path once it's closed and whole (the notes made in it go up with it, note 516).</summary>
    public event Action<string>? Closed;

    /// <summary>The night being recorded, if one is (or the last one, once it's closed).</summary>
    public string? Current { get; private set; }

    /// <summary>What recording the night being (or last) recorded has cost the frame so far, and over how many polls.</summary>
    public (TimeSpan Spent, long Polls, long RawBytes) Cost => _transport is { } t ? (t.Spent, t.Log.Polls, t.Log.RawBytes) : _last;
    (TimeSpan, long, long) _last;

    RecordingTransport? _transport;
    HostSession? _host;
    int _hostPlayer = -1;
    DateTime _began;

    /// <summary>A voice frame's audio blanked, its header and its length kept (the sim reads only that someone spoke).</summary>
    public static ReadOnlySpan<byte> ScrubVoice(byte[] payload)
    {
        // Client → host voice: [type][sequence u16][radio flag][Opus...] (Messages.WriteVoice).
        const int Header = 4;
        if (payload.Length <= Header || payload[0] != (byte)MessageType.Voice)
            return payload;
        var blank = new byte[payload.Length];
        payload.AsSpan(0, Header).CopyTo(blank);
        return blank;
    }

    /// <summary>A send counts in its step's digest unless it's voice: what a blanked recording forwards can't match.</summary>
    public static bool Digested(ReadOnlySpan<byte> payload) => payload.Length == 0 || payload[0] != (byte)MessageType.Voice;

    public ITransport Hosting(ITransport transport, HostedNight night)
    {
        Close("superseded");
        System.IO.Directory.CreateDirectory(directory);
        Prune();
        _began = DateTime.UtcNow;
        string name = $"{_began:yyyyMMdd-HHmmss}-{Slug(night.Setup.Route ?? night.Setup.Line)}{TransportLog.Extension}";
        Current = Path.Combine(directory, name);
        var header = NightHeader.For(night, voice, _began);
        var log = new TransportLogWriter(File.Create(Current), header.ToJson());
        _transport = new RecordingTransport(transport, log, voice ? null : ScrubVoice, Digested) { BeforePoll = BetweenSteps };
        _hostPlayer = -1;
        Started?.Invoke(Current);
        return _transport;
    }

    public void Hosted(HostSession host, HostedNight night)
    {
        _host = host;
        // The rejoin tokens (note 253) are drawn from the system's secure random, by design: each is noted as it's drawn, for
        // the replay to hand out the same ones in the same order (a crewmate coming back presents theirs).
        if (_transport is { } recording)
        {
            var draw = host.Tokens;
            host.Tokens = () =>
            {
                ulong token = draw();
                recording.Log.Note(Note("token", new JsonObject { ["token"] = token.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
                return token;
            };
        }
        // An online host's friends come in without the password (note 450): the answer is the platform's, so it's noted for
        // the replay, which has no platform to ask.
        if (host.Trusted is { } trusted && _transport is { } t)
            host.Trusted = peer =>
            {
                bool ok = trusted(peer);
                t.Log.Note(Note("trusted", new JsonObject { ["peer"] = peer.Value, ["trusted"] = ok }));
                return ok;
            };
    }

    public void Ended(HostSession host)
    {
        if (ReferenceEquals(host, _host))
            Close("ended");
    }

    /// <summary>
    /// Writes a note into the night being recorded (the feedback key's mark, note 516): at the tick it's at, unless the fields
    /// give one (a note kept a little after the moment it was begun at). The fields are the caller's; <c>kind</c> is added.
    /// </summary>
    public bool Mark(string kind, JsonObject fields)
    {
        if (_transport is not { } t || _host is not { } h)
            return false;
        fields["tick"] ??= h.Tick;
        t.Log.Note(Note(kind, fields));
        return true;
    }

    /// <summary>What the app does to the host between its steps, which the host can't hear through its transport.</summary>
    void BetweenSteps()
    {
        if (_host is { } h && h.HostPlayer != _hostPlayer && _transport is { } t)
        {
            _hostPlayer = h.HostPlayer;
            t.Log.Note(Note("hostPlayer", new JsonObject { ["id"] = _hostPlayer }));
        }
    }

    static string Note(string kind, JsonObject fields)
    {
        fields["kind"] = kind;
        return fields.ToJsonString(NightHeader.Compact);
    }

    /// <summary>Closes the night being recorded, if one is: at the app's exit, which can come without the night's end.</summary>
    public void Finish() => Close("exit");

    void Close(string why)
    {
        if (_transport is not { } t)
            return;
        t.CloseStep();
        var end = new JsonObject
        {
            ["why"] = why,
            ["ticks"] = _host?.Tick ?? 0,
            ["polls"] = t.Log.Polls,
            ["rawBytes"] = t.Log.RawBytes,
            ["end"] = _host?.World.Run is { } run ? run.End.ToString() : null,
            ["wallSeconds"] = Math.Round((DateTime.UtcNow - _began).TotalSeconds, 1),
        };
        t.Log.Close(end.ToJsonString(NightHeader.Compact));
        _last = (t.Spent, t.Log.Polls, t.Log.RawBytes);
        _transport = null;
        _host = null;
        if (Current is { } closed)
            Closed?.Invoke(closed);
    }

    /// <summary>Keeps the newest <c>keep</c> recordings; older ones go (a night's tens of megabytes at most).</summary>
    void Prune()
    {
        var old = new DirectoryInfo(directory).GetFiles("*" + TransportLog.Extension).OrderByDescending(f => f.Name, StringComparer.Ordinal).Skip(Math.Max(0, keep - 1));
        foreach (var f in old)
            try
            {
                f.Delete();
            }
            catch (IOException)
            {
            }
    }

    static string Slug(string s) => new([.. s.Select(c => char.IsLetterOrDigit(c) ? c : '-')]);
}
