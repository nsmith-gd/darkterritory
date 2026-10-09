using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>What the Freight Beetle is doing (replicated in <see cref="Enemy.Height"/>), for its clips and cues.</summary>
public enum BeetleMode : byte { Idle, Walk, Brace, Push, Startle, Away }

/// <summary>
/// THE FREIGHT BEETLE · sight · outside (GDD §21, App. A.6, B.6; ARCHITECTURE §8 note 366; the director's brief of 8 Oct
/// 2026, docs/design/creatures/freight-beetle.md). A big, workmanlike beetle at a facility, built for pushing: it takes the
/// loose freight nearest it and shoves it DIRECTLY AWAY FROM THE NEAREST PLAYER. Alone, a crewmate can only drive the
/// freight off from themselves (off a platform, out into the dark); two who understand it can steer it, and then it's a
/// loading tool (the freight goes where it's pushed, into a car's door from a platform and it's loaded, as if carried).
/// It never attacks anyone. Startled by someone at its head, it rears back a moment; blows drive it off its load (three in
/// a short while) or kill it. Rule: it pushes away from whoever's nearest. Stand where you want it not to go.
/// </summary>
/// <remarks>
/// Loose in the world (<see cref="Enemy.Loose"/>). <see cref="Enemy.Extra"/> is the load's body id (−1 none),
/// <see cref="Enemy.Lateral"/> its heading (a yaw), <see cref="Enemy.Height"/> its <see cref="BeetleMode"/>. The push is the
/// load's own physics (Bodies): it sets the crate's pace each tick, so a crate pushed over an edge falls, and one that comes
/// to rest in a car is loaded by the car's own rule.
/// </remarks>
public sealed class FreightBeetle(int id) : Enemy(id)
{
    double _modeSeconds, _awayFor, _push;
    readonly List<uint> _blows = [];

    public override EnemyKind Kind => EnemyKind.FreightBeetle;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Sight;
    public override Want Want => Want.Cargo;
    public override double MeleeRadius => 1.4;
    /// <summary>It belongs to its stop: the director never sends it off for want of company.</summary>
    public override bool StaysAboard => true;

    public BeetleMode Mode => (BeetleMode)(int)Height;
    public int? Load => Extra >= 0 ? (int)Extra : null;
    public Double3 Facing => Way(Lateral);

    static Double3 Way(double yaw) => new(-DMath.Sin(yaw), 0, -DMath.Cos(yaw));

    /// <summary>Settled beside a facility's freight at <paramref name="world"/>, facing <paramref name="yaw"/>.</summary>
    public static FreightBeetle At(int id, Double3 world, double lineHint, double yaw, FreightBeetleTuning t) =>
        new(id) { Attached = Loose, Local = world, LineDistance = lineHint, Lateral = yaw, Extra = -1, Health = t.Health };

    void SetMode(BeetleMode mode)
    {
        if (Mode != mode)
            _modeSeconds = 0;
        Height = (double)mode;
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.FreightBeetle;
        var train = ctx.Train;
        double dt = SimConstants.TickSeconds;
        _modeSeconds += dt;
        if (Attached != Loose)
        {
            Local = WorldPosition(train);
            Attached = Loose;
        }
        var living = ctx.LivingCrew().Select(c => c.World).ToList();
        var me = Local;

        // Driven off its load: away a while, then back to the freight.
        if (Mode == BeetleMode.Away)
        {
            _awayFor -= dt;
            if (living.Count > 0)
                Walk(ctx, (me - living.MinBy(p => Flat(p - me))) with { Y = 0 }, t.Walk);
            if (_awayFor <= 0)
                SetMode(BeetleMode.Idle);
            return;
        }
        // Someone at its head: it rears back off the load a moment.
        if (Mode == BeetleMode.Startle)
        {
            if (_modeSeconds >= t.StartleSeconds)
                SetMode(BeetleMode.Idle);
            return;
        }
        if (living.Count == 0 || living.Min(p => Flat(p - me)) > t.Notice)
        {
            // Nobody about: it settles where it is (the load too, by its own friction).
            Settle(ctx, BeetleMode.Idle);
            return;
        }
        var head = me + Facing * t.HeadAt;
        if (living.Any(p => Flat(p - head) <= t.StartleWithin))
        {
            Settle(ctx, BeetleMode.Startle);
            return;
        }

        if (Loaded(ctx) is not { } load)
        {
            Extra = -1;
            if (Pick(ctx, t) is not { } next)
            {
                Settle(ctx, BeetleMode.Idle);
                return;
            }
            Extra = next.Id;
            load = next;
            _push = Lateral;
        }
        var at = load.Centre;
        // Directly away from whoever's nearest it: the way it wants the load to go, turned toward at its turn rate.
        var nearest = living.MinBy(p => Flat(p - at));
        var want = (at - nearest) with { Y = 0 };
        if (want.Length < 1e-6)
            want = Facing;
        _push = Turn(_push, Yaw(want), t.TurnDegrees * Math.PI / 180 * dt);
        var push = Way(_push);
        double radius = load.Pbd.Particles[0].Radius;
        var behind = at - push * (radius + t.HeadAt);
        var gap = (behind - me) with { Y = 0 };
        if (gap.Length > t.PushReach)
        {
            // Round to the far side of it from where it's to go, and set itself behind it.
            SetMode(BeetleMode.Walk);
            Walk(ctx, gap, t.Walk);
            Lateral = Yaw(gap);
            if (Phase is SpinePhase.Telegraph or SpinePhase.Commit)
                Enter(ctx, SpinePhase.Alert);
            return;
        }
        // Behind it: brace (the telegraph), then push.
        if (Phase is SpinePhase.Dormant or SpinePhase.Alert)
        {
            Lateral = _push;
            Enter(ctx, SpinePhase.Telegraph);
            SetMode(BeetleMode.Brace);
            return;
        }
        if (Phase == SpinePhase.Telegraph && !Enter(ctx, SpinePhase.Commit))
        {
            SetMode(BeetleMode.Brace);
            return;
        }
        SetMode(BeetleMode.Push);
        Lateral = _push;
        double speed = load.Kind == BodyKind.Heavy ? t.PushHeavy : t.Push;
        Shove(load, push * speed);
        // It walks on behind it, its head to the load.
        var step = behind + push * speed * dt;
        double hint = LineDistance;
        Local = step with { Y = PlayerMotor.GroundAt(step, train.Line, ref hint) };
        LineDistance = hint;
    }

