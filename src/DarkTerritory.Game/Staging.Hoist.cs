using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game;

public static partial class Staging
{
    /// <summary>
    /// A friend on the crane's hook (note 726; dt screenshot --view hoist): the hook over open ground between the track and the
    /// stack, <paramref name="hook"/> m up, empty but for a crewmate stood on it holding its cable, counted as its load (the
    /// lamp hangs over them); and a crewmate on the ground under it, waving up.
    /// </summary>
    public static List<Crewmate> Hoist(Crane crane, double hook)
    {
        var t = crane.Tuning;
        foreach (var c in crane.Castings)
            if (c.State == CastingState.Hooked)
                c.State = CastingState.Stacked;
        crane.Bridge = t.Along + t.Length * 0.35;
        crane.Trolley = t.Span[1] * 0.55;
        crane.Hook = Math.Min(hook, t.Height - t.Hoist.Headroom);
        crane.Mirror(crane.Bridge, crane.Trolley, crane.Hook, 0, [.. crane.Castings.Select(c => (c.State, c.Car, c.At))], riders: 1);
        var feet = crane.RiderFeet(0, 1);
        var across = ((crane.BridgeEnd(0) - crane.BridgeEnd(1)) with { Y = 0 }).Normalized;
        // Facing the camera on the ground (HoistEye), so it sees their face.
        var look = (HoistEye(crane) - feet) with { Y = 0 };
        double yaw = Math.Atan2(-look.X, -look.Z);
        var rider = Art.CrewActs.OnTheHook(new Crewmate(31, feet, yaw, true), crane.HookAt);
        var ground = crane.HookAt with { Y = crane.HookAt.Y - crane.Hook };
        // On the yard's level ground beside it (the gantry's feet stand on it).
        var below = ground + across * 2.6 + new Double3(across.Z, 0, -across.X) * 1.2;
        var up = (ground - below) with { Y = 0 };
        var waver = new Crewmate(32, below, Math.Atan2(-up.X, -up.Z), true, Emote: Sim.Player.Emote.Wave, EmoteSeconds: 0.7);
        return [rider, waver];
    }

    /// <summary>
    /// The hoist view's eye (note 726): on the ground on the stack's side of the hook and back along the gantry, at a crewmate's
    /// height, clear of the train and the castings.
    /// </summary>
    public static Double3 HoistEye(Crane crane)
    {
        var across = ((crane.BridgeEnd(0) - crane.BridgeEnd(1)) with { Y = 0 }).Normalized;
        var along = new Double3(across.Z, 0, -across.X);
        var ground = crane.HookAt with { Y = crane.HookAt.Y - crane.Hook };
        return ground - across * 1.2 + along * 2.6 + Double3.Up * 1.7;
    }

    /// <summary>The hoist view (note 726): from <see cref="HoistEye"/> up at the friend on the hook.</summary>
    public static Camera HoistCamera(Crane crane) => Camera.LookAt(HoistEye(crane), crane.HookAt + Double3.Up * 0.9, 64);
}
