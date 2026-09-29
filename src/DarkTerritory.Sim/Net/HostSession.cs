using Ballast.Net;
using DarkTerritory.Sim.Combat;
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
        /// <summary>Newest snapshot this client says it has decoded: the baseline for its next delta.</summary>
        public uint AckedSnapshot;
        public PlayerIntent LastIntent;
        public PlayerIntent ThisTick;
        public PlayerState State;
        public int MissedInputs;
        /// <summary>What this client was sent at each tick: its own delta baselines, since interest differs per client.</summary>
        public readonly Dictionary<uint, List<WireRecord>> Sent = new();
    }

    readonly ITransport _transport;
    readonly List<Crew> _crew = new();
    readonly List<TransportEvent> _events = new();
    readonly List<InputFrame> _frames = new();
    readonly NetWriter _writer = new();
    readonly List<PlayerSnapshot> _snapshotScratch = new();
    /// <summary>Ticks of snapshot history kept for delta baselines (~2 s).</summary>
    const int HistoryTicks = 64;
    byte _nextId = 1;

    public HostSession(ITransport transport, TrainOnLine train, TrainTuning trainTuning, PlayerTuning playerTuning, CombatTuning? combat = null)
        : this(transport, new World(train, combat), trainTuning, playerTuning)
    {
    }

    public HostSession(ITransport transport, World world, TrainTuning trainTuning, PlayerTuning playerTuning)
    {
        World = world;
        _transport = transport;

        TrainTuning = trainTuning;
        PlayerTuning = playerTuning;
        Controls = new TrainControls { Reverser = 1 };
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
    public TrainControls Controls;
    /// <summary>Sent to everyone who joins, so they can build the same world (the host's route, car count...).</summary>
    public string SessionInfo { get; set; } = "";
    public uint Tick { get; private set; }
    public int PlayerCount => _crew.Count;
    public int LastSnapshotBytes { get; private set; }

    public IEnumerable<PlayerSnapshot> Players => _crew.Select(c => new PlayerSnapshot(c.Id, c.State));
    public int MissedInputs(byte id) => _crew.First(c => c.Id == id).MissedInputs;

    /// <summary>Starts the night's threats: the director, the route's Sleepers, the Hollow's watch (host authority).</summary>
    public void EnableEnemies(Enemies.EnemyTuning tuning, Route.Route? route, ulong seed, int expectedCrew)
    {
        World.EnableEnemies(tuning, route, seed, Math.Max(expectedCrew, _crew.Count), authority: true);
        InterestRadius = tuning.InterestRadius;
    }

    /// <summary>Puts a player somewhere authoritatively (respawns, debug teleports, tests).</summary>
    public void SetPlayerState(byte id, PlayerState state)
    {
        var crew = _crew.First(c => c.Id == id);
        state.Placed = (byte)(crew.State.Placed + 1);
        crew.State = state;
    }

    /// <summary>Every Vigil begun, broken or completed this session, oldest first.</summary>
    public List<Run.VigilEvent> VigilEvents { get; } = new();

    public void Step()
    {
        Receive();
        BoardWaiting();

        foreach (var c in _crew)
            c.ThisTick = NextIntent(c);

        World.BeginTick();
        Controls.Brake = 0;
        foreach (var c in _crew)
        {
            CabControls.Apply(ref Controls, c.ThisTick, c.State, Train);
            // Lag compensation: check this player's shots against where targets were on their screen.
            uint? view = c.AckedSnapshot > ClientSession.InterpolationTicks ? c.AckedSnapshot - ClientSession.InterpolationTicks : null;
            World.CrewAct(ref c.State, c.ThisTick, c.Id, view);
        }
        World.Step(Controls);
        World.ApplyDamage(id => _crew.FirstOrDefault(c => c.Id == id)?.State, (id, s) => _crew.First(c => c.Id == id).State = s, _crew.Select(c => (int)c.Id));
        foreach (var c in _crew)
            PlayerMotor.Step(ref c.State, c.ThisTick, Train, PlayerTuning, TrainTuning, SimConstants.TickSeconds, applyLook: false);
        World.StepBodies([.. _crew.Select(c => ((int)c.Id, c.State))]);
        if (World.StepVigil(id => _crew.FirstOrDefault(c => c.Id == id)?.State, (id, s) => _crew.First(c => c.Id == id).State = s,
            _crew.Select(c => (int)c.Id), PlayerTuning) is { } vigil)
            VigilEvents.Add(vigil);
        if (World.Run is not null)
            World.StepRun([.. _crew.Select(c => c.State)]);
        Mimic();
        Tick++;

        // Snap the world onto the replication grid and keep simulating from exactly that.
        _snapshotScratch.Clear();
        foreach (var c in _crew)
            _snapshotScratch.Add(new PlayerSnapshot(c.Id, c.State));
        var records = WorldRecords.Quantise(World, ref Controls, _snapshotScratch);
        foreach (var p in _snapshotScratch)
            _crew.First(c => c.Id == p.Id).State = p.State;

        Broadcast(records);
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
                    // Spec E: "Character remains as an inert body until recovered or the run ends."
                    foreach (var gone in _crew.Where(c => c.Peer == e.Peer).ToList())
                    {
                        if (gone.State.Alive && !World.Bodies.HasRagdoll(gone.Id))
                            World.Bodies.SpawnRagdoll(Train, gone.Id, gone.State);
                        _crew.Remove(gone);
                    }
                    _waiting.RemoveAll(w => w.Peer == e.Peer);
                    break;
                case TransportEventKind.Data when e.Payload is { Length: > 0 } payload:
                    OnData(e.Peer, payload);
                    break;
            }
        }
    }

    /// <summary>
    /// Spec E: drop-in "at POIs only". While this says no (the train is between stops), a joiner is welcomed,
    /// builds the world and watches, but only boards when it says yes, at <see cref="BoardAt"/>. Null: board now.
    /// </summary>
    public Func<bool>? CanBoard { get; set; }
    /// <summary>Where someone boarding mid-run appears (a figure waiting at the facility). Null: the usual spots.</summary>
    public Func<int, PlayerState>? BoardAt { get; set; }
    public string WaitReason { get; set; } = "the train is between stops: you'll board at the next one";
    readonly List<(byte Id, PeerId Peer)> _waiting = new();
    public int Waiting => _waiting.Count;

    void Join(PeerId peer)
    {
        byte id = _nextId++;
        Messages.WriteWelcome(_writer, id, Tick, SessionInfo);
        _transport.Send(peer, _writer.Written, Delivery.ReliableOrdered);
        if (CanBoard is { } can && !can())
        {
            _waiting.Add((id, peer));
            Messages.WriteWait(_writer, WaitReason);
            _transport.Send(peer, _writer.Written, Delivery.ReliableOrdered);
            return;
        }
        Board(id, peer);
    }

    void Board(byte id, PeerId peer)
    {
        var c = new Crew(id, peer);
        // First aboard takes the cab; everyone else spreads down the train.
        int car = 1 + (_crew.Count - 1) % Math.Max(1, Train.Frames.Count - 1);
        c.State = BoardAt is { } at && _crew.Count > 0 ? at(_crew.Count)
            : _crew.Count == 0 ? PlayerMotor.SpawnInCab(Train, PlayerTuning) : PlayerMotor.SpawnOnRoof(Train, car, 0, PlayerTuning);
        _crew.Add(c);
    }

    /// <summary>Boards anyone waiting, once the train is somewhere they can board it.</summary>
    void BoardWaiting()
    {
        if (_waiting.Count == 0 || CanBoard is { } can && !can())
            return;
        foreach (var (id, peer) in _waiting)
            Board(id, peer);
        _waiting.Clear();
    }

    void OnData(PeerId peer, byte[] payload)
    {
        var c = _crew.Find(x => x.Peer == peer);
        if (c is null)
            return;
        try
        {
            var r = new NetReader(payload);
            var type = (MessageType)r.U8();
            if (type == MessageType.Voice)
            {
                ForwardVoice(c, ref r);
                return;
            }
            if (type != MessageType.Input)
                return;
            _frames.Clear();
            Messages.ReadInput(ref r, _frames, out uint ackedSnapshot);
            c.AckedSnapshot = Math.Max(c.AckedSnapshot, ackedSnapshot);
            foreach (var f in _frames)
                if (f.Sequence > c.LastApplied)
                    c.Pending.TryAdd(f.Sequence, Sanitise(f.Intent));
        }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException)
        {
            // Malformed packet: drop it. Never let a client crash the host.
        }
    }

    readonly NetWriter _voiceWriter = new();

    /// <summary>Route for tunnels (radio dies in them). Defaults to the world's.</summary>
    public Route.Route? Route { get; set; }
    public long VoiceFramesForwarded { get; private set; }

    /// <summary>Forwards a voice frame to exactly the listeners it reaches (spec A.5, C.1). Never back to the speaker.</summary>
    void ForwardVoice(Crew speaker, ref NetReader r)
    {
        ushort seq = r.U16();
        bool radio = r.Bool();
        var opus = r.Rest();
        if (opus.Length is 0 or > 400)
            return;
        // Said aloud, it's heard outside: the Soot Children keep it (T40).
        if (speaker.State.Alive)
            World.Voices.Hear(speaker.Id, opus, World.Tick);
        var route = Route ?? World.Route;
        Func<double, bool>? tunnel = route is null ? null : route.InTunnel;
        Func<PlayerState, bool>? underground = World.Run is { } run ? s => run.Underground(s, Train) : null;
        // The radio's a thing (T41): no radio on you, nobody hears you on it, and you hear nobody.
        radio &= World.Bodies.HasRadio(speaker.Id);
        foreach (var listener in _crew)
        {
            if (listener == speaker)
                continue;
            var path = VoiceRouting.Route(speaker.State, listener.State, radio, Train, tunnel, World.Bodies.HasRadio(listener.Id), underground);
            if (path == VoicePath.None)
                continue;
            Messages.WriteVoiceDown(_voiceWriter, speaker.Id, seq, path, opus);
            _transport.Send(listener.Peer, _voiceWriter.Written, Delivery.Unreliable);
            VoiceFramesForwarded++;
        }
    }

    // The Soot Children's calls being played (T40): whose voice, the frames, how far through, and a sequence of their own.
    readonly List<Call> _calls = new();
    sealed class Call(int enemy, byte voice, IReadOnlyList<byte[]> frames)
    {
        public readonly int Enemy = enemy;
        public readonly byte Voice = voice;
        public readonly IReadOnlyList<byte[]> Frames = frames;
        public int Next;
        public double Due;
    }
    ushort _mimicSequence;

    /// <summary>
    /// Plays the Soot Children's calls (T40): the last thing the crewmate said, from the frames the host kept, at the
    /// voice's own pace (50 frames a second), from where the thing is. Everyone living within its call radius hears it at
    /// the same loudness (spec A.5: the missing falloff is the tell); walls still muffle it.
    /// </summary>
    void Mimic()
    {
        if (World.Enemies is not { } t)
            return;
        int frames = (int)Math.Round(t.SootChildren.CallSeconds * 1000 / 20);
        foreach (var (enemy, voice) in World.Calls)
            if (World.Voices.Utterance(voice, frames) is { Count: > 0 } said)
                _calls.Add(new Call(enemy, (byte)voice, said));
        foreach (var call in _calls)
        {
            var from = World.ActiveEnemies.FirstOrDefault(e => e.Id == call.Enemy && !e.Gone);
            if (from is null)
            {
                call.Next = call.Frames.Count;
                continue;
            }
            var at = from.WorldPosition(Train);
            for (call.Due += 50 * SimConstants.TickSeconds; call.Due >= 1 && call.Next < call.Frames.Count; call.Due--)
            {
                var opus = call.Frames[call.Next++];
                _mimicSequence++;
                foreach (var listener in _crew)
                {
                    if (!listener.State.Alive || (PlayerMotor.WorldPosition(listener.State, Train) - at).Length > t.SootChildren.CallRadius)
                        continue;
                    var path = VoicePath.Mimic | (PlayerMotor.Space(listener.State, Train) == PlayerMotor.Outside ? 0 : VoicePath.Occluded);
                    Messages.WriteVoiceDown(_voiceWriter, call.Voice, _mimicSequence, path, opus, call.Enemy);
                    _transport.Send(listener.Peer, _voiceWriter.Written, Delivery.Unreliable);
                    MimicFramesSent++;
                }
            }
        }
        _calls.RemoveAll(c => c.Next >= c.Frames.Count);
    }

    /// <summary>Soot Child frames sent, for the headless check.</summary>
    public long MimicFramesSent { get; private set; }

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

    /// <summary>
    /// Interest management (ARCHITECTURE §6.2): enemies and loose bodies farther than this from a client's player
    /// aren't sent to that client; the train, the crew and the world state always go. Set from the enemy tuning,
    /// which keeps it past the farthest audible tell. 0 (no enemies) sends everything.
    /// </summary>
    public double InterestRadius { get; set; }
    public long RecordsSkipped { get; private set; }

    void Broadcast(List<WireRecord> records)
    {
        foreach (var c in _crew)
        {
            var mine = Interest(records, c);
            c.Sent[Tick] = mine;
            c.Sent.Remove(Tick - HistoryTicks);
            // Delta against the newest snapshot the client has confirmed; full if that's gone from history.
            uint baseTick = c.AckedSnapshot;
            var baseline = baseTick > 0 ? c.Sent.GetValueOrDefault(baseTick) : null;
            if (baseline is null)
                baseTick = 0;
            Messages.WriteSnapshot(_writer, Tick, c.LastApplied, baseTick, mine, baseline);
            LastSnapshotBytes = _writer.Length;
            _transport.Send(c.Peer, _writer.Written, Delivery.Unreliable);
        }
    }

    readonly HashSet<uint> _far = new();

    List<WireRecord> Interest(List<WireRecord> records, Crew c)
    {
        if (InterestRadius <= 0)
            return records;
        var at = PlayerMotor.WorldPosition(c.State, Train);
        _far.Clear();
        foreach (var e in World.ActiveEnemies)
            if ((e.WorldPosition(Train) - at).Length > InterestRadius)
                _far.Add(WireRecord.MakeKey(RecordKind.Enemy, e.Id));
        foreach (var b in World.Bodies.All)
            if (!b.HeldBy(c.Id) && (Physics.Bodies.WorldCentre(b, Train) - at).Length > InterestRadius)
                _far.Add(WireRecord.MakeKey(RecordKind.Body, b.Id));
        if (_far.Count == 0)
            return records;
        RecordsSkipped += _far.Count;
        return records.Where(r => !_far.Contains(r.Key)).ToList();
    }
}
