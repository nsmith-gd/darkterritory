using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// STOKER · heat · interior (GDD v1.1 §21, App. A.5; the Hollow merged into it). Stoker v3, the director's decisions of 6 Oct
/// 2026 (GDD App. F.1; ARCHITECTURE §8 notes 263 and 271). It looks for heat: a firebox run above
/// <see cref="StokerTuning.HeatFirebox"/> for <see cref="StokerTuning.HeatSeconds"/> draws it; a low fire never does. It
/// boards at the tender, at the coal (the telegraph: a scraping on the coal and a sick glow, for
/// <see cref="StokerTuning.BoardSeconds"/>), and crosses the footplate: a fireman can drive it off on the way in.
/// Once in the firebox it's territorial: it feeds the fire's heat in lurches with the safety valve held shut and the
/// regulator creeping open, and whoever opens the door on it (a shovelful) takes a heavy burn
/// (<see cref="StokerTuning.DoorBurn"/>), and a second kills. That's the mistake you learn from. The counter is to vent and
/// starve: no coal, the blow-off held, until the fire's under <see cref="StokerTuning.StarveFirebox"/>; then it leaves the
/// way it came. A hose through the open door (an extinguisher sprayed into the firebox) kills it, at the cost of
/// <see cref="StokerTuning.HoseFireCost"/> of the fire. Either way none comes back for <see cref="StokerTuning.BreakSeconds"/>
/// (World). No chip damage: a crew that knows the rule never gets hurt. Running hot is fast and draws it; running cool is
/// safe and slow.
/// </summary>
/// <remarks><see cref="Enemy.Extra2"/> is 1 while it's out on the tender and the footplate, boarding or leaving
/// (replicated: the scene draws it there, and its blows land there).</remarks>
public sealed class Stoker(int id) : Enemy(id)
{
    public override EnemyKind Kind => EnemyKind.Stoker;
    public override PressureZone Zone => PressureZone.Interior;
    public override Sense Sense => Sense.Heat;
    public override Want Want => Want.Kill;
    /// <summary>On its way in from the tender: in the firebox there's nothing to swing at (note 271).</summary>
    public override double MeleeRadius => Boarding ? 0.8 : 0;
    /// <summary>Feeding the fire: the train's running away with it.</summary>
    public bool Feeding => Phase == SpinePhase.Commit;
    /// <summary>Out on the tender and the footplate, on its way in (note 263), not in the fire yet.</summary>
    public bool Boarding => Extra2 > 0.5 && !Gone && Phase == SpinePhase.Telegraph;
    /// <summary>Starved out (note 271): back across the footplate to the tender the way it came, and gone.</summary>
    public bool Leaving => Phase == SpinePhase.BreakOff && !Gone;

    /// <summary>
    /// Only on its way in (note 263). In the firebox it isn't clubbed (Stoker v3, note 271): the door is its territory, and
    /// a crowbar at a shut one only rings on the iron.
    /// </summary>
    public override bool Reachable(World world) => Boarding;

    /// <summary>Host: who it's burnt at the door this time in, and how often (a second burn kills).</summary>
    readonly Dictionary<int, int> _burnt = new();
    bool _doorWasOpen;
    double _hosed;

    static Double3 FireboxAt(TrainOnLine train) => train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position;

    /// <summary>Where it boards: the tender's coal face (the fireman's own back), or the firebox if the engine has none.</summary>
    static Double3 TenderAt(TrainOnLine train) =>
        train.Frames[0].Shape.Interactables.FirstOrDefault(i => i.Kind == InteractableKind.Coal) is { } coal ? coal.Position : FireboxAt(train);

    /// <summary>In the engine's firebox already (tests, audits, a director's insist): soot falls, then it feeds.</summary>
    public static Stoker InFirebox(int id, TrainOnLine train, bool door, StokerTuning t) => new(id)
    {
        Attached = 0,
        Local = FireboxAt(train),
        Health = t.Health,
        Extra = door ? 1 : 0,
    };

