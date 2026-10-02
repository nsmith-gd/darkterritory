using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

// GDD v1.3 §21's three, all met at stops (the yards and the village halts, where the crew is on foot): the Shy Thing,
// the Huddle and the Mimic.

/// <summary>
/// THE SHY THING · sight · outside (GDD v1.3 §21, App. A.6). It stands in the dark where someone on the ground has to look:
/// past the switch they're working, the door they're breaching, the loading. Watch it and it has you (the telegraph): you
/// can't move, only turn, and from then on only you can see it. Look away, and hold it, and it's gone; but the longer
/// you've been under, the longer it takes, and while you're under it walks in. A crewmate stepping into your line of sight
/// breaks it too, if you can tell them where it is. Reaching you, it unhinges its jaw like a snake (the GRAB, ten
/// seconds): now everyone can see it, and a friend's blow sends it off. Rule: look away while you still can.
/// </summary>
/// <remarks>
/// Loose in the world. <see cref="Enemy.Extra"/> is who it has (−1: nobody yet), <see cref="Enemy.Extra2"/> how long they've
/// been watching it (s), which sets the look-away it takes (<see cref="LookAwayNeeded"/>). From the moment it has someone
/// until it unhinges, the host sends it only to them and whoever watches through them (<see cref="SeenBy"/>).
/// </remarks>
public sealed class ShyThing(int id) : Enemy(id)
{
    readonly Dictionary<int, double> _watched = new();
    double _away;

    public override EnemyKind Kind => EnemyKind.ShyThing;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Sight;
    public override Want Want => Want.Split;
    /// <summary>Shown, unhinging: a friend's blow sends it off. Before that there's nothing anyone else can see to hit.</summary>
    public override double MeleeRadius => Phase == SpinePhase.Grab ? 0.6 : 0;
    public int? Victim => Extra >= 0 ? (int)Extra : null;
    /// <summary>Seconds its victim has spent watching it.</summary>
    public double Under => Extra2;
    /// <summary>It has someone, and hasn't shown itself: only they can see it.</summary>
    public bool Hiding => Phase is SpinePhase.Telegraph or SpinePhase.Commit;

    /// <summary>
    /// Whether a viewer's machine has it: waiting in the dark, anyone's; with someone under, only theirs (or theirs who
    /// watches through them); unhinged, everyone's.
    /// </summary>
    public bool SeenBy(int viewer) => !Hiding || Victim == viewer;

    /// <summary>How long a look away has to be held, after watching it this long.</summary>
    public static double LookAwayNeeded(ShyThingTuning t, double under) => Math.Min(t.LookAwayMax, t.LookAwaySeconds + t.LookAwayGrowth * under);

    public static ShyThing Waiting(int id, Double3 world, ShyThingTuning t) => new(id) { Attached = Loose, Local = world, Extra = -1, Health = t.Health };

    /// <summary>
    /// Out where a player on the ground is looking (B.6): along their view, at <paramref name="distance"/>, turned off it
    /// as little as it takes to stand clear of the train. Null if there's nowhere clear in front of them.
    /// </summary>
    public static Double3? Spot(TrainOnLine train, in PlayerState s, double distance)
    {
        var at = PlayerMotor.WorldPosition(s, train);
        double yaw = PlayerMotor.WorldYaw(s, train);
        foreach (double turn in (double[])[0, 30, -30, 60, -60])
        {
            double a = yaw + turn * Math.PI / 180;
            var p = at + new Double3(-DMath.Sin(a), 0, -DMath.Cos(a)) * distance;
            if (!ByTheTrain(train, p))
                return p;
        }
        return null;
    }

