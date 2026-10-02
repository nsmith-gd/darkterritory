using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Net;

/// <summary>Bump when any message's layout changes: a lobby on another protocol is refused before connecting.</summary>
public static class Protocol
{
    // 3: the wreck's poses (RecordKind.Wreck) and the Welcome's compact content hashes (T116, T117).
    // 4: the sim's trigonometry is DMath's, the same bits on every OS, so a 3 generates different lines from one seed.
    // 5: a body's record carries its tools (the engineering kit on the engineer, GDD v1.4 D.2).
    // 6: names (Hello, Names) and the incident report (Report) for GDD v1.4 App. C.9 and D.12.
    // 7: the crew lockers (note 173): a vehicle record's lockers' doors, a body record's locker and shelf.
    // 8: hit confirms and cannonball impacts (note 171), and the voice stream's hard-cut (note 172).
    // 9: the world record carries the derailment's track (GDD v1.4 App. E.6, note 174).
    // 10: the derailment film's start (Film) and the skip vote on the world record (GDD v1.4 App. E.2, E.5; note 176).
    public const int Version = 10;
}

public enum MessageType : byte
{
    /// <summary>Client → host, unreliable: the latest few inputs, redundantly, to ride out loss.</summary>
    Input = 1,
    /// <summary>Host → client, unreliable: authoritative world state for one tick.</summary>
    Snapshot = 2,
    /// <summary>Host → client, reliable: which player you are.</summary>
    Welcome = 3,
    /// <summary>Voice frame. Client → host: sequence, radio flag, Opus. Host → client: speaker, sequence, path, Opus.</summary>
    Voice = 4,
    /// <summary>Host → client, reliable: welcomed, but not aboard yet (spec E: drop-in at POIs only), and why.</summary>
    Wait = 5,
    /// <summary>Client → host, reliable: the name this player goes by (the roster, the report, the clerk).</summary>
    Hello = 6,
    /// <summary>Host → client, reliable: everyone's names, by player id, whenever they change.</summary>
    Names = 7,
    /// <summary>Host → client, reliable: one chunk of the night's report (GDD v1.4 App. D.12), compressed, in order.</summary>
    Report = 8,
    /// <summary>Host → client, reliable: one chunk of the derailment film's start (GDD v1.4 App. E.2), compressed, in order.</summary>
    Film = 9,
}

public readonly record struct InputFrame(uint Sequence, PlayerIntent Intent);

public readonly record struct PlayerSnapshot(byte Id, PlayerState State);

/// <summary>
/// Wire format. Hand-written and flat for now; delta compression against the last acked
/// snapshot and interest management come next (ARCHITECTURE §6.2).
/// </summary>
public static class Messages
{
    /// <summary>How many past inputs each input packet repeats.</summary>
    public const int InputRedundancy = 4;

    public static void WriteInput(NetWriter w, ReadOnlySpan<InputFrame> frames, uint lastSnapshotTick)
    {
        w.Reset();
        w.U8((byte)MessageType.Input);
        w.U32(lastSnapshotTick);
        w.U8((byte)frames.Length);
        foreach (var f in frames)
        {
            w.U32(f.Sequence);
            WriteIntent(w, f.Intent);
        }
    }

    public static void ReadInput(ref NetReader r, List<InputFrame> into, out uint lastSnapshotTick)
    {
        lastSnapshotTick = r.U32();
        int n = r.U8();
        for (int i = 0; i < n; i++)
            into.Add(new InputFrame(r.U32(), ReadIntent(ref r)));
    }

    static void WriteIntent(NetWriter w, in PlayerIntent i)
    {
        w.F32(i.MoveX);
        w.F32(i.MoveZ);
        w.F32(i.LookYaw);
        w.F32(i.LookPitch);
        w.U8((byte)i.Buttons);
        // The notch (−4..4) in the low five bits, the lamp switch (T52) in the two above: no extra byte on every intent.
        // The top bit says a watched crewmate follows (App. D.10), so only the dead pay for it.
        w.U8((byte)((i.ThrottleNotch & 0x1F) | ((byte)i.Lamp & 3) << 5 | (i.Watch != 0 ? 0x80 : 0)));
        // v1.1's second byte of buttons, and the voice level the loudness meter hears (App. C.7).
        bool tool = i.Select != 0 || i.Cycle != 0;
        w.U8((byte)(tool ? i.Actions | PlayerActions.Tool : i.Actions & ~PlayerActions.Tool));
        w.U8(i.Voice);
        if (i.Watch != 0)
            w.U8(i.Watch);
        // A hotbar choice (T108), only on the tick it's made: the slot in the low nibble, the wheel's step in the high.
        if (tool)
            w.U8((byte)((i.Select & 0x0F) | (i.Cycle > 0 ? 0x10 : i.Cycle < 0 ? 0x20 : 0)));
        // A reaching hand (T29) in centimetres, only when there is one: keyboards and bots send nothing more.
        if (i.Has(PlayerButtons.Hand))
        {
            w.I16(Centimetres(i.HandX));
            w.I16(Centimetres(i.HandY));
            w.I16(Centimetres(i.HandZ));
            // And the other hand, when it's tracked (T43).
            w.U8(i.Other ? (byte)1 : (byte)0);
            if (i.Other)
            {
                w.I16(Centimetres(i.OtherX));
                w.I16(Centimetres(i.OtherY));
                w.I16(Centimetres(i.OtherZ));
            }
        }
    }

