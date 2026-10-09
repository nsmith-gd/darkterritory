using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Enemies;

/// <summary>What the Lodger is doing (replicated in <see cref="Enemy.Height"/>), for its clips and cues.</summary>
public enum LodgerMode : byte { Hidden, Shriek, Lunge, Chase, Break, Return }

/// <summary>
/// THE LODGER · sound · outside (GDD §21; ARCHITECTURE §8 note 583; the director, 9 Oct 2026: "one of those terrifying hides
/// in the darkness ... a one-hit kill. It telegraphs with its shrill shriek to really scare the player. And then it maybe just
/// has like a slightly wider than normal pursuit radius before it'll go back to its little room"; docs/design/creatures/lodger.md).
/// Folded in the darkest corner of a village house, breathing. Someone in its house a moment, or close to it, and it SHRIEKS
/// (the telegraph), then lunges where they were: if it reaches them they're dead (a grab with almost no window: the one-hit
/// kill is the director's call, and the shriek is the warning). Missed, it chases, shrieking again before every lunge; a shut
/// door in its way it has to break open. It gives up on anyone further than its pursuit radius from its house, and walks back
/// to its corner; only blows from behind as it goes hurt it. Rule: when it shrieks, put a door between you.
/// </summary>
/// <remarks>
/// Loose in the world (<see cref="Enemy.Loose"/>, <see cref="Enemy.Local"/> its world position, on the floor).
/// <see cref="Enemy.Lateral"/> its heading (a yaw), <see cref="Enemy.Height"/> its <see cref="LodgerMode"/>,
/// <see cref="Enemy.Extra"/> the player it's after (−1 none), <see cref="Enemy.Extra2"/> its house (an index into the stop
/// walls' <c>OpenHouses</c>).
/// </remarks>
public sealed class Lodger(int id) : Enemy(id)
{
    double _modeSeconds, _noticed, _lunged, _rest;
    Double3 _hide, _home, _lungeWay;
    int _door = -1;

    public override EnemyKind Kind => EnemyKind.Lodger;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Sound;
    public override Want Want => Want.Kill;
    /// <summary>It belongs to its house: the director never dismisses it for want of company.</summary>
    public override bool StaysAboard => true;
    public override double MeleeRadius => 0.9;

    public LodgerMode Mode => (LodgerMode)(int)Height;
    public int House => (int)Extra2;
    public int? Target => Extra >= 0 ? (int)Extra : null;
    /// <summary>Its corner, where it hides (in the world, on the floor).</summary>
    public Double3 Hide => _hide;

    /// <summary>
    /// Folded in <paramref name="house"/>'s darkest corner: the floor furthest from its doors, <c>hideInset</c> off the walls
    /// (searched on a 0.25 m grid over its plan, the first furthest on a tie), facing into the room.
    /// </summary>
    public static Lodger In(int id, StopWalls.OpenHouse house, StopWalls walls, LodgerTuning t)
    {
        var doors = walls.HouseDoors.Where(d => d.House == house.Index).Select(d => d.At).ToList();
        var plan = house.Plan;
        double hx = Math.Max(0, plan.HalfX - t.HideInset), hy = Math.Max(0, plan.HalfY - t.HideInset);
        var best = house.Middle;
        double far = -1;
        for (double x = -hx; x <= hx + 1e-9; x += 0.25)
            for (double y = -hy; y <= hy + 1e-9; y += 0.25)
            {
                var p = house.World(plan.X + x, plan.Y + y);
                if (!walls.InHouse(house.Index, p + Double3.Up * 0.5))
                    continue;
                double d = doors.Count == 0 ? 0 : doors.Min(a => HouseWays.Flat(a - p));
                if (d > far + 1e-9)
                    (far, best) = (d, p);
            }
        var middle = house.Middle;
        return new Lodger(id)
        {
            Attached = Loose,
            Local = best,
            _hide = best,
            _home = middle,
            Lateral = HouseWays.Yaw(middle - best),
            Extra = -1,
            Extra2 = house.Index,
            Health = t.Health,
        };
    }