    static bool ByTheTrain(TrainOnLine train, Double3 p)
    {
        foreach (var f in train.Frames)
        {
            var local = f.ToLocal(p);
            if (Math.Abs(local.X) <= f.Shape.HalfWidth + 2.5 && Math.Abs(local.Z) <= f.Shape.HalfLength + 2.5)
                return true;
        }
        return false;
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.ShyThing;
        var train = ctx.Train;
        var here = WorldPosition(train);
        switch (Phase)
        {
            case SpinePhase.Dormant:
                {
                    // WAITS: whoever looks at it long enough, it has (the first of them, in the crew's order).
                    if (PhaseSeconds >= t.LingerSeconds)
                    {
                        Enter(ctx, SpinePhase.Gone);
                        return;
                    }
                    int? caught = null;
                    foreach (var (p, w) in ctx.LivingCrew())
                    {
                        bool watching = !p.State.Has(PlayerFlags.Held) && (w - here).Length <= t.WatchRange
                            && CrewSense.Facing(p.State, train, here, t.WatchDegrees) && !Blocked(ctx, p.Id, w, here, t);
                        double seconds = watching ? _watched.GetValueOrDefault(p.Id) + SimConstants.TickSeconds : 0;
                        _watched[p.Id] = seconds;
                        if (seconds >= t.WatchSeconds && caught is null)
                            caught = p.Id;
                    }
                    if (caught is { } v)
                    {
                        Extra = v;
                        Extra2 = 0;
                        _away = 0;
                        _watched.Clear();
                        Enter(ctx, SpinePhase.Telegraph); // they can't move
                        ctx.Hold(v);
                    }
                    return;
                }
            case SpinePhase.Telegraph or SpinePhase.Commit:
                {
                    if (Victim is not { } v || ctx.Crew.FirstOrDefault(c => c.Player.Id == v).Player.State is not { Alive: true } s)
                    {
                        Vanish(ctx);
                        return;
                    }
                    // Under: their feet are its, and they can still turn. Carried off out of its sight (aboard a train
                    // pulling out), it loses them.
                    var at = PlayerMotor.WorldPosition(s, train);
                    if ((at - here).Length > t.WatchRange * 1.5)
                    {
                        Vanish(ctx);
                        return;
                    }
                    ctx.Hold(v);
                    if (LookedAway(ctx, t, v, s, at, here))
                    {
                        Vanish(ctx);
                        return;
                    }
                    if (Phase == SpinePhase.Telegraph && PhaseSeconds >= ctx.Tuning.MinReactionSeconds)
                        Enter(ctx, SpinePhase.Commit);
                    if (Phase != SpinePhase.Commit)
                        return;
                    // DRAWS IN, and at arm's length, UNHINGES.
                    var to = (at - here) with { Y = 0 };
                    if (to.Length > t.Reach)
                        Local = here + to.Normalized * Math.Min(t.ApproachSpeed * SimConstants.TickSeconds, to.Length - t.Reach * 0.8);
                    else
                        Grab(ctx, v, t.UnhingeSeconds);
                    return;
                }
            case SpinePhase.Grab:
                {
                    // Even now, a look away held long enough saves them, if they can manage it.
                    if (Victim is { } v && ctx.Crew.FirstOrDefault(c => c.Player.Id == v).Player.State is { Alive: true } s
                        && LookedAway(ctx, t, v, s, PlayerMotor.WorldPosition(s, train), here))
                        Rescued(ctx, v);
                    return;
                }
            default:
                Enter(ctx, SpinePhase.Gone);
                return;
        }
    }

    /// <summary>
    /// The victim's looked away from it (past <see cref="ShyThingTuning.AwayDegrees"/>), or a crewmate's in the way, for as
    /// long as it takes. Looking at it, the time they've been under climbs, and so does what it'll take.
    /// </summary>
    bool LookedAway(EnemyContext ctx, ShyThingTuning t, int victim, in PlayerState s, Double3 at, Double3 here)
    {
        bool away = !CrewSense.Facing(s, ctx.Train, here, t.AwayDegrees) || Blocked(ctx, victim, at, here, t);
        _away = away ? _away + SimConstants.TickSeconds : 0;
        if (!away)
            Extra2 += SimConstants.TickSeconds;
        return _away >= LookAwayNeeded(t, Extra2);
    }

    /// <summary>A living crewmate standing in a player's line of sight to it: between them, within a body's width of the line.</summary>
    static bool Blocked(EnemyContext ctx, int player, Double3 eye, Double3 thing, ShyThingTuning t)
    {
        var line = (thing - eye) with { Y = 0 };
        double length = line.Length;
        if (length < 1)
            return false;
        var dir = line * (1 / length);
        foreach (var (p, w) in ctx.LivingCrew())
        {
            if (p.Id == player)
                continue;
            var to = (w - eye) with { Y = 0 };
            double along = Double3.Dot(to, dir);
            if (along <= 0.4 || along >= length - 0.3)
                continue;
            if ((to - dir * along).Length <= t.BlockWidth)
                return true;
        }
        return false;
    }

