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

    public void Step()
    {
        Receive();

        foreach (var c in _crew)
            c.ThisTick = NextIntent(c);

        World.BeginTick();
        World.LivingCrew = _crew.Count(c => c.State.Alive);
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
        World.StepHoldouts(id => _crew.FirstOrDefault(c => c.Id == id)?.State, (id, s) => _crew.First(c => c.Id == id).State = s,
            _crew.Select(c => (int)c.Id), _crew.Count, PlayerTuning);
        if (World.Run is not null)
            World.StepRun([.. _crew.Select(c => c.State)]);
        SpawnTheQueueAtTheFortress();
        WatchSpawns();
        SendTheReport();
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
                        // App. D.2 "drop-out": an inert body with no fee; a waiting player leaves the queue (D.6).
                        World.DroppedOut(gone.Id, gone.State);
                        _crew.Remove(gone);
                    }
                    break;
                case TransportEventKind.Data when e.Payload is { Length: > 0 } payload:
                    OnData(e.Peer, payload);
                    break;
            }
        }
    }

    /// <summary>
    /// GDD App. D.3. Before the gates open (or on a line with no run), a joiner is aboard at once, at the fortress. Once the
    /// run has left the gate, a Holdout is the only way in: the joiner goes to the back of the respawn queue as a lobbied
    /// player, with the Line Plan (the Welcome's session: every machine generates the same line) and the state (snapshots)
    /// at once, so they can watch.
    /// </summary>
    /// <remarks>
    /// A night that starts away from the fortress (resumed from its autosave, spec E) has its run start where it is: with
    /// nobody aboard there'd be nobody to free a Holdout, so a joiner with no crew aboard boards as at run start
    /// (ARCHITECTURE §8 note 93).
    /// </remarks>
    void Join(PeerId peer)
    {
        byte id = _nextId++;
        Messages.WriteWelcome(_writer, id, Tick, SessionInfo);
        _transport.Send(peer, _writer.Written, Delivery.ReliableOrdered);
        var c = new Crew(id, peer);
        bool crewAboard = _crew.Any(x => !x.State.Has(PlayerFlags.Lobbied));
        if (World.Run is { Phase: not Run.RunPhase.Yard } run && crewAboard)
        {
            c.State = Lobbied(Train);
            World.Holdouts?.Queue.Lobbied(id, run.Seconds);
        }
        else
        {
            c.State = AtTheFortress(_crew.Count);
            World.GiveStandardKit(id, c.State);
            if (World.Run is { Phase: not Run.RunPhase.Yard })
                _sessionStart.Add(id);
        }
        _crew.Add(c);
    }

    /// <summary>
    /// A lobbied player (D.3): not in the crew, not dead, no body; stood (for the record) where the engine is, since
    /// nothing of theirs is simulated until a Holdout frees them.
    /// </summary>
    public static PlayerState Lobbied(TrainOnLine train) => new()
    {
        Parent = PlayerState.World,
        Position = train.Frames[0].Origin,
        Surface = Surface.Ground,
        LineHint = train.Dynamics.Distance,
        Flags = PlayerFlags.Lobbied,
    };

    /// <summary>At the fortress (D.3 "run start"): first aboard takes the cab; everyone else spreads down the train.</summary>
    PlayerState AtTheFortress(int n)
    {
        int car = 1 + (n - 1) % Math.Max(1, Train.Frames.Count - 1);
        return n == 0 ? PlayerMotor.SpawnInCab(Train, PlayerTuning) : PlayerMotor.SpawnOnRoof(Train, car, 0, PlayerTuning);
    }

    /// <summary>
    /// D.3 "run start: every player in the session, including everyone in the queue, spawns at the fortress. The queue is
    /// empty when the gates open." In the yard, anyone waiting (dead in the yard, or joined) is back aboard at once.
    /// </summary>
    void SpawnTheQueueAtTheFortress()
    {
        if (World.Run is not { Phase: Run.RunPhase.Yard })
            return;
        for (int i = 0; i < _crew.Count; i++)
        {
            var c = _crew[i];
            if (c.State.Alive)
                continue;
            var back = AtTheFortress(i);
            back.Placed = (byte)(c.State.Placed + 1);
            c.State = back;
            World.Holdouts?.Queue.Remove(c.Id);
            World.GiveStandardKit(c.Id, back);
        }
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
            if (type == MessageType.Hello)
            {
                // Who they are beyond this session: their character, if the campaign has one for them (App. D.8).
                string profile = r.Str();
                if (profile.Length is > 0 and <= 64 && !Profiles.ContainsValue(profile))
                {
                    Profiles[c.Id] = profile;
                    if (CharacterOf?.Invoke(profile) is { } character)
                        World.Characters[c.Id] = character;
                }
                return;
            }
            if (type == MessageType.Request)
            {
                // GDD App. D: the dead phase's asks. Checked against the rules; what's refused is simply not done.
                var q = Messages.ReadRequest(ref r);
                Requests.Add((c.Id, q, World.Request(c.Id, q, [.. _crew.Select(x => ((int)x.Id, x.State))])));
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

    /// <summary>Each player's profile id, from their Hello (App. D.8).</summary>
    public Dictionary<byte, string> Profiles { get; } = new();
    /// <summary>The campaign's character for a profile, if it has one (the host's save, App. D.8). Null: none kept.</summary>
    public Func<string, Run.Character?>? CharacterOf { get; set; }

    /// <summary>The characters this session's players are now, by profile id: what the host's campaign save keeps (D.8).</summary>
    public IReadOnlyDictionary<string, Run.Character> CharactersByProfile() =>
        World.Characters.Where(c => Profiles.ContainsKey((byte)c.Key)).ToDictionary(c => Profiles[(byte)c.Key], c => c.Value);

    /// <summary>
    /// Every time a player came (back) to life, wherever it came from: the audit behind GDD App. D.14 "no open-world
    /// spawns" (every mid-run spawn inside a Holdout volume). Recorded by watching states, not by trusting the paths that
    /// make them.
    /// </summary>
    public List<SpawnRecord> Spawns { get; } = new();
    readonly HashSet<byte> _alive = new();
    readonly HashSet<byte> _sessionStart = new();

    void WatchSpawns()
    {
        foreach (var c in _crew)
        {
            if (!c.State.Alive)
            {
                _alive.Remove(c.Id);
                continue;
            }
            if (!_alive.Add(c.Id))
                continue;
            var at = PlayerMotor.WorldPosition(c.State, Train);
            Spawns.Add(new SpawnRecord(Tick, c.Id, at, World.Run?.Phase, World.Holdouts?.All.FirstOrDefault(h => h.Inside(at))?.Id,
                _sessionStart.Remove(c.Id)));
        }
    }

    /// <summary>The night's incident report (App. D.12), once the run's over: made then, and again with each commendation.</summary>
    public Run.IncidentReport? Report { get; private set; }
    ushort _reportRevision;

    /// <summary>
    /// At the run's end, the incident report to everyone (D.12), reliably; again whenever a commendation's given, so every
    /// run-end screen shows them as they come, and whenever the session changes (a joiner gets it, and can give one too).
    /// </summary>
    void SendTheReport()
    {
        if (World.Run is not { Over: true, Report: not null })
            return;
        var session = _crew.Select(c => (int)c.Id).Order().ToList();
        if (Report is not null && Report.Commendations.Count == World.Commendations.Count && Report.Session.SequenceEqual(session))
            return;
        Report = Run.IncidentReport.Of(World, session);
        _reportRevision++;
        var bytes = Report.Compress();
        int parts = Math.Max(1, (bytes.Length + Messages.ReportPartBytes - 1) / Messages.ReportPartBytes);
        if (parts > byte.MaxValue)
            throw new InvalidOperationException($"an incident report of {bytes.Length} bytes is too big to send");
        foreach (var c in _crew)
            for (int i = 0; i < parts; i++)
            {
                int at = i * Messages.ReportPartBytes;
                Messages.WriteReport(_writer, _reportRevision, i, parts, bytes.AsSpan(at, Math.Min(Messages.ReportPartBytes, bytes.Length - at)));
                _transport.Send(c.Peer, _writer.Written, Delivery.ReliableOrdered);
            }
    }

    /// <summary>Every request this session, and whether it was allowed (the harness and tests read it).</summary>
    public List<(byte Player, Request Request, bool Allowed)> Requests { get; } = new();

    /// <summary>
    /// Where a listener hears from (GDD App. D.10): their own place, or, dead or lobbied, the living crewmate they watch
    /// ("exactly what the followed player hears": their proximity mix, their radio if they have one).
    /// </summary>
    (PlayerState State, int Radio) Ears(Crew listener)
    {
        if (listener.State.Alive)
            return (listener.State, listener.Id);
        int target = World.Dead.FollowedBy(listener.Id);
        return _crew.Find(x => x.Id == target) is { } followed ? (followed.State, followed.Id) : (listener.State, listener.Id);
    }

    /// <summary>Route for tunnels (radio dies in them). Defaults to the world's.</summary>
    public Route.Route? Route { get; set; }
    public long VoiceFramesForwarded { get; private set; }

    /// <summary>
    /// Forwards a voice frame to exactly the listeners it reaches (spec A.5; GDD App. D.10). Never back to the speaker. The
    /// dead and lobbied hear what the crewmate they watch hears, and each other on the dead channel, which the living never
    /// get. A dead player on a Holdout's Live Mic (D.7) is heard from the Holdout on the proximity layer too.
    /// </summary>
    void ForwardVoice(Crew speaker, ref NetReader r)
    {
        ushort seq = r.U16();
        bool radio = r.Bool();
        var opus = r.Rest();
        if (opus.Length is 0 or > 400)
            return;
        // Said aloud, it's heard outside: the Soot Children keep it (T40). The dead say nothing aloud (D.7: the Live Mic
        // isn't talking to anything that listens for it).
        if (speaker.State.Alive)
            World.Voices.Hear(speaker.Id, opus, World.Tick);
        var route = Route ?? World.Route;
        Func<double, bool>? tunnel = route is null ? null : route.InTunnel;
        Func<PlayerState, bool>? underground = World.Run is { } run ? s => run.Underground(s, Train) : null;
        // The radio's a thing (T41): no radio on you, nobody hears you on it, and you hear nobody.
        radio &= World.Bodies.HasRadio(speaker.Id);
        var mic = speaker.State.Alive ? null : Run.DeadPhase.LiveMicOf(World, speaker.Id);
        foreach (var listener in _crew)
        {
            if (listener == speaker)
                continue;
            var (ears, radioOf) = Ears(listener);
            var path = speaker.State.Alive
                ? VoiceRouting.Route(speaker.State, ears, radio, Train, tunnel, World.Bodies.HasRadio(radioOf), underground)
                // The dead hear the dead on their own channel; the Live Mic is for the living at the door.
                : listener.State.Alive ? mic is null ? VoicePath.None : VoiceRouting.FromHoldout(mic.Door, ears, Train) : VoiceRouting.Dead(listener.State);
            if (path == VoicePath.None)
                continue;
            // What's holding the speaker changes how they sound (App. C.8): muffled under a hand, fading as they're drained.
            var (muffled, gain) = World.VoiceEffect(speaker.Id);
            if (muffled)
                path |= VoicePath.Muffled;
            if (gain < 1)
                path |= VoicePath.Fading;
            Messages.WriteVoiceDown(_voiceWriter, speaker.Id, seq, path, opus, path.HasFlag(VoicePath.Holdout) ? mic!.Index : 0, gain);
            _transport.Send(listener.Peer, _voiceWriter.Written, Delivery.Unreliable);
            VoiceFramesForwarded++;
            if (!listener.State.Alive && path.HasFlag(VoicePath.Dead))
                DeadFramesForwarded++;
            if (listener.State.Alive && path.HasFlag(VoicePath.Dead))
                DeadFramesToTheLiving++;
        }
    }

    /// <summary>Dead-channel frames sent to the dead and lobbied, and (which must stay 0, D.14) to the living.</summary>
    public long DeadFramesForwarded { get; private set; }
    public long DeadFramesToTheLiving { get; private set; }

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
        _far.Clear();
        // A Follower is never sent to whoever it's following (App. A.3: "visible ONLY to other players, never to the
        // carrier"): not drawn, not heard, not there at all on their machine. Nested in a car, it's off their back, and
        // anyone's to see.
        foreach (var e in World.ActiveEnemies)
            if (e is Enemies.Follower { Nested: false } f && f.Carrier == c.Id)
                _far.Add(WireRecord.MakeKey(RecordKind.Enemy, e.Id));
        // GDD App. D.6 "the living see nothing; roll call stays verbal": the queue goes to the dead and lobbied only.
        if (c.State.Alive)
        {
            _far.Add(WireRecord.MakeKey(RecordKind.Queue, 0));
            // D.11: the vote's hidden from the living until the run-end screen.
            _far.Add(WireRecord.MakeKey(RecordKind.Votes, 0));
        }
        // Whom each of the dead watches is theirs alone.
        foreach (var other in _crew)
            if (other != c)
                _far.Add(WireRecord.MakeKey(RecordKind.Spectate, other.Id));
        if (InterestRadius <= 0)
            return _far.Count == 0 ? records : records.Where(r => !_far.Contains(r.Key)).ToList();
        // The dead see what the one they watch sees.
        var at = PlayerMotor.WorldPosition(Ears(c).State, Train);
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

/// <summary>A player come (back) to life on the host (GDD App. D.14 "no open-world spawns").</summary>
/// <param name="Phase">The run's phase then (null: a line with no run). Anything but the yard is mid-run.</param>
/// <param name="Holdout">The Holdout whose volume it was inside, if any.</param>
/// <param name="SessionStart">The first aboard a night started away from the fortress (a resumed save): its run start.</param>
public sealed record SpawnRecord(uint Tick, byte Player, Ballast.Double3 At, Run.RunPhase? Phase, string? Holdout, bool SessionStart)
{
    public bool MidRun => Phase is not (null or Run.RunPhase.Yard);
}
