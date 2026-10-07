using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// DRAGGERS · movement · flank (GDD v1.1 §21, App. A.4). Cling under a car's edge, out of sight, dormant until someone's on
/// the roofs. Walk within a metre of the edge near one (half as far again at full speed) and a limb comes up over the lip a
/// second before it grabs (the tell). Grabbed, you're pulled over the side and hang there for a few seconds: someone has
/// to haul you back up. Nobody does, and you're dragged under. It never leaves its car. Rule: stay off the edges.
/// </summary>
/// <remarks>
/// <see cref="Enemy.Local"/> is where it clings (its X the edge it's under), <see cref="Enemy.Extra"/> who it's reaching
/// for (−1 for nobody); both replicate, so a client draws the limb.
/// </remarks>
public sealed class Dragger(int id) : Enemy(id)
{
    bool _rearming;

    public override EnemyKind Kind => EnemyKind.Dragger;
    public override PressureZone Zone => PressureZone.Flank;
    public override Sense Sense => Sense.Movement;
    public override Want Want => Want.Kill;
    /// <summary>Hauling a crewmate back up frees them (App. A.4 "a friend hauls them back up").</summary>
    public override bool PullsFree => true;
    /// <summary>A limb over the lip can be struck.</summary>
    public override double MeleeRadius => Phase is SpinePhase.Telegraph or SpinePhase.Grab ? 0.6 : 0;

    public int Side => Local.X >= 0 ? 1 : -1;
    public int? Target => Extra >= 0 ? (int)Extra : null;

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
    /// A player on its car's roof, on its side, within grab range of the edge (farther at speed; nearer with the roof
    /// handrails, spec F.3 "Dragger resistance", note 184), and near it along the car.
    /// </summary>
    public bool Reaches(in PlayerState s, TrainOnLine train, DraggerTuning t) =>
        s.Alive && s.Surface == Surface.Roof && s.Parent == Attached && Math.Sign(s.Position.X + 1e-9) == Side
        && train.Frames[Attached].Shape.HalfWidth - Math.Abs(s.Position.X) <= t.GrabAt(train.Dynamics.Speed) * Rails(train, r => r.DraggerGrab)
        && Math.Abs(s.Position.Z - Local.Z) <= t.ReachAlong;

    /// <summary>What the roof handrails do to it, if the train has them (train.json <c>composition</c>): 1 without.</summary>
    static double Rails(TrainOnLine train, Func<HandrailTuning, double> of) =>
        train.Dynamics.Tuning.Composition is { Handrails: true } c ? of(c.Rails) : 1;

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
        switch (Phase)
        {
            case SpinePhase.Dormant:
                {
                    Extra = -1;
                    if (_rearming && PhaseSeconds < t.RearmSeconds)
                        return;
                    _rearming = false;
                    var walkers = ctx.Crew.Where(c => c.Player.State is { Alive: true, Surface: Surface.Roof } s && s.Parent == Attached
                        && Math.Sign(s.Position.X + 1e-9) == Side).ToList();
                    if (walkers.Count == 0)
                    {
                        if (PhaseSeconds >= t.LingerSeconds)
                            Enter(ctx, SpinePhase.Gone);
                        return;
                    }
                    var near = walkers.MinBy(c => Math.Abs(c.Player.State.Position.Z - Local.Z));
                    Creep(near.Player.State.Position.Z, t, shape);
                    if (Reaches(near.Player.State, train, t))
                    {
                        Extra = near.Player.Id;
                        Enter(ctx, SpinePhase.Telegraph); // a limb over the lip, and the scrape
                    }
                    return;
                }
            case SpinePhase.Telegraph:
                {
                    int reaching = Target ?? -1;
                    var mark = ctx.Crew.FirstOrDefault(c => c.Player.Id == reaching).Player;
                    if (mark.State.Death != DeathCause.None || mark.Id != reaching || !Reaches(mark.State, train, t))
                    {
                        Rearm(ctx);
                        return;
                    }
                    Creep(mark.State.Position.Z, t, shape);
                    // A hand on the handrail holds on longer: friends have longer to haul them back (note 184).
                    if (PhaseSeconds >= t.TelegraphSeconds && Enter(ctx, SpinePhase.Commit))
                        Grab(ctx, reaching, t.HangSeconds * Rails(train, r => r.DraggerHang));
                    return;
                }
            case SpinePhase.Grab:
                return;
            default:
                Rearm(ctx);
                return;
        }
    }

    void Creep(double z, DraggerTuning t, CarShape shape)
    {
        double step = t.CreepSpeed * SimConstants.TickSeconds;
        double to = Math.Clamp(z, -shape.HalfLength + 0.5, shape.HalfLength - 0.5);
        Local = Local with { Z = Local.Z + Math.Clamp(to - Local.Z, -step, step) };
    }

    /// <summary>Dragged under: over the side at speed, and gone.</summary>
    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.Dragged);
        Rearm(ctx);
    }

    protected override void Rescued(EnemyContext ctx, int by)
    {
        base.Rescued(ctx, by);
        Rearm(ctx);
    }

    void Rearm(EnemyContext ctx)
    {
        Extra = -1;
        _rearming = true;
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Dormant);
    }
}

