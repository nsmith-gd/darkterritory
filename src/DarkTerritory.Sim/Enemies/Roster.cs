using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
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

/// <summary>
/// App. A.4 SOOT CHILDREN (T40): outside in the dark, calling for help in a crewmate's voice. It picks a car with crew
/// shut inside and the voice of someone who spoke lately and isn't in it: the friend the listeners think is out there.
/// <list type="bullet">
/// <item>Telegraph: the call itself, in that voice, with no distance falloff (spec A.5), every so often. The host plays
/// it (<see cref="World.Calls"/>).</item>
/// <item>Lure and commit: a door of its car opened near it, or someone on the ground near it. It takes whoever came.</item>
/// <item>Decay: ignored for its patience, it gives up and moves on.</item>
/// </list>
/// "RULE: never answer a voice from outside."
/// </summary>
public sealed class SootChildren(int id) : Enemy(id)
{
    double _nextCall;

    public override EnemyKind Kind => EnemyKind.SootChildren;
    public override PressureZone Zone => PressureZone.Structural;
    public override Sense Sense => Sense.Sound;

    /// <summary>Whose voice it's using (replicated in <see cref="Enemy.Extra"/>).</summary>
    public int Voice => (int)Extra;
    /// <summary>The car it's calling at (<see cref="Enemy.Extra2"/>).</summary>
    public int Car => (int)Extra2;

    /// <summary>
    /// Where it would go and whose voice it would use: the car with the most of the living crew shut inside it (someone
    /// to open a door), and someone who has spoken lately and isn't in there. Null when there's nobody to fool: a crew
    /// under <see cref="SootChildrenTuning.MinCrew"/>, nobody talking, or nobody shut in.
    /// </summary>
    public static (int Car, int Voice)? Choose(World world, SootChildrenTuning t, IReadOnlyList<(int Id, PlayerState State)> crew)
    {
        var train = world.Train;
        var living = crew.Where(c => c.State.Alive).ToList();
        if (living.Count < t.MinCrew)
            return null;
        uint window = (uint)Math.Min(world.Tick, t.RecentVoiceSeconds * SimConstants.TickRate);
        var talked = world.Voices.SpokeSince(world.Tick - window).ToHashSet();
        var shutIn = living.Select(c => (c.Id, Space: PlayerMotor.Space(c.State, train))).Where(c => c.Space > 0).ToList();
        foreach (var room in shutIn.GroupBy(c => c.Space).OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
        {
            int voice = living.Where(c => talked.Contains(c.Id) && PlayerMotor.Space(c.State, train) != room.Key)
                .Select(c => c.Id).DefaultIfEmpty(-1).First();
            if (voice >= 0)
                return (room.Key, voice);
        }
        return null;
    }

    /// <summary>Crouched out from a car's side, the side with a door on it if there is one.</summary>
    public static SootChildren At(int id, TrainOnLine train, int car, int voice, SootChildrenTuning t)
    {
        var shape = train.Frames[car].Shape;
        double side = shape.Interactables.Where(i => i.Kind == InteractableKind.Door && Math.Abs(i.Position.X) > 0.5)
            .Select(i => (double)Math.Sign(i.Position.X)).DefaultIfEmpty(1).First();
        var at = train.Frames[car].ToWorld(new Double3(side * (shape.HalfWidth + t.StandOff), 0, 0));
        double hint = train.Cars[car].FrontDistance;
        var (_, along) = train.Line.Nearest(at, ref hint);
        return new SootChildren(id)
        {
            Extra = voice,
            Extra2 = car,
            LineDistance = along,
            Lateral = side * (shape.HalfWidth + t.StandOff),
        };
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.SootChildren;
        var train = ctx.Train;
        if (Car <= 0 || Car >= train.Frames.Count)
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        var at = WorldPosition(train);
        switch (Phase)
        {
            case SpinePhase.Dormant:
                _nextCall = 0;
                Enter(ctx, SpinePhase.Telegraph);
                break;
            case SpinePhase.Telegraph:
                // Left behind (the train went on), or ignored long enough: it moves on.
                if ((train.Frames[Car].ToWorld(Double3.Zero) - at).Length > t.CallRadius || PhaseSeconds >= t.IgnoredSeconds)
                {
                    Enter(ctx, SpinePhase.BreakOff);
                    Enter(ctx, SpinePhase.Gone);
                    break;
                }
                if (PhaseSeconds >= _nextCall)
                {
                    ctx.World.Calls.Add((Id, Voice));
                    _nextCall += t.CallEverySeconds;
                }
                if (Answered(ctx, t, at) is { } who && Enter(ctx, SpinePhase.Commit))
                {
                    ctx.Bite(who, t.TakeDamage, DeathCause.Taken);
                    Enter(ctx, SpinePhase.Punish);
                }
                break;
            case SpinePhase.Punish:
                // It has what it came for.
                Enter(ctx, SpinePhase.BreakOff);
                Enter(ctx, SpinePhase.Gone);
                break;
            default:
                Enter(ctx, SpinePhase.Gone);
                break;
        }
    }

    /// <summary>
    /// Whoever answered it: someone on the ground near it, or, with a door of its car opened (any door: it's in the
    /// dark, and it's quick), whoever's at that door.
    /// </summary>
    int? Answered(EnemyContext ctx, SootChildrenTuning t, Double3 at)
    {
        var train = ctx.Train;
        var crew = ctx.LivingCrew().ToList();
        // (Nullable on purpose: a default crew entry reads as alive.)
        var outside = crew.Where(c => c.Player.State.Parent == PlayerState.World && (c.World - at).Length <= t.LureRadius)
            .OrderBy(c => (c.World - at).Length).Select(c => (int?)c.Player.Id).FirstOrDefault();
        if (outside is not null)
            return outside;
        var frame = train.Frames[Car];
        foreach (var door in frame.Shape.Interactables.Where(i => i.Kind == InteractableKind.Door))
        {
            if (!train.Vehicles[Car].DoorOpen(door.Index))
                continue;
            var doorway = frame.ToWorld(door.Position);
            if (crew.Where(c => (c.World - doorway).Length <= 3).OrderBy(c => (c.World - doorway).Length).Select(c => (int?)c.Player.Id).FirstOrDefault() is { } opener)
                return opener;
        }
        return null;
    }
}

/// <summary>
/// DRAGGERS · movement · flank (App. A.4). Cling under a car's edge, out of sight, dormant until someone's on the roofs
/// (App. B.4). Walk within a metre of the edge near one and a limb comes up over the lip, with a single scrape (the tell,
/// spec A.4); a moment later it grabs. Grabbed alone, you're pulled off the train, which at speed is death. Grabbed with
/// someone near, they have two seconds to free you. It never leaves its car: it follows a walker along under the edge.
/// Rule: stay off the edges; walk the centreline of the roof.
/// </summary>
/// <remarks>
/// <see cref="Enemy.Local"/> is where it clings (its X the edge it's under), <see cref="Enemy.Extra"/> who it's reaching
/// for (−1 for nobody); both replicate, so a client draws the limb and knows who's been grabbed.
/// </remarks>
public sealed class Dragger(int id) : Enemy(id)
{
    bool _rearming;
    double _window;

