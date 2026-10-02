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
        // With steam driving (T97), a night's train stands at the gate on its brake.
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

    /// <summary>Every Holdout assigned, freed, released or called out from this session, oldest first (GDD App. D).</summary>
    public List<Run.HoldoutEvent> HoldoutEvents { get; } = new();

    public void Step()
    {
        Receive();
        BoardWaiting();

        foreach (var c in _crew)
            c.ThisTick = NextIntent(c);

        World.BeginTick();
        // The brake is held, so it's cleared each tick and re-applied by whoever's holding it; with nobody at the controls
        // it stays where it was left (a driver who gets down with it on leaves the train standing on it), as the throttle
        // does and as a predicting client assumes.
        // A train that stops on it stands on it (T97: with steam driving, off the brake a standing engine pulls away) until
        // the driver lets it off (a notch up: CabControls.ReleasesBrake).
        if (_crew.Any(c => CabControls.CanDrive(c.State, Train)) && CabControls.Clears(Controls, Train, _crew.Any(c => CabControls.ReleasesBrake(c.ThisTick, c.State, Train))))
            Controls.Brake = 0;
        foreach (var c in _crew)
        {
            // App. C.9 "who was on the throttle": whoever's working the cab's controls, or failing that anyone at them.
            if (CabControls.CanDrive(c.State, Train) && (c.ThisTick.ThrottleNotch != 0 || c.ThisTick.Has(PlayerButtons.Brake) || World.Attribution.Driver < 0
                || !_crew.Any(o => o.Id == World.Attribution.Driver && CabControls.CanDrive(o.State, Train))))
                World.Attribution.Drove(c.Id);
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
        if (World.Holdouts is { } holdouts)
            holdouts.StartingKit = PlayerTuning.StartingKit;
        HoldoutEvents.AddRange(World.StepHoldouts([.. _crew.Select(c => ((int)c.Id, c.State))], (id, st) => _crew.First(c => c.Id == id).State = st));
        if (World.Run is not null)
            World.StepRun([.. _crew.Select(c => c.State)]);
        Tick++;
        SendNamesAndReport();

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
                        if (gone.State.Alive)
                            World.Bodies.DropOut(Train, gone.Id, gone.State);
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
        _namesChanged |= World.Names.Count > 0;
        // GDD App. D.3: with Holdouts, a mid-run joiner goes straight into the respawn queue; otherwise they wait for a stop.
        if (!Lobbying && CanBoard is { } can && !can())
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
        // Past the gate, nobody spawns aboard (D.1): they watch, waiting in the queue, until a Holdout frees them.
        if (Lobbying && _crew.Count > 0)
            c.State = c.State with { Health = 0, Death = DeathCause.Waiting };
        _crew.Add(c);
    }

    /// <summary>The run's under way with Holdouts: someone joining now joins the respawn queue (GDD App. D.3).</summary>
    bool Lobbying => World.Holdouts is not null && World.Run is { Phase: not Run.RunPhase.Yard };

    /// <summary>Boards anyone waiting, once the train is somewhere they can board it.</summary>
    void BoardWaiting()
    {
        if (_waiting.Count == 0 || CanBoard is { } can && !can())
            return;
        foreach (var (id, peer) in _waiting)
            Board(id, peer);
        _waiting.Clear();
    }

    bool _namesChanged;
    bool _reportSent;

    /// <summary>
    /// Names to everyone when they change (and to whoever's just joined), and the night's report once it's over (GDD v1.4
    /// App. D.12): the report is only the host's to write, so clients are sent it, in chunks, reliably.
    /// </summary>
    void SendNamesAndReport()
    {
        var peers = _crew.Select(c => c.Peer).Concat(_waiting.Select(w => w.Peer)).Distinct().ToList();
        if (_namesChanged)
        {
            _namesChanged = false;
            Messages.WriteNames(_writer, World.Names);
            foreach (var p in peers)
                _transport.Send(p, _writer.Written, Delivery.ReliableOrdered);
        }
        if (!_reportSent && World.Run?.Report is { } report)
        {
            _reportSent = true;
            foreach (var m in Messages.ReportMessages(report))
                foreach (var p in peers)
                    _transport.Send(p, m, Delivery.ReliableOrdered);
        }
    }

    void OnData(PeerId peer, byte[] payload)
    {
        // A name can come from someone still waiting to board.
        if (payload.Length > 0 && payload[0] == (byte)MessageType.Hello)
        {
            int id = _crew.Find(x => x.Peer == peer)?.Id ?? _waiting.Where(w => w.Peer == peer).Select(w => (int)w.Id).DefaultIfEmpty(-1).First();
            try
            {
                var hr = new NetReader(payload);
                hr.U8();
                if (id >= 0 && Messages.CleanName(hr.Str()) is { Length: > 0 } name)
                {
                    World.Names[id] = name;
                    _namesChanged = true;
                }
            }
            catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException)
            {
            }
            return;
        }
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
            // A dead listener hears the living through whoever they watch (App. D.10: "exactly what the followed player
            // hears"); the dead channel stays theirs.
            var ears = speaker.State.Alive ? EarsOf(listener) : listener;
            var path = VoiceRouting.Route(speaker.State, ears.State, radio, Train, tunnel, World.Bodies.HasRadio(ears.Id), underground);
            if (path == VoicePath.None)
                continue;
            // What's holding the speaker changes how they sound (App. C.8): muffled under a hand, fading as they're drained.
            var (muffled, gain) = World.VoiceEffect(speaker.Id);
            if (muffled)
                path |= VoicePath.Muffled;
            if (gain < 1)
                path |= VoicePath.Fading;
            Messages.WriteVoiceDown(_voiceWriter, speaker.Id, seq, path, opus, gain: gain);
            _transport.Send(listener.Peer, _voiceWriter.Written, Delivery.Unreliable);
            VoiceFramesForwarded++;
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
        if (i.Lamp > LampSwitch.Off)
            i.Lamp = LampSwitch.None;
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
            // Delta against the newest snapshot the client has confirmed; full if that's gone from history.
            uint baseTick = c.AckedSnapshot;
            var baseline = baseTick > 0 ? c.Sent.GetValueOrDefault(baseTick) : null;
            if (baseline is null)
                baseTick = 0;
            Messages.WriteSnapshot(_writer, Tick, c.LastApplied, baseTick, mine, baseline);
            if (_writer.Length > MaxSnapshotBytes)
            {
                mine = Budget(mine, baseline, c, baseTick);
                SnapshotsBudgeted++;
            }
            c.Sent[Tick] = mine;
            c.Sent.Remove(Tick - HistoryTicks);
            LastSnapshotBytes = _writer.Length;
            MaxSnapshotBytesSent = Math.Max(MaxSnapshotBytesSent, _writer.Length);
            _transport.Send(c.Peer, _writer.Written, Delivery.Unreliable);
        }
    }

    /// <summary>
    /// What one snapshot may take: a datagram (the transport never fragments, and refuses anything bigger). A world that
    /// doesn't fit in one (a joiner's first, full snapshot of a long train with its bodies, stops and Holdouts) goes over a
    /// few ticks instead (<see cref="Budget"/>).
    /// </summary>
    public int MaxSnapshotBytes { get; set; } = DatagramTransport<object>.MaxPayload;
    /// <summary>Snapshots that didn't fit and went on a budget, and the largest sent: for the harness and tests.</summary>
    public int SnapshotsBudgeted { get; private set; }
    public int MaxSnapshotBytesSent { get; private set; }

    /// <summary>
    /// A snapshot that fits: what the client has and is unchanged costs nothing in a delta and stays; what's changed or new
    /// goes in, most needed first (the players, this one's own first, then the train), while it fits. A changed record that
    /// doesn't fit goes as the client's old copy (so it isn't a removal), a new one next time. What's returned is what the
    /// client will hold, so the next delta is against exactly that. Leaves the snapshot written in <c>_writer</c>.
    /// </summary>
    List<WireRecord> Budget(List<WireRecord> mine, IReadOnlyList<WireRecord>? baseline, Crew c, uint baseTick)
    {
        var old = baseline?.ToDictionary(r => r.Key) ?? new Dictionary<uint, WireRecord>();
        var sent = new SortedDictionary<uint, WireRecord>();
        var pending = new List<WireRecord>();
        foreach (var r in mine)
        {
            if (old.TryGetValue(r.Key, out var b))
                sent[r.Key] = b;
            if (!old.TryGetValue(r.Key, out b) || !r.SameAs(b))
                pending.Add(r);
        }
        int Priority(WireRecord r) => r.Kind switch
        {
            RecordKind.Player => r.Id == c.Id ? 0 : 1,
            RecordKind.Rake or RecordKind.Vehicle or RecordKind.Boiler or RecordKind.Controls => 2,
            RecordKind.World or RecordKind.Run => 3,
            _ => 4,
        };
        foreach (var r in pending.OrderBy(Priority).ThenBy(r => r.Key))
        {
            bool had = sent.TryGetValue(r.Key, out var was);
            sent[r.Key] = r;
            Messages.WriteSnapshot(_writer, Tick, c.LastApplied, baseTick, [.. sent.Values], baseline);
            if (_writer.Length <= MaxSnapshotBytes)
                continue;
            // That one didn't fit: back to what the client has, and the rest wait for the next snapshot.
            if (had)
                sent[r.Key] = was;
            else
                sent.Remove(r.Key);
            break;
        }
        var list = sent.Values.ToList();
        Messages.WriteSnapshot(_writer, Tick, c.LastApplied, baseTick, list, baseline);
        return list;
    }

    readonly HashSet<uint> _far = new();

    /// <summary>
    /// Whose eyes and ears a client has (GDD App. D.10): their own, or, dead or waiting to board, the living crewmate they
    /// say they watch. Someone they can't watch (alive themselves, or the crewmate dead or gone) leaves them their own.
    /// </summary>
    Crew EarsOf(Crew c)
    {
        if (c.State.Alive || c.LastIntent.Watch == 0 || c.LastIntent.Watch == c.Id)
            return c;
        foreach (var other in _crew)
            if (other.Id == c.LastIntent.Watch)
                return other.State.Alive ? other : c;
        return c;
    }

    List<WireRecord> Interest(List<WireRecord> records, Crew c)
    {
        _far.Clear();
        // A Follower is never sent to whoever it's following (App. A.3: "visible ONLY to other players, never to the
        // carrier"): not drawn, not heard, not there at all on their machine. Nested in a car, it's off their back, and
        // anyone's to see. Watching the carrier, you see what they see: nothing on their back.
        var eyes = EarsOf(c);
        foreach (var e in World.ActiveEnemies)
            if (e is Enemies.Follower { Nested: false } f && (f.Carrier == c.Id || f.Carrier == eyes.Id))
                _far.Add(WireRecord.MakeKey(RecordKind.Enemy, e.Id));
        if (InterestRadius <= 0)
            return _far.Count == 0 ? records : records.Where(r => !_far.Contains(r.Key)).ToList();
        // Watching someone (App. D.10), a dead player is sent what's around them: they see it through their eyes.
        var at = PlayerMotor.WorldPosition(eyes.State, Train);
        foreach (var e in World.ActiveEnemies)
            if (!e.Far && (e.WorldPosition(Train) - at).Length > InterestRadius)
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