/// <summary>
/// WHISTLER · absence (the train stopping) · flank (GDD v1.1 §21, App. A.4). It boards at a stop into a coupling gap and
/// hides there while the train moves. At the next stop it blows the train's own whistle (the telegraph: the whistle with no
/// hand on the cord, and it feeds the loudness meter), then watches its gap. A player who passes the gap alone is snatched
/// and carried off at a run to its nest; there it paralyses them, and after ~20 s eats them. The crew chases on foot and
/// bludgeons it. Never grabs from a moving train. Rule: check the gaps after the whistle; move in pairs at stops. Found in
/// its gap by two, it flees.
/// </summary>
/// <remarks>
/// Hidden, <see cref="Enemy.Attached"/> is the vehicle ahead of its gap and <see cref="Enemy.Local"/> the gap. Carrying, it's
/// loose in the world (<see cref="Enemy.Loose"/>). <see cref="Enemy.Extra"/> is 1 while it's whistling.
/// </remarks>
public sealed class Whistler(int id) : Enemy(id)
{
    double _stopped;
    bool _whistled;
    Double3 _nest;

    public override EnemyKind Kind => EnemyKind.Whistler;
    public override PressureZone Zone => PressureZone.Flank;
    public override Sense Sense => Sense.Absence;
    public override Want Want => Want.Kill;
    /// <summary>In reach of a tool once it's been found (waiting in its gap after the whistle), or carrying someone off.</summary>
    public override double MeleeRadius => Phase is SpinePhase.Commit or SpinePhase.Grab ? 0.7 : 0;
    /// <summary>The whistle's blowing (the tell): replicated, so every client hears the whistle with no hand on the cord.</summary>
    public bool Whistling => Extra > 0.5;
    public int Gap => Attached;