    /// <summary>Looked away from, or sent off: it's gone, for the run.</summary>
    void Vanish(EnemyContext ctx)
    {
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Gone);
    }

    protected override void Rescued(EnemyContext ctx, int by)
    {
        base.Rescued(ctx, by);
        Enter(ctx, SpinePhase.Gone);
    }

    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.ShyThing);
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Gone);
    }
}

/// <summary>
/// THE HUDDLE · heat · outside (GDD v1.3 §21, App. A.6). A flock of small, soft, harmless-looking things at a stop, that
/// follow the crew about and come aboard after the warmth, piling round the firebox. They chirp, all the time, and it counts
/// toward the crew's loudness (App. C.7) like livestock; petting them hushes them a while. Hit one and it dies with a
/// squeak, and the rest bristle (the telegraph, a hiss): get clear or hush them, or they swarm whoever struck and bury
/// them (the GRAB), nipping anyone near. Friends pet them off; another blow only makes it worse. To be rid of them, leave
/// them a fire: live coals flung out of the firebox at a stop draw them off the train, and the train leaves them behind.
/// Rule: pet them, never hit them.
/// </summary>
/// <remarks>
/// One flock, its <see cref="Enemy.Health"/> how many there are (a blow kills one). Loose in the world, or in the cab
/// (<see cref="Enemy.Attached"/> 0, round the firebox). <see cref="Enemy.Extra"/> is who they're after (−1 nobody),
/// <see cref="Enemy.Extra2"/> how hushed they are, 0..1.
/// </remarks>
public sealed class Huddle(int id) : Enemy(id)
{
    double _nip, _alone;

    public override EnemyKind Kind => EnemyKind.Huddle;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Heat;
    public override Want Want => Want.Trust;
    /// <summary>Someone always hits one: they're underfoot.</summary>
    public override double MeleeRadius => 0.6;
    /// <summary>Petted off the one they've buried (Use at them).</summary>
    public override bool PullsFree => true;
    public int Count => (int)Math.Round(Health);
    public double Calm => Extra2;
    public int? Target => Extra >= 0 ? (int)Extra : null;
    /// <summary>Riding in the cab, round the firebox.</summary>
    public bool Aboard => Attached == 0;

    public static Huddle Flock(int id, Double3 world, int count) => new(id) { Attached = Loose, Local = world, Health = count, Extra = -1 };

    /// <summary>What it adds to the crew's loudness (App. C.7): every one of them not hushed, chirping.</summary>
    public double Noise(HuddleTuning t) => Phase == SpinePhase.Dormant ? t.ChirpLoudness * Count * (1 - Calm) : 0;

    /// <summary>Where they pile up in the cab: on the floor just back from the firebox, in the warm.</summary>
    public static Double3 CabSpot(TrainOnLine train)
    {
        var shape = train.Frames[0].Shape;
        var firebox = shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position;
        double floor = shape.Cab is { } cab ? cab.Min.Y : firebox.Y - 0.8;
        return new Double3(firebox.X, floor, firebox.Z + 0.9);
    }

