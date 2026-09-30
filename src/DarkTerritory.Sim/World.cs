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
    double _choirTimer;

    public World(TrainOnLine train, CombatTuning? combat = null)
    {
        Train = train;
        Combat = combat;
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
    /// <summary>Host, this tick: the Soot Children calling (the enemy, and whose voice), for the session to play (T40).</summary>
    public List<(int Enemy, int Voice)> Calls { get; } = new();
    /// <summary>The crew as they acted this tick (host, with enemies on), for spawns that go after someone in particular.</summary>
    public IReadOnlyList<(int Id, PlayerState State)> CrewThisTick =>
        _context is { } c ? [.. c.Crew.Select(x => ((int)x.Player.Id, x.Player.State))] : [];
    /// <summary>
    /// How reaching hands work (T29, player.json <c>hand</c>). The sessions set it from their player tuning; while it's
    /// unset, hands in intents are ignored and everyone reaches from the body.
    /// </summary>
    public HandTuning? Hand { get; set; }
    public ChoirState Choir;
    /// <summary>Host, set each tick before the crew act: how many of the crew are alive (App. D.9's solo remainer).</summary>
    public int LivingCrew { get; set; }
    /// <summary>Host: the crew loudness meter (App. C.7).</summary>
    public Combat.CrewLoudness Loudness { get; } = new();
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
    /// <summary>Host: how long nobody alive has been in the engine's cab (T53, the Deadman and the Stoker).</summary>
    public double CabEmptySeconds { get; private set; }

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

    /// <summary>
    /// Host: after everyone has moved, a body for each death this tick (App. D.2: one body per death, so a player who dies
    /// twice leaves two), then a physics step.
    /// </summary>
    public void StepBodies(IReadOnlyCollection<(int Id, PlayerState State)> crew)
    {
        if (!Authority)
            return;
        foreach (var (id, s) in crew)
        {
            if (s.Alive)
                _bodied.Remove(id);
            else if (s.Death != DeathCause.None && _bodied.Add(id))
                Died(id, s, Bodies.SpawnRagdoll(Train, id, s), dropOut: false);
        }
        Bodies.Step(Train, Train.Dynamics.Tuning, id => crew.FirstOrDefault(c => c.Id == id) is { State: var s } pair && pair.Id == id ? s : null);
    }

    // The players whose current death has already left its body.
    readonly HashSet<int> _bodied = new();

    /// <summary>Every death this run, and every drop-out's inert body (App. D.2), oldest first: the report's and settlement's.</summary>
    public List<Run.DeathRecord> Deaths { get; } = new();
    /// <summary>Each body as loot (App. D.9), by body id: its fee, its refund, and the kit it kept.</summary>
    public Dictionary<int, Run.BodyRecord> BodyRecords { get; } = new();

    /// <summary>
    /// Host: a player has left a body (App. D.2): recorded where it fell, charged its fee (a drop-out's isn't), and the
    /// player into the respawn queue, ineligible at the site whose zone it happened in (D.5).
    /// </summary>
    void Died(int id, in PlayerState s, Physics.Body body, bool dropOut)
    {
        // Before the gates open it isn't the run's (App. D.3: everyone in the yard is back aboard at the fortress).
        if (Run is { Phase: global::DarkTerritory.Sim.Run.RunPhase.Yard })
            return;
        // App. D.2 "it keeps everything the player was carrying": the train's things in their hands and on their belt go with
        // the body, and back to stores when it's delivered (D.9). Freight they had hold of, and another body, fall.
        var kit = new List<Physics.BodyKind>();
        foreach (var b in Bodies.All.Where(b => b.HeldBy(id) && b.Kind is not (Physics.BodyKind.Cargo or Physics.BodyKind.Heavy or Physics.BodyKind.Ragdoll)).ToList())
        {
            kit.Add(b.Kind);
            Bodies.Remove(b);
        }
        var at = PlayerMotor.WorldPosition(s, Train);
        double hint = s.LineHint;
        var (path, distance) = Train.Line.Nearest(at, ref hint);
        var zones = Holdouts?.ZonesAt(path, distance) ?? [];
        double seconds = Run?.Seconds ?? ElapsedSeconds;
        Deaths.Add(new Run.DeathRecord(id, body.Id, Math.Round(seconds, 2), Math.Round(hint, 1), dropOut ? DeathCause.None : s.Death, zones.FirstOrDefault(), dropOut));
        if (HoldoutTuning is { } ht && Run is { } run)
            BodyRecords[body.Id] = global::DarkTerritory.Sim.Run.BodyRecord.For(ht, run.PerCar, body.Id, id, dropOut, kit);
        if (!dropOut)
            Holdouts?.Queue.Died(id, seconds, zones);
    }

    /// <summary>
    /// Host: a player has left the session (App. D.2 "drop-out"): a living one leaves an inert body, kit and all, with no fee;
    /// a waiting one leaves the queue (D.6), and any Holdout they had reassigns.
    /// </summary>
    public void DroppedOut(int id, in PlayerState s)
    {
        if (s.Alive && !Bodies.HasRagdoll(id))
        {
            _bodied.Add(id);
            Died(id, s, Bodies.SpawnRagdoll(Train, id, s), dropOut: true);
        }
        Holdouts?.Queue.Remove(id);
        _bodied.Remove(id);
    }

    /// <summary>
    /// App. D.9: what a loose thing is worth to anything that ranks loot: a body, its refund. Nothing in this build's roster
    /// ranks loot yet (v1.1's Gaunt, Followers and Car Hugger will): this is what they're to rank bodies by.
    /// </summary>
    public double LootValue(Physics.Body b) =>
        b.Kind == Physics.BodyKind.Ragdoll && BodyRecords.TryGetValue(b.Id, out var r) ? r.LootValue : 0;

    /// <summary>App. D.13, when the night has Holdouts: holdouts.json.</summary>
    public Run.HoldoutTuning? HoldoutTuning { get; private set; }
    /// <summary>The night's Holdouts and the respawn queue (App. D.5, D.6), on a generated line that has them.</summary>
    public Run.Holdouts? Holdouts { get; private set; }

    /// <summary>The Holdouts from the night's Line Plan. Host and clients alike (clients mirror their state).</summary>
    public void EnableHoldouts(Run.HoldoutTuning tuning)
    {
        HoldoutTuning = tuning;
        if (TrackPlan is { Holdouts.Count: > 0 } plan)
            Holdouts = new Run.Holdouts(tuning, plan, Train.Line);
    }

    /// <summary>
    /// Host, after bodies: the Holdouts' step (App. D.5), and whoever it frees stood up inside theirs (D.8).
    /// </summary>
    /// <param name="sessionCrew">Everyone in the session, for the second facility Holdout's gate.</param>
    public void StepHoldouts(Func<int, PlayerState?> get, Action<int, PlayerState> set, IEnumerable<int> crew, int sessionCrew, PlayerTuning p)
    {
        if (!Authority)
            return;
        var ids = crew.ToList();
        List<(int, PlayerState)> Everyone() => [.. ids.Select(id => (id, get(id))).Where(x => x.Item2 is not null).Select(x => (x.id, x.Item2!.Value))];
        if (Holdouts is { } holdouts)
        {
            var damaged = Damage.Select(d => d.PlayerId).ToHashSet();
            holdouts.Step(Train, Everyone(), damaged, sessionCrew, Run?.Seconds ?? ElapsedSeconds, SimConstants.TickSeconds);
            foreach (var e in holdouts.Events)
                if (e.Outcome == global::DarkTerritory.Sim.Run.HoldoutOutcome.Freed && get(e.Player) is { } was)
                {
                    var h = holdouts.All[e.Holdout];
                    var back = holdouts.FreedState(h, Train, p);
                    back.Placed = (byte)(was.Placed + 1);
                    set(e.Player, back);
                    GiveStandardKit(e.Player, back);
                    Characters[e.Player] = new global::DarkTerritory.Sim.Run.Character(h.Appearance, h.VoiceSet);
                }
        }
        // Whom the dead watch, once the freed are back among the living.
        Dead.Step(Everyone());
    }

    /// <summary>The dead phase (App. D.10): whom the dead watch, and what they've asked for.</summary>
    public Run.DeadPhase Dead { get; } = new();

    /// <summary>Host: a player's request (App. D). Returns whether it was allowed.</summary>
    public bool Request(int player, in Net.Request q, IReadOnlyCollection<(int Id, PlayerState State)> crew) =>
        crew.FirstOrDefault(c => c.Id == player) is { } me && me.Id == player && Dead.Handle(this, player, me.State, q, crew);

    /// <summary>App. D.11: each creature's votes this run, and who's voted (once a run each).</summary>
    public Dictionary<EnemyKind, int> Votes { get; } = new();
    public List<(int Player, EnemyKind Kind, double Seconds)> VoteLog { get; } = new();

    /// <summary>
    /// App. D.11, the creature vote: dead players only (a lobbied player votes once they've been in the crew and died), once
    /// a run each, locked on submit (an unused vote waits for a later death), for a creature the director could draw now.
    /// Its only effect is a weight inside the director's roll, within the creature's want tag.
    /// </summary>
    public bool Vote(int player, in PlayerState state, EnemyKind kind)
    {
        if (state.Alive || state.Death == DeathCause.None || state.Has(PlayerFlags.Lobbied) || HoldoutTuning is not { } t || Director is not { } d)
            return false;
        if (VoteLog.Count(v => v.Player == player) >= t.Vote.PerRun || !d.Voteable(this).Contains(kind))
            return false;
        Votes[kind] = Votes.GetValueOrDefault(kind) + 1;
        VoteLog.Add((player, kind, Math.Round(Run?.Seconds ?? ElapsedSeconds, 1)));
        d.SetVotes(Votes, t.Vote);
        return true;
    }

    /// <summary>What the dead can vote for now (App. D.11): the director's, host side; mirrored to the dead, and only them.</summary>
    public IReadOnlyList<EnemyKind> VoteOptions => Director?.Voteable(this) ?? _voteOptions;
    List<EnemyKind> _voteOptions = [];

    /// <summary>Client side: the vote as the host sent it to the dead (the options, who's voted, the counts).</summary>
    public void MirrorVotes(IEnumerable<EnemyKind> options, IEnumerable<int> voted, IEnumerable<(EnemyKind Kind, int Count)> counts)
    {
        _voteOptions = [.. options];
        VoteLog.Clear();
        foreach (int p in voted)
            VoteLog.Add((p, EnemyKind.Sleepers, 0));
        Votes.Clear();
        foreach (var (k, n) in counts)
            Votes[k] = n;
    }

    /// <summary>App. D.12: the commendations given on the run-end screen, in the order they were given.</summary>
    public List<Run.Commendation> Commendations { get; } = new();

    /// <summary>
    /// App. D.12, a commendation: once the run's over, anyone in the session (the living, the dead, the lobbied) gives up to
    /// <c>commendations.perPlayer</c> (one) to someone else in it, never themselves, from the award list. There are no
    /// demerits. The receiver's profile keeps it (the game side, from the report).
    /// </summary>
    public bool Commend(int player, int to, int award, IReadOnlyCollection<(int Id, PlayerState State)> crew)
    {
        if (Run is not { Over: true } || HoldoutTuning is not { } t)
            return false;
        if (to == player || !crew.Any(c => c.Id == to) || award < 0 || award >= t.Commendations.Awards.Length)
            return false;
        if (Commendations.Count(c => c.From == player) >= t.Commendations.PerPlayer)
            return false;
        Commendations.Add(new Run.Commendation(player, to, t.Commendations.Awards[award]));
        return true;
    }

    /// <summary>
    /// App. D.8: a freed player's survivor, by player: who they are from then on. The host's campaign save keeps them against
    /// the player's id; the art draws them.
    /// </summary>
    public Dictionary<int, Run.Character> Characters { get; } = new();

    /// <summary>Host: what the train leaves the yard with that isn't cargo: crates and a lamp in the guard van (GDD §10 tool storage).</summary>
    public void Stock()
    {
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
        // The tools (App. C.2, D.7), along the other wall at the front end, clear of the crates and the rear door.
        int n = 0;
        foreach (var (kind, count) in Train.Dynamics.Tuning.Kit.Tools.OrderBy(t => t.Key))
            for (int i = 0; i < count; i++, n++)
                Bodies.SpawnCrate(Train, guard.Id, new Ballast.Double3(-0.9, floor, room.Min.Z + 1.2 + 0.5 * n), kind);
    }

    /// <summary>
    /// App. D.8: the standard kit (train.json <c>kit.standard</c>) into a player's hands: what everyone departs the fortress
    /// with, and what a freed player comes out of the Holdout with. A radio goes on the belt; one thing in the hands.
    /// </summary>
    public void GiveStandardKit(int player, in PlayerState s)
    {
        if (!Authority)
            return;
        foreach (var kind in Train.Dynamics.Tuning.Kit.Standard)
        {
            if (kind != Physics.BodyKind.Radio && Bodies.CarriedBy(player) is not null)
                continue;
            var at = PlayerMotor.WorldPosition(s, Train);
            var item = Bodies.SpawnLoose(at + Ballast.Double3.Up, s.LineHint, kind);
            item.Carrier = player;
            if (kind == Physics.BodyKind.Radio)
                Bodies.RadiosCarried = true;
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

    /// <summary>The headlamp is on (the cab's lamp switch, T52, and not smashed out).</summary>
    public bool LampShining => LampLit;

    /// <summary>The generated line whose track rules the host holds the train to (curves, weak bridges, washouts); null for a hand-laid one.</summary>
    public LineGen.LinePlan? TrackPlan { get; set; }
    /// <summary>What derailed the train, when the track did it (the report and the HUD say so).</summary>
    public string? DerailCause { get; private set; }

    /// <summary>Starts the run. The host steps it (<see cref="StepRun"/>); clients mirror it from records.</summary>
    /// <param name="facilities">The facilities' loading modules (spec D); null for none.</param>
    public void EnableRun(Run.RunTuning tuning, Route.Route route, double yardLength, bool authority, Run.FacilityTuning? facilities = null)
    {
        TrackPlan ??= route.Plan;
        Run = new Run.Run(tuning, route) { YardLength = yardLength };
        if (facilities is not null)
            Run.EnableSites(facilities, Train.Line);
        Authority |= authority;
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

    public void Derail()
    {
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
        // A Holdout's door first (App. D.7): holding Use there with a tool is the breach, not putting the tool down.
        bool breaching = Authority && Holdouts?.CrewAct(s, intent, playerId, Train, Bodies, Hand) == true;
        if (breaching)
            Bodies.Seen(playerId, intent);
        // Hands next: a Use press that picks something up (or puts it down) isn't also working a lever.
        bool handsTookIt = breaching || Authority && Bodies.Handle(s, intent, playerId, Train, Hand);
        // At the crane's controls, the stick drives the crane, not your feet (T48). Worked out the same everywhere, so a
        // client predicts standing still at the stand.
        bool operating = Run?.CurrentSite?.Crane is { } crane && crane.AtControls(s, intent, Train);
        s.Flags = operating ? s.Flags | PlayerFlags.Operating : s.Flags & ~PlayerFlags.Operating;
        if (Authority)
        {
            // Freight in your arms slows you and keeps you off ladders (spec B.2), and so does a body (App. D.9: hand-carried
            // loot, 2.8 m/s and no climbing), unless you're the last of the crew alive (the solo remainer's slow climb).
            // The motor reads the flags.
            bool heavy = Bodies.All.Any(b => b.HeldBy(playerId) && b.Kind is Physics.BodyKind.Cargo or Physics.BodyKind.Heavy or Physics.BodyKind.Ragdoll);
            bool solo = heavy && LivingCrew == 1 && Bodies.CarriedBy(playerId) is { Kind: Physics.BodyKind.Ragdoll };
            s.Flags = heavy ? s.Flags | PlayerFlags.Heavy : s.Flags & ~PlayerFlags.Heavy;
            s.Flags = solo ? s.Flags | PlayerFlags.SoloCarry : s.Flags & ~PlayerFlags.SoloCarry;
        }
        if (!handsTookIt)
            CrewActions.Apply(ref s, intent, Train, SimConstants.TickSeconds, Hand);
        var targets = viewTick is { } vt && _targetHistory.TryGetValue(vt, out var then) ? then : Targets;
        if (Combat is { } c
            && Guns.TryFire(s, intent, Train, c.Guns, ref Choir, c.Choir, targets, Tick, playerId) is { } shot)
        {
            Shots.Add(shot);
            // The guns' own Choir aggro is by the round (RoundFired): the meter counts them, and doesn't feed them twice.
            if (Authority)
                Loudness.Add("gun", "cannon", c.Loudness, counted: true);
        }
        _context?.Crew.Add((new PlayerSnapshot((byte)playerId, s), intent));
        if (Authority)
            _actors.Add((playerId, s, intent));
    }

    /// <summary>Starts a tick: clears last tick's shots and events.</summary>
    public void BeginTick()
    {
        Shots.Clear();
        SwitchThrows.Clear();
        Calls.Clear();
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
        // The Deadman at the controls (App. A.5): "throttle locks, brake unresponsive, train accelerates".
        if (_enemies.Any(e => e is Deadman { Holding: true }))
        {
            applied.Throttle = 1;
            applied.Brake = 0;
        }
        // The boards the lamp reaches, and the rail's grip where the engine is (both machines alike: it's prediction).
        Lineside?.See(Train, LampShining);
        // The Weight holding the rear car (App. A.3): "constant negative force; speed decays continuously". On the clients
        // too, from their mirror of it, so prediction drags as the host does.
        var weight = _enemies.OfType<Weight>().FirstOrDefault(w => w.Holding);
        Train.DraggedVehicle = weight?.Attached ?? -1;
        Train.DragFactor = Enemies?.Weight.DragFactor ?? 0;
        Train.Step(SimConstants.TickSeconds, applied);
        if (Authority && Lineside is { } lineside)
            lineside.Hazards(this, _actors, Damage);
        LampOutSeconds = Math.Max(0, LampOutSeconds - SimConstants.TickSeconds);
        // A generated line's lethal checks: a curve too fast, a weak bridge overloaded, a washout (linegen plan §7.3).
        if (Authority && TrackPlan is { } plan && LineGen.TrackRules.Step(this, plan, SimConstants.TickSeconds) is { } why)
            DerailCause = why;
        if (Combat is { } c)
        {
            Guns.Step(Train);
            // App. B.8: livestock aboard raises the Choir's floor (they're never quiet); the Choir's state replicates, floor and all.
            if (Authority)
                Choir.Floor = DarkTerritory.Sim.Enemies.Director.Aboard(this).Contains(DarkTerritory.Sim.Train.CargoKind.Livestock) ? c.Choir.LivestockFloor : 0;
            // App. C.7: what the living are doing that's loud (a breach, App. D.7) goes on the meter, and the meter to the Choir.
            if (Authority)
            {
                foreach (var level in Holdouts?.Noise() ?? [])
                    Loudness.Add("breach", level, c.Loudness);
                Loudness.Step(ref Choir, c.Choir, c.Loudness, SimConstants.TickSeconds);
            }
            Choir.Step(c.Choir, SimConstants.TickSeconds);
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

        // How long the cab's been empty (the Deadman's condition, and the Stoker's "unattended").
        CabEmptySeconds = ctx.Crew.Any(c => c.Player.State.Alive && PlayerMotor.InCab(c.Player.State, Train)) ? 0 : CabEmptySeconds + SimConstants.TickSeconds;
        // The director thinks once a second; the Hollow comes whenever its condition holds (App. B.5).
        if (Tick % SimConstants.TickRate == 0 && Director is { } d && !Derailed)
        {
            if (d.Decide(this, ElapsedSeconds, _enemies, NoSpawnFinalApproach) is { } kind)
                Spawn(kind, d);
            if (d.Allows(EnemyKind.Hollow) && Train.BoilerTuning is not null && Train.Boiler.LowFireSeconds >= t.Hollow.LowFireSeconds
                && !_enemies.Any(e => !e.Gone && e.Kind == EnemyKind.Hollow))
            {
                d.Charge(this, EnemyKind.Hollow, _enemies);
                _enemies.Add(new Hollow(_nextEnemyId++));
            }
            // The Drift (App. B.4): "a terrain region, not an entity". Over a marsh it's there, as the Hollow is when the fire's
            // low: it comes up once a marsh, whatever the director would rather, and it's charged when it does.
            if (d.Allows(EnemyKind.Drift) && Drift.Ground(this, t.Drift) is { } marsh && marsh.Start != _driftMarsh && Train.Dynamics.Consist.CarCount >= 1
                && !_enemies.Any(e => !e.Gone && e.Kind == EnemyKind.Drift))
            {
                _driftMarsh = marsh.Start;
                d.Charge(this, EnemyKind.Drift, _enemies);
                Spawn(EnemyKind.Drift, d);
            }
            // The Deadman (App. B.5): "not on Local routes; cab empty 30 s (20 s on Deep territory)". It starts its
            // approach that long less its telegraph, so it takes the cab at the spec's time; it's charged when it does.
            if (d.Allows(EnemyKind.Deadman) && Route is { Tier: not RouteTier.Local } r && Train.Frames[0].Shape.Cab is not null
                && CabEmptySeconds >= (r.Tier == RouteTier.DeepTerritory ? t.Deadman.EmptySecondsDeep : t.Deadman.EmptySeconds) - t.Deadman.TelegraphSeconds
                && !_enemies.Any(e => !e.Gone && e.Kind == EnemyKind.Deadman))
                _enemies.Add(Deadman.Watching(_nextEnemyId++, Train));
        }

        foreach (var e in _enemies.ToList())
            if (!e.Gone)
                e.Step(ctx);

        // The Choir in full swarm assaults everything exposed (App. A.6).
        if (Combat is { } cc && Choir.Phase(cc.Choir) == ChoirPhase.Swarm)
        {
            _choirTimer += SimConstants.TickSeconds;
            if (_choirTimer >= t.ChoirSwarm.EverySeconds)
            {
                _choirTimer = 0;
                // Sheltered means the cab, or a car with its doors shut (GDD §26: protected versus exposed).
                foreach (var (player, _) in ctx.Crew)
                    if (player.State.Alive && PlayerMotor.Space(player.State, Train) == PlayerMotor.Outside)
                        ctx.Bite(player.Id, t.ChoirSwarm.ExposedDamage, DeathCause.Choir);
            }
        }
        else
        {
            _choirTimer = 0;
        }

        _enemies.RemoveAll(e => e.Gone);
        EnemyEvents.AddRange(ctx.Events);
        Damage.AddRange(ctx.Damage);
    }

    readonly List<(uint Tick, Ballast.Double3 Muzzle)> _recentRounds = new();
    /// <summary>The marsh (its start) the Drift last came up over: once a marsh.</summary>
    double _driftMarsh = double.NaN;

    void Spawn(EnemyKind kind, Director d)
    {
        var t = Enemies!;
        switch (kind)
        {
            case EnemyKind.CinderHound:
                int pack = _nextEnemyId;
                int size = (int)Math.Round(d.NextRange(t.CinderHounds.PackSize[0], t.CinderHounds.PackSize[1] + 0.49));
                for (int i = 0; i < size; i++)
                    _enemies.Add(new CinderHound(_nextEnemyId++, pack)
                    {
                        LineDistance = Train.Dynamics.RearDistance - t.CinderHounds.SpawnBehind - i * 6,
                        Lateral = (i % 2 == 0 ? 1 : -1) * d.NextRange(3, 6),
                        Height = 0.6,
                        Health = t.CinderHounds.Health,
                    });
                break;
            case EnemyKind.Clinger:
                var cars = Train.Dynamics.Consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo).ToList();
                var car = cars[(int)(d.NextRange(0, cars.Count - 1e-9))];
                var shape = Train.Frames[car.Id].Shape;
                double side = d.NextRange(0, 1) < 0.5 ? -1 : 1;
                _enemies.Add(new Clinger(_nextEnemyId++)
                {
                    Attached = car.Id,
                    Local = new Ballast.Double3(side * (shape.HalfWidth + 0.15), 2.0, d.NextRange(-shape.HalfLength + 1.5, shape.HalfLength - 1.5)),
                });
                break;
            case EnemyKind.Switchman when DarkTerritory.Sim.Enemies.Switchman.Junction(this, t.Switchman) is { } junction:
                _enemies.Add(DarkTerritory.Sim.Enemies.Switchman.At(_nextEnemyId++, junction, t.Switchman, Switches?.Tuning.LeverOffset ?? 2.6));
                break;
            case EnemyKind.SootChildren when SootChildren.Choose(this, t.SootChildren, CrewThisTick) is { } mark:
                _enemies.Add(SootChildren.At(_nextEnemyId++, Train, mark.Car, mark.Voice, t.SootChildren));
                break;
            case EnemyKind.Dragger:
                // Under a car someone's walking the roof of (it was always there; they've woken it), on either edge.
                var walked = CrewThisTick.Where(c => c.State is { Alive: true, Surface: Surface.Roof } r && r.Parent > 0 && r.Parent < Train.Frames.Count)
                    .Select(c => c.State.Parent).Distinct().Order().ToList();
                if (walked.Count == 0)
                    break;
                int under = walked[(int)d.NextRange(0, walked.Count - 1e-9)];
                double length = Train.Frames[under].Shape.HalfLength;
                _enemies.Add(Dragger.Under(_nextEnemyId++, Train, under, d.NextRange(0, 1) < 0.5 ? -1 : 1, d.NextRange(-length, length)));
                break;
            case EnemyKind.Rattle:
                var nests = Rattle.Nests(this);
                if (nests.Count == 0)
                    break;
                double pick = d.NextRange(0, nests.Sum(n => n.Weight));
                var nest = nests[^1].Car;
                foreach (var (at, weight) in nests)
                {
                    if (pick < weight)
                    {
                        nest = at;
                        break;
                    }
                    pick -= weight;
                }
                _enemies.Add(Rattle.In(_nextEnemyId++, Train, nest, Train.Dynamics.Tuning.Geometry.CouplingGap));
                break;
            case EnemyKind.Stoker:
                _enemies.Add(Stoker.InFirebox(_nextEnemyId++, Train));
                break;
            case EnemyKind.CarFire or EnemyKind.LooseLoad or EnemyKind.Gnawers:
                {
                    double minLoad = kind == EnemyKind.LooseLoad ? t.LooseLoad.MinLoad : kind == EnemyKind.Gnawers ? t.Gnawers.MinLoad : 0;
                    var holds = DarkTerritory.Sim.Enemies.Director.IncidentCars(this, kind, minLoad).ToList();
                    if (holds.Count == 0)
                        break;
                    int hold = holds[(int)d.NextRange(0, holds.Count - 1e-9)];
                    double half = Train.Frames[hold].Shape.HalfLength;
                    double along = d.NextRange(-half + 2, half - 2);
                    _enemies.Add(kind switch
                    {
                        EnemyKind.CarFire => CarFire.In(_nextEnemyId++, Train, hold, along, t.CarFire),
                        EnemyKind.LooseLoad => LooseLoad.In(_nextEnemyId++, Train, hold, along),
                        _ => Gnawers.In(_nextEnemyId++, Train, hold, along, t.Gnawers),
                    });
                    break;
                }
            case EnemyKind.LongWhistle when LongWhistle.Spot(Train, t.LongWhistle) is { } spot:
                _enemies.Add(LongWhistle.At(_nextEnemyId++, Train, spot));
                break;
            case EnemyKind.Gaunt when Gaunt.Perch(this) is { } perch:
                _enemies.Add(Gaunt.OnRoof(_nextEnemyId++, Train, perch.Car, perch.Z));
                break;
            case EnemyKind.Drift when Train.Dynamics.Consist.CarCount >= 1:
                // Over one of the cars (the ground's coming up alongside and over the whole train; it's centred somewhere).
                var over = Train.Dynamics.Consist.Vehicles.Skip(1).Select(v => v.Id).ToList();
                _enemies.Add(Drift.Over(_nextEnemyId++, Train, over[(int)d.NextRange(0, over.Count - 1e-9)], t.Drift));
                break;
            case EnemyKind.Follower when Follower.Excursions(this) is { Count: > 0 } out_:
                // On one of them, by scent: whose, the director's draw.
                int on = out_[(int)d.NextRange(0, out_.Count - 1e-9)];
                _enemies.Add(Follower.Behind(_nextEnemyId++, Train, CrewThisTick.First(c => c.Id == on).State, on, t.Followers));
                break;
            case EnemyKind.Passenger when Passenger.Boards(this) is { } boards && CrewThisTick.Where(c => c.State.Alive).Select(c => (int)c.Id).ToList() is { Count: > 0 } faces:
                // Wearing one of the crew's faces: whose, the director's draw.
                _enemies.Add(Passenger.Aboard(_nextEnemyId++, Train, boards, faces[(int)d.NextRange(0, faces.Count - 1e-9)]));
                break;
            case EnemyKind.Weight when Weight.Spot(this, t.Weight) is { } lies:
                _enemies.Add(Weight.Buried(_nextEnemyId++, lies, d.NextRange(0, 1) < 0.5 ? -1 : 1));
                break;
            case EnemyKind.Climber when Climber.Gaps(Train) is { Count: > 0 } gaps:
                _enemies.Add(Climber.Pacing(_nextEnemyId++, Train, gaps[(int)d.NextRange(0, gaps.Count - 1e-9)], d.NextRange(0, 1) < 0.5 ? -1 : 1, t.Climbers));
                break;
            case EnemyKind.Ferryman:
                _enemies.Add(Ferryman.Ahead(_nextEnemyId++, Train, d.NextRange(0, 1) < 0.5 ? -1 : 1, t.Ferryman));
                break;
            case EnemyKind.Lamplighter:
                // Out in the dark beside the engine, on the side the other isn't (if there's one already).
                int taken = _enemies.OfType<Lamplighter>().Select(l => l.Side).FirstOrDefault();
                int flank = taken != 0 ? -taken : d.NextRange(0, 1) < 0.5 ? -1 : 1;
                _enemies.Add(Lamplighter.Beside(_nextEnemyId++, Train, flank, t.Lamplighters));
                break;
        }
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
            // App. D.4: nothing deals damage inside a sealed Holdout.
            if (Holdouts?.InSealed(PlayerMotor.WorldPosition(s, Train)) == true)
                continue;
            if (d.Pull is { } outward)
            {
                PlayerMotor.PullOff(ref s, Train, outward, Train.Dynamics.Tuning, d.Cause);
                set(d.PlayerId, s);
                continue;
            }
            s.Health -= d.Amount;
            if (s.Health <= 0)
            {
                s.Health = 0;
                s.Death = d.Cause;
            }
            set(d.PlayerId, s);
        }
        if (!Derailed)
            return;
        foreach (int id in crew)
            if (get(id) is { Alive: true } s)
                set(id, s with { Health = 0, Death = DeathCause.Derailed });
    }
}
