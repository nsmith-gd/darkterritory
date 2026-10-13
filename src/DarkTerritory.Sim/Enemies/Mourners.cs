using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>What a Mourner is doing (replicated in <see cref="Enemy.Height"/>), for its clips and cues.</summary>
public enum MournerMode : byte { Come, Wait, Creep, Drag, Startle, Follow, Leave }

/// <summary>
/// THE MOURNERS · absence · outside (GDD §21, App. A.6, B.6; ARCHITECTURE §8 note 362; the director's brief of 8 Oct 2026,
/// docs/design/creatures/mourners.md). Small ash-pale scavengers that come only after a crewmate dies, in a nervous group,
/// like crows to a carcass: they edge in, start back from anyone who comes near, and the moment nobody's standing over the
/// body two of them take it and drag it straight away from the railway. They never touch the living. A crewmate close (or
/// a blow on any of them) makes them drop it and scatter; a blow kills one; a body carried is followed, never touched; a
/// body brought home (into a car of the train) sends them away; a body dragged far enough from the line is gone, and its
/// refund (holdouts.json <c>bodyRefund</c>) and the kit it carried with it. Rule: stand over your dead, or carry them home.
/// </summary>
/// <remarks>
/// One of a group: each is its own enemy, loose in the world (<see cref="Enemy.Loose"/>, <see cref="Enemy.Local"/> its
/// world position). <see cref="Enemy.Extra"/> is the body's id, <see cref="Enemy.Extra2"/> the group's lead (the first of
/// them, by id), <see cref="Enemy.Lateral"/> its heading, <see cref="Enemy.Height"/> its <see cref="MournerMode"/>. What
/// they all do is decided by each from the same state, in id order, so the host and every client agree.
/// </remarks>
public sealed class Mourner(int id) : Enemy(id)
{
    double _modeSeconds, _scatter, _side;

    public override EnemyKind Kind => EnemyKind.Mourners;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Absence;
    /// <summary>The body is worth money (holdouts.json): what they take is the crew's refund.</summary>
    public override Want Want => Want.Cargo;
    /// <summary>Frail: any blow that reaches one lands.</summary>
    public override double MeleeRadius => Mode == MournerMode.Leave ? 0 : 0.9;
    /// <summary>They stay till the body's home or gone; the director never dismisses them for want of company.</summary>
    public override bool StaysAboard => true;

    public MournerMode Mode => (MournerMode)(int)Height;
    public int BodyId => (int)Extra;

    /// <summary>
    /// A group come for <paramref name="body"/>: <paramref name="count"/> of them, <c>arriveAt</c> metres off it on the side
    /// away from the line, spread along an arc. Ids from <paramref name="firstId"/> up; the first is the group's lead.
    /// </summary>
    public static List<Mourner> Come(int firstId, int count, Body body, TrainOnLine train, MournersTuning t)
    {
        var at = Bodies.WorldCentre(body, train);
        var away = AwayFromLine(train, at, out _);
        var right = Double3.Cross(away, Double3.Up).Normalized;
        var group = new List<Mourner>();
        for (int i = 0; i < count; i++)
        {
            double spread = count > 1 ? (i / (double)(count - 1) - 0.5) * 1.6 : 0;
            var spot = at + (away * DMath.Cos(spread) + right * DMath.Sin(spread)) * t.ArriveAt;
            double hint = train.Dynamics.Distance;
            spot = spot with { Y = PlayerMotor.GroundAt(spot, train.Line, ref hint) };
            var m = new Mourner(firstId + i)
            {
                Attached = Loose,
                Local = spot,
                LineDistance = hint,
                Extra = body.Id,
                Extra2 = firstId,
                Health = t.Health,
            };
            m._side = spread;
            m.Lateral = Yaw(at - spot);
            m.SetMode(MournerMode.Come);
            group.Add(m);
        }
        return group;
    }

