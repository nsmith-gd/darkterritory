using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// STOKER · heat · interior (GDD v1.1 §21, App. A.5; the Hollow merged into it). It perches on the smokestack, and gets into
/// the firebox when the fire's burned low (pressure under 40 for 45 s: down the stack) or the firebox door is left open at a
/// stop (through the door). Soot falls into the cab (the telegraph). Then it feeds: pressure climbs with no fuel going in,
/// and the speed with it (the gauge rising, the wrong-coloured glow, the train accelerating); past the next curve's or
/// grade's limit, the train derails. Counters: vent (pressure and speed drop, time's lost: it only buys time), or open the
/// firebox and club it, and every blow burns whoever swings. Rule: keep it hot, keep it shut. Fully preventable.
/// </summary>
/// <remarks><see cref="Enemy.Extra"/> is 1 when it came through the door (the director weighs it ×3 then).</remarks>
public sealed class Stoker(int id) : Enemy(id)
{
    public override EnemyKind Kind => EnemyKind.Stoker;
    public override PressureZone Zone => PressureZone.Interior;
    public override Sense Sense => Sense.Heat;
    public override Want Want => Want.Kill;
    /// <summary>In the firebox, it's clubbed through the open door.</summary>
    public override double MeleeRadius => Phase is SpinePhase.Telegraph or SpinePhase.Commit ? 0.8 : 0;
    /// <summary>Feeding the fire: the train's running away with it.</summary>
    public bool Feeding => Phase == SpinePhase.Commit;

    /// <summary>In the engine's firebox.</summary>
    public static Stoker InFirebox(int id, TrainOnLine train, bool door, StokerTuning t) => new(id)
    {
        Attached = 0,
        Local = train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position,
        Health = t.Health,
        Extra = door ? 1 : 0,
    };

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Stoker;
        var train = ctx.Train;
        if (train.BoilerTuning is null || train.Boiler.Ruptured)
        {
            Leave(ctx, train);
            return;
        }
        if (Phase == SpinePhase.Dormant)
            Enter(ctx, SpinePhase.Telegraph); // soot falls into the cab
        if (Phase == SpinePhase.Telegraph && PhaseSeconds >= Math.Max(t.SootSeconds, ctx.Tuning.MinReactionSeconds))
            Enter(ctx, SpinePhase.Commit);
        if (Phase != SpinePhase.Commit)
            return;
        // FEED: pressure without fuel, and the valve held shut. The train's boiler is a struct it holds: written through.
        train.Boiler.ExternalHeat = t.FeedRate;
        train.Boiler.SafetyValveJammed = true;
    }

    /// <summary>
    /// The speed climbs with the pressure (App. A.5 FEED): it opens the regulator further the longer it's fed, whatever's
    /// set. From replicated state (its phase and time in it), so a predicting client runs away as the host does.
    /// </summary>
    public override void Tamper(World world, ref TrainControls controls)
    {
        if (!Feeding || world.Enemies is not { } et)
            return;
        double ramp = Math.Clamp(PhaseSeconds / et.Stoker.RunawayRampSeconds, 0, 1);
        controls.Throttle = Math.Max(controls.Throttle, ramp);
        controls.Brake = Math.Min(controls.Brake, 1 - ramp);
    }

    /// <summary>Clubbed through the open firebox door: it's hurt, and it burns whoever swings.</summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        ctx.Bite(by, ctx.Tuning.Stoker.BurnPerBlow, DeathCause.Stoker);
        base.Struck(ctx, by, damage);
        if (Gone)
            Leave(ctx, ctx.Train);
    }

    void Leave(EnemyContext ctx, TrainOnLine train)
    {
        train.Boiler.ExternalHeat = 0;
        train.Boiler.SafetyValveJammed = false;
        Enter(ctx, SpinePhase.Gone);
    }
}

/// <summary>
/// TIPPY TOESIE · absence (a player standing still) · interior (GDD v1.1 §21, App. A.5). It picks a player idle and alone,
/// and tiptoes up behind them, slowly: faint tiptoeing, and it's plain to anyone facing it. The target turns and looks at
/// it, and it flees, to try someone else or wait a while. Reaching them, it covers their mouth (their voice goes muffled,
/// App. C.8) and suffocates them over 20 s; any friend who hits it or pulls it off breaks it, and it flees. Rule: don't
/// stand still alone. It naturally finds the conductor and the boiler player. At a crew of two it comes on slower.
/// </summary>
/// <remarks>
/// It stands in its target's frame: in their car (<see cref="Enemy.Attached"/>), or loose in the world beside the train.
/// <see cref="Enemy.Extra"/> is who it's after (−1 for nobody: hidden, waiting).
/// </remarks>
public sealed class TippyToesie(int id) : Enemy(id)
{
    double _wait;
    int _flees;

