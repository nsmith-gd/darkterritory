using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// RIBBITS · sight · outside (GDD v1.1 §21, App. A.6). Giant toad-rabbits in packs of two to four (never more than the crew)
/// in yards and villages. They hop toward the nearest group in bursts, and ignore any group at least their size (a group is
/// players within 8 m of each other, App. C.6). Catch someone outnumbered within ~4 m and the pack halts, lines up, throats
/// swelling (the telegraph), and their tongues freeze the target in place (they can still talk) while the pack hops in to
/// eat (~8 s). Friends arriving to even the count break it, and so does clubbing them. Outrunnable, if you run (5.5 m/s
/// against their ~4). Rule: never be outnumbered.
/// </summary>
/// <remarks>
/// Loose in the world. <see cref="Pack"/> is the pack's first id; the lowest-id living member leads (it chooses, it grabs).
/// <see cref="Enemy.Extra"/> is who the pack is after (−1 for nobody).
/// </remarks>
public sealed class Ribbit(int id, int pack) : Enemy(id)
{
    public override EnemyKind Kind => EnemyKind.Ribbit;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Sight;
    public override Want Want => Want.Kill;
    public override double MeleeRadius => 0.7;
    public int Pack { get; } = pack;
    public int? Target => Extra >= 0 ? (int)Extra : null;

    public static Ribbit At(int id, int pack, Double3 world, RibbitTuning t) => new(id, pack) { Attached = Loose, Local = world, Extra = -1, Health = t.Health };

    List<Ribbit> Members(EnemyContext ctx) => [.. ctx.World.ActiveEnemies.OfType<Ribbit>().Where(r => r.Pack == Pack && !r.Gone)];

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Ribbits;
        var members = Members(ctx);
        var leader = members.MinBy(r => r.Id)!;
        int size = members.Count;
        if (leader == this && Phase != SpinePhase.Grab)
            Choose(ctx, t, members);
        Extra = leader.Extra;
        var target = ctx.LivingCrew().FirstOrDefault(c => Target is { } w && c.Player.Id == w);
        if (Target is not { } who || ctx.LivingCrew().All(c => c.Player.Id != who))
        {
            if (Phase is SpinePhase.Telegraph or SpinePhase.Commit)
            {
                Enter(ctx, SpinePhase.BreakOff);
                Enter(ctx, SpinePhase.Dormant);
            }
            return;
        }
        var to = (target.World - Local) with { Y = 0 };
        double d = to.Length;
        switch (Phase)
        {
            case SpinePhase.Dormant:
                Hop(to, t.HopSpeed);
                // TONGUE: the pack outnumbers them and the leader's within reach: halt, line up, throats swell.
                if (leader == this && d <= t.TongueReach)
                    foreach (var m in members)
                        m.Enter(ctx, SpinePhase.Telegraph);
                return;
            case SpinePhase.Telegraph:
                // Outrun (App. A.6: "outrunnable, if you run"): out of the tongues' reach before they fire, and they hop after.
                if (leader == this && d > t.TongueReach * 1.5)
                {
                    foreach (var m in members)
                    {
                        m.Enter(ctx, SpinePhase.BreakOff);
                        m.Enter(ctx, SpinePhase.Dormant);
                    }
                    return;
                }
                if (leader == this && PhaseSeconds >= ctx.Tuning.MinReactionSeconds && Enter(ctx, SpinePhase.Commit))
                    Grab(ctx, who, t.DevourSeconds);
                return;
            case SpinePhase.Grab:
                // Friends arriving even the count: the tongue lets go.
                if (CrewSense.Group(ctx, who, t.GroupRadius).Count >= size)
                {
                    Rescued(ctx, -1);
                    return;
                }
                Hop(to, t.HopSpeed * 0.25);
                return;
            default:
                // The pack's slow hop in on a frozen target.
                Hop(to, leader.Phase == SpinePhase.Grab ? t.HopSpeed * 0.25 : t.HopSpeed);
                return;
        }
    }

    /// <summary>
    /// The leader picks: the nearest player on the ground whose group is smaller than the pack (App. A.6 COUNT/IGNORE);
    /// nobody's outnumbered, or they've got away (aboard, or run clear), and it's nobody.
    /// </summary>
    void Choose(EnemyContext ctx, RibbitTuning t, List<Ribbit> members)
    {
        int size = members.Count;
        var centre = members.Aggregate(Double3.Zero, (a, m) => a + m.Local) * (1.0 / size);
        int? pick = null;
        double best = double.MaxValue;
        foreach (var (p, w) in ctx.LivingCrew())
        {
            if (!CrewSense.OnGround(p.State) || CrewSense.Group(ctx, p.Id, t.GroupRadius).Count >= size)
                continue;
            double d = ((w - centre) with { Y = 0 }).Length;
            if (d <= t.GiveUpBeyond && d < best)
            {
                best = d;
                pick = p.Id;
            }
        }
        Extra = pick ?? -1;
    }

    /// <summary>In bursts: a leap, a sit, averaging <paramref name="average"/>.</summary>
    void Hop(Double3 to, double average)
    {
        bool leaping = (int)(PhaseSeconds * 2 + Id) % 2 == 0;
        double step = (leaping ? 2 * average : 0) * SimConstants.TickSeconds;
        if (to.Length > 0.9)
            Local += to.Normalized * Math.Min(step, to.Length - 0.8);
    }

    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.Devoured);
        foreach (var m in Members(ctx))
        {
            m.Enter(ctx, SpinePhase.BreakOff);
            m.Enter(ctx, SpinePhase.Gone);
        }
    }

    protected override void Rescued(EnemyContext ctx, int by)
    {
        base.Rescued(ctx, by);
        Enter(ctx, SpinePhase.Dormant);
    }
}