    void SetMode(MournerMode mode)
    {
        if (Mode != mode)
            _modeSeconds = 0;
        Height = (double)mode;
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Mourners;
        var train = ctx.Train;
        double dt = SimConstants.TickSeconds;
        _modeSeconds += dt;
        _scatter = Math.Max(0, _scatter - dt);
        // Always loose in the world (one put on a car or the line, by a test or an audit, steps off where it stands).
        if (Attached != Loose)
        {
            Local = WorldPosition(train);
            Attached = Loose;
        }
        // Their coming is the telegraph (the keening, the pale shapes at the light's edge); taking the body, the commit.
        if (Phase == SpinePhase.Dormant)
            Enter(ctx, SpinePhase.Telegraph);

        var body = ctx.World.Bodies.All.FirstOrDefault(b => b.Id == BodyId);
        if (Mode == MournerMode.Leave || body is null || Home(body, train))
        {
            Leave(ctx, t, body);
            return;
        }
        var me = Local;
        var bodyAt = body.Carrier >= 0 ? CarrierAt(ctx, body.Carrier) ?? Bodies.WorldCentre(body, train) : Bodies.WorldCentre(body, train);
        var living = ctx.LivingCrew().Select(c => c.World).ToList();
        double nearMe = living.Count > 0 ? living.Min(p => Flat(p - me)) : double.MaxValue;
        double nearBody = living.Count > 0 ? living.Min(p => Flat(p - bodyAt)) : double.MaxValue;

        // Dragging it: dropped for a crewmate close by (or a blow, Struck), else hauled straight off from the line.
        if (body.TakenBy == Id)
        {
            if (nearBody <= t.DropWithin || _scatter > 0)
            {
                ctx.World.Bodies.LetGo(body);
                Startle();
                return;
            }
            Haul(ctx, t, body);
            return;
        }

        // Started: back off from whoever came near (to startleTo off them), then edge in again after returnAfter.
        if (Mode == MournerMode.Startle)
        {
            if (living.Count > 0)
            {
                var near = living.MinBy(p => Flat(p - me));
                if (Flat(near - me) < t.StartleTo)
                    Walk(ctx, (me - near) with { Y = 0 }, t.Scatter);
            }
            if (_modeSeconds >= t.ReturnAfter && _scatter <= 0)
                SetMode(MournerMode.Wait);
            return;
        }
        if (nearMe < t.Shy || _scatter > 0)
        {
            Startle();
            return;
        }

        // Carried: they follow at a distance, waiting for it to be put down.
        if (body.Carrier >= 0)
        {
            SetMode(MournerMode.Follow);
            Ring(ctx, t, bodyAt, living, t.Shy + 2);
            return;
        }

        // Nobody over it: the two of them nearest it go in and take it (one hauls; the other, once it's hauling, beside it).
        var group = Group(ctx);
        var hauler = group.FirstOrDefault(g => body.TakenBy == g.Id);
        if (nearBody >= t.Shy && hauler is not null)
        {
            // Its partner: whoever's already hauling beside it, else the nearest of the rest.
            var helper = group.Where(g => g != hauler && g.Mode == MournerMode.Drag).MinBy(g => g.Id)
                ?? group.Where(g => g != hauler && g.Mode != MournerMode.Startle).OrderBy(g => Flat(g.Local - bodyAt)).ThenBy(g => g.Id).FirstOrDefault();
            if (helper == this)
            {
                SetMode(MournerMode.Drag);
                var beside = hauler.Local + Double3.Cross(AwayFromLine(train, hauler.Local, out _), Double3.Up).Normalized * 0.8;
                StepTo(ctx, beside, t.Drag * 1.5);
                Lateral = hauler.Lateral;
                return;
            }
        }
        else if (nearBody >= t.Shy
            && group.Where(g => g.Mode != MournerMode.Startle).OrderBy(g => Flat(g.Local - bodyAt)).ThenBy(g => g.Id).FirstOrDefault() == this)
        {
            SetMode(MournerMode.Creep);
            if (Flat(bodyAt - me) > t.TakeReach)
            {
                StepTo(ctx, bodyAt, t.Creep);
                return;
            }
            // At it, and nobody near: theirs now. (The spine's commit: the telegraph ran all the way in.)
            if (Enter(ctx, SpinePhase.Commit))
                Haul(ctx, t, body);
            return;
        }
        // Waiting their chance: a loose ring off it, on the far side from whoever's nearest.
        SetMode(Mode == MournerMode.Come && _modeSeconds < t.ComeSeconds ? MournerMode.Come : MournerMode.Wait);
        Ring(ctx, t, bodyAt, living, nearBody < t.Shy ? t.Shy + 2 : t.WaitAt);
    }