    public override EnemyKind Kind => EnemyKind.TippyToesie;
    public override PressureZone Zone => PressureZone.Interior;
    public override Sense Sense => Sense.Absence;
    public override Want Want => Want.Kill;
    public override double MeleeRadius => Phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Grab ? 0.5 : 0;
    public override bool PullsFree => true;
    public int? Target => Extra >= 0 ? (int)Extra : null;
    /// <summary>Hidden between tries: nowhere to be seen.</summary>
    public bool Hidden => Phase is SpinePhase.Dormant or SpinePhase.BreakOff;

    public static TippyToesie Hiding(int id, TippyToesieTuning t) => new(id) { Extra = -1, Health = t.Health };

    /// <summary>Players idle and alone: stood still long enough, nobody within 8 m (App. B.5's "per player idle and alone").</summary>
    public static IEnumerable<int> Marks(EnemyContext ctx, TippyToesieTuning t, IReadOnlyDictionary<int, double> idle) =>
        ctx.LivingCrew().Where(c => idle.GetValueOrDefault(c.Player.Id) >= t.IdleSeconds && !c.Player.State.Has(PlayerFlags.Held)
            && CrewSense.Alone(ctx, c.Player.Id, t.AloneRadius)).Select(c => (int)c.Player.Id);

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.TippyToesie;
        var train = ctx.Train;
        switch (Phase)
        {
            case SpinePhase.Dormant:
                {
                    Extra = -1;
                    _wait -= SimConstants.TickSeconds;
                    if (_wait > 0)
                        return;
                    if (_flees >= t.MaxFlees)
                    {
                        Enter(ctx, SpinePhase.Gone);
                        return;
                    }
                    // STALK: a player idle and alone, from behind.
                    var mark = Marks(ctx, t, ctx.World.IdleSeconds).Select(i => (int?)i).FirstOrDefault();
                    if (mark is not { } who)
                        return;
                    var s = ctx.Crew.First(c => c.Player.Id == who).Player.State;
                    var behind = new Double3(DMath.Sin(s.Yaw), 0, DMath.Cos(s.Yaw)) * t.StartBehind;
                    Attached = s.Parent >= 0 ? s.Parent : Loose;
                    Local = s.Parent >= 0 ? Clamp(train, s.Parent, s.Position + behind) : PlayerMotor.WorldPosition(s, train) + WorldBehind(s, train, t.StartBehind);
                    Extra = who;
                    Enter(ctx, SpinePhase.Telegraph); // faint tiptoeing
                    return;
                }
            case SpinePhase.Telegraph or SpinePhase.Commit:
                {
                    if (Target is not { } who || ctx.Crew.FirstOrDefault(c => c.Player.Id == who).Player.State is not { Alive: true } s)
                    {
                        Flee(ctx, t, seen: false);
                        return;
                    }
                    var at = WorldPosition(train);
                    // SEEN: the target looks at it, and it's off.
                    if (CrewSense.Facing(s, train, at, t.SeenDegrees))
                    {
                        Flee(ctx, t, seen: true);
                        return;
                    }
                    // The target's gone somewhere else (another car, off the train): it gives this one up.
                    if (s.Parent >= 0 && Attached != s.Parent || s.Parent < 0 && Attached != Loose)
                    {
                        Flee(ctx, t, seen: false);
                        return;
                    }
                    if (Phase == SpinePhase.Telegraph && PhaseSeconds >= ctx.Tuning.MinReactionSeconds)
                        Enter(ctx, SpinePhase.Commit);
                    double speed = ctx.Crew.Count(c => c.Player.State.Alive) <= 2 ? t.ApproachSpeedCrewOfTwo : t.ApproachSpeed;
                    var target = s.Parent >= 0 ? s.Position : PlayerMotor.WorldPosition(s, train);
                    var to = (target - Local) with { Y = 0 };
                    double step = speed * SimConstants.TickSeconds;
                    if (to.Length > t.Reach)
                    {
                        Local += to.Normalized * Math.Min(step, to.Length - t.Reach * 0.5);
                        return;
                    }
                    if (Phase == SpinePhase.Commit)
                        Grab(ctx, who, t.SuffocateSeconds); // a hand over the mouth
                    return;
                }
            case SpinePhase.Grab:
                return;
            default:
                Enter(ctx, SpinePhase.Dormant);
                return;
        }
    }

    static Double3 WorldBehind(in PlayerState s, TrainOnLine train, double distance)
    {
        double yaw = PlayerMotor.WorldYaw(s, train);
        return new Double3(DMath.Sin(yaw), 0, DMath.Cos(yaw)) * distance;
    }

    /// <summary>Behind them in their car, but inside its walls.</summary>
    static Double3 Clamp(TrainOnLine train, int car, Double3 local)
    {
        var shape = train.Frames[car].Shape;
        var box = shape.Interior ?? shape.Cab ?? default;
        if (box.Max.X <= box.Min.X)
            return local;
        return new Double3(Math.Clamp(local.X, box.Min.X + 0.3, box.Max.X - 0.3), local.Y, Math.Clamp(local.Z, box.Min.Z + 0.3, box.Max.Z - 0.3));
    }

    void Flee(EnemyContext ctx, TippyToesieTuning t, bool seen)
    {
        if (seen)
            _flees++;
        _wait = t.WaitAfterSeen;
        Extra = -1;
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Dormant);
    }

    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.Suffocated);
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Gone);
    }

    /// <summary>Hit or pulled off: it flees (App. A.5 "any friend hits or pulls it → it flees").</summary>
    protected override void Rescued(EnemyContext ctx, int by)
    {
        base.Rescued(ctx, by);
        Flee(ctx, ctx.Tuning.TippyToesie, seen: true);
    }
}

