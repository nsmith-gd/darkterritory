using Ballast;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim;

/// <summary>
/// Everything one night simulates beyond the players themselves: the train and its pieces, the Choir,
/// the enemies and the director, what can be shot, and what was fired this tick. The host steps it with
/// authority; a client steps the same code for its own predicted player (GDD §33: shared simulation)
/// and only mirrors enemies from snapshots.
/// </summary>
public sealed class World
{
    readonly List<Enemy> _enemies = new();
    readonly Dictionary<uint, List<HitTarget>> _targetHistory = new();
    EnemyContext? _context;
    int _nextEnemyId = 1;

    public World(TrainOnLine train, CombatTuning? combat = null)
    {
        Train = train;
        Combat = combat;
        // A roof hatch isn't shut down onto a casting the crane has hanging in it (T99).
        train.HatchBlocked = car => Run?.CurrentSite?.Cranes.Any(c => c.InHatch(train, car)) == true;
        // Note 301: a smashed lamp's glass is the wrench's work (repair.lampMendRate seconds of it to each second worked).
        train.LampOut = () => Derailed ? 0 : LampOutSeconds; // derailed, the night's over: nothing to call out
        train.MendLamp = dt => LampOutSeconds = Math.Max(0, LampOutSeconds - dt * train.Dynamics.Tuning.Repair.LampMendRate);
        Choir = ChoirState.Quiet;
        if (combat is not null)
            Guns.Arm(train, combat.Guns);
        if (train.Line.Branches.Count > 0)
            Switches = new Rail.SwitchStands(new Route.JunctionTuning());
    }

    /// <summary>The stands at the line's switches; null on a line without branches.</summary>
    public Rail.SwitchStands? Switches { get; private set; }
    /// <summary>Switches thrown (or tried) this tick.</summary>
    public List<Rail.SwitchThrow> SwitchThrows { get; } = new();

    /// <summary>The switch stands' tuning, from content (host and clients alike: the HUD asks them what's in reach).</summary>
    public void EnableSwitches(Route.JunctionTuning tuning)
    {
        if (Train.Line.Branches.Count > 0)
            Switches = new Rail.SwitchStands(tuning);
    }

    /// <summary>
    /// Host: sets a switch without anyone at its stand (the Switchman, scripted set pieces, tests). Returns whether it now
    /// stands as asked: false only when a wheel on the points kept it from moving. (Note 289: it returned whether it moved,
    /// so one already set that way read as held, and the Switchman left its lever a tick after throwing it.)
    /// </summary>
    public bool SetSwitch(int branch, bool diverge) =>
        Train.Diverging(branch) == diverge || Train.ThrowSwitch(branch, diverge, Switches?.Tuning.PointsLength ?? 0);

    public TrainOnLine Train { get; }
    /// <summary>
    /// Where the cab's controls were set for the last step: what its gauges and levers show anyone in the cab (a
    /// driver notches the throttle and flips the reverser from where they are).
    /// </summary>
    public TrainControls Controls { get; private set; } = new() { Reverser = 1 };

    /// <summary>
    /// Who holds the cab's controls (note 574): the player id, or −1 with nobody holding them (then anyone in the cab works
    /// them). <see cref="ControlsHolderBot"/>: that's a bot, which someone playing takes them off. The host's to say;
    /// replicated with the controls, so a bot driver knows when someone playing has them.
    /// </summary>
    public int ControlsHolder { get; private set; } = -1;
    public bool ControlsHolderBot { get; private set; }

    public void HoldControls(int holder, bool bot)
    {
        ControlsHolder = holder;
        ControlsHolderBot = holder >= 0 && bot;
    }
    public CombatTuning? Combat { get; set; }
    /// <summary>Host: what the crew have said lately (T40), fed by the session as voice arrives. The Soot Children listen here.</summary>
    public Net.VoiceMemory Voices { get; } = new();
    /// <summary>The crew as they acted this tick (host, with enemies on), for spawns that go after someone in particular.</summary>
    public IReadOnlyList<(int Id, PlayerState State)> CrewThisTick =>
        _context is { } c ? [.. c.Crew.Select(x => ((int)x.Player.Id, x.Player.State))] : [];
    /// <summary>
    /// How reaching hands work (T29, player.json <c>hand</c>). The sessions set it from their player tuning; while it's
    /// unset, hands in intents are ignored and everyone reaches from the body.
    /// </summary>
    public HandTuning? Hand { get; set; }
    public ChoirState Choir;
    /// <summary>Hit volumes for this tick (from the enemies).</summary>
    public List<HitTarget> Targets { get; } = new();
    /// <summary>Rounds fired this tick.</summary>
    public List<GunShot> Shots { get; } = new();
    /// <summary>
    /// Blows and balls that landed on creatures lately (T121), newest last: the host's, kept <see cref="HitTuning.KeepSeconds"/>
    /// and replicated, so every client sees the flinch, hears the thud, and its striker gets the marker.
    /// </summary>
    public List<HitConfirm> Hits { get; } = new();
    /// <summary>Crewmates' swings lately, landed or not (note 197): the host's, kept as long as hits are, and replicated.</summary>
    public List<SwingEvent> Swings { get; } = new();
    /// <summary>Where cannonballs came down lately (T121), newest last: the host's, kept as long as the smoke and replicated.</summary>
    public List<CannonImpact> Impacts { get; } = new();
    /// <summary>
    /// Crewmates' emotes lately (note 298): the host's, each kept for its length (<see cref="EmoteTuning"/>) and replicated,
    /// so the crew see it. Nothing in the sim reads them.
    /// </summary>
    public List<EmoteEvent> Emotes { get; } = new();
    /// <summary>How long an emote lasts, and how soon another may follow (player.json emotes; the host sets it).</summary>
    public EmoteTuning EmoteTuning { get; set; } = new();
    int _nextFx = 1;
    /// <summary>The host's world, or one with nobody else's to mirror: it decides what landed where.</summary>
    bool Hosting => Authority || Enemies is null;
    public uint Tick { get; set; }
    public double ElapsedSeconds => Tick * SimConstants.TickSeconds;

    /// <summary>The engine's forward lamp. Sleepers need it lit to be seen from far off (App. A.2); Lamplighters come for it (App. A.6).</summary>
    public bool LampLit { get; set; } = true;
    /// <summary>
    /// Seconds until a lamp the Lamplighters smashed can be lit again (T52: the glass is out until someone fits the spare).
    /// Counted down every tick, on the host and the clients alike.
    /// </summary>
    public double LampOutSeconds { get; set; }

    /// <summary>Where the engine's forward lamp is (world): on the cab's nose under its front windows, at the very front (note 276).</summary>
    public static Ballast.Double3 LampPosition(in CarFrame engine) => engine.ToWorld(new Ballast.Double3(0, LampHeight, -engine.Shape.HalfLength - 0.3));

    /// <summary>
    /// The forward lamp's height over the rail (m): cab forward (note 276), on the cab's nose under the front windows, clear of
    /// the driver's view down the line (it was 2.8, high on the smokebox door, with the boiler in front).
    /// </summary>
    public const double LampHeight = 2.0;

    /// <summary>Smashed: out, and no lighting it for a while.</summary>
    public void SmashLamp(double seconds)
    {
        LampLit = false;
        LampOutSeconds = Math.Max(LampOutSeconds, seconds);
        _relight = !Derailed;
    }

    // Note 266 (build 1121: "the lights are completely off"): a lamp smashed comes back lit once its glass is in (the
    // spare fitted, T52), as the driver had it; one the driver switched off stays off. Host only (LampLit is sent).
    bool _relight;
    /// <summary>
    /// Host: how long running a bend's overspeed warning has been up (LineGen.TrackRules.Assess, note 265); a bend derails
    /// the train only once it's been up train.json overspeed.leadSeconds.
    /// </summary>
    public double BendWarnSeconds { get; set; }
    /// <summary>Host: ticks a bend would have derailed the train but its warning hadn't been up long enough (should be 0).</summary>
    public int BendsSpared { get; set; }
    /// <summary>Host: the fires a boarded hound pack has set (note 269's; counted for the burns' report, note 437).</summary>
    public int PackFires { get; set; }
    /// <summary>Host: how long the cab's dead-end warning has been up (<see cref="Sim.Train.DeadEnds"/>, note 286).</summary>
    public double DeadEndWarnSeconds { get; set; }
    /// <summary>Host: buffers hit over the limit before the warning had been up its lead (the buffer stop's damage alone; should be 0).</summary>
    public int DeadEndsSpared { get; set; }
    /// <summary>Host: the dead lines a Switchman has thrown the points for this night (note 286), by branch: C.9's Switchman row.</summary>
    public HashSet<int> SwitchmanThrew { get; } = new();

    /// <summary>
    /// Through a dead line's buffers and off the end (note 286). Down a line the Switchman threw it onto, C.9's Switchman row
    /// names the forward cannon as it always has; otherwise it's the throttle's.
    /// </summary>
    void OffTheEnd(int branch, double speed, double limit)
    {
        int kmh = (int)Math.Round(speed * 3.6), over = (int)Math.Round(limit * 3.6);
        if (SwitchmanThrew.Contains(branch))
        {
            var (gunner, cannon) = Sim.Run.IncidentLog.ForwardCannon(this);
            Derail($"ran off the end of the dead line the Switchman threw it down, at {kmh} km/h (over {over} km/h it goes through the buffers)", gunner, cannon);
        }
        else
            Derail($"ran off the end of a dead line at {kmh} km/h (over {over} km/h it goes through the buffers)");
    }

    /// <summary>Host: each bend derailment, by how long its warning had been up when it came (the audit's).</summary>
    public List<double> BendCommits { get; } = new();
    /// <summary>GDD §23: derailment kills the entire crew at once.</summary>
    public bool Derailed { get; private set; }
    /// <summary>Host: the crew has braked hard for a Long Whistle's horn, a train that wasn't there (App. B.2's "false positive").</summary>
    public bool BrakedForFalseAlarm { get; set; }
    /// <summary>Host: how long nobody alive has been in the engine's cab (the Track Doll's tampering, the Stoker's open door).</summary>
    public double CabEmptySeconds { get; private set; }
    /// <summary>Host: the cab's been left empty for a while at some point this run (App. B.2: the Track Doll weighs up).</summary>
    public bool CabWasLeftEmpty { get; private set; }
    /// <summary>Host: how long each player has stood idle (still, doing nothing): Tippy Toesie's mark (App. A.5, B.5).</summary>
    public Dictionary<int, double> IdleSeconds { get; } = new();
    /// <summary>Out among a dead settlement's houses (the line's tag): the villages the Ribbits and the Gaunt keep to.</summary>
    public bool InSettlement => Route?.Plan?.Director.TagsAt(Train.Dynamics.Distance).Contains("dead_settlement") == true;
    /// <summary>The next child's call is a real child whatever the dice say (App. B.6: a host's first-ever is). The host sets it.</summary>
    public bool NextChildReal { get; set; }
    /// <summary>
    /// Host: a child's call has come this night (App. B.6). The Game keeps it in the host's profile, so the host's first-ever
    /// call is the only one <see cref="NextChildReal"/> forces (note 182); the Sim never touches a file.
    /// </summary>
    public bool ChildCalled { get; set; }
    /// <summary>The id the next enemy added will get.</summary>
    public int NextEnemyId => _nextEnemyId;
    /// <summary>
    /// Seconds the train's whistle has left to blow (GDD §12: the conductor's cord; the Whistler blows it too). Replicated:
    /// every client hears it. It feeds the loudness meter (App. C.7).
    /// </summary>
    public double WhistleSeconds { get; set; }
    /// <summary>Who's blowing it (the last to pull the cord), or −1: the Whistler's whistle belongs to nobody (App. C.7).</summary>
    public int WhistleBy { get; internal set; } = -1;
    /// <summary>
    /// The dark's answer to what the crew did (note 287): a call from out past the lamp and eyes at its edge while it lasts.
    /// The host's, replicated on the world record; presentation only.
    /// </summary>
    public DrawAnswer Answer { get; set; }

    /// <summary>
    /// A sign shown a crewmate afoot off the train (note 327): eyes toward what lives at the stop, and its sound. The host's,
    /// replicated on the world record; presentation only.
    /// </summary>
    public Watcher Watcher { get; set; }

    /// <summary>Host: a draw made (note 287), credited on the director's ledger; nothing in the safe yard.</summary>
    void Drew(DrawCause cause, int player, double amount)
    {
        if (Authority && !SafeYard && Director is { } d)
            d.Draws.Add(cause, player, amount);
    }

    /// <summary>
    /// Host: where the dark answers from (note 287): out at the lamp's edge ahead (answerDistance from the engine's nose), off to a side
    /// (answerLateral; the side from the tick, so it isn't always the same), at an animal's eye height off the ground.
    /// </summary>
    Ballast.Double3 AnswerAt(DrawTuning t)
    {
        double side = (Tick / SimConstants.TickRate) % 2 == 0 ? 1 : -1;
        return DrawAnswerAt(Train, t.AnswerDistance, side * t.AnswerLateral, t.AnswerHeight);
    }

    /// <summary>
    /// A point <paramref name="ahead"/> m out from the engine's nose, the way the lamp shines (its −Z), <paramref name="lateral"/>
    /// m to its right, <paramref name="height"/> m off the ground there (note 287: where the dark answers from).
    /// </summary>
    public static Ballast.Double3 DrawAnswerAt(TrainOnLine train, double ahead, double lateral, double height)
    {
        var engine = train.Frames[0];
        var at = engine.ToWorld(new Ballast.Double3(lateral, 0, -engine.Shape.HalfLength - ahead));
        double hint = train.Dynamics.Distance + ahead;
        return at with { Y = PlayerMotor.GroundAt(at, train.Line, ref hint) + height };
    }

    /// <summary>The whistle blows this long (the cord pulled by <paramref name="by"/>, or the Whistler at it).</summary>
    public void Whistled(double seconds, int by = -1)
    {
        WhistleSeconds = Math.Max(WhistleSeconds, seconds);
        WhistleBy = by;
    }

    /// <summary>Use held at the whistle cord's handle, looking at it (note 264): a hand on the cord.</summary>
    public bool OnTheCord(in PlayerState s, in PlayerIntent intent) =>
        s.Alive && intent.Has(PlayerButtons.Use) && intent.MoveZ <= 0.5 && CrewActions.Nearest(s, Train, Hand) == InteractableKind.Whistle;

    readonly SortedDictionary<int, double> _choirShares = [];

    /// <summary>
    /// Host: each crewmate's share of the loudness meter during the Choir's BUILD (GDD v1.4 App. A.7, C.7), in loudness-seconds:
    /// their voice, the cannon rounds they fired, the whistle they pulled and the noisy toy in their hands. Machinery,
    /// livestock and the Whistler's whistle belong to nobody. Cleared when it's not gathering; held while it's here, for the
    /// swarm to choose by.
    /// </summary>
    public IReadOnlyDictionary<int, double> ChoirShares => _choirShares;
    public double ChoirShare(int player) => _choirShares.GetValueOrDefault(player);

    /// <summary>The loudest of the build (ties to the lower id: the shares are kept in id order), or −1 if nobody's put in.</summary>
    public int ChoirLoudest => _choirShares.Where(s => s.Value > 0).Select(s => (s.Key, s.Value)).DefaultIfEmpty((-1, 0)).MaxBy(s => s.Item2).Item1;

    /// <summary>Credits <paramref name="player"/> with <paramref name="loudnessSeconds"/> while the Choir could gather.</summary>
    void CreditChoir(int player, double loudnessSeconds)
    {
        if (player >= 0 && loudnessSeconds > 0 && !Choir.Present && !Choir.Spent)
            _choirShares[player] = _choirShares.GetValueOrDefault(player) + loudnessSeconds;
    }
    /// <summary>
    /// What's being done to a player's voice (GDD v1.1 App. C.8): muffled under Tippy Toesie's hand; fading (the gain left,
    /// 0..1) as a Soot Child drains them. The host applies it to what it forwards; the Passenger has no voice to change.
    /// </summary>
    public (bool Muffled, double Gain) VoiceEffect(int playerId)
    {
        bool muffled = _enemies.Any(e => e is TippyToesie && e.Phase == SpinePhase.Grab && e.Holding == playerId);
        double gain = 1;
        foreach (var e in _enemies)
            if (e is SootChildren && e.Phase == SpinePhase.Grab && e.Holding == playerId && e.GrabWindow > 0)
                gain = Math.Min(gain, Math.Clamp(1 - e.PhaseSeconds / e.GrabWindow, 0.05, 1));
        return (muffled, gain);
    }

    /// <summary>The Choir's seized its one for the run (App. A.7 LIMIT): the swarm goes, and it's spent.</summary>
    public void ChoirTook() => _choirTook = true;

    /// <summary>The last of the swarm killed by the crew together (note 288): the Choir is done for the run, as when it takes its one.</summary>
    public void ChoirSlain() => _choirTook = true;

