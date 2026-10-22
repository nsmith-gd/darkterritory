using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// THE PASSENGER · absence (a player alone) · corrupted human (GDD v1.1 §21, App. A.8). A crew member from a train lost long
/// ago boards during a facility stop, unnoticed, and passes for one of yours in the dark: an old coat and cap, hanging near
/// the rear. The only tell is silence: it never speaks on proximity voice. It waits for a player alone, then drags them
/// toward the caboose at walking pace; the victim can't break free alone, and only the crew can stop it (kill it). At the
/// caboose it uncouples it and rolls away into the dark to eat: victim and caboose lost. Rule: make everyone speak.
/// <para>
/// Driven off by its rule (note 288, enemies.json <c>passenger.drivenOff</c>; the director's clarification of 7 Oct 2026):
/// found out, it's done. A crewmate's blow (anyone but its victim, who can't break free alone) unmasks it: it lets go, stands
/// <c>unmaskedSeconds</c>, then bolts for the back of the train at <c>fleeSpeed</c> and off it into the dark. Driven off isn't the end of it: it can
/// board again at a later stop (<see cref="World.DrivenOff"/>). Killed only by the crew together: blows from two or more
/// crewmates inside <c>coordinatedKill</c>'s window, run down before it's off the back; killed, it's gone for the night.
/// </para>
/// </summary>
/// <remarks>
/// In a car (<see cref="Enemy.Attached"/>, <see cref="Enemy.Local"/> its feet). <see cref="Enemy.Extra"/> is whose face it
/// passes with (a player id: the roster counts one too many); <see cref="Enemy.Extra2"/> its facing in its car's frame.
/// </remarks>
public sealed class Passenger(int id) : Enemy(id)
{
    double _uncoupling;

    public override EnemyKind Kind => EnemyKind.Passenger;
    public override PressureZone Zone => PressureZone.Corrupted;
    public override Sense Sense => Sense.Absence;
    public override Want Want => Want.Trust;
    public override double MeleeRadius => 0.5;
    /// <summary>Whose face it wears.</summary>
    public int Looks => (int)Math.Round(Extra);

    /// <summary>Into a car's room near the rear of the engine's rake (App. B.8 "boards during a facility stop"), with a face.</summary>
    public static Passenger Boards(int id, TrainOnLine train, int car, int looks, PassengerTuning t)
    {
        var room = train.Frames[car].Shape.Interior!.Value;
        return new Passenger(id) { Attached = car, Local = room.Centre with { Y = room.Min.Y, Z = room.Max.Z - 1 }, Extra = looks, Health = t.Health };
    }

