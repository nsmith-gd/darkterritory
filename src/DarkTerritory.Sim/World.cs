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
    }

    public TrainOnLine Train { get; }
    public CombatTuning? Combat { get; set; }
    public ChoirState Choir;
    /// <summary>Hit volumes for this tick (from the enemies).</summary>
    public List<HitTarget> Targets { get; } = new();
    /// <summary>Rounds fired this tick.</summary>
    public List<GunShot> Shots { get; } = new();
    public uint Tick { get; set; }
    public double ElapsedSeconds => Tick * SimConstants.TickSeconds;

    /// <summary>The engine's forward lamp. Sleepers need it lit to be seen from far off (App. A.2).</summary>
    public bool LampLit { get; set; } = true;
    /// <summary>GDD §23: derailment kills the entire crew at once.</summary>
    public bool Derailed { get; private set; }

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
        if (route is not null)
            foreach (var f in route.Of(FeatureKind.Sleepers))
                _enemies.Add(new Sleepers(_nextEnemyId++) { LineDistance = f.Start, Height = 0.2 });
    }

    /// <summary>Tonight's run (departure, facilities, terminus, dawn), when playing a route.</summary>
    public Run.Run? Run { get; private set; }

    /// <summary>Starts the run. The host steps it (<see cref="StepRun"/>); clients mirror it from records.</summary>
    public void EnableRun(Run.RunTuning tuning, Route.Route route, double yardLength, bool authority)
    {
        Run = new Run.Run(tuning, route) { YardLength = yardLength };
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
        if (Authority && Run is { } run)
            run.CrewAct(s, intent, playerId, Train);
        CrewActions.Apply(ref s, intent, Train, SimConstants.TickSeconds);
        var targets = viewTick is { } vt && _targetHistory.TryGetValue(vt, out var then) ? then : Targets;
        if (Combat is { } c && Guns.TryFire(s, intent, Train, c.Guns, ref Choir, c.Choir, targets, Tick, playerId) is { } shot)
            Shots.Add(shot);
        _context?.Crew.Add((new PlayerSnapshot((byte)playerId, s), intent));
    }

    /// <summary>Starts a tick: clears last tick's shots and events.</summary>
    public void BeginTick()
    {
        Shots.Clear();
        EnemyEvents.Clear();
        Damage.Clear();
        if (Authority && Enemies is { } t)
            _context = new EnemyContext { Tuning = t, World = this, RecentRounds = _recentRounds };
    }

    /// <summary>Advances the train and the world systems after everyone's crew actions.</summary>
    public void Step(in TrainControls controls)
    {
        Train.Step(SimConstants.TickSeconds, controls);
        if (Combat is { } c)
        {
            Guns.Step(Train);
            Choir.Step(c.Choir, SimConstants.TickSeconds);
        }
        if (Authority && _context is { } ctx)
            StepEnemies(ctx);
        Tick++;
        if (Authority)
            RefreshTargets();
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

        // The director thinks once a second; the Hollow comes whenever its condition holds (App. B.5).
        if (Tick % SimConstants.TickRate == 0 && Director is { } d && !Derailed)
        {
            if (d.Decide(this, ElapsedSeconds, _enemies, NoSpawnFinalApproach) is { } kind)
                Spawn(kind, d);
            if (Train.BoilerTuning is not null && Train.Boiler.LowFireSeconds >= t.Hollow.LowFireSeconds
                && !_enemies.Any(e => !e.Gone && e.Kind == EnemyKind.Hollow))
            {
                d.Charge(this, EnemyKind.Hollow, _enemies);
                _enemies.Add(new Hollow(_nextEnemyId++));
            }
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