    /// <summary>
    /// Host: the kinds the crew have killed together tonight (note 288, the director's clarification of 7 Oct 2026): a kill is
    /// for the night, so the director doesn't send that kind again. Driven off, a creature can come back.
    /// </summary>
    public HashSet<EnemyKind> Slain { get; } = [];

    /// <summary>
    /// Host: once-a-run kinds the crew drove off tonight rather than killed (the Passenger, note 288): driven off isn't the end
    /// of it, so the director may send it again (Spawns' once-a-run rule lets it).
    /// </summary>
    public HashSet<EnemyKind> DrivenOff { get; } = [];
    bool _choirTook;
    double _hotFor;
    bool _stokerWasIn;

    /// <summary>
    /// Host: how long before a Stoker may come again (the director's decision of 6 Oct 2026, note 263): one gone leaves
    /// <see cref="StokerTuning.BreakSeconds"/> of quiet, its clocks stopped meanwhile.
    /// </summary>
    public double StokerBreakSeconds { get; private set; }
    readonly Dictionary<int, uint> _swingReady = new();

    /// <summary>True on the host: enemies and the director run. False on clients, which mirror them.</summary>
    public bool Authority { get; private set; }
    public EnemyTuning? Enemies { get; private set; }

    /// <summary>
    /// The fire's running hot enough to draw the Stoker (note 263: <see cref="StokerTuning.HeatFirebox"/>) and it isn't
    /// aboard yet: it waits on the smokestack, watching the heat, and is drawn there. Read-only, from replicated state, for
    /// the presentation.
    /// </summary>
    public bool StokerWaiting => Enemies is { } t && Train.BoilerTuning is not null && !Train.Boiler.Ruptured && !SafeYard
        && Train.Boiler.Firebox >= t.Stoker.HeatFirebox && !_enemies.Any(e => e.Kind == EnemyKind.Stoker && !e.Gone);
    public Route.Route? Route { get; private set; }
    public Director? Director { get; private set; }

    /// <summary>
    /// The night's commendations (GDD v1.4 App. D.12; note 180): one from each player in the session at run end, to anyone
    /// but themselves, in the order given. The host's, sent to every client. Which is the starter set's index
    /// (<see cref="Run.Commendations"/>).
    /// </summary>
    public List<(int From, int To, byte Which)> Commendations { get; } = [];

    /// <summary>
    /// The look each player came into the night with (GDD v1.4 App. D.8; note 181), by tonight's id: a survivor freed on an
    /// earlier night (<see cref="Run.Identity"/>). The host fills it from <see cref="LooksByName"/> as names arrive, and sends it.
    /// </summary>
    public Dictionary<int, string> Looks { get; } = [];

    /// <summary>
    /// The outfit each player wears (GDD §9's yard: "try on outfits"; note 298), by id: one of the crew's looks, chosen in
    /// the settings and tried on in the yard. The host's, sent on change. A player missing from it wears their id's look.
    /// </summary>
    public Dictionary<int, byte> Outfits { get; } = [];

    /// <summary>The look a player is drawn in (note 298): their outfit if they've one, else their id.</summary>
    public int OutfitOf(int id) => Outfits.TryGetValue(id, out byte o) ? o : id;

    /// <summary>The host's: the campaign's looks by player name, going into the night.</summary>
    public IReadOnlyDictionary<string, string> LooksByName { get; set; } = new Dictionary<string, string>();
    /// <summary>
    /// Host, the harness's combination audit (GDD §34 "every pair and triple in the roster"; note 186): these kinds, and
    /// only these, come whenever their spawn can place them, from <see cref="InsistAfter"/> seconds into the night and again
    /// <see cref="InsistEvery"/> seconds after the last one's gone. The director's budget, pacing and weights are skipped
    /// (they decide when a kind comes; the audit asks what happens when they meet). Null on a real night.
    /// </summary>
    public IReadOnlyList<EnemyKind>? Insist { get; set; }
    public double InsistAfter { get; set; } = 2;
    public double InsistEvery { get; set; } = 10;
    readonly Dictionary<EnemyKind, double> _insistGone = [];

    public IReadOnlyList<Enemy> ActiveEnemies => _enemies;
    public List<EnemyEvent> EnemyEvents { get; } = new();
    public List<DamageEvent> Damage { get; } = new();

    /// <summary>Hands this world the enemies: the host gets the director and the route's Sleepers.</summary>
    public void EnableEnemies(EnemyTuning tuning, Route.Route? route, ulong seed, int crew, bool authority)
    {
        Enemies = tuning;
        Route = route;
        Authority = authority;
        // A seized axle's hold on the train (note 367), on every machine alike: prediction drags as the host does.
        Train.SeizedTopSpeed = tuning.Hotbox.SeizedTopSpeed;
        Train.SeizedHold = tuning.Hotbox.SeizedHold;
        Train.SeizedRepair = tuning.Hotbox;
        if (!authority)
            return;
        Director = new Director(tuning.Director, route, seed, Train.Dynamics.Consist.CarCount, crew);
        // Note 266: off unless a mod brings them back (the director's decision, 2026-10-06).
        if (route is not null && tuning.Sleepers.Enabled && Director.Allows(EnemyKind.Sleepers))
            foreach (var f in route.Of(FeatureKind.Sleepers))
                _enemies.Add(new Sleepers(_nextEnemyId++) { LineDistance = f.Start, Height = 0.2 });
    }

    /// <summary>Loose bodies: cargo crates, tools, the dead (GDD §33). Host-simulated, mirrored on clients.</summary>
    public Physics.Bodies Bodies { get; } = new();

    /// <summary>This world simulates loose bodies itself (the host, or the single-player prototype).</summary>
    public void EnableBodies() => Authority = true;

    /// <summary>Host: after everyone has moved, bodies for anyone who died, then a physics step.</summary>
    public void StepBodies(IReadOnlyCollection<(int Id, PlayerState State)> crew)
    {
        if (!Authority)
            return;
        LastCrew = [.. crew];
        // App. C.9: every death in the log, the tick its body goes down, with the contributing action its failure names.
        foreach (var (id, s, body) in Bodies.OnDeaths(Train, crew))
            if (Run is not null && !_countedAhead.Remove(id))
                Attribution.Add(Sim.Run.IncidentLog.Death(this, id, s, body, crew));
        Bodies.Step(Train, Train.Dynamics.Tuning, id => crew.FirstOrDefault(c => c.Id == id) is { State: var s } pair && pair.Id == id ? s : null);
        Recover();
        BreakRadios(crew);
    }

    readonly Dictionary<int, (int Health, bool Held)> _wasHurt = [];

    /// <summary>
    /// GDD §23 "radio breaks" (note 183): a hard knock (a fall, a blow) or being grabbed may smash the radio on your belt. The
    /// chance rises with the damage; the same on every run of the tick (a hash, not a die). Broken, it's carried but dead.
    /// </summary>
    void BreakRadios(IReadOnlyCollection<(int Id, PlayerState State)> crew)
    {
        var t = Train.Dynamics.Tuning.Kit;
        foreach (var (id, s) in crew)
        {
            (int Health, bool Held) was = _wasHurt.TryGetValue(id, out var before) ? before : (s.Health, s.Has(PlayerFlags.Held));
            _wasHurt[id] = (s.Health, s.Has(PlayerFlags.Held));
            int lost = Math.Max(0, was.Health - s.Health);
            bool grabbed = s.Has(PlayerFlags.Held) && !was.Held;
            if (lost == 0 && !grabbed)
                continue;
            double chance = Math.Min(1, t.RadioBreakPerDamage * lost + (grabbed ? t.RadioBreakOnGrab : 0));
            foreach (var radio in Bodies.All.Where(b => b.Kind == Physics.BodyKind.Radio && b.Carrier == id && !b.Broken))
                if (DarkTerritory.Sim.Combat.Guns.Fouls(Tick, 1000 + id, chance))
                    radio.Broken = true;
        }
    }

    /// <summary>
    /// Line Plan §12.6, GDD v1.4 App. D.2, D.9 and §23.2 (note 181): never an unrecoverable body, or kit. A body or a repair kit
    /// that's come to rest on the ground outside the walkable corridor (further from the track than it, or fallen well below
    /// the rails, off a bridge or into a ravine) is moved to the nearest walkable point on the formation's edge, the side it
    /// went off.
    /// </summary>
    void Recover()
    {
        var t = Train.Dynamics.Tuning.Recovery;
        foreach (var b in Bodies.All)
        {
            // The rescued child too (A.6 "cannot be harmed"; note 182): dropped off a bridge, it's found on the bank.
            if (b.Kind is not (Physics.BodyKind.Ragdoll or Physics.BodyKind.RepairKit or Physics.BodyKind.Child) || b.Parent != PlayerState.World || b.Carrier >= 0
                || b.Stowed || !b.Pbd.Asleep)
                continue;
            var at = b.Pbd.Centre;
            double hint = b.LineHint;
            var (path, along) = Train.Line.Nearest(at, ref hint);
            var rail = Train.Line.Sample(path, along);
            var right = Ballast.Double3.Cross(rail.Tangent, Ballast.Double3.Up).Normalized;
            double lateral = Ballast.Double3.Dot(at - rail.Position, right);
            if (Math.Abs(lateral) <= t.CorridorM && at.Y >= rail.Position.Y - t.DropM)
                continue;
            var edge = rail.Position + right * (Math.Sign(lateral == 0 ? 1 : lateral) * t.EdgeM);
            double ground = PlayerMotor.GroundAt(edge, Train.Line, ref hint);
            var shift = (edge with { Y = ground + 0.2 }) - at;
            foreach (ref var p in b.Pbd.Particles.AsSpan())
            {
                p.Position += shift;
                p.Previous = p.Position;
            }
            // Laid there, at rest: it doesn't roll back down the bank it came off.
            b.Pbd.Sleep();
            b.LineHint = hint;
        }
    }

    /// <summary>Host: what the train leaves the yard with that isn't cargo: crates and a lamp in the guard van (GDD §10 tool storage).</summary>
    public void Stock()
    {
        MountExtinguishers();
        // The radios (T41, train.json kit): one on the cab floor against its back wall right of the middle, out of the reach
        // of a crewmate arriving in the cab and of all the work at its front (note 280), and against the plate over the
        // boiler's end, clear of the corridor in beside it (note 338); the rest in the guard van.
        int radios = Train.Dynamics.Tuning.Kit.Radios;
        if (radios > 0 && Train.Frames[0].Shape.Cab is { } cab)
        {
            Bodies.RadiosCarried = true;
            Bodies.SpawnCrate(Train, 0, new Ballast.Double3(0.45, cab.Min.Y + 0.2, cab.Max.Z - 0.4), Physics.BodyKind.Radio);
            radios--;
        }
        StowRepairKits();
        var guard = Train.Dynamics.Consist.Vehicles.LastOrDefault(v => v.Kind == VehicleKind.Guard);
        if (guard is null || Train.Frames[guard.Id].Shape.Interior is not { } room)
            return;
        double floor = room.Min.Y + 0.1;
        // Side by side: bodies don't collide with each other yet (ARCHITECTURE §8).
        foreach (double z in new[] { 1.2, 2.0, 2.8 })
            Bodies.SpawnCrate(Train, guard.Id, new Ballast.Double3(0.6, floor, room.Min.Z + z));
        Bodies.SpawnCrate(Train, guard.Id, new Ballast.Double3(-0.9, floor, room.Max.Z - 2.5), Physics.BodyKind.Lamp);
        for (int i = 0; i < radios; i++)
            Bodies.SpawnCrate(Train, guard.Id, new Ballast.Double3(-0.9, floor, room.Max.Z - 3.3 - 0.5 * i), Physics.BodyKind.Radio);
        var kit = Train.Dynamics.Tuning.Kit;
        // The departure's stores (GDD §9; note 182): spare lamps along from the van's own, spare extinguishers across from them
        // (loose, with no bracket of their own to recharge on).
        for (int i = 0; i < kit.SpareLamps; i++)
            Bodies.SpawnCrate(Train, guard.Id, new Ballast.Double3(-0.5, floor, room.Max.Z - 2.5 - 0.4 * (i + 1)), Physics.BodyKind.Lamp);
        for (int i = 0; i < kit.SpareExtinguishers; i++)
            Bodies.SpawnCrate(Train, guard.Id, new Ballast.Double3(-0.2, floor, room.Min.Z + 1.2 + 0.4 * i), Physics.BodyKind.Extinguisher);
        // Hand-carried loot (GDD v1.1 App. C.4): toys, for the Track Doll to steal.
        for (int i = 0; i < kit.Toys; i++)
            Bodies.SpawnCrate(Train, guard.Id, new Ballast.Double3(0.6, floor, room.Max.Z - 1.2 - 0.5 * i), Physics.BodyKind.Toy).Noise =
                i < kit.ToyNoises.Count ? kit.ToyNoises[i] : Physics.ToyNoise.None;
    }

    /// <summary>
    /// What's aboard, for the autosave (note 500): every thing in a car of the train (the engine's cab too) on its floor, a roof,
    /// a locker's shelf, or in a crewmate's hands there. Not the dead (the crew's places aren't saved, and a resumed night's crew
    /// are all back), nor anything a creature's carrying off.
    /// </summary>
    public Campaign.ThingAboard[] Aboard() =>
        [.. Bodies.All.Where(b => b.Kind != Physics.BodyKind.Ragdoll && b.TakenBy < 0 && b.Parent >= 0 && b.Parent < Train.Vehicles.Count
            && b.Pbd.Particles.Length == 1).Select(b =>
        {
            var p = b.Pbd.Particles[0];
            return new Campaign.ThingAboard(b.Kind, b.Parent, p.Position, p.Radius, b.Pbd.Friction, b.Pbd.Bounce, b.Yaw, b.Locker, b.Slot, b.Claimed,
                b.Cargo, b.Owner, b.Home, b.Charge, b.Noise, b.Broken);
        })];

    /// <summary>
    /// Host, a night resumed from its autosave (note 500): what was aboard put back where it was, in place of
    /// <see cref="Stock"/>'s fresh stocking (a lamp taken out and lost stays lost; a find, a child or the kit where the crew put
    /// them). What's stowed goes back on its locker's shelves in the order it was there. The train's kit counts as stocked, and
    /// its radios as carried things, as <see cref="Stock"/> leaves them.
    /// </summary>
    public void Restock(IEnumerable<Campaign.ThingAboard> things)
    {
        var kit = Train.Dynamics.Tuning.Kit;
        if (kit.Radios > 0 && Train.Frames[0].Shape.Cab is not null)
            Bodies.RadiosCarried = true;
        if (RepairKitCar(Train) is not null && !Repairs.ByWrench(Train) && kit.RepairKits + kit.SpareKits > 0)
            KitStocked = true;
        foreach (var t in things.OrderBy(t => t.Locker < 0 ? 0 : 1).ThenBy(t => t.Car).ThenBy(t => t.Locker).ThenBy(t => t.Slot))
        {
            if (t.Car < 0 || t.Car >= Train.Vehicles.Count)
                continue;
            var b = Bodies.Put(Train, t.Car, t.Kind, t.At, t.Radius, t.Friction, t.Bounce);
            b.Yaw = t.Yaw;
            b.Claimed = t.Claimed;
            b.Cargo = t.Cargo;
            b.Owner = t.Owner;
            b.Home = t.Home;
            b.Charge = t.Charge;
            b.Noise = t.Noise;
            b.Broken = t.Broken;
            // A full shelf (the save's from a train with other lockers) leaves it where it lay: at its shelf, out of the way.
            if (t.Locker >= 0)
                Bodies.Stow(b, Train, t.Car, t.Locker);
        }
    }

    /// <summary>
    /// The repair kit (GDD §12) in its car (train.json kit.repairKitCar), where the crew learn to look for it: the first car
    /// back from the engine, a walk from the footplate, in the fitter's locker (note 173); and the spares the fortress sold
    /// the crew (GDD v1.4 App. E.12 question 4) beside it, then in the lockers after it. A car without lockers has its kits
    /// on the floor inside its front door, as it always did.
    /// </summary>
    void StowRepairKits()
    {
        if (RepairKitCar(Train) is not { } car)
            return;
        var shape = Train.Frames[car].Shape;
        var kit = Train.Dynamics.Tuning.Kit;
        // Note 301: where the wrench is the repair tool, the kit's gone: none rides in locker 8.
        int kits = Repairs.ByWrench(Train) ? 0 : kit.RepairKits + kit.SpareKits;
        int first = Math.Max(0, shape.KitLocker);
        // From the kit's locker on down the row, then round from the front.
        var order = Enumerable.Range(0, shape.Lockers.Count).Select(i => (first + i) % shape.Lockers.Count).ToList();
        for (int i = 0; i < kits; i++)
        {
            var b = Bodies.SpawnCrate(Train, car, RepairKitStowage(shape, shape.Interior!.Value, i), Physics.BodyKind.RepairKit);
            if (!order.Any(locker => Bodies.Stow(b, Train, car, locker)))
                b.Pbd.Particles[0].Position = b.Pbd.Particles[0].Previous = RepairKitStowage(shape, shape.Interior!.Value, i, floor: true) + Ballast.Double3.Up * 0.1;
        }
        KitStocked |= kits > 0;
        StockLockers(car, shape);
    }

