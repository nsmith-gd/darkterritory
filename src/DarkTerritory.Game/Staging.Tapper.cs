using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>The Tapper staged for a look (note 1011).</summary>
public static partial class Staging
{
    public const int TapperId = 1011;

    /// <summary>The guard car (the rear gun's), or the last car.</summary>
    static int TapperCar(TrainOnLine train) =>
        train.Vehicles.FirstOrDefault(v => v.Kind == VehicleKind.Guard)?.Id ?? train.Dynamics.Consist.Vehicles[^1].Id;

    /// <summary>
    /// The staged Tapper (<c>dt screenshot --tapper crawl|tap</c>; note 1011) on the rear gun's car roof, behind its seat:
    /// <c>crawl</c> flat, coming along the roof; <c>tap</c> up on its knees with an arm out over where the gunner sits.
    /// </summary>
    public static List<Enemy> Tapper(List<Enemy> threats, TrainOnLine train, string mode)
    {
        threats.RemoveAll(e => e is Tapper);
        int car = TapperCar(train);
        if (car <= 0 || car >= train.Frames.Count || train.Frames[car].Shape.Gun is not { } gun)
            return threats;
        var shape = train.Frames[car].Shape;
        double behind = gun.Facing.Z < 0 ? 1 : -1;
        double z = gun.Position.Z + behind * (mode == "tap" ? 1.7 : 4.5);
        var t = new Tapper(TapperId);
        t.Restore(mode == "tap" ? SpinePhase.Telegraph : SpinePhase.Alert, 1, 3, car, new Double3(0, shape.Bounds.Max.Y, z), train.Dynamics.Distance, behind,
            (double)(mode == "tap" ? TapperMode.Tap : TapperMode.Crawl), -1, 0);
        threats.Add(t);
        return threats;
    }

    /// <summary>The <c>tapper</c> view: from the roof ahead of the rear gun, along it at the one crawling; <c>tappertap</c>: from beside the gun, at the tap.</summary>
    public static Camera TapperCamera(TrainOnLine train, string view)
    {
        int car = TapperCar(train);
        var f = train.Frames[Math.Clamp(car, 0, train.Frames.Count - 1)];
        var gun = f.Shape.Gun ?? new GunMount(Double3.Zero, new Double3(0, 0, 1));
        double behind = gun.Facing.Z < 0 ? 1 : -1;
        double roof = f.Shape.Bounds.Max.Y;
        if (view == "tappertap")
            return Camera.LookAt(f.ToWorld(new Double3(f.Shape.HalfWidth + 2.2, roof + 1.6, gun.Position.Z + behind * 0.4)),
                f.ToWorld(new Double3(0, roof + 0.8, gun.Position.Z + behind * 1.2)), 55);
        return Camera.LookAt(f.ToWorld(new Double3(1.2, roof + 2.4, gun.Position.Z + behind * 9.5)),
            f.ToWorld(new Double3(0, roof + 0.3, gun.Position.Z + behind * 3)), 55);
    }
}