    /// <summary>The load it has, if it's still loose freight on the ground (not carried, stowed, taken or put in a car).</summary>
    Body? Loaded(EnemyContext ctx) =>
        Load is { } id && ctx.World.Bodies.All.FirstOrDefault(b => b.Id == id) is { } b && Movable(b) ? b : null;

    static bool Movable(Body b) => b.Kind is BodyKind.Crate or BodyKind.Cargo or BodyKind.Heavy && b.Parent == PlayerState.World
        && b.Carrier < 0 && b.Second < 0 && b.TakenBy < 0 && !b.Stowed;

    /// <summary>The loose freight on the ground within <c>facilityReach</c> of the train (by id): what it could take up.</summary>
    public static List<Body> Freight(World world, FreightBeetleTuning t)
    {
        var train = world.Train;
        var front = train.Frames[0].Origin;
        return [.. world.Bodies.All.Where(Movable).Where(b => Flat(b.Centre - front) <= t.FacilityReach + train.Dynamics.Consist.LengthMetres).OrderBy(b => b.Id)];
    }

    /// <summary>The loose freight nearest it, within its reach for freight (by distance, then id: the same on every machine).</summary>
    Body? Pick(EnemyContext ctx, FreightBeetleTuning t)
    {
        var me = Local;
        return ctx.World.Bodies.All.Where(Movable).Where(b => Flat(b.Centre - me) <= t.FreightReach)
            .OrderBy(b => Flat(b.Centre - me)).ThenBy(b => b.Id).FirstOrDefault();
    }

    /// <summary>Stops pushing: the load let be (its own friction stops it), the beetle out of its push.</summary>
    void Settle(EnemyContext ctx, BeetleMode mode)
    {
        SetMode(mode);
        if (Phase is SpinePhase.Telegraph or SpinePhase.Commit)
            Enter(ctx, SpinePhase.Dormant);
    }

    /// <summary>Sets the crate's pace across the ground to <paramref name="pace"/> (its own physics does the rest: walls, edges, falls).</summary>
    static void Shove(Body load, Double3 pace)
    {
        ref var p = ref load.Pbd.Particles[0];
        var previous = p.Position - pace * SimConstants.TickSeconds;
        p.Previous = new Double3(previous.X, p.Previous.Y, previous.Z);
        load.Pbd.Wake();
    }

    /// <summary>
    /// Struck: hurt by every blow (health in blows); <c>driveOffBlows</c> inside <c>driveOffSeconds</c> drive it off its load
    /// for <c>awaySeconds</c>. It never strikes back.
    /// </summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        var t = ctx.Tuning.FreightBeetle;
        Marked(ctx, by);
        Health -= damage;
        if (Health <= 0)
        {
            Slay(ctx);
            return;
        }
        uint window = (uint)Math.Round(t.DriveOffSeconds * SimConstants.TickRate);
        _blows.RemoveAll(b => ctx.Tick - b > window);
        _blows.Add(ctx.Tick);
        if (_blows.Count >= t.DriveOffBlows)
        {
            _blows.Clear();
            Extra = -1;
            _awayFor = t.AwaySeconds;
            Settle(ctx, BeetleMode.Away);
        }
    }

    void Walk(EnemyContext ctx, Double3 way, double speed)
    {
        way = way with { Y = 0 };
        if (way.Length < 1e-6)
            return;
        double step = Math.Min(speed * SimConstants.TickSeconds, way.Length);
        var next = Local + way.Normalized * step;
        double hint = LineDistance;
        Local = next with { Y = PlayerMotor.GroundAt(next, ctx.Train.Line, ref hint) };
        LineDistance = hint;
    }

    static double Turn(double from, double to, double max)
    {
        double d = Math.IEEERemainder(to - from, 2 * Math.PI);
        return from + Math.Clamp(d, -max, max);
    }

    static double Flat(Double3 v) => (v with { Y = 0 }).Length;

    static double Yaw(Double3 way) => DMath.Atan2(-way.X, -way.Z);
}
