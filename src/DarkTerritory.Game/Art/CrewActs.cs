using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

/// <summary>
/// What a crewmate is doing with their hands (X1), read off what the snapshot already carries about them: their flags,
/// their surface, their timed action and what's nearest it, what they're carrying, the gun they're at. Presentation only:
/// nothing here is sent or fed back to the sim, so it's free to guess (a frame's lag in an act doesn't matter), and the
/// crew's clips for it are tools/blender/crew_clips.py's (note 145).
/// </summary>
public static class CrewActs
{
    /// <summary>Falling faster than this (m/s down), someone in the air is falling, not at the top of a jump.</summary>
    const double Falling = 3;

    /// <summary>
    /// The crewmate <paramref name="id"/> as they're drawn this frame: where they are and what they're doing. At a gun they're
    /// sat on its seat, facing its way (note 137's cannon: the seat 0.75 m behind the pivot, on the roof under it).
    /// </summary>
    public static Crewmate Crewmate(byte id, in PlayerState s, World world, IReadOnlyList<CarFrame> frames)
    {
        var act = Of(s, id, world);
        var placed = act == CrewPose.Gunner && Guns.Mount(world.Train, s.Parent) is { } gun ? Seated(s, gun) : s;
        var (feet, yaw) = Eyes.World(placed, frames);
        return new Crewmate(id, feet, yaw, s.Alive, s.Hand, s.OtherHand, Act: act, Holding: Sim.Player.Kit.Held(s));
    }

    /// <summary><paramref name="s"/> moved onto <paramref name="gun"/>'s seat (their feet on the roof under it), facing the gun's way.</summary>
    public static PlayerState Seated(in PlayerState s, GunMount gun)
    {
        var seat = gun.Position - gun.Facing * TrainKit.CannonSeat.Z;
        return s with
        {
            Position = seat with { Y = gun.Position.Y + TrainKit.CannonSeat.Y - GunnerPan },
            Yaw = Math.Atan2(-gun.Facing.X, -gun.Facing.Z),
        };
    }

    /// <summary>The seat pan's height over the gunner's feet in crew_clips.py's gunner clip (m).</summary>
    public const double GunnerPan = 0.48;

    /// <summary>What <paramref name="s"/> (player <paramref name="id"/>) is doing, or null for nothing but standing or walking.</summary>
    public static CrewPose? Of(in PlayerState s, int id, World world)
    {
        if (!s.Alive)
            return null;
        var train = world.Train;
        if (s.Has(PlayerFlags.Held))
            return CrewPose.Held;
        if (s.Surface == Surface.Ladder)
            return CrewPose.Climb;
        if (s.Surface == Surface.Air)
            return s.Velocity.Y < -Falling ? CrewPose.Fall : null;
        if (s.Has(PlayerFlags.Pushing))
            return CrewPose.Push;
        if (s.Has(PlayerFlags.Operating))
            return CrewPose.Lever;
        if (world.Bodies.CarriedBy(id) is { } carried)
            return carried.Kind == BodyKind.Ragdoll ? CrewPose.Drag : CrewPose.Carry;
        if (s.Has(PlayerFlags.Shovelful))
            return CrewPose.Shovel;
        if (world.Combat is { } combat && Guns.MannedGun(s, train, combat.Guns) is not null)
            return CrewPose.Gunner;
        if (s.Parent == PlayerState.World || s.Parent >= train.Frames.Count)
            return null;
        var near = CrewActions.Nearest(s, train);
        // Something timed under way (CrewActions: the progress resets the moment it stops).
        if (s.ActionProgress > 0)
            return s.Surface == Surface.Coupler && near != InteractableKind.Door ? CrewPose.Uncouple : near switch
            {
                // (The wrench in hand there is mending a burst boiler, T109.)
                InteractableKind.Firebox when Sim.Player.Kit.Held(s) == Tool.Wrench => CrewPose.Mend,
                InteractableKind.Firebox or InteractableKind.Coal => CrewPose.Shovel,
                InteractableKind.Door => CrewPose.Door,
                InteractableKind.Handbrake => CrewPose.Handbrake,
                InteractableKind.Hatch => CrewPose.Hatch,
                _ => (CrewPose?)null,
            };
        // The held valves keep no progress: the boiler venting, the rail being sanded, by whoever's at it.
        if (s.Parent == 0 && s.Surface == Surface.Deck)
            return near switch
            {
                InteractableKind.Vent when train.Boiler.Venting => CrewPose.Vent,
                InteractableKind.Sandbox when train.Sanding => CrewPose.Lever,
                _ => (CrewPose?)null,
            };
        return null;
    }
}