    /// <summary>In the coupling gap behind a vehicle.</summary>
    public static Whistler InGap(int id, TrainOnLine train, int car, WhistlerTuning t) => new(id)
    {
        Attached = car,
        Local = CrewSense.GapLocal(train, car),
        Health = t.Health,
    };

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Whistler;
        var train = ctx.Train;
        bool stopped = train.Dynamics.Speed < t.StoppedBelow;
        switch (Phase)
        {
            case SpinePhase.Dormant:
                // HIDE while the train moves; the next stop, a few seconds in, it whistles.
                if (Attached < 0 || Attached >= train.Frames.Count || train.Dynamics.Consist.IndexOf(Attached) < 0 || train.VehicleBehind(Attached) < 0)
                {
                    Enter(ctx, SpinePhase.Gone);
                    return;
                }
                _stopped = stopped ? _stopped + SimConstants.TickSeconds : 0;
                if (!stopped)
                    _whistled = false;
                if (!_whistled && _stopped >= t.WhistleAfterStop)
                {
                    _whistled = true;
                    Extra = 1;
                    ctx.World.Whistled(t.WhistleSeconds);
                    Enter(ctx, SpinePhase.Telegraph);
                }
                return;
            case SpinePhase.Telegraph:
                if (PhaseSeconds >= t.WhistleSeconds)
                    Extra = 0;
                if (PhaseSeconds >= Math.Max(t.WhistleSeconds, ctx.Tuning.MinReactionSeconds))
                    Enter(ctx, SpinePhase.Commit); // WAIT: watching its gap
                return;
            case SpinePhase.Commit:
                {
                    Extra = 0;
                    // Moving again: back into hiding for the next stop.
                    if (!stopped)
                    {
                        Enter(ctx, SpinePhase.BreakOff);
                        Enter(ctx, SpinePhase.Dormant);
                        return;
                    }
                    var gap = WorldPosition(train);
                    // Found by two: it flees.
                    var lookers = ctx.Crew.Where(c => c.Player.State.Alive && (PlayerMotor.WorldPosition(c.Player.State, train) - gap).Length <= t.SpotReach
                        && CrewSense.Facing(c.Player.State, train, gap, 60)).ToList();
                    if (lookers.Count >= 2)
                    {
                        Enter(ctx, SpinePhase.BreakOff);
                        Enter(ctx, SpinePhase.Gone);
                        return;
                    }
                    // GRAB: someone passing the gap alone.
                    foreach (var (p, w) in ctx.LivingCrew())
                    {
                        if (p.State.Has(PlayerFlags.Held) || (w - gap).Length > t.PassReach && !CrewSense.InGap(w, train, Attached))
                            continue;
                        if (ctx.LivingCrew().Any(o => o.Player.Id != p.Id && (o.World - w).Length <= t.PairRadius))
                            continue;
                        int side = train.Frames[Attached].ToLocal(w).X >= 0 ? 1 : -1;
                        var (nest, run) = NestSite(train, gap, train.Frames[Attached].Right, side, t);
                        _nest = nest;
                        var victim = p.Id;
                        Local = w;
                        LineDistance = train.Dynamics.Distance;
                        Attached = Loose;
                        // How long the run to it takes (replicated: the nest's drawn under it once it's there).
                        Extra2 = run / t.RunSpeed;
                        Grab(ctx, victim, run / t.RunSpeed + t.NestSeconds);
                        return;
                    }
                    return;
                }
            case SpinePhase.Grab:
                {
                    // Carried off at a run to the nest; there, paralysed.
                    // Over the land, not through it (T128): it runs on the ground, whatever the ground does.
                    var to = (_nest - Local) with { Y = 0 };
                    double step = t.RunSpeed * SimConstants.TickSeconds;
                    var next = to.Length > step ? Local + to.Normalized * step : _nest;
                    double hint = LineDistance;
                    Local = next with { Y = PlayerMotor.GroundAt(next, train.Line, ref hint) };
                    LineDistance = hint;
                    ctx.Carry(Holding, Local);
                    return;
                }
            default:
                Enter(ctx, SpinePhase.Gone);
                return;
        }
    }

    /// <summary>
    /// Where it carries someone (T128, build 1121: "a creature carried the director up a mountainside"; note 273): straight
    /// out from its gap across the line, the side they were taken from first, as far as <see cref="WhistlerTuning.NestDistance"/>
    /// (inside Line Plan §12.6's walkable corridor, so a friend can follow and a body can be found), shorter if it must,
    /// never under <see cref="WhistlerTuning.NestMinDistance"/>. The way there has to be land a person can run over: no
    /// stretch of it steeper than <see cref="WhistlerTuning.NestMaxSlope"/>, never more than <see cref="WhistlerTuning.NestMaxRise"/>
    /// above or below the rail (not up a mountainside, not down a ravine, not into a cutting's wall or a tunnel's hill), and
    /// no water. Nowhere on either side is: the formation's edge on the side they were on, the nearest walkable point there
    /// is. Returns the nest (on the ground) and how far it runs to it.
    /// </summary>
    public static (Double3 Nest, double Run) NestSite(TrainOnLine train, Double3 gap, Double3 right, int side, WhistlerTuning t)
    {
        var across = (right with { Y = 0 }).Normalized;
        double hint = train.Dynamics.Distance;
        double rail = PlayerMotor.GroundAt(gap, train.Line, ref hint);
        var water = (train.Line.Conditions as LineGen.PlanConditions)?.Terrain;
        foreach (int s in new[] { side, -side })
            for (double d = t.NestDistance; d >= t.NestMinDistance - 1e-6; d -= Math.Max(1, t.NestStep))
                if (Runnable(train, gap, across * s, d, rail, water, t, ref hint) is { } nest)
                    return (nest, ((nest - gap) with { Y = 0 }).Length);
        var edge = gap + across * (side * train.Dynamics.Tuning.Recovery.EdgeM);
        return (edge with { Y = PlayerMotor.GroundAt(edge, train.Line, ref hint) }, train.Dynamics.Tuning.Recovery.EdgeM);
    }

    static Double3? Runnable(TrainOnLine train, Double3 gap, Double3 dir, double distance, double rail, LineGen.TerrainField? water, WhistlerTuning t, ref double hint)
    {
        const double Pace = 2;
        double last = rail;
        for (double x = Pace; x <= distance + 1e-6; x += Pace)
        {
            var at = gap + dir * Math.Min(x, distance);
            double ground = PlayerMotor.GroundAt(at, train.Line, ref hint);
            if (Math.Abs(ground - rail) > t.NestMaxRise || Math.Abs(ground - last) > t.NestMaxSlope * Pace
                || water?.WaterNear(at.X, at.Z, 1) is not null)
                return null;
            last = ground;
        }
        var nest = gap + dir * distance;
        return nest with { Y = last };
    }

    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.Carried);
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Gone);
    }

    /// <summary>Clubbed while carrying: it drops them and runs.</summary>
    protected override void Rescued(EnemyContext ctx, int by)
    {
        base.Rescued(ctx, by);
        Enter(ctx, SpinePhase.Gone);
    }
}

