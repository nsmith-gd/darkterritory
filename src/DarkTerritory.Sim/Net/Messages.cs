using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Net;

/// <summary>Bump when any message's layout changes: a lobby on another protocol is refused before connecting.</summary>
public static class Protocol
{
    public const int Version = 2;
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
    /// <summary>
    /// Client → host, reliable: something asked for from the dead phase or at the run's end (GDD App. D.6, D.7, D.10-D.12):
    /// intent, never state. The host checks every one.
    /// </summary>
    Request = 5,
    /// <summary>Host → client, reliable: the night's incident report, at its end (App. D.12).</summary>
    Report = 6,
    /// <summary>
    /// Client → host, reliable, once after the Welcome: who this is beyond the session (their profile's id), so the host's
    /// campaign keeps their character against it (App. D.8).
    /// </summary>
    Hello = 7,
}

public readonly record struct InputFrame(uint Sequence, PlayerIntent Intent);

/// <summary>
/// What a player can ask the host for outside their movement (GDD App. D): whom to watch, their place in the queue, a
/// Call Out, the Live Mic, the creature vote, a bookmark, a commendation. <see cref="Request.A"/> and
/// <see cref="Request.B"/> are the arguments.
/// </summary>
public enum RequestKind : byte
{
    /// <summary>Watch a living crewmate (A: their id).</summary>
    Follow = 1,
    /// <summary>Move down the respawn queue (A: the position to move to).</summary>
    Defer = 2,
    /// <summary>Call Out from a Holdout (A: its index).</summary>
    CallOut = 3,
    /// <summary>The Live Mic on or off (A: 1 on, 0 off).</summary>
    LiveMic = 4,
    /// <summary>The creature vote (A: the enemy kind).</summary>
    Vote = 5,
    /// <summary>A bookmark of the followed view (A: the followed player). The still stays on the machine that took it.</summary>
    Bookmark = 6,
    /// <summary>A commendation (A: the player, B: the award's index).</summary>
    Commend = 7,
}

public readonly record struct Request(RequestKind Kind, int A, int B = 0);

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
        w.U8((byte)((i.ThrottleNotch & 0x1F) | ((byte)i.Lamp & 3) << 5));
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

    /// <param name="source">For <see cref="VoicePath.Mimic"/>: the Soot Child it's coming from (T40); for <see cref="VoicePath.Holdout"/>, the Holdout.</param>
    public static void WriteVoiceDown(NetWriter w, byte speaker, ushort sequence, VoicePath path, ReadOnlySpan<byte> opus, int source = 0)
    {
        w.Reset();
        w.U8((byte)MessageType.Voice);
        w.U8(speaker);
        w.U16(sequence);
        w.U8((byte)path);
        if (path.HasFlag(VoicePath.Mimic) || path.HasFlag(VoicePath.Holdout))
            w.I32(source);
        w.Bytes(opus);
    }

    public static void WriteHello(NetWriter w, string profile)
    {
        w.Reset();
        w.U8((byte)MessageType.Hello);
        w.Str(profile.Length > 64 ? profile[..64] : profile);
    }

    public static void WriteRequest(NetWriter w, in Request q)
    {
        w.Reset();
        w.U8((byte)MessageType.Request);
        w.U8((byte)q.Kind);
        w.I32(q.A);
        w.I32(q.B);
    }

    /// <summary>The most of a report one datagram carries, leaving room for the header.</summary>
    public const int ReportPartBytes = 1100;

    /// <summary>
    /// One part of a compressed incident report (App. D.12): its revision (a new one whenever a commendation's given), which
    /// part of how many, and that part's bytes. The report can outgrow a datagram (eight players' deaths and rescues), and
    /// nothing in the transport fragments.
    /// </summary>
    public static void WriteReport(NetWriter w, ushort revision, int part, int parts, ReadOnlySpan<byte> bytes)
    {
        w.Reset();
        w.U8((byte)MessageType.Report);
        w.U16(revision);
        w.U8((byte)part);
        w.U8((byte)parts);
        w.Bytes(bytes);
    }

    /// <summary>Reads a report part after its type byte.</summary>
    public static (ushort Revision, int Part, int Parts, byte[] Bytes) ReadReport(ref NetReader r) =>
        (r.U16(), r.U8(), r.U8(), r.Rest().ToArray());

    /// <summary>Reads a Request after its type byte.</summary>
    public static Request ReadRequest(ref NetReader r) => new((RequestKind)r.U8(), r.I32(), r.I32());

    public static void WriteWelcome(NetWriter w, byte playerId, uint tick, string session = "")
    {
        w.Reset();
        w.U8((byte)MessageType.Welcome);
        w.U8(playerId);
        w.U32(tick);
        w.Str(session);
    }

    /// <summary>Reads a Welcome after its type byte.</summary>
    public static (byte PlayerId, uint Tick, string Session) ReadWelcome(ref NetReader r) => (r.U8(), r.U32(), r.Remaining > 0 ? r.Str() : "");
}
