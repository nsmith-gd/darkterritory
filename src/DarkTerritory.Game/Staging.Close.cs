using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>The Close staged for a look (note 1013).</summary>
public static partial class Staging
{
    public const int CloseId = 1013;

    /// <summary>The second car with a room (the first behind the crew's), or the first.</summary>
    static int CloseCar(TrainOnLine train)
    {
        var rooms = train.Dynamics.Consist.Vehicles.Where(v => v.Id > 0 && train.Frames[v.Id].Shape.Interior is not null).Select(v => v.Id).ToList();
        return rooms.Count > 1 ? rooms[1] : rooms.Count > 0 ? rooms[0] : -1;
    }

    /// <summary>
    /// The staged Close (<c>dt screenshot --close bow|pour|pin|burst</c>; note 1013) lying along its car's roof: <c>bow</c>
    /// slumped and pressing the boards in (the close and closeroof views'), <c>pour</c> through the hatch, <c>pin</c> filling
    /// the room, <c>burst</c> sagged off the side.
    /// </summary>
    public static List<Enemy> Close(List<Enemy> threats, TrainOnLine train, string mode)
    {
        threats.RemoveAll(e => e is Close);
        int car = CloseCar(train);
        if (car <= 0)
            return threats;
        var shape = train.Frames[car].Shape;
        var m = mode switch { "pour" => CloseMode.Pour, "pin" => CloseMode.Pin, "burst" => CloseMode.Burst, _ => CloseMode.Bow };
        var phase = m switch { CloseMode.Pour => SpinePhase.Commit, CloseMode.Pin => SpinePhase.Grab, CloseMode.Burst => SpinePhase.BreakOff, _ => SpinePhase.Telegraph };
        var c = new Close(CloseId);
        // Seconds in its mode: the bow at its deepest breath (1.5 s of its 3), the pour most of the way down.
        c.Restore(phase, 1.5, 3, car, new Double3(0, shape.RoofHeight, shape.Hatch?.Centre.Z ?? 0), train.Dynamics.Distance, 0, (double)m,
            shape.HalfLength, m == CloseMode.Pour ? 3 : 1.5);
        threats.Add(c);
        return threats;
    }

    /// <summary>The <c>close</c> view: off the car's side and above, along the sack on its roof; <c>closeroof</c>: from the room's floor, up at the bowed boards.</summary>
    public static Camera CloseCamera(TrainOnLine train, string view)
    {
        int car = CloseCar(train);
        var f = train.Frames[Math.Clamp(car, 0, train.Frames.Count - 1)];
        double roof = f.Shape.RoofHeight, half = f.Shape.HalfLength;
        if (view == "closeroof")
        {
            double floor = f.Shape.Interior?.Min.Y ?? 1.1;
            return Camera.LookAt(f.ToWorld(new Double3(0.6, floor + 1.4, half - 1.2)), f.ToWorld(new Double3(-0.1, roof - 0.5, -half * 0.3)), 75);
        }
        return Camera.LookAt(f.ToWorld(new Double3(f.Shape.HalfWidth + 5.5, roof + 3.2, half + 4)), f.ToWorld(new Double3(0, roof + 0.2, 0)), 50);
    }
}