/// <summary>
/// CLIMBERS · movement · flank (GDD v1.1 §21, App. A.4). A pack runs alongside at track level, matching the train, and
/// counts the players within 8 m of each coupling gap: it mounts any gap held by fewer players than there are Climbers
/// (a brief scrabbling there first, visible from the adjacent roofs). Up between the cars and along the roofs toward the
/// engine, then into the first car that's unlit or has nobody in it, where it's an interior threat: it takes a lone player
/// in there (a grab a friend can club it off). Rule: outnumber them at the gaps. Bludgeon any that mount. "Scales brutally
/// with train length": more gaps to hold with the same crew.
/// <para>
/// Driven off by its rule (note 288, enemies.json <c>climbers.drivenOff</c>; the director's clarification of 7 Oct 2026):
/// up on the roofs or in a car, outnumbered where it is (more of the crew than Climbers within <c>countRadius</c> of it)
/// with no gang striking it, it drops back off the train after <c>outnumberedSeconds</c> (letting go of anyone it holds)
/// and makes for another gap it hasn't tried: a break, not the night. A lone blow knocks it off a friend it holds and does
/// no more. Killed only by the crew together: blows from two or more crewmates inside <c>coordinatedKill</c>'s window wear it
/// down; killed, it's gone for good (the rest of its pack, and the director, aren't put off).
/// </para>
/// </summary>
/// <remarks>
/// <see cref="Enemy.Extra"/> is the vehicle ahead of the gap it's making for (pacing and mounting), <see cref="Enemy.Extra2"/>
/// its side (−1 or +1). Pacing it's free on the line; from the gap on it's on a vehicle.
/// </remarks>
public sealed class Climber(int id) : Enemy(id)
{
    double _bored, _bite, _outnumbered;
    int _tries;
    readonly List<int> _tried = [];

    public override EnemyKind Kind => EnemyKind.Climber;
    public override PressureZone Zone => PressureZone.Flank;
    public override Sense Sense => Sense.Movement;
    public override Want Want => Want.Split;
    /// <summary>On the roofs or in a car, a tool can reach it.</summary>
    public override double MeleeRadius => Attached >= 0 ? 0.6 : 0;
    /// <summary>Out on the roofs (or scrabbling at the gap), it's in the open: the guns can take it.</summary>
    public override bool Exposed => Phase is SpinePhase.Telegraph or SpinePhase.Commit && Extra >= 0;
    public override bool GunAnswers => true;

