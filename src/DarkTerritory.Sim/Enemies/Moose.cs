using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>What the Moose is doing within its spine phase (replicated in <see cref="Enemy.Height"/>), for its clips and cues.</summary>
public enum MooseMode : byte { Graze, Home, Hunt, Search, Ram, SquareUp, Charge, Wheel, Snag, Pin }

/// <summary>
/// THE MOOSE · movement (trespass), sound · outside (GDD §21, App. A.6, B.6; ARCHITECTURE §8 note 311; the director's
/// decisions of 7 Oct 2026, docs/design/creatures/moose.md). A hyper-aggressive, territorial bull moose, too big to get on
/// the train. It grazes beside the line and at the stops, docile, and leaves be whoever leaves it be. Its temper is a meter:
/// crowding it, talking near it and a passing train fill it; a hit fills it at once. Listening (head up, ears turning) and
/// warning (ears flat, the sac ridge up) come first; back off and hush and it settles. Full, it goes for whoever put the
/// most in (whoever hit it last, from then on): squares up (the telegraph, its heading locked at the end) and charges in a
/// straight line it can't turn out of. Anything narrower than its rack it can't get into: a doorway, a coupling gap, the
/// alleys between buildings. Run into a building, it's snagged a while. A charge is a heavy hit; on someone already hurt it
/// pins them, and a friend's blow takes it off them (they're its target then). Out of sight it searches where it lost
/// them, and a voice near it gives anyone away; lost at a car, it rams the car a while (only a ram: a boom and a shudder).
/// It gives up when it can't find them, when they're far enough from its ground, or when the train pulls away. Nothing
/// kills it. It never sets foot within <see cref="MooseTuning.TrackClearance"/> of a track: it's never in the train's way.
/// Rule: give it room, keep it quiet.
/// </summary>
/// <remarks>
/// Loose in the world at <see cref="Enemy.Local"/>. <see cref="Enemy.Extra"/> is its target (−1 none), <see cref="Enemy.Extra2"/>
/// its aggro (0–100), <see cref="Enemy.Lateral"/> its heading (world yaw, as a player's), <see cref="Enemy.Height"/> its
/// <see cref="MooseMode"/>, and <see cref="Enemy.LineDistance"/> its ground's line hint. Its home and what it's after are the
/// host's own.
/// </remarks>
public sealed class Moose(int id) : Enemy(id)
{
    Double3 _home, _lastSeen;
    double _modeSeconds, _charged, _chargeLength, _nextRam;
    int _blocked, _ramCar = -1;
    bool _passed, _located = true;
    readonly HashSet<int> _hitThisCharge = [];
    // Who put how much into the meter (App. A.6's "whoever put the most in"), by player id, in the crew's order.
    readonly SortedDictionary<int, double> _put = [];

    public override EnemyKind Kind => EnemyKind.Moose;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Movement;
    public override Want Want => Want.Split;
    public override double MeleeRadius => 2.0;

    public MooseMode Mode => (MooseMode)(int)Height;
    public int? Target => Extra >= 0 ? (int)Extra : null;
    public double Aggro => Extra2;
    /// <summary>Where it heads: the world yaw it faces (−Z at 0, as a player's).</summary>
    public double Yaw => Lateral;
    public static Double3 Facing(double yaw) => new(-DMath.Sin(yaw), 0, -DMath.Cos(yaw));
    public Double3 Home => _home;
    /// <summary>Host: the rams it's made at a car (each only a boom and a shudder: nothing in or on the car is touched).</summary>
    public int Rams { get; private set; }

    public static Moose Grazing(int id, Double3 world, double lineHint, double yaw) =>
        new(id) { Attached = Loose, Local = world, Extra = -1, Lateral = yaw, LineDistance = lineHint, _home = world };

