using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>Set pieces for looking at and listening to things headless (screenshots, audio renders, tests).</summary>
public static class Staging
{
    /// <summary>The sim tick staged impacts and hits land on (`dt screenshot --impact`, `--hit-flash`): the scene's clock is set after it.</summary>
    public const uint StrikeTick = 100;

    /// <summary>
    /// A cannonball come down (T121, `dt screenshot --impact`): <paramref name="ahead"/> metres up the line from the engine's
    /// front and <paramref name="lateral"/> to its right, on the ground there (water: as if over it; train and structure: a
    /// metre and a half up a face; creature: a body's height up). "doll" is the staged Track Doll's spot, shattered.
    /// </summary>
    public static Sim.Combat.CannonImpact Impact(TrainOnLine train, string surface, double ahead, double lateral, IReadOnlyList<Enemy>? staged = null)
    {
        bool doll = surface.Equals("doll", StringComparison.OrdinalIgnoreCase);
        if (doll && staged?.FirstOrDefault(e => e is TrackDoll { Attached: < 0 }) is { } d)
            ahead = d.LineDistance - train.Dynamics.Distance;
        var kind = doll ? Sim.Combat.ImpactSurface.Creature : Enum.Parse<Sim.Combat.ImpactSurface>(surface, ignoreCase: true);
        var sample = train.Line.Sample(train.Dynamics.Distance + ahead);
        var right = Double3.Cross(sample.Tangent, Double3.Up).Normalized;
        double hint = train.Dynamics.Distance + ahead;
        var at = sample.Position + right * lateral;
        at = at with { Y = Sim.Player.PlayerMotor.GroundAt(at, train.Line, ref hint) };
        if (kind is Sim.Combat.ImpactSurface.Train or Sim.Combat.ImpactSurface.Structure)
            at += Double3.Up * 1.5;
        else if (kind == Sim.Combat.ImpactSurface.Creature)
            at += Double3.Up * (doll ? 0.7 : 0.8);
        // Fired from the engine's gun, behind and above: the way the ball was going.
        var muzzle = train.Frames[0].ToWorld(new Double3(0, 5.3, -train.Frames[0].Shape.HalfLength + 12.8));
        return new Sim.Combat.CannonImpact(1, StrikeTick, at, (at - muzzle).Normalized, kind, 1, doll ? EnemyKind.TrackDoll : 0);
    }

    /// <summary>
    /// A blow landed on each staged creature (T121, `dt screenshot --hit-flash`), struck from the camera's side so its flinch
    /// is seen: the hit at its middle, a melee blow.
    /// </summary>
    public static List<Sim.Combat.HitConfirm> HitsOn(IReadOnlyList<Enemy> staged, TrainOnLine train, Double3 eye)
    {
        var hits = new List<Sim.Combat.HitConfirm>();
        foreach (var e in staged)
        {
            if (e.Gone || e.Kind is EnemyKind.Sleepers or EnemyKind.Drift or EnemyKind.CarFire)
                continue;
            var at = GreyboxPosition(e, train) + Double3.Up * 0.8;
            var from = (at - eye) with { Y = 0 };
            hits.Add(new Sim.Combat.HitConfirm(hits.Count + 1, StrikeTick, e.Id, e.Kind, 1, Sim.Combat.HitSource.Melee, at,
                from.Length > 1e-6 ? from.Normalized : new Double3(0, 0, -1), false));
        }
        return hits;
    }

    static Double3 GreyboxPosition(Enemy e, TrainOnLine train) =>
        e.Attached >= train.Frames.Count ? e.Local : e.WorldPosition(train);
    /// <summary>Crates, a lamp and a rescued child on car 2's roof and a body on car 3's, dropped and left to settle.</summary>
    public static Sim.Physics.Bodies Bodies(TrainOnLine train, string content)
    {
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
        var player = DataFile.Load<Sim.Player.PlayerTuning>(Path.Combine(content, Sim.Player.PlayerTuning.File));
        var bodies = new Sim.Physics.Bodies();
        double roof = tuning.Geometry.CarHeight;
        bodies.SpawnCrate(train, 2, new Double3(0.5, roof + 0.3, 1.0)).Yaw = 0.4;
        bodies.SpawnCrate(train, 2, new Double3(-0.4, roof + 0.6, 2.2)).Yaw = -0.3;
        bodies.SpawnCrate(train, 2, new Double3(0.1, roof + 0.2, -1.5), Sim.Physics.BodyKind.Lamp);
        // A rescued child (GDD §19), set down on the roof by the lamp.
        bodies.SpawnCrate(train, 2, new Double3(-0.6, roof + 0.4, -0.4), Sim.Physics.BodyKind.Child);
        var dead = Sim.Player.PlayerMotor.SpawnOnRoof(train, 3, -2, player) with { Health = 0, Yaw = 1.2 };
        bodies.SpawnRagdoll(train, 9, dead);
        for (int i = 0; i < 90; i++)
            bodies.Step(train, tuning, _ => null);
        return bodies;
    }

    /// <summary>
    /// A crewmate on car 2's roof with a body over their shoulder (App. C.4), carried a second and a half in the sim's
    /// fireman's carry (Bodies.Shoulder) so it's hanging as it would; <paramref name="walking"/>, stepping along the roof.
    /// </summary>
    /// <param name="child">The rescued child instead, in their arms (Bodies.ChildAt, the cradle).</param>
    public static (Sim.Physics.Bodies Bodies, Crewmate Carrier) Shouldered(TrainOnLine train, string content, bool walking = false, bool child = false)
    {
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
        var player = DataFile.Load<Sim.Player.PlayerTuning>(Path.Combine(content, Sim.Player.PlayerTuning.File));
        var bodies = new Sim.Physics.Bodies();
        var carrier = Sim.Player.PlayerMotor.SpawnOnRoof(train, 2, -3, player) with { Yaw = Math.PI - 0.6 };
        var dead = Sim.Player.PlayerMotor.SpawnOnRoof(train, 2, -2, player) with { Health = 0, Yaw = 0.4 };
        var body = child ? bodies.SpawnCrate(train, 2, new Double3(0, tuning.Geometry.CarHeight + 0.4, -2.6), Sim.Physics.BodyKind.Child)
            : bodies.SpawnRagdoll(train, 9, dead);
        body.Carrier = 1;
        for (int i = 0; i < 90; i++)
            bodies.Step(train, tuning, id => id == 1 ? carrier : null);
        var (feet, yaw) = (Sim.Player.PlayerMotor.WorldPosition(carrier, train), Sim.Player.PlayerMotor.WorldYaw(carrier, train));
        var act = child ? walking ? Art.CrewPose.CradleWalk : Art.CrewPose.Cradle : walking ? Art.CrewPose.ShoulderWalk : Art.CrewPose.Shoulder;
        return (bodies, new Crewmate(1, feet, yaw, true, Act: act));
    }

