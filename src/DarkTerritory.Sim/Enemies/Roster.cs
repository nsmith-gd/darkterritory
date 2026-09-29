using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// SLEEPERS · vibration · forward (App. A.2). Lie across the rail shaped like ties. The forward lamp reveals
/// them at 120 m; at 60 m they brace with a wet writhe you can hear. Rule: watch the road.
/// </summary>
public sealed class Sleepers(int id) : Enemy(id)
{
    public override EnemyKind Kind => EnemyKind.Sleepers;
    public override PressureZone Zone => PressureZone.Forward;
    public override Sense Sense => Sense.Vibration;
    public override bool OnMainLine => true;

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Sleepers;
        var engine = ctx.Train.Dynamics;
        // Off down a branch past their stretch of main line, the engine can't reach them (it will when it backs out).
        if (!ctx.Train.Line.OnMain(engine.Path, LineDistance))
            return;
        double ahead = LineDistance - engine.Distance;
        if (Phase == SpinePhase.Dormant && (ctx.World.LampShining && ahead <= t.LampRevealDistance || ahead <= t.BraceDistance) && ahead > 0)
            Enter(ctx, SpinePhase.Telegraph);
        if (ahead > 0.5)
            return;

        // The engine is on them. Speed decides.
        double speed = engine.Speed;
        bool fair = Enter(ctx, SpinePhase.Commit);
        var front = ctx.Train.Vehicles[engine.Consist.Vehicles[0].Id];
        if (fair && speed > t.DerailAbove)
        {
            Enter(ctx, SpinePhase.Punish);
            ctx.World.Derail();
        }
        else if (fair && speed > t.HeavyDamageAbove)
        {
            Enter(ctx, SpinePhase.Punish);
            front.Integrity = Math.Max(0, front.Integrity - t.HeavyDamage);
        }
        else
        {
            front.Integrity = Math.Max(0, front.Integrity - t.MinorDamage);
        }
        Enter(ctx, SpinePhase.Gone);
    }
}

/// <summary>
/// CINDER HOUNDS · heat, scent · rear (App. A.3). A pack running the line behind the train, gaining on every
/// grade. Only the rear gun stops them, and the gun brings the Choir. Rule: keep the rear gun crewed.
/// </summary>
public sealed class CinderHound(int id, int pack) : Enemy(id)
{
    double _biteTimer, _boredTimer;

    public override EnemyKind Kind => EnemyKind.CinderHound;
    public override PressureZone Zone => PressureZone.Rear;
    public override Sense Sense => Sense.Heat;
    public override double HitRadius => Attached < 0 ? 0.8 : 0;
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
            case SpinePhase.Telegraph:
                Run(ctx, t, closing: 0);
                if (PhaseSeconds >= t.HowlSeconds)
                    Enter(ctx, SpinePhase.Commit);
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
            case SpinePhase.Punish:
                Maul(ctx, t);
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
        Enter(ctx, SpinePhase.Punish);
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
            ctx.Bite(v.Id, t.BiteDamage, DeathCause.Mauled);
        }
    }
}

/// <summary>
/// CLINGERS · vibration · flank (App. A.4). Latch onto a cargo hull and drill; the scraping is clearly
/// audible inside that car. After 90 s the hull is breached and the cargo starts to go. Not shootable:
/// someone goes out on the roof and prises it off. Rule: go out and take them off.
/// </summary>
public sealed class Clinger(int id) : Enemy(id)
{
    public override EnemyKind Kind => EnemyKind.Clinger;
    public override PressureZone Zone => PressureZone.Flank;
    public override Sense Sense => Sense.Vibration;

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Clingers;
        if (Phase == SpinePhase.Dormant)
            Enter(ctx, SpinePhase.Telegraph); // drilling
        if (Phase == SpinePhase.Telegraph)
        {
            Extra = PhaseSeconds / t.DrillSeconds;
            if (PhaseSeconds >= t.DrillSeconds && Enter(ctx, SpinePhase.Commit))
                Enter(ctx, SpinePhase.Punish); // breach
        }
        if (Phase == SpinePhase.Punish)
        {
            var car = ctx.Train.Vehicles[Attached];
            car.CargoIntegrity = Math.Max(0, car.CargoIntegrity - t.BreachCargoLossPerSecond * SimConstants.TickSeconds);
        }

        // Counter: a player on this car's roof, over the clinger, holding Use, for 6 s.
        bool prying = ctx.Crew.Any(c => c.Player.State is { Alive: true, Surface: Surface.Roof } s && s.Parent == Attached
            && c.Intent.Has(PlayerButtons.Use) && c.Intent.MoveZ <= 0.5
            && Math.Abs(s.Position.Z - Local.Z) <= t.PryReach && Math.Sign(s.Position.X + 1e-9) == Math.Sign(Local.X));
        Extra2 = prying ? Extra2 + SimConstants.TickSeconds : 0;
        if (Extra2 >= t.PrySeconds)
        {
            Enter(ctx, SpinePhase.BreakOff);
            Enter(ctx, SpinePhase.Gone);
        }
    }
}

/// <summary>
/// HOLLOW · heat · interior (App. A.5). Comes down the stack when the fire's burned low: the fire gutters
/// and soot falls into the cab, then it hunts whoever's there until the fire is hot again. Fully preventable.
/// Rule: keep the firebox hot.
/// </summary>
public sealed class Hollow(int id) : Enemy(id)
{
    double _biteTimer;

