using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Train;

/// <summary>
/// The crew lockers (ARCHITECTURE §8 note 166): a row of iron lockers along a wall of the repair kit's car, each lettered
/// with a crew grade (train.json <c>kit.lockers</c>). The shape has them (<see cref="CarShape.Lockers"/>), the vehicle their
/// doors (<see cref="Vehicle.LockersOpen"/>), and a body on a shelf says which (<see cref="Body.Locker"/>): held there, out
/// of the physics, so nothing stowed falls, slides or goes through a wall.
/// <para>
/// Worked with Use, as everything is: held at a locker, its door opens or shuts (<see cref="CrewActions"/>, the same on a
/// predicting client); tapped, what's in your hands goes on its first free shelf, or the top thing comes off its shelves
/// into them (<see cref="Bodies.Handle"/>, the host's).
/// </para>
/// </summary>
public static class Lockers
{
    public static LockerTuning? Tuning(TrainOnLine train) => train.Dynamics.Tuning.Kit.Lockers;

    /// <summary>Shelves in each locker.</summary>
    public static int Slots(TrainOnLine train) => Math.Max(0, Tuning(train)?.Slots ?? 0);

    /// <summary>Whether a thing goes in a locker: hand-sized (train.json <c>kit.lockers.holds</c>).</summary>
    public static bool Holds(TrainOnLine train, BodyKind kind) => Tuning(train)?.Holds.Contains(kind) == true;

    /// <summary>How long Use is held at one to open or shut it; a shorter press is a tap.</summary>
    public static double DoorSeconds(TrainOnLine train) => Tuning(train)?.DoorSeconds ?? 0.4;

    /// <summary>
    /// Where a thing of <paramref name="radius"/> sits on shelf <paramref name="slot"/> of a locker (its car's frame): the
    /// locker's floor, then evenly up to a hand under its top; in the middle of the shelf.
    /// </summary>
    public static Double3 SlotAt(LockerBay bay, int slot, int slots, double radius)
    {
        double inner = bay.Box.Max.Y - bay.Box.Min.Y - 0.25;
        double shelf = bay.Box.Min.Y + 0.06 + slot * inner / Math.Max(1, slots);
        var c = bay.Box.Centre;
        return new Double3(c.X, shelf + radius, c.Z);
    }

    /// <summary>The locker of a name in a car's shape (case aside), or null.</summary>
    public static LockerBay? Named(CarShape shape, string name)
    {
        foreach (var bay in shape.Lockers)
            if (string.Equals(bay.Name, name, StringComparison.OrdinalIgnoreCase))
                return bay;
        return null;
    }

    /// <summary>The locker whose door a player's at (their own car's, facing it; a reaching hand's), or null.</summary>
    public static (int Car, LockerBay Bay)? AtHand(in PlayerState s, TrainOnLine train, HandTuning? hand = null)
    {
        if (CrewActions.NearestInteractable(s, train, hand) is not { Thing.Kind: InteractableKind.Locker } near)
            return null;
        var shape = train.Frames[near.Vehicle].Shape;
        int i = near.Thing.Index;
        return i >= 0 && i < shape.Lockers.Count ? (near.Vehicle, shape.Lockers[i]) : null;
    }

    /// <summary>What's on a locker's shelves, bottom shelf first.</summary>
    public static IEnumerable<Body> Contents(Bodies bodies, int car, int locker) =>
        bodies.All.Where(b => b.Locker == locker && b.Parent == car).OrderBy(b => b.Slot);

    /// <summary>A locker's first empty shelf, or −1 when it's full.</summary>
    public static int FreeSlot(Bodies bodies, TrainOnLine train, int car, int locker)
    {
        int slots = Slots(train);
        for (int k = 0; k < slots; k++)
            if (!bodies.All.Any(b => b.Locker == locker && b.Parent == car && b.Slot == k))
                return k;
        return -1;
    }
}