    public int Gap => (int)Extra;
    public int Side => Extra2 < 0 ? -1 : 1;
    /// <summary>Scrabbling at the gap: the telegraph.</summary>
    public bool Scrabbling => Phase == SpinePhase.Telegraph;
    /// <summary>In a car (or the cab): the interior threat.</summary>
    public bool Inside => Extra < 0 && Attached >= 0 && !Gone;

    /// <summary>Alongside the gap behind <paramref name="car"/>, out from the train on one side.</summary>
    public static Climber Pacing(int id, TrainOnLine train, int car, int side, ClimberTuning t)
    {
        var c = new Climber(id) { Extra = car, Extra2 = side, Health = t.Health, Pack = id };
        c.LineDistance = GapAlong(train, car);
        c.Lateral = side * (train.Frames[car].Shape.HalfWidth + t.PaceOut);
        return c;
    }

    /// <summary>Along-line distance of the gap behind a vehicle.</summary>
    public static double GapAlong(TrainOnLine train, int car) =>
        train.Cars[car].FrontDistance - train.Cars[car].Length - train.Dynamics.Tuning.Geometry.CouplingGap * 0.5;

    /// <summary>
    /// The gaps it can mount at, as the vehicle ahead of each: every coupling in the engine's rake behind a car (not the
    /// engine's own: that's the cab, and the fireman's). App. B.4: "weight scales directly with gap count".
    /// </summary>
    public static List<int> Gaps(TrainOnLine train) => CrewSense.Gaps(train);

    /// <summary>The pack it runs with (the first one's id): they count themselves against the gap together.</summary>
    public int Pack { get; init; }

    /// <summary>
    /// COUNT (App. A.4): the gap behind that vehicle is held against this many Climbers when at least as many players are
    /// within 8 m of it (App. C.6).
    /// </summary>
    public static bool Held(EnemyContext ctx, int car, int climbers, ClimberTuning t)
    {
        var gap = ctx.Train.Frames[car].ToWorld(CrewSense.GapLocal(ctx.Train, car));
        return CrewSense.Near(ctx, gap, t.CountRadius) >= climbers;
    }

    /// <summary>Its pack still out alongside or at the gap: the count it brings to a gap.</summary>
    int Brought(EnemyContext ctx) => Math.Max(1, ctx.World.ActiveEnemies.Count(e => e is Climber c && c.Pack == Pack && !c.Gone && c.Attached < 0 || e == this));

