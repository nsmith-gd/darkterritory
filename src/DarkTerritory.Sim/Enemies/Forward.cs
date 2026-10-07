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
/// She escalates only if ignored (the director, 6 Oct 2026; note 268): at first she only haunts the cars; left alone long
/// enough she takes an empty cab and nudges the regulator; left alone longer, she works the controls in earnest, and can let
/// a standing train off its brake. Each stage is telegraphed (restless) well before it comes, and attention winds her back.
/// A forward cannon's ball that finds her on the rail shatters her (T121 playtest: "I shot the track doll and it did
/// nothing"): gone for the run, the same as stopping short, so the train passes where she stood. The answer's loud (the
/// meter) and costs the reload; she's a cost-only enemy, and the cannon pays the cost another way.
/// </summary>
/// <remarks>
/// On the rail it's free on the main line (<see cref="Enemy.LineDistance"/>). Haunting (<see cref="SpinePhase.Punish"/>)
/// it's in a car (<see cref="Enemy.Attached"/>), or in the cab: tampering. <see cref="Enemy.Extra2"/> is her escalation
/// (<see cref="Escalation"/>: the stage, plus a half while she's restless for the next), negative in a car and positive at
/// the controls; <see cref="Enemy.Extra"/> is cornered (1) in a car, and in the cab the seconds she's been at the controls at her last stage.
/// </remarks>
public sealed class TrackDoll(int id) : Enemy(id)
{
    double _moveIn;
    int _hop;
    /// <summary>Host: seconds of neglect (note 268), and the escalation they come to.</summary>
    double _neglect;
    double _level = 1;

    public override EnemyKind Kind => EnemyKind.TrackDoll;
    public override PressureZone Zone => PressureZone.Forward;
    public override Sense Sense => Sense.Sight;
    public override Want Want => Want.Split;
    public override bool OnMainLine => Attached < 0;
    /// <summary>Only cornered can it be struck (App. A.2 CORNERED).</summary>
    public override double MeleeRadius => Phase == SpinePhase.Punish && Cornered ? 0.6 : 0;
    /// <summary>
    /// Standing on the rail ahead (not yet struck), she can be shot (T121): her body is enemies.json <c>bodies</c>'
    /// (none with <c>trackDoll.cannonShatters</c> off). Aboard, haunting, the cannon can't reach her.
    /// </summary>
    public override bool Exposed => Shatters && Attached < 0 && Phase is SpinePhase.Dormant or SpinePhase.Telegraph;
    public override bool GunAnswers => true;
    /// <summary>A ball shatters her on the rail (tuning <c>cannonShatters</c>; a client's mirror keeps the default).</summary>
    public bool Shatters { get; init; } = true;
    /// <summary>Shattered by a cannonball (T121), not stopped short of: the same end, but the porcelain's in pieces.</summary>
    public bool Shattered { get; private set; }

    /// <summary>At the controls of an empty cab (App. A.2 TAMPER): replicated, so a predicting client drives as it does.</summary>
    public bool Tampering => Phase == SpinePhase.Punish && Attached == 0 && Extra2 > 0.5;
    /// <summary>Approached from both ends of its car at once (<see cref="Enemy.Extra"/> = 1): it can't vanish.</summary>
    public bool Cornered => Attached != 0 && Extra > 0.5;
    public bool Haunting => Phase == SpinePhase.Punish;
    /// <summary>
    /// How far being ignored has brought her (note 268), replicated: 1, 2 or 3 for the stage, plus a half while she's
    /// restless, the next stage under <see cref="TrackDollTuning.WarnSeconds"/> away. 0 before she's aboard.
    /// </summary>
    public double Escalation => Haunting ? Math.Abs(Extra2) : 0;
    /// <summary>1 haunting the cars, 2 at an empty cab's regulator, 3 working the controls (a standing brake too); 0 not aboard.</summary>
    public int Stage => Haunting ? Math.Clamp((int)Escalation, 1, 3) : 0;
    /// <summary>The next stage is coming (her telegraph): the giggle quickens; at the controls she rattles the brake handle.</summary>
    public bool Restless => Haunting && Escalation - Math.Floor(Escalation) > 0.25;
    /// <summary>Seconds at the controls at her last stage, this visit to the cab (replicated in <see cref="Enemy.Extra"/>).</summary>
    public double SecondsAtControls => Tampering ? Extra : 0;

