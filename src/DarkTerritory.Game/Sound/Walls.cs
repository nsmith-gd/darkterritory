using Ballast;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Sound;

/// <summary>The walls' numbers (<c>content/audio/walls.json</c>; field docs there).</summary>
public sealed record WallsTuning(double OpenWall = 0.6, double OpeningMargin = 0.15, double BreachRadius = 0.9, double ShedWall = 0.35,
    double RoomWall = 0.8)
{
    public const string File = "audio/walls.json";
}

/// <summary>
/// The train's walls between an ear and a sound (spec A.5 "car walls −12 dB and lowpass at 900 Hz", A.7 "occlusion via
/// raycast against car geometry"; note 248). The line from one to the other is tested against the interior of each car one
/// of them is inside, in the car's own frame: the face it crosses is a wall, unless it crosses where a door, the roof hatch
/// or a breach is open. A car with something open in it leaks: its walls cost <see cref="WallsTuning.OpenWall"/>, not a whole
/// wall, the sound coming round by the opening. The engine's cab is open-sided and isn't a room here; the spaces have it.
/// </summary>
public static class Walls
{
    /// <summary>How occluded <paramref name="at"/> is from <paramref name="ear"/> by the walls: 0 (nothing between) to 1 (a wall or more).</summary>
    public static float Between(TrainOnLine train, Double3 ear, Double3 at, WallsTuning t)
    {
        double total = 0;
        for (int i = 1; i < train.Frames.Count && total < 1; i++)
        {
            var frame = train.Frames[i];
            if (frame.Shape.Interior is not { } room)
                continue;
            Double3 a = frame.ToLocal(ear), b = frame.ToLocal(at);
            if (!Crosses(room, a, b, out double enter, out double exit))
                continue;
            bool aIn = room.Contains(a), bIn = room.Contains(b);
            // Its walls are between them only with one of them inside it. A car in the way out in the open isn't a wall
            // to them: the sound goes round it, over the roof and under the floor.
            if (aIn == bIn)
                continue;
            var vehicle = train.Vehicles[i];
            bool open = vehicle.DoorsOpen != 0 || vehicle.Breached;
            double wall = open ? t.OpenWall : 1;
            // Out of the room where the ear is, or into it where the sound is: one face, unless it's open there.
            double crossing = aIn ? exit : enter;
            if (!Through(frame.Shape, vehicle, a + (b - a) * crossing, t))
                total += wall;
        }
        return (float)Math.Min(1, total);
    }

    /// <summary>Whether a point on the room's face is in one of its openings: an open door, the open hatch, the breach.</summary>
    static bool Through(CarShape shape, Vehicle vehicle, Double3 p, WallsTuning t)
    {
        var margin = new Double3(t.OpeningMargin, t.OpeningMargin, t.OpeningMargin);
        foreach (var door in shape.DoorList)
            if (vehicle.DoorOpen(door.Index) && new Box(door.Box.Min - margin, door.Box.Max + margin).Contains(p))
                return true;
        if (shape.Hatch is { } hatch && vehicle.DoorOpen(CarShape.HatchBit) && new Box(hatch.Min - margin, hatch.Max + margin).Contains(p))
            return true;
        return vehicle.Breached && (p - vehicle.BreachAt).Length <= t.BreachRadius;
    }

    /// <summary>
    /// The segment a→b against a box (slabs): the fractions along it where it goes in and comes out, clamped to the
    /// segment. False when it misses.
    /// </summary>
    static bool Crosses(Box box, Double3 a, Double3 b, out double enter, out double exit)
    {
        enter = 0;
        exit = 1;
        var d = b - a;
        for (int axis = 0; axis < 3; axis++)
        {
            double o = axis == 0 ? a.X : axis == 1 ? a.Y : a.Z, v = axis == 0 ? d.X : axis == 1 ? d.Y : d.Z;
            double lo = axis == 0 ? box.Min.X : axis == 1 ? box.Min.Y : box.Min.Z, hi = axis == 0 ? box.Max.X : axis == 1 ? box.Max.Y : box.Max.Z;
            if (Math.Abs(v) < 1e-12)
            {
                if (o < lo || o > hi)
                    return false;
                continue;
            }
            double t0 = (lo - o) / v, t1 = (hi - o) / v;
            if (t0 > t1)
                (t0, t1) = (t1, t0);
            enter = Math.Max(enter, t0);
            exit = Math.Min(exit, t1);
            if (enter > exit)
                return false;
        }
        return true;
    }
}
