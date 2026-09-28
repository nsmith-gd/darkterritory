using Ballast.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Net;

/// <summary>
/// The authoritative simulation (GDD §33, spec E): consumes client intent one input per tick,
/// steps the train and every player, and broadcasts a snapshot to each client with the last input
/// it applied for them. The host player (when there is one) goes through the same intent path.
/// </summary>
public sealed class HostSession
{
    /// <summary>Beyond this many queued inputs a client is too far ahead; drop the oldest to bound latency.</summary>
    const int MaxQueuedInputs = 8;

    sealed class Crew(byte id, PeerId peer)
    {
        public readonly byte Id = id;
        public readonly PeerId Peer = peer;
        public readonly SortedDictionary<uint, PlayerIntent> Pending = new();
        public uint LastApplied;
        public PlayerIntent LastIntent;
        public PlayerIntent ThisTick;
        public PlayerState State;
        public int MissedInputs;
    }

    readonly ITransport _transport;
    readonly List<Crew> _crew = new();
    readonly List<TransportEvent> _events = new();
    readonly List<InputFrame> _frames = new();
    readonly NetWriter _writer = new();
    readonly PlayerSnapshot[] _snapshotScratch = new PlayerSnapshot[byte.MaxValue];
    byte _nextId = 1;

    public HostSession(ITransport transport, TrainOnLine train, TrainTuning trainTuning, PlayerTuning playerTuning)
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
    public TrainControls Controls;
    public uint Tick { get; private set; }
    public int PlayerCount => _crew.Count;
    public int LastSnapshotBytes { get; private set; }

    public IEnumerable<PlayerSnapshot> Players => _crew.Select(c => new PlayerSnapshot(c.Id, c.State));
    public int MissedInputs(byte id) => _crew.First(c => c.Id == id).MissedInputs;

    public void Step()
    {
        Receive();

        foreach (var c in _crew)
            c.ThisTick = NextIntent(c);

        Controls.Brake = 0;
        foreach (var c in _crew)
        {
            CabControls.Apply(ref Controls, c.ThisTick, c.State, Train);
            CrewActions.Apply(ref c.State, c.ThisTick, Train, SimConstants.TickSeconds);
        }
        Train.Step(SimConstants.TickSeconds, Controls);
        foreach (var c in _crew)
            PlayerMotor.Step(ref c.State, c.ThisTick, Train, PlayerTuning, TrainTuning, SimConstants.TickSeconds);
        Tick++;

        Broadcast();
    }

    PlayerIntent NextIntent(Crew c)
    {
        while (c.Pending.Count > MaxQueuedInputs)
            c.Pending.Remove(c.Pending.Keys.First());
        if (c.Pending.Count > 0)
        {
            // Take the oldest input we have. A gap means those inputs were lost; skip past them.
            var (seq, intent) = c.Pending.First();
            c.Pending.Remove(seq);
            c.LastApplied = seq;
            c.LastIntent = intent;
            return intent;
        }
        // Nothing arrived in time: hold what they were doing, minus one-shot actions and look,
        // so a stalled client keeps walking rather than spinning or re-notching the throttle.
        c.MissedInputs++;
        var held = c.LastIntent;
        held.LookYaw = held.LookPitch = 0;
        held.ThrottleNotch = 0;
        held.Buttons &= ~(PlayerButtons.Jump | PlayerButtons.Reverser);
        return held;
    }

    void Receive()
    {
        _events.Clear();
        _transport.Poll(_events);
        foreach (var e in _events)
        {
            switch (e.Kind)
            {
                case TransportEventKind.Connected:
                    Join(e.Peer);
                    break;
                case TransportEventKind.Disconnected:
                    // Spec E: the character should remain as an inert body. For now, remove it.
                    _crew.RemoveAll(c => c.Peer == e.Peer);
                    break;
                case TransportEventKind.Data when e.Payload is { Length: > 0 } payload:
                    OnData(e.Peer, payload);
                    break;
            }
        }
    }

    void Join(PeerId peer)
    {
        var c = new Crew(_nextId++, peer);
        // First aboard takes the cab; everyone else spreads down the train.
        int car = 1 + (_crew.Count - 1) % Math.Max(1, Train.Frames.Count - 1);
        c.State = _crew.Count == 0 ? PlayerMotor.SpawnInCab(Train, PlayerTuning) : PlayerMotor.SpawnOnRoof(Train, car, 0, PlayerTuning);
        _crew.Add(c);
        Messages.WriteWelcome(_writer, c.Id, Tick);
        _transport.Send(peer, _writer.Written, Delivery.ReliableOrdered);
    }

    void OnData(PeerId peer, byte[] payload)
    {
        var c = _crew.Find(x => x.Peer == peer);
        if (c is null)
            return;
        try
        {
            var r = new NetReader(payload);
            if ((MessageType)r.U8() != MessageType.Input)
                return;
            _frames.Clear();
            Messages.ReadInput(ref r, _frames, out _);
            foreach (var f in _frames)
                if (f.Sequence > c.LastApplied)
                    c.Pending.TryAdd(f.Sequence, Sanitise(f.Intent));
        }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException)
        {
            // Malformed packet: drop it. Never let a client crash the host.
        }
    }

    /// <summary>Anti-cheat baseline: clamp everything a client can send to legal ranges.</summary>
    static PlayerIntent Sanitise(PlayerIntent i)
    {
        static float Clamp(float v, float lim) => float.IsFinite(v) ? Math.Clamp(v, -lim, lim) : 0;
        i.MoveX = Clamp(i.MoveX, 1);
        i.MoveZ = Clamp(i.MoveZ, 1);
        i.LookYaw = Clamp(i.LookYaw, MathF.PI);
        i.LookPitch = Clamp(i.LookPitch, MathF.PI);
        i.ThrottleNotch = (sbyte)Math.Clamp((int)i.ThrottleNotch, -4, 4);
        return i;
    }

    void Broadcast()
    {
        int n = 0;
        foreach (var c in _crew)
            _snapshotScratch[n++] = new PlayerSnapshot(c.Id, c.State);
        var train = TrainSnapshot.Capture(Train, Controls);
        foreach (var c in _crew)
        {
            Messages.WriteSnapshot(_writer, Tick, c.LastApplied, train, _snapshotScratch.AsSpan(0, n));
            LastSnapshotBytes = _writer.Length;
            _transport.Send(c.Peer, _writer.Written, Delivery.Unreliable);
        }
    }
}
