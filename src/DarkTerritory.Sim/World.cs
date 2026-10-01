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
    public uint Tick { get; set; }
    public double ElapsedSeconds => Tick * SimConstants.TickSeconds;

    /// <summary>The engine's forward lamp. Sleepers need it lit to be seen from far off (App. A.2); Lamplighters come for it (App. A.6).</summary>
    public bool LampLit { get; set; } = true;
    /// <summary>
    /// Seconds until a lamp the Lamplighters smashed can be lit again (T52: the glass is out until someone fits the spare).
    /// Counted down every tick, on the host and the clients alike.
    /// </summary>
    public double LampOutSeconds { get; set; }

    /// <summary>Where the engine's forward lamp is (world): high on the smokebox door, at the very front.</summary>
    public static Ballast.Double3 LampPosition(in CarFrame engine) => engine.ToWorld(new Ballast.Double3(0, 2.8, -engine.Shape.HalfLength - 0.3));

    /// <summary>Smashed: out, and no lighting it for a while.</summary>
    public void SmashLamp(double seconds)
    {
        LampLit = false;
        LampOutSeconds = Math.Max(LampOutSeconds, seconds);
    }
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
    /// <summary>The id the next enemy added will get.</summary>
    public int NextEnemyId => _nextEnemyId;
    /// <summary>
    /// Seconds the train's whistle has left to blow (GDD §12: the conductor's cord; the Whistler blows it too). Replicated:
    /// every client hears it. It feeds the loudness meter (App. C.7).
    /// </summary>
    public double WhistleSeconds { get; set; }
    /// <summary>The whistle blows this long (the cord pulled, or the Whistler at it).</summary>
    public void Whistled(double seconds) => WhistleSeconds = Math.Max(WhistleSeconds, seconds);
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
    double _lowPressure, _doorOpenAtStop;
    readonly Dictionary<int, uint> _swingReady = new();

    /// <summary>True on the host: enemies and the director run. False on clients, which mirror them.</summary>
    public bool Authority { get; private set; }
    public EnemyTuning? Enemies { get; private set; }
    public Route.Route? Route { get; private set; }
    public Director? Director { get; private set; }
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
        if (route is not null && Director.Allows(EnemyKind.Sleepers))
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
        Bodies.OnDeaths(Train, crew);
        Bodies.Step(Train, Train.Dynamics.Tuning, id => crew.FirstOrDefault(c => c.Id == id) is { State: var s } pair && pair.Id == id ? s : null);
    }

    /// <summary>Host: what the train leaves the yard with that isn't cargo: crates and a lamp in the guard van (GDD §10 tool storage).</summary>
    public void Stock()
    {
        MountExtinguishers();
        // The radios (T41, train.json kit): one on the cab floor at the back, clear of the firebox, the rest in the guard van.
        int radios = Train.Dynamics.Tuning.Kit.Radios;
        if (radios > 0 && Train.Frames[0].Shape.Cab is { } cab)
        {
            Bodies.RadiosCarried = true;
            Bodies.SpawnCrate(Train, 0, new Ballast.Double3(cab.Max.X - 0.4, cab.Min.Y + 0.2, cab.Max.Z - 0.5), Physics.BodyKind.Radio);
            radios--;
        }
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
        // Hand-carried loot (GDD v1.1 App. C.4): toys, for the Track Doll to steal.
        for (int i = 0; i < Train.Dynamics.Tuning.Kit.Toys; i++)
            Bodies.SpawnCrate(Train, guard.Id, new Ballast.Double3(0.6, floor, room.Max.Z - 1.2 - 0.5 * i), Physics.BodyKind.Toy);
    }

    /// <summary>
    /// Host: each car with a room gets its wall-mounted extinguisher (GDD v1.1 App. C.5), by the door end; put back there (or
    /// left lying in its car), it recharges slowly.
    /// </summary>
    public void MountExtinguishers()
    {
        foreach (var v in Train.Dynamics.Consist.Vehicles)
            if (v.Id > 0 && Train.Frames[v.Id].Shape.Interior is { } room)
            {
                var b = Bodies.SpawnCrate(Train, v.Id, new Ballast.Double3(room.Min.X + 0.3, room.Min.Y + 0.1, room.Min.Z + 2.0), Physics.BodyKind.Extinguisher);
                b.Home = v.Id;
            }
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

    /// <summary>Starts the run. The host steps it (<see cref="StepRun"/>); clients mirror it from records.</summary>
    /// <param name="facilities">The facilities' loading modules (spec D); null for none.</param>
    /// <param name="loot">What the stops' containers hold (level-design P14): the yards' crates and castings, the villages' finds; null for none.</param>
    public void EnableRun(Run.RunTuning tuning, Route.Route route, double yardLength, bool authority, Run.FacilityTuning? facilities = null,
        Stops.LootTuning? loot = null)
    {
        TrackPlan ??= route.Plan;
        Run = new Run.Run(tuning, route) { YardLength = yardLength };
        if (facilities is not null)
            Run.EnableSites(facilities, Train.Line);
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
    public List<Run.HoldoutEvent> StepHoldouts(IReadOnlyList<(int Id, PlayerState State)> crew, Action<int, PlayerState> set) =>
        Authority && Holdouts is { } h && Run is { Phase: not Sim.Run.RunPhase.Yard } ? h.Step(this, crew, set, SimConstants.TickSeconds) : [];

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

    public void Derail(string? why = null)
    {
        if (!Derailed)
            DerailCause = why;
        Derailed = true;
        foreach (var rake in Train.Rakes)
            rake.Velocity = 0;
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
            LampLit = intent.Lamp == LampSwitch.On && LampOutSeconds <= 0;
        if (Authority && Run is { } run)
        {
            run.CrewAct(s, intent, playerId, Train, Hand);
            // Spec D.2 "dropped loads kill": under a casting the crane let go of.
            if (run.Crushes(s, Train))
                Damage.Add(new Enemies.DamageEvent(playerId, 1000, DeathCause.Crushed));
        }
        if (Authority && Switches?.CrewAct(s, intent, playerId, Train, Hand) is { } thrown)
            SwitchThrows.Add(thrown);
        if (Authority)
            Holdouts?.CrewAct(s, intent, playerId, Train);
        // Hands first: a Use press that picks something up (or puts it down) isn't also working a lever.
        bool handsTookIt = Authority && Bodies.Handle(s, intent, playerId, Train, Hand);
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
            bool heavy = Bodies.All.Any(b => b.HeldBy(playerId) && b.Kind is Physics.BodyKind.Cargo or Physics.BodyKind.Heavy);
            s.Flags = heavy ? s.Flags | PlayerFlags.Heavy : s.Flags & ~PlayerFlags.Heavy;
        }
        if (!handsTookIt)
            CrewActions.Apply(ref s, intent, Train, SimConstants.TickSeconds, Hand);
        var targets = viewTick is { } vt && _targetHistory.TryGetValue(vt, out var then) ? then : Targets;
        if (Combat is { } c && Guns.TryFire(s, intent, Train, c.Guns, ref Choir, c.Choir, targets, Tick, playerId) is { } shot)
            Shots.Add(shot);
        if (Combat is { } cr)
            Guns.Reload(s, intent, Train, cr.Guns, SimConstants.TickSeconds);
        // Pushing the gun along its roof rail (T93), worked out alike everywhere so a client predicts it: the motor moves it.
        bool pushing = Combat is { } cp && Guns.Pushing(s, intent, Train, cp.Guns);
        s.Flags = pushing ? s.Flags | PlayerFlags.Pushing : s.Flags & ~PlayerFlags.Pushing;
        // The whistle cord, in the cab (GDD §12): a blast, loud, and every client hears it.
        if (intent.Has(PlayerActions.Whistle) && Net.CabControls.CanDrive(s, Train))
            Whistled(1.0);
        // The lamp in the car you're in (GDD v1.1 App. A.5): on the press, the host's to set.
        if (Authority && intent.Has(PlayerActions.CarLamp) && !_lampWas.Contains(playerId) && s.Parent > 0 && s.Parent < Train.Frames.Count
            && PlayerMotor.Indoors(s, Train))
            Train.Vehicles[s.Parent].LampLit = !Train.Vehicles[s.Parent].LampLit;
        if (intent.Has(PlayerActions.CarLamp)) _lampWas.Add(playerId); else _lampWas.Remove(playerId);
        if (Authority && _context is { } ec)
        {
            // Melee (App. C.2): a swing with the tool you carry, at what's in front of you.
            if (intent.Has(PlayerActions.Swing) && s.Alive && !s.Has(PlayerFlags.Held) && Guns.MannedGun(s, Train, Combat?.Guns ?? DefaultGun) is null)
                Swing(ec, s, playerId);
            // Standing idle (Tippy Toesie's mark): still, and not working anything.
            double moving = s.Velocity.Length;
            bool idle = moving < ec.Tuning.TippyToesie.IdleBelow && intent.MoveX == 0 && intent.MoveZ == 0 && intent.Buttons == PlayerButtons.None && intent.Actions == PlayerActions.None;
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
        var eye = PlayerMotor.WorldPosition(s, Train) + Ballast.Double3.Up * 1.3;
        double yaw = PlayerMotor.WorldYaw(s, Train);
        var facing = new Ballast.Double3(-Math.Sin(yaw), 0, -Math.Cos(yaw));
        double cos = Math.Cos(t.ConeDegrees * Math.PI / 180);
        Enemy? best = null;
        double bestD = double.MaxValue;
        foreach (var e in _enemies)
        {
            if (e.Gone || e.MeleeRadius <= 0)
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
        best?.Struck(ctx, playerId, t.Blow(Player.Kit.Held(s)));
    }

    /// <summary>Starts a tick: clears last tick's shots and events.</summary>
    public void BeginTick()
    {
        Shots.Clear();
        SwitchThrows.Clear();
        EnemyEvents.Clear();
        Damage.Clear();
        _actors.Clear();
        Beats.Clear();
        if (Authority && Enemies is { } t)
            _context = new EnemyContext { Tuning = t, World = this, RecentRounds = _recentRounds };
    }

    /// <summary>Advances the train and the world systems after everyone's crew actions.</summary>
    public void Step(in TrainControls controls)
    {
        Controls = controls;
        var applied = controls;
        // Something at the controls (v1.1 App. A.2, the Track Doll playing with an empty cab's throttle and brake). On the
        // clients too, from their mirror of it, so prediction drives as the host does.
        foreach (var e in _enemies)
            if (!e.Gone)
                e.Tamper(this, ref applied);
        // The boards the lamp reaches, and the rail's grip where the engine is (both machines alike: it's prediction).
        Lineside?.See(Train, LampShining);
        // Something clamped on a car and holding the train back past a speed (v1.1 App. A.3, the Car Hugger's cap on top
        // speed): its drag is more than the engine can pull, so the train settles at the cap, and on a climb, under it.
        // Mirrored on the clients too, so prediction drags as the host does.
        var drag = _enemies.FirstOrDefault(e => !e.Gone && e.Drags >= 0);
        Train.DraggedVehicle = drag?.Drags ?? -1;
        Train.DragFactor = drag is { } d && Train.Dynamics.Speed > d.DragAbove ? d.DragFactor : 0;
        Train.Step(SimConstants.TickSeconds, applied);
        if (Authority && Lineside is { } lineside)
            lineside.Hazards(this, _actors, Damage);
        LampOutSeconds = Math.Max(0, LampOutSeconds - SimConstants.TickSeconds);
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
                bool swarm = Choir.Step(c.Choir, Loudness(c.Choir), SimConstants.TickSeconds);
                if (swarm && Enemies is { } et && _context is not null)
                    for (int i = 0; i < et.Choir.Ghosts; i++)
                    {
                        double a = i * 2 * Math.PI / et.Choir.Ghosts;
                        var at = Train.Frames[0].Origin + new Ballast.Double3(Math.Cos(a) * 40, 12, Math.Sin(a) * 40);
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
        QuietSeconds = !out_ || Beats.Count > 0 || active ? 0 : QuietSeconds + SimConstants.TickSeconds;
    }

    /// <summary>
    /// This tick's loudness (App. C.7): every voice on the channel (the level each player's microphone reports in their
    /// intent), the whistle, and machinery (the coaling chute, the winch, the crane at a stop). Cannon shots add theirs as
    /// they're fired. The meter smooths it over a few seconds.
    /// </summary>
    double Loudness(ChoirTuning t)
    {
        double voices = 0;
        if (_context is { } ctx)
            foreach (var (p, intent) in ctx.Crew)
                if (p.State.Alive)
                    voices += intent.Voice / 255.0 * t.VoicePerPlayer;
        double whistle = WhistleSeconds > 0 ? t.WhistleLoudness : 0;
        double machinery = Run is { Phase: DarkTerritory.Sim.Run.RunPhase.AtFacility } run && run.Machinery ? t.MachineryLoudness : 0;
        return voices + whistle + machinery;
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
                _enemies.FirstOrDefault(e => e.Id == shot.HitTargetId)?.Hit(ctx, c.Guns.DamagePerRound);

        // How long the cab's been empty (the Track Doll's tampering; and a cab left empty is how a firebox door's left open).
        CabEmptySeconds = ctx.Crew.Any(c => c.Player.State.Alive && PlayerMotor.InCab(c.Player.State, Train)) ? 0 : CabEmptySeconds + SimConstants.TickSeconds;
        if (CabEmptySeconds >= t.TrackDoll.TamperAfterEmpty)
            CabWasLeftEmpty = true;
        // The Stoker's conditions (App. B.5): pressure under 40 for 45 s (down the stack), or the firebox door left open at a
        // stop (through the door, ×3 by the director's weighing: here, sooner).
        if (Train.BoilerTuning is not null && !Train.Boiler.Ruptured)
        {
            _lowPressure = Train.Boiler.Pressure < t.Stoker.LowPressure ? _lowPressure + SimConstants.TickSeconds : 0;
            // "Left open" (App. B.5): open at a stop with nobody in the cab. A fireman at the door, shovelling, isn't leaving it.
            _doorOpenAtStop = Train.Boiler.FireDoorOpen && Train.Dynamics.Speed < t.Stoker.StoppedBelow && CabEmptySeconds > 0 ? _doorOpenAtStop + SimConstants.TickSeconds : 0;
        }
        // The director thinks once a second; the Stoker comes whenever its condition holds, charged when it does (App. B.5).
        if (Tick % SimConstants.TickRate == 0 && Director is { } d && !Derailed)
        {
            if (d.Decide(this, ElapsedSeconds, _enemies, NoSpawnFinalApproach) is { } kind && Spawns.For(kind) is { } rule)
                rule.Spawn(new SpawnContext(this, t, d));
            // App. B.5: the door left open at a stop this long (it swings shut by itself with someone in the cab to see to it).
            bool door = _doorOpenAtStop >= t.Stoker.DoorOpenSeconds;
            if (d.Allows(EnemyKind.Stoker) && (door || _lowPressure >= t.Stoker.LowPressureSeconds) && Train.BoilerTuning is not null
                && !_enemies.Any(e => !e.Gone && e.Kind == EnemyKind.Stoker))
            {
                d.Charge(this, EnemyKind.Stoker, _enemies);
                _enemies.Add(Stoker.InFirebox(_nextEnemyId++, Train, door, t.Stoker));
                _lowPressure = _doorOpenAtStop = 0;
            }
            // The marsh (v1.1 §22, formerly the Drift): a hazard over the line's bogs, not a spawn. Once a marsh.
            if (d.Allows(EnemyKind.Drift) && Drift.Ground(this, t.Drift) is { } marsh && marsh.Start != _driftMarsh && Train.Dynamics.Consist.CarCount >= 1
                && !_enemies.Any(e => !e.Gone && e.Kind == EnemyKind.Drift))
            {
                _driftMarsh = marsh.Start;
                SpawnDrift(t);
            }
        }

        foreach (var e in _enemies.ToList())
            if (!e.Gone)
                e.Step(ctx);

        _enemies.RemoveAll(e => e.Gone);
        EnemyEvents.AddRange(ctx.Events);
        Damage.AddRange(ctx.Damage);
        _heldThisTick.Clear();
        _heldThisTick.UnionWith(ctx.Held);
        _carries.Clear();
        foreach (var (id, at) in ctx.Carries)
            _carries[id] = at;
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
        Targets.Clear();
        foreach (var e in _enemies)
            if (e.HitRadius > 0)
                Targets.Add(new HitTarget(e.Id, e.WorldPosition(Train), e.HitRadius));
        _targetHistory[Tick] = new List<HitTarget>(Targets);
        _targetHistory.Remove(Tick - 32);
    }

    /// <summary>Client side: replaces the mirrored enemies with what the host sent.</summary>
    public void MirrorEnemies(IEnumerable<Enemy> enemies)
    {
        _enemies.Clear();
        _enemies.AddRange(enemies);
        Targets.Clear();
        foreach (var e in _enemies)
            if (e.HitRadius > 0)
                Targets.Add(new HitTarget(e.Id, e.WorldPosition(Train), e.HitRadius));
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
        foreach (int id in crew)
            if (get(id) is { Alive: true } s)
                set(id, s with { Health = 0, Death = DeathCause.Derailed });
    }
}
