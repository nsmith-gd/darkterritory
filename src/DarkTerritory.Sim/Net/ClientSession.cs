using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Net;

/// <summary>
/// A connected player (GDD §33): applies its own input immediately (prediction), sends it to the
/// host, and when a snapshot arrives snaps to the host's truth and replays inputs the host has not
/// yet applied (reconciliation). The train is re-simulated the same way: it is deterministic from
/// its state and controls, so the client predicts it too. Other players are shown interpolated
/// between snapshots, a little in the past.
/// </summary>
/// <param name="Source">For a <see cref="VoicePath.Mimic"/> frame, the enemy it comes from (T40); 0 otherwise.</param>
/// <param name="Gain">With <see cref="VoicePath.Fading"/>: how much of the speaker's voice is left, 0..1 (App. C.8).</param>
public readonly record struct VoiceFrame(byte Speaker, ushort Sequence, VoicePath Path, byte[] Opus, int Source = 0, double Gain = 1);

public sealed class ClientSession
{
    /// <summary>Remote players are drawn this many ticks behind the newest snapshot (100 ms at 30 Hz).</summary>
    public const int InterpolationTicks = 3;
    const int HistoryLength = 128;
    const int SnapshotBuffer = 32;

    readonly ITransport _transport;
    readonly List<TransportEvent> _events = new();
    readonly List<PlayerSnapshot> _players = new();
    readonly NetWriter _writer = new();
    readonly (uint Seq, PlayerIntent Intent, PlayerState After)[] _history = new (uint, PlayerIntent, PlayerState)[HistoryLength];
    readonly InputFrame[] _redundant = new InputFrame[Messages.InputRedundancy];
    readonly List<(uint Tick, PlayerSnapshot[] Players)> _snapshots = new();

    uint _sequence;
    uint _newestSnapshotTick;
    bool _haveState;

    public ClientSession(ITransport transport, TrainOnLine train, TrainTuning trainTuning, PlayerTuning playerTuning, CombatTuning? combat = null)
        : this(transport, new World(train, combat), trainTuning, playerTuning)
    {
    }

    public ClientSession(ITransport transport, World world, TrainTuning trainTuning, PlayerTuning playerTuning)
    {
        World = world;
        // Clients mirror enemies; only the host simulates them.
        _transport = transport;

        TrainTuning = trainTuning;
        PlayerTuning = playerTuning;
        Controls = new TrainControls { Reverser = 1, Brake = world.Train.BoilerTuning?.SteamDrive == true ? 1 : 0 };
    }

    public World World { get; }
    public TrainOnLine Train => World.Train;
    public TrainTuning TrainTuning { get; set; }
    /// <summary>The player tuning; its hand tuning is the world's too (<see cref="World.Hand"/>), so hot reload reaches both.</summary>
    public PlayerTuning PlayerTuning
    {
        get => _playerTuning;
        set
        {
            _playerTuning = value;
            World.Hand = value.Hand;
        }
    }
    PlayerTuning _playerTuning = null!;
    public byte? PlayerId { get; private set; }
    /// <summary>Voice frames from others, as they arrived. The game drains and decodes these.</summary>
    public Queue<VoiceFrame> VoiceFrames { get; } = new();
    readonly NetWriter _voiceWriter = new();

    /// <summary>Sends one encoded voice frame to the host, which forwards it to whoever it reaches.</summary>
    public void SendVoice(ushort sequence, bool radio, ReadOnlySpan<byte> opus)
    {
        if (PlayerId is null)
            return;
        Messages.WriteVoiceUp(_voiceWriter, sequence, radio, opus);
        _transport.Send(PeerId.Host, _voiceWriter.Written, Delivery.Unreliable);
    }
    public string SessionInfo { get; private set; } = "";
    /// <summary>Set while welcomed but not yet aboard (spec E: drop-in at POIs), with the host's reason.</summary>
    public string? WaitingReason { get; private set; }
    public bool Waiting => WaitingReason is not null && !Connected;
    public bool Connected => PlayerId is not null && _haveState;
    /// <summary>This player as predicted locally: what the local camera shows.</summary>
    public PlayerState Predicted;
    public TrainControls Controls;

    /// <summary>Distance between what we predicted for an input and what the host computed for it.</summary>
    public double LastCorrection { get; private set; }
    public double MaxCorrection { get; private set; }
    public int Corrections { get; private set; }
    public int SnapshotsReceived { get; private set; }

    /// <summary>Clears the correction statistics (after a deliberate host-side teleport, for example).</summary>
    public void ResetStats()
    {
        LastCorrection = MaxCorrection = 0;
        Corrections = 0;
    }
    public uint NewestSnapshotTick => _newestSnapshotTick;