    /// <summary>
    /// The train as it leaves (World.Stock, Guns.Arm): the guard van's stores, lamp, radios and toys, every car's
    /// extinguisher on its mount, each at <paramref name="charge"/> (its sight glass, App. C.5), settled where they lie, and
    /// the guns' full stock of powder and shot.
    /// </summary>
    public static Sim.Physics.Bodies Stocked(TrainOnLine train, string content, double charge = 1)
    {
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
        var world = new Sim.World(train);
        world.Stock();
        Sim.Combat.Guns.Arm(train, DataFile.Load<Sim.Combat.CombatTuning>(Path.Combine(content, Sim.Combat.CombatTuning.File)).Guns);
        foreach (var b in world.Bodies.All)
            if (b.Kind == Sim.Physics.BodyKind.Extinguisher)
                b.Charge = charge;
        for (int i = 0; i < 90; i++)
            world.Bodies.Step(train, tuning, _ => null);
        return world.Bodies;
    }

    /// <summary>
    /// Three crewmates on car 2's roof (T47): a keyboard player, arms at their sides; a headset player pointing down the line
    /// ahead with one hand; and one holding something out in front with both.
    /// </summary>
    public static List<Crewmate> Crew(TrainOnLine train, string content)
    {
        var player = DataFile.Load<Sim.Player.PlayerTuning>(Path.Combine(content, Sim.Player.PlayerTuning.File));
        Crewmate At(byte id, double x, double z, double yaw, Double3 hand = default, Double3 other = default)
        {
            var s = Sim.Player.PlayerMotor.SpawnOnRoof(train, 2, z, player, x) with { Yaw = yaw };
            return new Crewmate(id, Sim.Player.PlayerMotor.WorldPosition(s, train), Sim.Player.PlayerMotor.WorldYaw(s, train), true, hand, other);
        }
        return
        [
            // Facing back down the roof, towards the roof view's camera.
            At(1, -0.9, -5, Math.PI + 0.3),
            // (Pointing down the line ahead, low: an arm raised high read as a salute.)
            At(2, 0.1, -5.5, Math.PI - 0.4, hand: new Double3(0.3, 1.05, -0.55)),
            At(3, 1.0, -4.5, Math.PI + 0.35, hand: new Double3(0.22, 1.15, -0.45), other: new Double3(-0.22, 1.2, -0.42)),
        ];
    }

    /// <summary>
    /// The crew at work (X1, <c>dt screenshot --working</c>; their acts are <see cref="Art.CrewActs"/>' and the clips
    /// crew_clips.py's): on car 2's roof one carrying a crate, one heaving at a hatch, one winding the brake wheel at its
    /// end, one standing by with a crowbar; and on the last gun car, a gunner sat in the cannon's seat.
    /// </summary>
    /// <summary>
    /// A row of the crew down car 2's roof, each at one of <paramref name="acts"/> (CrewPose names, dt screenshot --act
    /// smash,pry,...: the roof view looks at them), facing the camera; the smash and pry with a crowbar in hand, the lantern
    /// with the hand lamp hung from the fist.
    /// </summary>
    /// <param name="survivor">Who they all are (dt screenshot --survivor prisoner|wildlander): freed survivors' figures (App. D.8).</param>
    public static List<Crewmate> Acts(TrainOnLine train, string content, IEnumerable<string> acts, Art.Survivor survivor = Art.Survivor.None)
    {
        var player = DataFile.Load<Sim.Player.PlayerTuning>(Path.Combine(content, Sim.Player.PlayerTuning.File));
        var crew = new List<Crewmate>();
        int i = 0;
        foreach (var name in acts)
        {
            var act = Enum.Parse<Art.CrewPose>(name.Replace("_", ""), ignoreCase: true);
            var s = Sim.Player.PlayerMotor.SpawnOnRoof(train, 2, -6 + 1.5 * i, player, i % 2 == 0 ? -0.5 : 0.5) with { Yaw = Math.PI + (i % 2 == 0 ? 0.5 : -0.5) };
            var tool = act is Art.CrewPose.Smash or Art.CrewPose.Pry ? Sim.Player.Tool.Crowbar : Sim.Player.Tool.None;
            crew.Add(new Crewmate((byte)(20 + i), Sim.Player.PlayerMotor.WorldPosition(s, train), Sim.Player.PlayerMotor.WorldYaw(s, train), true,
                Act: act, Holding: tool, Lamp: act is Art.CrewPose.Lantern or Art.CrewPose.LanternWalk, Survivor: survivor));
            i++;
        }
        return crew;
    }

    public static List<Crewmate> Working(TrainOnLine train, string content)
    {
        var player = DataFile.Load<Sim.Player.PlayerTuning>(Path.Combine(content, Sim.Player.PlayerTuning.File));
        Crewmate At(byte id, int car, double x, double z, double yaw, Art.CrewPose? act, Sim.Player.Tool tool = Sim.Player.Tool.None)
        {
            var s = Sim.Player.PlayerMotor.SpawnOnRoof(train, car, z, player, x) with { Yaw = yaw };
            return new Crewmate(id, Sim.Player.PlayerMotor.WorldPosition(s, train), Sim.Player.PlayerMotor.WorldYaw(s, train), true, Act: act, Holding: tool,
                Lamp: act == Art.CrewPose.Lantern);
        }
        var crew = new List<Crewmate>
        {
            At(5, 2, -0.8, -5.2, Math.PI + 0.4, Art.CrewPose.Carry),
            At(6, 2, 0.5, -6.6, Math.PI - 0.6, Art.CrewPose.Hatch),
            At(7, 2, 0.2, -train.Frames[2].Shape.HalfLength + 0.6, 0.3, Art.CrewPose.Handbrake),
            // Standing by with the crowbar (T108's hotbar), the tool in their fist.
            At(9, 2, -0.3, -3.6, Math.PI - 0.2, null, Sim.Player.Tool.Crowbar),
            // On the next roof back: a hand lamp held out, and the extinguisher on the hip (L0a).
            At(10, 3, 0.4, -2.4, Math.PI + 0.2, Art.CrewPose.Lantern),
            At(11, 3, -0.4, -4.4, Math.PI - 0.3, Art.CrewPose.Extinguish),
        };
        int gun = Enumerable.Range(0, train.Vehicles.Count).LastOrDefault(i => train.Vehicles[i].HasGun, -1);
        if (gun >= 0 && Sim.Combat.Guns.Mount(train, gun) is { } mount)
        {
            var s = Art.CrewActs.Seated(Sim.Player.PlayerMotor.SpawnOnRoof(train, gun, mount.Position.Z, player), mount);
            crew.Add(new Crewmate(8, Sim.Player.PlayerMotor.WorldPosition(s, train), Sim.Player.PlayerMotor.WorldYaw(s, train), true, Act: Art.CrewPose.Gunner));
        }
        return crew;
    }