    /// <summary>
    /// The rest of the lockers' stock (note 264, the director's notes on build 1121: "all of these seem empty"): train.json
    /// kit.lockers.stock, each locker's things on its shelves, along the row front to back. What doesn't fit (a shelf the
    /// spare kits took) isn't stocked.
    /// </summary>
    void StockLockers(int car, CarShape shape)
    {
        if (Lockers.Tuning(Train) is not { } t)
            return;
        foreach (var bay in shape.Lockers)
            if (t.Stock.TryGetValue(bay.Name, out var things))
                foreach (var kind in things)
                {
                    var b = Bodies.SpawnCrate(Train, car, Lockers.SlotAt(bay, 0, 1, 0), kind);
                    if (!Bodies.Stow(b, Train, car, bay.Index))
                        Bodies.Remove(b);
                }
    }

    /// <summary>The train left with a repair kit (GDD v1.4 §23.2: without one, nothing can strand it).</summary>
    public bool KitStocked { get; private set; }

    /// <summary>The car the repair kit rides in: train.json's, or the nearest walk-in car to the engine before it; null with none.</summary>
    public static int? RepairKitCar(TrainOnLine train)
    {
        var cars = train.Dynamics.Consist.Vehicles.Where(v => !v.IsEngine && train.Frames[v.Id].Shape.Interior is not null).Select(v => v.Id).ToList();
        if (cars.Count == 0)
            return null;
        int want = train.Dynamics.Tuning.Kit.RepairKitCar;
        return cars.Contains(want) ? want : cars.Where(c => c < want).DefaultIfEmpty(cars[0]).Max();
    }

    /// <summary>
    /// Where a car's repair kit is kept (car frame): on the bottom shelf of its locker in a car with the crew lockers (note
    /// 151, the fitter's: train.json kit.lockers.kitLocker), the floor of the locker. Without them (or with
    /// <paramref name="floor"/>), on the floor just inside its front door, in the corner on the right of the aisle, ahead of
    /// the load (the cargo stands down the right side from 1.2 m in); in front of the tool lockers in a car that has them.
    /// The <paramref name="index"/>th of them half a metre further back.
    /// </summary>
    public static Ballast.Double3 RepairKitStowage(CarShape shape, Train.Box room, int index = 0, bool floor = false)
    {
        if (!floor && KitLocker(shape) is { } bay)
            return Lockers.SlotAt(bay, 0, 1, 0);
        var at = new Ballast.Double3(room.Max.X - 0.35, room.Min.Y + 0.1, room.Min.Z + 0.45 + 0.5 * index);
        foreach (var s in shape.Solids)
            if (s.Part == PartKind.Locker)
                at = new Ballast.Double3(s.Box.Max.X + 0.3, room.Min.Y + 0.1, (s.Box.Min.Z + s.Box.Max.Z) / 2 + 0.5 * index);
        return at;
    }

    /// <summary>The repair kit's locker in a car's shape (note 173): the one train.json names (the fitter's); null without lockers.</summary>
    public static LockerBay? KitLocker(CarShape shape) => shape.KitLocker >= 0 && shape.KitLocker < shape.Lockers.Count ? shape.Lockers[shape.KitLocker] : null;

    /// <summary>The repair kit's locker (note 173): its car and its place in the row; null on a train without lockers.</summary>
    public static (int Car, LockerBay Bay)? KitLocker(TrainOnLine train) =>
        RepairKitCar(train) is { } car && KitLocker(train.Frames[car].Shape) is { } bay ? (car, bay) : null;

    /// <summary>
    /// Host: each car with a room gets its wall-mounted extinguisher (GDD v1.1 App. C.5), by the door end; put back there (or
    /// left lying in its car), it recharges slowly.
    /// </summary>
    public void MountExtinguishers()
    {
        foreach (var v in Train.Dynamics.Consist.Vehicles)
            if (v.Id > 0 && Train.Frames[v.Id].Shape.Interior is { } room)
            {
                var b = Bodies.SpawnCrate(Train, v.Id, ExtinguisherMount(Train.Frames[v.Id].Shape, room), Physics.BodyKind.Extinguisher);
                b.Home = v.Id;
            }
    }

    /// <summary>
    /// Where a car's extinguisher stands on its mount (car frame, on the floor): the left wall, 2 m in from the front end, or
    /// just past the guard van's tool lockers where they stand along that wall. The art draws the bracket here.
    /// </summary>
    public static Ballast.Double3 ExtinguisherMount(CarShape shape, Train.Box room)
    {
        var at = new Ballast.Double3(room.Min.X + 0.3, room.Min.Y + 0.1, room.Min.Z + 2.0);
        foreach (var s in shape.Solids)
            if (s.Part == PartKind.Locker && at.X >= s.Box.Min.X - 0.2 && at.X <= s.Box.Max.X + 0.2 && at.Z >= s.Box.Min.Z - 0.3 && at.Z <= s.Box.Max.Z + 0.3)
                at = at with { Z = s.Box.Max.Z + 0.4 };
        // The crew lockers' row (note 173) runs back from just behind the front end wall: the board goes in the gap ahead of it.
        if (shape.Lockers.Count > 0 && shape.Lockers[0].Box.Min.X <= at.X + 0.2)
            at = at with { Z = (room.Min.Z + shape.Lockers[0].Box.Min.Z) / 2 };
        return at;
    }

    /// <summary>The route's boards and the hazards they warn of (sight.json), when playing a route.</summary>
    public Route.Lineside? Lineside { get; private set; }

    /// <summary>Puts up the route's boards: every machine reads them the same way; the host runs their hazards.</summary>
    public void EnableLineside(Route.SightTuning tuning, Route.Route route) => Lineside = new Route.Lineside(tuning, route);

    /// <summary>The crew as they acted this tick, on the host, with or without enemies (the lineside's hazards).</summary>
    readonly List<(int Id, PlayerState State, PlayerIntent Intent)> _actors = new();

    /// <summary>Tonight's run (departure, facilities, terminus, dawn), when playing a route.</summary>
    public Run.Run? Run { get; private set; }

    /// <summary>The headlamp is on.</summary>
    public bool LampShining => LampLit;

    /// <summary>The generated line whose track rules the host holds the train to (curves, weak bridges, washouts); null for a hand-laid one.</summary>
    public LineGen.LinePlan? TrackPlan { get; set; }
    /// <summary>What derailed the train (the report and the HUD say so): the track, a board run too fast, the Sleepers, the Switchman.</summary>
    public string? DerailCause { get; private set; }
    /// <summary>At the derail tick: the train's speed, and who was on the throttle (App. C.9's "speed at impact").</summary>
    public double DerailSpeed { get; private set; }
    public int DerailDriver { get; private set; } = -1;
    /// <summary>
    /// The derail's contributing action (App. C.9), as the cause card and the report read it: who made it (−1 for nobody)
    /// and the clerk's words, with <c>{actor}</c> where their name goes. The throttle for the track's dangers and the
    /// debris; the forward cannon for the Switchman; the firebox for a Stoker's runaway (note 190).
    /// </summary>
    public int DerailActor { get; private set; } = -1;
    public string DerailAction { get; private set; } = "";

    /// <summary>
    /// The host's music rotation (GDD v1.4 App. E.6): the manifest's tracks and the shuffle bag from the campaign save (or
    /// the app's, for a quick night). Only the host's world has one; it draws on the derail tick.
    /// </summary>
    public Music.MusicRotation? Music { get; set; }
    /// <summary>
    /// The derailment's track (<see cref="Sim.Music.MusicManifest.Key"/>; 0 for none): drawn by the host on the derail tick
    /// and replicated with the world, so every client plays the same opera. Presentation only: nothing simulates from it.
    /// </summary>
    public uint DerailMusic { get; set; }

    /// <summary>
    /// GDD v1.4 App. E.2 step 2, the derailment film's start: the wreck as it began, the crew as they were on the derail
    /// tick (flung from there), the seed and the cause card. The host's; sent to every client reliably, and each shoots the
    /// same film from it (<see cref="WreckFilm.Shoot"/>). Null till a derailment.
    /// </summary>
    public FilmStart? Film { get; set; }

    /// <summary>The film's been voted off (E.5 "Skipping"): every client cuts to the cause card. The host's; replicated.</summary>
    public bool FilmSkipped { get; set; }

    /// <summary>How many have voted to skip it, of how many (for the prompt). The host's; replicated.</summary>
    public (int Votes, int Of) FilmVotes { get; set; }

    /// <summary>
    /// The failure-attribution log (GDD v1.4 App. C.9), host-side: what happened to whom, and the contributing action. It
    /// feeds the incident report and nothing else.
    /// </summary>
    public Run.Attribution Attribution { get; } = new();

    /// <summary>
    /// The night's bookmarks (GDD v1.4 App. D.12): the host records where each still is to be taken from (GRAB starts,
    /// PUNISHes, the derailment's crew, the Stranded outro, a dead player's button); clients are sent them and mirror them.
    /// </summary>
    public Run.Bookmarks Bookmarks { get; } = new();

    /// <summary>The crew with their ids as the host last stepped bodies with them: who a night's end bookmarks.</summary>
    internal IReadOnlyList<(int Id, PlayerState State)> LastCrew { get; private set; } = [];

    /// <summary>The session's names for its crew by player id (the host's from each joiner's hello; clients are sent them).</summary>
    public Dictionary<int, string> Names { get; } = [];

    /// <summary>Starts the run. The host steps it (<see cref="StepRun"/>); clients mirror it from records.</summary>
    /// <param name="facilities">The facilities' loading modules (spec D); null for none.</param>
    /// <param name="loot">What the stops' containers hold (level-design P14): the yards' crates and castings, the villages' finds; null for none.</param>
    public void EnableRun(Run.RunTuning tuning, Route.Route route, double yardLength, bool authority, Run.FacilityTuning? facilities = null,
        Stops.LootTuning? loot = null)
    {
        TrackPlan ??= route.Plan;
        Run = new Run.Run(tuning, route) { YardLength = yardLength };
        Bookmarks.Tuning = tuning.Bookmarks;
        Forts = Sim.Run.Fortresses.Of(route, Train.Line, yardLength, tuning.TerminusZone);
        _walls = tuning.Walls;
        Train.Walls = LinesideToo(Sim.Run.StopWalls.Of(route, Train.Line, Forts, _walls), route);
        if (facilities is not null)
        {
            Run.EnableSites(facilities, Train.Line);
            // The switchyards' cars on their sidings (GDD §18; note 187), alike on the host and every client: so from the
            // route alone, not from anything one machine has and another mightn't (route.json's 12 m points).
            Run.StandCars(Train);
        }
        if (loot is not null)
        {
            // Note 301: where the wrench is the repair tool, the kit's gone, and none turns up at the stops either. (Kits are
            // rolled on their own stream, so the rest of a stop's loot is the same.)
            Run.EnableLoot(Repairs.ByWrench(Train) ? loot with { RepairKitChance = 0 } : loot, Train.Line, facilities);
            // GDD App. F.1's rare healing loot (note 272): which finds heal, and how long one takes to use.
            Bodies.Heals = Run.HealOf;
            Bodies.HealSeconds = loot.Healing?.UseSeconds ?? Bodies.HealSeconds;
        }
        Train.Walls = ClearSiteWork(Train.Walls);
        Authority |= authority;
    }

    /// <summary>
    /// Note 371: a generated line's trees, boulders and telegraph poles stand beside the stops' walls, out to
    /// run.json <c>walls.linesideReachM</c> from the line, none inside a fort (<see cref="Sim.Run.LinesideProps"/>).
    /// </summary>
    Sim.Run.StopWalls LinesideToo(Sim.Run.StopWalls walls, Route.Route route)
    {
        if (Sim.Run.LinesideProps.Of(route, Train.Line) is { } side && _walls is { LinesideReachM: > 0 } t)
        {
            walls.Add(side.Walls(Forts ?? [], t.LinesideReachM));
            // And the alternates' and dead lines' pines, out as far from their own track.
            walls.Add(side.BranchWalls(t.LinesideReachM));
        }
        return walls;
    }

    /// <summary>Note 279: the stops' walls less where a facility's modules are worked (<see cref="Sim.Run.Site.WorkPoints"/>), its yard cranes too.</summary>
    Sim.Run.StopWalls ClearSiteWork(Sim.Run.StopWalls walls) =>
        Run?.Sites is { Count: > 0 } sites ? walls.Clear(sites.Where(s => s is not null).SelectMany(s => s!.WorkPoints()), SiteWorkReachM) : walls;

    /// <summary>How far round a module's work point a stop's wall gives way: a crewmate's body and reach (m). Not a design number.</summary>
    const double SiteWorkReachM = 1.5;

    /// <summary>
    /// The departure fortress's town (GDD §3.1; note 281): its square, its people and papers, and their walls, built alike
    /// on every machine from the route and the content. Null where the content has no towns or they're switched off.
    /// </summary>
    public Towns.Town? Town { get; private set; }

    /// <param name="roster">The edition's creatures (enemies.json director.roster; empty, all): a town keeps only a custom
    /// for a creature this edition fields.</param>
    /// <param name="last">The custom of the last night's town, which this one won't have (App. F.1: "different from the last").</param>
    public void EnableTown(Towns.TownContent content, Route.Route route, double gate, IReadOnlyList<string> roster, string? last = null)
    {
        if (!content.Tuning.Enabled)
            return;
        var plan = Towns.TownGenerator.Generate(content, Towns.TownSite.Of(route, gate, roster, content, last));
        Town = new Towns.Town(plan, content.Tuning, Train.Line, content.Looks);
        // The departure fortress is the town's: its walls stand back round the square (note 281), so they're built again.
        if (Forts is { Count: > 0 } forts)
            Forts = [forts[0] with { Square = plan.Square, Bounds = plan.Bounds }, .. forts.Skip(1)];
        Train.Walls = ClearSiteWork(LinesideToo(Sim.Run.StopWalls.Of(route, Train.Line, Forts, _walls), route));
        Train.Walls.Add(Town.Walls);
    }

    // Host: who's holding Use by Nicki and how long, and who's had their glass tonight (note 571).
    readonly Dictionary<int, double> _byNicki = [];
    readonly SortedSet<int> _toasted = [];

    /// <summary>Nicki (note 571), where the town has her party: its host, else null.</summary>
    public Towns.Townsperson? Nicki => Town?.Plan.People.FirstOrDefault(p => p.Hosting);

    /// <summary>
    /// Whether Nicki has a glass for <paramref name="s"/> in reach (note 571): on foot, alive, near her, and not had theirs
    /// tonight. The host knows who has; a client goes by their being over full health, as only the wine puts anyone there.
    /// </summary>
    public bool WineInReach(in PlayerState s, int playerId)
    {
        if (!s.Alive || s.Parent != PlayerState.World || Town is not { } town || Nicki is not { } nicki
            || _toasted.Contains(playerId) || s.Health > Bodies.FullHealth)
            return false;
        return (PlayerMotor.WorldPosition(s, Train) - town.Feet(nicki)).Length <= town.Tuning.Wine.Reach;
    }

    /// <summary>
    /// Host: a crewmate by Nicki holding Use, empty-handed (note 571). Held <c>wine.holdSeconds</c>, they've taken a glass:
    /// <c>wine.health</c> on top of what they have, once a night. True while it's the wine their Use is on (not her door).
    /// </summary>
    bool WineAct(ref PlayerState s, in PlayerIntent intent, int playerId, bool emptyHanded)
    {
        if (!emptyHanded || !intent.Has(PlayerButtons.Use) || !WineInReach(s, playerId))
        {
            _byNicki.Remove(playerId);
            return false;
        }
        var t = Town!.Tuning.Wine;
        double held = _byNicki[playerId] = _byNicki.GetValueOrDefault(playerId) + SimConstants.TickSeconds;
        if (held >= t.HoldSeconds - 1e-9)
        {
            _toasted.Add(playerId);
            _byNicki.Remove(playerId);
            s.Health += t.Health;
        }
        return true;
    }

    /// <summary>Who's had a glass of Nicki's wine tonight (note 571), by player id; the host's.</summary>
    public IReadOnlyCollection<int> Toasted => _toasted;

    /// <summary>Note 279: the stops' buildings' walls by their doors, kept for the town's rebuild of the walls.</summary>
    Sim.Run.WallTuning? _walls;

    // Host: who's holding Use at which house door, how long, and whether this hold has already worked it (note 401).
    readonly Dictionary<int, (int Key, double Held, bool Done)> _atDoor = [];