/// <summary>
/// THE GAUNT · sound (silence) · outside (GDD v1.1 §21, App. A.6). A lonely, spindly thing asleep, curled up in a village or
/// yard: a folded shape breathing slowly, that stirs as you near. Come too close and it wakes, and follows its waker home at
/// arm's length, onto the train with them. It listens: every few seconds of silence near it, its anger climbs (it leans in
/// closer, head tilting, the telegraph), and at its threshold it attacks the nearest player, hard. Talking to it holds it
/// off. Kill it (a group job: it hits hard), or lead it to a car, where it takes the most valuable thing and leaves for the
/// run. Rule: keep talking to it. It punishes silence while the Choir punishes noise.
/// </summary>
/// <remarks>
/// <see cref="Enemy.Extra"/> is its waker (−1 asleep), <see cref="Enemy.Extra2"/> its anger (the lean). Talking only stops
/// the anger rising (GDD Part Eleven Q9, open): the tuning flag <c>anyVoiceCounts</c> says whose voice counts.
/// </remarks>
public sealed class Gaunt(int id) : Enemy(id)
{
    double _silence, _hit;

    public override EnemyKind Kind => EnemyKind.Gaunt;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Sound;
    public override Want Want => Want.Split;
    public override double MeleeRadius => Phase == SpinePhase.Dormant ? 0 : 0.8;
    public int? Waker => Extra >= 0 ? (int)Extra : null;
    public int Anger => (int)Extra2;

