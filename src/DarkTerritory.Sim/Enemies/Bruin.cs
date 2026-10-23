using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>What the Bruin is doing (replicated in <see cref="Enemy.Height"/>), for its clips and cues.</summary>
public enum BruinMode : byte { Come, Feed, Rise, Maul, Stagger, Leave }

/// <summary>
/// THE BRUIN · scent · outside, then interior (GDD §21; the director's playtest of 23 Oct 2026, line 36: "big, kind of scary,
/// that come out of the trees ... they can kill the player pretty easily if they get grabbed ... They can get into cars and
/// cause problems ... relatively hard to take down. Like, stopping in the wild feels like it should feel scary"; Sloe's
/// proposal, docs/design/creatures/bruin.md, note 1004; built by Rook, queue #712, note 1007). What's left of a great bear.
/// With the train stood in the wilderness <c>standSeconds</c> and cargo aboard, it comes out of the trees huffing (the
/// telegraph), to the car with the most cargo, climbs in and feeds (the car's cargo going). It turns to face whoever's
/// nearest within <c>reach</c>; whoever stays in front of it for <c>windupSeconds</c> it rises and charges, and a charge that
/// lands within <c>chargeReach</c> is the maul (a grab: dead in <c>maulSeconds</c>). From the front it barely feels a blow
/// (×<c>frontFactor</c>); from the side or behind a blow staggers it, makes it drop whoever it's mauling, and turns it on the
/// hitter. So one alone is always in front of it; two or three round its sides keep it turning and never let it land a maul.
/// <c>driveOffBlows</c> blows and it leaves the car for the trees; <c>health</c> and it's dead. The train moving off with it
/// aboard, it rides along feeding. It never comes to a moving train. Rule: never alone with it; never face it.
/// </summary>
/// <remarks>
/// Loose (<see cref="Enemy.Local"/> its world position) coming and leaving; in its car (<see cref="Enemy.Attached"/>, its floor
/// in the car's frame) feeding. <see cref="Enemy.Extra"/> is its target (−1 none), <see cref="Enemy.Extra2"/> the car it's after,
/// <see cref="Enemy.Lateral"/> its heading (world yaw, as a player's), <see cref="Enemy.Height"/> its <see cref="BruinMode"/>.
/// Its tree line, wind-up, blows and seconds fed are the host's.
/// </remarks>
public sealed class Bruin(int id) : Enemy(id)
{
    Double3 _home;
    double _windup, _fed, _stagger;
    int _blows;

    public override EnemyKind Kind => EnemyKind.Bruin;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Scent;
    public override Want Want => Want.Cargo;
    public override double MeleeRadius => Gone ? 0 : 1.6;
    /// <summary>A great body in the open or in a car's door: anyone can reach it.</summary>
    public override bool Exposed => !Gone;
    /// <summary>It feeds whether or not anyone's near: the director never dismisses it for want of company.</summary>
    public override bool StaysAboard => true;
    /// <summary>In a car, feeding (note 881): the cargo going, nobody watching or not.</summary>
    public override bool Working(World world) => !Gone && Attached >= 0;

    public BruinMode Mode => (BruinMode)(int)Height;
    public int? Target => Extra >= 0 ? (int)Extra : null;
    public int Car => (int)Math.Round(Extra2);
    public double Yaw => Lateral;
    public bool InCar => Attached >= 0;
    /// <summary>Host: the blows it's taken in its car (toward <c>driveOffBlows</c>).</summary>
    public int Blows => _blows;
    public Double3 Home => _home;

    public static Double3 Facing(double yaw) => new(-DMath.Sin(yaw), 0, -DMath.Cos(yaw));
    static double YawTo(Double3 d) => DMath.Atan2(-d.X, -d.Z);

    /// <summary>One out of the trees at <paramref name="home"/>, for car <paramref name="car"/>: it comes huffing.</summary>
    public static Bruin Out(int id, Double3 home, int car, double health) =>
        new(id) { Attached = Loose, Local = home, _home = home, Extra = -1, Extra2 = car, Height = (double)BruinMode.Come, Health = health };

    /// <summary>
    /// The car it goes for (the brief: "the car with the most of it"): a car with a room to climb into and cargo left in it, the
    /// most cargo first, then the nearest the engine. −1 for none.
    /// </summary>
    public static int CarFor(TrainOnLine train)
    {
        int best = -1;
        double most = 0;
        foreach (var v in train.Dynamics.Consist.Vehicles)
        {
            if (v.Id == 0 || v.Cargo == CargoKind.None || v.Taken || train.Frames[v.Id].Shape.Interior is null)
                continue;
            double has = train.Vehicles[v.Id].CargoIntegrity;
            if (has > most + 1e-9)
            {
                most = has;
                best = v.Id;
            }
        }
        return best;
    }

