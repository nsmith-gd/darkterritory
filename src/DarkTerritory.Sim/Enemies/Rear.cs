using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// CINDER HOUNDS · heat, scent · rear (GDD v1.1 §21, App. A.3). A pack running the line behind the train, gaining on every
/// grade. Rule: keep the rear cannon crewed. A cannon shot at the pack drives it off, and it's loud (it feeds the loudness
/// meter, and the Choir). Any that board become a pack fight the crew bludgeons together: each takes several blows, and
/// bites hard; a crewmate bitten down to their last is pinned and mauled, and a friend has the rescue window to club it off.
/// </summary>
public sealed class CinderHound(int id, int pack) : Enemy(id)
{
    public override Want Want => Want.Kill;
    double _biteTimer, _boredTimer;

    public override EnemyKind Kind => EnemyKind.CinderHound;
    public override PressureZone Zone => PressureZone.Rear;
    public override Sense Sense => Sense.Heat;
    public override double HitRadius => Attached < 0 ? 0.8 : 0;
    /// <summary>Aboard, it's in reach of a tool (App. A.3 PACK FIGHT).</summary>
    public override double MeleeRadius => Attached >= 0 ? 0.8 : 0;
    public int Pack { get; } = pack;

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.CinderHounds;
        Extra = Pack;
        var train = ctx.Train.Dynamics;
        // Sustained fire from a gun in range drives a running pack off, dead or not (App. A.3 break off).
        if (Attached < 0 && Phase is SpinePhase.Telegraph or SpinePhase.Commit && ctx.World.Combat is { } combat
            && ctx.RoundsNear(WorldPosition(ctx.Train), combat.Guns.Range, t.SuppressWindowSeconds) >= t.SuppressRounds)
        {
            Enter(ctx, SpinePhase.BreakOff);
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        switch (Phase)
        {
            case SpinePhase.Dormant:
                Enter(ctx, SpinePhase.Telegraph); // acquired: the howl behind, closing
                break;
            case SpinePhase.Telegraph when Attached >= 0:
                // Snarling on the car after it's been clubbed off someone: at it again once the window's passed.
                if (PhaseSeconds >= ctx.Tuning.MinReactionSeconds)
                    Enter(ctx, SpinePhase.Commit);
                break;
            case SpinePhase.Telegraph:
                Run(ctx, t, closing: 0);
                if (PhaseSeconds >= t.HowlSeconds)
                    Enter(ctx, SpinePhase.Commit);
                break;
            case SpinePhase.Commit when Attached >= 0:
                // PACK FIGHT on the rear car, until it's clubbed off or there's nobody left to bite.
                if (ctx.Train.Dynamics.Consist.IndexOf(Attached) < 0)
                {
                    Enter(ctx, SpinePhase.Gone); // its car cut loose: it goes with it
                    break;
                }
                Maul(ctx, t);
                break;
            case SpinePhase.Commit:
                double gap = train.RearDistance - LineDistance;
                if (gap > 250)
                {
                    Enter(ctx, SpinePhase.BreakOff); // outrun: the train is faster than they can sustain
                    Enter(ctx, SpinePhase.Gone);
                    break;
                }
                if (gap <= t.LeapDistance && train.Speed <= t.MaxSpeed)
                {
                    Board(ctx);
                    break;
                }
                Run(ctx, t, t.ClosingSpeed);
                break;
            case SpinePhase.Grab:
                break;
        }
    }

    void Run(EnemyContext ctx, HoundTuning t, double closing)
    {
        double speed = Math.Min(t.MaxSpeed, ctx.Train.Dynamics.Speed + closing);
        LineDistance += speed * SimConstants.TickSeconds;
    }

    void Board(EnemyContext ctx)
    {
        int rear = ctx.Train.Dynamics.Consist.Vehicles[^1].Id;
        var shape = ctx.Train.Frames[rear].Shape;
        Attached = rear;
        Local = new Double3(Lateral > 0 ? 0.6 : -0.6, shape.RoofHeight, shape.HalfLength - 2.5);
    }

