using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// CINDER HOUNDS · heat, scent · rear (GDD v1.1 §21, App. A.3). A pack running the line behind the train, gaining on every
/// grade. Rule: keep the rear cannon crewed. A cannon shot at the pack drives it off, and it's loud (it feeds the loudness
/// meter, and the Choir). Any that board become a pack fight the crew bludgeons together: each takes several blows, and
/// bites hard; a crewmate bitten down to their last is pinned and mauled, and a friend has the rescue window to club it off.
/// Once aboard they stay (GDD App. F, 6 Oct 2026; note 269): left alone, they eat the car's supplies and keep setting it
/// alight, until they're killed or their car is cut loose.
/// </summary>
public sealed class CinderHound(int id, int pack) : Enemy(id)
{
    public override Want Want => Want.Kill;
    double _biteTimer, _boredTimer, _from;
    bool _stays;

    public override EnemyKind Kind => EnemyKind.CinderHound;
    public override PressureZone Zone => PressureZone.Rear;
    public override Sense Sense => Sense.Heat;
    /// <summary>Running on the line behind, it's in the open: the rear cannon's work (App. A.3). Aboard, the car's walls are round it.</summary>
    public override bool Exposed => Attached < 0 && !Gone;
    public override bool GunAnswers => true;
    /// <summary>Aboard, it's in reach of a tool (App. A.3 PACK FIGHT).</summary>
    public override double MeleeRadius => Attached >= 0 ? 0.8 : 0;
    public int Pack { get; } = pack;
    /// <summary>
    /// A runner in the hound run (note 328): sent at a fast train, faster than it, and answered one at a time by a ball landing
    /// near it, not by any round fired near the pack. Host only (the director's; a client's mirror never steps it).
    /// </summary>
    public bool Runner { get; init; }
    /// <summary>
    /// A runner of the lane ahead (note 405): put down in front of the train, it meets it, across the line in the lamp, and
    /// leaps aboard the first car behind the engine. The forward gun's.
    /// </summary>
    public bool Ahead { get; init; }
    /// <summary>A runner of a flank lane (note 418): put down out in the open abeam the train, it runs in to the car alongside.</summary>
    public bool Flank { get; init; }
    public override bool StaysAboard => _stays && Attached >= 0;

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.CinderHounds;
        _stays = t.StayAboard;
        Extra = Pack;
        var train = ctx.Train.Dynamics;
        // A runner (note 328) is sent off by a ball landing near it, and only that: the guns answer the run one hound at a time.
        if (Runner && Attached < 0 && Phase is SpinePhase.Telegraph or SpinePhase.Commit)
        {
            var at = WorldPosition(ctx.Train);
            double scatter = ctx.World.Director?.Tuning.Run.Scatter ?? 0;
            if (ctx.Landed.Any(l => (l - at).Length <= scatter))
            {
                ctx.World.Director?.RunnerEnded(Pack, 0);
                Enter(ctx, SpinePhase.BreakOff);
                Enter(ctx, SpinePhase.Gone);
                return;
            }
        }
        // Sustained fire from a gun in range drives a running pack off, dead or not (App. A.3 break off).
        else if (Attached < 0 && Phase is SpinePhase.Telegraph or SpinePhase.Commit && ctx.World.Combat is { } combat
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
            case SpinePhase.Telegraph when Ahead:
                // Howling in the lamp ahead, where it was put down: the train comes on.
                if (PhaseSeconds >= t.HowlSeconds)
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
            case SpinePhase.Commit when Ahead:
                Meet(ctx);
                break;
            case SpinePhase.Commit when Flank:
                Run(ctx, t, closing: 0);
                RunIn(ctx);
                break;
            case SpinePhase.Commit:
                double gap = train.RearDistance - LineDistance;
                if (gap > 250)
                {
                    Enter(ctx, SpinePhase.BreakOff); // outrun: the train is faster than they can sustain
                    Enter(ctx, SpinePhase.Gone);
                    break;
                }
                if (gap <= t.LeapDistance && (Runner || train.Speed <= t.MaxSpeed))
                {
                    Board(ctx);
                    break;
                }
                Run(ctx, t, Runner ? ctx.World.Director?.Tuning.Run.Closing ?? t.ClosingSpeed : t.ClosingSpeed);
                break;
            case SpinePhase.Grab:
                break;
        }
    }

    void Run(EnemyContext ctx, HoundTuning t, double closing)
    {
        // A runner keeps up with any train and closes on it (note 328: a hot train can't outrun the run).
        double speed = Runner ? ctx.Train.Dynamics.Speed + closing : Math.Min(t.MaxSpeed, ctx.Train.Dynamics.Speed + closing);
        LineDistance += speed * SimConstants.TickSeconds;
    }

    /// <summary>
    /// The lane ahead (note 405): running in against the train, across the line to its far side by the time they meet, and
    /// aboard the first car behind the engine as it comes alongside (the engine's hooded, nothing to leap onto: note 338).
    /// </summary>
    void Meet(EnemyContext ctx)
    {
        var run = ctx.World.Director?.Tuning.Run ?? new HoundRunTuning();
        var train = ctx.Train;
        var consist = train.Dynamics.Consist.Vehicles;
        int car = consist[Math.Min(1, consist.Count - 1)].Id;
        var frame = train.Frames[car];
        if (frame.ToLocal(WorldPosition(train)).Z >= -frame.Shape.HalfLength)
        {
            ctx.World.Director?.RunnerEnded(Pack, 2);
            Attached = car;
            Local = new Double3(Lateral > 0 ? 0.6 : -0.6, frame.Shape.RoofHeight, -(frame.Shape.HalfLength - 2.5));
            return;
        }
        if (LineDistance < train.Dynamics.RearDistance)
        {
            Enter(ctx, SpinePhase.BreakOff); // the train's by: nothing left to leap onto
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        double closing = train.Dynamics.Speed + run.AheadSpeed;
        double meet = Math.Max(SimConstants.TickSeconds, (LineDistance - train.Dynamics.Distance) / Math.Max(1, closing));
        if (_from == 0)
            _from = Math.Sign(Lateral); // the flank it came in from
        double across = -_from * run.AheadCross;
        Lateral += (across - Lateral) * Math.Min(1, SimConstants.TickSeconds / meet);
        LineDistance -= run.AheadSpeed * SimConstants.TickSeconds;
    }

    /// <summary>
    /// A flank lane (note 418): in across the open ground, keeping pace, and aboard the car it comes alongside (not the
    /// engine's hood: note 338), on its side, once it's at the car's edge.
    /// </summary>
    void RunIn(EnemyContext ctx)
    {
        var run = ctx.World.Director?.Tuning.Run ?? new HoundRunTuning();
        var train = ctx.Train;
        double edge = train.Frames[train.Dynamics.Consist.Vehicles[^1].Id].Shape.HalfWidth + 0.6;
        if (Math.Abs(Lateral) > edge)
        {
            Lateral -= Math.Sign(Lateral) * Math.Min(Math.Abs(Lateral) - edge, run.FlankSpeed * SimConstants.TickSeconds);
            return;
        }
        var at = WorldPosition(train);
        int best = -1;
        double nearest = double.MaxValue, z = 0;
        foreach (var v in train.Dynamics.Consist.Vehicles)
        {
            if (v.IsEngine)
                continue;
            var f = train.Frames[v.Id];
            double local = f.ToLocal(at).Z, off = Math.Max(0, Math.Abs(local) - f.Shape.HalfLength);
            if (off < nearest)
                (best, nearest, z) = (v.Id, off, Math.Clamp(local, -f.Shape.HalfLength + 1, f.Shape.HalfLength - 1));
        }
        if (best < 0)
            return;
        ctx.World.Director?.RunnerEnded(Pack, 2);
        var shape = train.Frames[best].Shape;
        Attached = best;
        Local = new Double3(Lateral > 0 ? 0.6 : -0.6, shape.RoofHeight, z);
    }

    void Board(EnemyContext ctx)
    {
        if (Runner)
            ctx.World.Director?.RunnerEnded(Pack, 2);
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
            if (t.StayAboard)
                Feed(ctx, t);
            else if (_boredTimer >= t.BoredSeconds)
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

    /// <summary>
    /// Nobody near: it eats the car's supplies, and keeps setting the car alight (GDD App. F, 6 Oct 2026: "they keep setting
    /// the car alight while they eat the supplies, which forces the crew to confront them"; note 269). The fire is App. C.5's,
    /// one to a car; put out, it starts another once it's been left alone that long again.
    /// </summary>
    void Feed(EnemyContext ctx, HoundTuning t)
    {
        var train = ctx.Train;
        var car = train.Vehicles[Attached];
        car.CargoIntegrity = Math.Max(0, car.CargoIntegrity - t.CargoPerSecond * SimConstants.TickSeconds);
        if (_boredTimer < t.IgniteEverySeconds)
            return;
        _boredTimer = 0;
        if (ctx.World.ActiveEnemies.Any(e => e is CarFire f && !f.Gone && f.Attached == Attached))
            return;
        int into = Attached;
        double along = Local.Z;
        // No C.9 record: its table names no actor for a fire the hounds set (the burn's own deaths are recorded as ever).
        ctx.World.AddEnemy(i => CarFire.In(i, train, into, along, ctx.Tuning.CarFire));
    }

    /// <summary>A runner killed on the line (note 328: a ball on it) is counted for the run's report.</summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        bool running = Runner && Attached < 0 && !Gone;
        base.Struck(ctx, by, damage);
        if (running && Gone)
            ctx.World.Director?.RunnerEnded(Pack, 1);
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
    /// <summary>Lurking by the line ahead or clamped on a car, it's a great body in the open (note 290).</summary>
    public override bool Exposed => !Gone;
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
