using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game;

/// <summary>
/// The Hollow House's tell felt (G1's note 585; the director, 9 Oct 2026: "the ground rumbles under it"): in it or near it
/// while it rumbles, slams its doors, sinks and comes down, the eye shakes, hardest inside and at the end. Presentation only,
/// as <see cref="BoilerShake"/>: it moves the eye, never the player, and the settings' CAMERA SHAKE scales it.
/// </summary>
public static class HouseShake
{
    /// <summary>How far from its walls it's still felt (m), fading out to there.</summary>
    const double Felt = 12;

    /// <summary>How hard a Hollow House shakes in each mode (m of the eye's travel, inside).</summary>
    static double Strength(HollowMode mode) => mode switch
    {
        HollowMode.Rumble => 0.006,
        HollowMode.Shut => 0.01,
        HollowMode.Sink => 0.018,
        HollowMode.Collapse => 0.04,
        _ => 0,
    };

    /// <summary>The eye's offset this frame (m) for <paramref name="viewer"/>: the nearest working Hollow House's.</summary>
    public static Double3 Offset(World world, in PlayerState viewer, double seconds)
    {
        if (world.Train.Walls is not { } walls)
            return default;
        double a = 0;
        Double3? at = null;
        foreach (var e in world.ActiveEnemies)
        {
            if (e is not HollowHouse { Gone: false } hollow)
                continue;
            double strength = Strength(hollow.Mode);
            if (strength <= 0)
                continue;
            at ??= PlayerMotor.WorldPosition(viewer, world.Train);
            var p = at.Value;
            int index = (int)hollow.Extra2;
            double k = walls.InHouse(index, p) ? 1 : Math.Max(0, 1 - ((p - hollow.Local) with { Y = 0 }).Length / Felt) * 0.5;
            a = Math.Max(a, strength * k);
        }
        if (a <= 0)
            return default;
        // A low rumble under a faster rattle.
        return new Double3(a * (0.7 * Math.Sin(seconds * 29.3) + 0.3 * Math.Sin(seconds * 61.7 + 0.4)), a * (0.8 * Math.Sin(seconds * 23.9 + 1.7)
            + 0.2 * Math.Sin(seconds * 71.3)), a * 0.6 * Math.Sin(seconds * 37.1 + 2.3));
    }
}