    public static Gaunt Asleep(int id, Double3 world, GauntTuning t) => new(id) { Attached = Loose, Local = world, Extra = -1, Health = t.Health };

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Gaunt;
        var train = ctx.Train;
        switch (Phase)
        {
            case SpinePhase.Dormant or SpinePhase.Alert:
                {
                    // ASLEEP; it stirs as you near (the telegraph), and wakes if you come too close.
                    var here = WorldPosition(train);
                    var near = ctx.LivingCrew().OrderBy(c => (c.World - here).Length).FirstOrDefault();
                    if (ctx.LivingCrew().All(c => c.Player.Id != near.Player.Id))
                        return;
                    double d = (near.World - here).Length;
                    if (Phase == SpinePhase.Dormant && d <= t.StirAt)
                        Enter(ctx, SpinePhase.Alert);
                    if (Phase == SpinePhase.Alert && d <= t.WakeAt)
                    {
                        Extra = near.Player.Id;
                        Enter(ctx, SpinePhase.Telegraph); // awake, and following
                    }
                    if (PhaseSeconds >= t.LingerSeconds)
                        Enter(ctx, SpinePhase.Gone);
                    return;
                }
            case SpinePhase.Telegraph or SpinePhase.Commit:
                {
                    if (Waker is not { } who || ctx.Crew.FirstOrDefault(c => c.Player.Id == who).Player.State is not { Alive: true } w)
                    {
                        // Its waker's dead: it follows whoever's nearest.
                        if (ctx.LivingCrew().Select(c => (int?)c.Player.Id).FirstOrDefault() is { } next)
                            Extra = next;
                        else
                            Enter(ctx, SpinePhase.Gone);
                        return;
                    }
                    Follow(ctx, w, t);
                    if (Phase == SpinePhase.Telegraph && Takes(ctx, w, t))
                        return;
                    // LISTEN: silence near it, and the anger climbs; a voice holds it where it is.
                    var at = WorldPosition(train);
                    bool talked = ctx.Crew.Any(c => c.Player.State.Alive && c.Intent.Voice >= t.TalkingAbove
                        && (t.AnyVoiceCounts || c.Player.Id == who)
                        && (PlayerMotor.WorldPosition(c.Player.State, train) - at).Length <= t.ListenRadius);
                    _silence = talked ? 0 : _silence + SimConstants.TickSeconds;
                    if (_silence >= t.SilenceSeconds)
                    {
                        _silence = 0;
                        Extra2 = Math.Min(t.AttackAt, Extra2 + 1);
                    }
                    if (Phase == SpinePhase.Telegraph && Extra2 >= t.AttackAt && PhaseSeconds >= ctx.Tuning.MinReactionSeconds)
                        Enter(ctx, SpinePhase.Commit);
                    if (Phase == SpinePhase.Commit)
                        Attack(ctx, t);
                    return;
                }
            case SpinePhase.Grab:
                return;
            default:
                Enter(ctx, SpinePhase.Gone);
                return;
        }
    }

    /// <summary>At its waker's back, at arm's length: in their car's frame aboard, loose in the world off it.</summary>
    void Follow(EnemyContext ctx, in PlayerState w, GauntTuning t)
    {
        var train = ctx.Train;
        var behind = new Double3(DMath.Sin(w.Yaw), 0, DMath.Cos(w.Yaw)) * t.FollowAt;
        if (w.Parent >= 0)
        {
            Attached = w.Parent;
            Local = w.Position + behind;
            return;
        }
        var want = PlayerMotor.WorldPosition(w, train) + new Double3(DMath.Sin(PlayerMotor.WorldYaw(w, train)), 0, DMath.Cos(PlayerMotor.WorldYaw(w, train))) * t.FollowAt;
        var from = WorldPosition(train);
        Attached = Loose;
        var to = want - from;
        double step = t.FollowSpeed * SimConstants.TickSeconds;
        Local = to.Length <= step ? want : from + to.Normalized * step;
    }

    /// <summary>
    /// Led into a car (its waker inside one with something in it): it takes the most valuable thing there, and leaves for the
    /// run (App. A.6 COUNTER). Hand loot first; with none, some of the car's freight.
    /// </summary>
    bool Takes(EnemyContext ctx, in PlayerState w, GauntTuning t)
    {
        var train = ctx.Train;
        if (w.Parent <= 0 || w.Parent >= train.Frames.Count || train.Frames[w.Parent].Shape.Interior is not { } room || !room.Contains(w.Position))
            return false;
        int car = w.Parent;
        var vehicle = train.Vehicles[car];
        // What's in a shut crew locker it doesn't get at (note 166); an open one's as good as the floor.
        var loot = ctx.World.Bodies.All.Where(b => b.Parent == car && b.Carrier < 0 && Bodies.Value(b.Kind) > 0 && (!b.Stowed || vehicle.LockerOpen(b.Locker)))
            .MaxBy(b => Bodies.Value(b.Kind));
        if (loot is null && (vehicle.Load <= 0.01 || vehicle.CargoIntegrity <= 0.01))
            return false;
        if (loot is not null)
            ctx.World.Bodies.Remove(loot);
        else
            vehicle.CargoIntegrity = Math.Max(0, vehicle.CargoIntegrity - t.LootCargo);
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Gone);
        return true;
    }

    /// <summary>ATTACK: heavy blows at the nearest; one it's beaten down, it crushes (a grab a group can club it off).</summary>
    void Attack(EnemyContext ctx, GauntTuning t)
    {
        var at = WorldPosition(ctx.Train);
        var near = ctx.LivingCrew().Where(c => (c.World - at).Length <= t.Reach && !c.Player.State.Has(PlayerFlags.Held))
            .OrderBy(c => (c.World - at).Length).Select(c => ((int)c.Player.Id, c.Player.State.Health)).FirstOrDefault();
        _hit += SimConstants.TickSeconds;
        if (near == default || _hit < t.HitEvery)
            return;
        _hit = 0;
        if (near.Health <= t.GrabBelowHealth && Grab(ctx, near.Item1, t.CrushSeconds))
            return;
        ctx.Bite(near.Item1, t.HitDamage, DeathCause.Gaunt);
    }

    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.Gaunt);
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Telegraph);
    }

    protected override void Rescued(EnemyContext ctx, int by)
    {
        base.Rescued(ctx, by);
        Enter(ctx, SpinePhase.Telegraph);
        Enter(ctx, SpinePhase.Commit);
    }
}