    /// <summary>A car to board: the rearmost in the engine's rake with a room.</summary>
    public static int? Car(TrainOnLine train) =>
        train.Dynamics.Consist.Vehicles.Select(v => v.Id).Where(i => i > 0 && train.Frames[i].Shape.Interior is not null).Select(i => (int?)i).LastOrDefault();

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Passenger;
        var train = ctx.Train;
        if (Attached < 0 || Attached >= train.Frames.Count || train.Dynamics.Consist.IndexOf(Attached) < 0)
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        switch (Phase)
        {
            case SpinePhase.Dormant:
                // BLEND: aboard, passing for crew, near the rear, silent (the telegraph is what it never does).
                Enter(ctx, SpinePhase.Telegraph);
                return;
            case SpinePhase.Telegraph:
                if (PhaseSeconds >= t.LingerSeconds)
                {
                    Enter(ctx, SpinePhase.Gone);
                    return;
                }
                // STALK: a player alone on the train, and it goes to them.
                if (PhaseSeconds < t.StalkSeconds)
                    return;
                var mark = ctx.LivingCrew().Where(c => c.Player.State.Parent > 0 && !c.Player.State.Has(PlayerFlags.Held) && CrewSense.Alone(ctx, c.Player.Id, t.AloneRadius))
                    .OrderBy(c => ctx.Weigh(c.Player.Id, Math.Abs(train.Dynamics.Consist.IndexOf(c.Player.State.Parent) - train.Dynamics.Consist.IndexOf(Attached))))
                    .Select(c => (int?)c.Player.Id).FirstOrDefault();
                if (mark is not { } who)
                    return;
                var s = ctx.Crew.First(c => c.Player.Id == who).Player.State;
                if (s.Parent != Attached)
                {
                    // Along the train to their car (it walks the cars like crew).
                    Walk(ctx, train, s.Parent, t.WalkSpeed);
                    return;
                }
                var to = (s.Position - Local) with { Y = 0 };
                if (to.Length > t.Reach)
                {
                    Local += to.Normalized * Math.Min(t.WalkSpeed * SimConstants.TickSeconds, to.Length);
                    Extra2 = DMath.Atan2(-to.X, -to.Z);
                    return;
                }
                if (Enter(ctx, SpinePhase.Commit))
                    Grab(ctx, who, 600); // held until the caboose's gone, or it's killed
                return;
            case SpinePhase.Grab:
                {
                    // Dragging them to the caboose at walking pace; there, it uncouples it.
                    int rear = train.Dynamics.Consist.Vehicles[^1].Id;
                    if (Attached != rear)
                        Walk(ctx, train, rear, t.DragSpeed);
                    else
                    {
                        var end = new Double3(0, Local.Y, train.Frames[rear].Shape.HalfLength - 0.5);
                        var to2 = end - Local;
                        if (to2.Length > 0.3)
                            Local += to2.Normalized * Math.Min(t.DragSpeed * SimConstants.TickSeconds, to2.Length);
                        else
                        {
                            _uncoupling += SimConstants.TickSeconds;
                            if (_uncoupling >= t.UncoupleSeconds)
                            {
                                int ahead = train.VehicleAhead(rear);
                                if (ahead >= 0)
                                    train.Uncouple(ahead, Parting.Passenger);
                                train.Vehicles[rear].Taken = true;
                                int victim = Holding;
                                Enter(ctx, SpinePhase.Punish);
                                Punish(ctx, victim);
                                return;
                            }
                        }
                    }
                    ctx.Carry(Holding, WorldPosition(train));
                    return;
                }
            case SpinePhase.BreakOff when t.DrivenOff:
                {
                    // Found out, it lets go and stands there a moment, face to face with the crew (their one chance to kill it
                    // together), then it's off the back of the train at a run (out of the car's end into the dark), or, with no way
                    // through, gone anyway once it's had the time.
                    if (PhaseSeconds < t.UnmaskedSeconds)
                        return;
                    int rear = train.Dynamics.Consist.Vehicles[^1].Id;
                    if (Attached != rear && PhaseSeconds < FleeGiveUpSeconds)
                    {
                        Walk(ctx, train, rear, t.FleeSpeed);
                        return;
                    }
                    var end = new Double3(0, Local.Y, train.Frames[Attached].Shape.HalfLength - 0.3);
                    var off = end - Local;
                    if (off.Length > 0.3 && PhaseSeconds < FleeGiveUpSeconds)
                    {
                        Local += off.Normalized * Math.Min(t.FleeSpeed * SimConstants.TickSeconds, off.Length);
                        Extra2 = Math.PI;
                        return;
                    }
                    Enter(ctx, SpinePhase.Gone);
                    return;
                }
            default:
                Enter(ctx, SpinePhase.Gone);
                return;
        }
    }

    /// <summary>Along the cars toward a car: to the end of this one, then through into the next.</summary>
    void Walk(EnemyContext ctx, TrainOnLine train, int toward, double speed)
    {
        var consist = train.Dynamics.Consist;
        int mine = consist.IndexOf(Attached), theirs = consist.IndexOf(toward);
        if (theirs < 0 || mine < 0)
            return;
        var shape = train.Frames[Attached].Shape;
        double end = theirs < mine ? -shape.HalfLength + 0.4 : shape.HalfLength - 0.4;
        double step = speed * SimConstants.TickSeconds;
        if (Math.Abs(Local.Z - end) <= step)
        {
            int next = consist.Vehicles[mine + (theirs < mine ? -1 : 1)].Id;
            if (next == 0 || train.Frames[next].Shape.Interior is not { } room)
                return;
            Attached = next;
            Local = room.Centre with { Y = room.Min.Y, Z = theirs < mine ? room.Max.Z - 0.4 : room.Min.Z + 0.4 };
            return;
        }
        Local = Local with { Z = Local.Z + Math.Sign(end - Local.Z) * step };
        Extra2 = end < Local.Z ? 0 : Math.PI;
    }

    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.Uncoupled);
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Gone);
    }

    /// <summary>Found out and running, it's off the train by this long (s) whatever's in its way. Not a design number: a stop.</summary>
    const double FleeGiveUpSeconds = 30;

    /// <summary>
    /// Old rule (<c>drivenOff</c> false): only killing it frees its victim (App. A.8 "the victim can't break free alone").
    /// Note 288: a crewmate's blow finds it out and drives it off (it lets go); the gang's blows kill it.
    /// </summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        if (ctx.Tuning.Passenger.DrivenOff)
        {
            if (by == Holding || Gone)
                return;
            Marked(ctx, by);
            if (Ganged(ctx, except: Holding))
            {
                Health -= damage;
                if (Health <= 0)
                {
                    ctx.World.DrivenOff.Remove(Kind);
                    Slay(ctx);
                    return;
                }
            }
            if (Phase != SpinePhase.BreakOff)
            {
                ctx.World.DrivenOff.Add(Kind);
                Enter(ctx, SpinePhase.BreakOff); // lets go, if it held anyone
            }
            return;
        }
        Health -= damage;

        if (Health > 0)
            return;
        Release(ctx);
        Enter(ctx, SpinePhase.Gone);
    }
}


