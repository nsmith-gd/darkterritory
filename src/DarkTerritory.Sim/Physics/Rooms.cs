using Ballast;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Physics;

/// <summary>
/// A walk-in car's room as the loose bodies see it (ARCHITECTURE §8 note 172): what's inside stays inside unless it goes
/// out through an opening (an open door, an open roof hatch). Its walls are a tenth of a metre thick, thinner than a thrown
/// lamp travels in a tick and thinner than a hand reaches past them, so the solids alone don't hold things in: a sphere
/// whose centre ends up past a wall's middle is pushed out of its far side.
/// </summary>
public static class Rooms
{
    /// <summary>The room a thing of <paramref name="radius"/> can be in: its walls and roof brought in by the radius (the floor is a solid).</summary>
    public static Box Inner(Box room, double radius) =>
        new(new Double3(room.Min.X + radius, room.Min.Y, room.Min.Z + radius), new Double3(room.Max.X - radius, room.Max.Y - radius, room.Max.Z - radius));

    public static Double3 Clamp(Box b, Double3 p) =>
        new(Math.Clamp(p.X, b.Min.X, b.Max.X), Math.Clamp(p.Y, b.Min.Y, b.Max.Y), Math.Clamp(p.Z, b.Min.Z, b.Max.Z));

    static double Axis(Double3 p, int a) => a == 0 ? p.X : a == 1 ? p.Y : p.Z;

    /// <summary>
    /// Whether going from <paramref name="from"/> (in the room) to <paramref name="to"/> (past its walls) is out through an
    /// opening: where the way crosses the room's side, an open door's in that wall there, or the open hatch overhead.
    /// </summary>
    public static bool ThroughOpening(CarShape shape, Vehicle vehicle, Box room, Double3 from, Double3 to)
    {
        double first = double.MaxValue;
        int axis = -1;
        for (int a = 0; a < 3; a++)
        {
            double f = Axis(from, a), t = Axis(to, a), lo = Axis(room.Min, a), hi = Axis(room.Max, a);
            double u = t > hi && t > f ? (hi - f) / (t - f) : t < lo && t < f ? (lo - f) / (t - f) : double.MaxValue;
            if (u < first)
                (first, axis) = (u, a);
        }
        if (axis < 0)
            return true;
        var at = from + (to - from) * Math.Clamp(first, 0, 1);
        const double Margin = 0.02;
        foreach (var door in shape.DoorList)
        {
            if (!vehicle.DoorOpen(door.Index))
                continue;
            var b = door.Box;
            bool inside = true;
            for (int a = 0; a < 3 && inside; a++)
                inside = a == axis
                    ? Axis(at, a) >= Axis(b.Min, a) - 0.3 && Axis(at, a) <= Axis(b.Max, a) + 0.3
                    : Axis(at, a) >= Axis(b.Min, a) - Margin && Axis(at, a) <= Axis(b.Max, a) + Margin;
            if (inside)
                return true;
        }
        return axis == 1 && to.Y > room.Max.Y && shape.Hatch is { } hatch && vehicle.DoorOpen(CarShape.HatchBit) && hatch.ContainsXZ(at);
    }

    /// <summary>
    /// Where a thing of <paramref name="radius"/> going from <paramref name="from"/> to <paramref name="to"/> in a car's frame
    /// ends up: <paramref name="to"/>, unless that's out through a wall of the room it was in, when it's stopped inside
    /// the wall. Null when it goes where it was going.
    /// </summary>
    /// <summary>How far clear of a wall a stopped thing is left.</summary>
    const double Clear = 0.02;

    public static Double3? Held(CarShape shape, Vehicle vehicle, Double3 from, Double3 to, double radius)
    {
        if (shape.Interior is not { } room || !room.Contains(from))
            return null;
        // A hair clear of the wall, too: touching it, the contact's friction would hold it up there off the floor.
        var inner = Inner(room, radius + Clear);
        if (inner.Contains(to) || to.Y < inner.Min.Y && inner.ContainsXZ(to) || ThroughOpening(shape, vehicle, room, from, to))
            return null;
        return Clamp(inner, to);
    }
}