    /// <summary>Already riled by <paramref name="target"/> (a test's, an audit's): it goes for them from where it stands.</summary>
    public static Moose Enraged(int id, Double3 world, double lineHint, int target)
    {
        var m = Grazing(id, world, lineHint, 0);
        m.Restore(SpinePhase.Alert, 0, 1, Loose, world, lineHint, 0, (double)MooseMode.Hunt, target, 100);
        m._located = false;
        return m;
    }

    void SetMode(MooseMode mode)
    {
        if (Mode != mode)
            _modeSeconds = 0;
        Height = (double)mode;
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Moose;
        var train = ctx.Train;
        double dt = SimConstants.TickSeconds;
        _modeSeconds += dt;
        // Put down on a car or the line (a test's staging): it stands in the world where that is, on its own feet.
        if (Attached != Loose)
        {
            Place(ctx, WorldPosition(train));
            Attached = Loose;
            _home = Local;
        }
        // Left behind by the train: gone.
        if (train.Frames.Count == 0 || train.Frames.Min(f => Flat(f.Origin - Local)) > t.GoneBeyond)
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        bool moving = train.Dynamics.Speed > 1.0;
        // The train pulling away ends it (the director, 7 Oct 2026: "or you drive the train away").
        if (moving && Target is not null && Phase != SpinePhase.Grab)
        {
            Calm(ctx);
            return;
        }
        switch (Phase)
        {
            case SpinePhase.Dormant or SpinePhase.Alert or SpinePhase.BreakOff when Target is null:
                Temper(ctx, t, moving);
                return;
            case SpinePhase.Alert:
                Hunt(ctx, t);
                return;
            case SpinePhase.Telegraph:
                SquareUp(ctx, t);
                return;
            case SpinePhase.Commit:
                Charge(ctx, t);
                return;
            case SpinePhase.Grab:
                SetMode(MooseMode.Pin);
                return;
            case SpinePhase.Punish:
                return;
            default:
                Calm(ctx);
                return;
        }
    }

    /// <summary>
    /// Its temper, unriled (App. A.6 AGGRO): crowding, voices and the passing train fill the meter; given room and quiet it
    /// falls. Grazing, listening, warning; at full, it picks its target.
    /// </summary>
    void Temper(EnemyContext ctx, MooseTuning t, bool moving)
    {
        double dt = SimConstants.TickSeconds;
        var here = Local;
        bool any = false;
        foreach (var (p, intent) in ctx.Crew)
        {
            if (!p.State.Alive)
                continue;
            var at = PlayerMotor.WorldPosition(p.State, ctx.Train);
            double d = Flat(at - here);
            double add = 0;
            // Crowding is what it sees (round a corner or behind the train, you're not crowding it); voices it hears anyway.
            bool crowds = CrewSense.OnGround(p.State) && d <= t.CrowdAt && Clear(ctx.Train, here + Double3.Up * 1.8, at + Double3.Up * 1.2);
            if (crowds && d <= t.CloseAt)
                add += t.ClosePerSecond;
            else if (crowds)
                add += t.CrowdPerSecond;
            if (intent.Voice >= t.TalkingAbove && d <= t.HearVoice)
                add += t.VoicePerSecond * intent.Voice / 255.0;
            if (add <= 0)
                continue;
            any = true;
            _put[p.Id] = _put.GetValueOrDefault(p.Id) + add * dt;
            Extra2 = Math.Min(100, Extra2 + add * dt);
        }
        // The passing train: a rival going by, once a pass.
        double nearCar = ctx.Train.Frames.Min(f => Flat(f.Origin - here) - f.Shape.HalfLength);
        if (moving && nearCar <= t.TrainPassAt && !_passed)
        {
            _passed = true;
            Extra2 = Math.Min(100, Extra2 + t.TrainPass);
        }
        else if (!moving || nearCar > t.TrainPassAt + 10)
            _passed = false;
        if (!any)
        {
            Extra2 = Math.Max(0, Extra2 - t.CalmPerSecond * dt);
            if (Extra2 <= 0)
                _put.Clear();
        }
        if (Extra2 >= 100 && _put.Count > 0)
        {
            // Whoever put the most in; the lowest id on a tie (the crew's order, never a dictionary's).
            int who = _put.OrderByDescending(k => k.Value).ThenBy(k => k.Key).First().Key;
            Rile(ctx, who);
            return;
        }
        if (Phase == SpinePhase.BreakOff)
        {
            // Going home: back to its ground, then grazing.
            SetMode(MooseMode.Home);
            if (Walk(ctx, t, _home, t.SearchSpeed) <= 1.0)
            {
                Enter(ctx, SpinePhase.Dormant);
                SetMode(MooseMode.Graze);
            }
            return;
        }
        if (Phase == SpinePhase.Dormant && Extra2 >= t.ListenAt)
            Enter(ctx, SpinePhase.Alert);
        else if (Phase == SpinePhase.Alert && Extra2 < t.ListenAt * 0.5)
            Enter(ctx, SpinePhase.Dormant);
        SetMode(MooseMode.Graze);
        // Listening: its head to whoever's put the most in lately.
        if (Phase == SpinePhase.Alert && _put.Count > 0)
        {
            int who = _put.OrderByDescending(k => k.Value).ThenBy(k => k.Key).First().Key;
            if (Where(ctx, who) is { } at)
                Turn(at - here, Math.PI);
        }
    }