/// <summary>
/// FIRE FLIES · light · interior (GDD v1.1 §21, App. A.5). They drift lineside in dark sections and swarm to a lit lamp
/// inside a car: glow and buzzing around the lamp (the telegraph). Left to linger ~20 s, the car catches fire, and the fire
/// spreads (App. C.5). Break off: the lamp turned off, or the train pulling away at speed. Rule: lamps off when they swarm.
/// The lamps-off answer blinds you to the Track Doll: that contradiction is the point.
/// </summary>
/// <remarks><see cref="Enemy.Attached"/> is the car whose lamp they're on.</remarks>
public sealed class FireFlies(int id) : Enemy(id)
{
    double _fast;

    public override EnemyKind Kind => EnemyKind.FireFlies;
    public override PressureZone Zone => PressureZone.Interior;
    public override Sense Sense => Sense.Light;
    public override Want Want => Want.Cargo;
    /// <summary>
    /// A swat at the swarm round the lamp lands (T121: every creature confirms a hit), and the swarm parts and closes again:
    /// it does nothing to them. The answer's still the lamp, or driving away (App. A.5).
    /// </summary>
    public override double MeleeRadius => Phase is SpinePhase.Dormant or SpinePhase.Telegraph ? 0.8 : 0;

    public override void Struck(EnemyContext ctx, int by, double damage) { }

    /// <summary>On a car's lamp.</summary>
    public static FireFlies OnLamp(int id, TrainOnLine train, int car)
    {
        var room = train.Frames[car].Shape.Interior!.Value;
        return new FireFlies(id) { Attached = car, Local = room.Centre with { Y = room.Max.Y - 0.3 } };
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.FireFlies;
        var train = ctx.Train;
        if (Attached <= 0 || Attached >= train.Frames.Count || train.Dynamics.Consist.IndexOf(Attached) < 0)
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        var car = train.Vehicles[Attached];
        _fast = train.Dynamics.Speed >= t.PullAwaySpeed ? _fast + SimConstants.TickSeconds : 0;
        // BREAK OFF: the lamp out, or the train pulling away from them at speed.
        if (!car.LampLit || _fast >= t.PullAwaySeconds)
        {
            Enter(ctx, SpinePhase.BreakOff);
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        if (Phase == SpinePhase.Dormant)
            Enter(ctx, SpinePhase.Telegraph); // the glow and the buzzing about the lamp
        if (Phase == SpinePhase.Telegraph && PhaseSeconds >= t.IgniteSeconds && Enter(ctx, SpinePhase.Commit))
        {
            // IGNITE: the car catches, unless it's already alight.
            if (!ctx.World.ActiveEnemies.Any(e => e is CarFire f && !f.Gone && f.Attached == Attached))
            {
                int into = Attached;
                double along = Local.Z;
                ctx.World.AddEnemy(i => CarFire.In(i, train, into, along, ctx.Tuning.CarFire));
            }
            Enter(ctx, SpinePhase.BreakOff);
            Enter(ctx, SpinePhase.Gone);
        }
    }
}