    static short Centimetres(float metres) => (short)Math.Round(Math.Clamp(float.IsFinite(metres) ? metres : 0, -300, 300) * 100);

    static PlayerIntent ReadIntent(ref NetReader r)
    {
        var i = new PlayerIntent
        {
            MoveX = r.F32(),
            MoveZ = r.F32(),
            LookYaw = r.F32(),
            LookPitch = r.F32(),
            Buttons = (PlayerButtons)r.U8(),
        };
        byte notch = r.U8();
        i.ThrottleNotch = (sbyte)((sbyte)(notch << 3) >> 3);
        i.Lamp = (LampSwitch)((notch >> 5) & 3);
        i.Actions = (PlayerActions)r.U8();
        i.Voice = r.U8();
        if ((notch & 0x80) != 0)
            i.Watch = r.U8();
        if (i.Has(PlayerActions.Tool))
        {
            byte t = r.U8();
            i.Select = (byte)(t & 0x0F);
            i.Cycle = (sbyte)((t & 0x10) != 0 ? 1 : (t & 0x20) != 0 ? -1 : 0);
            i.Actions &= ~PlayerActions.Tool;
        }
        if (i.Has(PlayerButtons.Hand))
        {
            i.HandX = r.I16() / 100f;
            i.HandY = r.I16() / 100f;
            i.HandZ = r.I16() / 100f;
            i.Other = r.U8() != 0;
            if (i.Other)
            {
                i.OtherX = r.I16() / 100f;
                i.OtherY = r.I16() / 100f;
                i.OtherZ = r.I16() / 100f;
            }
        }
        return i;
    }

    /// <summary>Snapshot: tick, the input it acknowledges, the tick it's a delta against (0 = full), then the records.</summary>
    public static void WriteSnapshot(NetWriter w, uint tick, uint ackedInput, uint baselineTick, IReadOnlyList<WireRecord> records, IReadOnlyList<WireRecord>? baseline)
    {
        w.Reset();
        w.U8((byte)MessageType.Snapshot);
        w.U32(tick);
        w.U32(ackedInput);
        w.U32(baselineTick);
        WorldRecords.WriteDelta(w, records, baseline);
    }

    /// <param name="session">What a joining machine needs to build the same world (route, cars...), opaque to the sim.</param>
    public static void WriteVoiceUp(NetWriter w, ushort sequence, bool radio, ReadOnlySpan<byte> opus)
    {
        w.Reset();
        w.U8((byte)MessageType.Voice);
        w.U16(sequence);
        w.Bool(radio);
        w.Bytes(opus);
    }

    /// <param name="source">For <see cref="VoicePath.Mimic"/>: the Soot Child it's coming from (T40).</param>
    /// <param name="gain">For <see cref="VoicePath.Fading"/>: how much of the voice is left, 0..1.</param>
    public static void WriteVoiceDown(NetWriter w, byte speaker, ushort sequence, VoicePath path, ReadOnlySpan<byte> opus, int source = 0, double gain = 1)
    {
        w.Reset();
        w.U8((byte)MessageType.Voice);
        w.U8(speaker);
        w.U16(sequence);
        w.U8((byte)path);
        if (path.HasFlag(VoicePath.Mimic))
            w.I32(source);
        if (path.HasFlag(VoicePath.Fading))
            w.U8((byte)Math.Round(Math.Clamp(gain, 0, 1) * 255));
        w.Bytes(opus);
    }

    public static void WriteWait(NetWriter w, string reason)
    {
        w.Reset();
        w.U8((byte)MessageType.Wait);
        w.Str(reason);
    }

