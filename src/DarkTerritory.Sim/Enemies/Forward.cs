using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// TRACK DOLL · sight · forward (GDD v1.1 §21, App. A.2). A large porcelain doll standing on the rails; its white face
/// shines in the lamp out to 200 m. Rule: stop before you hit the doll. Stop short and it's gone for the run. Hit it and
/// it haunts the train: it giggles in the cars, admires the cargo (and costs some), and plays with the cab's controls
/// whenever the cab is empty. It vanishes when approached from one side; come at it from both ends of its car at once and
/// it's cornered, and two players can club it. Or give it a toy, and it steals it and leaves. No lethal punish of its own:
/// it costs speed control and cargo, and it's the reason someone stays in the cab (App. A.2, replacing the Deadman).
/// A forward cannon's ball that finds her on the rail shatters her (T121 playtest: "I shot the track doll and it did
/// nothing"): gone for the run, the same as stopping short, so the train passes where she stood. The answer's loud (the
/// meter) and costs the reload; she's a cost-only enemy, and the cannon pays the cost another way.
/// </summary>
/// <remarks>
/// On the rail it's free on the main line (<see cref="Enemy.LineDistance"/>). Haunting (<see cref="SpinePhase.Punish"/>)
/// it's in a car (<see cref="Enemy.Attached"/>), or in the cab with <see cref="Enemy.Extra2"/> = 1: tampering.
/// </remarks>
public sealed class TrackDoll(int id) : Enemy(id)
{
    double _moveIn;
    int _hop;

    public override EnemyKind Kind => EnemyKind.TrackDoll;
    public override PressureZone Zone => PressureZone.Forward;
    public override Sense Sense => Sense.Sight;
    public override Want Want => Want.Split;
    public override bool OnMainLine => Attached < 0;
    /// <summary>Only cornered can it be struck (App. A.2 CORNERED).</summary>
    public override double MeleeRadius => Phase == SpinePhase.Punish && Cornered ? 0.6 : 0;
    /// <summary>
    /// Standing on the rail ahead (not yet struck), she can be shot (T121): a hit volume this big round her body
    /// (enemies.json <c>trackDoll.railHitRadius</c>; 0 with <c>cannonShatters</c> off). Aboard, the cannon can't reach her.
    /// </summary>
    public override double HitRadius => Attached < 0 && Phase is SpinePhase.Dormant or SpinePhase.Telegraph ? RailHitRadius : 0;
    public override double HitHeight => Attached < 0 ? RailHitHeight : 0;
    /// <summary>Her hit volume on the rail: set from tuning where she's put there (a client's mirror keeps the defaults).</summary>
    public double RailHitRadius { get; init; } = 0.55;
    public double RailHitHeight { get; init; } = 0.7;
    /// <summary>Shattered by a cannonball (T121), not stopped short of: the same end, but the porcelain's in pieces.</summary>
    public bool Shattered { get; private set; }

    /// <summary>At the controls of an empty cab (App. A.2 TAMPER): replicated, so a predicting client drives as it does.</summary>
    public bool Tampering => Phase == SpinePhase.Punish && Attached == 0 && Extra2 > 0.5;
    /// <summary>Approached from both ends of its car at once (<see cref="Enemy.Extra"/> = 1): it can't vanish.</summary>
    public bool Cornered => Extra > 0.5;
    public bool Haunting => Phase == SpinePhase.Punish;

    /// <summary>On the rail this far ahead of the engine.</summary>
    public static TrackDoll Ahead(int id, TrainOnLine train, double ahead, TrackDollTuning t) => new(id)
    {
        LineDistance = train.Dynamics.Distance + ahead,
        Height = 0,
        Health = t.Health,
        RailHitRadius = t.CannonShatters ? t.RailHitRadius : 0,
        RailHitHeight = t.RailHitHeight,
    };