    public void Step(in PlayerIntent intent)
    {
        Receive();
        if (!Connected)
            return;

        _sequence++;
        Predict(intent);
        _history[_sequence % HistoryLength] = (_sequence, intent, Predicted);
        SendInputs();
    }

    void Predict(in PlayerIntent intent)
    {
        World.BeginTick();
        // The host clears the brake every tick and re-applies whoever is holding it. If we're the one in
        // the cab it's almost certainly us, so do the same; otherwise assume whoever was braking still is.
        if (CabControls.CanDrive(Predicted, Train) && CabControls.Clears(Controls, Train, CabControls.ReleasesBrake(intent, Predicted, Train)))
            Controls.Brake = 0;
        CabControls.Apply(ref Controls, intent, Predicted, Train);
        World.CrewAct(ref Predicted, intent, PlayerId ?? 0);
        World.Step(Controls);
        PlayerMotor.Step(ref Predicted, intent, Train, PlayerTuning, TrainTuning, SimConstants.TickSeconds, applyLook: false);
        // The host snaps its world to the replication grid every tick; do the same so we match it exactly. The hand
        // isn't replicated (the next intent brings it), but this machine's HUD reads it between ticks, so it stays.
        _quantise.Clear();
        _quantise.Add(new PlayerSnapshot(PlayerId ?? 0, Predicted));
        WorldRecords.Quantise(World, ref Controls, _quantise);
        Predicted = _quantise[0].State with { Hand = Predicted.Hand, OtherHand = Predicted.OtherHand };
    }

    readonly List<PlayerSnapshot> _quantise = new();
    readonly Dictionary<uint, List<WireRecord>> _decoded = new();
    const int DecodedHistory = 64;

    void SendInputs()
    {
        int n = (int)Math.Min(_sequence, (uint)_redundant.Length);
        for (int i = 0; i < n; i++)
        {
            uint seq = _sequence - (uint)(n - 1 - i);
            _redundant[i] = new InputFrame(seq, _history[seq % HistoryLength].Intent);
        }
        Messages.WriteInput(_writer, _redundant.AsSpan(0, n), _newestSnapshotTick);
        _transport.Send(PeerId.Host, _writer.Written, Delivery.Unreliable);
    }