    /// <summary>
    /// The staged Ribbit pack as it goes (<c>dt screenshot --ribbits</c>): <c>hop</c> after crewmate 4 (alone on the ground
    /// off the train's left, <see cref="Lone"/>), <c>swell</c> lined up on them (App. A.6 TELEGRAPH), <c>tongue</c> on them
    /// (GRAB). Without a mode, they're as <see cref="Threats"/> has them.
    /// </summary>
    public static List<Enemy> Ribbits(List<Enemy> threats, string mode)
    {
        if (mode.Length == 0)
            return threats;
        var phase = mode switch
        {
            "hop" => SpinePhase.Dormant,
            "swell" => SpinePhase.Telegraph,
            "tongue" => SpinePhase.Grab,
            _ => throw new ArgumentException($"--ribbits {mode}: hop, swell or tongue"),
        };
        foreach (var r in threats.OfType<Ribbit>())
            r.Restore(phase, 0.3 + 0.21 * (r.Id - 60), r.Health, r.Attached, r.Local, 0, 0, 0, LoneId, 0);
        return threats;
    }

    /// <summary>Crewmate 4, alone on the ground off the second car's left, facing the Ribbits there (they're after them).</summary>
    public static Crewmate Lone(TrainOnLine train)
    {
        var side = train.Frames[Math.Min(2, train.Frames.Count - 1)];
        var at = side.ToWorld(new Double3(-(side.Shape.HalfWidth + 2.2), 0, -1.5));
        var toward = side.ToWorld(new Double3(-(side.Shape.HalfWidth + 5), 0, -1.5)) - at;
        return new Crewmate(LoneId, at, Math.Atan2(-toward.X, -toward.Z), true, default, default);
    }

    public const byte LoneId = 4;

    /// <summary>
    /// The staged track debris as the kind its id makes it (<c>dt screenshot --threats --debris k</c>, Art/DebrisKit: 0 a
    /// fallen pine, 1 a rockfall, 2 a heap of old ties and a rail), where it lies 40 m up the line in the lamp.
    /// </summary>
    public static List<Enemy> Debris(List<Enemy> threats, string kind)
    {
        if (kind.Length == 0 || threats.OfType<Sleepers>().FirstOrDefault() is not { } was)
            return threats;
        var debris = new Sleepers(int.Parse(kind));
        debris.Restore(was.Phase, was.PhaseSeconds, was.Health, was.Attached, was.Local, was.LineDistance, was.Lateral, was.Height, was.Extra, was.Extra2);
        threats[threats.IndexOf(was)] = debris;
        return threats;
    }
    /// <summary>How far out from its gap the staged Whistler's nest is (a camera on the trail: <c>--whistler nest --view trail</c>).</summary>
    public const double NestOut = 24;

    /// <summary>How far out from its gap the staged Whistler is on its run, carrying its catch (<c>--whistler carry</c>).</summary>
    public const double CarryOut = 7;

    /// <summary>
    /// The staged Whistler as it goes (<c>dt screenshot --whistler</c>): <c>fold</c> hidden in its gap (App. A.4 HIDE),
    /// <c>whistle</c> pulling the cord, <c>watch</c> watching the gap's mouth after (WAIT). The <c>gapside</c> view looks in.
    /// <c>carry</c>: on its run out to its nest with crewmate <see cref="LoneId"/> (<see cref="Carried"/>), the <c>carry</c> view
    /// off its side.
    /// </summary>
    public static List<Enemy> Whistler(List<Enemy> threats, string mode, TrainOnLine? train = null)
    {
        if (mode.Length == 0 || threats.OfType<Whistler>().FirstOrDefault() is not { } w)
            return threats;
        if (mode == "carry" && train is not null && w.Attached >= 0)
        {
            // Carrying its catch off (App. A.4 GRAB): loose, off the train's left, a second into the run out from its gap.
            var f = train.Frames[w.Attached];
            var at = f.ToWorld(w.Local + new Double3(-CarryOut, 0, 0)) with { Y = f.ToWorld(Double3.Zero).Y };
            w.Restore(SpinePhase.Grab, 1.0, w.Health, Enemy.Loose, at, 0, 0, 0, 0, 0, holding: LoneId);
            return threats;
        }
        if (mode == "nest" && train is not null && w.Attached >= 0)
        {
            // At its nest with its catch (App. A.4): loose, off the train's left, the run out from its gap done (GreyboxScene
            // sets it on the land, its trail behind it).
            var f = train.Frames[w.Attached];
            var at = f.ToWorld(w.Local + new Double3(-NestOut, 0, 0)) with { Y = f.ToWorld(Double3.Zero).Y };
            w.Restore(SpinePhase.Grab, 18, w.Health, Enemy.Loose, at, 0, 0, 0, 0, 0, holding: LoneId);
            return threats;
        }
        var (phase, extra) = mode switch
        {
            "fold" => (SpinePhase.Dormant, 0.0),
            "whistle" => (SpinePhase.Telegraph, 1.0),
            "watch" => (SpinePhase.Commit, 0.0),
            _ => throw new ArgumentException($"--whistler {mode}: fold, whistle, watch, carry or nest"),
        };
        w.Restore(phase, 0.4, w.Health, w.Attached, w.Local, 0, 0, 0, extra, 0);
        return threats;
    }

    /// <summary>
    /// The staged Tippy Toesie elsewhere (<c>dt screenshot --tippy</c>): <c>behind</c> crewmate 1, over their shoulder
    /// from the <c>crew</c> view (Crew: they face back down the roof, at its camera); <c>grab</c> on them, its hand over
    /// their mouth (App. A.5 GRAB); <c>in</c> stalking down car 2's aisle, stooped under its roof (the <c>inside</c> view);
    /// <c>door</c> ducking out through car 2's rear end door onto the plate (the <c>door</c> view: note 110).
    /// </summary>
    public static List<Enemy> Tippy(List<Enemy> threats, TrainOnLine train, string mode)
    {
        if (mode.Length == 0 || threats.OfType<TippyToesie>().FirstOrDefault() is not { } tippy)
            return threats;
        int car = Math.Min(2, train.Frames.Count - 1);
        var shape = train.Frames[car].Shape;
        double floor = train.Dynamics.Tuning.Geometry.Interior?.FloorHeight ?? 0, x = train.Dynamics.Tuning.Geometry.PlateX;
        switch (mode)
        {
            case "behind" or "grab":
                tippy.Restore(mode == "grab" ? SpinePhase.Grab : SpinePhase.Telegraph, tippy.PhaseSeconds, tippy.Health, tippy.Attached,
                    tippy.Local with { X = -0.7, Z = -6.7 }, 0, 0, 0, tippy.Extra, tippy.Extra2);
                break;
            case "in":
                tippy.Restore(SpinePhase.Telegraph, 5, 3, car, new Double3(-0.35, floor, -3.6), 0, 0, 0, -1, 0);
                break;
            case "door":
                tippy.Restore(SpinePhase.Telegraph, 5, 3, car, new Double3(x, floor, shape.HalfLength - 0.3), 0, 0, 0, -1, 0);
                break;
            default:
                throw new ArgumentException($"--tippy {mode}: behind, grab, in or door");
        }
        return threats;
    }