    /// <summary>
    /// A crewmate petting them: Use held within reach, looking down at them; a headset's hand reached down to them does it
    /// too (the same reach-down a coupling takes).
    /// </summary>
    public static bool Petting(in PlayerState s, in PlayerIntent intent, TrainOnLine train, Double3 flock, HuddleTuning t)
    {
        if (!s.Alive || s.Has(PlayerFlags.Held) || !intent.Has(PlayerButtons.Use))
            return false;
        if (s.Hand != default && PlayerMotor.HandWorld(s, train) is { } hand)
            return s.Hand.Y < 0.7 && (hand - flock).Length <= t.PetReach;
        var at = PlayerMotor.WorldPosition(s, train);
        return ((flock - at) with { Y = 0 }).Length <= t.PetReach && s.Pitch <= -t.PetLookDown * Math.PI / 180;
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Huddle;
        var train = ctx.Train;
        var here = WorldPosition(train);
        // Hushed by petting; the hush wears off.
        Extra2 = Math.Max(0, Extra2 - SimConstants.TickSeconds / t.CalmSeconds);
        if (ctx.Crew.Any(c => Petting(c.Player.State, c.Intent, train, here, t)))
            Extra2 = Math.Min(1, Extra2 + (t.PetPerSecond + 1 / t.CalmSeconds) * SimConstants.TickSeconds);
        switch (Phase)
        {
            case SpinePhase.Dormant:
                Wander(ctx, t, here);
                return;
            case SpinePhase.Telegraph:
                {
                    // Bristling at whoever struck: clear of them, or hushed, and they settle.
                    if (Target is not { } who || ctx.LivingCrew().FirstOrDefault(c => c.Player.Id == who) is not { Player.State.Alive: true } target
                        || ((target.World - here) with { Y = 0 }).Length > t.ClearOf || Calm >= 0.5)
                    {
                        Settle(ctx);
                        return;
                    }
                    if (PhaseSeconds >= Math.Max(t.BristleSeconds, ctx.Tuning.MinReactionSeconds))
                        Enter(ctx, SpinePhase.Commit);
                    return;
                }
            case SpinePhase.Commit:
                {
                    // SWARM: on them, wherever they are.
                    if (Target is not { } who || ctx.Crew.FirstOrDefault(c => c.Player.Id == who).Player.State is not { Alive: true } s)
                    {
                        Settle(ctx);
                        return;
                    }
                    var at = PlayerMotor.WorldPosition(s, train);
                    var to = at - here;
                    if (to.Length > t.Reach)
                    {
                        Attached = Loose;
                        Local = here + to.Normalized * Math.Min(t.SwarmSpeed * SimConstants.TickSeconds, to.Length);
                        return;
                    }
                    OnThem(s);
                    Grab(ctx, who, t.BuriedSeconds);
                    return;
                }
            case SpinePhase.Grab:
                {
                    // On the buried one, and nipping anyone else who comes near.
                    if (ctx.Crew.FirstOrDefault(c => c.Player.Id == Holding).Player.State is { Alive: true } held)
                        OnThem(held);
                    _nip += SimConstants.TickSeconds;
                    if (_nip >= t.NipEvery)
                    {
                        _nip = 0;
                        foreach (var (p, w) in ctx.LivingCrew())
                            if (p.Id != Holding && (w - here).Length <= t.NipRadius)
                                ctx.Bite(p.Id, t.NipDamage, DeathCause.Huddle);
                    }
                    return;
                }
            default:
                Settle(ctx);
                return;
        }
    }

    /// <summary>On a player: in their car's frame aboard, loose in the world off it.</summary>
    void OnThem(in PlayerState s)
    {
        Attached = s.Parent >= 0 ? s.Parent : Loose;
        Local = s.Position;
    }

    /// <summary>
    /// After the warmth (App. A.6 FOLLOW, BOARD): live coals on the ground first; aboard, the firebox; on the ground, the
    /// nearest crewmate on foot, or with nobody near, the cab. Left on the ground with the engine gone, they're left behind.
    /// </summary>
    void Wander(EnemyContext ctx, HuddleTuning t, Double3 here)
    {
        var train = ctx.Train;
        // LEFT BEHIND: off the train, the engine gone off, and nobody near (round a fire, or not).
        bool alone = !Aboard && (train.Frames[0].Origin - here).Length > t.LeftBehind
            && ctx.LivingCrew().All(c => (c.World - here).Length > t.NoticeRadius);
        _alone = alone ? _alone + SimConstants.TickSeconds : 0;
        if (_alone >= 5 || !Aboard && PhaseSeconds >= t.LingerSeconds)
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        bool slow = Math.Abs(train.Dynamics.Speed) < t.BoardBelow;
        var embers = ctx.World.Bodies.All.Where(b => b.Kind == BodyKind.Embers && b.Carrier < 0 && b.Charge > 0)
            .Select(b => Bodies.WorldCentre(b, train)).Where(w => (w - here).Length <= t.EmberLure)
            .OrderBy(w => (w - here).Length).Select(w => (Double3?)w).FirstOrDefault();
        if (embers is { } fire && (!Aboard || slow))
        {
            Go(here, fire, t.FollowAt * 0.5, t.FollowSpeed);
            return;
        }
        if (Aboard)
        {
            Local = CabSpot(train);
            return;
        }
        var cab = train.Frames[0].ToWorld(CabSpot(train));
        var near = ctx.LivingCrew().Where(c => CrewSense.OnGround(c.Player.State) && (c.World - here).Length <= t.NoticeRadius)
            .OrderBy(c => (c.World - here).Length).Select(c => (Double3?)c.World).FirstOrDefault();
        if (near is { } crewmate)
            Go(here, crewmate, t.FollowAt, t.FollowSpeed);
        else if ((cab - here).Length <= t.CabLure && slow && ctx.LivingCrew().Any())
        {
            if ((cab - here).Length <= t.BoardReach)
            {
                Attached = 0;
                Local = CabSpot(train);
                return;
            }
            Go(here, cab, 0, t.FollowSpeed);
        }
    }

