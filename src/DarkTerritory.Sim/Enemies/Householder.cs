using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Enemies;

/// <summary>What the Householder is doing (replicated in <see cref="Enemy.Height"/>), for its clips and cues.</summary>
public enum HouseholderMode : byte { Sit, Watch, Rise, Hunt, Grab, Return }

/// <summary>
/// THE HOUSEHOLDER · sight · outside (GDD §21; ARCHITECTURE §8 note 584; the director, 9 Oct 2026: "The householder is very
/// good"; docs/design/creatures/householder.md). A gaunt figure at its set table in a lived-in-looking house, harmless to
/// guests: it only watches. Everything lying in its house is its own. Anything of yours left on its table pays for one of its
/// things; one of its things carried out of the door unpaid and it rises and hunts whoever carries it, slow and relentless, to
/// the train if it must (a grab a friend can break). Dropped, it takes the thing back and sits again. Rule: pay for what you
/// take.
/// </summary>
/// <remarks>
/// Loose in the world (<see cref="Enemy.Local"/> its world position: its chair at the table while it sits).
/// <see cref="Enemy.Lateral"/> its heading (a yaw; sitting, toward its table), <see cref="Enemy.Height"/> its
/// <see cref="HouseholderMode"/>, <see cref="Enemy.Extra"/> the player it's hunting (−1 none), <see cref="Enemy.Extra2"/> its
/// house (an index into the stop walls' <c>OpenHouses</c>). Its table stands <see cref="TableOut"/> m in front of its chair.
/// What it owns and what's been paid are the host's alone.
/// </remarks>
public sealed class Householder(int id) : Enemy(id)
{
    /// <summary>How far in front of its chair its table stands (the art draws the set table there).</summary>
    public const double TableOut = 0.7;

    double _modeSeconds;
    Double3 _chair;
    double _chairYaw;
    int _credit, _thing = -1;
    readonly SortedSet<int> _mine = [], _paid = [];

    public override EnemyKind Kind => EnemyKind.Householder;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Sight;
    public override Want Want => Want.Cargo;
    /// <summary>It belongs to its house: the director never dismisses it for want of company.</summary>
    public override bool StaysAboard => true;
    /// <summary>Its hands are a friend's to prise off: holding Use beside the one it has frees them.</summary>
    public override bool PullsFree => true;
    public override double MeleeRadius => 0.9;

    public HouseholderMode Mode => (HouseholderMode)(int)Height;
    public int House => (int)Extra2;
    /// <summary>Its table, in the world, on the floor.</summary>
    public Double3 Table => _chair + Way(_chairYaw) * TableOut;
    /// <summary>The things in its house it counts as its own (by body id), and how many are paid for.</summary>
    public IReadOnlyCollection<int> Mine => _mine;
    public int Credit => _credit;

    /// <summary>Sat at its table in <paramref name="house"/>: its chair a little back from the middle, facing its nearest door.</summary>
    public static Householder In(int id, StopWalls.OpenHouse house, StopWalls walls, HouseholderTuning t)
    {
        var middle = house.Middle;
        var door = HouseWays.Door(walls, house.Index, middle);
        var toDoor = door is { } d ? (d.At - middle) with { Y = 0 } : house.Ex;
        toDoor = toDoor.Length > 1e-6 ? toDoor.Normalized : house.Ex;
        var chair = middle - toDoor * 0.6;
        double yaw = HouseWays.Yaw(toDoor);
        return new Householder(id) { Attached = Loose, Local = chair, _chair = chair, _chairYaw = yaw, Lateral = yaw, Extra = -1, Extra2 = house.Index, Health = t.Health };
    }