    /// <summary>Hauls the body a step straight off from the nearest track; gone with it far enough out (its refund and kit lost).</summary>
    void Haul(EnemyContext ctx, MournersTuning t, Body body)
    {
        var train = ctx.Train;
        var away = AwayFromLine(train, Local, out double off);
        if (off >= t.LostAt)
        {
            // Out past where anyone will find it: the body's gone, and they go with it.
            ctx.World.Bodies.Remove(body);
            foreach (var g in Group(ctx))
                g.SetMode(MournerMode.Leave);
            SetMode(MournerMode.Leave);
            ctx.World.MournersTook++;
            return;
        }
        SetMode(MournerMode.Drag);
        Walk(ctx, away, t.Drag);
        // Leaning back, hauling: facing the body (toward the line), the body held low in front of them.
        Lateral = Yaw(away * -1);
        var hold = Local - away * t.HoldAt + Double3.Up * t.HoldHeight;
        ctx.World.Bodies.TakeAlong(body, train, Id, PlayerState.World, hold, Yaw(away));
    }

    /// <summary>Off into the dark: gone once well clear of the train, or a while after.</summary>
    void Leave(EnemyContext ctx, MournersTuning t, Body? body)
    {
        if (body is { } b && b.TakenBy == Id)
            ctx.World.Bodies.LetGo(b);
        SetMode(MournerMode.Leave);
        var away = AwayFromLine(ctx.Train, Local, out double off);
        Walk(ctx, away, t.Scatter);
        if (off >= t.ArriveAt + 20 || _modeSeconds >= t.LeaveSeconds)
            Enter(ctx, SpinePhase.Gone);
    }

    void Startle() => SetMode(MournerMode.Startle);

    /// <summary>Waiting in a loose ring <paramref name="radius"/> off the body, on the side away from the nearest crewmate.</summary>
    void Ring(EnemyContext ctx, MournersTuning t, Double3 bodyAt, List<Double3> living, double radius)
    {
        var from = living.Count > 0 ? living.MinBy(p => Flat(p - bodyAt)) : bodyAt - AwayFromLine(ctx.Train, bodyAt, out _);
        var away = (bodyAt - from) with { Y = 0 };
        away = away.Length > 1e-6 ? away.Normalized : AwayFromLine(ctx.Train, bodyAt, out _);
        var right = Double3.Cross(away, Double3.Up).Normalized;
        var spot = bodyAt + (away * DMath.Cos(_side) + right * DMath.Sin(_side)) * radius;
        StepTo(ctx, spot, t.Creep);
        Lateral = Yaw(bodyAt - Local);
    }

    /// <summary>The rest of its group still about, in id order.</summary>
    List<Mourner> Group(EnemyContext ctx) =>
        [.. ctx.World.ActiveEnemies.OfType<Mourner>().Where(m => !m.Gone && m.Extra2 == Extra2).OrderBy(m => m.Id)];

