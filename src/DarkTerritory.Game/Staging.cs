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

    /// <summary>One of each demo enemy mid-telegraph or mid-punish around the train.</summary>
    public static List<Enemy> Threats(TrainOnLine train)
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
        int cargo = d.Consist.Vehicles.First(v => v.Kind == VehicleKind.Cargo).Id;
        var cargoShape = train.Frames[cargo].Shape;
        var clinger = new Clinger(20);
        clinger.Restore(SpinePhase.Telegraph, 50, 1, cargo, new Double3(cargoShape.HalfWidth + 0.15, 2.0, 0), 0, 0, 0, 0.55, 0);
        threats.Add(clinger);
        // Under the same car's other edge, reaching over the lip for someone walking too near it.
        var dragger = new Dragger(21);
        dragger.Restore(SpinePhase.Telegraph, 0.6, 1, cargo, new Double3(-(cargoShape.HalfWidth + 0.1), cargoShape.RoofHeight - 0.35, 3), 0, 0, 0, 1, 0);
        threats.Add(dragger);
        // In the coupling behind the middle car, rattling at someone come up to cross.
        var rattle = new Rattle(22);
        int middle = Math.Max(0, (train.Frames.Count - 1) / 2);
        rattle.Restore(SpinePhase.Telegraph, 1, 1, middle, Rattle.In(0, train, middle, train.Dynamics.Tuning.Geometry.CouplingGap).Local, 0, 0, 0, 0, 0);
        threats.Add(rattle);
        // Trouble inside the cars: the first cargo car alight, and in the middle car a load gone loose and Gnawers out.
        var fire = CarFire.In(24, train, cargo, 1.5, new CarFireTuning());
        fire.Restore(SpinePhase.Punish, 5, 1, cargo, fire.Local, 0, 0, 0, 0.7, 0);
        threats.Add(fire);
        if (train.Vehicles[middle].Kind == VehicleKind.Cargo)
        {
            var load = LooseLoad.In(25, train, middle, -2);
            load.Restore(SpinePhase.Telegraph, 4, 1, middle, load.Local, 0, 0, 0, 0, d.Speed);
            threats.Add(load);
            var gnawers = Gnawers.In(26, train, middle, 2.5, new GnawerTuning());
            gnawers.Restore(SpinePhase.Punish, 3, 0.8, middle, gnawers.Local, 0, 0, 0, 0, 0);
            threats.Add(gnawers);
        }
        // Coming in for the lamp off the engine's right, its eyes catching the beam's spill.
        var lamplighter = new Lamplighter(23);
        lamplighter.Restore(SpinePhase.Telegraph, 3, 1, -1, default, d.Distance + 4, 3.2, 0, 0, 0);
        threats.Add(lamplighter);
        // Coming for the empty cab, and in the firebox (T53).
        var deadman = new Deadman(31);
        deadman.Restore(SpinePhase.Telegraph, 4, 1, 0, train.Frames[0].Shape.Cab!.Value.Centre, 0, 0, 0, 0, 0);
        threats.Add(deadman);
        var stoker = Stoker.InFirebox(32, train);
        stoker.Restore(SpinePhase.Telegraph, 6, 1, 0, stoker.Local, 0, 0, 0, 0, 0);
        threats.Add(stoker);
        var hollow = new Hollow(30);
        hollow.Restore(SpinePhase.Punish, 2, 1, 0, train.Frames[0].Shape.Cab!.Value.Centre, 0, 0, 0, 0, 0);
        threats.Add(hollow);
        // Stood on the fourth car's roof, still, facing back along the train (T60).
        int gauntCar = Math.Min(4, train.Frames.Count - 1);
        var gaunt = new Gaunt(47);
        gaunt.Restore(SpinePhase.Telegraph, 5, 1, gauntCar, new Double3(0.3, train.Frames[gauntCar].Shape.RoofHeight, -1), 0, 0, 0, 0, Math.PI);
        threats.Add(gaunt);
        // Hung under the rear coupling, dragging (T59): below the guard van's gun, beside the boarded hound's car.
        var weight = new Weight(46);
        weight.Restore(SpinePhase.Telegraph, 2, 1, rear, new Double3(0, 0.35, rearShape.HalfLength + 0.4), 0, 0, 0, 0, 0);
        threats.Add(weight);
        // Scrabbling up the gap behind the second car on its right, and one already walking the third car's roof (T58).
        int gapCar = Math.Min(2, train.Frames.Count - 2);
        var gapShape = train.Frames[gapCar].Shape;
        var climbing = new Climber(44);
        climbing.Restore(SpinePhase.Telegraph, 1.2, 1, gapCar, new Double3(gapShape.HalfWidth - 0.2, 1.0, gapShape.HalfLength + train.Dynamics.Tuning.Geometry.CouplingGap * 0.5), 0, 0, 0, gapCar, 1);
        threats.Add(climbing);
        int roofCar = Math.Min(3, train.Frames.Count - 1);
        var walking = new Climber(45);
        walking.Restore(SpinePhase.Commit, 3, 1, roofCar, new Double3(0, train.Frames[roofCar].Shape.RoofHeight, 2), 0, 0, 0, roofCar, -1);
        threats.Add(walking);
        // Up the line out of sight, its first blast sounding (T57): never seen, only heard.
        var whistle = new LongWhistle(43);
        whistle.Restore(SpinePhase.Telegraph, 1, 1, -1, default, d.Distance + 450, 0, 3, 1, 14);
        threats.Add(whistle);
        // On the line further up, lantern raised and swinging, waving the train down (T56).
        var ferryman = new Ferryman(42);
        ferryman.Restore(SpinePhase.Telegraph, 2, 1, -1, default, d.Distance + 90, 0.3, 0, 14, 1);
        threats.Add(ferryman);
        // At the side of the line ahead, where a switch stand would be, lantern out.
        var switchman = new Switchman(40);
        switchman.Restore(SpinePhase.Telegraph, 3, 1, -1, default, d.Distance + 55, 3.8, 0, -1, 0);
        threats.Add(switchman);
        // Out in the dark off the second car's side, calling in someone's voice.
        int beside = Math.Min(2, train.Frames.Count - 1);
        var soot = new SootChildren(41);
        soot.Restore(SpinePhase.Telegraph, 4, 1, -1, default, train.Cars[beside].FrontDistance - train.Frames[beside].Shape.HalfLength, -(train.Frames[beside].Shape.HalfWidth + 6), 0, 1, beside);
        threats.Add(soot);
        // Among the three on the second car's roof, a fourth (T61): crewmate 2 again, the same cap, the same coat. It lives in
        // the cars' rooms, which no view looks into; staged up here so the art review sees it stand beside the one it copies.
        var passenger = new Passenger(48);
        passenger.Restore(SpinePhase.Telegraph, 20, 1, beside, new Double3(0.55, train.Frames[beside].Shape.RoofHeight, -7.0), 0, 0, 0, 2, Math.PI - 0.3);
        threats.Add(passenger);
        // At crewmate 1's back, bent and in their step (T62): stood free in the world, facing the way they face. In play
        // it's on the grounds at a stop; here it rides the roof behind them, so the roof shot has it.
        var follower = new Follower(49);
        double heading = train.Frames[beside].Heading + Math.PI + 0.3;
        var behind = train.Frames[beside].ToWorld(new Double3(-0.9, train.Frames[beside].Shape.RoofHeight, -5)) + new Double3(Math.Sin(heading), 0, Math.Cos(heading)) * 1.3;
        follower.Restore(SpinePhase.Telegraph, 8, 1, Enemy.Loose, behind, 0, 0, 0, 1, heading);
        threats.Add(follower);
        // Over the first car, spread wide and surging at someone on its roof (T63): the dark coming up over the roof's edge.
        int driftCar = Math.Min(1, train.Frames.Count - 1);
        var drift = new Drift(50);
        drift.Restore(SpinePhase.Telegraph, 2, 1, driftCar, new Double3(0.8, train.Frames[driftCar].Shape.RoofHeight, 2), 0, 0, 0, 6, 1);
        threats.Add(drift);
        return threats;
    }
}
