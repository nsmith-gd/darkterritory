using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>Set pieces for looking at and listening to things headless (screenshots, audio renders, tests).</summary>
public static class Staging
{
    /// <summary>Crates and a lamp on car 2's roof and a body on car 3's, dropped and left to settle.</summary>
    public static Sim.Physics.Bodies Bodies(TrainOnLine train, string content)
    {
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
        var player = DataFile.Load<Sim.Player.PlayerTuning>(Path.Combine(content, Sim.Player.PlayerTuning.File));
        var bodies = new Sim.Physics.Bodies();
        double roof = tuning.Geometry.CarHeight;
        bodies.SpawnCrate(train, 2, new Double3(0.5, roof + 0.3, 1.0)).Yaw = 0.4;
        bodies.SpawnCrate(train, 2, new Double3(-0.4, roof + 0.6, 2.2)).Yaw = -0.3;
        bodies.SpawnCrate(train, 2, new Double3(0.1, roof + 0.2, -1.5), Sim.Physics.BodyKind.Lamp);
        var dead = Sim.Player.PlayerMotor.SpawnOnRoof(train, 3, -2, player) with { Health = 0, Yaw = 1.2 };
        bodies.SpawnRagdoll(train, 9, dead);
        for (int i = 0; i < 90; i++)
            bodies.Step(train, tuning, _ => null);
        return bodies;
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
    /// The staged Whistler as it goes (<c>dt screenshot --whistler</c>): <c>fold</c> hidden in its gap (App. A.4 HIDE),
    /// <c>whistle</c> pulling the cord, <c>watch</c> watching the gap's mouth after (WAIT). The <c>gapside</c> view looks in.
    /// </summary>
    public static List<Enemy> Whistler(List<Enemy> threats, string mode)
    {
        if (mode.Length == 0 || threats.OfType<Whistler>().FirstOrDefault() is not { } w)
            return threats;
        var (phase, extra) = mode switch
        {
            "fold" => (SpinePhase.Dormant, 0.0),
            "whistle" => (SpinePhase.Telegraph, 1.0),
            "watch" => (SpinePhase.Commit, 0.0),
            _ => throw new ArgumentException($"--whistler {mode}: fold, whistle or watch"),
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
    /// A roster for the screenshot (T69): you in the cab, three crewmates, and a Passenger wearing crewmate 2's face. Crew 1
    /// is speaking, crew 2 was heard a while ago, crew 3 hasn't said anything yet; the Passenger never has.
    /// </summary>
    public static (IReadOnlyList<RosterLine> Lines, Func<byte, double?> Heard) Roster(TrainOnLine train, string content)
    {
        var player = DataFile.Load<Sim.Player.PlayerTuning>(Path.Combine(content, Sim.Player.PlayerTuning.File));
        var world = new Sim.World(train, null);
        var p = new Passenger(48);
        p.Restore(SpinePhase.Telegraph, 20, 1, Math.Min(3, train.Frames.Count - 1), default, 0, 0, 0, 2, 0);
        world.MirrorEnemies([p]);
        var cab = Sim.Player.PlayerMotor.SpawnInCab(train, player);
        var roof = Sim.Player.PlayerMotor.SpawnOnRoof(train, Math.Min(2, train.Frames.Count - 1), 0, player);
        var lines = NetPlaySession.RosterOf(4, cab, [(1, roof), (2, roof with { Parent = 1 }), (3, cab)], world);
        return (lines, id => id switch { 1 => 0.5, 2 => 14, _ => null });
    }

    /// <summary>One of each enemy (GDD v1.1 §21) mid-telegraph or mid-commit around the train, where a view can see it.</summary>
    /// <param name="dollAhead">How far up the line the Track Doll stands (App. A.2's reveal is 200 m in the lamp).</param>
    /// <param name="lurkAhead">If given, a second Car Hugger lurking beside the line this far ahead (App. A.3 LURK).</param>
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
        var stoker = Stoker.InFirebox(32, train, false, new StokerTuning());
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