/// <summary>
/// GRUMBLER · sound · corrupted human (GDD v1.1 §21, App. A.8). A labourer that still works the crane, after a fashion: it
/// scuttles like a spider over the crane and the food crates at a facility, gnawing them (audible gnawing; visible on the
/// crates, the telegraph). Interrupt it or hit it and it goes feral, hunting whoever hit it last. It heals if only one
/// player has hit it in the last few seconds: no one player can kill it. Craned aboard with a crate by mistake, it eats the
/// cargo on the train. Rule: gang up or leave it alone. A spotter checks the crates before every lift.
/// <para>
/// Driven off by its rule (note 288, enemies.json <c>grumbler.drivenOff</c>; the director's clarification of 7 Oct 2026): a
/// lone player's blows never wear it down. Feral, with its prey among friends (two or more of the crew within
/// <c>gangRadius</c> of it) and no gang striking it, it breaks off after <c>outnumberedSeconds</c> (letting go of a maul too)
/// and scuttles off for <c>fleeSeconds</c>, then back to gnawing, calmed: a break, not the night. Killed only by the gang
/// (blows from two or more crewmates inside <c>coordinatedKill</c>'s window wear its health down; it regenerates against
/// one), and while they're on it, it fights rather than flees. Killed, it's gone for the night (<see cref="World.Slain"/>).
/// </para>
/// </summary>
/// <remarks>
/// <see cref="Enemy.Extra"/> is the crane casting it's on (its index), −1 once it's off them; aboard,
/// <see cref="Enemy.Attached"/> is the car it's eating in. <see cref="Enemy.Extra2"/> is 1 once it's feral.
/// </remarks>
public sealed class Grumbler(int id) : Enemy(id)
{
    readonly List<(int By, uint Tick)> _hits = [];
    double _bite, _outnumbered;
    Double3 _fleeWay;

    public override EnemyKind Kind => EnemyKind.Grumbler;
    public override PressureZone Zone => PressureZone.Corrupted;
    public override Sense Sense => Sense.Sound;
    public override Want Want => Want.Cargo;
    public override double MeleeRadius => 0.8;
    public bool Feral => Extra2 > 0.5;
    public int Casting => (int)Math.Round(Extra);

    /// <summary>
    /// The crane whose castings it's on (note 848: a yard's gantry, the perch it was put on), on the host; null, the stop's
    /// own crane as before (the gym's call, a test's).
    /// </summary>
    Crane? _crane;

