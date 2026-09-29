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
        var hollow = new Hollow(30);
        hollow.Restore(SpinePhase.Punish, 2, 1, 0, train.Frames[0].Shape.Cab!.Value.Centre, 0, 0, 0, 0, 0);
        threats.Add(hollow);
        // At the side of the line ahead, where a switch stand would be, lantern out.
        var switchman = new Switchman(40);
        switchman.Restore(SpinePhase.Telegraph, 3, 1, -1, default, d.Distance + 55, 3.8, 0, -1, 0);
        threats.Add(switchman);
        // Out in the dark off the second car's side, calling in someone's voice.
        int beside = Math.Min(2, train.Frames.Count - 1);
        var soot = new SootChildren(41);
        soot.Restore(SpinePhase.Telegraph, 4, 1, -1, default, train.Cars[beside].FrontDistance - train.Frames[beside].Shape.HalfLength, -(train.Frames[beside].Shape.HalfWidth + 6), 0, 1, beside);
        threats.Add(soot);
        return threats;
    }
}