    public override EnemyKind Kind => EnemyKind.Dragger;
    public override PressureZone Zone => PressureZone.Flank;
    public override Sense Sense => Sense.Movement;

    /// <summary>Which edge it's under: +1 the car's right, −1 its left.</summary>
    public int Side => Local.X >= 0 ? 1 : -1;
    /// <summary>Who it's reaching for or holding, or null.</summary>
    public int? Target => Extra >= 0 ? (int)Extra : null;

    /// <summary>Clinging under one edge of a car, at a point along it.</summary>
    public static Dragger Under(int id, TrainOnLine train, int car, int side, double along)
    {
        var shape = train.Frames[car].Shape;
        return new Dragger(id)
        {
            Attached = car,
            Local = new Double3(side * (shape.HalfWidth + 0.1), shape.RoofHeight - 0.35, Math.Clamp(along, -shape.HalfLength + 0.5, shape.HalfLength - 0.5)),
            Extra = -1,
        };
    }

    /// <summary>
    /// Whether a player on its car's roof is in its reach: on its side, within the grab range of its edge (farther at speed,
    /// spec B.3), and near it along the car.
    /// </summary>
    public bool Reaches(in PlayerState s, TrainOnLine train, DraggerTuning t) =>
        s.Alive && s.Surface == Surface.Roof && s.Parent == Attached && Math.Sign(s.Position.X + 1e-9) == Side
        && train.Frames[Attached].Shape.HalfWidth - Math.Abs(s.Position.X) <= t.GrabAt(train.Dynamics.Speed)
        && Math.Abs(s.Position.Z - Local.Z) <= t.ReachAlong;

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Draggers;
        var train = ctx.Train;
        if (Attached < 0 || Attached >= train.Frames.Count)
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        var shape = train.Frames[Attached].Shape;
        Net.PlayerSnapshot? Crew(int id) => ctx.Crew.Select(c => (Net.PlayerSnapshot?)c.Player).FirstOrDefault(p => p!.Value.Id == id);
        switch (Phase)
        {
            case SpinePhase.Dormant:
                Extra = -1;
                if (_rearming && PhaseSeconds < t.RearmSeconds)
                    return;
                _rearming = false;
                // Under the lip, it follows the nearest walker on its side of this car's roof along the edge.
                var walkers = ctx.Crew.Where(c => c.Player.State is { Alive: true, Surface: Surface.Roof } s && s.Parent == Attached
                    && Math.Sign(s.Position.X + 1e-9) == Side).ToList();
                if (walkers.Count == 0)
                    return;
                var near = walkers.MinBy(c => Math.Abs(c.Player.State.Position.Z - Local.Z));
                Creep(near.Player.State.Position.Z, t, shape);
                if (Reaches(near.Player.State, train, t))
                {
                    Extra = near.Player.Id;
                    Enter(ctx, SpinePhase.Telegraph); // a limb over the lip, and the scrape
                }
                break;
            case SpinePhase.Telegraph:
                // Back off the edge in time, and it sinks back under.
                int reaching = Target ?? -1;
                if (Crew(reaching) is not { } mark || !Reaches(mark.State, train, t))
                {
                    Rearm(ctx);
                    break;
                }
                Creep(mark.State.Position.Z, t, shape);
                if (PhaseSeconds >= t.TelegraphSeconds && Enter(ctx, SpinePhase.Commit) && Enter(ctx, SpinePhase.Punish))
                {
                    // Grabbed. With someone near, they have a moment to free them; alone, it's a beat and over the side.
                    var at = PlayerMotor.WorldPosition(mark.State, train);
                    bool ally = ctx.LivingCrew().Any(c => c.Player.Id != reaching && (c.World - at).Length <= t.AllyRadius);
                    _window = ally ? t.FreeSeconds : t.AloneSeconds;
                }
                break;
            case SpinePhase.Punish:
                int held = Target ?? -1;
                if (Crew(held) is not { } victim || !victim.State.Alive || victim.State.Parent != Attached)
                {
                    Rearm(ctx);
                    break;
                }
                var there = PlayerMotor.WorldPosition(victim.State, train);
                bool freed = ctx.Crew.Any(c => c.Player.Id != held && c.Player.State.Alive && c.Intent.Has(PlayerButtons.Use) && c.Intent.MoveZ <= 0.5
                    && (PlayerMotor.WorldPosition(c.Player.State, train) - there).Length <= t.FreeReach);
                if (freed)
                {
                    Enter(ctx, SpinePhase.BreakOff);
                    Rearm(ctx);
                    break;
                }
                if (PhaseSeconds >= _window)
                {
                    ctx.Pull(held, train.Frames[Attached].DirToWorld(new Double3(Side, 0, 0)) * t.PullSpeed);
                    Enter(ctx, SpinePhase.BreakOff);
                    Rearm(ctx);
                }
                break;
            default:
                Rearm(ctx);
                break;
        }
    }