    public static Grumbler OnCrates(int id, Double3 world, int casting, GrumblerTuning t, Crane? crane = null) =>
        new(id) { Attached = Loose, Local = world, Extra = casting, Health = t.Health, _crane = crane };

    // Note 991: its trip over the crane (the host's; every machine sees where it is): the way, how far along it it's gone,
    // how long it's gnawed since the last, and the ground under the gantry, to drop to when it's struck up there.
    List<Double3>? _trip;
    double _tripGone, _gnawing, _ground;

    /// <summary>Up on its crane, on a trip off its casting (note 991).</summary>
    public bool Roaming => _trip is not null;

    // How far out from a leg's column it climbs (m: the column's 0.24 and its own belly), and how far short of the bridge's
    // end trucks it turns back on the girder.
    const double LegFace = 0.4, BridgeClear = 1.6;

    /// <summary>
    /// Note 991 (GDD §21: "scuttles like a spider over the crane"): the way a Grumbler gnawing at <paramref name="from"/>
    /// goes over <paramref name="crane"/>: off its casting to the foot of the gantry's nearest leg, up the leg's face, onto
    /// the rail girder's flange, along it up to <paramref name="run"/> m (short of the bridge), and back the same way to the
    /// leg's foot. The last stretch, onto its casting wherever that is by then, is <see cref="Along"/>'s.
    /// </summary>
    public static List<Double3> CraneTrip(Crane crane, Double3 from, double run)
    {
        var (end, side) = Enumerable.Range(0, 4).Select(i => (End: i / 2, Side: i % 2))
            .MinBy(c => ((crane.Corner(c.End, c.Side) - from) with { Y = 0 }).Length);
        var corner = crane.Corner(end, side);
        var toward = (from - corner) with { Y = 0 };
        toward = toward.Length > 1e-6 ? toward.Normalized : new Double3(1, 0, 0);
        var foot = corner + toward * LegFace;
        var high = foot with { Y = corner.Y + crane.Top - 0.45 };
        double start = end == 0 ? 0.3 : crane.Length - 0.3, way = end == 0 ? 1 : -1;
        double stop = start + way * run, bridge = crane.BridgeAlong;
        if (way * (bridge - start) > 0)
            stop = way > 0 ? Math.Min(stop, bridge - BridgeClear) : Math.Max(stop, bridge + BridgeClear);
        if (way * (stop - start) < 0)
            stop = start;
        var onTop = crane.OnRail(side, start);
        return [from, foot, high, onTop, crane.OnRail(side, stop), onTop, high, foot];
    }