    void Go(Double3 here, Double3 to, double stopAt, double speed)
    {
        Attached = Loose;
        var d = (to - here) with { Y = 0 };
        if (d.Length <= stopAt)
            return;
        double move = Math.Min(speed * SimConstants.TickSeconds, d.Length - stopAt);
        // Over the ground, and up or down with it as they go (onto the cab's floor, down off it to the coals).
        var step = d.Normalized * move;
        Local = new Double3(here.X + step.X, here.Y + (to.Y - here.Y) * move / d.Length, here.Z + step.Z);
    }

    void Settle(EnemyContext ctx)
    {
        Extra = -1;
        if (Phase is not (SpinePhase.Dormant or SpinePhase.BreakOff))
            Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Dormant);
    }

    /// <summary>
    /// A blow kills one of them (with a squeak), and the rest are after whoever did it: bristling, or straight back at them
    /// if they're swarming; on the one they've buried, sooner.
    /// </summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        Health -= 1;
        if (Health < 0.5)
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        Extra2 = 0;
        switch (Phase)
        {
            case SpinePhase.Grab:
                Shorten(ctx.Tuning.Huddle.HitShortens);
                return;
            case SpinePhase.Commit:
                Extra = by;
                return;
            default:
                Extra = by;
                Enter(ctx, SpinePhase.Telegraph);
                return;
        }
    }

    protected override void Rescued(EnemyContext ctx, int by)
    {
        base.Rescued(ctx, by);
        Extra2 = 1;
        Settle(ctx);
    }

    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.Huddle);
        Settle(ctx);
    }
}

/// <summary>
/// THE MIMIC · movement · outside (GDD v1.3 §21, App. A.6). A crate among a yard's crates, and the best-looking one. Its tells
/// are all subtle: it's one more than the count chalked by the stack; it breathes when someone stands still beside it;
/// carried, it's heavier than a crate should be; and put in a car, it never goes into the load. Harmless in your arms and on
/// the ground, it wakes a while after it's set down aboard: then anyone who comes within arm's length (or picks it up) sees
/// the lid lift a crack (the telegraph), and still there, it has them (the GRAB). Friends pull them out or hit it; a group
/// kills it, one alone with difficulty: struck, it lunges at whoever struck it. Throw it off the train, or cut its car.
/// Rule: not on the count? Leave it.
/// </summary>
/// <remarks>
/// It's a body as much as an enemy: a cargo crate (<see cref="Enemy.Extra"/> is its id) that goes where bodies go, carried,
/// thrown, stowed; the enemy follows it. <see cref="Enemy.Extra2"/> is its breathing (0 none, 0.5 faint, 1 awake), drawn
/// and heard on every machine.
/// </remarks>
public sealed class Mimic(int id) : Enemy(id)
{
    double _settled, _gone;
    bool _awake, _lunge;
    int _target = -1;

    public override EnemyKind Kind => EnemyKind.Mimic;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Movement;
    public override Want Want => Want.Trust;
    public override double MeleeRadius => 0.5;
    /// <summary>Pulled out of its mouth (Use at the one it has).</summary>
    public override bool PullsFree => true;
    public int BodyId => (int)Math.Round(Extra);
    public bool Breathing => Extra2 > 0;
    /// <summary>The lid lifting, or shut on someone.</summary>
    public bool Open => Phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish;

    /// <summary>The Mimic that's this crate: where it lies, in its car's frame or the world's.</summary>
    public static Mimic As(int id, Body body, MimicTuning t) =>
        new(id) { Attached = body.Parent >= 0 ? body.Parent : Loose, Local = body.Centre, Extra = body.Id, Health = t.Health };