    /// <summary>
    /// Whether the main line runs straight for the doll's sightline ending <paramref name="at"/> (App. B.2 "straight track
    /// with a clear 200 m sightline"): the rail's heading changes by less than a few degrees over it.
    /// </summary>
    public static bool Straight(TrainOnLine train, double at, double length)
    {
        if (at > train.Line.Length - 5 || at - length < 0)
            return false;
        var first = train.Line.Sample(at - length).Tangent;
        for (double s = at - length; s <= at; s += 25)
            if (Double3.Dot(first, train.Line.Sample(s).Tangent) < DMath.Cos(3 * Math.PI / 180))
                return false;
        return true;
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.TrackDoll;
        var train = ctx.Train;
        var engine = train.Dynamics;
        switch (Phase)
        {
            case SpinePhase.Dormant or SpinePhase.Telegraph:
                {
                    if (!train.Line.OnMain(engine.Path, LineDistance))
                        return;
                    double ahead = LineDistance - engine.Distance;
                    // TELEGRAPH: the porcelain catches the lamp at 200 m; without it, only close in.
                    if (Phase == SpinePhase.Dormant && ahead > 0
                        && (ctx.World.LampShining && ahead <= t.LampRevealDistance || ahead <= t.DarkRevealDistance))
                        Enter(ctx, SpinePhase.Telegraph);
                    // Stopped short: gone, no threat for the rest of the run.
                    if (Phase == SpinePhase.Telegraph && ahead > 0 && engine.Speed < t.StoppedBelow)
                    {
                        Enter(ctx, SpinePhase.BreakOff);
                        Enter(ctx, SpinePhase.Gone);
                        return;
                    }
                    // Behind the engine now, whoever's line it was on.
                    if (ahead < -2 && Phase == SpinePhase.Dormant && engine.Speed < t.StoppedBelow)
                        return;
                    if (ahead <= 0.5 && engine.Speed >= t.StoppedBelow)
                    {
                        // Struck. With a fair window it's aboard; struck unseen (no window), it's only broken.
                        if (Enter(ctx, SpinePhase.Commit) && Enter(ctx, SpinePhase.Punish))
                            Haunt(ctx);
                        else
                        {
                            // C.9 "Track Doll struck" all the same: the throttle, and the speed.
                            ctx.World.Attribution.Add(Run.IncidentLog.Struck(ctx.World, "Struck the Track Doll before she could be seen"));
                            Enter(ctx, SpinePhase.Gone);
                        }
                    }
                    return;
                }
            case SpinePhase.Punish:
                HauntTick(ctx, t);
                return;
            default:
                Enter(ctx, SpinePhase.Gone);
                return;
        }
    }

    /// <summary>C.9's Track Doll row (note 190): struck, with who was on the throttle and the speed at impact.</summary>
    protected override Run.Incident? Punished(EnemyContext ctx) => Run.IncidentLog.Struck(ctx.World, "Struck the Track Doll");

    /// <summary>Aboard: into a car with a room, the one farthest from anyone.</summary>
    void Haunt(EnemyContext ctx)
    {
        _moveIn = ctx.Tuning.TrackDoll.HauntMoveSeconds;
        MoveTo(ctx, null);
    }

    void MoveTo(EnemyContext ctx, int? from)
    {
        var train = ctx.Train;
        var rooms = train.Dynamics.Consist.Vehicles.Select(v => v.Id)
            .Where(id => id > 0 && id != from && train.Frames[id].Shape.Interior is not null && !CrewSense.Occupied(ctx, id)).ToList();
        if (rooms.Count == 0)
            rooms = [.. train.Dynamics.Consist.Vehicles.Select(v => v.Id).Where(id => id > 0 && train.Frames[id].Shape.Interior is not null)];
        if (rooms.Count == 0)
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        // Deterministic: the hop count picks among the rooms, no dictionary or clock in it.
        int car = rooms[_hop++ % rooms.Count];
        var room = train.Frames[car].Shape.Interior!.Value;
        Attached = car;
        Local = room.Centre with { Y = room.Min.Y };
        Extra = 0;
        Extra2 = 0;
    }