    /// <summary>
    /// The door an open house's doorway has that a crewmate could work (note 401): on foot, alive, within run.json
    /// <c>walls.houseDoorReachM</c> of its doorway, with no hiding spot in reach (that's the search's). Alike on host and client,
    /// for the HUD.
    /// </summary>
    public Sim.Run.HouseDoor? DoorInReach(in PlayerState s)
    {
        if (!s.Alive || s.Parent != PlayerState.World || Train.Walls is not { HouseDoors.Count: > 0 } walls || Run?.SpotInReach(s, Train) is not null)
            return null;
        return walls.DoorInReach(PlayerMotor.WorldPosition(s, Train), (_walls ?? new Sim.Run.WallTuning()).HouseDoorReachM);
    }

    /// <summary>
    /// Host: a crewmate's hands on a house door this tick. Use held <c>walls.houseDoorSeconds</c> shuts an open one or opens a
    /// shut one, once a hold; let go and hold again to work it again. As a car's door is worked (CrewActions).
    /// </summary>
    // Who's been holding Use by Jacob, and how long (note 572).
    readonly Dictionary<int, double> _byJacob = [];

    /// <summary>
    /// Host: a crewmate on the ground holding Use within Jacob's reach for <c>jacob.holdSeconds</c> has had their word with
    /// him, and once a night he mends the train (<see cref="Bless"/>).
    /// </summary>
    void JacobAct(in PlayerState s, in PlayerIntent intent, int playerId)
    {
        var t = Enemies?.Jacob;
        var jacob = t is null ? null : _enemies.OfType<Sim.Enemies.Jacob>().FirstOrDefault(j => !j.Gone && !j.Blessed);
        if (jacob is null || !s.Alive || s.Parent != PlayerState.World || !intent.Has(PlayerButtons.Use)
            || (PlayerMotor.WorldPosition(s, Train) - jacob.Local).Length > t!.Reach)
        {
            _byJacob.Remove(playerId);
            return;
        }
        double held = _byJacob[playerId] = _byJacob.GetValueOrDefault(playerId) + SimConstants.TickSeconds;
        if (held < t.HoldSeconds - 1e-9)
            return;
        jacob.Bless();
        Bless();
        _byJacob.Clear();
    }

    /// <summary>
    /// Jacob's blessing (note 572; the director: "repair everything instantly, restoring it to brand new condition without
    /// affecting your loot count"). Every one of the train's own cars as new:
    /// <list type="bullet">
    /// <item>its body whole (dents and the Car Hugger's bites), its char gone, any breach closed;</item>
    /// <item>its axle box cool, its lamp trimmed and lit, its coupling tight;</item>
    /// <item>its gun cleared and cooled.</item>
    /// </list>
    /// The boiler whole and in steam again, its valve free and nothing in its firebox that shouldn't be; the forward lamp
    /// mended and lit; the brakes fresh; every fire out; every radio aboard working. Untouched: what's loaded and what it's
    /// worth (its load, cargo and cargo integrity), the finds, the coal in the tender and the powder (supplies, not the
    /// train's condition), and a car already gone (taken, or cut loose).
    /// </summary>
    public void Bless()
    {
        if (!Authority)
            return;
        var bt = Train.BoilerTuning;
        foreach (var v in Train.Dynamics.Consist.Vehicles)
        {
            if (v.Taken || v.Derelict || v.YardCar)
                continue;
            v.Integrity = 1;
            v.Eaten = 0;
            v.Char = [];
            v.Breached = false;
            v.HotBox = 0;
            v.Gutter = 0;
            v.Loose = 0;
            v.LampLit = true;
            // And what the creatures of 8 Oct leave broken (notes 364, 367), and a car the tipple threw off its rails (note 423).
            v.Wound = false;
            v.Seized = false;
            v.OffRails = false;
            if (v.HasGun)
                v.Gun = v.Gun with { Jammed = false, Cooldown = 0 };
        }
        if (bt is not null)
        {
            ref var b = ref Train.Boiler;
            if (b.Ruptured)
                b.Repair();
            b.Pressure = Math.Max(b.Pressure, bt.StartPressure);
            b.Firebox = Math.Max(b.Firebox, bt.StartFirebox);
            b.SafetyValveJammed = false;
            b.ExternalHeat = 0;
        }
        LampOutSeconds = 0;
        LampLit = true;
        Train.Dynamics.Restore(Train.Dynamics.Distance, Train.Dynamics.Velocity, 1);
        foreach (var e in _enemies)
            if (e.Kind == EnemyKind.CarFire && !e.Gone)
                e.Dismiss();
        foreach (var radio in Bodies.All.Where(b => b.Kind == Physics.BodyKind.Radio))
            radio.Broken = false;
    }

    void DoorAct(in PlayerState s, in PlayerIntent intent, int playerId, bool emptyHanded)
    {
        if (!emptyHanded || !intent.Has(PlayerButtons.Use) || intent.MoveZ > 0.5 || DoorInReach(s) is not { } door)
        {
            _atDoor.Remove(playerId);
            return;
        }
        var (key, held, done) = _atDoor.TryGetValue(playerId, out var was) && was.Key == door.Key ? was : (door.Key, 0.0, false);
        held += SimConstants.TickSeconds;
        if (!done && held >= (_walls ?? new Sim.Run.WallTuning()).HouseDoorSeconds - 1e-9)
        {
            Train.Walls!.SetShut(key, !Train.Walls.Shut(key));
            done = true;
        }
        _atDoor[playerId] = (key, held, done);
    }

    /// <summary>The night's fortresses (<see cref="Sim.Run.Fortresses.Of"/>; T124), the departure one's town square on it once there's a town.</summary>
    public IReadOnlyList<Sim.Run.Fort>? Forts { get; private set; }

    /// <summary>
    /// GDD App. D: the Holdouts at the route's halts, villages and yards, and the respawn queue, the only way back into
    /// a run once the gate has opened. The host steps them (<see cref="StepHoldouts"/>); clients mirror them.
    /// </summary>
    public Run.Holdouts? Holdouts { get; private set; }
    public void EnableHoldouts(Run.HoldoutTuning tuning, Route.Route route) => Holdouts = new Run.Holdouts(tuning, route, Train.Line);

    /// <summary>Host, after bodies: the queue and every Holdout (App. D.5-D.8). Returns what happened.</summary>
    /// <param name="crew">Everyone in the session, living, dead and waiting, in order.</param>
    public List<Run.HoldoutEvent> StepHoldouts(IReadOnlyList<(int Id, PlayerState State)> crew, Action<int, PlayerState> set)
    {
        if (!Authority || Holdouts is not { } h || Run is not { Phase: not Sim.Run.RunPhase.Yard } run)
            return [];
        var events = h.Step(this, crew, set, SimConstants.TickSeconds);
        // D.12: rescues, with who freed whom and at which site.
        foreach (var e in events.Where(e => e.Kind == Sim.Run.HoldoutEventKind.Freed))
        {
            var site = h.All[e.Holdout];
            Attribution.Add(new Sim.Run.Incident(Sim.Run.IncidentKind.Rescue, run.Seconds, e.PlayerId, "Freed from the Holdout",
                Sim.Run.IncidentLog.At(this, site.Inside, site.LineHint), e.By, e.By >= 0 ? "Broken out by {actor}." : ""));
        }
        return events;
    }

    /// <summary>Host: advances the run after the world and damage are applied, with everyone's state.</summary>
    public void StepRun(IReadOnlyCollection<PlayerState> crew)
    {
        if (Authority)
            Run?.Step(this, crew, SimConstants.TickSeconds);
    }

    /// <summary>Puts an enemy into the world directly (tests, the editor, scripted set pieces). Host only.</summary>
    public T AddEnemy<T>(Func<int, T> make) where T : Enemy
    {
        var e = make(_nextEnemyId++);
        _enemies.Add(e);
        return e;
    }

    /// <summary>Off the rails, for a cause whose contributing action is the throttle (App. C.9: the track, the debris).</summary>
    public void Derail(string? why = null) =>
        Derail(why, Attribution.Driver, Attribution.Driver >= 0 ? "Throttle: {actor}." : "Nobody on the throttle.");

    /// <summary>
    /// Off the rails for going too fast (a bend, the Sleepers). With a Stoker feeding the fire, it's the Stoker's runaway
    /// (App. A.5 "past the next curve's limit, the train derails"), and C.9's row for it is the firebox, not the throttle:
    /// who last fuelled or tended it, and how long it had gone unattended (note 190).
    /// </summary>
    public void Overspeed(string why)
    {
        if (_enemies.Any(e => e is Stoker { Feeding: true }))
        {
            var (actor, action) = Sim.Run.IncidentLog.Firebox(this);
            Derail($"the Stoker ran away with it: {why}", actor, action);
        }
        else
            Derail(why);
    }

    /// <summary>Off the rails, with the contributing action C.9 names for this cause (<see cref="DerailAction"/>).</summary>
    public void Derail(string? why, int actor, string action)
    {
        if (!Derailed)
        {
            DerailCause = why;
            DerailSpeed = Train.Dynamics.Speed;
            DerailDriver = Attribution.Driver;
            DerailActor = actor;
            DerailAction = action;
            // E.6: the host draws tonight's opera from the bag, weighted by the speed it came off at, from the same seed
            // as the wreck's (deterministic); clients are sent the key.
            if (Music?.Draw(DerailSpeed, (ulong)Tick * 0x9E3779B97F4A7C15UL ^ (Route?.Seed ?? 0) ^ 0xE6UL) is { } track)
                DerailMusic = Sim.Music.MusicManifest.Key(track.Id);
            // T117: off the rails, every car carries on as itself, into the ground and into each other. The host's; the
            // clients are sent the poses. Thrown outward off the curve it was on, if it was on one.
            if (Train.Wreck is null)
            {
                double k = Train.Line.Sample(Train.Dynamics.Distance).Curvature;
                ulong seed = (ulong)Tick * 0x9E3779B97F4A7C15UL ^ (Route?.Seed ?? 0);
                Train.Wreck = Wreck.Begin(WreckTuning, Train, Ground, seed, first: Train.Dynamics.Consist.Vehicles[0].Id, outward: k > 1e-6 ? -1 : k < -1e-6 ? 1 : 0);
                // E.2 step 2: the crew as they were this tick, alive, and the wreck as it began, for the film. (Only what
                // simulates the train derails it; a client's wreck is a puppet of the host's, and its film is sent.)
                Film = WreckFilm.StartOf(Train.Wreck, FilmCrew(), Sim.Run.IncidentLog.CauseCard(this), DerailSpeed, Train.Dynamics.Distance,
                    v => v >= 0 && v < Train.Frames.Count && (Train.Frames[v].Shape.Interior ?? Train.Frames[v].Shape.Cab) is { } room ? room.Min.Y : 0,
                    v => v >= 0 && v < Train.Vehicles.Count && Train.Vehicles[v].HasGun && Sim.Combat.Guns.Mount(Train, v) is { } mount
                        ? (mount.Position, Sim.Combat.Guns.FacingYaw(mount) + Train.Vehicles[v].Gun.Traverse) : null,
                    v => v >= 0 && v < Train.Frames.Count ? [.. Train.Frames[v].Shape.DoorList.Select(d => d.Box)] : []);
                // App. E.3's extras (note 373): the stowed dead and the loose things aboard go into the wreck too.
                Film = Film with { Extras = FilmExtras(Film) };
                // App. E.2 step 1 (the director's decision of 5 Oct 2026): nobody dies on the derail tick. The film's own
                // physics, recorded now, says when each of the crew takes the hit that kills them; they die then, as it lands
                // in their own first person (FilmTuning.DeathDelay), and every client's first person ends on its own.
                _recording = WreckFilm.Record(WreckTuning, Film, FilmGround(Film), FilmWater());
                _filmRecorded = Film;
                _derailTick = Tick;
                _doomedAt.Clear();
                foreach (var (id, death) in _recording.Deaths)
                    _doomedAt[id] = Tick + (uint)Math.Ceiling(WreckTuning.Film.DeathDelay(death.At) * SimConstants.TickRate - 1e-9);
            }
        }
        Derailed = true;
        // GDD v1.4 App. E.4 O12, §23 "lights fail": the lamps die in the wreck, the forward lamp and every car's.
        SmashLamp(1e5);
        foreach (var v in Train.Vehicles)
            v.LampLit = false;
        foreach (var rake in Train.Rakes)
            rake.Velocity = 0;
    }

    /// <summary>
    /// Everyone alive on the derail tick (this tick's actors), for the film (E.3): where they were in the world and how fast
    /// they were going with their car, which car they were inside (they tumble about in it), and their name card.
    /// </summary>
    List<FilmPlayer> FilmCrew()
    {
        var crew = new List<FilmPlayer>();
        foreach (var (id, s, _) in _actors.OrderBy(a => a.Id))
        {
            if (!s.Alive)
                continue;
            int inside = s.Parent >= 0 && s.Parent < Train.Frames.Count && PlayerMotor.Indoors(s, Train) ? s.Parent : -1;
            // What they were at (App. F.2 take 4): the film starts their body in it.
            var task = s.Has(PlayerFlags.Seated) ? FilmTask.Gunning
                : Bodies.CarriedBy(id) is not null ? FilmTask.Carrying
                : PlayerMotor.InCab(s, Train) ? Net.CabControls.CanDrive(s, Train) && Attribution.Driver == id ? FilmTask.Driving : FilmTask.Firing
                : FilmTask.None;
            // In their arms, a load goes into the wreck with them (note 370); someone carried isn't one.
            var load = task == FilmTask.Carrying && Bodies.CarriedBy(id) is { Kind: not (Physics.BodyKind.Ragdoll or Physics.BodyKind.Child) } carried ? carried : null;
            crew.Add(new FilmPlayer(id, Sim.Run.IncidentLog.NameOf(this, id), Sim.Run.IncidentLog.Role(this, s, id),
                PlayerMotor.WorldPosition(s, Train), PlayerMotor.WorldVelocity(s, Train), PlayerMotor.WorldYaw(s, Train), inside, s.Has(PlayerFlags.Seated), task,
                load?.Kind, load?.Cargo ?? CargoKind.None));
        }
        return crew;
    }

    FilmRecording? _recording;
    FilmStart? _filmRecorded;
    uint _derailTick;
    readonly Dictionary<int, uint> _doomedAt = [];
    readonly HashSet<int> _countedAhead = [];
    bool _doomed;

    /// <summary>
    /// Host, after a derailment (App. E.2 step 1, the director's decision of 5 Oct 2026): the tick each of the crew dies on,
    /// their first hard hit in the film's physics as it lands in their first person. Empty before, and on a client.
    /// </summary>
    public IReadOnlyDictionary<int, uint> DoomedAt => _doomedAt;

    /// <summary>The tick the train came off (host).</summary>
    public uint DerailTick => _derailTick;

    /// <summary>
    /// Off the rails, the living are the wreck's, not their own (host and a predicting client alike): nothing they press
    /// moves them; they ride where they were till the hit that kills them. Only the skip vote still counts (E.5).
    /// </summary>
    public bool Wrecked(in PlayerState s) => Derailed && s.Alive;

    /// <summary>What's left of an intent once the wreck has you (<see cref="Wrecked"/>): the skip vote.</summary>
    public static PlayerIntent WreckedIntent(in PlayerIntent i) => new() { Actions = i.Actions & PlayerActions.Skip };

    /// <summary>The water's surface over a point for the film (note 373: bodies float in it), as the guns find it.</summary>
    Func<double, double, double?> FilmWater() => (x, z) => Sim.Combat.Guns.Water(Train, new Ballast.Double3(x, 0, z));

    /// <summary>
    /// App. E.3's extras (note 373): every loose thing aboard a car at the derail tick (the stowed dead, crates, loot, the
    /// extinguishers; not what's in someone's hands, shut in a locker, or being carried off), in the world at its car's
    /// velocity there, within the film's body budget (those nearest the crew).
    /// </summary>
    List<FilmExtra> FilmExtras(FilmStart start)
    {
        var cars = start.Cars.ToDictionary(c => c.Vehicle);
        var all = new List<FilmExtra>();
        foreach (var b in Bodies.All)
        {
            if (b.Parent < 0 || b.Parent >= Train.Frames.Count || b.Carrier >= 0 || b.Stowed || b.TakenBy >= 0 || !cars.TryGetValue(b.Parent, out var car))
                continue;
            var f = Train.Frames[b.Parent];
            Ballast.Double3[] joints = b.Kind == Physics.BodyKind.Ragdoll ? [.. b.Pbd.Particles.Select(p => f.ToWorld(p.Position))] : [f.ToWorld(b.Centre)];
            var at = joints.Length > 2 ? joints[2] : joints[0];
            all.Add(new FilmExtra(b.Kind, joints, car.Velocity + Ballast.Double3.Cross(car.Spin, at - car.Origin), b.Parent, b.Owner, b.Cargo));
        }
        return WreckFilm.Budget(all, [.. start.Players.Select(p => p.Position)], WreckTuning.Film);
    }

