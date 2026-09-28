using Ballast;
using Ballast.Net;
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

    public ClientSession(ITransport transport, TrainOnLine train, TrainTuning trainTuning, PlayerTuning playerTuning)
    {
        _transport = transport;
        Train = train;
        TrainTuning = trainTuning;
        PlayerTuning = playerTuning;
        Controls = new TrainControls { Reverser = 1 };
    }

    public TrainOnLine Train { get; }
    public TrainTuning TrainTuning { get; set; }
    public PlayerTuning PlayerTuning { get; set; }
    public byte? PlayerId { get; private set; }
    public bool Connected => PlayerId is not null && _haveState;
    /// <summary>This player as predicted locally: what the local camera shows.</summary>
    public PlayerState Predicted;
    public TrainControls Controls;

    /// <summary>Distance between what we predicted for an input and what the host computed for it.</summary>
    public double LastCorrection { get; private set; }
    public double MaxCorrection { get; private set; }
    public int Corrections { get; private set; }
    public int SnapshotsReceived { get; private set; }
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
        CabControls.Apply(ref Controls, intent, Predicted, Train);
        CrewActions.Apply(ref Predicted, intent, Train, SimConstants.TickSeconds);
        Train.Step(SimConstants.TickSeconds, Controls);
        PlayerMotor.Step(ref Predicted, intent, Train, PlayerTuning, TrainTuning, SimConstants.TickSeconds);
    }

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
        // Only the newest snapshot matters for reconciliation; all of them feed interpolation.
        byte[]? newest = null;
        uint newestTick = _newestSnapshotTick;
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
                case MessageType.Welcome:
                    PlayerId = r.U8();
                    break;
                case MessageType.Snapshot:
                    uint tick = new NetReader(payload.AsSpan(1)).U32();
                    SnapshotsReceived++;
                    Buffer(payload);
                    if (tick > newestTick)
                    {
                        newestTick = tick;
                        newest = payload;
                    }
                    break;
            }
        }
        if (newest is not null)
            Reconcile(newest);
    }

    void Buffer(byte[] payload)
    {
        var r = new NetReader(payload.AsSpan(1));
        _players.Clear();
        Messages.ReadSnapshot(ref r, out uint tick, out _, out _, _players);
        int at = _snapshots.FindIndex(s => s.Tick >= tick);
        if (at >= 0 && _snapshots[at].Tick == tick)
            return;
        _snapshots.Insert(at < 0 ? _snapshots.Count : at, (tick, _players.ToArray()));
        if (_snapshots.Count > SnapshotBuffer)
            _snapshots.RemoveAt(0);
    }

    void Reconcile(byte[] payload)
    {
        var r = new NetReader(payload.AsSpan(1));
        _players.Clear();
        Messages.ReadSnapshot(ref r, out uint tick, out uint acked, out var train, _players);
        _newestSnapshotTick = tick;
        if (PlayerId is not { } id)
            return;
        int mine = _players.FindIndex(p => p.Id == id);
        if (mine < 0)
            return;
        var truth = _players[mine].State;

        Train.Restore(train.Distance, train.Velocity, train.BrakeEfficiency, train.Boiler);
        Controls = train.Controls;

        if (!_haveState)
        {
            _haveState = true;
            Predicted = truth;
            _sequence = Math.Max(_sequence, acked);
            return;
        }

        var predicted = _history[acked % HistoryLength];
        if (acked > 0 && predicted.Seq == acked)
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
    static double Difference(in PlayerState a, in PlayerState b) =>
        a.Parent != b.Parent ? 100 : (a.Position - b.Position).Length;

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