    void Receive()
    {
        _events.Clear();
        _transport.Poll(_events);
        // Decode every snapshot (each may be the baseline for a later one); reconcile against the newest.
        List<WireRecord>? newest = null;
        uint newestAcked = 0;
        foreach (var e in _events)
        {
            if (e.Kind == TransportEventKind.Disconnected)
            {
                PlayerId = null;
                _haveState = false;
                continue;
            }
            if (e.Kind != TransportEventKind.Data || e.Payload is not { Length: > 0 } payload)
                continue;
            var r = new NetReader(payload);
            switch ((MessageType)r.U8())
            {
                case MessageType.Voice:
                    byte speaker = r.U8();
                    ushort vseq = r.U16();
                    var path = (VoicePath)r.U8();
                    int source = path.HasFlag(VoicePath.Mimic) ? r.I32() : 0;
                    double gain = path.HasFlag(VoicePath.Fading) ? r.U8() / 255.0 : 1;
                    VoiceFrames.Enqueue(new VoiceFrame(speaker, vseq, path, r.Rest().ToArray(), source, gain));
                    break;
                case MessageType.Wait:
                    WaitingReason = r.Str();
                    break;
                case MessageType.Welcome:
                    (PlayerId, _, SessionInfo) = Messages.ReadWelcome(ref r);
                    break;
                case MessageType.Snapshot:
                    uint tick = r.U32(), acked = r.U32(), baseTick = r.U32();
                    if (tick <= _newestSnapshotTick || _decoded.ContainsKey(tick))
                        continue;
                    var baseline = baseTick == 0 ? null : _decoded.GetValueOrDefault(baseTick);
                    if (baseTick != 0 && baseline is null)
                        continue; // we no longer have what it's relative to; the next one will be
                    List<WireRecord> records;
                    try { records = WorldRecords.ReadDelta(ref r, baseline); }
                    catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException) { continue; }
                    SnapshotsReceived++;
                    _decoded[tick] = records;
                    if (_decoded.Count > DecodedHistory)
                        foreach (var old in _decoded.Keys.Where(k => k + DecodedHistory < tick).ToList())
                            _decoded.Remove(old);
                    Buffer(tick, records);
                    _newestSnapshotTick = tick;
                    newest = records;
                    newestAcked = acked;
                    break;
            }
        }
        if (newest is not null)
            Reconcile(newest, newestAcked);
    }

    void Buffer(uint tick, List<WireRecord> records)
    {
        var players = records.Where(r => r.Kind == RecordKind.Player).Select(r => PlayerFrom(r)).ToArray();
        _snapshots.Add((tick, players));
        if (_snapshots.Count > SnapshotBuffer)
            _snapshots.RemoveAt(0);
    }

    static PlayerSnapshot PlayerFrom(WireRecord r)
    {
        var list = new List<PlayerSnapshot>(1);
        WorldRecords.ApplyPlayers([r], list);
        return list[0];
    }

    void Reconcile(List<WireRecord> records, uint acked)
    {
        if (PlayerId is not { } id)
            return;
        WorldRecords.Apply(records, World, ref Controls, _players);
        int mine = _players.FindIndex(p => p.Id == id);
        if (mine < 0)
            return;
        var truth = _players[mine].State;

        if (!_haveState)
        {
            _haveState = true;
            Predicted = truth;
            _sequence = Math.Max(_sequence, acked);
            return;
        }

        var predicted = _history[acked % HistoryLength];
        // A host placement (respawn, revival) isn't a misprediction: adopt it without counting it.
        if (acked > 0 && predicted.Seq == acked && predicted.After.Placed == truth.Placed)
        {
            LastCorrection = Difference(predicted.After, truth);
            MaxCorrection = Math.Max(MaxCorrection, LastCorrection);
            if (LastCorrection > 1e-6)
                Corrections++;
        }

        // Rewind to the host's truth and replay everything it hasn't applied yet.
        Predicted = truth;
        for (uint seq = acked + 1; seq <= _sequence; seq++)
        {
            ref var h = ref _history[seq % HistoryLength];
            if (h.Seq != seq)
                continue;
            Predict(h.Intent);
            h.After = Predicted;
        }
    }

    /// <summary>Position error in metres, measured in the player's own frame; a frame change counts as large.</summary>
    /// <summary>
    /// How far the prediction was out. Across two frames (the ballast against a car's step, one roof against the next)
    /// it's measured in the world through the frames as they are now: exact at a stand, near enough on the move. It used to
    /// count any change of frame as 100 m, which hid how small those corrections are (a crate picked up on the host slows
    /// its carrier a round trip before their client knows).
    /// </summary>
    double Difference(in PlayerState a, in PlayerState b)
    {
        if (a.Parent == b.Parent)
            return (a.Position - b.Position).Length;
        if (a.Parent >= Train.Frames.Count || b.Parent >= Train.Frames.Count)
            return 100;
        return (PlayerMotor.WorldPosition(a, Train) - PlayerMotor.WorldPosition(b, Train)).Length;
    }

    /// <summary>
    /// Another player's state for rendering, interpolated <see cref="InterpolationTicks"/> behind the newest
    /// snapshot. Positions stay in their parent frame, so a remote player riding a car sits exactly on
    /// our (predicted) car even though their state is 100 ms old.
    /// </summary>
    public bool TryGetRemote(byte id, double alpha, out PlayerState state)
    {
        state = default;
        if (_snapshots.Count == 0)
            return false;
        double renderTick = _newestSnapshotTick - InterpolationTicks + alpha;
        int after = _snapshots.FindIndex(s => s.Tick >= renderTick);
        if (after < 0)
            return Find(_snapshots[^1].Players, id, out state);
        if (after == 0)
            return Find(_snapshots[0].Players, id, out state);
        var (t0, p0) = _snapshots[after - 1];
        var (t1, p1) = _snapshots[after];
        if (!Find(p0, id, out var a) || !Find(p1, id, out var b))
            return Find(p1, id, out state);
        double t = (renderTick - t0) / (t1 - t0);
        state = b;
        if (a.Parent == b.Parent)
        {
            state.Position = Double3.Lerp(a.Position, b.Position, t);
            state.Yaw = a.Yaw + (b.Yaw - a.Yaw) * t;
            // Their hands too, while they had them at both ends (T47).
            if (a.Hand != default && b.Hand != default)
                state.Hand = Double3.Lerp(a.Hand, b.Hand, t);
            if (a.OtherHand != default && b.OtherHand != default)
                state.OtherHand = Double3.Lerp(a.OtherHand, b.OtherHand, t);
        }
        return true;
    }

    public IEnumerable<byte> RemoteIds =>
        _snapshots.Count == 0 ? [] : _snapshots[^1].Players.Select(p => p.Id).Where(i => i != PlayerId);

    static bool Find(PlayerSnapshot[] players, byte id, out PlayerState state)
    {
        foreach (var p in players)
            if (p.Id == id)
            {
                state = p.State;
                return true;
            }
        state = default;
        return false;
    }
}