    Func<double, double, double> FilmGround(FilmStart start) => (x, z) =>
    {
        double hint = start.Along;
        return PlayerMotor.GroundAt(new Ballast.Double3(x, 0, z), Train.Line, ref hint);
    };

    /// <summary>The wreck's numbers (wreck.json): the default until the session loads them.</summary>
    public WreckTuning WreckTuning { get; set; } = new();

    /// <summary>
    /// The jobs the train makes as it runs (upkeep.json; orchestrator.md §5.1): null, none. Set on every machine (the hot
    /// boxes' seconds and drag are predicted); the host brings them on and sets the fires (<see cref="Train.HotBoxes"/>).
    /// </summary>
    public UpkeepTuning? Upkeep
    {
        get => _upkeep;
        set
        {
            _upkeep = value;
            Train.HotBoxTuning = value?.HotBox is { Enabled: true } hb ? hb : null;
            Train.Gutter = value?.Lamp is { Enabled: true } lt ? lt : null;
            Train.Loose = value?.Coupling is { Enabled: true } ct ? ct : null;
            _hotBoxes = null;
            _gutters = null;
            _couplings = null;
        }
    }
    UpkeepTuning? _upkeep;
    HotBoxes? _hotBoxes;
    Gutters? _gutters;
    Couplings? _couplings;
    /// <summary>Host: the guns' racks filled from the powder locker tonight (note 374), for the harness.</summary>
    public int RacksFilled { get; private set; }

    /// <summary>The gun this player's at whose ready rack wants powder from the lockers (note 374), or null.</summary>
    int? Charging(in PlayerState s, GunTuning t) =>
        Guns.MannedGun(s, Train, t) is { } g && Guns.Ready(Train.Vehicles[g].Gun, t) < t.Rack && Guns.Stowed(Train, t) > 0 ? g : null;

    /// <summary>
    /// Host: a charge in hand at a gun that wants it, Use held (standing) <see cref="GunTuning.ChargeSeconds"/>: the rack's
    /// filled and the charge is spent. Let go, and it starts again.
    /// </summary>
    void Charge(in PlayerState s, in PlayerIntent intent, int playerId, GunTuning t)
    {
        if (Bodies.CarriedBy(playerId) is not { Kind: Physics.BodyKind.Powder } charge || Charging(s, t) is not { } gun)
            return;
        if (!intent.Has(PlayerButtons.Use) || Math.Abs(intent.MoveX) > 0.5 || Math.Abs(intent.MoveZ) > 0.5)
        {
            charge.MendTicks = 0;
            return;
        }
        if (++charge.MendTicks * SimConstants.TickSeconds < t.ChargeSeconds)
            return;
        if (Guns.Fill(Train, gun, t) > 0)
        {
            Bodies.Remove(charge);
            RacksFilled++;
            RacksFilledBy[playerId] = RacksFilledBy.GetValueOrDefault(playerId) + 1;
        }
        else
            charge.MendTicks = 0;
    }

    /// <summary>Host: the racks filled so far (note 377, the harness's upkeep report), by who carried the charge up.</summary>
    public SortedDictionary<int, int> RacksFilledBy { get; } = [];
    /// <summary>Host: the night's loose couplings so far (note 356): how many worked loose, and how many parted.</summary>
    public (int Came, int Parted) LooseCount => _couplings is { } c ? (c.Came, c.Parted) : (0, 0);
    /// <summary>Host: the night's guttering lamps so far (note 346): how many started, and how many went out.</summary>
    public (int Came, int WentOut) GutterCount => _gutters is { } g ? (g.Came, g.WentOut) : (0, 0);
    /// <summary>Host: the night's hot boxes so far (note 331): how many came on, and how many caught.</summary>
    public (int Came, int Caught) HotBoxCount => _hotBoxes is { } h ? (h.Came, h.Caught) : (0, 0);

    double _groundHint;

    /// <summary>The land's height under a point (the line's terrain, or the ballast by a hand-laid line).</summary>
    double Ground(double x, double z) => PlayerMotor.GroundAt(new Ballast.Double3(x, 0, z), Train.Line, ref _groundHint);

    /// <summary>
    /// Shoots the derailment film from <see cref="Film"/> over this world's ground (E.2 steps 3 and 4), the same on every
    /// machine: the ground's own search hint is fresh, so nothing this world did before changes a height.
    /// </summary>
    public WreckFilm? ShootFilm()
    {
        if (Film is not { } start)
            return null;
        // The host recorded it on the derail tick (for the deaths); a client records the same from the start it's sent.
        var recorded = _recording;
        return WreckFilm.Shoot(WreckTuning, start, FilmGround(start), ReferenceEquals(start, _filmRecorded) ? recorded : null, FilmWater());
    }

    /// <summary>
    /// One player's hands this tick: crew actions at interactables, and firing a gun they're manning.
    /// <paramref name="viewTick"/> is when the shooter saw the targets (lag compensation: the host checks
    /// hits against where things were on the shooter's screen).
    /// </summary>
    public void CrewAct(ref PlayerState s, in PlayerIntent intent, int playerId, uint? viewTick = null)
    {
        PlayerMotor.Look(ref s, intent);
        PlayerMotor.TakeHand(ref s, intent, Hand);
        // The lamp switch in the cab (T52, "lamps down"): a predicting client sets it too, so the lamp goes out at once.
        if (intent.Lamp != LampSwitch.None && Net.CabControls.CanDrive(s, Train))
        {
            LampLit = intent.Lamp == LampSwitch.On && LampOutSeconds <= 0;
            _relight &= intent.Lamp == LampSwitch.On;
        }
        if (Authority && Run is { } run)
        {
            run.CrewAct(s, intent, playerId, Train, Hand);
            // Spec D.2 "dropped loads kill": under a casting the crane let go of.
            if (run.Crushes(s, Train))
                Damage.Add(new Enemies.DamageEvent(playerId, 1000, DeathCause.Crushed));
            // GDD §18 (note 185): a powder keg's blast, a leaking hose's gas.
            Damage.AddRange(run.Harm(s, playerId, Train));
        }
        if (Authority && Switches?.CrewAct(s, intent, playerId, Train, Hand) is { } thrown)
            SwitchThrows.Add(thrown);
        // The repair kit in hand at a Holdout's door is opening it (GDD App. D.7), and at a ruptured boiler's firebox mending
        // it (T109): not being put down.
        bool kit = Authority && Bodies.CarriedBy(playerId) is { Kind: Physics.BodyKind.RepairKit };
        // Note 301: where the wrench is the repair tool, it's the wrench in hand that opens a lock quietly, as the kit did.
        bool picks = Repairs.ByWrench(Train) ? Authority && Repairs.WrenchInHand(s) && Bodies.CarriedBy(playerId) is null : kit;
        // Smash and pry are a melee tool's (D.7; note 275): with empty hands only the kit opens a lock.
        bool breaching = Authority && Holdouts?.CrewAct(s, intent, playerId, Train, picks, Player.Kit.Held(s) != Player.Tool.None) == true;
        // Powder to the guns (note 374): a charge in hand at a gun whose rack wants it is being loaded, not put down; empty
        // hands at a powder locker with powder in it take a charge.
        var gunTuning = Combat?.Guns;
        bool charging = Authority && gunTuning is { Rack: > 0 } && Bodies.CarriedBy(playerId) is { Kind: Physics.BodyKind.Powder } && Charging(s, gunTuning) is not null;
        var fetch = Authority && gunTuning is { Rack: > 0 } && Guns.AtLocker(s, Train, gunTuning) is not null && Guns.Stowed(Train, gunTuning) > 0
            ? Physics.BodyKind.Powder : (Physics.BodyKind?)null;
        // Hands first: a Use press that picks something up (or puts it down) isn't also working a lever. Except at a switch's
        // lever, which takes Use whatever's in your hands (queue #94, note 357): the lamp you carried out to a stand stays lit
        // in your hand while you throw it, and a crate lying by it stays down.
        bool lever = Authority && Switches?.InReach(s, Train, Hand) is not null;
        bool handsTookIt = Authority && Bodies.Handle(s, intent, playerId, Train, Hand,
            keep: kit && (breaching || CrewActions.AtTheRupture(s, Train, Hand)) || charging, lever: lever, fetch: fetch);
        if (charging && gunTuning is not null)
            Charge(s, intent, playerId, gunTuning);
        if (handsTookIt && Bodies.CarriedBy(playerId) is { Kind: Physics.BodyKind.Ragdoll } lifted)
            Physics.Bodies.TakeTools(ref s, lifted);
        // Searching an open house's hiding spot (note 326), empty-handed, with a Use the hands didn't take.
        if (Authority && Run is { } searching)
            searching.SearchAct(s, intent, playerId, this, emptyHanded: !handsTookIt && Bodies.CarriedBy(playerId) is null);
        // Nicki's wine (note 571), then an open house's door (note 401): empty-handed, with a Use neither the hands nor a hiding
        // spot took. (She waves you in at her door: by her, a held Use is a glass, not the door shut in her face.)
        if (Authority && !WineAct(ref s, intent, playerId, emptyHanded: !handsTookIt && Bodies.CarriedBy(playerId) is null))
            DoorAct(s, intent, playerId, emptyHanded: !handsTookIt && Bodies.CarriedBy(playerId) is null);
        JacobAct(s, intent, playerId);
        // A healing find used up in the hands this tick (GDD App. F.1; note 272): its health back, up to full.
        if (Authority && Bodies.TakeDose(playerId) is > 0 and var dose && s.Alive)
            s.Health = Math.Min(Bodies.FullHealth, s.Health + dose);
        // At the crane's controls, the stick drives the crane, not your feet (T48); taken and let go with a press of Use at
        // the stand (Crane.Operates). Worked out the same everywhere, so a client predicts standing still at the stand.
        bool operating = false;
        if (Run?.CurrentSite is { } site)
            foreach (var crane in site.Cranes)
                operating |= crane.Operates(s, intent, Train);
        s.Flags = operating ? s.Flags | PlayerFlags.Operating : s.Flags & ~PlayerFlags.Operating;
        if (Authority)
        {
            // Freight in your arms slows you and keeps you off ladders (spec B.2); the motor reads the flag.
            // GDD v1.4 App. C.4 and D.9 (note 181): hand-carried loot is carried the same way: a toy, a find, the child, a body.
            bool heavy = Bodies.All.Any(b => b.HeldBy(playerId) && b.Kind is Physics.BodyKind.Cargo or Physics.BodyKind.Heavy
                or Physics.BodyKind.Toy or Physics.BodyKind.Loot or Physics.BodyKind.Child or Physics.BodyKind.Ragdoll);
            s.Flags = heavy ? s.Flags | PlayerFlags.Heavy : s.Flags & ~PlayerFlags.Heavy;
            // D.9's solo remainer: the last one alive, with a body, may still climb (slowly).
            bool solo = s.Alive && Bodies.CarriedBy(playerId) is { Kind: Physics.BodyKind.Ragdoll } && LastCrew.Count(c => c.State.Alive && c.Id != playerId) == 0;
            s.Flags = solo ? s.Flags | PlayerFlags.SoloCarry : s.Flags & ~PlayerFlags.SoloCarry;
            bool repairKit = Bodies.CarriedBy(playerId) is { Kind: Physics.BodyKind.RepairKit };
            s.Flags = repairKit ? s.Flags | PlayerFlags.RepairKit : s.Flags & ~PlayerFlags.RepairKit;
        }
        if (!handsTookIt)
        {
            // App. C.9's contributing actions, as the host sees them made: who fired or vented, who pulled a coupler.
            // (Wherever the crew act is worked: the host's log is the one that's read, and a client's only ever says "last".)
            double firebox = Train.Boiler.Firebox;
            bool venting = Train.Boiler.Venting, door = Train.Boiler.FireDoorOpen;
            int cars = Train.Dynamics.Consist.Vehicles.Count;
            var attached = Train.Dynamics.Consist.Vehicles.Select(v => v.Id).ToArray();
            CrewActions.Apply(ref s, intent, Train, SimConstants.TickSeconds, Hand);
            if (Train.Boiler.Firebox > firebox + 1e-9 || Train.Boiler.Venting && !venting)
                Attribution.Fired(playerId, Run?.Seconds ?? 0);
            else if (Train.Boiler.FireDoorOpen && !door)
                Attribution.Tended(playerId, Run?.Seconds ?? 0); // a shovelful into a full firebox still opens its door
            if (Train.Dynamics.Consist.Vehicles.Count < cars)
                foreach (int v in attached)
                    if (Train.Dynamics.Consist.IndexOf(v) < 0)
                        Attribution.PulledCoupler(v, playerId);
        }
        if (s.Alive && Bodies.CarriedBy(playerId) is { Kind: Physics.BodyKind.RepairKit })
            Attribution.HeldKit(playerId);
        if (operating)
            Attribution.Craned(playerId);
        // The gun's seat (T112): sat in or got up from, the view held to the gun's arc and the gun laid after it, before it fires.
        if (Combat is { } cs)
            Guns.Sit(ref s, intent, Train, cs.Guns, SimConstants.TickSeconds);
        var targets = viewTick is { } vt && _targetHistory.TryGetValue(vt, out var then) ? then : Targets;
        if (Combat is { } c && Guns.TryFire(s, intent, Train, c.Guns, ref Choir, c.Choir, targets, Tick, playerId) is { } shot)
        {
            Shots.Add(shot);
            // The round's burst, in the gunner's name: it lifts the meter by roundLoudness, which the window takes to fall away.
            if (Authority)
                CreditChoir(playerId, c.Choir.RoundLoudness * c.Choir.WindowSeconds);
            // GDD §14: "a cannon solves the immediate problem while alerting everything nearby" (note 287).
            Drew(DrawCause.Cannon, playerId, Director?.Tuning.Draw.Weight(DrawCause.Cannon) ?? 0);
            // B.9: the fumes, once everyone's moved this tick (note 182).
            if (Authority && c.Fumes is not null)
                _fumes.Add(shot);
        }
        if (Combat is { } cr)
            Guns.Reload(s, intent, Train, cr.Guns, SimConstants.TickSeconds);
        // Pushing the gun along its roof rail (T93), worked out alike everywhere so a client predicts it: the motor moves it.
        bool pushing = Combat is { } cp && Guns.Pushing(s, intent, Train, cp.Guns);
        s.Flags = pushing ? s.Flags | PlayerFlags.Pushing : s.Flags & ~PlayerFlags.Pushing;
        // The whistle cord, in the cab (GDD §12): a blast, loud, and every client hears it.
        // Note 267: or Use held on the cord's handle, looked at (CrewActions picks it only so). Either way it's in the puller's
        // name: their share of the loudness meter, and the HUD's "on the cord" (the Whistler's blows with no name, App. A.4).
        if ((intent.Has(PlayerActions.Whistle) || OnTheCord(s, intent)) && Net.CabControls.CanDrive(s, Train))
            Whistled(1.0, playerId);
        // The lamp in the car you're in (GDD v1.1 App. A.5): on the press, the host's to set.
        if (Authority && intent.Has(PlayerActions.CarLamp) && !_lampWas.Contains(playerId) && s.Parent > 0 && s.Parent < Train.Frames.Count
            && PlayerMotor.Indoors(s, Train))
        {
            // Guttering (note 346), the press trims it: it burns steady again, and stays lit (nothing newly lit to be seen).
            var car = Train.Vehicles[s.Parent];
            if (car is { LampLit: true, Gutter: > 0 })
                car.Gutter = 0;
            else
            {
                car.LampLit = !car.LampLit;
                if (car.LampLit)
                {
                    Attribution.LitLamp(s.Parent, playerId);
                    // A light in the dark is seen (note 287).
                    Drew(DrawCause.Lamp, playerId, Director?.Tuning.Draw.Weight(DrawCause.Lamp) ?? 0);
                }
            }
        }
        if (intent.Has(PlayerActions.CarLamp)) _lampWas.Add(playerId); else _lampWas.Remove(playerId);
        // An emote (note 298): the crew see it, and that's all. Not while something has hold of you, and not on top of the
        // last one picked.
        if (Hosting && intent.Emote != Emote.None && s.Alive && !s.Has(PlayerFlags.Held)
            && !Emotes.Any(e => e.By == playerId && (Tick - e.Tick) * SimConstants.TickSeconds < EmoteTuning.Cooldown))
        {
            Emotes.RemoveAll(e => e.By == playerId);
            Emotes.Add(new EmoteEvent(_nextFx, Tick, playerId, intent.Emote));
            _nextFx = _nextFx % 0xFFFFFF + 1;
        }
        if (Authority && _context is { } ec)
        {
            // Melee (App. C.2): a swing with the tool you carry, at what's in front of you.
            if (intent.Has(PlayerActions.Swing) && s.Alive && !s.Has(PlayerFlags.Held) && Guns.MannedGun(s, Train, Combat?.Guns ?? DefaultGun) is null)
                Swing(ec, s, playerId);
            // Standing idle (Tippy Toesie's mark): still, and not working anything.
            double moving = s.Velocity.Length;
            // Driving a moving train is working it (T115 playtest: the driver, watching the line, was Tippy Toesie's mark).
            bool driving = Net.CabControls.CanDrive(s, Train) && Math.Abs(Train.Dynamics.Speed) > 1;
            bool idle = moving < ec.Tuning.TippyToesie.IdleBelow && intent.MoveX == 0 && intent.MoveZ == 0 && intent.Buttons == PlayerButtons.None
                && intent.Actions == PlayerActions.None && !driving;
            IdleSeconds[playerId] = idle ? IdleSeconds.GetValueOrDefault(playerId) + SimConstants.TickSeconds : 0;
            // Alone, there's no friend to act: at a crew of one the held can struggle free (the solo rule, T89).
            if (s.Has(PlayerFlags.Held) && intent.Has(PlayerButtons.Use) && ec.Tuning.Grab.SoloStruggleOn && _context.Crew.Count(x => x.Player.State.Alive) <= 1)
                foreach (var holder in _enemies.Where(e => e.Phase == SpinePhase.Grab && e.Holding == playerId).ToList())
                    holder.Struggle(ec, SimConstants.TickSeconds);
        }
        _context?.Crew.Add((new PlayerSnapshot((byte)playerId, s), intent));
        if (Authority)
            _actors.Add((playerId, s, intent));
    }