    void Creep(double z, DraggerTuning t, CarShape shape)
    {
        double step = t.CreepSpeed * SimConstants.TickSeconds;
        double to = Math.Clamp(z, -shape.HalfLength + 0.5, shape.HalfLength - 0.5);
        Local = Local with { Z = Local.Z + Math.Clamp(to - Local.Z, -step, step) };
    }

    /// <summary>Back under the lip, not reaching again for a while. It never leaves its car.</summary>
    void Rearm(EnemyContext ctx)
    {
        Extra = -1;
        _rearming = true;
        Enter(ctx, SpinePhase.Dormant);
    }
}

/// <summary>
/// RATTLE · vibration · interior (App. A.5). "Lives in the couplings. You hear it before you cross." It nests in a coupling
/// gap, silent, until someone comes near it below the roofs; then it rattles, and whoever steps into the gap while it
/// rattles is grabbed and pulled under. No approach for a while and it goes quiet again. It can't be shot or driven off:
/// wait it out, or go over the roof. Rule: don't cross between cars rattling.
/// </summary>
/// <remarks>
/// <see cref="Enemy.Attached"/> is the vehicle ahead of its gap and <see cref="Enemy.Local"/> the gap's middle in that
/// vehicle's frame; both replicate, so a client knows which gap is rattling (the tell, and what bots keep out of).
/// </remarks>
public sealed class Rattle(int id) : Enemy(id)
{
    double _quiet;
    double _age;

    public override EnemyKind Kind => EnemyKind.Rattle;
    public override PressureZone Zone => PressureZone.Interior;
    public override Sense Sense => Sense.Vibration;

    /// <summary>Rattling: the telegraph, and all the warning there is.</summary>
    public bool Rattling => Phase == SpinePhase.Telegraph;

    /// <summary>In the gap behind <paramref name="car"/> (it has to have a vehicle coupled behind it).</summary>
    public static Rattle In(int id, TrainOnLine train, int car, double couplingGap) => new(id)
    {
        Attached = car,
        Local = new Double3(0, 0.6, train.Frames[car].Shape.HalfLength + couplingGap * 0.5),
    };

    /// <summary>
    /// The gaps it could take in the engine's rake (the vehicle ahead of each, and how much it prefers it): mid-train
    /// ones most (App. B.5), and none with someone already standing in it.
    /// </summary>
    public static List<(int Car, double Weight)> Nests(World world)
    {
        var train = world.Train;
        var rake = train.Dynamics.Consist.Vehicles;
        double mid = (rake.Count - 2) * 0.5;
        var nests = new List<(int, double)>();
        for (int i = 0; i + 1 < rake.Count; i++)
        {
            var probe = In(0, train, rake[i].Id, train.Dynamics.Tuning.Geometry.CouplingGap);
            if (world.CrewThisTick.Any(c => c.State.Alive && probe.InGap(PlayerMotor.WorldPosition(c.State, train), train)))
                continue;
            nests.Add((rake[i].Id, 1 / (1 + Math.Abs(i - mid))));
        }
        return nests;
    }

    /// <summary>
    /// Whether a point is in its gap: between the two cars' ends, no wider than the cars, below their roofs (over the roofs
    /// is the way round it). The ground between the cars at a stop counts; that's the gap too.
    /// </summary>
    public bool InGap(Double3 world, TrainOnLine train)
    {
        if (Attached < 0 || Attached >= train.Frames.Count)
            return false;
        var frame = train.Frames[Attached];
        var local = frame.ToLocal(world);
        double half = frame.Shape.HalfLength;
        return Math.Abs(local.X) <= frame.Shape.HalfWidth && local.Z >= half && local.Z <= 2 * Local.Z - half
            && local.Y < frame.Shape.RoofHeight - 0.5 && local.Y > -1.5;
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Rattle;
        var train = ctx.Train;
        _age += SimConstants.TickSeconds;
        // It lives in the coupling: cut the cars apart there and it's gone with it.
        if (Attached < 0 || Attached >= train.Frames.Count || train.VehicleBehind(Attached) < 0)
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        var gap = WorldPosition(train);
        var near = ctx.LivingCrew().Where(c => (c.World - gap).Length <= t.ArmRadius && train.Frames[Attached].ToLocal(c.World).Y < train.Frames[Attached].Shape.RoofHeight - 0.5).ToList();
        switch (Phase)
        {
            case SpinePhase.Dormant:
                if (_age >= t.LingerSeconds)
                    Enter(ctx, SpinePhase.Gone);
                else if (near.Count > 0)
                {
                    _quiet = 0;
                    Enter(ctx, SpinePhase.Telegraph); // the rattle
                }
                break;
            case SpinePhase.Telegraph:
                // Its time's up, rattling or not: a stop's thing, it doesn't ride the night out (T54).
                if (_age >= t.LingerSeconds)
                {
                    Enter(ctx, SpinePhase.Gone);
                    break;
                }
                _quiet = near.Count > 0 ? 0 : _quiet + SimConstants.TickSeconds;
                if (_quiet >= t.QuietSeconds)
                {
                    Enter(ctx, SpinePhase.Dormant);
                    break;
                }
                // Stepping into the gap while it rattles (and it's rattled long enough to have been heard, App. A.1).
                var into = near.Where(c => InGap(c.World, train)).Select(c => (int?)c.Player.Id).FirstOrDefault();
                if (into is { } victim && Enter(ctx, SpinePhase.Commit) && Enter(ctx, SpinePhase.Punish))
                {
                    ctx.Bite(victim, t.GrabDamage, DeathCause.PulledUnder);
                    Enter(ctx, SpinePhase.BreakOff);
                    Enter(ctx, SpinePhase.Dormant);
                }
                break;
            default:
                Enter(ctx, SpinePhase.Dormant);
                break;
        }
    }
}

/// <summary>
/// LAMPLIGHTERS · light · structural (App. A.6). "Light-reactive. Work the lineside." One paces the train out in the dark
/// beside the engine, just beyond the lamp's reach. While the forward lamp is lit it comes for it (the tell: its eyeshine at
/// the edge of the lamp's light), and reaching it, smashes the lamp and goes for whoever's nearest. Put the lights out and
/// it loses track and goes back to the lineside. It can't keep up with a train at speed. Rule: lamps down, which is what
/// the Sleepers ahead need lit. "That contradiction is the point."
/// </summary>
/// <remarks><see cref="Enemy.Lateral"/>'s sign is its side; it's free on the line, placed along the engine's path.</remarks>
public sealed class Lamplighter(int id) : Enemy(id)
{
    double _age;
    bool _lost;