    void Maul(EnemyContext ctx, HoundTuning t)
    {
        var at = WorldPosition(ctx.Train);
        (int Id, double Distance)? victim = null;
        foreach (var (player, world) in ctx.LivingCrew())
        {
            double d = (world - at).Length;
            if (d <= 12 && (victim is null || d < victim.Value.Distance))
                victim = (player.Id, d);
        }
        if (victim is not { } v)
        {
            _boredTimer += SimConstants.TickSeconds;
            if (_boredTimer >= t.BoredSeconds)
            {
                Enter(ctx, SpinePhase.BreakOff);
                Enter(ctx, SpinePhase.Gone);
            }
            return;
        }
        _boredTimer = 0;
        _biteTimer += SimConstants.TickSeconds;
        if (v.Distance <= t.Reach && _biteTimer >= t.BiteEverySeconds)
        {
            _biteTimer = 0;
            // Bitten down to the last: it pins them (App. A.1, kills go through GRAB). Punish to Commit first: the spine's.
            var held = ctx.Crew.First(c => c.Player.Id == v.Id).Player.State;
            if (held.Health <= t.BiteDamage && Grab(ctx, v.Id, t.MaulSeconds))
                return;
            ctx.Bite(v.Id, t.BiteDamage, DeathCause.Mauled);
        }
    }

    /// <summary>Mauled: it takes its kill and leaves the fight.</summary>
    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.Mauled);
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Gone);
    }

    /// <summary>Clubbed off a crewmate it holds: back to the fight.</summary>
    protected override void Rescued(EnemyContext ctx, int by) => ReturnToFight(ctx);

    void ReturnToFight(EnemyContext ctx)
    {
        base.Rescued(ctx, -1);
        // BreakOff then back into the fight: Telegraph (a snarl) and Commit again once the window's passed.
        Enter(ctx, SpinePhase.Telegraph);
    }
}

/// <summary>
/// CAR HUGGER · vibration · rear (GDD v1.1 §21, App. A.3). It lurks on low ground beside the track, and clamps onto the
/// rear car as it passes: a heavy grinding from the rear, and the train's top speed capped while it's on (the cap stacks with
/// grades: a capped long train may not crest a hill). It eats the car's shell and loot steadily; eaten through, the car
/// drops away with it. Anyone in front of its mouth is swallowed (a grab: friends pull them free or hit it). Rule: cut the
/// caboose or kill it. Uncoupled, it leaves with the car; bludgeoned from the rear platform, it dies fast to a group and
/// slowly to one (it heals between one player's blows).
/// </summary>
/// <remarks>Lurking it's free on the line; latched, <see cref="Enemy.Attached"/> is its car, <see cref="Enemy.Local"/> its mouth at the car's rear end.</remarks>
public sealed class CarHugger(int id) : Enemy(id)
{
    public override EnemyKind Kind => EnemyKind.CarHugger;
    public override PressureZone Zone => PressureZone.Rear;
    public override Sense Sense => Sense.Vibration;
    public override Want Want => Want.Cargo;
    public override double MeleeRadius => Attached >= 0 ? 1.2 : 0;
    public override bool PullsFree => true;

    /// <summary>Clamped on a car (the telegraph onwards).</summary>
    public bool Latched => Attached >= 0 && Phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish;
    public override int Drags => Latched ? Attached : -1;
    double _cap = 9, _factor = 1.6;
    public override double DragAbove => _cap;
    public override double DragFactor => _factor;

    /// <summary>Beside the track at a low spot ahead of the train.</summary>
    public static CarHugger Lurking(int id, double along, int side, CarHuggerTuning t) =>
        new(id) { LineDistance = along, Lateral = side * 2.6, Height = -0.3, Health = t.Health, _cap = t.SpeedCap, _factor = t.DragFactor };