    /// <summary>
    /// The staged Gaunt as it goes (<c>dt screenshot --gaunt</c>), on crewmate 4 (alone off the train's left,
    /// <see cref="Lone"/>; the Ribbits put away): <c>sleep</c> heaped up in front of them (App. A.6 ASLEEP), <c>stir</c>
    /// waking as they near it, <c>listen</c> woken and stood over them, <c>angry</c> leant right in (three points of anger),
    /// <c>attack</c> striking at them; <c>in</c> squatted in car 2's aisle, listening (the <c>inside</c> view: note 118). The
    /// <c>gaunt</c> view looks over crewmate 4's shoulder up at it.
    /// </summary>
    public static List<Enemy> Gaunt(List<Enemy> threats, TrainOnLine train, string mode)
    {
        if (mode.Length == 0 || threats.OfType<Gaunt>().FirstOrDefault() is not { } gaunt)
            return threats;
        threats.RemoveAll(e => e is Ribbit);
        var side = train.Frames[Math.Min(2, train.Frames.Count - 1)];
        var before = side.ToWorld(new Double3(-(side.Shape.HalfWidth + GauntOut), 0, -1.5));
        switch (mode)
        {
            case "sleep" or "stir" or "listen" or "angry" or "attack":
                var (phase, extra, anger) = mode switch
                {
                    "sleep" => (SpinePhase.Dormant, -1, 0),
                    "stir" => (SpinePhase.Alert, -1, 0),
                    "listen" => (SpinePhase.Telegraph, LoneId, 0),
                    "angry" => (SpinePhase.Telegraph, LoneId, 3),
                    _ => (SpinePhase.Commit, LoneId, 4),
                };
                gaunt.Restore(phase, 2.2, gaunt.Health, Enemy.Loose, before, 0, 0, 0, extra, anger);
                break;
            case "in":
                int car = Math.Min(2, train.Frames.Count - 1);
                double floor = train.Dynamics.Tuning.Geometry.Interior?.FloorHeight ?? 0;
                gaunt.Restore(SpinePhase.Telegraph, 2.2, gaunt.Health, car, new Double3(-0.45, floor, -train.Frames[car].Shape.HalfLength + 4.2), 0, 0, 0, -1, 1);
                break;
            // Leaving with what it took (App. A.6): off from the car's side on the ground, or still aboard making for the
            // door (GauntLoad gives it the body it's carrying).
            case "leave":
                gaunt.Restore(SpinePhase.BreakOff, 2.2, gaunt.Health, Enemy.Loose, before, 0, 0, 0, -1, 0);
                break;
            case "leavein":
                int from = Math.Min(2, train.Frames.Count - 1);
                double deck = train.Dynamics.Tuning.Geometry.Interior?.FloorHeight ?? 0;
                gaunt.Restore(SpinePhase.BreakOff, 2.2, gaunt.Health, from, new Double3(0.2, deck, -train.Frames[from].Shape.HalfLength + 3.2), 0, 0, 0, -1, 0);
                break;
            default:
                throw new ArgumentException($"--gaunt {mode}: sleep, stir, listen, angry, attack, in, leave or leavein");
        }
        return threats;
    }

    /// <summary>
    /// A body for a staged Gaunt leaving (<c>--gaunt leave|leavein</c>) to carry off, held under it as Gaunt.Leave holds
    /// it (Bodies.TakeAlong: by the hips, high as it stalks, low as it creeps aboard) and settled there for 1.5 s.
    /// </summary>
    public static Sim.Physics.Bodies GauntLoad(TrainOnLine train, string content, Gaunt gaunt)
    {
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
        var g = DataFile.Load<EnemyTuning>(Path.Combine(content, EnemyTuning.File)).Gaunt;
        var player = DataFile.Load<Sim.Player.PlayerTuning>(Path.Combine(content, Sim.Player.PlayerTuning.File));
        var bodies = new Sim.Physics.Bodies();
        var dead = Sim.Player.PlayerMotor.SpawnOnRoof(train, 2, 0, player) with { Health = 0 };
        var body = bodies.SpawnRagdoll(train, 6, dead);
        gaunt.Extra = body.Id;
        bool aboard = gaunt.Attached >= 0;
        // Facing where it's going: aboard, toward the car's front door; off it, away from the car.
        double yaw;
        if (aboard)
            yaw = 0;
        else
        {
            var frame = train.Frames[Math.Min(2, train.Frames.Count - 1)];
            var away = frame.Right * -1;
            yaw = Math.Atan2(-away.X, -away.Z);
        }
        for (int i = 0; i < 90; i++)
        {
            bodies.TakeAlong(body, train, gaunt.Id, aboard ? gaunt.Attached : Sim.Player.PlayerState.World,
                gaunt.Local + Double3.Up * (aboard ? g.CarryLow : g.CarryHigh), yaw);
            bodies.Step(train, tuning, _ => null);
        }
        return bodies;
    }

    /// <summary>
    /// The staged Grumbler (<c>dt screenshot --grumbler</c>), in front of crewmate 4 off the train's left (the Ribbits and
    /// the Gaunt put away): <c>gnaw</c> at a crate (App. A.8 TELEGRAPH), <c>rear</c> hit and feral, reared up at them,
    /// <c>bite</c> on them, <c>maul</c> on them beaten down (GRAB). The <c>grumbler</c> view looks over their shoulder down
    /// at it.
    /// </summary>
    public static List<Enemy> Grumbler(List<Enemy> threats, TrainOnLine train, string mode)
    {
        if (mode.Length == 0 || threats.OfType<Grumbler>().FirstOrDefault() is not { } g)
            return threats;
        threats.RemoveAll(e => e is Ribbit or Sim.Enemies.Gaunt);
        var side = train.Frames[Math.Min(2, train.Frames.Count - 1)];
        var before = side.ToWorld(new Double3(-(side.Shape.HalfWidth + GrumblerOut), 0, -1.5));
        var (phase, feral) = mode switch
        {
            "gnaw" => (SpinePhase.Telegraph, 0),
            "rear" => (SpinePhase.Telegraph, 1),
            "bite" => (SpinePhase.Commit, 1),
            "maul" => (SpinePhase.Grab, 1),
            _ => throw new ArgumentException($"--grumbler {mode}: gnaw, rear, bite or maul"),
        };
        g.Restore(phase, 0.6, g.Health, Enemy.Loose, before, 0, 0, 0, -1, feral);
        return threats;
    }

    /// <summary>
    /// The staged Stoker (<c>dt screenshot --stoker</c>, which opens the firebox door): <c>peer</c> watching out of the door
    /// (App. A.5, the soot falling), <c>reach</c> feeding, an arm out after whoever opened it. The <c>firebox</c> view looks
    /// at the door.
    /// </summary>
    public static List<Enemy> Stoker(List<Enemy> threats, string mode)
    {
        if (mode.Length == 0 || threats.OfType<Sim.Enemies.Stoker>().FirstOrDefault() is not { } s)
            return threats;
        var phase = mode switch
        {
            "peer" => SpinePhase.Telegraph,
            "reach" => SpinePhase.Commit,
            _ => throw new ArgumentException($"--stoker {mode}: peer or reach"),
        };
        s.Restore(phase, 1.2, s.Health, s.Attached, s.Local, 0, 0, 0, s.Extra, 0);
        return threats;
    }