    /// <summary>
    /// Struck: a blow kills one (they're frail), and the rest of the group scatter for <c>scatterOnDeath</c>; whoever was
    /// hauling the body drops it.
    /// </summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        var t = ctx.Tuning.Mourners;
        if (ctx.World.Bodies.All.FirstOrDefault(b => b.Id == BodyId && b.TakenBy == Id) is { } held)
            ctx.World.Bodies.LetGo(held);
        base.Struck(ctx, by, damage);
        foreach (var g in Group(ctx))
            if (g != this)
            {
                g._scatter = t.ScatterOnDeath;
                g.SetMode(MournerMode.Startle);
            }
    }

    /// <summary>Home: in a car of the train the engine pulls (what the report counts as brought home), or the train gone.</summary>
    static bool Home(Body body, TrainOnLine train) =>
        body.Parent >= 0 && train.Dynamics.Consist.IndexOf(body.Parent) >= 0;

    static Double3? CarrierAt(EnemyContext ctx, int carrier) =>
        ctx.LivingCrew().Where(c => c.Player.Id == carrier).Select(c => (Double3?)c.World).FirstOrDefault();

    /// <summary>Level, away from the nearest track (its right where it's straight over it), and how far off it is now.</summary>
    static Double3 AwayFromLine(TrainOnLine train, Double3 at, out double off)
    {
        double hint = train.Dynamics.Distance;
        var (path, d) = train.Line.Nearest(at, ref hint);
        var on = train.Line.Sample(path, d);
        var away = (at - on.Position) with { Y = 0 };
        off = away.Length;
        if (off > 1e-3)
            return away.Normalized;
        return Double3.Cross(on.Tangent, Double3.Up).Normalized;
    }

    void Walk(EnemyContext ctx, Double3 way, double speed)
    {
        way = way with { Y = 0 };
        if (way.Length < 1e-6)
            return;
        var next = Local + way.Normalized * speed * SimConstants.TickSeconds;
        double hint = LineDistance;
        Local = next with { Y = PlayerMotor.GroundAt(next, ctx.Train.Line, ref hint) };
        LineDistance = hint;
        Lateral = Yaw(way);
    }

    void StepTo(EnemyContext ctx, Double3 to, double speed)
    {
        var way = (to - Local) with { Y = 0 };
        // Far off, they come at a trot; the last stretch in, the slow bent creep.
        if (way.Length > ctx.Tuning.Mourners.WaitAt + 2)
            speed = Math.Max(speed, ctx.Tuning.Mourners.Approach);
        double step = speed * SimConstants.TickSeconds;
        if (way.Length <= step)
        {
            double hint = LineDistance;
            Local = to with { Y = PlayerMotor.GroundAt(to, ctx.Train.Line, ref hint) };
            LineDistance = hint;
            return;
        }
        Walk(ctx, way, speed);
    }

    static double Flat(Double3 v) => (v with { Y = 0 }).Length;

    /// <summary>A heading for a way along the ground, as a player's yaw (−Z forward).</summary>
    static double Yaw(Double3 way) => DMath.Atan2(-way.X, -way.Z);
}

/// <summary>
/// Where the Mourners come from (note 362): every crewmate's body lying off the train, watched; a group comes for one left
/// <c>after</c> seconds, as many as the tier says. On the host, once a second. Not the director's: they hunt no one, cost it
/// nothing, and come for every body.
/// </summary>
public sealed class Mourning
{
    // Each body's seconds lying off the train, by id; and the bodies a group has already come for.
    readonly SortedDictionary<int, double> _lying = [];
    readonly SortedSet<int> _come = [];

    public void Step(World world, MournersTuning t, Route.RouteTier tier, ref int nextId, List<Enemy> into, double seconds)
    {
        var train = world.Train;
        foreach (var b in world.Bodies.All)
        {
            if (b.Kind != BodyKind.Ragdoll || b.DroppedOut || _come.Contains(b.Id))
                continue;
            bool off = b.Carrier < 0 && b.TakenBy < 0 && !(b.Parent >= 0 && train.Dynamics.Consist.IndexOf(b.Parent) >= 0);
            if (!off)
            {
                _lying.Remove(b.Id);
                continue;
            }
            double lying = _lying.GetValueOrDefault(b.Id) + seconds;
            _lying[b.Id] = lying;
            if (lying < t.After)
                continue;
            _come.Add(b.Id);
            _lying.Remove(b.Id);
            int count = t.CountFor(tier);
            into.AddRange(Mourner.Come(nextId, count, b, train, t));
            nextId += count;
        }
    }
}
