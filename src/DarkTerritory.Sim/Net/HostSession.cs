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
        /// <summary>Whose link it is: a player who comes back on a new connection (note 253) moves to it.</summary>
        public PeerId Peer = peer;
        public readonly SortedDictionary<uint, PlayerIntent> Pending = new();
        public uint LastApplied;
        /// <summary>Newest snapshot this client says it has decoded: the baseline for its next delta.</summary>
        public uint AckedSnapshot;
        public PlayerIntent LastIntent;
        public PlayerIntent ThisTick;
        /// <summary>Bookmark held on the tick before (the press is what counts, App. D.12).</summary>
        public bool Bookmarking;
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
            World.EmoteTuning = value.Emotes;
            World.Bodies.FullHealth = value.Health; // a healing find is used only short of it (note 272)
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
        Greet();
        Expire();
        HangUp();
        BoardWaiting();

        foreach (var c in _crew)
        {
            c.ThisTick = NextIntent(c);
            // Off the rails, the living are the wreck's till its hit kills them (App. E.2 step 1): only the skip vote counts.
            if (World.Wrecked(c.State))
                c.ThisTick = World.WreckedIntent(c.ThisTick);
        }

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
        Bookmark();
        Votes();
        // E.5 "Skipping": a majority of the session, or the host, skips the film to the cause card, or the Stranded outro
        // (E.9). When a vote counts at all is the clients' to say: they only offer it after the first player's shot, or three
        // seconds into the outro. Once skipped, it stays skipped. With each player's own skip (wreck.json "skip", note 311)
        // there's no vote: each client skips its own, and the host has nothing to count.
        if (!World.WreckTuning.Skip.Own && (World.Film is not null || World.Run?.End == Run.RunEnd.Stranded) && _crew.Count > 0)
        {
            int votes = _crew.Count(c => c.ThisTick.Has(PlayerActions.Skip));
            World.FilmVotes = (votes, _crew.Count);
            if (votes * 2 > _crew.Count || _crew.Any(c => c.Id == HostPlayer && c.ThisTick.Has(PlayerActions.Skip)))
                World.FilmSkipped = true;
        }
        World.ApplyDamage(id => _crew.FirstOrDefault(c => c.Id == id)?.State, (id, s) => _crew.First(c => c.Id == id).State = s, _crew.Select(c => (int)c.Id));
        CutTheDead();
        foreach (var c in _crew)
            if (!World.Wrecked(c.State))
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
                    // Welcomed once it's said hello, with the token if it's coming back (note 253), or after greetSeconds.
                    if (!_greeting.Any(g => g.Peer == e.Peer))
                        _greeting.Add(new Greeting(e.Peer, Tick));
                    break;
                case TransportEventKind.Disconnected:
                    Gone(e.Peer);
                    break;
                case TransportEventKind.Data when e.Payload is { Length: > 0 } payload:
                    OnData(e.Peer, payload);
                    break;
            }
        }
    }

    /// <summary>
    /// Cuts a player's link from the host's end, as if their connection had failed (a test's, or the screenshot's, stand-in
    /// for a dropped joiner): their crewmate goes limp and their place is held, as for any drop (note 253).
    /// </summary>
    public void Drop(byte id)
    {
        PeerId? found = _crew.Find(c => c.Id == id)?.Peer ?? _waiting.Where(w => w.Id == id).Select(w => (PeerId?)w.Peer).FirstOrDefault();
        if (found is not { } peer)
            return;
        _transport.Disconnect(peer);
        Gone(peer);
    }

    /// <param name="hold">A dropped link: the place is held for a rejoin (note 253). False, the player said Leave (they quit
    /// on purpose): the place frees now, its token no good, and a full crew has room again (note 254).</param>
    void Gone(PeerId peer, bool hold = true)
    {
        _greeting.RemoveAll(g => g.Peer == peer);
        _refused.RemoveAll(r => r.Peer == peer);
        // Spec E: "Character remains as an inert body until recovered or the run ends." No bot takes over: the
        // crewmate goes limp where they stood, and their place is held for them (note 253).
        foreach (var gone in _crew.Where(c => c.Peer == peer).ToList())
        {
            if (gone.State.Alive)
                World.Bodies.DropOut(Train, gone.Id, gone.State);
            _crew.Remove(gone);
            if (hold)
                Reserve(gone.Id, peer, gone.State, aboard: true, gone.LastApplied, gone.MissedInputs);
            else
                Free(gone.Id);
        }
        foreach (var (id, _) in _waiting.Where(w => w.Peer == peer).ToList())
        {
            if (hold)
                Reserve(id, peer, default, aboard: false, 0, 0);
            else
                Free(id);
        }
        _waiting.RemoveAll(w => w.Peer == peer);
    }

    /// <summary>A place given up for good (a Leave): its token's no good, so nobody gets it back (note 254).</summary>
    void Free(byte id)
    {
        _tokens.Remove(id);
        Leaves++;
    }

    /// <summary>
    /// The crew cap (player.json crew.cap; note 254). Every place that's someone's counts: the crew in the world, living and
    /// dead (the dead are still the crew's: the dead channel, the vote, the queue), those welcomed and waiting to board at a
    /// stop, and the places held for the dropped. Bots are crew like anyone. A joiner past it is turned away, but never
    /// one coming back with a held place's token.
    /// </summary>
    public int Cap => PlayerTuning.Crew.Places;
    /// <summary>The places taken against <see cref="Cap"/>: aboard (living or dead), waiting at a stop, and held.</summary>
    public int Occupied => _crew.Count + _waiting.Count + _reserved.Count;
    /// <summary>No room for a new joiner: a lobby's listed FULL and its platform lobby shut while this holds.</summary>
    public bool Full => Occupied >= Cap;
    /// <summary>Joiners turned away for a full crew; players who left on purpose and gave their place up.</summary>
    public int Refusals { get; private set; }
    public int Leaves { get; private set; }

    /// <summary>Links turned away (note 254), kept open till their Refused has had time to get there, then hung up on.</summary>
    readonly List<(PeerId Peer, uint Until)> _refused = [];
    /// <summary>Links turned away and not yet hung up on.</summary>
    public int TurnedAway => _refused.Count;

    void Refuse(PeerId peer)
    {
        Refusals++;
        Messages.WriteRefused(_writer, new Refusal(RefusalReason.CrewFull, Occupied, Cap));
        _transport.Send(peer, _writer.Written, Delivery.ReliableOrdered);
        _refused.Add((peer, Tick + (uint)TicksOf(PlayerTuning.Crew.RefuseLingerSeconds)));
    }

    /// <summary>Hangs up on the refused whose refusal has had its time (the client usually hangs up first).</summary>
    void HangUp()
    {
        foreach (var (peer, _) in _refused.Where(r => Tick >= r.Until).ToList())
        {
            _refused.RemoveAll(r => r.Peer == peer);
            _transport.Disconnect(peer);
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

    byte Join(PeerId peer)
    {
        byte id = _nextId++;
        ulong token = NewToken();
        _tokens[id] = token;
        Messages.WriteWelcome(_writer, id, Tick, SessionInfo, token);
        _transport.Send(peer, _writer.Written, Delivery.ReliableOrdered);
        _namesChanged |= World.Names.Count > 0;
        Admit(id, peer);
        return id;
    }

    /// <summary>A welcomed player aboard, or (GDD App. D.3, spec E) told to wait for a stop.</summary>
    void Admit(byte id, PeerId peer)
    {
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

    /// <summary>A new connection, waiting to be welcomed: its Hello (the name, a token) once it's said it.</summary>
    sealed class Greeting(PeerId peer, uint since)
    {
        public readonly PeerId Peer = peer;
        public readonly uint Since = since;
        public bool Said;
        /// <summary>Quiet past greetSeconds: out of the line's way, and welcomed only once it does say hello.</summary>
        public bool Late;
        public string Name = "";
        public ulong Token;
        public byte Outfit = Messages.NoOutfit;
    }

    readonly List<Greeting> _greeting = [];

    /// <summary>
    /// A dropped player's place (spec E drop-out, note 253): who they were, the token that gets it back, and their state when
    /// the link went (the dead stay dead). Held till <see cref="Until"/>, the host's tick.
    /// </summary>
    sealed record Seat(byte Id, PeerId Peer, ulong Token, PlayerState State, bool Aboard, uint LastApplied, int MissedInputs, uint Until);

    readonly List<Seat> _reserved = [];
    /// <summary>Every slot's token while it's someone's: in the crew, waiting to board, or held for them.</summary>
    readonly Dictionary<byte, ulong> _tokens = [];

    /// <summary>Where slot tokens come from: random, so nobody guesses another's way back in. A test can make them plain.</summary>
    public Func<ulong> Tokens { get; set; } = () => BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));

    ulong NewToken()
    {
        ulong t;
        do
            t = Tokens();
        while (t == 0 || _tokens.ContainsValue(t));
        return t;
    }

    /// <summary>Places held for players whose link dropped (note 253), and whether one is held for this id.</summary>
    public int Reserved => _reserved.Count;
    public bool IsReserved(byte id) => _reserved.Any(r => r.Id == id);
    /// <summary>Players who came back to their own slot (a held place, or a live one from a new connection); places that ran out.</summary>
    public int Rejoins { get; private set; }
    public int ReservesExpired { get; private set; }

    int TicksOf(double seconds) => (int)Math.Ceiling(seconds * SimConstants.TickRate);

    void Reserve(byte id, PeerId peer, PlayerState state, bool aboard, uint lastApplied, int missed)
    {
        if (!_tokens.TryGetValue(id, out ulong token))
            return;
        _reserved.RemoveAll(r => r.Id == id);
        _reserved.Add(new Seat(id, peer, token, state, aboard, lastApplied, missed, Tick + (uint)TicksOf(PlayerTuning.Rejoin.ReserveSeconds)));
    }

    /// <summary>Places held past reserveSeconds are let go: the slot's free, its token no good, the body stays (D.2).</summary>
    void Expire()
    {
        foreach (var seat in _reserved.Where(r => Tick >= r.Until).ToList())
        {
            _reserved.Remove(seat);
            _tokens.Remove(seat.Id);
            ReservesExpired++;
        }
    }

    /// <summary>
    /// Lets the greeting line in, in the order they connected (first aboard takes the cab), each once it's said hello. One
    /// quiet for greetSeconds stops holding up the rest, and is welcomed whenever it does speak. It's never let in silent:
    /// a redial given up on before it spoke (its socket closed, the host's Accept unheard) is a connection nobody's
    /// behind, and welcomed it was a phantom crewmate (note 253). The link's timeout takes it instead.
    /// A Hello with a held slot's token gets that slot back.
    /// </summary>
    void Greet()
    {
        int patience = TicksOf(PlayerTuning.Rejoin.GreetSeconds);
        for (int i = 0; i < _greeting.Count;)
        {
            var g = _greeting[i];
            if (!g.Said)
            {
                if (!g.Late && Tick - g.Since < patience)
                    break;
                g.Late = true;
                i++;
                continue;
            }
            _greeting.RemoveAt(i);
            // A held place's token always gets back in: its place is already counted (note 254). A new joiner (or a token
            // whose place ran out) needs room. The greeting line itself takes no place: a connection that never speaks is
            // never counted, so it can't fill the crew or shut the lobby.
            byte? back = g.Token != 0 ? Return(g.Peer, g.Token) : null;
            if (back is null && Full)
            {
                Refuse(g.Peer);
                continue;
            }
            byte id = back ?? Join(g.Peer);
            Name(id, g.Name);
            Wear(id, g.Outfit, any: true);
        }
    }

    /// <summary>
    /// Note 253: a player back with their slot's token. Held for them, they get it back; still live on another connection
    /// (the host hadn't yet seen that link go), the new one takes over from the old. Null if the token's no one's.
    /// </summary>
    byte? Return(PeerId peer, ulong token)
    {
        if (_reserved.Find(r => r.Token == token) is { } seat)
        {
            _reserved.Remove(seat);
            Rejoins++;
            Welcome(seat.Id, peer, token);
            if (!seat.Aboard)
            {
                Admit(seat.Id, peer);
                return seat.Id;
            }
            var c = new Crew(seat.Id, peer) { LastApplied = seat.LastApplied, MissedInputs = seat.MissedInputs };
            if (Back(seat) is { } state)
            {
                state.Placed = (byte)(seat.State.Placed + 1);
                c.State = state;
                int at = _crew.FindIndex(x => x.Id > seat.Id);
                _crew.Insert(at < 0 ? _crew.Count : at, c);
            }
            else
                Admit(seat.Id, peer);
            return seat.Id;
        }
        if (_crew.Find(x => _tokens.GetValueOrDefault(x.Id) == token) is { } live)
        {
            Rejoins++;
            var old = live.Peer;
            live.Peer = peer;
            live.Pending.Clear();
            live.Sent.Clear();
            live.AckedSnapshot = 0;
            if (old != peer)
                _transport.Disconnect(old);
            Welcome(live.Id, peer, token);
            return live.Id;
        }
        int waiting = _waiting.FindIndex(w => _tokens.GetValueOrDefault(w.Id) == token);
        if (waiting >= 0)
        {
            var (id, old) = _waiting[waiting];
            _waiting[waiting] = (id, peer);
            if (old != peer)
                _transport.Disconnect(old);
            Rejoins++;
            Welcome(id, peer, token);
            Messages.WriteWait(_writer, WaitReason);
            _transport.Send(peer, _writer.Written, Delivery.ReliableOrdered);
            return id;
        }
        return null;
    }

    /// <summary>
    /// The Welcome to a returning player, and what they'd have been sent while they were gone: everyone's names and looks,
    /// the commendations, the film and the report if they've gone out (GDD v1.4 App. E.8: "a client that drops sees the
    /// incident report on rejoin"), and their ballot, offered afresh.
    /// </summary>
    void Welcome(byte id, PeerId peer, ulong token)
    {
        Messages.WriteWelcome(_writer, id, Tick, SessionInfo, token);
        _transport.Send(peer, _writer.Written, Delivery.ReliableOrdered);
        _namesChanged = true;
        _ballotsSent.Remove(id);
        if (World.Commendations.Count > 0)
        {
            Messages.WriteCommendations(_writer, World.Commendations);
            _transport.Send(peer, _writer.Written, Delivery.ReliableOrdered);
        }
        if (_filmSent && World.Film is { } film)
            foreach (var m in Messages.FilmMessages(film))
                _transport.Send(peer, m, Delivery.ReliableOrdered);
        if (_reportSent && World.Run?.Report is { } report)
            foreach (var m in Messages.ReportMessages(report))
                _transport.Send(peer, m, Delivery.ReliableOrdered);
    }

    /// <summary>
    /// Who a returning player is (note 253). Dead when they dropped, dead still (the dead channel, their ballot, the queue).
    /// Alive, they stand up where their body lies now, with what's left on it (player.json rejoin.reclaimBody), and the
    /// body's gone from the world. With reclaimBody off, or their body gone (carried off by the Gaunt), it's GDD v1.4 App.
    /// D.2's letter: the body stays, and they're a lobbied joiner (null: <see cref="Admit"/> them).
    /// </summary>
    PlayerState? Back(Seat seat)
    {
        if (!seat.State.Alive)
            return seat.State;
        var body = World.Bodies.All.LastOrDefault(b => b.Kind == Physics.BodyKind.Ragdoll && b.DroppedOut && b.Owner == seat.Id);
        if (!PlayerTuning.Rejoin.ReclaimBody || body is null || body.TakenBy >= 0)
            return null;
        var up = PlayerMotor.StandUp(Train, body.Parent, body.Centre, body.LineHint, PlayerTuning);
        World.Bodies.Remove(body);
        return up with
        {
            Health = seat.State.Health,
            Cold = seat.State.Cold,
            Yaw = seat.State.Yaw,
            Kit = body.Tools,
            HeldSlot = seat.State.HeldSlot,
        };
    }

    void Board(byte id, PeerId peer)
    {
        var c = new Crew(id, peer);
        // First aboard takes the cab; everyone else spreads down the train.
        int car = 1 + (_crew.Count - 1) % Math.Max(1, Train.OwnVehicles - 1);
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
    bool _reportSent, _filmSent;

    /// <summary>The host's own player (its local client), whose vote alone skips the film (GDD v1.4 App. E.5); −1 if none.</summary>
    public int HostPlayer { get; set; } = -1;
    int _bookmarksSent;

    /// <summary>
    /// GDD v1.4 App. D.10, D.12: a dead player's Bookmark, on the press, of the living crewmate they follow (their intent's
    /// Watch, as <see cref="EarsOf"/> reads it). Only once the run's under way; capped per player and per run.
    /// </summary>
    readonly Dictionary<byte, (int Options, Enemies.EnemyKind? Cast)> _ballotsSent = [];
    bool _commendationsChanged;

    /// <summary>
    /// GDD v1.4 App. D.11 (note 180): a dead crewmate (not one still waiting to board) is offered their ballot, and casts it
    /// with a hotbar number (the dead carry nothing, so 1-3 are free): once per run, locked on submit.
    /// </summary>
    void Votes()
    {
        if (World.Director is not { } director || World.Run is { Over: true })
            return;
        foreach (var c in _crew)
        {
            if (c.State.Alive || c.State.Death == DeathCause.Waiting)
                continue;
            var ballot = director.Ballot(World, c.Id);
            if (c.ThisTick.Select is > 0 and var pick && pick <= ballot.Count && director.CanVote(c.Id))
                director.Vote(c.Id, ballot[pick - 1]);
        }
    }

    void Bookmark()
    {
        foreach (var c in _crew)
        {
            bool held = c.ThisTick.Has(PlayerActions.Bookmark);
            bool pressed = held && !c.Bookmarking;
            c.Bookmarking = held;
            if (!pressed || c.State.Alive || World.Run is not { Over: false })
                continue;
            var eyes = EarsOf(c);
            if (eyes != c)
                World.Bookmarks.Manual(World, c.Id, eyes.Id, eyes.State);
        }
    }

    /// <summary>
    /// Names to everyone when they change (and to whoever's just joined), each bookmark as it's made, and the night's report
    /// once it's over (GDD v1.4 App. D.12): the report is only the host's to write, so clients are sent it, in chunks, reliably.
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
            Messages.WriteLooks(_writer, World.Looks);
            foreach (var p in peers)
                _transport.Send(p, _writer.Written, Delivery.ReliableOrdered);
            Messages.WriteOutfits(_writer, World.Outfits);
            foreach (var p in peers)
                _transport.Send(p, _writer.Written, Delivery.ReliableOrdered);
        }
        // D.12: each bookmark as it's made, so every machine takes its still from the world as it is now.
        for (; _bookmarksSent < World.Bookmarks.All.Count; _bookmarksSent++)
        {
            Messages.WriteBookmark(_writer, World.Bookmarks.All[_bookmarksSent]);
            foreach (var p in peers)
                _transport.Send(p, _writer.Written, Delivery.ReliableOrdered);
        }
        // D.11: each dead player's ballot to them alone, as it's offered and once cast; the cue to the dead alone.
        if (World.Director is { } director && World.Run is not { Over: true })
        {
            foreach (var c in _crew.Where(c => !c.State.Alive && c.State.Death != DeathCause.Waiting))
            {
                var offered = director.Ballot(World, c.Id);
                if (offered.Count == 0)
                    continue;
                var state = (offered.Count, director.VoteOf(c.Id));
                if (_ballotsSent.TryGetValue(c.Id, out var sent) && sent == state)
                    continue;
                _ballotsSent[c.Id] = state;
                Messages.WriteBallot(_writer, offered, state.Item2);
                _transport.Send(c.Peer, _writer.Written, Delivery.ReliableOrdered);
            }
            foreach (var (kind, voters) in director.TakeVoteCues())
            {
                Messages.WriteVoteCue(_writer, kind, voters);
                foreach (var c in _crew.Where(c => !c.State.Alive))
                    _transport.Send(c.Peer, _writer.Written, Delivery.ReliableOrdered);
            }
        }
        if (_commendationsChanged)
        {
            _commendationsChanged = false;
            Messages.WriteCommendations(_writer, World.Commendations);
            foreach (var p in peers)
                _transport.Send(p, _writer.Written, Delivery.ReliableOrdered);
        }
        if (!_filmSent && World.Film is { } film)
        {
            _filmSent = true;
            foreach (var m in Messages.FilmMessages(film))
                foreach (var p in peers)
                    _transport.Send(p, m, Delivery.ReliableOrdered);
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
        // D.12: a commendation can come from anyone in the session at run end, aboard or still waiting to board.
        if (payload.Length == 3 && payload[0] == (byte)MessageType.Commend)
        {
            int from = _crew.Find(x => x.Peer == peer)?.Id ?? _waiting.Where(w => w.Peer == peer).Select(w => (int)w.Id).DefaultIfEmpty(-1).First();
            var session = _crew.Select(x => (int)x.Id).Concat(_waiting.Select(w => (int)w.Id)).ToList();
            if (from >= 0 && Run.Commendations.Give(World, from, payload[1], payload[2], session))
                _commendationsChanged = true;
            return;
        }
        // Note 254: they quit on purpose. Their crewmate goes limp as for a drop (spec E), but the place isn't held.
        if (payload.Length == 1 && payload[0] == (byte)MessageType.Leave)
        {
            Gone(peer, hold: false);
            // The hang-up can come in ahead of the word (a link whose close is heard at once): the place already held, let go.
            foreach (var seat in _reserved.Where(r => r.Peer == peer).ToList())
            {
                _reserved.Remove(seat);
                Free(seat.Id);
            }
            _transport.Disconnect(peer);
            return;
        }
        if (payload.Length > 0 && payload[0] == (byte)MessageType.Hello)
        {
            try
            {
                var hr = new NetReader(payload);
                hr.U8();
                var (name, token, outfit) = Messages.ReadHello(ref hr);
                // Note 253: a joiner's first word, before it's welcomed. It's let in in the order it connected (first aboard
                // takes the cab), so it waits its turn in the greeting line.
                if (_greeting.Find(g => g.Peer == peer) is { } greeting)
                {
                    (greeting.Said, greeting.Name, greeting.Token, greeting.Outfit) = (true, name, token, outfit);
                    return;
                }
                int id = _crew.Find(x => x.Peer == peer)?.Id ?? _waiting.Where(w => w.Peer == peer).Select(w => (int)w.Id).DefaultIfEmpty(-1).First();
                if (id >= 0)
                {
                    Name(id, name);
                    Wear(id, outfit, any: true);
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
        // Note 298: trying an outfit on, in the yard.
        if (payload.Length == 2 && payload[0] == (byte)MessageType.Wear)
        {
            Wear(c.Id, payload[1], any: false);
            return;
        }
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

    void Name(int id, string name)
    {
        if (name.Length == 0)
            return;
        World.Names[id] = name;
        _namesChanged = true;
        // D.8: whoever they were freed as on an earlier night, they still are.
        if (World.LooksByName.TryGetValue(name, out var look))
            World.Looks[id] = look;
    }

    /// <summary>
    /// A player's outfit (note 298): the one they came in (<paramref name="any"/>, from their Hello), or one tried on, which
    /// only takes in the yard before the gate (GDD §9: the yard is where the crew "try on outfits"). Out of range, it's
    /// their id's look.
    /// </summary>
    void Wear(int id, byte outfit, bool any)
    {
        if (!any && World.Run is { Phase: not Sim.Run.RunPhase.Yard })
            return;
        bool had = World.Outfits.TryGetValue(id, out byte was);
        if (outfit >= MaxOutfits)
        {
            if (had && World.Outfits.Remove(id))
                _namesChanged = true;
            return;
        }
        if (had && was == outfit)
            return;
        World.Outfits[id] = outfit;
        _namesChanged = true;
    }

    /// <summary>The outfits there can be (note 298): the art draws as many as it has looks, round again past them.</summary>
    public const int MaxOutfits = 64;

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
        // Spec F.3's radio range (note 196): it carries this far in from a tunnel's mouth or a mine spur's points.
        double reach = Train.Dynamics.Tuning.Kit.RadioReach;
        Func<double, bool>? tunnel = route is null ? null : s => route.DeepInTunnel(s, reach);
        Func<PlayerState, bool>? underground = World.Run is { } run ? s => run.Underground(s, Train, reach) : null;
        // Held (GDD v1.4 App. C.8, "radio broadcast of a GRAB"): a grabbed player's radio is keyed open for the whole GRAB,
        // whatever they meant to say into it; it closes at break-off, or with the hard-cut at death (a dead speaker has only
        // the dead channel, VoiceRouting). The radio's a thing (T41): no radio on you, nobody hears you on it, and you hear
        // nobody. Tunnels and mine spurs still kill it.
        radio |= speaker.State.Alive && speaker.State.Has(PlayerFlags.Held);
        radio &= World.Bodies.HasRadio(speaker.Id);
        // GDD v1.4 App. D.7 Live Mic (note 179): waiting in a Holdout with it on, the dead speaker is heard from the Holdout on
        // the proximity layer by the living near it (8 m clear, 26 m cutoff), and on the dead channel as ever. It's never said
        // aloud in the world: nothing listening for talk (the meter, the Gaunt, the Soot Children) hears it.
        var liveMic = speaker.State.Alive ? null : World.Holdouts?.LiveMicOf(speaker.Id);
        foreach (var listener in _crew)
        {
            if (listener == speaker)
                continue;
            // A dead listener hears the living through whoever they watch (App. D.10: "exactly what the followed player
            // hears"); the dead channel stays theirs.
            var ears = speaker.State.Alive ? EarsOf(listener) : listener;
            var path = VoiceRouting.Route(speaker.State, ears.State, radio, Train, tunnel, World.Bodies.HasRadio(ears.Id), underground);
            if (liveMic is not null && VoiceRouting.HearsLiveMic(listener.State, liveMic.Inside, Train))
                path |= VoicePath.Proximity;
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

    readonly HashSet<byte> _cut = [];

    /// <summary>
    /// GDD v1.4 App. D.2, the hard-cut: on the tick a crewmate dies, every other client is told in the voice stream to drop
    /// what it has of them (<see cref="VoicePath.Cut"/>). Reliable: a cut that went missing would let the end of the
    /// sentence play out. A freed player (App. D.8) is heard again.
    /// </summary>
    void CutTheDead()
    {
        foreach (var c in _crew)
        {
            if (c.State.Alive)
            {
                _cut.Remove(c.Id);
                continue;
            }
            if (!_cut.Add(c.Id))
                continue;
            Messages.WriteVoiceDown(_voiceWriter, c.Id, 0, VoicePath.Cut, []);
            foreach (var listener in _crew)
                if (listener != c)
                    _transport.Send(listener.Peer, _voiceWriter.Written, Delivery.ReliableOrdered);
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
            {
                _far.Add(WireRecord.MakeKey(RecordKind.Enemy, e.Id));
                // Nor a friend's blow landing on it there (T121's hit confirm): the thud at your back would give it away.
                foreach (var h in World.Hits)
                    if (h.EnemyId == e.Id)
                        _far.Add(WireRecord.MakeKey(RecordKind.Hit, h.Id));
            }
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
