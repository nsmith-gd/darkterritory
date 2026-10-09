using System.Text.Json.Nodes;
using Ballast.Dev;
using Ballast.Net;
using DarkTerritory.Game;
using DarkTerritory.Sim.Net;

namespace DarkTerritory.Dev.Replay;

/// <summary>A note recorded in the night (a feedback marker, note 516): its kind, the tick it was made at, and its fields.</summary>
public sealed record NightMark(string Kind, uint Tick, JsonObject Fields);

/// <summary>
/// A recorded night played again (ARCHITECTURE §8 note 515): the host's world built as <see cref="NetPlaySession.BuildHost"/>
/// built it from the recording's header, then stepped once per recorded poll with what that poll handed it. After each
/// step what the host sent is compared with the recorded digest, so the first tick that differs (other code, other content,
/// or a nondeterministic sim) is named. The host is a real <see cref="HostSession"/>: anything that reads one (the census,
/// the report, a screenshot of its world) reads the replay at any tick.
/// </summary>
public sealed class NightReplay : IDisposable
{
    readonly IEnumerator<LogRecord> _records;
    readonly ReplayTransport _transport = new(NightRecorder.Digested);
    readonly HashSet<ulong> _trusted;
    readonly Queue<ulong> _tokens;

    NightReplay(string path, string content, NightHeader header, HashSet<ulong> trusted, Queue<ulong> tokens)
    {
        Path = path;
        Header = header;
        _trusted = trusted;
        _tokens = tokens;
        var night = header.Night();
        ContentDifferences = night.Setup.ContentDifferences(content);
        (Host, _, _, _) = NetPlaySession.BuildHost(content, night, _transport);
        if (trusted.Count > 0)
            Host.Trusted = peer => _trusted.Contains(peer.Value);
        // The rejoin tokens it drew, in order; past them (a recording from before they were noted), fresh ones.
        var fresh = Host.Tokens;
        Host.Tokens = () => _tokens.TryDequeue(out ulong t) ? t : fresh();
        _records = TransportLogReader.Read(path).Skip(1).GetEnumerator();
    }

    public static NightReplay Open(string path, string content)
    {
        NightHeader? header = null;
        var trusted = new HashSet<ulong>();
        var tokens = new Queue<ulong>();
        // What the host asked outside itself mid-step (the platform about friends, the system for a token) was noted as it
        // was asked: gathered first, to answer alike when the replay asks.
        foreach (var r in Records(path))
        {
            if (r is LogHeader h)
                header = NightHeader.FromJson(h.Json);
            else if (r is LogNote n && JsonNode.Parse(n.Json) is JsonObject o)
                switch ((string?)o["kind"])
                {
                    case "trusted" when (bool?)o["trusted"] == true:
                        trusted.Add((ulong)o["peer"]!);
                        break;
                    case "token":
                        tokens.Enqueue(ulong.Parse((string)o["token"]!, System.Globalization.CultureInfo.InvariantCulture));
                        break;
                }
        }
        return new NightReplay(path, content, header ?? throw new InvalidDataException($"{path} has no header"), trusted, tokens);
    }

    /// <summary>The records as far as they go: a recording cut off by a crash ends where it was cut.</summary>
    static IEnumerable<LogRecord> Records(string path)
    {
        using var e = TransportLogReader.Read(path).GetEnumerator();
        while (true)
        {
            try
            {
                if (!e.MoveNext())
                    yield break;
            }
            catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException or IOException)
            {
                yield break;
            }
            yield return e.Current;
        }
    }

    public string Path { get; }
    public NightHeader Header { get; }
    public HostSession Host { get; }

    /// <summary>Tuning files that differ between the recording machine's content and this one's: the replay may part there.</summary>
    public IReadOnlyList<string> ContentDifferences { get; }

    /// <summary>Steps replayed so far.</summary>
    public int Steps { get; private set; }

    /// <summary>The first tick whose sends differ from the recording's, or null while they've all matched.</summary>
    public uint? DivergedAt { get; private set; }

    /// <summary>The recording's own last word on how it ended, when it got that far (a crash leaves none).</summary>
    public JsonObject? End { get; private set; }

    public bool Finished { get; private set; }

    public List<NightMark> Marks { get; } = new();

    bool _stepped;

    /// <summary>Plays the next step (and the notes before it); false once the recording's played out.</summary>
    public bool Advance()
    {
        while (!Finished)
        {
            if (!Next(out var record))
            {
                Finished = true;
                break;
            }
            switch (record)
            {
                case LogPoll poll:
                    // (A step with no digest after it was the last before the recorder was cut off: it's played unchecked.)
                    _transport.Next = poll.Events;
                    Host.Step();
                    Steps++;
                    _stepped = true;
                    return true;
                case LogSent sent:
                    if (_stepped && DivergedAt is null && (sent.Digest != _transport.SentSincePoll.Value || sent.Count != _transport.SentSincePoll.Count))
                        DivergedAt = Host.Tick - 1;
                    _stepped = false;
                    break;
                case LogNote note:
                    Apply(note);
                    break;
                case LogEnd end:
                    End = JsonNode.Parse(end.Json) as JsonObject;
                    break;
            }
        }
        return false;
    }

    bool Next(out LogRecord record)
    {
        try
        {
            if (_records.MoveNext())
            {
                record = _records.Current;
                return true;
            }
        }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException or IOException)
        {
            // A recording cut off (the game crashed, or was killed): it plays to where it got.
        }
        record = null!;
        return false;
    }

    /// <summary>The recording stops short of its end record: cut off by a crash, or still being written.</summary>
    public bool Truncated => Finished && End is null;

    void Apply(LogNote note)
    {
        if (JsonNode.Parse(note.Json) is not JsonObject o)
            return;
        switch ((string?)o["kind"])
        {
            case "hostPlayer":
                Host.HostPlayer = (int)o["id"]!;
                break;
            case "trusted" or "token":
                break;
            case string kind:
                Marks.Add(new NightMark(kind, (uint?)o["tick"] ?? Host.Tick, o));
                break;
        }
    }

    /// <summary>Plays on to the end of the recording, or until the host's at <paramref name="tick"/>.</summary>
    public void Run(uint? tick = null)
    {
        while ((tick is null || Host.Tick < tick) && Advance())
        {
        }
    }

    public void Dispose() => _records.Dispose();
}