    /// <summary>On the rail this far ahead of the engine.</summary>
    public static TrackDoll Ahead(int id, TrainOnLine train, double ahead, TrackDollTuning t) => new(id)
    {
        LineDistance = train.Dynamics.Distance + ahead,
        Height = 0,
        Health = t.Health,
        Shatters = t.CannonShatters,
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
        _neglect = 0;
        _level = 1;
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
        Extra2 = -_level;
    }

    /// <summary>The escalation <paramref name="neglect"/> seconds of being left alone come to: the stage, plus a half if restless.</summary>
    public static double Level(double neglect, TrackDollTuning t)
    {
        int stage = neglect >= t.ReleaseAfter ? 3 : neglect >= t.ControlsAfter ? 2 : 1;
        double next = stage switch { 1 => t.ControlsAfter, 2 => t.ReleaseAfter, _ => double.PositiveInfinity };
        return stage + (next - neglect <= t.WarnSeconds ? 0.5 : 0);
    }

    /// <summary>
    /// Someone's minding her (note 268): a living crewmate in her car, or within <see cref="TrackDollTuning.AttendRadius"/>.
    /// In the cab nobody's in it (she leaves when anyone comes in), so only someone close outside it.
    /// </summary>
    bool Attended(EnemyContext ctx, Double3 at, TrackDollTuning t) =>
        ctx.Crew.Any(c => c.Player.State is { Alive: true } s
            && (Attached > 0 && s.Parent == Attached || (PlayerMotor.WorldPosition(s, ctx.Train) - at).Length <= t.AttendRadius));

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
        // Ignored, she gets worse; minded, she winds back (the director, 6 Oct 2026; note 268).
        _neglect = Attended(ctx, at, t)
            ? Math.Max(0, _neglect - t.AttendedEase * SimConstants.TickSeconds)
            : _neglect + SimConstants.TickSeconds;
        _level = Level(_neglect, t);
        // TAMPER: past her first stage, the cab left empty long enough, she's in there at the controls.
        bool cabEmpty = ctx.World.CabEmptySeconds >= t.TamperAfterEmpty;
        if (Attached == 0)
        {
            bool someone = ctx.Crew.Any(c => c.Player.State.Alive && PlayerMotor.InCab(c.Player.State, train));
            if (someone || _level < 2)
            {
                // Somebody's back (or she's been minded back to her first stage): she's off to a car.
                MoveTo(ctx, 0);
                return;
            }
            // How long she's been at them at her last stage, this visit: the brake handle worked that long before it goes.
            Extra = _level >= 3 ? Extra + SimConstants.TickSeconds : 0;
            Extra2 = _level;
            return;
        }
        Extra2 = -_level;
        if (cabEmpty && _level >= 2 && train.Frames[0].Shape.Cab is { } cab)
        {
            Attached = 0;
            Local = cab.Centre with { Y = cab.Min.Y };
            Extra = 0;
            Extra2 = _level;
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
    public override bool Hit(EnemyContext ctx, int by, double damage)
    {
        if (!Exposed)
            return false;
        Shattered = true;
        Health = 0;
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Gone);
        return true;
    }

    /// <summary>
    /// Plays with the controls in beats of three seconds, as far as her stage goes (note 268): at stage 2 the regulator
    /// nudged up on one beat in three, the brake left alone; at 3 the regulator open, then shut, then the brake on. From
    /// replicated state, so clients agree. A standing train's held brake stays on unless <see cref="ReleasesStandingBrake"/>.
    /// </summary>
    public override void Tamper(World world, ref TrainControls controls)
    {
        if (!Tampering)
            return;
        controls = Hands(Escalation, PhaseSeconds, (world.Enemies?.TrackDoll ?? new()).NudgeThrottle, controls);
    }

    /// <summary>What her hands make of the controls as set (<see cref="Tamper"/>; the client's lever sounds too).</summary>
    public static TrainControls Hands(double escalation, double phaseSeconds, double nudge, TrainControls set)
    {
        int beat = (int)(phaseSeconds / 3) % 3;
        if (escalation >= 3)
            return set with { Throttle = beat == 0 ? 1 : 0, Brake = beat == 2 ? 1 : 0 };
        return beat == 0 ? set with { Throttle = Math.Max(set.Throttle, nudge) } : set;
    }

    /// <summary>
    /// Her last stage, at the controls <see cref="TrackDollTuning.ReleaseAfterAtControls"/> on this visit: she can let a
    /// standing train off its brake (the director, 6 Oct 2026; note 268). From replicated state.
    /// </summary>
    public override bool ReleasesStandingBrake(World world) =>
        Tampering && Escalation >= 3 && world.Enemies?.TrackDoll is { FinalStageReleasesBrake: true } t && Extra >= t.ReleaseAfterAtControls;
}
