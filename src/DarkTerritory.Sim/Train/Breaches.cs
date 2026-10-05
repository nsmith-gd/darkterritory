using Ballast;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Train;

/// <summary>
/// A car's shell given way to the outside (decided 1 Oct, checklist state-breach; spec B.9): the Car Hugger chewing through
/// its end wall, Climbers forcing their way in through its roof. A breached car shuts nobody in, whatever its doors
/// (<see cref="PlayerMotor.Space"/>): the cold, the night's sound and the Choir come in as through an open door, until
/// someone boards it up from inside (<see cref="CrewActions.Apply"/>: Use held at the hole, train.json <c>breach</c>).
/// The host breaks it open; a client predicts its own hands boarding it, like any door.
/// </summary>
public static class Breaches
{
    /// <summary>The end wall the Car Hugger eats through (car frame): the middle of the room's rear wall, a metre up.</summary>
    public static Double3? EndWall(CarShape shape) =>
        shape.Interior is { } room ? new Double3(0, room.Min.Y + 1.0, room.Max.Z) : null;

    /// <summary>
    /// Where Climbers come in through the roof (car frame): its hatch, torn off, on a cargo car that has one; otherwise the
    /// roof over the middle of the room, where they drop in.
    /// </summary>
    public static Double3? Roof(CarShape shape) =>
        shape.Interior is { } room ? new Double3(0, room.Max.Y, shape.Hatch is { } hatch ? hatch.Centre.Z : room.Centre.Z) : null;

    /// <summary>
    /// The breached car this player can board up now, if any: inside it on its floor, within <see cref="BreachTuning.BoardReach"/>
    /// of the hole across the floor (a headset's reaching hand, T29, measured from the hand), and carrying the repair kit if
    /// boarding needs it (<see cref="BreachTuning.NeedsKit"/>; note 150).
    /// </summary>
    public static int? Within(in PlayerState s, TrainOnLine train, HandTuning? hand = null)
    {
        if (!s.Alive || s.Parent <= 0 || s.Parent >= train.Frames.Count || !train.Vehicles[s.Parent].Breached || !PlayerMotor.Indoors(s, train))
            return null;
        var t = train.Dynamics.Tuning.Breach;
        if (t.NeedsKit && !s.Has(PlayerFlags.RepairKit))
            return null;
        var from = (hand is not null && s.Hand != default ? PlayerMotor.HandAt(s) : null) ?? s.Position;
        var hole = train.Vehicles[s.Parent].BreachAt;
        double dx = from.X - hole.X, dz = from.Z - hole.Z;
        return dx * dx + dz * dz <= t.BoardReach * t.BoardReach ? s.Parent : null;
    }

    /// <summary>
    /// Where to stand on a breached car's floor to board it up (car frame): in the aisle, in line with the hole, clear of the
    /// end wall. For a bot's legs; a player just walks up to it.
    /// </summary>
    public static Double3 StandAt(TrainOnLine train, int car)
    {
        var shape = train.Frames[car].Shape;
        var hole = train.Vehicles[car].BreachAt;
        double aisle = train.Dynamics.Tuning.Geometry.Interior?.DoorX ?? 0;
        if (shape.Interior is not { } room)
            return hole with { Y = 0 };
        return new Double3(aisle, room.Min.Y, Math.Clamp(hole.Z, room.Min.Z + 0.7, room.Max.Z - 0.7));
    }
}