    /// <summary>
    /// The train stood in the wilderness long enough for one to come (note 1007): stood (the director's stance, note 999), out of
    /// the forts, at no stop, halt or facility, for <c>standSeconds</c>.
    /// </summary>
    public static bool Wild(SpawnContext c) =>
        c.Director.Stance == StanceKind.Wilderness && c.Director.StoodSeconds >= c.Tuning.Bruin.StandSeconds && !c.World.TrainInFort;

    /// <summary>Its tree line for <paramref name="car"/>: <c>comeFromM</c> out to <paramref name="side"/> (±1) of it, on the ground, out of anything solid.</summary>
    public static Double3 TreeLine(World world, int car, int side, BruinTuning t)
    {
        var train = world.Train;
        var frame = train.Frames[car];
        var spot = frame.ToWorld(new Double3(side * (frame.Shape.HalfWidth + t.ComeFromM), 0, 0));
        double hint = train.Dynamics.Distance;
        spot = spot with { Y = PlayerMotor.GroundAt(spot, train.Line, ref hint) };
        return world.Enemies is { } et ? Solidity.Clear(train, et, EnemyKind.Bruin, spot) : spot;
    }

    /// <summary>Where it climbs in: its car's side, at the floor, on the side it came from.</summary>
    static Double3 Door(TrainOnLine train, int car, Double3 from)
    {
        var frame = train.Frames[car];
        double side = Math.Sign(Double3.Dot(from - frame.Origin, frame.Right)) is var s and not 0 ? s : 1;
        return frame.ToWorld(new Double3(side * (frame.Shape.HalfWidth + 0.6), 0, 0));
    }

    void SetMode(BruinMode mode) => Height = (double)mode;

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Bruin;
        var train = ctx.Train;
        double dt = SimConstants.TickSeconds;
        if (Phase == SpinePhase.Dormant)
            Enter(ctx, SpinePhase.Telegraph);
        _stagger = Math.Max(0, _stagger - dt);
        if (Mode == BruinMode.Leave)
        {
            if (Walk(train, _home, t.Come, dt) || (WorldPosition(train) - train.Frames[0].Origin).Length > t.LeaveFarM)
            {
                Enter(ctx, SpinePhase.BreakOff);
                Enter(ctx, SpinePhase.Gone);
            }
            return;
        }
        if (!InCar)
        {
            // COME: to its car's door. A train that moves off before it's in leaves it behind.
            if (Car <= 0 || Car >= train.Frames.Count || train.Dynamics.Consist.IndexOf(Car) < 0 || train.Dynamics.Speed > t.GiveUpAbove)
            {
                Leave(train);
                return;
            }
            var door = Door(train, Car, _home);
            if (Walk(train, door, t.Come, dt) && PhaseSeconds >= t.HuffSeconds)
            {
                var frame = train.Frames[Car];
                Attached = Car;
                Local = new Double3(0, frame.Shape.Interior?.Min.Y ?? 1, 0);
                SetMode(BruinMode.Feed);
            }
            return;
        }
        // Its car cut away or gone: it goes with it into the dark.
        if (Attached >= train.Frames.Count || train.Dynamics.Consist.IndexOf(Attached) < 0)
        {
            Enter(ctx, SpinePhase.BreakOff);
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        if (Phase == SpinePhase.Telegraph && PhaseSeconds >= ctx.Tuning.MinReactionSeconds)
            Enter(ctx, SpinePhase.Commit);
        // FEED: the car's cargo going (riding along if the train moves off).
        var car = train.Vehicles[Attached];
        car.CargoIntegrity = Math.Max(0, car.CargoIntegrity - t.FeedPerSecond * dt);
        _fed += dt;
        double leaveAfter = MooseTuning.ByTier(t.LeaveAfter, ctx.World.Route?.Tier ?? Route.RouteTier.Frontier);
        if (Phase != SpinePhase.Grab && (leaveAfter >= 0 && _fed >= leaveAfter || car.CargoIntegrity <= 0))
        {
            Leave(train);
            return;
        }
        if (Phase == SpinePhase.Grab)
        {
            SetMode(BruinMode.Maul);
            return;
        }
        if (_stagger > 0)
        {
            SetMode(BruinMode.Stagger);
            _windup = 0;
            return;
        }
        // Whoever's nearest within reach (its target once it has one: whoever hit it last, while they're in reach).
        var me = WorldPosition(train);
        var near = ctx.LivingCrew().Where(c => (c.World - me).Length <= t.Reach && !c.Player.State.Has(PlayerFlags.Held))
            .OrderBy(c => c.Player.Id == Target ? 0 : 1).ThenBy(c => (c.World - me).Length).ThenBy(c => c.Player.Id).ToList();
        if (near.Count == 0)
        {
            Extra = -1;
            _windup = 0;
            SetMode(BruinMode.Feed);
            return;
        }
        var (who, at) = (near[0].Player.Id, near[0].World);
        Extra = who;
        // TURN toward them at its turn rate.
        double want = YawTo((at - me) with { Y = 0 });
        double diff = Wrap(want - Lateral), step = t.TurnDegreesPerSecond * Math.PI / 180 * dt;
        Lateral = Wrap(Lateral + Math.Clamp(diff, -step, step));
        bool inFront = Math.Abs(Wrap(want - Lateral)) <= t.FrontDegrees * Math.PI / 180;
        // RISE: whoever stays in front of it, it rises over; then the CHARGE: within chargeReach, the MAUL.
        _windup = inFront ? _windup + dt : 0;
        SetMode(_windup > 0 ? BruinMode.Rise : BruinMode.Feed);
        if (_windup < t.WindupSeconds || Phase != SpinePhase.Commit)
            return;
        _windup = 0;
        if ((at - me).Length <= t.ChargeReach)
        {
            Grab(ctx, who, t.MaulSeconds);
            SetMode(BruinMode.Maul);
        }
        else
            SetMode(BruinMode.Feed);
    }