    public override EnemyKind Kind => EnemyKind.Hollow;
    public override PressureZone Zone => PressureZone.Interior;
    public override Sense Sense => Sense.Heat;

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Hollow;
        var train = ctx.Train;
        bool hotAgain = train.BoilerTuning is { } bt && train.Boiler.FireFraction(bt) >= t.RetreatFireFraction;
        if (hotAgain)
        {
            Enter(ctx, SpinePhase.BreakOff);
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        switch (Phase)
        {
            case SpinePhase.Dormant:
                Attached = 0;
                Local = train.Frames[0].Shape.Cab!.Value.Centre;
                Enter(ctx, SpinePhase.Telegraph); // the fire gutters, soot falls
                break;
            case SpinePhase.Telegraph when PhaseSeconds >= t.DescendSeconds:
                if (Enter(ctx, SpinePhase.Commit))
                    Enter(ctx, SpinePhase.Punish);
                break;
            case SpinePhase.Punish:
                _biteTimer += SimConstants.TickSeconds;
                if (_biteTimer < t.BiteEverySeconds)
                    break;
                foreach (var (player, _) in ctx.Crew)
                {
                    if (!player.State.Alive || !PlayerMotor.InCab(player.State, train))
                        continue;
                    _biteTimer = 0;
                    ctx.Bite(player.Id, t.BiteDamage, DeathCause.Hollow);
                    break;
                }
                break;
        }
    }
}

/// <summary>
/// THE SWITCHMAN · sight · forward (App. A.7). A corrupted railway worker still doing its job: at a junction ahead it
/// throws the switch for the dead line. Its telegraph is the switch itself (the stand's lamp reads wrong from the cab)
/// and the figure standing at it; the commit is the train taking the points; the dead line and its buffer stop are the
/// punish. Approached on the ground, it goes, leaving the switch as it set it. Never directly lethal: it costs the clock.
/// Rule: verify every switch on the ground.
/// </summary>
public sealed class Switchman(int id) : Enemy(id)
{
    public override EnemyKind Kind => EnemyKind.Switchman;
    public override PressureZone Zone => PressureZone.Forward;
    public override Sense Sense => Sense.Sight;
    public override bool OnMainLine => true;

    /// <summary>The branch whose switch it works (replicated in <see cref="Enemy.Extra"/>).</summary>
    public int Branch => (int)Extra;

    /// <summary>
    /// A dead line's points ahead of the engine, inside the spawn window, whose switch is still set for the main line: the
    /// junction a Switchman would go to (App. B.7), or null. The engine has to be on the main line.
    /// </summary>
    public static Rail.Branch? Junction(World world, SwitchmanTuning t)
    {
        var train = world.Train;
        if (!train.OnMain)
            return null;
        double front = train.Dynamics.Distance;
        return train.Line.Branches.Where(b => b.Kind == Rail.BranchKind.DeadLine && !train.Diverging(b.Index)
                && b.Toe - front >= t.Ahead[0] && b.Toe - front <= t.Ahead[1])
            .OrderBy(b => b.Toe).FirstOrDefault();
    }

    /// <summary>At a junction's switch stand: beside the lever, farther out from the track.</summary>
    public static Switchman At(int id, Rail.Branch branch, SwitchmanTuning t, double leverOffset) => new(id)
    {
        Extra = branch.Index,
        LineDistance = branch.Toe,
        Lateral = branch.Side * (leverOffset + t.StandBeside),
    };

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Switchman;
        var train = ctx.Train;
        if (Branch < 0 || Branch >= train.Line.Branches.Count)
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        var branch = train.Line.Branches[Branch];
        var engine = train.Dynamics;
        switch (Phase)
        {
            case SpinePhase.Dormant:
                // Over it goes, for the dead line: the lamp now reads wrong from the cab. A wheel on the points holds them.
                Enter(ctx, ctx.World.SetSwitch(Branch, true) ? SpinePhase.Telegraph : SpinePhase.Gone);
                break;
            case SpinePhase.Telegraph:
                if (Approached(ctx, t) || !train.Diverging(Branch))
                {
                    // Seen to, or somebody coming for it: it goes. (Put back, it's lost this one.)
                    Enter(ctx, SpinePhase.BreakOff);
                    Enter(ctx, SpinePhase.Gone);
                }
                else if (engine.Path == Branch && engine.Distance > branch.Toe)
                {
                    // The train's taken the points: down the dead line it goes, to the buffer stop or a long way back.
                    Enter(ctx, Enter(ctx, SpinePhase.Commit) ? SpinePhase.Punish : SpinePhase.BreakOff);
                }
                else if (engine.Distance > branch.Toe + train.Dynamics.Consist.LengthMetres)
                {
                    // Got by on the main line (the switch was set back and thrown again behind it): nothing to do here.
                    Enter(ctx, SpinePhase.Gone);
                }
                break;
            case SpinePhase.Punish:
                if (PhaseSeconds >= t.LingerSeconds || Approached(ctx, t))
                {
                    Enter(ctx, SpinePhase.BreakOff);
                    Enter(ctx, SpinePhase.Gone);
                }
                break;
            default:
                Enter(ctx, SpinePhase.Gone);
                break;
        }
    }

    /// <summary>Someone on the ground near it.</summary>
    bool Approached(EnemyContext ctx, SwitchmanTuning t)
    {
        var at = WorldPosition(ctx.Train);
        return ctx.LivingCrew().Any(c => c.Player.State.Parent == PlayerState.World && (c.World - at).Length <= t.FleeRadius);
    }
}