    /// <summary>Riled: it's after <paramref name="who"/> from now, its meter full.</summary>
    void Rile(EnemyContext ctx, int who)
    {
        Extra = who;
        Extra2 = 100;
        _blocked = 0;
        _ramCar = -1;
        if (Where(ctx, who) is { } at)
            _lastSeen = at;
        if (Phase is SpinePhase.Dormant or SpinePhase.BreakOff)
            Enter(ctx, SpinePhase.Alert);
        SetMode(MooseMode.Hunt);
    }

    /// <summary>
    /// After its target (App. A.6 ENRAGED): on the ground and in sight, it closes and squares up; aboard a car it lost them
    /// at, it rams the car; out of sight, it searches where it lost them. It gives up past its leash, after its search or its
    /// rams, or after charging into what it can't get into time after time.
    /// </summary>
    void Hunt(EnemyContext ctx, MooseTuning t)
    {
        int who = (int)Extra;
        var me = ctx.Crew.FirstOrDefault(c => c.Player.Id == who);
        if (me.Player.State is not { Alive: true } s || ctx.Crew.All(c => c.Player.Id != who))
        {
            Calm(ctx);
            return;
        }
        var at = PlayerMotor.WorldPosition(s, ctx.Train);
        // Riled from outside (a test's, an audit's): it knows where they were when it was.
        if (!_located)
        {
            _lastSeen = at;
            _located = true;
        }
        if (Flat(at - _home) > t.LeashRadius || _blocked >= t.BlockedCharges)
        {
            Calm(ctx);
            return;
        }
        bool seen = Sees(ctx, t, s, at);
        if (seen)
            _lastSeen = at;
        // A voice near it gives anyone away (the director, 7 Oct 2026): whoever it hears is who it's after now.
        else if (Heard(ctx, t) is { } heard && heard.Id != who)
        {
            Rile(ctx, heard.Id);
            _lastSeen = heard.At;
            return;
        }
        else if (Heard(ctx, t) is { } same)
        {
            _lastSeen = same.At;
            if (Mode == MooseMode.Search)
                _modeSeconds = 0;
        }
        if (seen)
        {
            double d = Flat(at - Local);
            if (d <= t.SquareUpAt[1])
            {
                SetMode(MooseMode.SquareUp);
                Enter(ctx, SpinePhase.Telegraph);
                return;
            }
            SetMode(MooseMode.Hunt);
            Walk(ctx, t, at, t.HuntSpeed);
            // In sight but no way to them (round the far side of what it can't get past): it gives up in a search's time.
            if (_modeSeconds >= t.SearchSeconds)
                Calm(ctx);
            return;
        }
        // Aboard a car it lost them at: the ram (a sound and a shudder, nothing more).
        if (s.Parent >= 0 && s.Parent < ctx.Train.Frames.Count && (_ramCar == s.Parent || NearCar(ctx.Train.Frames[s.Parent], _lastSeen) <= t.LostAtCar))
        {
            if (_ramCar != s.Parent)
            {
                _ramCar = s.Parent;
                SetMode(MooseMode.Ram);
                _nextRam = 0;
            }
            var frame = ctx.Train.Frames[_ramCar];
            var side = frame.ToWorld(new Double3(Math.Sign(Double3.Dot(Local - frame.Origin, frame.Right)) * (frame.Shape.HalfWidth + t.RackSpan / 2 + 0.2), 0,
                Math.Clamp(frame.ToLocal(Local).Z, -frame.Shape.HalfLength + 1, frame.Shape.HalfLength - 1)));
            if (Walk(ctx, t, side, t.HuntSpeed) <= 1.5)
            {
                Turn(frame.Origin - Local, Math.PI);
                if (_modeSeconds >= _nextRam)
                {
                    _nextRam = _modeSeconds + t.RamEvery;
                    Rams++;
                }
            }
            if (_modeSeconds >= t.RamSeconds)
                Calm(ctx);
            return;
        }
        // Lost them: where it last saw or heard them, head sweeping.
        if (Mode != MooseMode.Search)
            SetMode(MooseMode.Search);
        Walk(ctx, t, _lastSeen, t.SearchSpeed);
        if (_modeSeconds >= t.SearchSeconds)
            Calm(ctx);
    }