    /// <summary>
    /// The staged Follower (<c>dt screenshot --follower</c>): <c>back</c> on crewmate 4's back (App. A.6 RIDE; <see cref="Lone"/>,
    /// whose shoulder the <c>pack</c> view looks over), <c>crawl</c> off them on the ground making for the train, <c>nest</c>
    /// built in car 2's aisle (the <c>inside</c> view). The Ribbits are put away.
    /// </summary>
    public static List<Enemy> Follower(List<Enemy> threats, TrainOnLine train, string mode)
    {
        if (mode.Length == 0 || threats.OfType<Sim.Enemies.Follower>().FirstOrDefault() is not { } f)
            return threats;
        threats.RemoveAll(e => e is Ribbit);
        int car = Math.Min(2, train.Frames.Count - 1);
        var side = train.Frames[car];
        switch (mode)
        {
            case "back":
                f.Restore(SpinePhase.Telegraph, 2, f.Health, Enemy.Loose, side.ToWorld(new Double3(-(side.Shape.HalfWidth + 2.2), 1.35, -1.5)), 0, 0, 0, LoneId, 0);
                break;
            case "crawl":
                f.Restore(SpinePhase.Commit, 2, f.Health, Enemy.Loose, side.ToWorld(new Double3(-(side.Shape.HalfWidth + 1.6), 0, -0.9)), 0, 0, 0, -1, 0);
                break;
            case "nest":
                double floor = train.Dynamics.Tuning.Geometry.Interior?.FloorHeight ?? 0;
                f.Restore(SpinePhase.Punish, 30, f.Health, car, new Double3(-0.45, floor, -side.Shape.HalfLength + 3.0), 0, 0, 0, -1, 1);
                break;
            default:
                throw new ArgumentException($"--follower {mode}: back, crawl or nest");
        }
        return threats;
    }

    /// <summary>
    /// The staged Climber (<c>dt screenshot --climber</c>), at car 2: <c>run</c> pacing the train beside it (App. A.4),
    /// <c>scrabble</c> up in the gap behind it (MOUNT; the <c>gapside</c> view), <c>walk</c> on its roof for the engine,
    /// <c>crouch</c> waiting in its aisle (the <c>inside</c> view).
    /// </summary>
    public static List<Enemy> Climber(List<Enemy> threats, TrainOnLine train, string mode)
    {
        var c = threats.OfType<Sim.Enemies.Climber>().FirstOrDefault();
        if (mode.Length == 0 || c is null)
            return threats;
        threats.RemoveAll(e => e is Sim.Enemies.Climber && e != c);
        int car = Math.Min(2, train.Frames.Count - 2);
        var at = train.Frames[car];
        var shape = at.Shape;
        double floor = train.Dynamics.Tuning.Geometry.Interior?.FloorHeight ?? 0, gap = train.Dynamics.Tuning.Geometry.CouplingGap;
        switch (mode)
        {
            case "run":
                c.Restore(SpinePhase.Dormant, 1, c.Health, Enemy.Loose, at.ToWorld(new Double3(shape.HalfWidth + 1.3, 0, shape.HalfLength - 1.5)), 0, 0, 0, car, 1);
                break;
            case "scrabble":
                c.Restore(SpinePhase.Telegraph, 1, c.Health, car, new Double3(shape.HalfWidth - 0.2, 1.0, shape.HalfLength + gap * 0.5), 0, 0, 0, car, 1);
                break;
            case "walk":
                c.Restore(SpinePhase.Commit, 1, c.Health, car, new Double3(0, shape.RoofHeight, -2), 0, 0, 0, car, 1);
                break;
            case "crouch":
                c.Restore(SpinePhase.Commit, 1, c.Health, car, new Double3(-0.45, floor, -shape.HalfLength + 3.0), 0, 0, 0, -1, 1);
                break;
            default:
                throw new ArgumentException($"--climber {mode}: run, scrabble, walk or crouch");
        }
        return threats;
    }

    /// <summary>
    /// The staged Passenger (<c>dt screenshot --passenger</c>), in car 2's aisle facing the <c>inside</c> view (App. A.8):
    /// <c>stand</c> hanging about passing for crew (BLEND), <c>walk</c> going down the car, <c>drag</c> hauling someone
    /// (GRAB; <see cref="Dragged"/> is them), <c>pin</c> stopped at the coupling heaving at the pin (UNCOUPLE). Its paces
    /// (<see cref="PassengerPace"/>) are what a still frame can't measure.
    /// </summary>
    public static List<Enemy> Passenger(List<Enemy> threats, TrainOnLine train, string mode)
    {
        var p = threats.OfType<Sim.Enemies.Passenger>().FirstOrDefault();
        if (mode.Length == 0 || p is null)
            return threats;
        int car = Math.Min(2, train.Frames.Count - 2);
        double floor = train.Dynamics.Tuning.Geometry.Interior?.FloorHeight ?? 0;
        var at = PassengerAt(train);
        var phase = mode switch
        {
            "stand" or "walk" => SpinePhase.Telegraph,
            "drag" or "pin" => SpinePhase.Grab,
            _ => throw new ArgumentException($"--passenger {mode}: stand, walk, drag or pin"),
        };
        p.Restore(phase, 2.4, p.Health, car, at with { Y = floor }, 0, 0, 0, p.Extra, 0);
        return threats;
    }

    /// <summary>How fast the staged Passenger is going (m/s) for <paramref name="mode"/>: walking and dragging at the sim's paces.</summary>
    public static float PassengerPace(string mode) => mode switch
    {
        "walk" => (float)new PassengerTuning().WalkSpeed,
        "drag" => (float)new PassengerTuning().DragSpeed,
        _ => 0,
    };

    /// <summary>The one the staged Whistler's carrying off (<c>--whistler carry</c>) or has at its nest (<c>nest</c>): where the
    /// sim has them, at its middle (the scene hangs them in its forelegs, or lays them in the nest: GreyboxScene.Hung).</summary>
    public static Crewmate Carried(Whistler w) => new(LoneId, w.Local, 0, true, Act: Art.CrewPose.HeldCarried);

    /// <summary>The one the staged Passenger is dragging (<c>--passenger drag</c>): down on the floor at its feet, where the sim has them.</summary>
    public static Crewmate Dragged(TrainOnLine train)
    {
        int car = Math.Min(2, train.Frames.Count - 2);
        double floor = train.Dynamics.Tuning.Geometry.Interior?.FloorHeight ?? 0;
        return new Crewmate(LoneId, train.Frames[car].ToWorld(PassengerAt(train) with { Y = floor }), Math.PI * 0.5, false);
    }

    static Double3 PassengerAt(TrainOnLine train) =>
        new(-0.35, 0, -train.Frames[Math.Min(2, train.Frames.Count - 2)].Shape.HalfLength + 5.6);