    static readonly GunTuning DefaultGun = new(1, 0, 0, 0, 0, 0, 0, 1.0, 0, 0);
    /// <summary>Host: this tick's shots, for the chemicals' fumes once the whole crew has acted (App. B.9; note 182).</summary>
    readonly List<GunShot> _fumes = new();

    /// <summary>
    /// A cannon fired beside a chemicals car (App. B.9: "lethal to the crew"; note 182): the fumes go up from every loaded
    /// chemicals car in the gun's rake within <see cref="FumesTuning.Cars"/> couplings of the gun's car, and the gunner and
    /// everyone within <see cref="FumesTuning.GasM"/> of such a car is gassed. Host only; the crew's places are last tick's.
    /// </summary>
    void Fumes(GunShot shot, FumesTuning f)
    {
        int gunner = shot.Shooter;
        var rake = Train.RakeOf(shot.GunVehicle).Consist.Vehicles;
        int at = rake.ToList().FindIndex(v => v.Id == shot.GunVehicle);
        if (at < 0)
            return;
        var cars = rake.Where((v, i) => Math.Abs(i - at) <= f.Cars && v.Kind == VehicleKind.Cargo && v.Cargo == CargoKind.Chemicals && v.Load > 0.01
            && v.CargoIntegrity > 0.01).Select(v => v.Id).ToList();
        // GDD §18 "do not fire indoors" (note 185): under a chemical works' pipe rack the gun's own car is as bad as one.
        if (Run?.Indoors(Train.Frames[shot.GunVehicle].Origin) == true && !cars.Contains(shot.GunVehicle))
            cars.Add(shot.GunVehicle);
        if (cars.Count == 0)
            return;
        Attribution.Gassed(gunner);
        var victims = new SortedSet<int>();
        foreach (var (id, st, _) in _actors)
            if (st.Alive && (id == gunner || cars.Any(car => FromCar(car, PlayerMotor.WorldPosition(st, Train) + Ballast.Double3.Up) <= f.GasM)))
                victims.Add(id);
        foreach (int id in victims)
            Damage.Add(new DamageEvent(id, f.Damage, DeathCause.Poisoned, Lethal: true));
    }

    /// <summary>How far a world point is from a car's body (0 inside it).</summary>
    double FromCar(int car, Ballast.Double3 world)
    {
        var frame = Train.Frames[car];
        var local = frame.ToLocal(world);
        var b = frame.Shape.Bounds;
        var near = new Ballast.Double3(Math.Clamp(local.X, b.Min.X, b.Max.X), Math.Clamp(local.Y, b.Min.Y, b.Max.Y), Math.Clamp(local.Z, b.Min.Z, b.Max.Z));
        return (local - near).Length;
    }
    readonly HashSet<int> _lampWas = new();

    /// <summary>
    /// A tool's swing (App. C.2): the nearest thing in front within reach that can be struck takes a blow. The host decides,
    /// generous in reach for a remote crewmate's latency (GDD §33's lag compensation, first pass: the reach, not a rewind).
    /// </summary>
    void Swing(EnemyContext ctx, in PlayerState s, int playerId)
    {
        var t = ctx.Tuning.Melee;
        if (_swingReady.TryGetValue(playerId, out uint ready) && Tick < ready)
            return;
        _swingReady[playerId] = Tick + (uint)Math.Round(t.SwingSeconds * SimConstants.TickRate);
        // Seen by everyone, whatever it hits (note 197).
        Swings.Add(new SwingEvent(_nextFx, Tick, playerId));
        _nextFx = _nextFx % 0xFFFFFF + 1;
        var eye = PlayerMotor.WorldPosition(s, Train) + Ballast.Double3.Up * 1.3;
        double yaw = PlayerMotor.WorldYaw(s, Train);
        var facing = new Ballast.Double3(-DMath.Sin(yaw), 0, -DMath.Cos(yaw));
        double cos = DMath.Cos(t.ConeDegrees * Math.PI / 180);
        Enemy? best = null;
        double bestD = double.MaxValue;
        foreach (var e in _enemies)
        {
            if (e.Gone || !e.Strikable(playerId) || !e.Reachable(this))
                continue;
            var to = e.WorldPosition(Train) + Ballast.Double3.Up * 0.8 - eye;
            double d = to.Length;
            if (d > t.Reach + e.MeleeRadius)
                continue;
            var flat = to with { Y = 0 };
            if (flat.Length > 0.4 && Ballast.Double3.Dot(flat.Normalized, facing) < cos)
                continue;
            if (d < bestD)
            {
                bestD = d;
                best = e;
            }
        }
        // By the tool in hand (App. C.2; note 275): the shovel the best club, the crowbar a blow, the wrench less, and
        // empty-handed (a slot picked with nothing in it) a fraction of one (T108).
        if (best is null)
            return;
        var at = best.WorldPosition(Train) + Ballast.Double3.Up * 0.8;
        best.Struck(ctx, playerId, t.Blow(Player.Kit.Held(s)));
        // It landed: everyone's told (T121), at the point of it, the way the blow went.
        Confirm(best, playerId, HitSource.Melee, at, (at - eye).Length > 1e-6 ? (at - eye).Normalized : facing);
    }

    /// <summary>
    /// Host: a blast where nothing was fired (a powder car going up, note 182): an impact on the train's own body, the same
    /// explosion and sound every client already makes of a cannonball's, nobody's shot.
    /// </summary>
    public void Blast(Ballast.Double3 at)
    {
        Impacts.Add(new CannonImpact(_nextFx, Tick, at, Ballast.Double3.Up, ImpactSurface.Train, -1));
        _nextFx = _nextFx % 0xFFFFFF + 1;
    }

    /// <summary>A blow or a ball landed on <paramref name="e"/> (T121): the record every client's flinch, thud and marker come from.</summary>
    void Confirm(Enemy e, int by, HitSource source, Ballast.Double3 at, Ballast.Double3 from)
    {
        // Killed is dead (note 458, D1): a blow that has one give up and go (the Gannet below its giveUpBelow, a Whistler
        // dropping who it carried, a Climber's last try knocked off by a ball) leaves it gone but alive. No kill confirm for
        // that (the sound, the HUD's red mark), and the scene sees it go rather than fall (GreyboxScene.Retreating).
        Hits.Add(new HitConfirm(_nextFx, Tick, e.Id, e.Kind, by, source, at, from, e.Gone && e.Health <= 0));
        _nextFx = _nextFx % 0xFFFFFF + 1;
    }

    /// <summary>Starts a tick: clears last tick's shots and events.</summary>
    public void BeginTick()
    {
        // Hits and impacts last a while on the wire, not a tick (T121): a dropped snapshot doesn't lose one.
        if (Hosting)
        {
            var keep = Combat?.Hits ?? new HitTuning();
            Hits.RemoveAll(h => Tick - h.Tick > keep.KeepSeconds * SimConstants.TickRate);
            Swings.RemoveAll(w => Tick - w.Tick > keep.KeepSeconds * SimConstants.TickRate);
            Emotes.RemoveAll(e => Tick - e.Tick > EmoteTuning.Seconds(e.Kind) * SimConstants.TickRate);
            Impacts.RemoveAll(i => Tick - i.Tick > keep.ImpactKeepSeconds * SimConstants.TickRate);
        }
        Shots.Clear();
        SwitchThrows.Clear();
        EnemyEvents.Clear();
        Damage.Clear();
        _actors.Clear();
        _fumes.Clear();
        Beats.Clear();
        if (Authority && Enemies is { } t)
            _context = new EnemyContext { Tuning = t, World = this, RecentRounds = _recentRounds };
    }

    /// <summary>Advances the train and the world systems after everyone's crew actions.</summary>
    /// <summary>
    /// The fortress yard before the run begins, a safe space (run.json yardIsSafe; the director's decision of 6 Oct 2026, note
    /// 265): nothing spawns, the boiler and fire hold, the cold doesn't bite. From the run's phase, which clients mirror.
    /// </summary>
    public bool SafeYard => Run is { Phase: Sim.Run.RunPhase.Yard, Tuning.YardIsSafe: true };

    /// <summary>
    /// GDD §9 "forts must be safe spaces that monsters never enter" (T128; run.json <c>forts</c>, note 273): whether a world
    /// point is inside one of the night's forts, all night long. The departure fortress is the main line up to its outer gate
    /// (the run's yard); the terminus is from its gate on (a generated line's plan says where, and whether a silent
    /// settlement's gate is kept safe; otherwise the run's terminus zone). Either reaches <see cref="Sim.Run.FortTuning.HalfWidthM"/>
    /// out from the line. No forts without a run.
    /// </summary>
    public bool InFort(Ballast.Double3 world)
    {
        if (Run is not { Tuning.Forts: { Safe: true } forts } run)
            return false;
        double hint = Train.Dynamics.Distance;
        Train.Line.Nearest(world, ref hint);
        double along = hint;
        var rail = Train.Line.Sample(Rail.RailLine.MainPath, along);
        double off = ((world - rail.Position) with { Y = 0 }).Length;
        // A walled town's fort reaches its wall (queue #74, note 335), past the radius every other fort keeps.
        if (along <= run.YardLength && Town?.Plan.Bounds is { } walled)
            return off <= Math.Max(forts.HalfWidthM, Math.Max(walled.Left, walled.Right) + 5);
        if (off > forts.HalfWidthM)
            return false;
        if (along <= run.YardLength)
            return true;
        var terminus = run.Route.Plan?.Terminus;
        if (terminus is { Silent: true, GateSafe: false })
            return false;
        return along >= (terminus?.GateM ?? run.Route.Length - run.Tuning.TerminusZone);
    }

    /// <summary>Any of the train in a fort (note 273): the director sends nothing then.</summary>
    public bool TrainInFort => Run is { Tuning.Forts.Safe: true } && Train.Frames.Count > 0
        && (InFort(Train.Frames[0].Origin) || InFort(Train.Frames[Train.Dynamics.Consist.Vehicles[^1].Id].Origin));

