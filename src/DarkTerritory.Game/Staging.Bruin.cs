using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>The Bruin staged for a look (note 1007).</summary>
public static partial class Staging
{
    public const int BruinId = 1007;

    /// <summary>The car the staged Bruin is after (the second behind the engine).</summary>
    const int BruinCar = 2;

    /// <summary>
    /// The staged Bruin (<c>dt screenshot --bruin out|car</c>; note 1007) at the second car: <c>out</c> one come out of the trees
    /// on the car's right, on all fours, huffing its way to the door; <c>car</c> one in the car, its doors open, risen over
    /// whoever's at the door on its right.
    /// </summary>
    public static List<Enemy> Bruin(List<Enemy> threats, TrainOnLine train, string mode)
    {
        threats.RemoveAll(e => e is Bruin);
        if (train.Frames.Count <= BruinCar)
            return threats;
        var f = train.Frames[BruinCar];
        var b = new Bruin(BruinId);
        if (mode == "car")
        {
            train.Vehicles[BruinCar].DoorsOpen = 0xFF;
            double yaw = Math.Atan2(-f.Right.X, -f.Right.Z);
            b.Restore(SpinePhase.Commit, 2, 20, BruinCar, new Double3(0, f.Shape.Interior?.Min.Y ?? 1, 0), train.Dynamics.Distance, yaw,
                (double)BruinMode.Rise, -1, BruinCar);
        }
        else
        {
            var at = OnGround(train, f.ToWorld(new Double3(f.Shape.HalfWidth + 9, 0, 1.5)));
            var door = f.ToWorld(new Double3(f.Shape.HalfWidth, 0, 0));
            var way = (door - at) with { Y = 0 };
            b.Restore(SpinePhase.Telegraph, 3, 20, Enemy.Loose, at, train.Dynamics.Distance, Math.Atan2(-way.X, -way.Z),
                (double)BruinMode.Come, -1, BruinCar);
        }
        threats.Add(b);
        return threats;
    }

    /// <summary>The <c>bruin</c> view: from ahead along the car's right side, out at the one coming; <c>bruincar</c>: from its door, in.</summary>
    public static Camera BruinCamera(TrainOnLine train, string view)
    {
        var f = train.Frames[Math.Min(BruinCar, train.Frames.Count - 1)];
        if (view == "bruincar")
            return Camera.LookAt(OnGround(train, f.ToWorld(new Double3(f.Shape.HalfWidth + 3.2, 0, 0.6))) + Double3.Up * 1.6,
                f.ToWorld(new Double3(0, 1.6, 0)), 60);
        return Camera.LookAt(OnGround(train, f.ToWorld(new Double3(f.Shape.HalfWidth + 2.5, 0, -9))) + Double3.Up * 1.7,
            OnGround(train, f.ToWorld(new Double3(f.Shape.HalfWidth + 7, 0, 1))) + Double3.Up * 0.9, 56);
    }
}