    /// <summary>
    /// The staged Soot Child (<c>dt screenshot --soot</c>), off car 2's left in front of crewmate 4 (<see cref="Lone"/>; the
    /// Ribbits put away): <c>huddle</c> squatted in the ash, <c>call</c> calling to them (App. A.6 CALL; black eyes, black
    /// hands), <c>real</c> the same, a real child (ROLL), <c>drink</c> on them (GRAB). The <c>soot</c> view looks over their
    /// shoulder at it.
    /// </summary>
    public static List<Enemy> Soot(List<Enemy> threats, TrainOnLine train, string mode)
    {
        if (mode.Length == 0 || threats.OfType<SootChildren>().FirstOrDefault() is not { } c)
            return threats;
        threats.RemoveAll(e => e is Ribbit);
        var side = train.Frames[Math.Min(2, train.Frames.Count - 1)];
        var before = side.ToWorld(new Double3(-(side.Shape.HalfWidth + SootOut), 0, -1.5));
        switch (mode)
        {
            case "huddle" or "call" or "real":
                c.Restore(SpinePhase.Telegraph, 1.1, c.Health, Enemy.Loose, before, 0, 0, 0, mode == "huddle" ? 0 : 1, mode == "real" ? 0 : 1);
                break;
            case "drink":
                c.Restore(SpinePhase.Grab, 2.5, c.Health, Enemy.Loose, Lone(train).Feet, 0, 0, 0, 0, 1);
                break;
            default:
                throw new ArgumentException($"--soot {mode}: huddle, call, real or drink");
        }
        return threats;
    }

    // How far off the second car's side the staged Soot Child is (m): in front of crewmate 4, a few metres out.
    const double SootOut = 4.6;

    /// <summary>
    /// The staged Switchman (<c>dt screenshot --switchman</c>), at its lever 55 m up the line (App. A.7): <c>wait</c> by it
    /// (TELEGRAPH), <c>grip</c> its hand on it, waiting to throw it under the train (COMMIT, the derailer's tell),
    /// <c>throw</c> it thrown (PUNISH). The <c>switchman</c> view is on the line short of it.
    /// </summary>
    public static List<Enemy> Switchman(List<Enemy> threats, string mode)
    {
        if (mode.Length == 0 || threats.OfType<Sim.Enemies.Switchman>().FirstOrDefault() is not { } s)
            return threats;
        var (phase, derail) = mode switch
        {
            "wait" => (SpinePhase.Telegraph, 0.0),
            "grip" => (SpinePhase.Commit, 1.0),
            "throw" => (SpinePhase.Punish, 1.0),
            _ => throw new ArgumentException($"--switchman {mode}: wait, grip or throw"),
        };
        s.Restore(phase, 0.4, s.Health, s.Attached, s.Local, s.LineDistance, s.Lateral, s.Height, s.Extra, derail);
        return threats;
    }

    // How far off the second car's side the staged Grumbler is (m): in front of crewmate 4, a lunge from them.
    const double GrumblerOut = 3.3;

    // How far off the second car's side the staged Gaunt stands (m): in front of crewmate 4, at its arm's length from them.
    const double GauntOut = 3.4;

    /// <summary>
    /// A roster for the screenshot (T69): you in the cab and three crewmates, named; crew 1 is speaking. A Passenger is aboard
    /// wearing crewmate 2's face, and (GDD v1.4: roll call is verbal) the roster doesn't give it away.
    /// </summary>
    public static (IReadOnlyList<RosterLine> Lines, Func<byte, double?> Heard) Roster(TrainOnLine train, string content)
    {
        var player = DataFile.Load<Sim.Player.PlayerTuning>(Path.Combine(content, Sim.Player.PlayerTuning.File));
        var world = new Sim.World(train, null);
        var p = new Passenger(48);
        p.Restore(SpinePhase.Telegraph, 20, 1, Math.Min(3, train.Frames.Count - 1), default, 0, 0, 0, 2, 0);
        world.MirrorEnemies([p]);
        foreach (var (id, name) in new[] { (1, "Dunmore"), (2, "Okafor"), (3, "Reyes"), (4, "Dave") })
            world.Names[id] = name;
        var cab = Sim.Player.PlayerMotor.SpawnInCab(train, player);
        var roof = Sim.Player.PlayerMotor.SpawnOnRoof(train, Math.Min(2, train.Frames.Count - 1), 0, player);
        var lines = NetPlaySession.RosterOf(4, cab, [(1, roof), (2, roof with { Parent = 1 }), (3, cab)], world);
        return (lines, id => id switch { 1 => 0.5, 2 => 14, _ => null });
    }

    /// <summary>
    /// The staged night's crew for <see cref="Report"/>: Dave and Dunmore in the cab, Okafor on car 3's roof, Priya on the line
    /// 180 m behind.
    /// </summary>
    public static List<(int Id, Sim.Player.PlayerState State)> ReportCrew(TrainOnLine train)
    {
        var cab = Sim.Player.PlayerMotor.SpawnInCab(train, DefaultPlayer);
        var beside = Sim.Player.PlayerMotor.SpawnInCab(train, DefaultPlayer, 0.6) with { Yaw = 0.9 };
        var roof = Sim.Player.PlayerMotor.SpawnOnRoof(train, Math.Min(3, train.Frames.Count - 1), 0, DefaultPlayer) with { Yaw = Math.PI, Pitch = -0.25 };
        var line = Sim.Player.PlayerMotor.SpawnOnGround(train.Line.Sample(train.Dynamics.RearDistance - 180).Position, train.Line, train.Dynamics.RearDistance - 180, DefaultPlayer);
        return [(0, cab with { Yaw = -0.5, Pitch = -0.1 }), (1, beside), (2, roof), (3, line)];
    }