    public void Step(in TrainControls controls)
    {
        Controls = controls;
        Train.HeldInYard = SafeYard;
        var applied = controls;
        // Something at the controls (v1.1 App. A.2, the Track Doll playing with an empty cab's throttle and brake). On the
        // clients too, from their mirror of it, so prediction drives as the host does.
        foreach (var e in _enemies)
            if (!e.Gone)
                e.Tamper(this, ref applied);
        // Build 1121 (note 263): a train standing on the brake it was left on stays on it, whatever's at the controls. With
        // steam driving (T97) a standing engine off its brake pulls away, so a Stoker's runaway took a train held in the yard
        // off with nobody in the cab. (Clients alike, from the same replicated state: prediction holds the brake as the host does.)
        // The one exception is the director's (note 268): a Track Doll left alone to her last stage, at the controls a while.
        if (Enemies is { TamperReleasesStandingBrake: false } && controls.Brake > 0 && Train.Dynamics.Speed < Net.CabControls.StandingBelow
            && !_enemies.Any(e => !e.Gone && e.ReleasesStandingBrake(this)))
            applied.Brake = Math.Max(applied.Brake, controls.Brake);
        // The boards the lamp reaches, and the rail's grip where the engine is (both machines alike: it's prediction).
        Lineside?.See(Train, LampShining);
        // Something clamped on a car and holding the train back past a speed (v1.1 App. A.3, the Car Hugger's cap on top
        // speed): its drag is more than the engine can pull, so the train settles at the cap, and on a climb, under it.
        // Mirrored on the clients too, so prediction drags as the host does.
        var drag = _enemies.FirstOrDefault(e => !e.Gone && e.Drags >= 0);
        Train.DraggedVehicle = drag?.Drags ?? -1;
        Train.DragFactor = drag is { } d && Train.Dynamics.Speed > d.DragAbove ? d.DragFactor : 0;
        // A dead line's end (the director's decision of 7 Oct 2026, note 286): the warning counted on the host before the step,
        // as a bend's is; after it, running through the buffers over the limit with the warning up a full lead derails it.
        var overspeed = Train.Dynamics.Tuning.Overspeed;
        if (Authority)
            DeadEndWarnSeconds = Sim.Train.DeadEnds.Assess(Train, overspeed).Warning ? DeadEndWarnSeconds + SimConstants.TickSeconds : 0;
        Train.Step(SimConstants.TickSeconds, applied);
        if (Authority && !Derailed)
        {
            int spared = DeadEndsSpared, path = Train.Dynamics.Path;
            if (Sim.Train.DeadEnds.OverranThisTick(Train, path, DeadEndWarnSeconds, overspeed, ref spared) is { } hit)
                OffTheEnd(path, hit, overspeed.DeadEndDerailAbove);
            DeadEndsSpared = spared;
        }
        // The wreck (T117), on the host: a tick of it, and the cars' frames where it's put them.
        if (Train.Wreck is { Puppet: false } wreck)
        {
            wreck.Step(SimConstants.TickSeconds);
            Train.RefreshFrames();
        }
        if (Authority && Lineside is { } lineside)
            lineside.Hazards(this, _actors, Damage);
        // The hot boxes (note 331): one open for each crewmate at most, none in the yard or a fort; one left too long
        // catches, and the car's alight (App. C.5).
        // Made on the first step, when the night's route (its seed) is known: the host's enemies come after its run.
        if (Authority && _hotBoxes is null && Train.HotBoxTuning is { } hbt)
            _hotBoxes = new HotBoxes(hbt, (Route?.Seed ?? 0) ^ 0x407B0UL);
        if (Authority && _gutters is null && Train.Gutter is { } gt)
            _gutters = new Gutters(gt, (Route?.Seed ?? 0) ^ 0x6077UL);
        if (Authority && _couplings is null && Train.Loose is { } ct)
            _couplings = new Couplings(ct, (Route?.Seed ?? 0) ^ 0xC0091UL);
        // The couplings (note 356): one loose for each crewmate at most, none in the yard or a fort; one left too long drops
        // its pin, and the rake parts behind it.
        if (Authority && !Derailed && _couplings is { } pins
            && pins.Step(Train, Math.Max(1, _actors.Count(a => a.State.Alive)), SafeYard || TrainInFort) >= 0)
            // Note 511: what it parted is in the report as the pin's, not a cut nobody's named for.
            foreach (var v in Train.Rakes.Where(r => r != Train.Dynamics).SelectMany(r => r.Consist.Vehicles))
                if (Attribution.CouplerPulledBy(v.Id) < 0)
                    Attribution.PartedAt(v.Id);
        // The lamps (note 346): one guttering for each crewmate at most, none in the yard or a fort; one left too long goes out.
        if (Authority && !Derailed && _gutters is { } lamps)
            lamps.Step(Train, Math.Max(1, _actors.Count(a => a.State.Alive)), SafeYard || TrainInFort);
        if (Authority && !Derailed && _hotBoxes is { } boxes)
        {
            int open = Math.Max(1, _actors.Count(a => a.State.Alive));
            if (boxes.Step(Train, open, SafeYard || TrainInFort) is var caught and >= 0 && Enemies is { } ht)
                AddEnemy(id => CarFire.In(id, Train, caught, Train.Frames[caught].Shape.HalfLength - boxes.Tuning.BogieInset, ht.CarFire));
        }
        if (Authority && Combat?.Fumes is { } fumes)
            foreach (var shot in _fumes)
                Fumes(shot, fumes);
        // The shovel nobody has is back on its rack (note 275): its carrier gone from the session, or its body taken off
        // the line with the car it lay in. Out with a crewmate (living or dead) or on a body, it's out.
        if (Authority && Train.Boiler.ShovelOut && _actors.Count > 0 && !_actors.Any(a => Player.Kit.Has(a.State.Kit, Player.Tool.Shovel))
            && !Bodies.All.Any(b => b.HasTool(Player.Tool.Shovel)))
            Train.Boiler.ShovelOut = false;
        // Note 301: where the wrench is the repair tool, the glass goes in only when someone mends it (Repairs.Lamp).
        if (!Repairs.ByWrench(Train))
            LampOutSeconds = Math.Max(0, LampOutSeconds - SimConstants.TickSeconds);
        if (_relight && LampOutSeconds <= 0 && Authority && !Derailed && Train.Dynamics.Tuning.Kit.RelightSmashedLamp)
        {
            _relight = false;
            LampLit = true;
        }
        // A generated line's lethal checks: a curve too fast, a weak bridge overloaded, a washout (linegen plan §7.3).
        if (Authority && TrackPlan is { } plan)
            LineGen.TrackRules.Step(this, plan, SimConstants.TickSeconds);
        WhistleSeconds = Math.Max(0, WhistleSeconds - SimConstants.TickSeconds);
        if (Authority && Answer.Showing)
            Answer = Answer with { Seconds = Math.Max(0, Answer.Seconds - SimConstants.TickSeconds) };
        if (Authority && Watcher.Showing)
            Watcher = Watcher with { Seconds = Math.Max(0, Watcher.Seconds - SimConstants.TickSeconds) };
        if (Combat is { } c)
        {
            Guns.Step(Train);
            // The loudness meter and the Choir it draws (v1.1 App. A.7, C.7), on the host; its state replicates.
            if (Authority)
            {
                // App. B.9: livestock aboard raise the baseline (they're never quiet).
                Choir.Floor = DarkTerritory.Sim.Enemies.Director.Aboard(this).Contains(DarkTerritory.Sim.Train.CargoKind.Livestock) ? c.Choir.LivestockFloor : 0;
                // Spec D.2's livestock ramp (note 185): the herd stirred up at the slaughterhouse raises it too.
                Choir.Floor = Math.Max(Choir.Floor, Run?.HerdFloor ?? 0);
                // Insisted on (note 186): it's gathered all but the last few seconds, and the meter's held up till it comes.
                // Once it's here the crew can hush it off as on any night.
                if (Insist?.Contains(EnemyKind.Choir) == true && !Choir.Present && !Choir.Spent && Choir.Rest <= 0 && ElapsedSeconds >= InsistAfter)
                {
                    Choir.Build = Math.Max(Choir.Build, 1 - InsistLeadSeconds / c.Choir.BuildSeconds);
                    Choir.Floor = Math.Max(Choir.Floor, c.Choir.Threshold * 1.25);
                }
                // In the safe yard (note 263) the crew can be as loud as they like: the meter doesn't gather. Nor with the train in
                // a fort (GDD §9; note 273's caveat, note 296): what it had gathered falls away as in the quiet, and a swarm
                // that followed the train in is gone (its ghosts are driven off by the fort, below).
                bool fort = !SafeYard && TrainInFort;
                if (fort)
                {
                    if (Choir.Present)
                        Choir.Disperse(false, c.Choir.RestSeconds);
                    Choir.Build = Math.Max(0, Choir.Build - c.Choir.QuietDecayPerSecond * SimConstants.TickSeconds);
                }
                bool swarm = !SafeYard && !fort && Choir.Step(c.Choir, Loudness(c.Choir), SimConstants.TickSeconds);
                // Not gathering, nobody's to blame yet: the shares are the BUILD's only (A.7 "during BUILD"). Spent, they're kept
                // as they stood when it took its one, for the incident report to read.
                if (Choir.Phase(c.Choir) == ChoirPhase.Distant && !Choir.Spent)
                    _choirShares.Clear();
                // The Choir come first (note 287): drawn by the noise, in the loudest's name (A.7), by what of theirs was loudest.
                if (swarm && Director is { First: null } fd)
                {
                    int loudest = ChoirLoudest;
                    var noise = new[] { DrawCause.Whistle, DrawCause.Cannon, DrawCause.Voices, DrawCause.Toy }
                        .Select(k => (Cause: k, Amount: fd.Draws.Of(k, loudest))).MaxBy(k => k.Amount);
                    fd.Came(this, EnemyKind.Choir, noise.Amount > 0 ? noise.Cause : DrawCause.Voices, loudest, noise.Amount);
                }
                if (swarm && Enemies is { } et && _context is not null && Insist?.Contains(EnemyKind.Choir) != false)
                    for (int i = 0; i < et.Choir.Ghosts; i++)
                    {
                        double a = i * 2 * Math.PI / et.Choir.Ghosts;
                        var at = Train.Frames[0].Origin + new Ballast.Double3(DMath.Cos(a) * 40, 12, DMath.Sin(a) * 40);
                        AddEnemy(id => ChoirGhost.Around(id, at, et.Choir));
                    }
                // Its one taken (even by a ghost still holding on after the rest dispersed), it's spent for the run.
                if (Enemies is { } dt && (_choirTook || Choir.Present && Choir.QuietSeconds >= dt.Choir.DisperseQuietSeconds))
                {
                    Choir.Disperse(_choirTook, c.Choir.RestSeconds);
                    _choirTook = false;
                    foreach (var ghost in _enemies.Where(e => e.Kind == EnemyKind.Choir && e.Phase != SpinePhase.Grab))
                        ghost.Dismiss();
                }
            }
        }
        // The firebox door swings shut a few seconds after the last shovelful, with someone in the cab to see to it (the Stoker).
        if (Authority && Train.BoilerTuning is not null && Enemies is { } st)
        {
            Train.Boiler.SinceShovel += SimConstants.TickSeconds;
            if (Train.Boiler.FireDoorOpen && Train.Boiler.SinceShovel >= st.Stoker.FireDoorShutSeconds && CabEmptySeconds <= 0)
                Train.Boiler.FireDoorOpen = false;
        }
        // Extinguishers left in their own car recharge, slowly (App. C.5).
        if (Authority && Enemies is { } ft)
            foreach (var b in Bodies.All)
                if (b.Kind == Physics.BodyKind.Extinguisher && b.Carrier < 0 && b.Parent == b.Home && b.Charge < 1)
                    b.Charge = Math.Min(1, b.Charge + SimConstants.TickSeconds / ft.CarFire.RechargeSeconds);
        // Where this tick's balls came down (T121), before they land on anything: what they struck is still there to name.
        if (Hosting)
            foreach (var shot in Shots)
            {
                var struck = shot.HitTargetId > 0 ? _enemies.FirstOrDefault(e => e.Id == shot.HitTargetId)?.Kind ?? 0 : 0;
                Impacts.Add(new CannonImpact(_nextFx, Tick, shot.Impact, shot.Direction, shot.Surface, shot.Shooter, struck));
                _nextFx = _nextFx % 0xFFFFFF + 1;
            }
        if (Authority && _context is { } ctx)
            StepEnemies(ctx);
        Pace();
        Tick++;
        // The town's people go about their rounds on the night's clock (note 353).
        if (Town is { } town)
            town.Clock = Tick * SimConstants.TickSeconds;
        if (Authority)
            RefreshTargets();
    }

    /// <summary>
    /// What happened this tick that the crew would call a moment (the pacing log, after the playtest's "2.5 minutes of nothing
    /// is unacceptable"): a threat showing itself or hitting home, a board read, a bag caught or gone by, a stop made or left.
    /// </summary>
    public List<string> Beats { get; } = new();
    /// <summary>
    /// Seconds out on the line with nothing happening: no beat, and nothing out there telegraphing, committing or punishing.
    /// Counted from the gate (not in the yard, nor once the night's over). The director won't let it pass its pace.
    /// </summary>
    public double QuietSeconds { get; private set; }
    /// <summary>
    /// The same quiet in line travelled: metres the train has run since the last beat (the director's decision of 6 Oct 2026,
    /// GDD App. F.1: "quiet stretches are counted in kilometres, not seconds": a stretch of line holds the same danger whatever
    /// the train's speed, with <see cref="QuietSeconds"/> the time backstop for a stopped train). The pacing log's measure
    /// (ARCHITECTURE §8 note 270); the director keeps its own, from the last threat engaged (enemies.json quietRampMetres).
    /// </summary>
    public double QuietMetres { get; private set; }
    DarkTerritory.Sim.Run.RunPhase _lastPhase;

    void Pace()
    {
        foreach (var e in EnemyEvents)
            if (e.To == SpinePhase.Telegraph && e.From is SpinePhase.Dormant or SpinePhase.Alert || e.To == SpinePhase.Punish)
                Beats.Add($"{e.Kind}:{e.To}");
        if (Lineside is { } lineside)
        {
            foreach (var s in lineside.ReadThisTick)
                Beats.Add($"board:{s.Kind}");
            foreach (var d in lineside.CaughtThisTick)
                Beats.Add($"caught:{d.Kind}");
            foreach (var d in lineside.MissedThisTick)
                Beats.Add($"missed:{d.Kind}");
        }
        if (Run is { } run && run.Phase != _lastPhase)
        {
            Beats.Add($"run:{run.Phase}");
            _lastPhase = run.Phase;
        }
        // Out on the line: not the yard, not home, and not the run in to the terminus either, where nothing's sent by design
        // (the line's terminus_safe, the final approach): the quiet there is the night letting go (T74).
        double front = Train.Dynamics.Distance;
        bool home = Route is { } r && (front > r.Length - NoSpawnFinalApproach || r.Plan?.Director.TagsAt(front).Contains("terminus_safe") == true);
        bool out_ = (Run is null || Run.Phase is DarkTerritory.Sim.Run.RunPhase.Underway or DarkTerritory.Sim.Run.RunPhase.AtFacility) && !home;
        bool active = _enemies.Any(e => !e.Gone && e.Phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Punish)
            // The line at its hardest (linegen plan §15.4): the director sends nothing of its own there because the terrain's
            // the problem, and a crew working a train over it isn't sitting through a quiet (T76).
            || Route?.Plan?.Director is { } context && context.PressureAt(front) >= context.PressureCeiling;
        bool quiet = out_ && Beats.Count == 0 && !active;
        QuietSeconds = quiet ? QuietSeconds + SimConstants.TickSeconds : 0;
        QuietMetres = quiet ? QuietMetres + Train.Dynamics.Speed * SimConstants.TickSeconds : 0;
    }

    /// <summary>
    /// This tick's loudness (App. C.7): every voice on the channel (the level each player's microphone reports in their
    /// intent), the whistle, and machinery (the coaling chute, the winch, the crane at a stop). Cannon shots add theirs as
    /// they're fired. The meter smooths it over a few seconds.
    /// </summary>
    /// <summary>This tick's loudness (App. C.7), each part credited to whoever made it (<see cref="ChoirShares"/>).</summary>
    double Loudness(ChoirTuning t)
    {
        double dt = SimConstants.TickSeconds, total = 0;
        void Add(int player, double loudness)
        {
            total += loudness;
            CreditChoir(player, loudness * dt);
        }
        // The same acts draw the night's first threat (note 287): a voice raised past the draw's floor (talking as you work
        // doesn't), a noisy toy carried, the whistle. Its own weights, so the whistle's blast outweighs a minute's chatter.
        var draw = Director?.Tuning.Draw;
        if (_context is { } ctx)
            foreach (var (p, intent) in ctx.Crew)
                if (p.State.Alive)
                {
                    Add(p.Id, intent.Voice / 255.0 * t.VoicePerPlayer);
                    if (draw is { VoiceFloor: < 1 } && intent.Voice / 255.0 > draw.VoiceFloor)
                        Drew(DrawCause.Voices, p.Id, draw.Weight(DrawCause.Voices) * (intent.Voice / 255.0 - draw.VoiceFloor) / (1 - draw.VoiceFloor) * dt);
                }
        // A squeaker, a music box, a wind-up drummer: noisy in the hands that carry it.
        foreach (var b in Bodies.All)
            if (b is { Kind: Physics.BodyKind.Toy, Carrier: >= 0 } && b.Noise != Physics.ToyNoise.None)
            {
                Add(b.Carrier, t.Toys.Of(b.Noise));
                Drew(DrawCause.Toy, b.Carrier, (draw?.Weight(DrawCause.Toy) ?? 0) * dt);
            }
        if (WhistleSeconds > 0)
        {
            Add(WhistleBy, t.WhistleLoudness);
            // The Whistler's blowing belongs to nobody, and it's already a threat: only a crewmate's pull draws.
            if (WhistleBy >= 0)
                Drew(DrawCause.Whistle, WhistleBy, (draw?.Weight(DrawCause.Whistle) ?? 0) * dt);
        }
        if (Run is { Phase: DarkTerritory.Sim.Run.RunPhase.AtFacility } run && run.Machinery)
            total += t.MachineryLoudness;
        return total;
    }

    readonly Dictionary<int, double> _unmet = [];

    /// <summary>
    /// Once a second: what's gone <see cref="DirectorTuning.LingerSeconds"/> with nobody near it, holding nobody, goes (T114).
    /// Alone, a Climber settled in a car nobody walked into, or hounds trailing a train nobody shot from, held the caps full
    /// and the director had room for nothing new all night.
    /// </summary>
    void Unmet(EnemyContext ctx, DirectorTuning t)
    {
        var crew = ctx.LivingCrew().Select(c => c.World).ToList();
        foreach (var e in _enemies)
        {
            // What lies in wait (a Dragger under a car's edge) doesn't count against the caps, so it may wait all night.
            // Only what has someone in its grip is spared; a car fire's "punish" is the car burning, with nobody in it.
            // A car fire is never dismissed for want of company (build 1121, note 263): App. C.5's fire grows and jumps the
            // couplings with nobody in the car, and while the crew fought one, the rest went out by themselves.
            // Nor is what stays aboard until it's dealt with (Cinder Hounds, note 269): that's the point of it. Nor a haunting
            // Track Doll (the director's decision of 6 Oct 2026, note 268): being left alone is what makes her worse, and only
            // getting her off the train ends her, so she can't give up and go for want of company.
            if (!DarkTerritory.Sim.Enemies.Director.Engaged(e) || e.Holding >= 0 || e.Kind == EnemyKind.CarFire || e.StaysAboard
                || e is DarkTerritory.Sim.Enemies.TrackDoll { Haunting: true })
            {
                _unmet.Remove(e.Id);
                continue;
            }
            var at = e.WorldPosition(Train);
            bool met = crew.Any(p => (p - at).Length <= t.LingerRadius);
            double seconds = met ? 0 : _unmet.GetValueOrDefault(e.Id) + 1;
            _unmet[e.Id] = seconds;
            // Or stuck: telegraphing or committing far longer than any threat's telegraph runs, and nobody in its grip (a
            // Climber scrabbling at the cab's gap all night, never getting in).
            bool stuck = e.Phase is SpinePhase.Telegraph or SpinePhase.Commit && e.PhaseSeconds >= t.LingerSeconds * 1.5;
            if (seconds >= t.LingerSeconds || stuck)
            {
                e.Dismiss();
                _unmet.Remove(e.Id);
            }
        }
    }