/// <summary>
/// FOLLOWERS · scent · outside (GDD v1.1 §21, App. A.6). A hand-sized parasite on the facility grounds that latches onto a
/// player's back, between the shoulder blades: a small lump that twitches (the telegraph). Its host can't see it; their
/// friends can, if they look. It rides aboard with them, harmless to them, then drops off and crawls to the car holding the
/// most loot, builds a nest there over ~60 s, and eats that car's loot steadily. Rule: check each other's backs. A friend
/// clubbing it off your back kills it; or kill it as it crawls, or bludgeon the nest. The loot is the risk, not the host.
/// </summary>
/// <remarks>
/// <see cref="Enemy.Extra"/> is its carrier (on their back, riding), or −1 once it's off; crawling and nesting it's in its
/// car (<see cref="Enemy.Attached"/>) with <see cref="Enemy.Extra2"/> the nest's progress, 0 to 1.
/// </remarks>
public sealed class Follower(int id) : Enemy(id)
{
    int _car = -1;

    public override EnemyKind Kind => EnemyKind.Follower;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Scent;
    public override Want Want => Want.Trust;
    public override double MeleeRadius => 0.5;
    public int Carrier => Extra >= 0 ? (int)Math.Round(Extra) : -1;
    /// <summary>On someone's back: the host (the interest filter) keeps it from its carrier's screen.</summary>
    public bool Riding => Carrier >= 0 && !Gone;
    /// <summary>Its nest built, eating the car's loot.</summary>
    public bool Nested => Phase == SpinePhase.Punish;

    /// <summary>On a player's back.</summary>
    public static Follower On(int id, TrainOnLine train, in PlayerState carrier, int carrierId, FollowerTuning t)
    {
        var f = new Follower(id) { Attached = Loose, Extra = carrierId, Health = t.Health };
        f.Ride(train, carrier);
        return f;
    }

    /// <summary>The crew on the ground without one on them already (App. B.6 "requires an excursion").</summary>
    public static List<int> Excursions(World world) =>
        world.CrewThisTick.Where(c => c.State is { Alive: true, Parent: PlayerState.World })
            .Where(c => !world.ActiveEnemies.Any(e => e is Follower f && !f.Gone && f.Carrier == c.Id))
            .Select(c => c.Id).Order().ToList();