    void SetMode(LodgerMode mode)
    {
        if (Mode != mode)
            _modeSeconds = 0;
        Height = (double)mode;
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Lodger;
        double dt = SimConstants.TickSeconds;
        _modeSeconds += dt;
        _rest = Math.Max(0, _rest - dt);
        if (ctx.Train.Walls is not { } walls)
            return;
        // A kill done, or let go of: back to its corner.
        if (Phase is SpinePhase.Punish or SpinePhase.BreakOff)
            GoBack(ctx);
        if (Phase == SpinePhase.Grab)
            return;
        var living = ctx.LivingCrew().ToList();
        var me = Local;
        var target = living.Where(c => Target == c.Player.Id).Select(c => ((int Id, Double3 World)?)(c.Player.Id, c.World)).FirstOrDefault();

        switch (Mode)
        {
            case LodgerMode.Hidden:
                {
                    // Someone in its house a while, or right by it: it wakes on the nearest of them.
                    var inside = living.Where(c => walls.InHouse(House, c.World + Double3.Up * 0.5)).ToList();
                    _noticed = inside.Count > 0 ? _noticed + dt : Math.Max(0, _noticed - dt);
                    var close = living.Where(c => HouseWays.Flat(c.World - me) <= t.ShriekWithin && !HouseWays.Blocked(walls, me, c.World)).ToList();
                    if (_rest > 0 || (close.Count == 0 && (_noticed < t.NoticeSeconds || inside.Count == 0)))
                        return;
                    var pick = (close.Count > 0 ? close : inside).OrderBy(c => HouseWays.Flat(c.World - me)).ThenBy(c => c.Player.Id).First();
                    Shriek(ctx, pick.Player.Id);
                    return;
                }
            case LodgerMode.Shriek:
                {
                    if (target is not { } tg)
                    {
                        GoBack(ctx);
                        return;
                    }
                    Lateral = HouseWays.Yaw(tg.World - me);
                    // The shriek's the telegraph: at its end it commits, and lunges at where they are.
                    if (_modeSeconds >= t.ShriekSeconds && Enter(ctx, SpinePhase.Commit))
                    {
                        var way = (tg.World - me) with { Y = 0 };
                        _lungeWay = way.Length > 1e-6 ? way.Normalized : new Double3(-DMath.Sin(Lateral), 0, -DMath.Cos(Lateral));
                        _lunged = 0;
                        SetMode(LodgerMode.Lunge);
                    }
                    return;
                }
            case LodgerMode.Lunge:
                {
                    double step = t.LungeSpeed * dt;
                    var next = me + _lungeWay * step;
                    double hint = LineDistance;
                    Local = next with { Y = PlayerMotor.GroundAt(next, ctx.Train.Line, ref hint) };
                    LineDistance = hint;
                    _lunged += step;
                    if (target is { } tg && HouseWays.Flat(tg.World - Local) <= t.KillWithin && !HouseWays.Blocked(walls, Local, tg.World)
                        && Grab(ctx, tg.Id, t.KillWindow))
                        return;
                    if (_lunged >= t.LungeReach)
                    {
                        Enter(ctx, SpinePhase.Alert);
                        SetMode(LodgerMode.Chase);
                    }
                    return;
                }
            case LodgerMode.Chase:
                {
                    if (target is not { } tg || HouseWays.Flat(tg.World - _home) > t.PursuitRadius)
                    {
                        GoBack(ctx);
                        return;
                    }
                    bool blocked = HouseWays.Blocked(walls, me, tg.World);
                    // Close and nothing between: shriek again, and lunge.
                    if (!blocked && HouseWays.Flat(tg.World - me) <= t.ShriekWithin)
                    {
                        Shriek(ctx, tg.Id);
                        return;
                    }
                    // A shut door in its way: it breaks it open.
                    if (blocked && walls.DoorInReach(me, 1.3) is { } door && walls.Shut(door.Key))
                    {
                        _door = door.Key;
                        Lateral = HouseWays.Yaw(door.At - me);
                        SetMode(LodgerMode.Break);
                        return;
                    }
                    double hint = LineDistance;
                    HouseWays.Step(this, ctx, HouseWays.Toward(walls, me, tg.World), t.Chase, ref hint);
                    LineDistance = hint;
                    return;
                }
            case LodgerMode.Break:
                {
                    if (_modeSeconds >= t.BreakSeconds)
                    {
                        if (_door >= 0)
                            walls.SetShut(_door, false);
                        _door = -1;
                        SetMode(LodgerMode.Chase);
                    }
                    return;
                }
            case LodgerMode.Return:
                {
                    double hint = LineDistance;
                    HouseWays.Step(this, ctx, HouseWays.Toward(walls, me, _hide), t.Walk, ref hint);
                    LineDistance = hint;
                    if (HouseWays.Flat(_hide - Local) < 0.05)
                    {
                        Lateral = HouseWays.Yaw(_home - _hide);
                        _noticed = 0;
                        _rest = t.RestSeconds;
                        SetMode(LodgerMode.Hidden);
                    }
                    return;
                }
        }
    }

    void Shriek(EnemyContext ctx, int at)
    {
        Extra = at;
        Enter(ctx, SpinePhase.Telegraph);
        SetMode(LodgerMode.Shriek);
    }

    void GoBack(EnemyContext ctx)
    {
        Extra = -1;
        Enter(ctx, SpinePhase.Dormant);
        SetMode(LodgerMode.Return);
    }

    /// <summary>The one-hit kill: its grab's window is all but none (the shriek was the warning).</summary>
    protected override void Punish(EnemyContext ctx, int victim) => Kill(ctx, victim, DeathCause.Mauled);

    /// <summary>
    /// Struck: only blows as it walks back to its corner (from behind, it's not looking) hurt it; one at it in its corner
    /// wakes it on whoever struck; any other it shrugs off.
    /// </summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        if (Mode == LodgerMode.Return)
        {
            base.Struck(ctx, by, damage);
            return;
        }
        Marked(ctx, by);
        if (Mode == LodgerMode.Hidden)
            Shriek(ctx, by);
    }
}