    /// <summary>
    /// Where it lurks (App. B.3 "marsh, water crossings, low ground"; "cannot spawn on grades"): a marsh or a bridge this far
    /// ahead, level there. Null if there's none.
    /// </summary>
    public static double? Spot(World world, CarHuggerTuning t)
    {
        if (world.Route is not { } route)
            return null;
        var train = world.Train;
        double front = train.Dynamics.Distance;
        foreach (var f in route.Features.Where(f => f.Kind is Route.FeatureKind.Marsh or Route.FeatureKind.Bridge))
        {
            double at = (f.Start + f.End) * 0.5;
            if (at < front + t.LurkAheadMin || at > front + t.LurkAheadMax)
                continue;
            if (Math.Abs(train.Line.Sample(train.Dynamics.Path, at).GradePercent) <= t.MaxGrade)
                return at;
        }
        return null;
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.CarHugger;
        var train = ctx.Train;
        _cap = t.SpeedCap;
        _factor = t.DragFactor;
        if (Phase == SpinePhase.Dormant)
        {
            // LURK until the rear car passes, then LATCH: the grinding, and the cap.
            if (train.Dynamics.RearDistance > LineDistance + 2 || train.Dynamics.Distance < LineDistance)
                return;
            int rear = train.Dynamics.Consist.Vehicles[^1].Id;
            if (rear == 0)
                return;
            var shape = train.Frames[rear].Shape;
            Attached = rear;
            Local = new Double3(0, 1.0, shape.HalfLength + 0.4);
            Enter(ctx, SpinePhase.Telegraph);
            return;
        }
        if (Attached < 0)
            return;
        // Cut loose, it goes with its car into the dark.
        if (Attached >= train.Frames.Count || train.Dynamics.Consist.IndexOf(Attached) < 0)
        {
            Enter(ctx, SpinePhase.BreakOff);
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        // Healing between blows: fast to a group, slow alone.
        Health = Math.Min(t.Health, Health + t.RegenPerSecond * SimConstants.TickSeconds);
        // FEED: shell and loot, steadily.
        var car = train.Vehicles[Attached];
        double eaten = car.Eaten;
        // An armoured car's plate is slower eating (note 184).
        car.Eaten += car.Batter(t.ShellPerSecond * SimConstants.TickSeconds, train.Dynamics.Tuning);
        // Through the end wall (the breach, decided 1 Oct): every breachEaten of shell, boarded up or not, it's through again.
        if (t.BreachEaten > 0 && Math.Floor(car.Eaten / t.BreachEaten) > Math.Floor(eaten / t.BreachEaten)
            && Breaches.EndWall(train.Frames[Attached].Shape) is { } wall)
            car.Breach(wall);
        car.CargoIntegrity = Math.Max(0, car.CargoIntegrity - t.LootPerSecond * SimConstants.TickSeconds);
        if (car.Integrity <= 0)
        {
            // FINISH: eaten through, the car drops away with it (and whoever was in it goes with the car).
            int ahead = train.VehicleAhead(Attached);
            if (ahead >= 0)
                train.Uncouple(ahead);
            car.Taken = true;
            Enter(ctx, SpinePhase.BreakOff);
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        if (Phase == SpinePhase.Telegraph && PhaseSeconds >= ctx.Tuning.MinReactionSeconds)
            Enter(ctx, SpinePhase.Commit);
        if (Phase != SpinePhase.Commit)
            return;
        // SWALLOW: in front of its mouth, the rear platform's end.
        var mouth = WorldPosition(train);
        var victim = ctx.LivingCrew().Where(c => (c.World - mouth).Length <= t.MouthReach && !c.Player.State.Has(PlayerFlags.Held))
            .OrderBy(c => (c.World - mouth).Length).Select(c => (int?)c.Player.Id).FirstOrDefault();
        if (victim is { } v)
            Grab(ctx, v, t.SwallowSeconds);
    }

    /// <summary>Eaten, then back to the car.</summary>
    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.Eaten);
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Telegraph);
    }

    /// <summary>Pulled free or hit off: it's still on the car, grinding.</summary>
    protected override void Rescued(EnemyContext ctx, int by)
    {
        base.Rescued(ctx, by);
        Enter(ctx, SpinePhase.Telegraph);
    }
}