    /// <summary>
    /// SQUARE UP (the telegraph): ears flat, ridge up, the rack levelled at them, two stamps. Its heading locks at the end and
    /// it charges where they were then.
    /// </summary>
    void SquareUp(EnemyContext ctx, MooseTuning t)
    {
        SetMode(MooseMode.SquareUp);
        if (Target is not { } who || Where(ctx, who) is not { } at)
        {
            Enter(ctx, SpinePhase.Alert);
            return;
        }
        double d = Flat(at - Local);
        if (d > t.SquareUpAt[1] * 1.5)
        {
            Enter(ctx, SpinePhase.Alert);
            SetMode(MooseMode.Hunt);
            return;
        }
        Turn(at - Local, Math.PI);
        if (PhaseSeconds >= Math.Max(t.SquareUpSeconds, ctx.Tuning.MinReactionSeconds) && Enter(ctx, SpinePhase.Commit))
        {
            var to = (at - Local) with { Y = 0 };
            if (to.Length > 1e-6)
                Lateral = DMath.Atan2(-to.X, -to.Z);
            _charged = 0;
            _chargeLength = d + t.Overrun;
            _hitThisCharge.Clear();
            SetMode(MooseMode.Charge);
        }
    }

    /// <summary>
    /// CHARGE: a straight line it can't turn out of. Whoever on the ground it runs through takes a heavy hit, and someone
    /// already hurt is pinned. A building stops it with its rack jammed (snagged); a car, or the edge of the track's
    /// clearance, pulls it up. Then it wheels round.
    /// </summary>
    void Charge(EnemyContext ctx, MooseTuning t)
    {
        double dt = SimConstants.TickSeconds;
        switch (Mode)
        {
            case MooseMode.Charge:
                {
                    var dir = Facing(Lateral);
                    double step = t.ChargeSpeed * dt;
                    var next = Local + dir * step;
                    var blocked = Blocked(ctx, t, next);
                    if (blocked != Obstacle.None)
                    {
                        _blocked++;
                        SetMode(blocked == Obstacle.Wall ? MooseMode.Snag : MooseMode.Wheel);
                        return;
                    }
                    Place(ctx, next);
                    _charged += step;
                    foreach (var (p, _) in ctx.Crew)
                    {
                        if (!CrewSense.OnGround(p.State) || _hitThisCharge.Contains(p.Id))
                            continue;
                        var at = PlayerMotor.WorldPosition(p.State, ctx.Train);
                        var off = (at - Local) with { Y = 0 };
                        if (off.Length > t.RackSpan / 2 + t.HitReach || Math.Abs(at.Y - Local.Y) > 2.5 || Double3.Dot(off, dir) < -0.5)
                            continue;
                        _hitThisCharge.Add(p.Id);
                        if (p.Id == Target)
                            _blocked = 0;
                        if (p.State.Health <= t.GrabBelowHealth && Grab(ctx, p.Id, t.PinSeconds))
                        {
                            SetMode(MooseMode.Pin);
                            return;
                        }
                        ctx.Bite(p.Id, t.ChargeDamage, DeathCause.Trampled);
                    }
                    if (_charged >= _chargeLength)
                        SetMode(MooseMode.Wheel);
                    return;
                }
            case MooseMode.Snag:
                if (_modeSeconds >= t.SnagSeconds)
                    SetMode(MooseMode.Wheel);
                return;
            default:
                {
                    // The skid and wheel round, toward where it last had them.
                    SetMode(MooseMode.Wheel);
                    if (Target is { } who && Where(ctx, who) is { } at)
                        Turn(at - Local, Math.PI * 0.6);
                    if (_modeSeconds < t.WheelSeconds)
                        return;
                    if (Target is { } w && ctx.Crew.FirstOrDefault(c => c.Player.Id == w).Player.State is { Alive: true } s
                        && PlayerMotor.WorldPosition(s, ctx.Train) is var pos && Sees(ctx, t, s, pos) && Flat(pos - Local) <= t.SquareUpAt[1])
                    {
                        _lastSeen = pos;
                        Enter(ctx, SpinePhase.Telegraph);
                        SetMode(MooseMode.SquareUp);
                        return;
                    }
                    Enter(ctx, SpinePhase.Alert);
                    SetMode(MooseMode.Hunt);
                    return;
                }
        }
    }

