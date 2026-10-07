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

    /// <summary>Host: sets a switch without anyone at its stand (the Switchman, scripted set pieces, tests).</summary>
    public bool SetSwitch(int branch, bool diverge) => Train.ThrowSwitch(branch, diverge, Switches?.Tuning.PointsLength ?? 0);

    public TrainOnLine Train { get; }
    /// <summary>
    /// Where the cab's controls were set for the last step: what its gauges and levers show anyone in the cab (a
    /// driver notches the throttle and flips the reverser from where they are).
    /// </summary>
    public TrainControls Controls { get; private set; } = new() { Reverser = 1 };
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
        // The radios (T41, train.json kit): one on the cab floor against its right wall ahead of the right doorway, out of
        // the reach of a crewmate arriving in the cab, the driver at the controls, the cord, the vent and the fire door (cab
        // forward, note 276); the rest in the guard van.
        int radios = Train.Dynamics.Tuning.Kit.Radios;
        if (radios > 0 && Train.Frames[0].Shape.Cab is { } cab)
        {
            Bodies.RadiosCarried = true;
            double doorFront = EnginePlan.Of(Train.Dynamics.Tuning.Geometry).DoorFront;
            Bodies.SpawnCrate(Train, 0, new Ballast.Double3(cab.Max.X - 0.4, cab.Min.Y + 0.2, doorFront - 0.8), Physics.BodyKind.Radio);
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
        int kits = kit.RepairKits + kit.SpareKits;
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
        Train.Walls = Sim.Run.StopWalls.Of(route, Train.Line, Sim.Run.Fortresses.Of(route, Train.Line, yardLength, tuning.TerminusZone));
        if (facilities is not null)
        {
            Run.EnableSites(facilities, Train.Line);
            // The switchyards' cars on their sidings (GDD §18; note 187), alike on the host and every client: so from the
            // route alone, not from anything one machine has and another mightn't (route.json's 12 m points).
            Run.StandCars(Train);
        }
        if (loot is not null)
            Run.EnableLoot(loot, Train.Line, facilities);
        Authority |= authority;
    }

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
                // App. E.2 step 1 (the director's decision of 5 Oct 2026): nobody dies on the derail tick. The film's own
                // physics, recorded now, says when each of the crew takes the hit that kills them; they die then, as it lands
                // in their own first person (FilmTuning.DeathDelay), and every client's first person ends on its own.
                _recording = WreckFilm.Record(WreckTuning, Film, FilmGround(Film));
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
            crew.Add(new FilmPlayer(id, Sim.Run.IncidentLog.NameOf(this, id), Sim.Run.IncidentLog.Role(this, s, id),
                PlayerMotor.WorldPosition(s, Train), PlayerMotor.WorldVelocity(s, Train), PlayerMotor.WorldYaw(s, Train), inside, s.Has(PlayerFlags.Seated), task));
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

    Func<double, double, double> FilmGround(FilmStart start) => (x, z) =>
    {
        double hint = start.Along;
        return PlayerMotor.GroundAt(new Ballast.Double3(x, 0, z), Train.Line, ref hint);
    };

    /// <summary>The wreck's numbers (wreck.json): the default until the session loads them.</summary>
    public WreckTuning WreckTuning { get; set; } = new();

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
        return WreckFilm.Shoot(WreckTuning, start, FilmGround(start), ReferenceEquals(start, _filmRecorded) ? recorded : null);
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
        bool breaching = Authority && Holdouts?.CrewAct(s, intent, playerId, Train, kit) == true;
        // Hands first: a Use press that picks something up (or puts it down) isn't also working a lever.
        bool handsTookIt = Authority && Bodies.Handle(s, intent, playerId, Train, Hand, keep: kit && (breaching || CrewActions.AtTheRupture(s, Train, Hand)));
        if (handsTookIt && Bodies.CarriedBy(playerId) is { Kind: Physics.BodyKind.Ragdoll } lifted)
            Physics.Bodies.TakeTools(ref s, lifted);
        // At the crane's controls, the stick drives the crane, not your feet (T48). Worked out the same everywhere, so a
        // client predicts standing still at the stand.
        bool operating = false;
        if (Run?.CurrentSite is { } site)
            foreach (var crane in site.Cranes)
                operating |= crane.AtControls(s, intent, Train);
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
            Train.Vehicles[s.Parent].LampLit = !Train.Vehicles[s.Parent].LampLit;
            if (Train.Vehicles[s.Parent].LampLit)
                Attribution.LitLamp(s.Parent, playerId);
        }
        if (intent.Has(PlayerActions.CarLamp)) _lampWas.Add(playerId); else _lampWas.Remove(playerId);
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
        // With a tool a blow; empty-handed (a slot picked with nothing in it) a fraction of one (T108).
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
        Hits.Add(new HitConfirm(_nextFx, Tick, e.Id, e.Kind, by, source, at, from, e.Gone));
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
        if (((world - rail.Position) with { Y = 0 }).Length > forts.HalfWidthM)
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
        Train.Step(SimConstants.TickSeconds, applied);
        // The wreck (T117), on the host: a tick of it, and the cars' frames where it's put them.
        if (Train.Wreck is { Puppet: false } wreck)
        {
            wreck.Step(SimConstants.TickSeconds);
            Train.RefreshFrames();
        }
        if (Authority && Lineside is { } lineside)
            lineside.Hazards(this, _actors, Damage);
        if (Authority && Combat?.Fumes is { } fumes)
            foreach (var shot in _fumes)
                Fumes(shot, fumes);
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
        if (_context is { } ctx)
            foreach (var (p, intent) in ctx.Crew)
                if (p.State.Alive)
                    Add(p.Id, intent.Voice / 255.0 * t.VoicePerPlayer);
        // A squeaker, a music box, a wind-up drummer: noisy in the hands that carry it.
        foreach (var b in Bodies.All)
            if (b is { Kind: Physics.BodyKind.Toy, Carrier: >= 0 } && b.Noise != Physics.ToyNoise.None)
                Add(b.Carrier, t.Toys.Of(b.Noise));
        if (WhistleSeconds > 0)
            Add(WhistleBy, t.WhistleLoudness);
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
        foreach (var shot in Shots)
            _recentRounds.Add((Tick, shot.Muzzle));
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
        _hotFor = Train.BoilerTuning is not null && !Train.Boiler.Ruptured && !stokerIn && StokerBreakSeconds <= 0 && !SafeYard
            && Train.Boiler.Firebox >= t.Stoker.HeatFirebox ? _hotFor + SimConstants.TickSeconds : 0;
        // The director thinks once a second; the Stoker comes whenever its condition holds, charged when it does (App. B.5).
        // Not in the safe yard (note 263): nothing comes before the run begins.
        if (Tick % SimConstants.TickRate == 0 && Director is { } d && !Derailed && !SafeYard)
        {
            d.Present(_context?.Crew.Count ?? 0);
            Unmet(ctx, t.Director);
            if (Insist is { } insist)
                InsistOn(insist, t, d);
            // Its grace counts from the run's start when the yard's safe (note 263): a crew who waited half an hour at the gate
            // haven't been out in the Territory for it.
            else if (d.Decide(this, Run is { Tuning.YardIsSafe: true } r ? r.Seconds : ElapsedSeconds, _enemies, NoSpawnFinalApproach) is { } kind && Spawns.For(kind) is { } rule)
                rule.Spawn(new SpawnContext(this, t, d));
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
            // T128 (note 273): whoever the train's left behind has a pressure of their own, and the hunts that come of it.
            d.Abandoned(this, _enemies);
        }

        foreach (var e in _enemies.ToList())
            if (!e.Gone)
                e.Step(ctx);
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
    public void MirrorHits(IEnumerable<HitConfirm> hits, IEnumerable<CannonImpact> impacts, IEnumerable<SwingEvent>? swings = null)
    {
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