    public override EnemyKind Kind => EnemyKind.Lamplighter;
    public override PressureZone Zone => PressureZone.Structural;
    public override Sense Sense => Sense.Light;

    /// <summary>Its eyes catching the light: while it comes for the lamp (the telegraph), and as it strikes.</summary>
    public bool Eyeshine => Phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Punish;
    public int Side => Lateral >= 0 ? 1 : -1;

    /// <summary>Beside the engine, out past the lamp, on one side.</summary>
    public static Lamplighter Beside(int id, TrainOnLine train, int side, LamplighterTuning t) => new(id)
    {
        LineDistance = train.Dynamics.Distance - t.PaceBehind,
        Lateral = side * t.PaceLateral,
    };

    /// <summary>The engine's forward lamp (world): the light it's after.</summary>
    public static Double3 Lamp(TrainOnLine train) => World.LampPosition(train.Frames[0]);

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Lamplighters;
        var train = ctx.Train;
        double dt = SimConstants.TickSeconds;
        _age += dt;
        double front = train.Dynamics.Distance;
        // Left behind by a train going faster than it can run, it's lost.
        if (LineDistance < train.Dynamics.RearDistance - t.LoseBehind)
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        bool lit = ctx.World.LampShining;
        double wantAlong = front - t.PaceBehind, wantLateral = Side * t.PaceLateral;
        switch (Phase)
        {
            case SpinePhase.Dormant:
                if (lit)
                    Enter(ctx, SpinePhase.Telegraph); // a light: it comes, eyes catching it
                else if (_age >= t.LingerSeconds)
                    Enter(ctx, SpinePhase.Gone);
                break;
            case SpinePhase.Telegraph:
                if (!lit)
                {
                    // Lights out: it's lost the light, and stops following ("loses track, returns to the lineside").
                    _lost = true;
                    Enter(ctx, SpinePhase.BreakOff);
                    break;
                }
                wantAlong = front;
                wantLateral = Side * t.StrikeLateral;
                // It reaches up to the lamp from the ballast: within reach of it across the ground.
                if (((WorldPosition(train) - Lamp(train)) with { Y = 0 }).Length <= t.StrikeReach && Enter(ctx, SpinePhase.Commit) && Enter(ctx, SpinePhase.Punish))
                {
                    ctx.World.SmashLamp(t.RelightSeconds);
                    var at = WorldPosition(train);
                    var nearest = ctx.LivingCrew().Where(c => (c.World - at).Length <= t.BiteReach).OrderBy(c => (c.World - at).Length).Select(c => (int?)c.Player.Id).FirstOrDefault();
                    if (nearest is { } victim)
                        ctx.Bite(victim, t.BiteDamage, DeathCause.Lamplighter);
                    _lost = false;
                    Enter(ctx, SpinePhase.BreakOff);
                }
                break;
            case SpinePhase.BreakOff when _lost:
                // Standing out at the lineside where it lost the light: a moving train leaves it behind. Lit again while
                // it's still near (a stopped train), it comes again.
                if (lit && Math.Abs(Lateral) >= t.PaceLateral - 0.5)
                {
                    _lost = false;
                    Enter(ctx, SpinePhase.Telegraph);
                }
                else if (_age >= t.LingerSeconds)
                    Enter(ctx, SpinePhase.Gone);
                break;
            case SpinePhase.BreakOff:
                // Having smashed the lamp: back out to pace the train until it's lit again.
                if (Math.Abs(Lateral) >= t.PaceLateral - 0.5)
                    Enter(ctx, SpinePhase.Dormant);
                break;
            default:
                Enter(ctx, SpinePhase.BreakOff);
                break;
        }
        // It runs alongside, as fast as it can; having lost track, it stands.
        double speed = Phase == SpinePhase.BreakOff && _lost ? 0
            : Math.Clamp(train.Dynamics.Velocity + (wantAlong - LineDistance) * t.Catch, -t.MaxSpeed, t.MaxSpeed);
        LineDistance += speed * dt;
        Lateral += Math.Clamp(wantLateral - Lateral, -t.CloseSpeed * dt, t.CloseSpeed * dt);
    }
}

/// <summary>
/// DEADMAN · absence · interior (App. A.5). "Takes the cab if nobody is in it." Condition-triggered: the cab's been empty a
/// while and it begins its approach (the tell: the cab lamp dims and the controls click on their own). Still empty when
/// its time's up, it takes the cab: the regulator locks open, the brake does nothing, and the train runs on at whatever's
/// next. Someone in the cab contests it; it takes a few seconds, and it hurts. Fully preventable, and it costs budget only
/// when it takes the cab (App. B.5). Rule: never leave the cab empty.
/// </summary>
public sealed class Deadman(int id) : Enemy(id)
{
    double _contest;

    public override EnemyKind Kind => EnemyKind.Deadman;
    public override PressureZone Zone => PressureZone.Interior;
    public override Sense Sense => Sense.Absence;

    /// <summary>At the controls: the world locks the regulator open and the brake off while it is.</summary>
    public bool Holding => Phase == SpinePhase.Punish;