    /// <summary>Gives up: its meter emptied, back home to graze (it can be riled again the same way).</summary>
    void Calm(EnemyContext ctx)
    {
        Extra = -1;
        Extra2 = 0;
        _put.Clear();
        _blocked = 0;
        _ramCar = -1;
        Enter(ctx, SpinePhase.BreakOff);
        SetMode(MooseMode.Home);
    }

    /// <summary>A blow or a ball (App. C.2, note 290): it can't be hurt, but whoever struck it is its target now, and a friend's blow takes it off whoever it's pinning.</summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        base.Struck(ctx, by, 0);
        if (Gone || by < 0 || by == Holding || Phase is SpinePhase.Grab or SpinePhase.Punish)
            return;
        _put[by] = _put.GetValueOrDefault(by) + 100;
        if (Target == by)
            return;
        switch (Phase)
        {
            case SpinePhase.Commit:
                // Mid-charge it can't turn: it goes for them once it's wheeled round.
                Extra = by;
                return;
            case SpinePhase.Telegraph:
                // Squaring up at someone else: it squares up again, at them, from the start (the telegraph is theirs too).
                Enter(ctx, SpinePhase.Alert);
                Rile(ctx, by);
                return;
            default:
                Rile(ctx, by);
                return;
        }
    }

    /// <summary>A friend's blow broke its pin: it lets go, and goes for them.</summary>
    protected override void Rescued(EnemyContext ctx, int by)
    {
        base.Rescued(ctx, by);
        if (by >= 0 && by != Target && ctx.Crew.Any(c => c.Player.Id == by && c.Player.State.Alive))
            Rile(ctx, by);
        else
        {
            Enter(ctx, SpinePhase.Alert);
            SetMode(MooseMode.Hunt);
        }
    }

    /// <summary>PUNISH: trampled; then it's done with them and goes home.</summary>
    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.Trampled);
        Calm(ctx);
    }

    /// <summary>On the ground, within its sight, and nothing between (a building, a car).</summary>
    bool Sees(EnemyContext ctx, MooseTuning t, in PlayerState s, Double3 at) =>
        CrewSense.OnGround(s) && Flat(at - Local) <= t.SightRange && Clear(ctx.Train, Local + Double3.Up * 1.8, at + Double3.Up * 1.2);

    /// <summary>The loudest voice within its hearing, and where they are.</summary>
    (int Id, Double3 At)? Heard(EnemyContext ctx, MooseTuning t)
    {
        (int Id, Double3 At)? best = null;
        int loudest = t.TalkingAbove - 1;
        foreach (var (p, intent) in ctx.Crew)
        {
            if (!p.State.Alive || intent.Voice <= loudest)
                continue;
            var at = PlayerMotor.WorldPosition(p.State, ctx.Train);
            if (Flat(at - Local) > t.HearVoice)
                continue;
            loudest = intent.Voice;
            best = (p.Id, at);
        }
        return best;
    }

    static Double3? Where(EnemyContext ctx, int who) =>
        ctx.Crew.FirstOrDefault(c => c.Player.Id == who).Player.State is { Alive: true } s && ctx.Crew.Any(c => c.Player.Id == who)
            ? PlayerMotor.WorldPosition(s, ctx.Train) : null;

    /// <summary>Turns toward a direction at up to <paramref name="rate"/> radians a second.</summary>
    void Turn(Double3 to, double rate)
    {
        to = to with { Y = 0 };
        if (to.Length < 1e-6)
            return;
        double want = DMath.Atan2(-to.X, -to.Z);
        double diff = Math.IEEERemainder(want - Lateral, 2 * Math.PI);
        double max = rate * SimConstants.TickSeconds;
        Lateral = Math.IEEERemainder(Lateral + Math.Clamp(diff, -max, max), 2 * Math.PI);
    }

    /// <summary>
    /// Walks toward a point, sliding round what it can't get through (it tries either side of straight on); returns how far
    /// it still has to go.
    /// </summary>
    double Walk(EnemyContext ctx, MooseTuning t, Double3 to, double speed)
    {
        var flat = (to - Local) with { Y = 0 };
        double left = flat.Length;
        if (left <= 0.5)
            return left;
        double step = Math.Min(speed * SimConstants.TickSeconds, left);
        double straight = DMath.Atan2(-flat.X, -flat.Z);
        foreach (double turn in Swerves)
        {
            double yaw = straight + turn;
            var next = Local + Facing(yaw) * step;
            if (Blocked(ctx, t, next) != Obstacle.None)
                continue;
            Place(ctx, next);
            Turn(Facing(yaw), Math.PI);
            return left - step;
        }
        Turn(flat, Math.PI);
        return left;
    }

    static readonly double[] Swerves = [0, 0.5, -0.5, 1.0, -1.0, 1.5, -1.5];

    void Place(EnemyContext ctx, Double3 at)
    {
        double hint = LineDistance;
        Local = at with { Y = PlayerMotor.GroundAt(at, ctx.Train.Line, ref hint) };
        LineDistance = hint;
    }

    enum Obstacle : byte { None, Track, Wall, Car }

    /// <summary>
    /// What stops its body (its rack's span across) at a point: within the track's clearance (never on the rail, the
    /// director's rule; further off one the train's moving on), a stop building's wall, or a car.
    /// </summary>
    Obstacle Blocked(EnemyContext ctx, MooseTuning t, Double3 at) => Blocks(ctx.Train, t, at, LineDistance);

    static Obstacle Blocks(TrainOnLine train, MooseTuning t, Double3 at, double hint)
    {
        double r = t.RackSpan / 2;
        if (TrackOff(train, at, hint) < (train.Dynamics.Speed > 1.0 ? t.MovingClearance : t.TrackClearance))
            return Obstacle.Track;
        if (train.Walls is { } walls)
            foreach (var w in walls.Near(at))
            {
                var local = w.ToLocal(at);
                double dx = Math.Max(0, Math.Abs(local.X) - w.HalfLength), dz = Math.Max(0, Math.Abs(local.Z) - w.HalfWidth);
                if (dx * dx + dz * dz < r * r)
                    return Obstacle.Wall;
            }
        foreach (var f in train.Frames)
            if (NearCar(f, at) < r)
                return Obstacle.Car;
        return Obstacle.None;
    }

    /// <summary>Whether a point is somewhere a moose may stand (the spawns' placement, the tests).</summary>
    public static bool Free(TrainOnLine train, MooseTuning t, Double3 at, double hint) => Blocks(train, t, at, hint) == Obstacle.None;

    /// <summary>
    /// Where a moose may be put down near a point: on the ground (not under water), clear of the tracks, the stop buildings
    /// and the cars, and outside the forts; null if it can't stand there.
    /// </summary>
    public static Double3? Place(World world, MooseTuning t, Double3 at, double hint)
    {
        var train = world.Train;
        double h = hint;
        double ground = PlayerMotor.GroundAt(at, train.Line, ref h);
        var spot = at with { Y = ground };
        if (Combat.Guns.Water(train, spot) is { } w && w > ground + 0.4)
            return null;
        if (TrackOff(train, spot, h) < Math.Max(t.TrackClearance, t.MovingClearance) || !Free(train, t, spot, h) || world.InFort(spot))
            return null;
        return spot;
    }

    /// <summary>The line's biome's weight where it is (<see cref="MooseTuning.BiomeWeights"/>; 1 where unlisted or unknown).</summary>
    public static double BiomeWeight(World world, MooseTuning t, double along)
    {
        if (world.Route?.Plan?.Biomes is not { Count: > 0 } biomes)
            return 1;
        var here = biomes.FirstOrDefault(b => b.Edge == "main" && b.S0 <= along && along < b.S1) ?? biomes[^1];
        return t.BiomeWeights.GetValueOrDefault(here.Biome, 1);
    }

    /// <summary>How far a point is (level) from the nearest track's centre: the main line's or a branch's.</summary>
    public static double TrackOff(TrainOnLine train, Double3 at, double hint)
    {
        var (path, along) = train.Line.Nearest(at, ref hint);
        return Flat(train.Line.Sample(path, along).Position - at);
    }

    /// <summary>How far a point is (level) from a car's box.</summary>
    static double NearCar(in CarFrame f, Double3 at)
    {
        var local = f.ToLocal(at);
        double dx = Math.Max(0, Math.Abs(local.X) - f.Shape.HalfWidth), dz = Math.Max(0, Math.Abs(local.Z) - f.Shape.HalfLength);
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// Nothing solid between two points: no stop building's wall and no car (each sampled every half metre), so a crewmate
    /// round a corner or behind the train is out of its sight.
    /// </summary>
    public static bool Clear(TrainOnLine train, Double3 from, Double3 to)
    {
        var d = to - from;
        double length = d.Length;
        int steps = Math.Max(1, (int)(length / 0.5));
        for (int i = 1; i < steps; i++)
        {
            var p = from + d * ((double)i / steps);
            if (train.Walls is { } walls)
                foreach (var w in walls.Near(p))
                {
                    var local = w.ToLocal(p);
                    if (Math.Abs(local.X) <= w.HalfLength && Math.Abs(local.Z) <= w.HalfWidth && p.Y >= w.Bottom && p.Y <= w.Top)
                        return false;
                }
            foreach (var f in train.Frames)
            {
                var local = f.ToLocal(p);
                if (Math.Abs(local.X) <= f.Shape.HalfWidth && Math.Abs(local.Z) <= f.Shape.HalfLength && local.Y >= -0.6 && local.Y <= f.Shape.RoofHeight)
                    return false;
            }
        }
        return true;
    }

    static double Flat(Double3 v) => Math.Sqrt(v.X * v.X + v.Z * v.Z);
}