    /// <summary>Whether a body is a Mimic: the loading leaves it be (it never goes into the load), and so does a crew that reads the count.</summary>
    public static bool Is(World world, Body b) => world.ActiveEnemies.Any(e => e is Mimic m && !m.Gone && m.BodyId == b.Id);

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Mimic;
        var train = ctx.Train;
        var body = ctx.World.Bodies.All.FirstOrDefault(b => b.Id == BodyId);
        if (body is null)
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        // Where its crate is, so its tells (and its lid) are drawn and heard there.
        bool onTrain = body.Parent >= 0 && body.Parent < train.Frames.Count;
        Attached = onTrain ? body.Parent : Loose;
        Local = body.Centre;
        var here = Bodies.WorldCentre(body, train);
        bool aboard = body.Carrier < 0 && onTrain && train.Dynamics.Consist.IndexOf(body.Parent) >= 0;

        // Thrown off, or its car cut: once the engine's far enough off, it's gone into the dark.
        bool away = !aboard && body.Carrier < 0 && (train.Frames[0].Origin - here).Length > t.LeftBehind;
        _gone = away || Phase == SpinePhase.Dormant && !aboard && PhaseSeconds >= t.LingerSeconds ? _gone + SimConstants.TickSeconds : 0;
        if (_gone >= 5)
        {
            ctx.World.Bodies.Remove(body);
            Enter(ctx, SpinePhase.Gone);
            return;
        }

        // STOWED: set down aboard and left, it wakes.
        _settled = aboard ? _settled + SimConstants.TickSeconds : 0;
        if (_settled >= t.WakeSeconds)
            _awake = true;
        // Its breathing, for anyone stood still beside it.
        bool still = ctx.LivingCrew().Any(c => (c.World - here).Length <= t.BreatheNear && ctx.World.IdleSeconds.GetValueOrDefault(c.Player.Id) >= t.BreatheStill);
        Extra2 = still ? (_awake ? 1 : 0.5) : 0;

        switch (Phase)
        {
            case SpinePhase.Dormant:
                {
                    if (!_awake || !aboard && body.Carrier < 0)
                        return;
                    // REACH: whoever's carrying it, or the nearest within arm's length.
                    _target = body.Carrier >= 0 ? body.Carrier
                        : ctx.LivingCrew().Where(c => !c.Player.State.Has(PlayerFlags.Held) && (c.World - here).Length <= t.Reach)
                            .OrderBy(c => (c.World - here).Length).Select(c => (int)c.Player.Id).DefaultIfEmpty(-1).First();
                    if (_target >= 0)
                        Enter(ctx, SpinePhase.Telegraph); // the lid lifts a crack
                    return;
                }
            case SpinePhase.Telegraph:
                {
                    var target = ctx.LivingCrew().FirstOrDefault(c => c.Player.Id == _target);
                    bool held = body.Carrier == _target && _target >= 0;
                    bool there = ctx.LivingCrew().Any(c => c.Player.Id == _target)
                        && (held || (target.World - here).Length <= (_lunge ? t.LungeReach : t.Reach));
                    // Stepped back, or thrown: it shuts again.
                    if (!there || body.Carrier < 0 && !aboard && !_lunge)
                    {
                        Shut(ctx);
                        return;
                    }
                    if (PhaseSeconds >= Math.Max(t.LidSeconds, ctx.Tuning.MinReactionSeconds) && Enter(ctx, SpinePhase.Commit))
                    {
                        // BITE: out of their arms, if they had it, and onto them.
                        body.Carrier = body.Second = -1;
                        Grab(ctx, _target, t.BiteSeconds);
                    }
                    return;
                }
            case SpinePhase.Grab:
                return;
            default:
                Shut(ctx);
                return;
        }
    }

    void Shut(EnemyContext ctx)
    {
        _lunge = false;
        _target = -1;
        if (Phase is not (SpinePhase.Dormant or SpinePhase.BreakOff))
            Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Dormant);
    }

    /// <summary>Struck, it's hurt; awake or not, it lunges at whoever struck it. Killed, its crate's gone with it.</summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        base.Struck(ctx, by, damage);
        if (Gone)
        {
            if (ctx.World.Bodies.All.FirstOrDefault(b => b.Id == BodyId) is { } body)
                ctx.World.Bodies.Remove(body);
            return;
        }
        _awake = true;
        if (Phase == SpinePhase.Dormant)
        {
            _target = by;
            _lunge = true;
            Enter(ctx, SpinePhase.Telegraph);
        }
    }

    protected override void Rescued(EnemyContext ctx, int by)
    {
        base.Rescued(ctx, by);
        Shut(ctx);
    }

    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.Mimic);
        Shut(ctx);
    }
}