    void SetMode(HouseholderMode mode)
    {
        if (Mode != mode)
            _modeSeconds = 0;
        Height = (double)mode;
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Householder;
        double dt = SimConstants.TickSeconds;
        _modeSeconds += dt;
        if (ctx.Train.Walls is not { } walls)
            return;
        var bodies = ctx.World.Bodies;
        var train = ctx.Train;
        // Its own: whatever lies in its house; paid: what's of yours left on its table (one of its things for each).
        foreach (var b in bodies.All.OrderBy(b => b.Id))
        {
            if (b.Kind == BodyKind.Ragdoll || b.Carrier >= 0 || b.TakenBy >= 0 || b.Parent != PlayerState.World)
                continue;
            var at = Bodies.WorldCentre(b, train);
            if (!walls.InHouse(House, at + Double3.Up * 0.3))
                continue;
            if (!_mine.Contains(b.Id) && !_paid.Contains(b.Id) && HouseWays.Flat(at - Table) <= t.TableReach && _modeSeconds > 0 && Mode != HouseholderMode.Return)
            {
                _paid.Add(b.Id);
                _credit++;
                continue;
            }
            if (!_paid.Contains(b.Id))
                _mine.Add(b.Id);
        }
        if (Phase is SpinePhase.Punish or SpinePhase.BreakOff)
        {
            Extra = -1;
            Enter(ctx, SpinePhase.Dormant);
            SetMode(HouseholderMode.Return);
        }
        if (Phase == SpinePhase.Grab)
        {
            SetMode(HouseholderMode.Grab);
            return;
        }
        var living = ctx.LivingCrew().ToList();
        var me = Local;

        switch (Mode)
        {
            case HouseholderMode.Sit or HouseholderMode.Watch:
            {
                Local = _chair;
                // One of its things carried out of the door: paid for, or it rises for whoever has it.
                foreach (var c in living.OrderBy(c => c.Player.Id))
                {
                    var carried = bodies.All.Where(b => _mine.Contains(b.Id) && b.Carrier == c.Player.Id).OrderBy(b => b.Id).FirstOrDefault();
                    if (carried is null || walls.InHouse(House, c.World + Double3.Up * 0.5))
                        continue;
                    _mine.Remove(carried.Id);
                    if (_credit > 0)
                    {
                        _credit--;
                        continue;
                    }
                    _thing = carried.Id;
                    Extra = c.Player.Id;
                    Enter(ctx, SpinePhase.Telegraph);
                    SetMode(HouseholderMode.Rise);
                    return;
                }
                // Guests it watches.
                var guest = living.Where(c => walls.InHouse(House, c.World + Double3.Up * 0.5)).OrderBy(c => HouseWays.Flat(c.World - me)).ThenBy(c => c.Player.Id)
                    .Select(c => (Double3?)c.World).FirstOrDefault();
                if (guest is { } g)
                {
                    SetMode(HouseholderMode.Watch);
                    Lateral = HouseWays.Yaw(g - me);
                }
                else
                {
                    SetMode(HouseholderMode.Sit);
                    Lateral = _chairYaw;
                }
                return;
            }
            case HouseholderMode.Rise:
            {
                if (_modeSeconds >= t.RiseSeconds && Enter(ctx, SpinePhase.Commit))
                    SetMode(HouseholderMode.Hunt);
                return;
            }
            case HouseholderMode.Hunt:
            {
                var thing = bodies.All.FirstOrDefault(b => b.Id == _thing);
                var carrier = living.Where(c => thing is not null && thing.Carrier == c.Player.Id).Select(c => ((int Id, Double3 World)?)(c.Player.Id, c.World)).FirstOrDefault();
                if (thing is null)
                {
                    // Gone (sold, lost, in a car the train took off): nothing to take back.
                    GoBack(ctx);
                    return;
                }
                var thingAt = Bodies.WorldCentre(thing, train);
                if (HouseWays.Flat(thingAt - _chair) > t.GiveUpBeyond)
                {
                    GoBack(ctx);
                    return;
                }
                if (carrier is { } who)
                {
                    Extra = who.Id;
                    if (HouseWays.Flat(who.World - me) <= t.GrabReach && !HouseWays.Blocked(walls, me, who.World) && Grab(ctx, who.Id, t.GrabSeconds))
                    {
                        SetMode(HouseholderMode.Grab);
                        return;
                    }
                    double hint = LineDistance;
                    HouseWays.Step(this, ctx, HouseWays.Toward(walls, me, who.World), t.Hunt, ref hint);
                    LineDistance = hint;
                    return;
                }
                // Put down: it takes it back.
                Extra = -1;
                if (thing.Carrier < 0 && thing.TakenBy < 0 && HouseWays.Flat(thingAt - me) <= 0.9)
                {
                    bodies.TakeAlong(thing, train, Id, PlayerState.World, me + Double3.Up * 0.9, Lateral);
                    GoBack(ctx);
                    return;
                }
                double h2 = LineDistance;
                HouseWays.Step(this, ctx, HouseWays.Toward(walls, me, thingAt), t.Hunt, ref h2);
                LineDistance = h2;
                return;
            }
            case HouseholderMode.Return or HouseholderMode.Grab:
            {
                SetMode(HouseholderMode.Return);
                var held = bodies.All.FirstOrDefault(b => b.Id == _thing && b.TakenBy == Id);
                double hint = LineDistance;
                bool there = HouseWays.Step(this, ctx, HouseWays.Toward(walls, me, _chair), t.Walk, ref hint) && HouseWays.Flat(_chair - Local) < 0.05;
                LineDistance = hint;
                if (held is { } h)
                    bodies.TakeAlong(h, train, Id, PlayerState.World, Local + Double3.Up * 0.9, Lateral);
                if (there)
                {
                    // Home: what it carried back goes on the floor beside its chair, its own again.
                    if (held is { } back)
                    {
                        bodies.LetGo(back);
                        _mine.Add(back.Id);
                    }
                    _thing = -1;
                    Lateral = _chairYaw;
                    SetMode(HouseholderMode.Sit);
                }
                return;
            }
        }
    }

    void GoBack(EnemyContext ctx)
    {
        Extra = -1;
        Enter(ctx, SpinePhase.Dormant);
        SetMode(HouseholderMode.Return);
    }

    protected override void Punish(EnemyContext ctx, int victim) => Kill(ctx, victim, DeathCause.Seized);

    static Double3 Way(double yaw) => new(-DMath.Sin(yaw), 0, -DMath.Cos(yaw));
}