    /// <summary>
    /// Where <paramref name="gone"/> m along <paramref name="trip"/> is, the trip ending on <paramref name="home"/> (its casting
    /// now); null once it's back on it.
    /// </summary>
    public static Double3? Along(IReadOnlyList<Double3> trip, Double3 home, double gone)
    {
        for (int i = 0; i < trip.Count; i++)
        {
            var a = trip[i];
            var b = i + 1 < trip.Count ? trip[i + 1] : home;
            double length = (b - a).Length;
            if (gone < length)
                return a + (b - a) * (gone / length);
            gone -= length;
        }
        return null;
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Grumbler;
        var train = ctx.Train;
        // REGEN: one player alone at it, and it heals.
        uint window = (uint)Math.Round(t.GangSeconds * SimConstants.TickRate);
        _hits.RemoveAll(h => ctx.Tick - h.Tick > window);
        if (_hits.Select(h => h.By).Distinct().Count() <= 1)
            Health = Math.Min(t.Health, Health + t.RegenPerSecond * SimConstants.TickSeconds);
        if (t.DrivenOff && Feral && Phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Grab && Outnumbered(ctx, t))
            return;
        switch (Phase)
        {
            case SpinePhase.Dormant:
                Enter(ctx, SpinePhase.Telegraph); // gnawing on the crates
                return;
            case SpinePhase.Telegraph when !Feral:
                {
                    // On its casting: where the casting goes, it goes. Craned onto a car: LOADED, it's aboard.
                    if ((_crane ?? ctx.World.Run?.CurrentSite?.Crane) is { } crane && Casting >= 0 && Casting < crane.Castings.Length)
                    {
                        var c = crane.Castings[Casting];
                        var home = crane.Where(c, train.Frames) is { } h ? h + Double3.Up * 0.8 : (Double3?)null;
                        // Note 991: over the crane, and back onto its casting; rigged or lifted meanwhile, it hurries back to it
                        // (onto a car with it, if it's craned aboard first).
                        if (_trip is { } trip && home is { } back)
                        {
                            _tripGone += (c.State == CastingState.Stacked ? t.RoamPace : t.RoamHurry) * SimConstants.TickSeconds;
                            if (Along(trip, back, _tripGone) is { } on)
                            {
                                Local = on;
                                return;
                            }
                        }
                        _trip = null;
                        if (t.RoamEvery > 0 && c.State == CastingState.Stacked && !crane.Wrecked && home is not null)
                        {
                            _gnawing += SimConstants.TickSeconds;
                            if (_gnawing >= t.RoamEvery)
                            {
                                _gnawing = 0;
                                _tripGone = 0;
                                _ground = crane.Corner(0, 0).Y;
                                _trip = CraneTrip(crane, home.Value, t.RoamRun);
                            }
                        }
                        if (c.State == CastingState.Loaded && c.Car >= 0 && c.Car < train.Frames.Count)
                        {
                            // C.9's Grumbler row (note 190): aboard, and who ran the crane for that lift.
                            int op = ctx.World.Attribution.CraneOperator;
                            ctx.World.Attribution.Add(Run.IncidentLog.Event(ctx.World, Run.IncidentKind.Aboard, $"Grumbler craned aboard car {c.Car}", op,
                                op >= 0 ? "Crane: {actor}." : "Nobody at the crane.", c.Car));
                            Attached = c.Car;
                            Local = c.At + Double3.Up * 0.8;
                            Extra = -1;
                            return;
                        }
                        if (crane.Where(c, train.Frames) is { } at)
                            Local = at + Double3.Up * 0.8;
                    }
                    // Aboard: it eats the cargo.
                    if (Attached >= 0 && Attached < train.Frames.Count)
                    {
                        var v = train.Vehicles[Attached];
                        v.CargoIntegrity = Math.Max(0, v.CargoIntegrity - t.CargoPerSecond * SimConstants.TickSeconds);
                    }
                    return;
                }
            case SpinePhase.Telegraph or SpinePhase.Commit:
                if (Phase == SpinePhase.Telegraph && PhaseSeconds >= ctx.Tuning.MinReactionSeconds)
                    Enter(ctx, SpinePhase.Commit);
                if (Phase == SpinePhase.Commit)
                    Hunt(ctx, t);
                return;
            case SpinePhase.Grab:
                return;
            case SpinePhase.BreakOff when t.DrivenOff:
                Flee(ctx, t);
                return;
            default:
                Enter(ctx, SpinePhase.Gone);
                return;
        }
    }

    /// <summary>
    /// GANG UP (note 288): its prey among friends, two or more of the crew within <see cref="GrumblerTuning.GangRadius"/> of it,
    /// and no gang striking it: held for <see cref="GrumblerTuning.OutnumberedSeconds"/>, it lets go of anyone it's mauling and
    /// breaks off. True the tick it does.
    /// </summary>
    bool Outnumbered(EnemyContext ctx, GrumblerTuning t)
    {
        var here = WorldPosition(ctx.Train);
        bool crowded = CrewSense.Near(ctx, here, t.GangRadius) >= ctx.Tuning.CoordinatedKill.Gang && !Ganged(ctx);
        _outnumbered = crowded ? _outnumbered + SimConstants.TickSeconds : 0;
        if (_outnumbered < t.OutnumberedSeconds)
            return false;
        _outnumbered = 0;
        // Away from the crew about it (straight across their line, if it's in the middle of them), and it keeps to it.
        var crew = ctx.LivingCrew().Where(c => (c.World - here).Length <= t.GangRadius * 3).Select(c => c.World).ToList();
        var middle = crew.Aggregate(Double3.Zero, (a, b) => a + b) * (1.0 / Math.Max(1, crew.Count));
        var away = (here - middle) with { Y = 0 };
        if (away.Length < 0.2 && crew.Count > 0)
            away = Double3.Cross((crew[0] - here) with { Y = 0 }, Double3.Up);
        _fleeWay = away.Length > 1e-6 ? away.Normalized : new Double3(1, 0, 0);
        Extra2 = 0; // calmed: when it comes back, it's to gnaw
        Extra = -1;
        if (Attached != Loose)
        {
            Local = here;
            Attached = Loose;
        }
        Enter(ctx, SpinePhase.BreakOff);
        return true;
    }