    /// <summary>Watching the cab from outside.</summary>
    public static Deadman Watching(int id, TrainOnLine train) => new(id) { Attached = 0, Local = train.Frames[0].Shape.Cab!.Value.Centre };

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Deadman;
        var train = ctx.Train;
        var inCab = ctx.Crew.Where(c => c.Player.State.Alive && PlayerMotor.InCab(c.Player.State, train)).Select(c => c.Player.Id).ToList();
        switch (Phase)
        {
            case SpinePhase.Dormant:
                Enter(ctx, SpinePhase.Telegraph); // the lamp dims, the controls click
                break;
            case SpinePhase.Telegraph:
                // Someone back in the cab: it's free to prevent.
                if (inCab.Count > 0)
                {
                    Enter(ctx, SpinePhase.BreakOff);
                    Enter(ctx, SpinePhase.Gone);
                }
                else if (PhaseSeconds >= t.TelegraphSeconds && Enter(ctx, SpinePhase.Commit) && Enter(ctx, SpinePhase.Punish))
                    ctx.World.Director?.Charge(ctx.World, EnemyKind.Deadman, ctx.World.ActiveEnemies);
                break;
            case SpinePhase.Punish:
                if (inCab.Count == 0)
                {
                    _contest = 0;
                    break;
                }
                // "A player entering contests it: ~4 s and damage taken."
                if (_contest == 0)
                    ctx.Bite(inCab[0], t.EvictDamage, DeathCause.Deadman);
                _contest += SimConstants.TickSeconds;
                if (_contest >= t.EvictSeconds)
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
}

/// <summary>
/// STOKER · heat · interior (App. A.5). "Gets into the firebox. Pressure climbs on its own." In during a stop with the
/// firebox unattended, it feeds the boiler (through <see cref="Boiler.ExternalHeat"/>) and holds the safety valve shut: the
/// gauge climbs with no fuel going in (the tell, with the fire's wrong colour and its hiss). At the maximum for the
/// boiler's rupture hold, the boiler goes. Counters: vent (faster than it feeds, but it costs the pressure and so the
/// clock), or hold Use at the firebox and drive it out, which it makes whoever does it pay for. Rule: vent, or the boiler
/// goes.
/// </summary>
public sealed class Stoker(int id) : Enemy(id)
{
    double _driving;

    public override EnemyKind Kind => EnemyKind.Stoker;
    public override PressureZone Zone => PressureZone.Interior;
    public override Sense Sense => Sense.Heat;

    /// <summary>In the engine's firebox.</summary>
    public static Stoker InFirebox(int id, TrainOnLine train) => new(id)
    {
        Attached = 0,
        Local = train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position,
    };

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Stoker;
        var train = ctx.Train;
        if (train.BoilerTuning is not { } bt || train.Boiler.Ruptured)
        {
            Leave(ctx, train);
            return;
        }
        if (Phase == SpinePhase.Dormant)
            Enter(ctx, SpinePhase.Telegraph); // the gauge starts to climb on its own
        // The train's boiler is a struct it holds: written through, not a copy.
        train.Boiler.ExternalHeat = t.FeedRate;
        train.Boiler.SafetyValveJammed = true;
        if (Phase == SpinePhase.Telegraph && train.Boiler.Pressure >= bt.PressureMax - 1e-6 && Enter(ctx, SpinePhase.Commit))
            Enter(ctx, SpinePhase.Punish); // at the maximum: the rupture clock runs
        // Drive it out: Use held at the firebox, and it lashes out at whoever's doing it.
        var driver = ctx.Crew.Where(c => c.Player.State.Alive && c.Intent.Has(PlayerButtons.Use) && PlayerMotor.InCab(c.Player.State, train)
            && CrewActions.Nearest(c.Player.State, train, ctx.World.Hand) == InteractableKind.Firebox).Select(c => (int?)c.Player.Id).FirstOrDefault();
        if (driver is not { } who)
        {
            _driving = 0;
            return;
        }
        if (_driving == 0)
            ctx.Bite(who, t.DriveOutDamage, DeathCause.Stoker);
        _driving += SimConstants.TickSeconds;
        if (_driving >= t.DriveOutSeconds)
        {
            Enter(ctx, SpinePhase.BreakOff);
            Leave(ctx, train);
        }
    }

    void Leave(EnemyContext ctx, TrainOnLine train)
    {
        train.Boiler.ExternalHeat = 0;
        train.Boiler.SafetyValveJammed = false;
        Enter(ctx, SpinePhase.Gone);
    }
}

/// <summary>
/// THE FERRYMAN · sight · forward (App. A.2). "Stands on the track ahead holding a lantern, waving you down." It waits at
/// the lineside far up a long straight, the lantern raised: the lantern itself is the telegraph, seen from very far out.
/// As the train comes it waves and steps onto the line. If the train slows (any real deceleration from the fastest it has
/// come at it), it closes on the slowed train, boards the engine and goes for the conductor. If the train holds its speed
/// or goes faster, it steps aside at the last moment and is gone, never to come again. Rule: do not slow down. "Entirely
/// defeated by doing nothing." It can't be shot.
/// </summary>
/// <remarks>
/// Free on the line along the engine's path until it boards; then in the cab. <see cref="Enemy.Extra"/> is the fastest the
/// train has come at it, <see cref="Enemy.Extra2"/> the side it stepped (or steps) to.
/// </remarks>
public sealed class Ferryman(int id) : Enemy(id)
{
    public override EnemyKind Kind => EnemyKind.Ferryman;
    public override PressureZone Zone => PressureZone.Forward;
    public override Sense Sense => Sense.Sight;

    /// <summary>The lantern's lit and raised: until it's stepped aside or struck.</summary>
    public bool Lantern => Phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Punish;
    /// <summary>Waving the train down: the lantern swings while it lures.</summary>
    public bool Waving => Phase == SpinePhase.Telegraph;
    public bool Aboard => Attached == 0;
    /// <summary>The lantern's seen from very far out: past the interest radius.</summary>
    public override bool Far => Lantern;
    int Side => Extra2 < 0 ? -1 : 1;

    /// <summary>Up the line ahead of the engine at the lineside, on one side.</summary>
    public static Ferryman Ahead(int id, TrainOnLine train, int side, FerrymanTuning t) => new(id)
    {
        LineDistance = train.Dynamics.Distance + t.SpawnAhead,
        Lateral = side * t.WaitLateral,
        Height = 0,
        Extra = train.Dynamics.Speed,
        Extra2 = side,
    };

