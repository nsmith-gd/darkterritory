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
                    .OrderBy(c => Math.Abs(train.Dynamics.Consist.IndexOf(c.Player.State.Parent) - train.Dynamics.Consist.IndexOf(Attached)))
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
                                    train.Uncouple(ahead);
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

    /// <summary>Only killing it frees its victim (App. A.8 "the victim can't break free alone").</summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        Health -= damage;
        if (Health > 0)
            return;
        Release(ctx);
        Enter(ctx, SpinePhase.Gone);
    }
}

/// <summary>
/// THE SWITCHMAN · vibration · corrupted human (GDD v1.1 §21, App. A.8). Half railway worker, half something spindly and
/// wrong, waiting at a junction ahead: a tall figure at the lever in the headlamp, the junction lamp showing the wrong signal
/// (it's thrown it). The train takes the wrong route as it passes (a dead line: the clock's cost). Sometimes, instead, it
/// throws the switch under the train to derail it, and that telegraphs too: it grips the lever and the lamp flickers, always
/// in time to brake or fire. Rule: kill the Switchman before the switch. One cannon shot, or stop and club it.
/// </summary>
/// <remarks><see cref="Enemy.Extra"/> is the branch whose switch it works; <see cref="Enemy.Extra2"/> is 1 when it means to derail.</remarks>
public sealed class Switchman(int id) : Enemy(id)
{
    public override EnemyKind Kind => EnemyKind.Switchman;
    public override PressureZone Zone => PressureZone.Corrupted;
    public override Sense Sense => Sense.Vibration;
    public override Want Want => Want.Cargo;
    public override bool OnMainLine => true;
    /// <summary>At his lever, revealed (the telegraph on): "one cannon shot, or stop and club it" (GDD §21).</summary>
    public override double MeleeRadius => Phase is SpinePhase.Telegraph or SpinePhase.Commit ? 0.7 : 0;
    public override bool GunAnswers => true;
    public int Branch => (int)Extra;
    public bool Derailer => Extra2 > 0.5;

    /// <summary>
    /// C.9's Switchman row (note 190) for its PUNISH short of a derailment (whose own record says it): the train down the dead
    /// line, or the points split under a crawling engine. Whether the forward cannon was crewed, and by whom.
    /// </summary>
    protected override Run.Incident? Punished(EnemyContext ctx)
    {
        if (ctx.World.Derailed)
            return null;
        var (gunner, cannon) = Run.IncidentLog.ForwardCannon(ctx.World);
        string what = Derailer ? "Split the Switchman's points under the engine" : "Switchman threw the train down the dead line";
        return Run.IncidentLog.Event(ctx.World, Run.IncidentKind.Points, what, gunner, cannon);
    }
    /// <summary>Gripping the lever to throw it under the train (the derail's telegraph: the lamp flickers).</summary>
    public bool Gripping => Derailer && Phase == SpinePhase.Commit;

    /// <summary>A dead line's points ahead, inside the window, set for the main line: where it waits (App. B.8), or null.</summary>
    public static Rail.Branch? Junction(World world, SwitchmanTuning t)
    {
        var train = world.Train;
        if (!train.OnMain)
            return null;
        double front = train.Dynamics.Distance;
        return train.Line.Branches.Where(b => b.Kind == Rail.BranchKind.DeadLine && !train.Diverging(b.Index)
                && b.Toe - front >= t.Ahead[0] && b.Toe - front <= t.Ahead[1])
            .OrderBy(b => b.Toe).FirstOrDefault();
    }