    /// <summary>Drawn by the heat (note 263): on the tender's coal, to cross to the firebox.</summary>
    public static Stoker AtTender(int id, TrainOnLine train, StokerTuning t) => new(id)
    {
        Attached = 0,
        Local = TenderAt(train),
        Health = t.Health,
        Extra2 = 1,
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
            Enter(ctx, SpinePhase.Telegraph); // the scrape on the coal and the glow (boarding), or soot falling into the cab
        if (Phase == SpinePhase.Telegraph)
        {
            double wait = Boarding ? Math.Max(t.BoardSeconds, ctx.Tuning.MinReactionSeconds) : Math.Max(t.SootSeconds, ctx.Tuning.MinReactionSeconds);
            if (Boarding)
            {
                // Across the footplate from the coal to the fire door, at an even creep.
                double k = Math.Clamp(PhaseSeconds / wait, 0, 1);
                var from = TenderAt(train);
                Local = from + (FireboxAt(train) - from) * k;
            }
            if (PhaseSeconds >= wait && Enter(ctx, SpinePhase.Commit))
            {
                string how = Boarding ? "from the tender" : Extra > 0.5 ? "through the open door" : "down the stack";
                Extra2 = 0;
                Local = FireboxAt(train);
                // C.9's Stoker row (note 190): the runaway begins. Who last fuelled or tended the firebox, and for how long not.
                var (actor, action) = Run.IncidentLog.Firebox(ctx.World);
                ctx.World.Attribution.Add(Run.IncidentLog.Event(ctx.World, Run.IncidentKind.Runaway, $"Stoker got into the firebox {how}", actor, action));
            }
        }
        if (Phase == SpinePhase.BreakOff)
        {
            // Starved out (note 271): back across the footplate to the coal, and off into the dark.
            double k = Math.Clamp(PhaseSeconds / Math.Max(1e-6, t.BoardSeconds), 0, 1);
            var to = TenderAt(train);
            Local = FireboxAt(train) + (to - FireboxAt(train)) * k;
            if (k >= 1)
                Enter(ctx, SpinePhase.Gone);
            return;
        }
        if (Phase != SpinePhase.Commit)
            return;
        // In the firebox (Stoker v3, note 271). The fire it's in lurches (the gauge swings, and climbs) with the valve held
        // shut. The train's boiler is a struct it holds: written through. Replicated (the boiler record), so every gauge
        // swings alike.
        double swing = t.SwingSeconds > 0 ? DMath.Sin(2 * Math.PI * PhaseSeconds / t.SwingSeconds) : 0;
        train.Boiler.ExternalHeat = t.FeedRate + t.Swing * swing;
        train.Boiler.SafetyValveJammed = true;
        train.Boiler.Firebox = Math.Max(0, train.Boiler.Firebox - t.EatPerSecond * SimConstants.TickSeconds);
        // A hose through the door: sprayed into the firebox, it's killed, and takes most of the fire with it.
        if (Hosed(ctx, t))
            return;
        // Territorial: the door opened on it (a shovelful) burns whoever's at it, heavily; a second time, to death.
        bool open = train.Boiler.FireDoorOpen;
        if (open && !_doorWasOpen && AtTheDoor(ctx) is { } who)
        {
            int times = _burnt.GetValueOrDefault(who) + 1;
            _burnt[who] = times;
            ctx.Harm(who, times >= 2 ? t.DoorKill : t.DoorBurn, DeathCause.Stoker);
        }
        _doorWasOpen = open;
        // Starved: the fire under its mark, and it goes the way it came.
        if (train.Boiler.Firebox < t.StarveFirebox && Enter(ctx, SpinePhase.BreakOff))
        {
            train.Boiler.ExternalHeat = 0;
            train.Boiler.SafetyValveJammed = false;
            Extra2 = 1;
        }
    }