    void Ride(TrainOnLine train, in PlayerState s)
    {
        var back = new Double3(DMath.Sin(s.Yaw), 0, DMath.Cos(s.Yaw)) * 0.2 + Double3.Up * 1.35;
        if (s.Parent >= 0)
        {
            Attached = s.Parent;
            Local = s.Position + back;
        }
        else
        {
            Attached = Loose;
            double yaw = PlayerMotor.WorldYaw(s, train);
            Local = PlayerMotor.WorldPosition(s, train) + new Double3(DMath.Sin(yaw), 0, DMath.Cos(yaw)) * 0.2 + Double3.Up * 1.35;
        }
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Followers;
        var train = ctx.Train;
        if (PhaseSeconds >= t.LingerSeconds && Phase != SpinePhase.Punish)
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        switch (Phase)
        {
            case SpinePhase.Dormant:
                // LATCH: onto its carrier's back (the lump that twitches).
                if (PhaseSeconds >= t.LatchSeconds)
                    Enter(ctx, SpinePhase.Telegraph);
                goto case SpinePhase.Telegraph;
            case SpinePhase.Telegraph:
                {
                    if (ctx.Crew.FirstOrDefault(c => c.Player.Id == Carrier).Player.State is not { Alive: true } s || Carrier < 0)
                    {
                        Enter(ctx, SpinePhase.Gone);
                        return;
                    }
                    Ride(train, s);
                    // RIDE until aboard; then DROP, and make for the car with the most loot.
                    if (Phase == SpinePhase.Telegraph && s.Parent >= 0 && train.Dynamics.Consist.IndexOf(s.Parent) >= 0
                        && PhaseSeconds >= ctx.Tuning.MinReactionSeconds && Richest(ctx) is { } car && Enter(ctx, SpinePhase.Commit))
                    {
                        _car = car;
                        Extra = -1;
                    }
                    return;
                }
            case SpinePhase.Commit:
                Crawl(ctx, t);
                return;
            case SpinePhase.Punish:
                {
                    if (_car < 0 || _car >= train.Frames.Count || train.Dynamics.Consist.IndexOf(_car) < 0)
                    {
                        Enter(ctx, SpinePhase.Gone);
                        return;
                    }
                    var v = train.Vehicles[_car];
                    v.CargoIntegrity = Math.Max(0, v.CargoIntegrity - t.EatPerSecond * SimConstants.TickSeconds);
                    return;
                }
            default:
                Enter(ctx, SpinePhase.Gone);
                return;
        }
    }

    /// <summary>The cargo car in the engine's rake holding the most (freight still worth something, and hand loot left in it).</summary>
    static int? Richest(EnemyContext ctx)
    {
        var train = ctx.Train;
        return train.Dynamics.Consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo && train.Frames[v.Id].Shape.Interior is not null)
            .Select(v => (v.Id, Worth: v.Load * v.CargoIntegrity + ctx.World.Bodies.All.Where(b => b.Parent == v.Id).Sum(b => Bodies.Value(b.Kind))))
            .Where(x => x.Worth > 0.01).OrderByDescending(x => x.Worth).ThenBy(x => x.Id).Select(x => (int?)x.Id).FirstOrDefault();
    }

    /// <summary>Off its host and along the train to its car, then the nest.</summary>
    void Crawl(EnemyContext ctx, FollowerTuning t)
    {
        var train = ctx.Train;
        if (_car < 0 || _car >= train.Frames.Count || train.Dynamics.Consist.IndexOf(_car) < 0)
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        var goal = train.Frames[_car].Shape.Interior!.Value;
        var target = train.Frames[_car].ToWorld(goal.Centre with { Y = goal.Min.Y });
        var here = WorldPosition(train);
        var to = target - here;
        double step = t.CrawlSpeed * SimConstants.TickSeconds;
        if (to.Length > 0.5 && Attached != _car)
        {
            Attached = Loose;
            Local = here + to.Normalized * Math.Min(step, to.Length);
            // Close enough to be in its car: its frame now.
            if ((Local - target).Length <= 1.5)
            {
                Attached = _car;
                Local = goal.Centre with { Y = goal.Min.Y };
            }
            return;
        }
        Attached = _car;
        // NEST: built over a minute, then it eats. A nest takes a few blows.
        Extra2 = Math.Min(1, Extra2 + SimConstants.TickSeconds / t.NestSeconds);
        if (Extra2 >= 1)
        {
            Health = t.NestHealth;
            Enter(ctx, SpinePhase.Punish);
        }
    }

    /// <summary>Clubbed off a friend's back, as it crawls, or its nest beaten in: any blow that finishes it.</summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        // Its carrier can't reach round and hit it: only a friend can (App. A.6).
        if (by == Carrier)
            return;
        base.Struck(ctx, by, damage);
    }
}