    public static Switchman At(int id, Rail.Branch branch, SwitchmanTuning t, double leverOffset, bool derail) => new(id)
    {
        Extra = branch.Index,
        Extra2 = derail ? 1 : 0,
        LineDistance = branch.Toe,
        Lateral = branch.Side * (leverOffset + t.StandBeside),
        Health = t.Health,
    };

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Switchman;
        var train = ctx.Train;
        if (Branch < 0 || Branch >= train.Line.Branches.Count)
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        var branch = train.Line.Branches[Branch];
        var engine = train.Dynamics;
        double ahead = branch.Toe - engine.Distance;
        switch (Phase)
        {
            case SpinePhase.Dormant:
                // At the lever. The routing kind has already thrown it: the lamp reads wrong.
                if (!Derailer && !ctx.World.SetSwitch(Branch, true))
                {
                    Enter(ctx, SpinePhase.Gone);
                    return;
                }
                if (ahead <= t.RevealAt)
                    Enter(ctx, SpinePhase.Telegraph);
                return;
            case SpinePhase.Telegraph:
                if (!Derailer)
                {
                    // Set back by hand, it's lost this one.
                    if (!train.Diverging(Branch))
                    {
                        Enter(ctx, SpinePhase.BreakOff);
                        Enter(ctx, SpinePhase.Gone);
                    }
                    else if (engine.Path == Branch && engine.Distance > branch.Toe && Enter(ctx, SpinePhase.Commit))
                    {
                        // THROW: the train's taken the points, down the dead line: the clock's cost, never a life.
                        Enter(ctx, SpinePhase.Punish);
                    }
                    else if (engine.Distance > branch.Toe + engine.Consist.LengthMetres)
                        Enter(ctx, SpinePhase.Gone);
                    return;
                }
                // DERAIL: it grips the lever in time to brake or fire.
                if (ahead <= t.GripAt && PhaseSeconds >= ctx.Tuning.MinReactionSeconds)
                    Enter(ctx, SpinePhase.Commit);
                return;
            case SpinePhase.Commit:
                // Under the train: the engine's over the points and the rest isn't.
                if (engine.Path == Rail.RailLine.MainPath && engine.Distance > branch.Toe + 2 && engine.RearDistance < branch.Toe - 2)
                {
                    ctx.World.SetSwitch(Branch, true);
                    double v = engine.Speed;
                    if (v > t.DerailAbove)
                    {
                        // C.9's Switchman row (note 190): not the throttle, but whether the forward cannon was crewed, and by whom.
                        var (gunner, cannon) = Run.IncidentLog.ForwardCannon(ctx.World);
                        ctx.World.Derail($"the Switchman threw the points under it at {v * 3.6:0} km/h (over {t.DerailAbove * 3.6:0} km/h they throw a train off)",
                            gunner, cannon);
                    }
                    else
                    {
                        // Run through at a crawl: the points split, the engine's wrenched about and brought up short.
                        var front = train.Vehicles[engine.Consist.Vehicles[0].Id];
                        front.Integrity = Math.Max(0, front.Integrity - t.RunThroughDamage);
                        foreach (var rake in train.Rakes)
                            rake.Velocity = 0;
                    }
                    Enter(ctx, SpinePhase.Punish);
                    return;
                }
                // Stopped short of it, or got by whole: it's done.
                if (engine.RearDistance > branch.Toe + 2)
                    Enter(ctx, SpinePhase.Gone);
                return;
            case SpinePhase.Punish:
                if (PhaseSeconds >= t.LingerSeconds)
                {
                    Enter(ctx, SpinePhase.BreakOff);
                    Enter(ctx, SpinePhase.Gone);
                }
                return;
            default:
                Enter(ctx, SpinePhase.Gone);
                return;
        }
    }
}

/// <summary>
/// GRUMBLER · sound · corrupted human (GDD v1.1 §21, App. A.8). A labourer that still works the crane, after a fashion: it
/// scuttles like a spider over the crane and the food crates at a facility, gnawing them (audible gnawing; visible on the
/// crates, the telegraph). Interrupt it or hit it and it goes feral, hunting whoever hit it last. It heals if only one
/// player has hit it in the last few seconds: no one player can kill it. Craned aboard with a crate by mistake, it eats the
/// cargo on the train. Rule: gang up or leave it alone. A spotter checks the crates before every lift.
/// </summary>
/// <remarks>
/// <see cref="Enemy.Extra"/> is the crane casting it's on (its index), −1 once it's off them; aboard,
/// <see cref="Enemy.Attached"/> is the car it's eating in. <see cref="Enemy.Extra2"/> is 1 once it's feral.
/// </remarks>
public sealed class Grumbler(int id) : Enemy(id)
{
    readonly List<(int By, uint Tick)> _hits = [];
    double _bite;

    public override EnemyKind Kind => EnemyKind.Grumbler;
    public override PressureZone Zone => PressureZone.Corrupted;
    public override Sense Sense => Sense.Sound;
    public override Want Want => Want.Cargo;
    public override double MeleeRadius => 0.8;
    public bool Feral => Extra2 > 0.5;
    public int Casting => (int)Math.Round(Extra);

    public static Grumbler OnCrates(int id, Double3 world, int casting, GrumblerTuning t) =>
        new(id) { Attached = Loose, Local = world, Extra = casting, Health = t.Health };

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Grumbler;
        var train = ctx.Train;
        // REGEN: one player alone at it, and it heals.
        uint window = (uint)Math.Round(t.GangSeconds * SimConstants.TickRate);
        _hits.RemoveAll(h => ctx.Tick - h.Tick > window);
        if (_hits.Select(h => h.By).Distinct().Count() <= 1)
            Health = Math.Min(t.Health, Health + t.RegenPerSecond * SimConstants.TickSeconds);
        switch (Phase)
        {
            case SpinePhase.Dormant:
                Enter(ctx, SpinePhase.Telegraph); // gnawing on the crates
                return;
            case SpinePhase.Telegraph when !Feral:
                {
                    // On its casting: where the casting goes, it goes. Craned onto a car: LOADED, it's aboard.
                    if (ctx.World.Run?.CurrentSite?.Crane is { } crane && Casting >= 0 && Casting < crane.Castings.Length)
                    {
                        var c = crane.Castings[Casting];
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
            default:
                Enter(ctx, SpinePhase.Gone);
                return;
        }
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
        ctx.Bite(LastHitBy, t.BiteDamage, DeathCause.Mauled);
    }

    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        _hits.Add((by, ctx.Tick));
        Extra2 = 1; // FERAL
        base.Struck(ctx, by, damage);
        if (!Gone && Phase == SpinePhase.Telegraph && Extra >= 0)
            Extra = -1; // off the crates
    }

    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.Mauled);
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Telegraph);
    }

    protected override void Rescued(EnemyContext ctx, int by)
    {
        base.Rescued(ctx, by);
        Enter(ctx, SpinePhase.Telegraph);
    }
}