    /// <summary>
    /// A night's incident report (GDD v1.4 App. D.12) staged through the real log, formatter and bookmarks: a grab and its
    /// punish beside the death they ended in, a grab somebody got away from, the derailment's crew stills (derailed), and two
    /// of the dead's own bookmarks. The clock is moved between them (<see cref="Sim.Run.Run.Resume"/>) so each has its time.
    /// </summary>
    public static Sim.Run.RunReport Report(Sim.World world, Sim.Run.RunEnd end)
    {
        foreach (var (id, name) in new[] { (0, "Dave"), (1, "Dunmore"), (2, "Okafor"), (3, "Priya") })
            world.Names[id] = name;
        var train = world.Train;
        var log = world.Attribution;
        var marks = world.Bookmarks;
        var run = world.Run!;
        void At(double seconds) => run.Resume(seconds, -1, train.Boiler.Tender, 0);
        log.Drove(0);
        log.Fired(1, 100);
        var crew = ReportCrew(train);
        var (cab, beside, roof, line) = (crew[0].State, crew[1].State, crew[2].State, crew[3].State);
        At(312);
        log.Add(Sim.Run.IncidentLog.Grab(world, 1, beside, "Grabbed by the Dragger"));
        marks.Grab(world, 1, "Grabbed by the Dragger", crew);
        At(604);
        log.Add(Sim.Run.IncidentLog.Grab(world, 2, roof, "Grabbed by the Car Hugger"));
        marks.Grab(world, 2, "Grabbed by the Car Hugger", crew);
        At(610);
        marks.Punish(world, 46, "CarHugger", 2, Sim.Player.PlayerMotor.WorldPosition(roof, train), crew);
        log.Add(Sim.Run.IncidentLog.Death(world, 2, roof with { Death = Sim.Player.DeathCause.Eaten }, null, crew));
        log.Add(new Sim.Run.Incident(Sim.Run.IncidentKind.Rescue, 900, 2, "Freed from the Holdout", "at Hollin Halt", 0, "Broken out by {actor}."));
        At(1012);
        log.Add(Sim.Run.IncidentLog.Death(world, 3, line with { Death = Sim.Player.DeathCause.Cold }, null, crew));
        At(1104);
        marks.Manual(world, 3, 0, cab);
        At(1290);
        marks.Manual(world, 3, 1, beside);
        log.Add(new Sim.Run.Incident(Sim.Run.IncidentKind.Rupture, 1300, -1, "Boiler ruptured", "at km 14", 1, "Last fired: {actor}. At 100 for 20 s."));
        At(1500);
        log.Add(Sim.Run.IncidentLog.Death(world, 1, beside with { Death = Sim.Player.DeathCause.Seized }, null, crew));
        var lines = Sim.Run.IncidentLog.Lines(world, 350, 263, body => false);
        lines.Add(new Sim.Run.ReportLine(Sim.Run.IncidentKind.CarLost, "", "Car 5 finished by the Car Hugger at km 9. Inside: freight, 0.6 car-loads, the body of Okafor."));
        At(1720);
        if (end == Sim.Run.RunEnd.Derailed)
        {
            lines.Add(new Sim.Run.ReportLine(Sim.Run.IncidentKind.Derailed, "", "Consist derailed, 68 km/h at km 17. Took the 50 km/h bend at 68 km/h, 18 km/h too fast. Throttle: Dave.") { Seconds = 1720 });
            // Only Dave's still aboard by then: the derailment's stills are of whoever it took.
            marks.End(world, end, [crew[0], crew[1] with { State = beside with { Death = Sim.Player.DeathCause.Seized } }]);
        }
        var (marked, shown) = Sim.Run.Run.MarkBookmarks(world, lines);
        return new Sim.Run.RunReport(end, 1720, 17.2, 4, 1, 2.1, 0, 120, 6, 40, -1416, 1, 3, Deaths: 3, CrewLossFees: 1050) { Lines = marked, Bookmarks = shown };
    }

    static readonly Sim.Player.PlayerTuning DefaultPlayer = DataFile.Load<Sim.Player.PlayerTuning>(Path.Combine(DataFile.FindContentRoot(), Sim.Player.PlayerTuning.File));

    /// <summary>One of each enemy (GDD v1.1 §21) mid-telegraph or mid-commit around the train, where a view can see it.</summary>
    /// <param name="dollAhead">How far up the line the Track Doll stands (App. A.2's reveal is 200 m in the lamp).</param>
    /// <param name="lurkAhead">If given, a second Car Hugger lurking beside the line this far ahead (App. A.3 LURK).</param>
    /// <summary>The staged car fire smouldering instead (its TELEGRAPH, App. C.5: smoke before flame): dt screenshot --threats --smoulder.</summary>
    public static List<Enemy> Smoulder(List<Enemy> threats)
    {
        foreach (var e in threats)
            if (e is CarFire fire)
                fire.Restore(SpinePhase.Telegraph, 8, 1, fire.Attached, fire.Local, 0, 0, 0, 0.15, 0);
        return threats;
    }

    /// <summary>
    /// The staged car fire on its way to jumping the coupling (App. A.5; <c>dt screenshot --threats --spread f</c>): alight,
    /// its blaze <paramref name="fraction"/> of the way to enemies.json carFire.spreadSeconds.
    /// </summary>
    public static List<Enemy> Spread(List<Enemy> threats, double fraction, double spreadSeconds)
    {
        foreach (var e in threats)
            if (e is CarFire fire)
                fire.Restore(fire.Phase, fire.PhaseSeconds, fire.Health, fire.Attached, fire.Local, 0, 0, 0, fire.Extra, fraction * spreadSeconds);
        return threats;
    }

