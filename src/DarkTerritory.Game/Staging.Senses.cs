using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>The sense table staged for a look (note 728): a music box drawing the Moose, the extinguisher on a Cinder Hound.</summary>
public static partial class Staging
{
    public const int SprayedHoundId = 7280, SprayerId = 12;

    /// <summary>Where the staged music box lies (<c>dt screenshot musicbox</c>): out in the field 18 m up the line, 14 m off its left.</summary>
    public static Double3 MusicBoxAt(TrainOnLine train) => Lineside(train, 18, -14);

    /// <summary>
    /// The music box (note 728; <c>dt screenshot musicbox</c>): wound and thrown out into the field off the line's left, playing
    /// where it lies, and the Moose come over from its ground to it, stood over it listening (its head up, aggro at listenAt:
    /// what a toy puts it at, senses.json answers.moose), turned to it. The box is in <paramref name="bodies"/>.
    /// </summary>
    public static List<Enemy> MusicBox(List<Enemy> threats, TrainOnLine train, string content, out Sim.Physics.Bodies bodies)
    {
        threats.RemoveAll(e => e is Sim.Enemies.Moose);
        var t = DataFile.Load<EnemyTuning>(Path.Combine(content, EnemyTuning.File)).Moose;
        var standOff = Sim.Enemies.SenseTuning.Load(content).Answers.Moose.StandOff;
        var box = MusicBoxAt(train);
        bodies = new Sim.Physics.Bodies();
        var toy = bodies.SpawnItem(box, train.Dynamics.Distance + 18, Sim.Physics.BodyKind.Toy);
        toy.Noise = Sim.Physics.ToyNoise.MusicBox;
        toy.Playing = true;
        toy.Yaw = 0.6;
        toy.Pbd.Sleep();
        // Come in from further out and up the line, stood just short of it, facing it.
        var from = (Lineside(train, 26, -20) - box) with { Y = 0 };
        var at = OnGround(train, box + from.Normalized * standOff);
        var to = box - at;
        double yaw = Math.Atan2(-to.X, -to.Z);
        var moose = new Sim.Enemies.Moose(MooseId);
        moose.Restore(SpinePhase.Alert, 1.4, 1, Enemy.Loose, at, train.Dynamics.Distance + 20, yaw, (double)MooseMode.Graze, -1, t.ListenAt);
        threats.Add(moose);
        return threats;
    }

    /// <summary>
    /// The music box view's eye: crouched in the grass by the box, the Moose over it beyond, the line's lamp-lit
    /// train behind (a crewmate gone quiet watching it work).
    /// </summary>
    public static Camera MusicBoxCamera(TrainOnLine train)
    {
        var box = MusicBoxAt(train);
        var from = ((Lineside(train, 26, -20) - box) with { Y = 0 }).Normalized;
        var side = Double3.Cross(from, Double3.Up).Normalized;
        var eye = OnGround(train, box + side * 1.3 - from * 1.1) + Double3.Up * 0.7;
        return Camera.LookAt(eye, box + from * 1.5 + Double3.Up * 0.75, 64);
    }

    /// <summary>
    /// The extinguisher on a Cinder Hound (note 728; the director's playtest of 13 Oct evening, line 24; <c>dt screenshot
    /// extinguisherhound</c>): a hound on car 3's roof backing off down it from crewmate 12, who's braced into the extinguisher
    /// 2.6 m from it, spraying it (its jet drawn, GreyboxScene.SprayingHounds). The extinguisher's in <paramref name="bodies"/>,
    /// in their hands.
    /// </summary>
    public static (List<Enemy> Threats, Crewmate Sprayer, Sim.Physics.Bodies Bodies) ExtinguisherHound(List<Enemy> threats, TrainOnLine train, string content)
    {
        threats.RemoveAll(e => e is CinderHound);
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
        var player = DataFile.Load<Sim.Player.PlayerTuning>(Path.Combine(content, Sim.Player.PlayerTuning.File));
        int car = Math.Min(3, train.Frames.Count - 1);
        var shape = train.Frames[car].Shape;
        // The sprayer up the car (toward the engine), facing back down it at the hound.
        var s = Sim.Player.PlayerMotor.SpawnOnRoof(train, car, -2.2, player, -0.1) with { Yaw = Math.PI, Pitch = -0.25 };
        var hound = new CinderHound(SprayedHoundId, 1);
        // Backing off down the roof (HoundMode.Patrol, facing +Z: away), snarling at it (the telegraph).
        hound.Restore(SpinePhase.Telegraph, 0.8, 3, car, new Double3(0.25, shape.RoofHeight, 0.4), 0, (int)HoundMode.Patrol * 4 + 1, 0.6, 1, 0);
        threats.Add(hound);
        var bodies = new Sim.Physics.Bodies();
        var ext = bodies.SpawnCrate(train, car, s.Position + Double3.Up * 1.1, Sim.Physics.BodyKind.Extinguisher);
        ext.Carrier = SprayerId;
        ext.Charge = 0.7;
        for (int i = 0; i < 30; i++)
            bodies.Step(train, tuning, id => id == SprayerId ? s : null);
        var sprayer = new Crewmate(SprayerId, Sim.Player.PlayerMotor.WorldPosition(s, train), Sim.Player.PlayerMotor.WorldYaw(s, train), true,
            Act: Art.CrewPose.Extinguish, Car: car, Local: s.Position);
        return (threats, sprayer, bodies);
    }

    /// <summary>The extinguisher view's eye: off the car's left, up at roof height, side on to the jet between them.</summary>
    public static Camera ExtinguisherHoundCamera(TrainOnLine train)
    {
        int car = Math.Min(3, train.Frames.Count - 1);
        var f = train.Frames[car];
        double roof = f.Shape.RoofHeight;
        return Camera.LookAt(f.ToWorld(new Double3(-(f.Shape.HalfWidth + 4.2), roof + 1.4, 0.6)), f.ToWorld(new Double3(0, roof + 0.7, -0.9)), 55);
    }
}