/// <summary>
/// SOOT CHILDREN · sound · outside (GDD v1.1 §21, App. A.6). A child's voice calling for help from the dark, near facilities
/// and dead towns. Half the time it's a real survivor, the most valuable cargo in the game (carried by hand to a cargo car);
/// half the time it's a Soot Child, with black eyes and blackened hands and feet (the telegraph, seen from five metres).
/// Get within five metres of a Soot Child and it turns visibly inhuman, pins you and drinks: every second it drinks adds to
/// what it takes to kill it, and your cries for help get quieter (App. C.8). Friends kill it, or you're drained. Rule: check
/// the eyes from five metres. A host's first-ever call is always a real child.
/// </summary>
/// <remarks>
/// Loose in the world, standing where it calls from. <see cref="Enemy.Extra2"/> is 1 for a Soot Child, 0 for a real child
/// (replicated: the eyes are drawn from it). <see cref="Enemy.Extra"/> is 1 while it's calling.
/// </remarks>
public sealed class SootChildren(int id) : Enemy(id)
{
    public override EnemyKind Kind => EnemyKind.SootChildren;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Sound;
    public override Want Want => Want.Trust;
    public override double MeleeRadius => Soot && Phase is SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Telegraph ? 0.6 : 0;
    public bool Soot => Extra2 > 0.5;
    public bool Calling => Extra > 0.5;

    public static SootChildren Calls(int id, Double3 world, bool soot, SootChildrenTuning t) =>
        new(id) { Attached = Loose, Local = world, Extra2 = soot ? 1 : 0, Health = t.Health };

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.SootChildren;
        var train = ctx.Train;
        var here = WorldPosition(train);
        Extra = (int)(PhaseSeconds / t.CallEvery) % 2 == 0 && Phase is SpinePhase.Dormant or SpinePhase.Telegraph ? 1 : 0;
        switch (Phase)
        {
            case SpinePhase.Dormant:
                // CALL: the voice from the dark; the eyes and hands are there for anyone who looks (the telegraph).
                Enter(ctx, SpinePhase.Telegraph);
                return;
            case SpinePhase.Telegraph:
                {
                    var near = ctx.LivingCrew().Where(c => CrewSense.OnGround(c.Player.State)).OrderBy(c => (c.World - here).Length).FirstOrDefault();
                    bool someone = ctx.LivingCrew().Any(c => CrewSense.OnGround(c.Player.State) && c.Player.Id == near.Player.Id);
                    if (!someone || (near.World - here).Length > (Soot ? t.LungeWithin : 1.5))
                    {
                        if (PhaseSeconds >= t.IgnoredSeconds)
                            Enter(ctx, SpinePhase.Gone);
                        return;
                    }
                    if (!Soot)
                    {
                        // REAL: a survivor, picked up and carried (it's a body now, the most valuable there is).
                        ctx.World.Bodies.SpawnItem(here, train.Dynamics.Distance, BodyKind.Child);
                        Enter(ctx, SpinePhase.BreakOff);
                        Enter(ctx, SpinePhase.Gone);
                        return;
                    }
                    // LUNGE: inside five metres, it's on them.
                    if (PhaseSeconds >= ctx.Tuning.MinReactionSeconds && Enter(ctx, SpinePhase.Commit))
                    {
                        Local = near.World;
                        Grab(ctx, near.Player.Id, t.DrainSeconds);
                    }
                    return;
                }
            case SpinePhase.Grab:
                // Every second it drinks, it takes more to kill.
                Health += t.HealthPerSecond * SimConstants.TickSeconds;
                if (ctx.Crew.FirstOrDefault(c => c.Player.Id == Holding).Player.State is { Alive: true } held)
                    Local = PlayerMotor.WorldPosition(held, train);
                return;
            default:
                Enter(ctx, SpinePhase.Gone);
                return;
        }
    }

    /// <summary>Only killing it frees them: a blow that doesn't finish it only hurts it (App. A.6 "interrupt: friends kill it").</summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        if (!Soot || MeleeRadius <= 0)
            return;
        Health -= damage;
        if (Health <= 0)
        {
            Release(ctx);
            Enter(ctx, SpinePhase.Gone);
        }
    }

    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.Drained);
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Gone);
    }
}