    public static List<Enemy> Threats(TrainOnLine train, double dollAhead = 22, double? lurkAhead = null)
    {
        var d = train.Dynamics;
        int rear = d.Consist.Vehicles[^1].Id;
        var rearShape = train.Frames[rear].Shape;
        var threats = new List<Enemy>();
        var sleepers = new Sleepers(1);
        sleepers.Restore(SpinePhase.Telegraph, 1.2, 1, -1, default, d.Distance + 40, 0, 0.2, 0, 0);
        threats.Add(sleepers);
        for (int i = 0; i < 3; i++)
        {
            var hound = new CinderHound(10 + i, 10);
            hound.Restore(SpinePhase.Commit, 2, 60, -1, default, d.RearDistance - 14 - i * 6, (i % 2 == 0 ? 1 : -1) * (2.5 + i), 0.6, 10, 0);
            threats.Add(hound);
        }
        var boarded = new CinderHound(13, 10);
        boarded.Restore(SpinePhase.Punish, 0.4, 60, rear, new Double3(0.6, rearShape.RoofHeight, rearShape.HalfLength - 2.5), 0, 0, 0, 10, 0);
        threats.Add(boarded);
        // Latched on the guard van's rear end, grinding (GDD v1.1 A.3): its head on the rear platform, its mouth on the door.
        var hugger = new CarHugger(46);
        hugger.Restore(SpinePhase.Commit, 4, 12, rear, new Double3(0, 1.0, rearShape.HalfLength + 0.4), 0, 0, 0, 0, 0);
        threats.Add(hugger);
        int cargo = d.Consist.Vehicles.First(v => v.Kind == VehicleKind.Cargo).Id;
        var cargoShape = train.Frames[cargo].Shape;
        // Under the cargo car's edge, a limb up over the lip for someone walking too near it.
        var dragger = new Dragger(21);
        dragger.Restore(SpinePhase.Telegraph, 0.6, 1, cargo, new Double3(-(cargoShape.HalfWidth + 0.1), cargoShape.RoofHeight - 0.35, 3), 0, 0, 0, 1, 0);
        threats.Add(dragger);
        // Hidden in the coupling behind the middle car (A.4): it waits there for the next stop's whistle.
        int middle = Math.Max(0, (train.Frames.Count - 1) / 2);
        var whistler = new Whistler(22);
        whistler.Restore(SpinePhase.Alert, 1, 6, middle, CrewSense.GapLocal(train, middle), 0, 0, 0, 0, 0);
        threats.Add(whistler);
        // The first cargo car alight, and Fire Flies swarming the lamp in the middle car (A.5, C.5).
        var fire = CarFire.In(24, train, cargo, 1.5, new CarFireTuning());
        fire.Restore(SpinePhase.Punish, 5, 1, cargo, fire.Local, 0, 0, 0, 0.7, 0);
        threats.Add(fire);
        if (train.Frames[middle].Shape.Interior is { } room)
        {
            var flies = new FireFlies(25);
            flies.Restore(SpinePhase.Telegraph, 4, 1, middle, new Double3(0, room.Max.Y - 0.4, room.Centre.Z), 0, 0, 0, 0, 0);
            threats.Add(flies);
        }
        // On the rails further up, white face in the lamp (A.2).
        var doll = new TrackDoll(23);
        doll.Restore(SpinePhase.Telegraph, 3, 1, -1, default, d.Distance + dollAhead, 0, 0, 0, 0);
        threats.Add(doll);
        // Haunting the cab at the controls (A.2 TAMPER), where it's heard giggling.
        var haunting = new TrackDoll(26);
        haunting.Restore(SpinePhase.Punish, 3, 1, 0, train.Frames[0].Shape.Cab!.Value.Centre + new Double3(0.4, -1.35, 0.3), 0, 0, 0, 0, 1);
        threats.Add(haunting);
        // On the ground by the first cargo car, gnawing a crate it's come off (A.8).
        var grumbler = new Grumbler(27);
        grumbler.Restore(SpinePhase.Telegraph, 2, 8, Enemy.Loose, train.Frames[cargo].ToWorld(new Double3(cargoShape.HalfWidth + 3, 0, -2)), 0, 0, 0, -1, 0);
        threats.Add(grumbler);
        // In the firebox, fed (A.5).
        var stoker = Sim.Enemies.Stoker.InFirebox(32, train, false, new StokerTuning());
        stoker.Restore(SpinePhase.Commit, 6, 1, 0, stoker.Local, 0, 0, 0, 0, 0);
        threats.Add(stoker);
        // Tiptoeing up on crewmate 1 on the second car's roof (A.5). (AudioTests hears its tiptoeing from here: Tippy moves
        // it for the camera.)
        int beside = Math.Min(2, train.Frames.Count - 1);
        double roof = train.Frames[beside].Shape.RoofHeight;
        var tippy = new TippyToesie(30);
        tippy.Restore(SpinePhase.Telegraph, 5, 3, beside, new Double3(-0.9, roof, -2.8), 0, 0, 0, 1, Math.PI);
        threats.Add(tippy);
        // Scrabbling up the gap behind the second car on its right, and one already walking the third car's roof (A.4).
        int gapCar = Math.Min(2, train.Frames.Count - 2);
        var gapShape = train.Frames[gapCar].Shape;
        var climbing = new Climber(44);
        climbing.Restore(SpinePhase.Telegraph, 1.2, 1, gapCar, new Double3(gapShape.HalfWidth - 0.2, 1.0, gapShape.HalfLength + train.Dynamics.Tuning.Geometry.CouplingGap * 0.5), 0, 0, 0, gapCar, 1);
        threats.Add(climbing);
        int roofCar = Math.Min(3, train.Frames.Count - 1);
        var walking = new Climber(45);
        walking.Restore(SpinePhase.Commit, 3, 1, roofCar, new Double3(0, train.Frames[roofCar].Shape.RoofHeight, 2), 0, 0, 0, roofCar, -1);
        threats.Add(walking);
        // On the ground off the train's left: a Ribbit pack lined up, throats swelling (A.6), and a woken Gaunt leaning in.
        var side = train.Frames[beside];
        for (int i = 0; i < 3; i++)
        {
            var ribbit = new Ribbit(60 + i, 60);
            ribbit.Restore(SpinePhase.Telegraph, 1, 3, Enemy.Loose, side.ToWorld(new Double3(-(side.Shape.HalfWidth + 5 + i * 0.3), 0, -3 + i * 1.6)), 0, 0, 0, -1, 0);
            threats.Add(ribbit);
        }
        var gaunt = new Gaunt(47);
        gaunt.Restore(SpinePhase.Telegraph, 5, 8, Enemy.Loose, side.ToWorld(new Double3(side.Shape.HalfWidth + 2.5, 0, 4)), 0, 0, 0, 1, 0.8);
        threats.Add(gaunt);
        // Two of the Choir's ghosts over the guard van's roof (A.7), besieging.
        for (int i = 0; i < 2; i++)
        {
            var ghost = new ChoirGhost(70 + i);
            ghost.Restore(SpinePhase.Commit, 2, 6, Enemy.Loose, train.Frames[rear].ToWorld(new Double3(i == 0 ? -1.2 : 1.3, rearShape.RoofHeight + 1.6 + i * 0.4, -1 + i * 2)), 0, 0, 0, -1, 0);
            threats.Add(ghost);
        }
        // At the side of the line ahead, where a switch stand would be, lantern out.
        var switchman = new Switchman(40);
        switchman.Restore(SpinePhase.Telegraph, 3, 1, -1, default, d.Distance + 55, 3.8, 0, -1, 0);
        threats.Add(switchman);
        // Out in the dark off the second car's side, calling for help: black eyes, from five metres (A.6).
        var soot = new SootChildren(41);
        soot.Restore(SpinePhase.Telegraph, 4, 1, Enemy.Loose, side.ToWorld(new Double3(-(side.Shape.HalfWidth + 6), 0, 6)), 0, 0, 0, 1, 1);
        threats.Add(soot);
        // Among the three on the second car's roof, a fourth (T61): crewmate 2 again, the same cap, the same coat. It lives in
        // the cars' rooms, which no view looks into; staged up here so the art review sees it stand beside the one it copies.
        var passenger = new Passenger(48);
        passenger.Restore(SpinePhase.Telegraph, 20, 1, beside, new Double3(0.55, train.Frames[beside].Shape.RoofHeight, -7.0), 0, 0, 0, 2, Math.PI - 0.3);
        threats.Add(passenger);
        // Nesting in the cargo car's loot (A.6): halfway built.
        if (cargoShape.Interior is { } hold)
        {
            var follower = new Follower(49);
            follower.Restore(SpinePhase.Punish, 30, 2, cargo, new Double3(0.4, hold.Min.Y + 0.05, hold.Max.Z - 1), 0, 0, 0, -1, 0.5);
            threats.Add(follower);
        }
        // Over the first car, spread wide and surging at someone on its roof (T63): the dark coming up over the roof's edge.
        int driftCar = Math.Min(1, train.Frames.Count - 1);
        var drift = new Drift(50);
        drift.Restore(SpinePhase.Telegraph, 2, 1, driftCar, new Double3(0.8, train.Frames[driftCar].Shape.RoofHeight, 2), 0, 0, 0, 6, 1);
        threats.Add(drift);
        if (lurkAhead is { } lurk)
        {
            // Where CarHugger.Lurking puts one: down on the low ground by the line, waiting for the rear car.
            var lurker = new CarHugger(51);
            lurker.Restore(SpinePhase.Dormant, 3, 16, -1, default, d.Distance + lurk, 2.6, -0.3, 0, 0);
            threats.Add(lurker);
        }
        return threats;
    }
}
