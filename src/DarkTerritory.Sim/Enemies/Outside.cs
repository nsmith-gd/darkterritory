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
        var (way, apart) = Place(target.World, members, leader, t);
        switch (Phase)
        {
            case SpinePhase.Dormant:
                Hop(way, apart, t.HopSpeed, members, t.Spacing);
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
                {
                    Grab(ctx, who, t.DevourSeconds);
                    // And the rest of the pack's tongues are out to them too, and in they hop to eat (App. A.6: "their
                    // tongues freeze the target in place while the pack hops in to eat"; note 558: they'd stayed lined up
                    // where they swelled).
                    foreach (var m in members)
                        if (m != this && m.Phase == SpinePhase.Telegraph)
                            m.Enter(ctx, SpinePhase.Commit);
                }
                return;
            case SpinePhase.Grab:
                // Friends arriving even the count: the tongue lets go.
                if (CrewSense.Group(ctx, who, t.GroupRadius).Count >= size)
                {
                    Rescued(ctx, -1);
                    return;
                }
                Hop(way, apart, t.HopSpeed * 0.25, members, t.Spacing);
                return;
            default:
                // The pack's slow hop in on a frozen target; let go of (a friend evened the count, they struggled free), the
                // rest of the pack breaks off with the leader.
                if (leader != this && Phase == SpinePhase.Commit && leader.Phase is not (SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish))
                {
                    Enter(ctx, SpinePhase.BreakOff);
                    Enter(ctx, SpinePhase.Dormant);
                    return;
                }
                Hop(way, apart, leader.Phase == SpinePhase.Grab ? t.HopSpeed * 0.5 : t.HopSpeed, members, t.Spacing);
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

    /// <summary>
    /// Where this one makes for (note 558; the director, 9 Oct: "they overlapped each other a lot"), and the push off any
    /// packmate too near. Each hopped straight at the catch and nothing kept them apart, so a pack closed into one heap.
    /// Now the leader comes straight in and the rest fan out either side of it, <c>fan</c>° a place apart round the catch
    /// from the side the pack comes from; within <c>fanFrom</c> m of the catch they make for those places,
    /// <c>ringOut</c> m from it (the leader to the hop's own 0.8); and none comes nearer a packmate than <c>spacing</c>.
    /// (By ids and positions only, so every client's the same.)
    /// </summary>
    (Double3 Way, Double3 Apart) Place(Double3 catchAt, List<Ribbit> members, Ribbit leader, RibbitTuning t)
    {
        var to = (catchAt - Local) with { Y = 0 };
        double d = to.Length;
        var apart = Double3.Zero;
        foreach (var m in members)
        {
            if (m == this)
                continue;
            var off = (Local - m.Local) with { Y = 0 };
            double s = off.Length;
            if (s >= t.Spacing)
                continue;
            var dir = s > 1e-6 ? off * (1 / s) : new Double3(Id < m.Id ? -1 : 1, 0, 0);
            apart += dir * ((t.Spacing - s) / t.Spacing);
        }
        if (this == leader || d < 1e-6)
            return (to, apart);
        // Its place: the leader's in the middle, the rest by id out to either side of it in turn.
        int rank = members.Count(m => m != leader && m.Id < Id);
        double side = (rank / 2 + 1) * (rank % 2 == 0 ? 1 : -1);
        var centre = members.Aggregate(Double3.Zero, (a, m) => a + m.Local) * (1.0 / members.Count);
        var from = (centre - catchAt) with { Y = 0 };
        if (from.Length < 1e-6)
            from = to * -1;
        double a0 = DMath.Atan2(from.X, from.Z) + side * t.Fan * Math.PI / 180;
        var place = catchAt + new Double3(DMath.Sin(a0), 0, DMath.Cos(a0)) * t.RingOut;
        double k = Math.Clamp(1 - (d - t.RingOut) / t.FanFrom, 0, 1);
        var goal = catchAt + (place - catchAt) * k;
        return ((goal - Local) with { Y = 0 }, apart);
    }

    /// <summary>
    /// In bursts: a leap, a sit, averaging <paramref name="average"/>, along <paramref name="to"/> (the leader stopping 0.8 m
    /// short of its catch) and
    /// off any packmate too near (<paramref name="apart"/>, from <see cref="Place"/>); and a leap never lands it nearer a
    /// packmate than <paramref name="spacing"/> (they leap on alternate half seconds, so one would land on a sitter): it
    /// slides round it instead.
    /// </summary>
    void Hop(Double3 to, Double3 apart, double average, List<Ribbit> members, double spacing)
    {
        bool leaping = (int)(PhaseSeconds * 2 + Id) % 2 == 0;
        double step = (leaping ? 2 * average : 0) * SimConstants.TickSeconds;
        if (step <= 0)
            return;
        // The leader's way is to the catch, and it stops 0.8 m short (on them); the rest go to their places.
        double stop = members.MinBy(m => m.Id) == this ? 0.8 : 0.05;
        var dir = (to.Length > stop + 0.1 ? to.Normalized : Double3.Zero) + apart * 4;
        if (dir.Length < 1e-6)
            return;
        double most = to.Length > stop + 0.1 ? Math.Min(step, to.Length - stop) : step * 0.5;
        var next = Local + dir.Normalized * Math.Max(0, most);
        foreach (var m in members)
        {
            if (m == this)
                continue;
            var off = (next - m.Local) with { Y = 0 };
            if (off.Length >= spacing)
                continue;
            // Out to the spacing from it: so it slides round a packmate in its way instead of landing on it.
            var dirOut = off.Length > 1e-6 ? off * (1 / off.Length) : new Double3(Id < m.Id ? -1 : 1, 0, 0);
            next = m.Local + dirOut * spacing + Double3.Up * (next.Y - m.Local.Y);
        }
        Local = next;
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
/// <para>
/// Driven off by its rule (note 288, enemies.json <c>gaunt.drivenOff</c>; the director's clarification of 7 Oct 2026). With
/// <c>talkingCalms</c>, talk brings its anger down as silence raises it (a step every <c>silenceSeconds</c>: GDD Part Eleven
/// Q9's "does talking lower its aggro", answered yes for now); calmed under its threshold it stops attacking and follows
/// again, and kept calm and talked to for <c>talkedDownSeconds</c> it loses interest and leaves, empty-handed (it may wake
/// again somewhere else tonight). Killed only by the crew together while someone keeps talking to it: blows from two or
/// more crewmates inside <c>coordinatedKill</c>'s window, with a voice near it; a blow struck in silence angers it instead.
/// Killed, it drops what it carried and is gone for the night. A lone player's blows only break its crush on a friend.
/// </para>
/// </summary>
/// <remarks>
/// <see cref="Enemy.Extra"/> is its waker (−1 asleep), <see cref="Enemy.Extra2"/> its anger (the lean). Leaving
/// (<see cref="SpinePhase.BreakOff"/>), <see cref="Enemy.Extra"/> is instead the id of the body it's carrying off (−1: it
/// took some of a car's freight, nothing to carry), and loose off the train <see cref="Enemy.LineDistance"/> its ground hint. Talking only stops
/// the anger rising (GDD Part Eleven Q9, open): the tuning flag <c>anyVoiceCounts</c> says whose voice counts.
/// </remarks>
public sealed class Gaunt(int id) : Enemy(id)
{
    double _silence, _hit, _talk, _calm;

    public override EnemyKind Kind => EnemyKind.Gaunt;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Sound;
    public override Want Want => Want.Split;
    public override double MeleeRadius => Phase == SpinePhase.Dormant ? 0 : 0.8;
    /// <summary>Asleep and curled up, or awake and following, or leaving with what it took: a body in the open (note 290).</summary>
    public override bool Exposed => !Gone;
    public override double Stoop(EnemyTuning t) => Phase is SpinePhase.Dormant or SpinePhase.Alert ? t.GauntAsleep : 1;
    public int? Waker => Extra >= 0 ? (int)Extra : null;
    public int Anger => (int)Extra2;

    public static Gaunt Asleep(int id, Double3 world, GauntTuning t) => new(id) { Attached = Loose, Local = world, Extra = -1, Health = t.Health };

    /// <summary>
    /// Already awake and after <paramref name="waker"/> (note 296: one sent at a crewmate left behind): it follows them from
    /// where it's put down, as if they'd come too close to it there.
    /// </summary>
    public static Gaunt WokenBy(int id, Double3 world, int waker, GauntTuning t)
    {
        var g = Asleep(id, world, t);
        g.Restore(SpinePhase.Telegraph, 0, t.Health, Loose, world, 0, 0, 0, waker, 0);
        return g;
    }

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
                    // LISTEN: silence near it, and the anger climbs; a voice holds it where it is (and talks it down: TalkedDown).
                    bool talked = Talked(ctx, t);
                    _silence = talked ? 0 : _silence + SimConstants.TickSeconds;
                    if (_silence >= t.SilenceSeconds)
                    {
                        _silence = 0;
                        Extra2 = Math.Min(t.AttackAt, Extra2 + 1);
                    }
                    if (t.DrivenOff && t.TalkingCalms && TalkedDown(ctx, t, talked))
                        return;
                    if (Phase == SpinePhase.Telegraph && Extra2 >= t.AttackAt && PhaseSeconds >= ctx.Tuning.MinReactionSeconds)
                        Enter(ctx, SpinePhase.Commit);
                    if (Phase == SpinePhase.Commit)
                        Attack(ctx, t);
                    return;
                }
            case SpinePhase.Grab:
                return;
            case SpinePhase.BreakOff:
                Leave(ctx, t);
                return;
            default:
                Enter(ctx, SpinePhase.Gone);
                return;
        }
    }

    /// <summary>LISTEN: someone (its waker, unless <c>anyVoiceCounts</c>) talking within its listening radius this tick.</summary>
    bool Talked(EnemyContext ctx, GauntTuning t)
    {
        var train = ctx.Train;
        var at = WorldPosition(train);
        int who = Waker ?? -1;
        return ctx.Crew.Any(c => c.Player.State.Alive && c.Intent.Voice >= t.TalkingAbove
            && (t.AnyVoiceCounts || c.Player.Id == who)
            && (PlayerMotor.WorldPosition(c.Player.State, train) - at).Length <= t.ListenRadius);
    }

    /// <summary>
    /// KEEP TALKING (note 288): talk brings its anger down a step every <see cref="GauntTuning.SilenceSeconds"/>; calmed under
    /// its threshold it stops attacking; held calm and talked to for <see cref="GauntTuning.TalkedDownSeconds"/>, it leaves
    /// with nothing. True the tick it goes.
    /// </summary>
    bool TalkedDown(EnemyContext ctx, GauntTuning t, bool talked)
    {
        _talk = talked ? _talk + SimConstants.TickSeconds : 0;
        if (_talk >= t.SilenceSeconds)
        {
            _talk = 0;
            Extra2 = Math.Max(0, Extra2 - 1);
        }
        if (Phase == SpinePhase.Commit && Extra2 < t.AttackAt)
            Enter(ctx, SpinePhase.Telegraph);
        _calm = talked && Extra2 <= 0 && Phase == SpinePhase.Telegraph ? _calm + SimConstants.TickSeconds : Extra2 > 0 ? 0 : _calm;
        if (_calm < t.TalkedDownSeconds)
            return false;
        Extra = -1; // carrying nothing
        ctx.World.DrivenOff.Add(Kind);
        Enter(ctx, SpinePhase.BreakOff);
        return true;
    }

    /// <summary>
    /// LEAVES with it (App. A.6: "it carries the body out at walking pace, in full view, and the crew can still chase it down
    /// before it clears the train"): to its car's nearest door (opening it if it's shut), down beside the car, and straight
    /// off from the train, holding what it took; gone with it once it's <see cref="GauntTuning.ClearedAt"/> from the train.
    /// </summary>
    void Leave(EnemyContext ctx, GauntTuning t)
    {
        var train = ctx.Train;
        double step = t.LeaveSpeed * SimConstants.TickSeconds;
        Double3 way;
        bool low = false;
        if (Attached >= 0)
        {
            if (Attached >= train.Frames.Count)
            {
                Away(ctx);
                return;
            }
            var frame = train.Frames[Attached];
            var shape = frame.Shape;
            var here = Local;
            low = shape.Interior is { } room && room.Contains(here);
            // The nearest way out: a door's middle, or (a car without one) its side.
            var doors = shape.DoorList;
            var door = doors.Count > 0 ? doors.MinBy(d => ((d.Box.Centre - here) with { Y = 0 }).Length) : (Door?)null;
            var exit = door is { } dd ? dd.Box.Centre with { Y = here.Y } : new Double3((here.X >= 0 ? 1 : -1) * shape.HalfWidth, here.Y, here.Z);
            var to = (exit - here) with { Y = 0 };
            way = to.Length > 1e-6 ? to.Normalized : new Double3(1, 0, 0);
            if (door is { } d && to.Length < 1.0 && !train.Vehicles[Attached].DoorOpen(d.Index))
                train.Vehicles[Attached].ToggleDoor(d.Index);
            if (to.Length > step)
                Local = here + way * step;
            else
            {
                // Out through it and down onto the ground beside the car (an end door: off the end).
                bool side = Math.Abs(exit.X) > shape.HalfWidth * 0.6;
                var outward = side ? new Double3(exit.X >= 0 ? 1 : -1, 0, 0) : new Double3(0, 0, exit.Z >= 0 ? 1 : -1);
                var world = frame.ToWorld(exit + outward * 1.0);
                double hint = train.Dynamics.Distance;
                Attached = Loose;
                Local = world with { Y = PlayerMotor.GroundAt(world, train.Line, ref hint) };
                LineDistance = hint;
            }
            Carry(ctx, t, way, low);
            return;
        }
        // Loose: straight off from the nearest car, across the line's way.
        var at = Local;
        var near = train.Frames.MinBy(f => ((f.Origin - at) with { Y = 0 }).Length);
        var away = (at - near.Origin) with { Y = 0 };
        if (away.Length >= t.ClearedAt)
        {
            Away(ctx);
            return;
        }
        double across = Double3.Dot(away, near.Right);
        way = near.Right * (across >= 0 ? 1 : -1);
        var next = at + way * step;
        double ground = LineDistance;
        Local = next with { Y = PlayerMotor.GroundAt(next, train.Line, ref ground) };
        LineDistance = ground;
        Carry(ctx, t, way, false);
    }

    /// <summary>What it took, held under it as it goes (Bodies.TakeAlong), facing the way it's going.</summary>
    void Carry(EnemyContext ctx, GauntTuning t, Double3 way, bool low)
    {
        if (Extra < 0 || ctx.World.Bodies.All.FirstOrDefault(b => b.Id == (int)Extra) is not { } load)
            return;
        // (Its yaw in the frame it's in, as the way it's going is: a car's, or the world's.)
        double yaw = DMath.Atan2(-way.X, -way.Z);
        ctx.World.Bodies.TakeAlong(load, ctx.Train, Id, Attached >= 0 ? Attached : PlayerState.World,
            Local + Double3.Up * (low ? t.CarryLow : t.CarryHigh), yaw);
    }

    /// <summary>Cleared the train: gone for the run, and what it carried with it.</summary>
    void Away(EnemyContext ctx)
    {
        foreach (var b in ctx.World.Bodies.All.Where(b => b.TakenBy == Id).ToList())
            ctx.World.Bodies.Remove(b);
        Enter(ctx, SpinePhase.Gone);
    }

    /// <summary>Killed (or gone some other way) while carrying something off: it's dropped where it is (App. A.6).</summary>
    void DropIfGone(EnemyContext ctx)
    {
        if (!Gone)
            return;
        foreach (var b in ctx.World.Bodies.All.Where(b => b.TakenBy == Id))
            ctx.World.Bodies.LetGo(b);
    }

    /// <summary>
    /// Struck with a tool. Old rule (<c>drivenOff</c> false): hurt by any blow. Note 288: hurt only by the gang (two or more
    /// crewmates inside the window) while someone's talking to it; a blow in silence angers it; a friend's blow breaks its crush.
    /// </summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        var t = ctx.Tuning.Gaunt;
        if (!t.DrivenOff)
        {
            base.Struck(ctx, by, damage);
            DropIfGone(ctx);
            return;
        }
        Marked(ctx, by);
        bool talked = Talked(ctx, t);
        if (talked && Ganged(ctx, except: Holding))
        {
            Health -= damage;
            if (Health <= 0)
            {
                ctx.World.DrivenOff.Remove(Kind);
                Slay(ctx);
                DropIfGone(ctx);
                return;
            }
        }
        else if (!talked && Phase is SpinePhase.Telegraph or SpinePhase.Commit)
            Extra2 = Math.Min(t.AttackAt, Extra2 + 1);
        if (Phase == SpinePhase.Grab && by != Holding)
            Rescued(ctx, by);
    }

    /// <summary>At its waker's back, at arm's length: in their car's frame aboard, loose in the world off it.</summary>
    void Follow(EnemyContext ctx, in PlayerState w, GauntTuning t)
    {
        var train = ctx.Train;
        var behind = new Double3(DMath.Sin(w.Yaw), 0, DMath.Cos(w.Yaw)) * t.FollowAt;
        // Onto the train with them only from at their back: one still walking up after them (note 296: woken from far off)
        // comes the rest of the way on foot, and is left standing if the train pulls away.
        if (w.Parent >= 0 && (PlayerMotor.WorldPosition(w, train) - WorldPosition(train)).Length <= t.FollowAt * 3)
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
        // What's in a shut crew locker it doesn't get at (note 173); an open one's as good as the floor.
        // Never the rescued child (A.6: "cannot be harmed"; note 182): it passes over them for the next best thing.
        var loot = ctx.World.Bodies.All.Where(b => b.Parent == car && b.Carrier < 0 && b.TakenBy < 0 && Bodies.Prey(b) > 0 && (!b.Stowed || vehicle.LockerOpen(b.Locker)))
            .MaxBy(b => Bodies.Prey(b));
        if (loot is null && (vehicle.Load <= 0.01 || vehicle.CargoIntegrity <= 0.01))
            return false;
        // It takes it up and leaves with it (Leave): what was freight is lost here and now; a thing in the hand, a body, is
        // carried out in full view, to be fought for.
        if (loot is null)
            vehicle.CargoIntegrity = Math.Max(0, vehicle.CargoIntegrity - t.LootCargo);
        Extra = loot?.Id ?? -1;
        Enter(ctx, SpinePhase.BreakOff);
        if (loot is not null)
            Carry(ctx, t, new Double3(0, 0, -1), true);
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
    /// <summary>Who brought it aboard on their back (host only: for the report, C.9 "who carried it aboard").</summary>
    int _carriedBy = -1;

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
                        _carriedBy = Carrier;
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
            .Select(v => (v.Id, Worth: v.Load * v.CargoIntegrity + ctx.World.Bodies.All.Where(b => b.Parent == v.Id).Sum(b => Bodies.Prey(b))))
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

    /// <summary>C.9's Followers row (note 190): the nest built, and who carried it aboard.</summary>
    protected override Run.Incident? Punished(EnemyContext ctx) => Run.IncidentLog.Event(ctx.World, Run.IncidentKind.Nest,
        $"Followers nested in car {_car}", _carriedBy, _carriedBy >= 0 ? "Carried aboard by {actor}." : "Nobody seen carrying them aboard.", _car);

    /// <summary>Its carrier can't reach round and hit it: only a friend can (App. A.6).</summary>
    public override bool Strikable(int by) => base.Strikable(by) && by != Carrier;

    /// <summary>Clubbed off a friend's back, as it crawls, or its nest beaten in: any blow that finishes it.</summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
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