    /// <summary>Whoever's nearest the fire door in the cab, within reach of it: the one who opened it.</summary>
    int? AtTheDoor(EnemyContext ctx)
    {
        var firebox = FireboxAt(ctx.Train);
        double reach = ctx.Tuning.Stoker.DoorReach;
        return ctx.Crew.Where(c => c.Player.State.Alive && PlayerMotor.InCab(c.Player.State, ctx.Train)
                && Flat(c.Player.State.Position - firebox) <= reach)
            .OrderBy(c => Flat(c.Player.State.Position - firebox)).Select(c => (int?)c.Player.Id).FirstOrDefault();
    }

    static double Flat(Double3 d) => Math.Sqrt(d.X * d.X + d.Z * d.Z);

    /// <summary>
    /// The hose (note 271): a crewmate in the cab within reach of the fire door, an extinguisher with charge in their hands,
    /// Fire held for <see cref="StokerTuning.HoseSeconds"/>. The spray opens the door and goes in (no burn: the nozzle's
    /// between them); the Stoker's killed and the fire's left with <see cref="StokerTuning.HoseFireCost"/> of it gone.
    /// </summary>
    bool Hosed(EnemyContext ctx, StokerTuning t)
    {
        var train = ctx.Train;
        var firebox = FireboxAt(train);
        double reach = t.DoorReach;
        bool any = false;
        foreach (var (p, intent) in ctx.Crew)
        {
            var s = p.State;
            if (!s.Alive || !intent.Has(PlayerButtons.Fire) || !PlayerMotor.InCab(s, train) || Flat(s.Position - firebox) > reach)
                continue;
            if (ctx.World.Bodies.CarriedBy(p.Id) is not { Kind: Physics.BodyKind.Extinguisher, Charge: > 0 } ext)
                continue;
            any = true;
            ext.Charge = Math.Max(0, ext.Charge - SimConstants.TickSeconds / ctx.Tuning.CarFire.ChargeSeconds);
            ctx.World.Attribution.Tended(p.Id, ctx.World.Run?.Seconds ?? 0);
        }
        _hosed = any ? _hosed + SimConstants.TickSeconds : 0;
        if (_hosed < t.HoseSeconds)
            return false;
        train.Boiler.FireDoorOpen = true;
        _doorWasOpen = true;
        train.Boiler.Firebox *= 1 - t.HoseFireCost;
        Leave(ctx, train);
        return true;
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

    /// <summary>
    /// Caught on its way in from the tender (note 263): it isn't in the fire yet, so a blow doesn't burn, and each blow tells
    /// <see cref="StokerTuning.TenderBlowScale"/> times over. In the firebox it can't be reached (note 271).
    /// </summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        if (Boarding)
            damage *= ctx.Tuning.Stoker.TenderBlowScale;
        ctx.World.Attribution.Tended(by, ctx.World.Run?.Seconds ?? 0); // tending the firebox, the hard way
        base.Struck(ctx, by, damage);
        if (Gone)
            Leave(ctx, ctx.Train);
    }

    void Leave(EnemyContext ctx, TrainOnLine train)
    {
        if (Phase is SpinePhase.Commit or SpinePhase.BreakOff)
        {
            train.Boiler.ExternalHeat = 0;
            train.Boiler.SafetyValveJammed = false;
        }
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
/// They come only to a stopped train (GDD App. F, 6 Oct 2026), so "pulling away" is getting under way (note 269).
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
        // BREAK OFF: the lamp out, or the train pulling away from them: under way again (note 269).
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
                // C.9's Fire Flies row (note 190): who last lit that car's lamp.
                int lit = ctx.World.Attribution.LampLitBy(into);
                ctx.World.Attribution.Add(Run.IncidentLog.Event(ctx.World, Run.IncidentKind.Fire, $"Fire Flies set car {into} alight", lit,
                    lit >= 0 ? "Lamp lit by {actor}." : "Nobody lit that lamp.", into));
            }
            Enter(ctx, SpinePhase.BreakOff);
            Enter(ctx, SpinePhase.Gone);
        }
    }
}