    void StepEnemies(EnemyContext ctx)
    {
        var t = ctx.Tuning;
        ctx.Landed.Clear();
        foreach (var shot in Shots)
        {
            _recentRounds.Add((Tick, shot.Muzzle));
            // On the ground, water, a wall or a creature; not a ball stopped by the train's own body.
            if (shot.Surface != ImpactSurface.Train)
                ctx.Landed.Add(shot.Impact);
        }
        _recentRounds.RemoveAll(r => Tick - r.Tick > t.CinderHounds.SuppressWindowSeconds * SimConstants.TickRate);
        // Rounds fired this tick land first.
        if (Combat is { } c)
            foreach (var shot in Shots.Where(s => s.HitTargetId > 0))
                if (_enemies.FirstOrDefault(e => e.Id == shot.HitTargetId) is { Exposed: true } struck)
                {
                    // A ball on a creature's body lands as a heavy blow by the gunner, answered by its own rule (note 290).
                    struck.Hit(ctx, shot.Shooter, c.Guns.DamagePerRound);
                    Confirm(struck, shot.Shooter, HitSource.Cannon, shot.Impact, shot.Direction);
                }

        // How long the cab's been empty (the Track Doll's tampering; and a cab left empty is how a firebox door's left open).
        CabEmptySeconds = ctx.Crew.Any(c => c.Player.State.Alive && PlayerMotor.InCab(c.Player.State, Train)) ? 0 : CabEmptySeconds + SimConstants.TickSeconds;
        if (CabEmptySeconds >= t.TrackDoll.TamperAfterEmpty)
            CabWasLeftEmpty = true;
        // The Stoker's condition (the director's decision of 6 Oct 2026, note 263, in place of App. B.5's low fire and open
        // door): a firebox run hot, heatFirebox or more for heatSeconds. The clock only runs with no Stoker about, once the
        // break after the last one's over, and not in the safe yard (the run hasn't begun).
        bool stokerIn = _enemies.Any(e => e.Kind == EnemyKind.Stoker && !e.Gone);
        if (stokerIn)
            _stokerWasIn = true;
        else if (_stokerWasIn)
        {
            _stokerWasIn = false;
            StokerBreakSeconds = t.Stoker.BreakSeconds;
            // However it went (clubbed out, or sent away by the director's linger rule, which skips its Leave): it's stopped
            // feeding the fire and holding the valve.
            Train.Boiler.ExternalHeat = 0;
            Train.Boiler.SafetyValveJammed = false;
        }
        else
            StokerBreakSeconds = Math.Max(0, StokerBreakSeconds - SimConstants.TickSeconds);
        // How long the train's run at the Gannet's speed (note 340): its arrival rule.
        FastSeconds = Train.Dynamics.Speed >= t.Gannet.ArriveAbove ? FastSeconds + SimConstants.TickSeconds : 0;
        _hotFor = Train.BoilerTuning is not null && !Train.Boiler.Ruptured && !stokerIn && StokerBreakSeconds <= 0 && !SafeYard
            && Train.Boiler.Firebox >= t.Stoker.HeatFirebox ? _hotFor + SimConstants.TickSeconds : 0;
        // The director thinks once a second; the Stoker comes whenever its condition holds, charged when it does (App. B.5).
        // Not in the safe yard (note 263): nothing comes before the run begins.
        if (Tick % SimConstants.TickRate == 0 && Director is { } d && !Derailed && !SafeYard)
        {
            d.Present(_context?.Crew.Count ?? 0);
            d.Count(this, Run is { Tuning.YardIsSafe: true } rc ? rc.Seconds : ElapsedSeconds, _enemies, NoSpawnFinalApproach);
            Unmet(ctx, t.Director);
            // What the crew's done that draws (note 287): the firebox held hot, the engine at speed, cargo come aboard.
            d.Listen(this);
            // Note 327: the crew afoot off the train, watched (the pressure they draw, and a sign now and then).
            if (d.Watch(this) is { } sign)
                Watcher = sign;
            if (Insist is { } insist)
                InsistOn(insist, t, d);
            // Its grace counts from the run's start when the yard's safe (note 263): a crew who waited half an hour at the gate
            // haven't been out in the Territory for it.
            else if (d.Decide(this, Run is { Tuning.YardIsSafe: true } r ? r.Seconds : ElapsedSeconds, _enemies, NoSpawnFinalApproach) is { } kind && Spawns.For(kind) is { } rule)
                rule.Spawn(new SpawnContext(this, t, d));
            // The dark answers the draw (note 287): heard from out past the lamp, and eyes at its edge, before the threat comes.
            foreach (var (cause, actor) in d.TakeAnswers())
                Answer = new DrawAnswer(t.Director.Draw.ShowSeconds, cause, AnswerAt(t.Director.Draw), actor);
            // Drawn by the heat (note 263): it boards at the tender, to cross to the firebox.
            if (d.Allows(EnemyKind.Stoker) && _hotFor >= t.Stoker.HeatSeconds && Train.BoilerTuning is not null
                && !_enemies.Any(e => !e.Gone && e.Kind == EnemyKind.Stoker) && !TrainInFort)
            {
                d.Charge(this, EnemyKind.Stoker, _enemies);
                _enemies.Add(Stoker.AtTender(_nextEnemyId++, Train, t.Stoker));
                _hotFor = 0;
            }
            // The marsh (v1.1 §22, formerly the Drift): a hazard over the line's bogs, not a spawn. Once a marsh.
            if (d.Allows(EnemyKind.Drift) && Drift.Ground(this, t.Drift) is { } marsh && marsh.Start != _driftMarsh && Train.Dynamics.Consist.CarCount >= 1
                && !_enemies.Any(e => !e.Gone && e.Kind == EnemyKind.Drift))
            {
                _driftMarsh = marsh.Start;
                SpawnDrift(t);
            }
            // Dave at his easel, some nights (note 570): the route's, not the director's; put down once the stops stand.
            if (!_daveLooked && Route is { } dr && Train.Walls is not null)
            {
                _daveLooked = true;
                if (Sim.Enemies.Dave.Site(this, dr, t.Dave) is { } dave)
                    _enemies.Add(Sim.Enemies.Dave.At(_nextEnemyId++, dave.At, dave.Along, dave.Yaw, t.Dave));
                // And Jacob at a water's edge, very rarely (note 572).
                if (Sim.Enemies.Jacob.Site(this, dr, t.Jacob) is { } jacob)
                    _enemies.Add(Sim.Enemies.Jacob.At(_nextEnemyId++, jacob.At, jacob.Along, jacob.Yaw));
            }
            // The lineside moose (note 339): grazing beside the line ahead, as the line's own; they cost the director nothing.
            if (Insist is null && d.Allows(EnemyKind.Moose) && Route is { } route && Train.Dynamics.Speed > 3 && !TrainInFort)
                LinesideMoose(t.Moose, route);
            // The Mourners (note 362): a crewmate's body left lying off the train brings a group for it, the director's or not.
            if (t.Mourners.Enabled)
                _mourning.Step(this, t.Mourners, Route?.Tier ?? Sim.Route.RouteTier.Local, ref _nextEnemyId, _enemies, 1);
            // T128 (note 273): whoever the train's left behind has a pressure of their own, and the hunts that come of it.
            d.Abandoned(this, _enemies);
            // Note 328: a train run fast draws the hound run, the guns' wave.
            d.Runs(this, Run is { Tuning.YardIsSafe: true } rs ? rs.Seconds : ElapsedSeconds, _enemies, NoSpawnFinalApproach);
            // Note 435: a Dragger on a truss's top chord, ahead of a fast train.
            d.Drops(this, Run is { Tuning.YardIsSafe: true } rd ? rd.Seconds : ElapsedSeconds, NoSpawnFinalApproach);
        }

        foreach (var e in _enemies.ToList())
            if (!e.Gone)
                e.Step(ctx);
        // The world is solid (note 279): nothing loose in it stands in a building or a tunnel's lining, and what walks is on the land.
        Sim.Enemies.Solidity.Settle(_enemies, Train, t);
        // GDD §9, T128 (note 273): no creature comes into a fort. One that does (riding the train in, running down a crewmate
        // who got back inside the gate, put down there by a spawn) is driven off: it lets go and is gone.
        if (Run is { Tuning.Forts.Safe: true })
            foreach (var e in _enemies)
                if (!e.Gone && !e.Hazard && e is not Sim.Enemies.Incident && InFort(e.WorldPosition(Train)))
                    e.Dismiss();

        _enemies.RemoveAll(e => e.Gone);
        EnemyEvents.AddRange(ctx.Events);
        Damage.AddRange(ctx.Damage);
        _heldThisTick.Clear();
        _heldThisTick.UnionWith(ctx.Held);
        _carries.Clear();
        foreach (var (id, at) in ctx.Carries)
            _carries[id] = at;
    }

    /// <summary>The Choir insisted on comes this many seconds after the meter's held up (note 186).</summary>
    const double InsistLeadSeconds = 5;

    /// <summary>
    /// The combination audit's spawns (note 186): each insisted kind, when none of it is about and it's been gone long
    /// enough, put in by its own spawn rule's placement (which may still find nowhere: no crane, no marsh ahead). The Stoker
    /// goes straight into the firebox; the Choir is gathered in the meter's step.
    /// </summary>
    void InsistOn(IReadOnlyList<EnemyKind> insist, EnemyTuning t, Director d)
    {
        if (ElapsedSeconds < InsistAfter)
            return;
        var ctx = new SpawnContext(this, t, d);
        foreach (var kind in insist)
        {
            if (kind == EnemyKind.Choir || _enemies.Any(e => !e.Gone && e.Kind == kind))
                continue;
            if (d.Log.Any(l => l.Kind == kind) && !_insistGone.ContainsKey(kind))
                _insistGone[kind] = ElapsedSeconds;
            if (_insistGone.TryGetValue(kind, out double gone) && ElapsedSeconds - gone < InsistEvery)
                continue;
            bool placed = kind == EnemyKind.Stoker
                ? Train.BoilerTuning is not null && !Train.Boiler.Ruptured && AddStoker(t)
                : Spawns.For(kind) is { } rule && rule.Spawn(ctx);
            if (!placed)
                continue;
            _insistGone.Remove(kind);
            d.Charge(this, kind, _enemies);
        }
    }

    bool AddStoker(EnemyTuning t)
    {
        _enemies.Add(Stoker.InFirebox(_nextEnemyId++, Train, false, t.Stoker));
        return true;
    }

    readonly List<(uint Tick, Ballast.Double3 Muzzle)> _recentRounds = new();
    readonly HashSet<int> _heldThisTick = new();
    readonly Dictionary<int, Ballast.Double3> _carries = new();
    /// <summary>The marsh (its start) the Drift last came up over: once a marsh.</summary>
    double _driftMarsh = double.NaN;

    /// <summary>Seconds the train's run at or over the Gannet's <see cref="GannetTuning.ArriveAbove"/> without a break (note 340).</summary>
    public double FastSeconds { get; private set; }

    readonly Sim.Enemies.Mourning _mourning = new();
    /// <summary>Structures Tower Jaw brought down tonight, and their wrecks the crew cleared (note 363).</summary>
    public int TowersDown { get; set; }
    public int TowersCleared { get; set; }
    /// <summary>Bodies the Mourners hauled off past finding (note 362): their refunds gone with them.</summary>
    public int MournersTook { get; set; }
    double _mooseNext = double.NaN;
    Ballast.Pcg32 _mooseDice;

    /// <summary>
    /// GDD App. B.6, the director's decision of 7 Oct 2026 (note 339): "you can see it standing beside the rail sometimes".
    /// <see cref="MooseTuning.Lineside"/> per 10 km by tier, fewer where the biome has fewer, put down beside the line
    /// <see cref="MooseTuning.LinesideAhead"/> ahead (never within the track's clearance), one about at a time. Their own
    /// dice from the route's seed, so they never move the director's.
    /// </summary>
    bool _daveLooked;

    void LinesideMoose(MooseTuning t, Route.Route route)
    {
        double front = Train.Dynamics.Distance;
        if (double.IsNaN(_mooseNext))
        {
            _mooseDice = new Ballast.Pcg32(route.Seed, 0x4D_4F4F_5345UL);
            _mooseNext = front + Gap();
        }
        if (front < _mooseNext)
            return;
        _mooseNext = front + Gap();
        double along = front + t.LinesideAhead;
        if (_enemies.Any(e => !e.Gone && e.Kind == EnemyKind.Moose) || along >= Train.Line.PathLength(Train.Dynamics.Path) - 50)
            return;
        var at = Train.Line.Sample(Train.Dynamics.Path, along);
        var right = Ballast.Double3.Cross(at.Tangent, Ballast.Double3.Up).Normalized;
        double side = _mooseDice.Chance(0.5) ? 1 : -1;
        var spot = at.Position + right * (side * _mooseDice.Range(t.LinesideOut[0], t.LinesideOut[1]));
        double yaw = _mooseDice.Range(-Math.PI, Math.PI);
        if (Moose.Place(this, t, spot, along) is { } put)
            _enemies.Add(Moose.Grazing(_nextEnemyId++, put, along, yaw));

        double Gap()
        {
            double per10 = MooseTuning.ByTier(t.Lineside, route.Tier) * Moose.BiomeWeight(this, t, front);
            return per10 <= 0 ? 1000 : 10000 / per10 * _mooseDice.Range(0.5, 1.5);
        }
    }

    /// <summary>The marsh's mass, over one of the cars (the ground's coming up alongside and over the whole train).</summary>
    void SpawnDrift(EnemyTuning t)
    {
        var over = Train.Dynamics.Consist.Vehicles.Skip(1).Select(v => v.Id).ToList();
        if (over.Count == 0 || Director is not { } d)
            return;
        _enemies.Add(Drift.Over(_nextEnemyId++, Train, over[(int)d.NextRange(0, over.Count - 1e-9)], t.Drift));
    }

    void RefreshTargets()
    {
        ExposedBodies(Targets);
        _targetHistory[Tick] = new List<HitTarget>(Targets);
        _targetHistory.Remove(Tick - 32);
    }

    /// <summary>Client side: replaces the mirrored enemies with what the host sent.</summary>
    public void MirrorEnemies(IEnumerable<Enemy> enemies)
    {
        _enemies.Clear();
        _enemies.AddRange(enemies);
        ExposedBodies(Targets);
    }

    /// <summary>Every creature's body in the open, as a ball finds it (enemies.json <c>bodies</c>; note 290).</summary>
    void ExposedBodies(List<HitTarget> into)
    {
        into.Clear();
        if (Enemies is { } t)
            foreach (var e in _enemies)
                into.AddRange(e.Body(Train, t));
    }

    /// <summary>Client side: the host's recent hits and impacts (T121), as the snapshot has them.</summary>
    public void MirrorHits(IEnumerable<HitConfirm> hits, IEnumerable<CannonImpact> impacts, IEnumerable<SwingEvent>? swings = null,
        IEnumerable<EmoteEvent>? emotes = null)
    {
        Emotes.Clear();
        if (emotes is not null)
            Emotes.AddRange(emotes);
        Hits.Clear();
        Hits.AddRange(hits);
        Swings.Clear();
        if (swings is not null)
            Swings.AddRange(swings);
        Impacts.Clear();
        Impacts.AddRange(impacts);
    }

    public void SetDerailed(bool derailed) => Derailed = derailed;

    /// <summary>App. B.1: nothing may spawn inside the final approach.</summary>
    public double NoSpawnFinalApproach { get; set; } = 500;

    /// <summary>Applies this tick's damage and a derailment to the crew. Host only.</summary>
    public void ApplyDamage(Func<int, PlayerState?> get, Action<int, PlayerState> set, IEnumerable<int> crew)
    {
        foreach (var d in Damage)
        {
            if (get(d.PlayerId) is not { Alive: true } s)
                continue;
            // The wreck has them: what kills them is its hit (App. E.2 step 1), already in the log.
            if (Derailed && _doomedAt.ContainsKey(d.PlayerId))
                continue;
            if (d.Pull is { } outward)
            {
                PlayerMotor.PullOff(ref s, Train, outward, Train.Dynamics.Tuning, d.Cause);
                set(d.PlayerId, s);
                continue;
            }
            // Only a punish after a grab kills, or the train's own dangers (App. A.1): any other hurt leaves the last point.
            s.Health -= d.Amount;
            if (!d.Lethal && s.Health < 1)
                s.Health = 1;
            if (s.Health <= 0)
            {
                s.Health = 0;
                s.Death = d.Cause;
            }
            set(d.PlayerId, s);
        }
        // Held this tick (App. A.1 GRAB): their feet are the thing's; carried off, they go where it goes.
        if (Authority)
            foreach (int id in crew)
                if (get(id) is { Alive: true } h)
                {
                    bool held = _heldThisTick.Contains(id);
                    var flags = held ? h.Flags | PlayerFlags.Held : h.Flags & ~PlayerFlags.Held;
                    if (held && _carries.TryGetValue(id, out var to))
                        h = h with { Parent = PlayerState.World, Position = to, Velocity = Ballast.Double3.Zero, Surface = Surface.Ground };
                    if (flags != h.Flags || held && _carries.ContainsKey(id))
                        set(id, h with { Flags = flags });
                }
        if (!Derailed)
            return;
        // App. E.2 step 1 (the director's decision of 5 Oct 2026): the run ends on the derail tick and the settlement is fixed
        // there (E.7), so every death the wreck will cause is counted and logged on it; each player's body goes down on the
        // tick of their own hit (DoomedAt), and that death isn't counted or logged again (Bodies.OnDeaths, StepBodies).
        if (!_doomed)
        {
            _doomed = true;
            var all = crew.Select(id => (Id: id, State: get(id))).Where(c => c.State is not null).Select(c => (c.Id, c.State!.Value)).ToList();
            foreach (var (id, s) in all)
                if (s.Alive && _doomedAt.ContainsKey(id))
                {
                    Bodies.CountAhead(id);
                    _countedAhead.Add(id);
                    if (Run is not null)
                        Attribution.Add(Sim.Run.IncidentLog.Death(this, id, s with { Health = 0, Death = DeathCause.Derailed }, null, all));
                }
        }
        foreach (int id in crew)
            if (get(id) is { Alive: true } s && (!_doomedAt.TryGetValue(id, out uint at) || Tick >= at))
                set(id, s with { Health = 0, Death = DeathCause.Derailed });
    }
}