    /// <summary>Out of its car (if it's in one) and back to the trees it came from.</summary>
    void Leave(TrainOnLine train)
    {
        if (InCar)
        {
            Local = Door(train, Attached, _home);
            Attached = Loose;
        }
        Extra = -1;
        _windup = 0;
        SetMode(BruinMode.Leave);
    }

    /// <summary>A step toward <paramref name="to"/> (loose, on the ground) at <paramref name="speed"/>; true there.</summary>
    bool Walk(TrainOnLine train, Double3 to, double speed, double dt)
    {
        var at = Local;
        var d = (to - at) with { Y = 0 };
        double far = d.Length, step = speed * dt;
        if (far > 1e-6)
            Lateral = YawTo(d);
        var next = far <= step ? to : at + d * (step / far);
        double hint = LineDistance;
        next = next with { Y = PlayerMotor.GroundAt(next, train.Line, ref hint) };
        LineDistance = hint;
        Local = next;
        return far <= Math.Max(step, 0.5);
    }

    static double Wrap(double a)
    {
        while (a > Math.PI)
            a -= 2 * Math.PI;
        while (a < -Math.PI)
            a += 2 * Math.PI;
        return a;
    }

    /// <summary>
    /// A blow (App. C.2): from the front it barely feels it (×<c>frontFactor</c>) and keeps hold of whoever it's mauling; from
    /// the side or behind it's staggered, drops them, and turns on the hitter. Blows in its car drive it out at
    /// <c>driveOffBlows</c>; its health gone, it's dead (the crew's kill, note 288).
    /// </summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        var t = ctx.Tuning.Bruin;
        Marked(ctx, by);
        var train = ctx.Train;
        var me = WorldPosition(train);
        bool front = true;
        foreach (var (p, _) in ctx.Crew)
            if (p.Id == by)
                front = !Flank(me, Lateral, PlayerMotor.WorldPosition(p.State, train), t);
        Health -= front ? damage * t.FrontFactor : damage;
        if (Health <= 0)
        {
            Slay(ctx);
            return;
        }
        if (InCar && ++_blows >= t.DriveOffBlows && Phase != SpinePhase.Grab)
        {
            Leave(train);
            return;
        }
        if (front)
            return;
        _stagger = t.StaggerSeconds;
        _windup = 0;
        Extra = by;
        if (Phase == SpinePhase.Grab && by != Holding)
            Rescued(ctx, by);
        if (InCar && _blows >= t.DriveOffBlows)
            Leave(train);
    }

    /// <summary>Whether <paramref name="from"/> is at its side or behind it (outside its front cone), as it stands at <paramref name="at"/> facing <paramref name="yaw"/>.</summary>
    public static bool Flank(Double3 at, double yaw, Double3 from, BruinTuning t)
    {
        var d = (from - at) with { Y = 0 };
        if (d.Length < 1e-6)
            return false;
        return Math.Abs(Wrap(YawTo(d) - yaw)) > t.FrontDegrees * Math.PI / 180;
    }

    /// <summary>Mauled: dead under it; then it goes back to feeding.</summary>
    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.Mauled);
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Telegraph);
        SetMode(InCar ? BruinMode.Feed : BruinMode.Leave);
    }

    /// <summary>Dropped its victim (a blow from the side, a pull): back to feeding, staggered.</summary>
    protected override void Rescued(EnemyContext ctx, int by)
    {
        base.Rescued(ctx, by);
        Enter(ctx, SpinePhase.Telegraph);
        _stagger = Math.Max(_stagger, ctx.Tuning.Bruin.StaggerSeconds);
    }
}