    /// <summary>
    /// The director's gate for placing one (App. B.2 "long straight with clear sightline"): from the engine to past where it
    /// would stand, the line is straight enough and near level, and nothing on it gives a crew a reason to slow down: no
    /// Sleepers, Grease, tunnel, facility or the end of the line. It's not a trap you can only lose.
    /// </summary>
    public static bool ClearAhead(World world, FerrymanTuning t)
    {
        var train = world.Train;
        double from = train.Dynamics.Distance, to = from + t.SpawnAhead + t.ClearPast;
        if (train.Line.PathLength(train.Dynamics.Path) < to + t.StopMargin)
            return false;
        for (double s = from; s <= to; s += 25)
        {
            var sample = train.Line.Sample(train.Dynamics.Path, s);
            if (Math.Abs(sample.Curvature) > 1 / t.StraightRadius || Math.Abs(sample.GradePercent) > t.MaxGradePercent)
                return false;
        }
        return world.Route is not { } r || !r.Features.Any(f => f.Kind is FeatureKind.Sleepers or FeatureKind.Grease or FeatureKind.Tunnel or FeatureKind.Facility
            && f.End >= from && f.Start <= to + t.StopMargin);
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Ferryman;
        var train = ctx.Train;
        double dt = SimConstants.TickSeconds;
        double speed = train.Dynamics.Speed;
        double gap = LineDistance - train.Dynamics.Distance;
        switch (Phase)
        {
            case SpinePhase.Dormant:
                Enter(ctx, SpinePhase.Telegraph); // the lantern, raised
                break;
            case SpinePhase.Telegraph:
                Extra = Math.Max(Extra, speed);
                // It waves the train down, stepping out onto the line.
                Lateral += Math.Clamp(-Lateral, -t.StepSpeed * dt, t.StepSpeed * dt);
                if (speed < Extra - t.SlowTolerance)
                {
                    // The train's slowing for it: it comes.
                    Enter(ctx, SpinePhase.Commit);
                    break;
                }
                // Held its speed: at the last moment it steps aside, and it's done.
                if (gap <= t.StepAsideAt)
                    Enter(ctx, SpinePhase.BreakOff);
                else if (PhaseSeconds >= t.LingerSeconds)
                    Enter(ctx, SpinePhase.Gone);
                break;
            case SpinePhase.Commit:
                // ADVANCE: onto the line and down it to the slowed train.
                Lateral += Math.Clamp(-Lateral, -t.StepSpeed * dt, t.StepSpeed * dt);
                LineDistance -= t.AdvanceSpeed * dt;
                if (LineDistance - train.Dynamics.Distance <= t.BoardReach)
                {
                    Attached = 0;
                    Local = train.Frames[0].Shape.Cab!.Value.Centre;
                    Enter(ctx, SpinePhase.Punish);
                    // "Cab invasion; attacks the conductor directly": whoever's in the cab, else whoever's nearest.
                    var at = WorldPosition(train);
                    var victim = ctx.Crew.Where(c => c.Player.State.Alive && PlayerMotor.InCab(c.Player.State, train)).Select(c => (int?)c.Player.Id).FirstOrDefault()
                        ?? ctx.LivingCrew().Where(c => (c.World - at).Length <= t.StrikeReach).OrderBy(c => (c.World - at).Length).Select(c => (int?)c.Player.Id).FirstOrDefault();
                    if (victim is { } who)
                        ctx.Bite(who, t.StrikeDamage, DeathCause.Ferryman);
                }
                break;
            case SpinePhase.Punish:
                if (PhaseSeconds >= t.AboardSeconds)
                    Enter(ctx, SpinePhase.Gone);
                break;
            case SpinePhase.BreakOff:
                // Aside, lantern down, watching it go by. Cannot re-engage.
                Lateral += Math.Clamp(Side * t.StepAsideLateral - Lateral, -t.StepSpeed * dt, t.StepSpeed * dt);
                if (LineDistance < train.Dynamics.RearDistance - t.LoseBehind || PhaseSeconds >= t.LingerSeconds)
                    Enter(ctx, SpinePhase.Gone);
                break;
            default:
                Enter(ctx, SpinePhase.Gone);
                break;
        }
    }
}

/// <summary>
/// THE LONG WHISTLE · sound · forward (App. A.2). "Sounds a horn on the line ahead. There is no train ahead." Hidden, never
/// seen, from a point up the line just short of a grade or a curve, it sounds an authentic locomotive horn, again and
/// louder (the telegraph: the horn doesn't doppler and its pitch is flawed). A crew that brakes hard for it has lost: "the
/// stop itself is the punishment", on the worst stretch to stop on, with whatever else is about. Ignored, it escalates
/// twice and abandons. It has no punish of its own and never comes alone (App. A.8). Rule: don't trust the horn.
/// </summary>
/// <remarks>
/// Free on the line along the engine's path, at a fixed point. <see cref="Enemy.Extra"/> counts the blasts sounded (the
/// audio plays one each time it goes up), <see cref="Enemy.Extra2"/> is the fastest the train has come at it.
/// </remarks>
public sealed class LongWhistle(int id) : Enemy(id)
{
    public override EnemyKind Kind => EnemyKind.LongWhistle;
    public override PressureZone Zone => PressureZone.Forward;
    public override Sense Sense => Sense.Sound;

    public int Blasts => (int)Extra;
    /// <summary>The horn carries far past the interest radius.</summary>
    public override bool Far => true;

    /// <summary>At a point up the line ahead.</summary>
    public static LongWhistle At(int id, TrainOnLine train, double along) => new(id)
    {
        LineDistance = along,
        Height = 3,
        Extra2 = train.Dynamics.Speed,
    };

    /// <summary>
    /// Where it sounds from (App. B.2): 400-900 m ahead, "immediately before a grade or curve so braking is worst". The
    /// first place in that window where the line starts to curve or climb or fall, just short of it; null if there's none.
    /// </summary>
    public static double? Spot(TrainOnLine train, LongWhistleTuning t)
    {
        double front = train.Dynamics.Distance;
        double end = Math.Min(front + t.AheadMax, train.Line.PathLength(train.Dynamics.Path) - t.ShortOf);
        for (double s = front + t.AheadMin + t.ShortOf; s <= end; s += 10)
        {
            var at = train.Line.Sample(train.Dynamics.Path, s);
            if (Math.Abs(at.Curvature) > 1 / t.CurveRadius || Math.Abs(at.GradePercent) >= t.GradePercent)
                return s - t.ShortOf;
        }
        return null;
    }