    public static void WriteWelcome(NetWriter w, byte playerId, uint tick, string session = "")
    {
        w.Reset();
        w.U8((byte)MessageType.Welcome);
        w.U8(playerId);
        w.U32(tick);
        w.Str(session);
    }

    /// <summary>A name as the session shows it: printable, trimmed, at most this long.</summary>
    public const int NameLength = 20;

    public static string CleanName(string? name)
    {
        var s = new string((name ?? "").Where(c => c >= ' ' && c < 0x7f).ToArray()).Trim();
        return s.Length > NameLength ? s[..NameLength].TrimEnd() : s;
    }

    public static void WriteHello(NetWriter w, string name)
    {
        w.Reset();
        w.U8((byte)MessageType.Hello);
        w.Str(CleanName(name));
    }

    public static void WriteNames(NetWriter w, IEnumerable<KeyValuePair<int, string>> names)
    {
        w.Reset();
        w.U8((byte)MessageType.Names);
        var list = names.Where(n => n.Key is >= 0 and < 256).OrderBy(n => n.Key).ToList();
        w.U8((byte)list.Count);
        foreach (var (id, name) in list)
        {
            w.U8((byte)id);
            w.Str(CleanName(name));
        }
    }

    public static void ReadNames(ref NetReader r, IDictionary<int, string> into)
    {
        int n = r.U8();
        for (int i = 0; i < n; i++)
        {
            int id = r.U8();
            into[id] = CleanName(r.Str());
        }
    }

    /// <summary>The bytes of a chunk, under the transport's 1200 B datagram with room for the headers.</summary>
    const int ReportChunk = 1000;

    /// <summary>The report as reliable messages: its JSON, Brotli-compressed, cut into numbered chunks.</summary>
    public static List<byte[]> ReportMessages(Run.RunReport report) =>
        Chunked(MessageType.Report, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(report));

    /// <summary>
    /// The derailment film's start in chunks (E.2 step 5): exact over JSON (doubles round-trip), so every client shoots the
    /// same film the host would.
    /// </summary>
    public static List<byte[]> FilmMessages(Train.FilmStart start) =>
        Chunked(MessageType.Film, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(start, Ballast.DataFile.Options));

    /// <summary>The film's start once every chunk is in, else null.</summary>
    public static Train.FilmStart? ReadFilm(IReadOnlyDictionary<int, byte[]> chunks, int count) =>
        Unchunk(chunks, count) is { } json ? System.Text.Json.JsonSerializer.Deserialize<Train.FilmStart>(json, Ballast.DataFile.Options) : null;

    /// <summary>JSON, Brotli-compressed, cut into numbered reliable messages of <paramref name="type"/>.</summary>
    static List<byte[]> Chunked(MessageType type, byte[] json)
    {
        using var buffer = new MemoryStream();
        using (var z = new System.IO.Compression.BrotliStream(buffer, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            z.Write(json);
        var packed = buffer.ToArray();
        int count = Math.Max(1, (packed.Length + ReportChunk - 1) / ReportChunk);
        var messages = new List<byte[]>(count);
        var w = new NetWriter();
        for (int i = 0; i < count; i++)
        {
            w.Reset();
            w.U8((byte)type);
            w.U8((byte)i);
            w.U8((byte)count);
            w.Bytes(packed.AsSpan(i * ReportChunk, Math.Min(ReportChunk, packed.Length - i * ReportChunk)));
            messages.Add(w.Written.ToArray());
        }
        return messages;
    }

    /// <summary>Puts reassembled chunks back into the report; null while some are still to come.</summary>
    public static Run.RunReport? ReadReport(IReadOnlyDictionary<int, byte[]> chunks, int count) =>
        Unchunk(chunks, count) is { } json ? System.Text.Json.JsonSerializer.Deserialize<Run.RunReport>(json) : null;

    static byte[]? Unchunk(IReadOnlyDictionary<int, byte[]> chunks, int count)
    {
        if (count == 0 || Enumerable.Range(0, count).Any(i => !chunks.ContainsKey(i)))
            return null;
        using var packed = new MemoryStream(Enumerable.Range(0, count).SelectMany(i => chunks[i]).ToArray());
        using var z = new System.IO.Compression.BrotliStream(packed, System.IO.Compression.CompressionMode.Decompress);
        using var json = new MemoryStream();
        z.CopyTo(json);
        return json.ToArray();
    }

    /// <summary>Reads a Welcome after its type byte.</summary>
    public static (byte PlayerId, uint Tick, string Session) ReadWelcome(ref NetReader r) => (r.U8(), r.U32(), r.Remaining > 0 ? r.Str() : "");
}