    static bool Occupied(EnemyContext ctx, int car) => CrewSense.Occupied(ctx, car);

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Climbers;
        var train = ctx.Train;
        double dt = SimConstants.TickSeconds;
        // The car it's on or making for has gone (cut off and left behind): so has it.
        if (Gap >= train.Frames.Count || Attached >= train.Frames.Count || Attached >= 0 && Attached != 0 && train.Dynamics.Consist.IndexOf(Attached) < 0)
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        if (t.DrivenOff && Attached >= 0 && (Phase is SpinePhase.Grab || Phase is SpinePhase.Commit || Phase is SpinePhase.Telegraph && Extra < 0)
            && Outnumbered(ctx, t))
            return;
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
                        if (Held(ctx, Gap, Brought(ctx), t))
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
            case SpinePhase.Telegraph when Extra < 0:
                // In its car, snarling after it was clubbed off someone: at them again once the window's passed.
                if (PhaseSeconds >= ctx.Tuning.MinReactionSeconds)
                    Enter(ctx, SpinePhase.Commit);
                break;
            case SpinePhase.Telegraph:
                // Enough of the crew get to the gap as it scrabbles: outnumbered, it drops back and tries another.
                if (Held(ctx, Gap, Brought(ctx), t))
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
            case SpinePhase.Commit when Extra < 0:
                Nest(ctx, t);
                break;
            case SpinePhase.Commit:
                Traverse(ctx, t);
                break;
            case SpinePhase.Grab:
                break;
            default:
                Enter(ctx, SpinePhase.Gone);
                break;
        }
    }

    /// <summary>
    /// OUTNUMBER THEM (note 288): more of the crew than Climbers within <see cref="ClimberTuning.CountRadius"/> of it, and no
    /// gang striking it: held for <see cref="ClimberTuning.OutnumberedSeconds"/>, it lets go and drops back off the train for
    /// another gap (<see cref="Retry"/>). True the tick it does.
    /// </summary>
    bool Outnumbered(EnemyContext ctx, ClimberTuning t)
    {
        var at = WorldPosition(ctx.Train);
        int climbers = ctx.World.ActiveEnemies.Count(e => e is Climber c && !c.Gone && c.Attached >= 0 && (c.WorldPosition(ctx.Train) - at).Length <= t.CountRadius);
        bool beaten = CrewSense.Near(ctx, at, t.CountRadius) > Math.Max(1, climbers) && !Ganged(ctx);
        _outnumbered = beaten ? _outnumbered + SimConstants.TickSeconds : 0;
        if (_outnumbered < t.OutnumberedSeconds)
            return false;
        _outnumbered = 0;
        // In a car it has no gap of its own: it goes out over the one behind the car it's in.
        if (Extra < 0)
            Extra = Attached;
        Retry(ctx, t);
        return true;
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
        // Over the middle of a car with a room in it, unlit or with nobody inside (App. A.4 ENTER): in it goes. Shut up and
        // dark, it forces its way in through the roof (the hatch torn off): the car's breached (decided 1 Oct).
        if (Local.Z > 0 && z <= 0 && shape.Interior is { } room && (!train.Vehicles[car].LampLit || !Occupied(ctx, car)))
        {
            var v = train.Vehicles[car];
            if (v.DoorsOpen == 0 && (!v.LampLit || t.BreachLitCars) && Breaches.Roof(shape) is { } roof)
                v.Breach(roof);
            Local = room.Centre with { Y = room.Min.Y };
            Extra = -1; // inside: the interior threat
            // GDD §23 "lights fail" (note 183): in the dark is how it likes it; the lamp goes out as it comes in.
            train.Vehicles[car].LampLit = false;
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
            Extra = -1;
            // Coming over the tender into the cab, it smashes the forward lamp for a while (§23 "lights fail", note 183).
            ctx.World.SmashLamp(t.LampOutSeconds);
            return;
        }
        var next = train.Frames[ahead].Shape;
        Attached = ahead;
        Local = new Double3(0, next.RoofHeight, next.HalfLength - 0.3);
    }

    /// <summary>
    /// In a car (or the cab): the interior threat. It bites whoever's in there with it, and takes one who's alone with it
    /// (a grab: a friend clubs it off). Left alone long enough, it leaves.
    /// </summary>
    void Nest(EnemyContext ctx, ClimberTuning t)
    {
        var train = ctx.Train;
        var at = WorldPosition(train);
        var near = ctx.LivingCrew().Where(c => (c.World - at).Length <= t.Reach && !c.Player.State.Has(PlayerFlags.Held))
            .OrderBy(c => (c.World - at).Length).Select(c => (int?)c.Player.Id).FirstOrDefault();
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
            if (CrewSense.Alone(ctx, victim, t.CountRadius) && Grab(ctx, victim, t.TakeSeconds))
                return;
            ctx.Bite(victim, t.BiteDamage, DeathCause.Climbed);
        }
    }

    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.Climbed);
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Gone);
    }

    /// <summary>
    /// Struck with a tool. Old rule (<c>drivenOff</c> false): hurt by any blow. Note 288: only the gang's blows hurt it (two or
    /// more crewmates inside the window); a lone blow knocks it off a friend it holds and does no more.
    /// </summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        if (!ctx.Tuning.Climbers.DrivenOff)
        {
            base.Struck(ctx, by, damage);
            return;
        }
        Marked(ctx, by);
        if (Ganged(ctx, except: Holding))
        {
            Health -= damage;
            if (Health <= 0)
            {
                Slay(ctx, forTheNight: false);
                return;
            }
        }
        if (Phase == SpinePhase.Grab && by != Holding)
            Rescued(ctx, by);
    }

    /// <summary>Clubbed off: back to lurking in its car (the committed interior threat).</summary>

    protected override void Rescued(EnemyContext ctx, int by)
    {
        base.Rescued(ctx, by);
        Enter(ctx, SpinePhase.Telegraph);
    }
}