    /// <summary>
    /// App. B.2: "requires ≥1 other active lineside threat in region". Anything but Sleepers (level content) and other
    /// Long Whistles that's out on the line or the train within the region of the engine: whatever does the killing.
    /// </summary>
    public static bool Company(World world, LongWhistleTuning t) =>
        world.ActiveEnemies.Any(e => !e.Gone && e.Kind is not (EnemyKind.Sleepers or EnemyKind.LongWhistle)
            && (e.WorldPosition(world.Train) - world.Train.Frames[0].Origin).Length <= t.Region);

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.LongWhistle;
        var train = ctx.Train;
        double speed = train.Dynamics.Speed;
        // The train's reached it (or gone by down another line): nothing ahead to sound from.
        if (LineDistance <= train.Dynamics.Distance && Phase is SpinePhase.Dormant or SpinePhase.Telegraph)
        {
            Enter(ctx, SpinePhase.BreakOff);
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        switch (Phase)
        {
            case SpinePhase.Dormant:
                Enter(ctx, SpinePhase.Telegraph);
                Extra = 1; // the first blast
                break;
            case SpinePhase.Telegraph:
                Extra2 = Math.Max(Extra2, speed);
                if (Extra2 - speed >= t.HardBrake && Enter(ctx, SpinePhase.Commit))
                {
                    // The crew's braking for a train that isn't there: the false positive the Ferryman feeds on (App. B.2).
                    ctx.World.BrakedForFalseAlarm = true;
                    break;
                }
                // Ignored: louder, twice, then it abandons.
                if (PhaseSeconds >= Blasts * t.BlastEvery)
                {
                    if (Blasts > t.Escalations)
                    {
                        Enter(ctx, SpinePhase.BreakOff);
                        Enter(ctx, SpinePhase.Gone);
                    }
                    else
                        Extra = Blasts + 1;
                }
                break;
            case SpinePhase.Commit:
                // The stop is the punishment. Picked up again short of a stand, it's had what it wanted all the same.
                if (speed <= t.StoppedBelow)
                    Enter(ctx, SpinePhase.Punish);
                else if (PhaseSeconds >= t.CommitSeconds)
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
}

/// <summary>
/// CLIMBERS · movement · flank (App. A.4). They run alongside at track level, matching the train, and mount only at a
/// coupling gap: a brief scrabbling there (the telegraph, "visible from adjacent roofs"), then up between the cars and along
/// the roofs toward the engine, and into the first car nobody's in (any car, lights out), where it's an interior threat.
/// At the engine, the cab. Someone standing in the gap blocks that mount point: it drops back and tries another. On the
/// roofs it can be shot. "Scales directly with train length": every gap is a way on, held by the same crew.
/// </summary>
/// <remarks>
/// <see cref="Enemy.Extra"/> is the vehicle ahead of the gap it's making for (pacing and mounting), <see cref="Enemy.Extra2"/>
/// its side (−1 or +1). Pacing it's free on the line; from the gap on it's on a vehicle.
/// </remarks>
public sealed class Climber(int id) : Enemy(id)
{
    double _bored, _bite;
    int _tries;
    readonly List<int> _tried = [];

    public override EnemyKind Kind => EnemyKind.Climber;
    public override PressureZone Zone => PressureZone.Flank;
    public override Sense Sense => Sense.Movement;
    /// <summary>Out on the roofs (or scrabbling at the gap), it's in the open: the guns can take it.</summary>
    public override double HitRadius => Phase is SpinePhase.Telegraph or SpinePhase.Commit ? 0.55 : 0;

    public int Gap => (int)Extra;
    public int Side => Extra2 < 0 ? -1 : 1;
    /// <summary>Scrabbling at the gap: the telegraph.</summary>
    public bool Scrabbling => Phase == SpinePhase.Telegraph;
    /// <summary>In a car (or the cab): the interior threat.</summary>
    public bool Inside => Phase == SpinePhase.Punish;

    /// <summary>Alongside the gap behind <paramref name="car"/>, out from the train on one side.</summary>
    public static Climber Pacing(int id, TrainOnLine train, int car, int side, ClimberTuning t)
    {
        var c = new Climber(id) { Extra = car, Extra2 = side, Health = t.Health };
        c.LineDistance = GapAlong(train, car);
        c.Lateral = side * (train.Frames[car].Shape.HalfWidth + t.PaceOut);
        return c;
    }

    /// <summary>Along-line distance of the gap behind a vehicle.</summary>
    static double GapAlong(TrainOnLine train, int car) =>
        train.Cars[car].FrontDistance - train.Cars[car].Length - train.Dynamics.Tuning.Geometry.CouplingGap * 0.5;

    /// <summary>
    /// The gaps it can mount at, as the vehicle ahead of each: every coupling in the engine's rake behind a car (not the
    /// engine's own: that's the cab, and the fireman's). App. B.4: "weight scales directly with gap count".
    /// </summary>
    public static List<int> Gaps(TrainOnLine train)
    {
        var rake = train.Dynamics.Consist.Vehicles;
        var gaps = new List<int>();
        for (int i = 1; i + 1 < rake.Count; i++)
            gaps.Add(rake[i].Id);
        return gaps;
    }

    /// <summary>Someone's in the gap behind that vehicle, below the roofs or stood on a roof end over it: it's held.</summary>
    public static bool Held(EnemyContext ctx, int car, ClimberTuning t)
    {
        var train = ctx.Train;
        var probe = Rattle.In(0, train, car, train.Dynamics.Tuning.Geometry.CouplingGap);
        var gap = probe.WorldPosition(train);
        return ctx.LivingCrew().Any(c => probe.InGap(c.World, train) || ((c.World - gap) with { Y = 0 }).Length <= t.HoldReach);
    }

    /// <summary>Someone living is inside that car (standing in it, doors shut or not).</summary>
    static bool Occupied(EnemyContext ctx, int car) =>
        ctx.Crew.Any(c => c.Player.State is { Alive: true, Surface: Surface.Deck } s && s.Parent == car
            && ctx.Train.Frames[car].Shape.Interior is { } room && room.Contains(s.Position));

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Climbers;
        var train = ctx.Train;
        double dt = SimConstants.TickSeconds;
        // The car it's on or making for has gone (cut off and left behind): so has it.
        if (Gap >= train.Frames.Count || Attached >= train.Frames.Count)
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        switch (Phase)
        {
            case SpinePhase.Dormant:
                {
                    // PACE: alongside, making for its gap, as fast as it can run.
                    if (train.VehicleBehind(Gap) < 0)
                    {
                        Retry(ctx, t);
                        break;
                    }
                    double want = GapAlong(train, Gap);
                    double speed = Math.Clamp(train.Dynamics.Velocity + (want - LineDistance) * t.Catch, -t.MaxSpeed, t.MaxSpeed);
                    LineDistance += speed * dt;
                    if (LineDistance < train.Dynamics.RearDistance - t.LoseBehind)
                    {
                        Enter(ctx, SpinePhase.Gone);
                        break;
                    }
                    if (PhaseSeconds >= t.PaceSeconds && Math.Abs(want - LineDistance) <= 1.5)
                    {
                        if (Held(ctx, Gap, t))
                        {
                            Retry(ctx, t);
                            break;
                        }
                        // At the gap: the scrabbling, in at the couplers from the side.
                        var shape = train.Frames[Gap].Shape;
                        Attached = Gap;
                        Local = new Double3(Side * (shape.HalfWidth - 0.2), 1.0, shape.HalfLength + train.Dynamics.Tuning.Geometry.CouplingGap * 0.5);
                        Enter(ctx, SpinePhase.Telegraph);
                    }
                    break;
                }
            case SpinePhase.Telegraph:
                // Someone gets into the gap as it scrabbles: blocked, it drops back and tries another.
                if (Held(ctx, Gap, t))
                {
                    Retry(ctx, t);
                    break;
                }
                if (PhaseSeconds >= t.ScrabbleSeconds && Enter(ctx, SpinePhase.Commit))
                {
                    // MOUNT: up between the cars, onto the roof of the one ahead, at its back end.
                    var shape = train.Frames[Gap].Shape;
                    Local = new Double3(0, shape.RoofHeight, shape.HalfLength - 0.4);
                }
                break;
            case SpinePhase.Commit:
                Traverse(ctx, t);
                break;
            case SpinePhase.Punish:
                Nest(ctx, t);
                break;
            default:
                Enter(ctx, SpinePhase.Gone);
                break;
        }
    }

    /// <summary>Blocked or lost its gap: drop back off the train and make for another it hasn't tried, or give up.</summary>
    void Retry(EnemyContext ctx, ClimberTuning t)
    {
        var train = ctx.Train;
        _tried.Add(Gap);
        _tries++;
        var left = Gaps(train).Where(g => !_tried.Contains(g)).ToList();
        if (_tries >= t.MaxTries || left.Count == 0)
        {
            Enter(ctx, SpinePhase.BreakOff);
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        // The nearest untried gap: it doesn't run the train's length to find one.
        int next = left.OrderBy(g => Math.Abs(g - Gap)).First();
        if (Attached >= 0)
        {
            LineDistance = GapAlong(train, Gap);
            Lateral = Side * (train.Frames[Gap].Shape.HalfWidth + t.PaceOut);
            Attached = -1;
        }
        Extra = next;
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Dormant);
    }

    /// <summary>TRAVERSE: along the roofs toward the engine, into the first car nobody's in; at the engine, the cab.</summary>
    void Traverse(EnemyContext ctx, ClimberTuning t)
    {
        var train = ctx.Train;
        int car = Attached;
        var shape = train.Frames[car].Shape;
        double z = Local.Z - t.TraverseSpeed * SimConstants.TickSeconds;
        // Over the middle of a car with a room in it and nobody inside (or every lamp out): in it goes.
        if (Local.Z > 0 && z <= 0 && shape.Interior is { } room && (ctx.World.EmergencyLights || !Occupied(ctx, car)))
        {
            Local = room.Centre with { Y = room.Min.Y };
            Enter(ctx, SpinePhase.Punish);
            return;
        }
        if (z > -shape.HalfLength + 0.3)
        {
            Local = Local with { Z = z };
            return;
        }
        // Over the gap onto the next roof ahead.
        int ahead = train.VehicleAhead(car);
        if (ahead < 0)
        {
            Enter(ctx, SpinePhase.BreakOff);
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        if (ahead == 0)
        {
            // The engine: down into the cab.
            Attached = 0;
            Local = train.Frames[0].Shape.Cab!.Value.Centre;
            Enter(ctx, SpinePhase.Punish);
            return;
        }
        var next = train.Frames[ahead].Shape;
        Attached = ahead;
        Local = new Double3(0, next.RoofHeight, next.HalfLength - 0.3);
    }

    /// <summary>In a car (or the cab): it goes for whoever's in there with it. Left alone long enough, it leaves.</summary>
    void Nest(EnemyContext ctx, ClimberTuning t)
    {
        var train = ctx.Train;
        var at = WorldPosition(train);
        var near = ctx.LivingCrew().Where(c => (c.World - at).Length <= t.Reach).OrderBy(c => (c.World - at).Length).Select(c => (int?)c.Player.Id).FirstOrDefault();
        bool company = Attached == 0 ? ctx.Crew.Any(c => c.Player.State.Alive && PlayerMotor.InCab(c.Player.State, train)) : Occupied(ctx, Attached);
        if (!company && near is null)
        {
            _bored += SimConstants.TickSeconds;
            if (_bored >= t.BoredSeconds)
                Enter(ctx, SpinePhase.Gone);
            return;
        }
        _bored = 0;
        _bite += SimConstants.TickSeconds;
        if (near is { } victim && _bite >= t.BiteEvery)
        {
            _bite = 0;
            ctx.Bite(victim, t.BiteDamage, DeathCause.Climbed);
        }
    }
}