    void HauntTick(EnemyContext ctx, TrackDollTuning t)
    {
        var train = ctx.Train;
        if (Attached >= train.Frames.Count || Attached >= 0 && train.Dynamics.Consist.IndexOf(Attached) < 0 && Attached != 0)
        {
            // Its car was cut loose: it goes with it.
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        // APPEASED: a toy held out to it, and it's gone for the run with the toy.
        var at = WorldPosition(train);
        foreach (var (p, _) in ctx.Crew)
        {
            if (!p.State.Alive || ctx.World.Bodies.CarriedBy(p.Id) is not { Kind: BodyKind.Toy } toy)
                continue;
            if ((PlayerMotor.WorldPosition(p.State, train) - at).Length <= t.ToyReach)
            {
                ctx.World.Bodies.Remove(toy);
                Enter(ctx, SpinePhase.BreakOff);
                Enter(ctx, SpinePhase.Gone);
                return;
            }
        }
        // TAMPER: the cab left empty long enough, it's in there at the controls.
        bool cabEmpty = ctx.World.CabEmptySeconds >= t.TamperAfterEmpty;
        if (Attached == 0)
        {
            bool someone = ctx.Crew.Any(c => c.Player.State.Alive && PlayerMotor.InCab(c.Player.State, train));
            if (someone)
            {
                // Somebody's back: it's off to a car.
                Extra2 = 0;
                MoveTo(ctx, 0);
                return;
            }
            Extra2 = 1;
            return;
        }
        if (cabEmpty && train.Frames[0].Shape.Cab is { } cab)
        {
            Attached = 0;
            Local = cab.Centre with { Y = cab.Min.Y };
            Extra = 0;
            Extra2 = 1;
            return;
        }
        // Admiring the cargo: it costs a little of what's in its car.
        var car = train.Vehicles[Attached];
        car.CargoIntegrity = Math.Max(0, car.CargoIntegrity - t.CargoPerSecond * SimConstants.TickSeconds);
        // CORNERED: players at both ends of its car around it. Otherwise approached, it vanishes to another car.
        var frame = train.Frames[Attached];
        var here = Local.Z;
        var inCar = ctx.Crew.Where(c => c.Player.State is { Alive: true } s && s.Parent == Attached && frame.Shape.Interior is { } r && r.Contains(s.Position))
            .Select(c => c.Player.State.Position).ToList();
        bool before = inCar.Any(p => p.Z < here && here - p.Z <= t.CornerWithin);
        bool behind = inCar.Any(p => p.Z > here && p.Z - here <= t.CornerWithin);
        Extra = before && behind ? 1 : 0;
        if (!Cornered && inCar.Any(p => (p - Local).Length <= t.VanishNear))
        {
            MoveTo(ctx, Attached);
            return;
        }
        _moveIn -= SimConstants.TickSeconds;
        if (_moveIn <= 0 && !Cornered)
        {
            _moveIn = t.HauntMoveSeconds;
            MoveTo(ctx, Attached);
        }
    }

    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        if (!Cornered)
            return;
        base.Struck(ctx, by, damage);
    }

    /// <summary>
    /// A cannonball on the rail (T121): shattered, whatever her health, and gone for the run by the same way out as stopping
    /// short (BREAK OFF, then gone), so nothing's left on the rail for the train to strike and nothing to haunt it.
    /// </summary>
    public override bool Hit(EnemyContext ctx, double damage)
    {
        if (HitRadius <= 0 || Gone)
            return false;
        Shattered = true;
        Health = 0;
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Gone);
        return true;
    }

    /// <summary>Plays with the throttle and the brake, in turns of a few seconds (from replicated state, so clients agree).</summary>
    public override void Tamper(World world, ref TrainControls controls)
    {
        if (!Tampering)
            return;
        int beat = (int)(PhaseSeconds / 3) % 3;
        controls.Throttle = beat == 0 ? 1 : 0;
        controls.Brake = beat == 2 ? 1 : 0;
    }
}