    /// <summary>Driven off: away from the crew at its hunting pace, for a while; then back to gnawing, calmed.</summary>
    void Flee(EnemyContext ctx, GrumblerTuning t)
    {
        if (PhaseSeconds >= t.FleeSeconds)
        {
            Enter(ctx, SpinePhase.Telegraph);
            return;
        }
        var here = WorldPosition(ctx.Train);
        Attached = Loose;
        Local = here + _fleeWay * t.HuntSpeed * SimConstants.TickSeconds;
    }

    /// <summary>FERAL: after whoever hit it last, biting; one it's beaten down, it mauls (a grab the gang can break).</summary>
    void Hunt(EnemyContext ctx, GrumblerTuning t)
    {
        var train = ctx.Train;
        if (ctx.Crew.FirstOrDefault(c => c.Player.Id == LastHitBy).Player.State is not { Alive: true } prey || LastHitBy < 0)
        {
            Extra2 = 0;
            Enter(ctx, SpinePhase.BreakOff);
            Enter(ctx, SpinePhase.Telegraph);
            return;
        }
        var here = WorldPosition(train);
        var there = PlayerMotor.WorldPosition(prey, train);
        var to = there - here;
        if (to.Length > t.Reach)
        {
            Attached = Loose;
            Local = here + to.Normalized * Math.Min(t.HuntSpeed * SimConstants.TickSeconds, to.Length - t.Reach * 0.8);
            return;
        }
        _bite += SimConstants.TickSeconds;
        if (_bite < t.BiteEvery)
            return;
        _bite = 0;
        if (prey.Health <= t.GrabBelowHealth && Grab(ctx, LastHitBy, t.MaulSeconds))
            return;
        ctx.Bite(LastHitBy, t.BiteDamage, DeathCause.Grumbler);
    }

    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        _hits.Add((by, ctx.Tick));
        Extra2 = 1; // FERAL
        // Struck up on the crane (note 991): it drops, to the ground under it.
        if (_trip is not null)
        {
            Local = Local with { Y = _ground };
            _trip = null;
        }
        if (ctx.Tuning.Grumbler.DrivenOff)
            Coordinated(ctx, by, damage);
        else
            base.Struck(ctx, by, damage);
        if (!Gone && Phase == SpinePhase.Telegraph && Extra >= 0)
            Extra = -1; // off the crates
    }

    /// <summary>
    /// Note 288: a blow makes it feral at whoever struck it and breaks a maul (a friend's), but only the gang's blows hurt it:
    /// two or more crewmates inside the window. Killed by them, it's gone for the night.
    /// </summary>
    void Coordinated(EnemyContext ctx, int by, double damage)
    {
        Marked(ctx, by);
        if (Phase == SpinePhase.BreakOff)
            Enter(ctx, SpinePhase.Telegraph); // struck as it goes: it turns on them
        if (Ganged(ctx))
        {
            Health -= damage;
            if (Health <= 0)
            {
                Slay(ctx);
                return;
            }
        }
        if (Phase == SpinePhase.Grab && by != Holding)
            Rescued(ctx, by);
    }


    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.Grumbler);
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Telegraph);
    }

    protected override void Rescued(EnemyContext ctx, int by)
    {
        base.Rescued(ctx, by);
        Enter(ctx, SpinePhase.Telegraph);
    }
}
